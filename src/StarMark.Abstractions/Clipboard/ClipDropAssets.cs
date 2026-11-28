#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace StarMark.Abstractions.Clipboard;

/// <summary>
/// 拖进剪贴板格的<b>虚拟文件</b>（只有流没有盘上地址的那一类）物化之后的"那一套"——
/// 目录、命名、名字名册：全部纯函数（✅P-307 第二刀・乙）。
/// <para>
/// 与 <see cref="ClipAssets"/> 同一范式、同一理由：这个目录（<c>clip-drops</c>）与 <c>clip</c> 一样
/// 躺在 <c>%LOCALAPPDATA%\StarMark</c> 里对用户可见，名字会被手删、手改、被备份带走再拼回来；
/// 命名字典与校验只许有一份，写侧（Integrations 的 <c>ClipboardDropFileStore</c>）、
/// 删侧（Data 的轮转/清空/删单条）、物化侧（UI 的分流腿）都问它。
/// </para>
/// <para>
/// 命名是<b>确定性</b>的：<c>&lt;内容哈希前 12 位&gt;_&lt;洗过的原始名&gt;</c>。同内容再拖＝同一个名字、
/// 同一个 <c>source_id</c> ⇒ 合并回放，不攒孤儿；哈希在前、名字在后，资源管理器里同内容的件挨着排，
/// 用户手翻也读得懂这是"从哪来的那一件"。
/// </para>
/// <para>IO（真正落盘、删、stat）留在调用方：这里的每个函数都只吃字符串与字节，能逐值断言。</para>
/// </summary>
public static class ClipDropAssets
{
    /// <summary>目录名，挂在 <c>%LOCALAPPDATA%\StarMark\</c> 下（与 clip/logs/sdk/downloads 同一根）。</summary>
    public const string FolderName = "clip-drops";

    /// <summary>文件名里带的内容哈希位数（12 位小写十六进制＝48 比特，同批拖放里撞名的概率可忽略）。</summary>
    public const int HashPrefixChars = 12;

    /// <summary>原始名洗完后最多留几位（拖源给的名字可以长到离谱，名字是给人看的，不是给拖源的）。</summary>
    public const int MaxOriginalNameChars = 80;

    /// <summary>洗完之后一个字符都不剩时的兜底名（拖源可以给空名——文件总得有个可读的名字）。</summary>
    public const string FallbackOriginalName = "dropped";

    /// <summary>整个文件名（含哈希段与下划线）的长度上限。库里读回来的名字超了它＝不安全，不拼路径。</summary>
    public const int MaxFileNameChars = 120;

    /// <summary>目录的绝对路径（<paramref name="storageRoot"/>＝<see cref="ClipAssets.DefaultStorageRoot"/> 那棵根）。</summary>
    public static string DirectoryFor(string storageRoot) => Path.Combine(storageRoot, FolderName);

    /// <summary>
    /// 测试/诊断用的目录改道。<b>写侧（Integrations）与删侧（Data 的轮转/清空/删单条）必须读同一个字段</b>：
    /// 两边各留一份 override 时，测试里改了写侧、删侧照旧指向用户目录，症状是"删除路径永远删不到东西，
    /// 而用户的 clip-drops 目录里悄悄长出测试件"——与 <see cref="ClipAssets.FolderOverride"/> 同一条教训。
    /// </summary>
    public static string? FolderOverride;

    /// <summary>目录的绝对路径。凡是拼 clip-drops 路径的地方都问它，不许自己 Combine 一遍。</summary>
    public static string Folder => FolderOverride ?? DirectoryFor(ClipAssets.DefaultStorageRoot);

    /// <summary>
    /// 内容 → 文件名的哈希段（前 <see cref="HashPrefixChars"/> 位小写十六进制）。
    /// <para>SHA-256 而不是整串十六进制：<b>这是给人看的区分段，不是完整性承诺</b>——
    /// 真正的"同一件"判据是它拼出来的路径（同内容＝同路径＝回放合并）。十二位足够让
    /// 一次拖放里的几十件互不撞名，再长只是让文件名更难读。</para>
    /// </summary>
    public static string HashOf(byte[] content)
        => Convert.ToHexString(SHA256.HashData(content), 0, HashPrefixChars / 2).ToLowerInvariant();

    /// <summary>
    /// 拖源给的原始名 → 可以放进文件名的那一段。
    /// <para>
    /// 只取文件名段（拖源带路径段的名字落到目录外面就是脏手）；
    /// Windows 不许的字符一律换成 <c>_</c>（不丢内容：用户还要靠这段认出"这是我拖进来的哪件"）；
    /// <b>连续点折叠</b>（<c>a..b</c> → <c>a.b</c>）——不折的话洗出来的名字带着 <c>..</c>，
    /// 自家 <see cref="IsSafeFileName"/> 会拒它，于是同一件拖两次：第一次成、第二次永远失败，
    /// 而两边的日志都说不出为什么。
    /// 结尾的空格与点折掉（Windows 会静默剥它们，"写下去的"与"读出来的"就不是同一个名字了）；
    /// 洗完还剩空串就落 <see cref="FallbackOriginalName"/>。
    /// </para>
    /// </summary>
    public static string SanitizeOriginalName(string? originalName)
    {
        var raw = Path.GetFileName((originalName ?? string.Empty).Trim());
        var sb = new StringBuilder(raw.Length);
        var lastWasDot = false;
        foreach (var c in raw)
        {
            if (IsBadNameChar(c)) { sb.Append('_'); lastWasDot = false; continue; }
            if (c == '.')
            {
                if (lastWasDot) continue;        // 连点折叠：见上——防自家名册拒 ".."
                lastWasDot = true;
            }
            else lastWasDot = false;
            sb.Append(c);
        }
        var name = sb.ToString().TrimEnd(' ', '.');
        if (name.Length > MaxOriginalNameChars) name = name[..MaxOriginalNameChars].TrimEnd(' ', '.');
        return name.Length == 0 ? FallbackOriginalName : name;
    }

    /// <summary>
    /// 哈希段 + 洗过的原始名 → 文件名：<c>9f2a3b8c4d5e_报告.pdf</c>。
    /// <para>哈希在前：同内容的件在目录里挨着排；下划线是两段的界碑，名册按它切。</para>
    /// </summary>
    public static string FileNameOf(string hash12, string? originalName)
        => hash12 + "_" + SanitizeOriginalName(originalName);

    /// <summary>
    /// 写盘用的临时名（半写残由构造消灭——先写 <c>*.tmp</c> 再改名）。
    /// 与 <see cref="ClipAssets.TempNameOf"/> 同一口径：带 <c>.tmp</c> 是为了让对账一眼认得出
    /// "这是我们自己没改完的残货"。
    /// </summary>
    public static string TempNameOf(string finalName) => finalName + ".tmp";

    /// <summary>
    /// 一个<b>从库里读出来</b>的文件名能不能安全地拼到 clip-drops 目录下。
    /// <para>
    /// 我们自己写出去的名字一定过这一关，但认领名单（<c>clipDropFiles</c>）存在 <c>extra_json</c> 里，
    /// 而备份文件是用户可以手改的文本：不校验就拼路径，<c>..\</c> 会把删除与写入都带到目录之外。
    /// 判据比 <see cref="ClipAssets.IsSafeFileName"/> 更严：<b>前 13 位必须是"12 位小写十六进制＋下划线"</b>
    /// ——我们写出去的名字没有第二种形状，长成别的样子的一律不是我们的件，宁可少删、不可乱删。
    /// </para>
    /// </summary>
    public static bool IsSafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxFileNameChars) return false;
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) name = name[..^".tmp".Length];
        if (name.Length <= HashPrefixChars) return false;               // 连哈希段都不够长
        if (name[HashPrefixChars] != '_') return false;
        for (var i = 0; i < HashPrefixChars; i++)
            if (!IsLowerHex(name[i])) return false;
        foreach (var c in name[(HashPrefixChars + 1)..])
            if (IsBadNameChar(c)) return false;
        if (name.Contains("..", StringComparison.Ordinal)) return false;
        if (name.StartsWith(' ') || name.EndsWith('.')) return false;   // Windows 会静默剥它们
        return true;
    }

    /// <summary>
    /// 库里读出来的文件名 → 绝对路径；<b>名字不安全就返回 null</b>（调用方据此"什么都不做"）。
    /// 删除与读取都走这一句：校验只写在写盘那侧的话，<c>..\</c> 这种从手改过的备份里回来的名字
    /// 就只在写的时候被拦住，删的时候照样被拼进路径。
    /// </summary>
    public static string? FullPathOf(string? name)
        => IsSafeFileName(name) ? Path.Combine(Folder, name!) : null;

    private static bool IsLowerHex(char c)
        => c is >= '0' and <= '9' or >= 'a' and <= 'f';

    /// <summary>Windows 文件名不许出现的字符（含控制符）。洗名与名册共用同一份——两处各写一遍就会分出两种"安全"。</summary>
    private static bool IsBadNameChar(char c)
        => c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(c);
}
