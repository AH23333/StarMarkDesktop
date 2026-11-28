#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using StarMark.Abstractions.I18n;

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

    // ==================== 占用与对账（片292・照 clip-images house pattern 配齐四件） ====================

    /// <summary>
    /// 占用统计的口径：<b>一族一种件</b>（没有主图/缩略图那对儿，所以只有件数与字节两项）。
    /// 与 <see cref="ClipAssets.Footprint"/> 同一颗职责：设置页那句"占了多少"只有这一份算法。
    /// </summary>
    public readonly record struct DropFootprint(int FileCount, long FileBytes);

    /// <summary>
    /// 目录内容 → <see cref="DropFootprint"/>。<paramref name="files"/> 由调用方枚举（这里不碰文件系统，
    /// 才可能在单测里逐值钉住分派规则）；含 <c>.tmp</c> 的按临时件计入 <paramref name="tempBytes"/>/<paramref name="tempCount"/>，
    /// 不算正式件。临时件<b>件数与字节分开交</b>：那句话要说"另有 N 个（约 X）"，两个数各是各的量纲。
    /// </summary>
    public static DropFootprint Summarize(IEnumerable<(string Name, long Bytes)> files,
        out long tempBytes, out int tempCount)
    {
        var count = 0; var bytes = (long)0; var tmp = (long)0; var tmpCount = 0;
        foreach (var (name, size) in files)
        {
            if (IsTempName(name)) { tmp += size; tmpCount++; continue; }
            count++;
            bytes += size;
        }
        tempBytes = tmp;
        tempCount = tmpCount;
        return new DropFootprint(count, bytes);
    }

    /// <summary>写盘用的临时名判据（对账只按这一条认"这是我们自己的半件"）。名册口径见 <see cref="IsSafeFileName"/>。</summary>
    public static bool IsTempName(string fileName) => fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 对账结果。<b>三类全是"只数"的事实，零类动数据库、零类动文件</b>（片292・分叉一甲）：
    /// <list type="bullet">
    /// <item><description><c>MissingNames</c>：行认领了、件不在（用户手删 / 旧备份恢复过来）。<b>不打标记</b>——
    /// drops 的认领是"一行认 N 件"，行级缺失标记语义不清（一行三件缺一件，标谁？），而且没有 UI 读那颗标记；
    /// 无状态＝天然幂等，用户把件放回来，这一类自动归零（§3-Q6 第三类反向免费得到）。</description></item>
    /// <item><description><c>OrphanNames</c>：件在、无人认领（删行时文件删失败 / 用户自己拷进来）⇒ 只数不删，
    /// 出口在设置页确认框后的 <c>CleanAsync</c>。</description></item>
    /// <item><description><c>TempNames</c>：临时件（<c>*.tmp</c>）——出现即说明有一次物化没写完。</description></item>
    /// </list>
    /// </summary>
    public readonly record struct ReconcileDropResult(
        IReadOnlyList<string> MissingNames,
        IReadOnlyList<string> OrphanNames,
        IReadOnlyList<string> TempNames)
    {
        public static readonly ReconcileDropResult Empty = new(
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        public bool NothingToDo => MissingNames.Count == 0 && OrphanNames.Count == 0 && TempNames.Count == 0;
    }

    /// <summary>
    /// 把"库里的认领名单"与"目录里的文件"对一次账。<paramref name="rowClaims"/> 一项＝一行的认领名单
    /// （<see cref="ClipboardEntry.DropFileNamesOf"/>；空行＝那行什么都没认领，跳过）。
    /// <para><b>比对一律 OrdinalIgnoreCase</b>：NTFS 大小写不敏感（与 <see cref="ClipAssets.Reconcile"/> 同一条理由）。</para>
    /// <para>同一个名字被多行认领、或一行名单里出现两次，都只算<b>一件</b>：对账数的是"磁盘上有几个名字对不上"，
    /// 不是"名单里出现几次"。</para>
    /// </summary>
    public static ReconcileDropResult Reconcile(
        IEnumerable<IReadOnlyList<string>> rowClaims, IEnumerable<string> fileNames)
    {
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var seenMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claims in rowClaims)
        {
            if (claims is null) continue;
            foreach (var name in claims)
            {
                if (string.IsNullOrWhiteSpace(name) || !claimed.Add(name)) continue;
                missing.Add(name);                 // 先按"认领了就未必在"收着，下面再按盘上事实分箱
            }
        }

        var orphans = new List<string>();
        var temps = new List<string>();
        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in fileNames)
        {
            onDisk.Add(name);
            if (IsTempName(name)) temps.Add(name);
        }

        // 分箱第二遍：认领的名字真在盘上＝不是缺失。两遍而不是一遍，是因为"盘上有什么"要全枚举完才说得清。
        missing.RemoveAll(n => onDisk.Contains(n));
        foreach (var name in onDisk)
        {
            if (IsTempName(name)) continue;        // 临时件是自己的类，不冒充孤儿
            if (!claimed.Contains(name)) orphans.Add(name);
        }
        return new ReconcileDropResult(missing, orphans, temps);
    }

    /// <summary>
    /// 占用那句话（与 <see cref="ClipAssets.DescribeUsage"/> 同一颗职责：交<b>没取词的</b>「键＋实参」，
    /// 取词发生在宿主贴上屏那一刻）。没件时如实说空，不编一个 0 KB。
    /// 这里交的只是<b>占用那一段</b>；整行（占用＋路径＋孤儿计数）由宿主拿 <c>drop.usage.line</c> 收。
    /// </summary>
    public static LocMessage DescribeUsage(DropFootprint footprint, long tempBytes, int tempCount)
    {
        if (footprint.FileCount == 0 && tempBytes == 0) return LocMessage.Of("drop.usage.empty");
        return LocMessage.Of("drop.usage.full",
            LocMessage.Of("drop.usage.files", footprint.FileCount, ClipAssets.DescribeBytes(footprint.FileBytes)),
            tempCount > 0
                ? LocMessage.Of("drop.usage.temps", tempCount, ClipAssets.DescribeBytes(tempBytes))
                : null);
    }

    /// <summary>
    /// 孤儿那一类的计数句（§3-Q6 第二类：只数不删，但数出来的必须看得见）。
    /// 0 时交 <c>null</c>：没有孤儿不是一条需要占一行才能读到的事实
    /// （与 <see cref="ClipAssets.DescribeOrphans"/> 同一口径）。
    /// </summary>
    public static LocMessage? DescribeOrphans(int count, long bytes) => count <= 0
        ? null
        : LocMessage.Of("drop.orphans.some", count, ClipAssets.DescribeBytes(bytes));
}
