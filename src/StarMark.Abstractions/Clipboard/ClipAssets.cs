#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace StarMark.Abstractions.Clipboard;

/// <summary>
/// 剪贴板图片这套"看得见的文件"的<b>命名、尺寸与占用口径</b>——全部纯函数。
/// <para>
/// 为什么单独立一份：§3-Q6 把存储改成"对用户直接可见"，于是文件名不再是随手一个 GUID。
/// 可见就意味着这些名字会被用户手删、手改、从旧机器拷回来，还会被备份恢复再次拼出来——
/// 一处算错，症状分散成"图找不回 / 两张图抢同一个文件 / 设置页报的占用与实际差一倍"。
/// 所以命名字典与占用统计只能有这一份，采集侧、删除侧、对账侧、设置页都问它。
/// </para>
/// <para>IO（真正落盘、stat、原子替换）留在调用方：这里的每个函数都只吃字符串与数字，能逐值断言。</para>
/// </summary>
public static class ClipAssets
{
    /// <summary>图片目录名，挂在 <c>%LOCALAPPDATA%\StarMark\</c> 下（与 logs/sdk/downloads 同一根）。</summary>
    public const string FolderName = "clip";

    /// <summary>主图扩展名。内部只存 PNG（§2 裁决）。</summary>
    public const string MainExtension = ".png";

    /// <summary>缩略图扩展名：JPEG q60——列表每行都要加载它，PNG 那种尺寸对它没意义。</summary>
    public const string ThumbExtension = ".jpg";

    /// <summary>缩略图长边像素（§5 Q5 裁决）。</summary>
    public const int ThumbnailMaxEdge = 160;

    /// <summary>缩略图 JPEG 质量（百分数，§5 Q5 裁决）。</summary>
    public const int ThumbnailQuality = 60;

    /// <summary>文件名里带的哈希位数。<b>只为人类友好</b>；完整性靠条目里的 <c>source_id</c>（真哈希），见 §3-Q6。</summary>
    public const int HashPrefixChars = 8;

    /// <summary>图片目录的绝对路径（<paramref name="storageRoot"/>＝<c>%LOCALAPPDATA%\StarMark</c>）。</summary>
    public static string DirectoryFor(string storageRoot) => Path.Combine(storageRoot, FolderName);

    /// <summary>
    /// 主/缩略两张图共用的名基：<c>2026-09-27_1432_9f2a3b8c</c>（本地时间到分 + 哈希前缀）。
    /// <para>日期在前＝资源管理器按名排序就是按时间排序，用户手翻也读得懂；
    /// 只到分而不是到秒：同一分钟内连贴两张很常见，靠哈希段区分即可，多一位时间戳只会让名字更长更难读。</para>
    /// </summary>
    public static string BaseNameOf(string sourceId, DateTimeOffset whenLocal)
    {
        var hash = HashPartOf(sourceId);
        var stamp = whenLocal.LocalDateTime.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);
        return $"{stamp}_{hash}";
    }

    /// <summary>主图文件名。</summary>
    public static string MainNameOf(string sourceId, DateTimeOffset whenLocal)
        => BaseNameOf(sourceId, whenLocal) + MainExtension;

    /// <summary>缩略图文件名：与主图同基、加 <c>_thumb</c> 后缀（两文件必须同目录，见 §5 Q5"同生同死"）。</summary>
    public static string ThumbNameOf(string sourceId, DateTimeOffset whenLocal)
        => BaseNameOf(sourceId, whenLocal) + "_thumb" + ThumbExtension;

    /// <summary>
    /// 写盘用的临时名（§3-Q6 洞3：半写残由构造消灭——先写 <c>*.tmp</c> 再原子改名）。
    /// <para>它带 <c>.tmp</c> 而不是一个随机后缀，是为了让启动对账一眼认得出"这是我们自己没改完的残货"，
    /// 从而<b>只清自己的临时件</b>，永不误碰用户拷进来的文件。</para>
    /// </summary>
    public static string TempNameOf(string finalName) => finalName + ".tmp";

    /// <summary>这个名是不是我们自己的临时件（对账只按这一条判"可清"）。</summary>
    public static bool IsTempName(string fileName) => fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    /// <summary>缩略图的长边（横向图取宽、纵向图取高）。</summary>
    public static int ThumbnailEdge(int width, int height) => Math.Max(1, Math.Max(width, height));

    /// <summary>
    /// <c>source_id</c> 的哈希段（去掉 <see cref="ClipboardPolicy.SourceIdPrefix"/> 前缀取前
    /// <see cref="HashPrefixChars"/> 位）。
    /// <para><b>这里必须验字符集</b>：source_id 会随备份文件回到本机，而备份的 <c>extra_json</c>/<c>source_id</c>
    /// 是用户可以手改的文本。不验就拼路径，等于把"../../windows/x"这类字符串交给文件系统。</para>
    /// </summary>
    public static string HashPartOf(string? sourceId)
    {
        var raw = sourceId ?? string.Empty;
        if (raw.StartsWith(ClipboardPolicy.SourceIdPrefix, StringComparison.Ordinal))
            raw = raw[ClipboardPolicy.SourceIdPrefix.Length..];
        var keep = 0;
        while (keep < raw.Length && keep < HashPrefixChars && IsHex(raw[keep])) keep++;
        // 一个字符都不合法时也不抛、也不留空串（空串会让两张图抢同一个文件名），落到"00000000"，
        // 由调用方的"同名先查内容"路径接手——坏数据不该把采集链炸断，但也不能变成可遍历的路径片段。
        return keep == 0 ? new string('0', HashPrefixChars) : raw[..keep];
    }

    /// <summary>这个 <c>source_id</c> 能不能安全地拿去拼文件名（对账/恢复都问它，不各写一份字符集）。</summary>
    public static bool IsSafeSourceId(string? sourceId)
    {
        var raw = sourceId ?? string.Empty;
        if (raw.Length < ClipboardPolicy.SourceIdPrefix.Length + 8) return false;
        if (!raw.StartsWith(ClipboardPolicy.SourceIdPrefix, StringComparison.Ordinal)) return false;
        foreach (var c in raw[ClipboardPolicy.SourceIdPrefix.Length..])
            if (!IsHex(c)) return false;
        return true;
    }

    private static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    /// <summary>
    /// 占用统计的<b>唯一口径</b>：主图与缩略图分开计，设置页要说"图片 N 张 · 共 X，缩略图 Y"。
    /// <para>把两个数一起加总成一个"占用"是最省事的写法，也正是用户最先问的那一句答不上来：
    /// "我删了 100 张图为什么没少多少"——缩略图还在。分类计数是这份职责的形状。</para>
    /// </summary>
    public readonly record struct Footprint(long MainBytes, long ThumbBytes, int MainCount, int ThumbCount)
    {
        public long TotalBytes => MainBytes + ThumbBytes;
        public int TotalFiles => MainCount + ThumbCount;
    }

    /// <summary>
    /// 目录内容 → <see cref="Footprint"/>。<paramref name="files"/> 由调用方枚举（这里不碰文件系统，
    /// 才可能在单测里逐值钉住分派规则）。<paramref name="name"/> 是文件名，含 <c>.tmp</c> 的按临时件计入
    /// <paramref name="tempBytes"/>，既不算主图也不算缩略图。
    /// </summary>
    public static Footprint Summarize(IEnumerable<(string Name, long Bytes)> files, out long tempBytes)
    {
        var m = (long)0; var t = (long)0; var mc = 0; var tc = 0; var tmp = (long)0;
        foreach (var (name, bytes) in files)
        {
            if (IsTempName(name)) { tmp += bytes; continue; }
            if (name.EndsWith(ThumbExtension, StringComparison.OrdinalIgnoreCase)
                && name.Contains("_thumb", StringComparison.OrdinalIgnoreCase)) { t += bytes; tc++; continue; }
            m += bytes; mc++;
        }
        tempBytes = tmp;
        return new Footprint(m, t, mc, tc);
    }

    /// <summary>
    /// 人类可读体积（设置页与条目副标题共用一份，免得两处对"1.2 MB"的取整不一样）。
    /// <b>固定用不变文化</b>：小数点写成分号或逗号的地方，测试与用户看到的都不是同一个数。
    /// </summary>
    public static string DescribeBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB",
        < 1024L * 1024 * 1024 => (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
        _ => (bytes / (1024.0 * 1024 * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + " GB",
    };

    /// <summary>图片条目的标题（§1b 裁决：尺寸本身就是要说给用户看的第一个事实）。</summary>
    public static string DescribeTitle(int width, int height)
        => width > 0 && height > 0 ? $"{width}×{height}" : "图片";
}
