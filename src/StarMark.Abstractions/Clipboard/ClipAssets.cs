#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

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

    /// <summary>默认存储根（与 <c>logs</c>/<c>sdk</c>/<c>downloads</c> 同一根，同样不加密不藏）。</summary>
    public static string DefaultStorageRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConstants.AppName);

    /// <summary>
    /// 测试/诊断用的目录改道。<b>写侧（Integrations）与删侧（Data 的轮转/清空/删单条）必须读同一个字段</b>：
    /// 两边各留一份 override 时，测试里改了写侧、删侧照旧指向用户目录，症状是
    /// "删除路径永远删不到东西，而用户的 clip 目录里悄悄长出测试图"——这条恰好是本轮要验的事，却测不出来。
    /// </summary>
    public static string? FolderOverride;

    /// <summary>图片目录的绝对路径。凡是拼 clip 路径的地方都问它，不许自己 Combine 一遍。</summary>
    public static string Folder => FolderOverride ?? DirectoryFor(DefaultStorageRoot);

    /// <summary>
    /// 库里读出来的文件名 → 绝对路径；<b>名字不安全就返回 null</b>（调用方据此"什么都不做"）。
    /// <para>删除与读取都走这一句：校验只写在写盘那侧的话，"..\\" 这种从手改过的备份里回来的名字
    /// 就只在写的时候被拦住，删的时候照样被拼进路径。</para>
    /// </summary>
    public static string? FullPathOf(string? name)
        => IsSafeFileName(name) ? Path.Combine(Folder, name!) : null;

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
    /// 一个<b>从库里读出来</b>的文件名能不能安全地拼到 clip 目录下。
    /// <para>我们写出去的名字一定过这一关，但 <c>clipFile</c> 存在 <c>extra_json</c> 里，而备份文件是
    /// 用户可以手改的文本：不校验就拼路径，"..\\" 会把删除与写入都带到 clip 目录之外——那就不再是
    /// "清一张历史图片"，而是"按一个来路不明的字符串删文件"。宁可少删、不可乱删。</para>
    /// </summary>
    public static bool IsSafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) return false;
        if (name.IndexOfAny(new[] { '/', '\\', ':', '?' }) >= 0) return false;
        if (name.Contains("..", StringComparison.Ordinal)) return false;
        if (name.StartsWith(' ') || name.EndsWith('.')) return false;      // Windows 会静默剥掉它们，两边就不是同一个文件了
        return name.EndsWith(MainExtension, StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(ThumbExtension, StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }

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
    /// 占用那句话（设置页打开时算一次，§3-Q1 第三层）。
    /// <para>主图与缩略图<b>分开报</b>：合成一个总数，用户删了图却看见数字几乎没动，
    /// 就会以为这个功能在骗人——而真相是缩略图还在。临时件单列（它只可能来自一次没写完的采集）。</para>
    /// </summary>
    public static string DescribeUsage(Footprint footprint, long tempBytes)
    {
        if (footprint.TotalFiles == 0 && tempBytes == 0)
            return "图片目录还是空的。";
        var sb = new System.Text.StringBuilder();
        sb.Append(footprint.MainCount > 0
            ? $"图片 {footprint.MainCount} 张 · 共 {DescribeBytes(footprint.MainBytes)}"
            : "还没有图片");
        if (footprint.ThumbCount > 0)
            sb.Append($"；缩略图 {footprint.ThumbCount} 张 · {DescribeBytes(footprint.ThumbBytes)}");
        if (tempBytes > 0)
            sb.Append($"；另有 {DescribeBytes(tempBytes)} 未写完的临时件（不影响历史，可清理）");
        return sb.ToString();
    }

    /// <summary>
    /// 按当前的平均单张体积，估一下"上限调到 <paramref name="cap"/> 张大概占多少"。
    /// <para>§3-Q1 要的是"让用户自己配"这件事<b>有数字可依</b>：只给一个条数框，用户不知道该填多少，
    /// 结果就是要么不敢调、要么一口气调到 2000 然后发现磁盘满了。没样本时如实说估不了，不编一个数。</para>
    /// </summary>
    public static string DescribeProjection(Footprint footprint, int cap)
    {
        if (footprint.MainCount == 0) return $"当前一张都还没有，调到 {cap} 张的实际占用还估不出来。";
        var avg = footprint.MainBytes / (double)footprint.MainCount;
        return $"按现有平均单张 {DescribeBytes((long)avg)} 估算，上限 {cap} 张约 {DescribeBytes((long)(avg * cap))}"
             + $"（缩略图另计，约为其 {ThumbnailMaxEdge}px 的 JPEG）。";
    }

    /// <summary>确认框里最多列出几个名字：再多就变成一屏没人会读的清单，剩下的用总数说清。</summary>
    public const int CleanupPreviewLimit = 8;

    /// <summary>
    /// 孤儿那一类的计数句（§3-Q6：这一类<b>只数不删</b>——但"数出来"必须让用户看得见，
    /// 否则"目录里有些文件历史并不认识"这件事只存在于日志里）。
    /// <para>0 时返回空串：没有孤儿不是一条需要占一行才能读到的事实（同一口径见"关着不占行"）。</para>
    /// </summary>
    public static string DescribeOrphans(int count, long bytes) => count <= 0
        ? string.Empty
        : $"另有 {count} 个文件不在历史里（共 {DescribeBytes(bytes)}）："
          + "可能是某次删除没删干净的残留，也可能是你自己拷进来的。";

    /// <summary>
    /// 「清理」确认框的正文。<b>三件事必须写在同一屏里</b>：动的是哪个目录里的哪几个名字、
    /// 一条历史条目都不动、<b>这份名单之外一个文件都不碰</b>。
    /// <para>缺第二句，用户会以为这颗按钮是"清理剪贴板历史"；缺第三句，一次批量删除的想象空间
    /// 就足够让人不敢按——而按不下去的结果是那些文件永远留在那里。名单被截断时必须说"其余 N 个没列出"，
    /// 不许让用户以为列出来的就是全部。</para>
    /// </summary>
    public static string CleanupConfirmBody(IReadOnlyList<string> orphanNames, long orphanBytes,
        int tempCount, long tempBytes, string folder)
    {
        var sb = new StringBuilder();
        var shown = Math.Min(CleanupPreviewLimit, orphanNames.Count);
        if (shown > 0)
        {
            sb.Append("这些文件在「").Append(folder).Append("」里，但历史中没有任何条目指向它们：\n");
            for (var i = 0; i < shown; i++) sb.Append("· ").Append(orphanNames[i]).Append('\n');
            if (orphanNames.Count > shown)
                sb.Append("…其余 ").Append(orphanNames.Count - shown).Append(" 个没有列出。\n");
            sb.Append("共 ").Append(orphanNames.Count).Append(" 个，约 ").Append(DescribeBytes(orphanBytes)).Append("。\n");
        }
        if (tempCount > 0)
            sb.Append("另有 ").Append(tempCount).Append(" 个没写完的临时件（约 ")
              .Append(DescribeBytes(tempBytes)).Append("）——那是我们自己留下的半件，不可能是你的文件。\n");
        sb.Append("\n删掉这些文件不会动任何一条历史条目，也不会碰这份名单之外的任何一个文件。")
          .Append("拿不准就先别删：它们除了占地方，不做任何事。");
        return sb.ToString();
    }

    /// <summary>
    /// 人类可读体积（设置页与条目副标题共用一份，免得两处对"1.2 MB"的取整不一样）。
    /// <b>固定用不变文化</b>：小数点写成分号或逗号的地方，测试与用户看到的都不是同一个数。
    /// <para>批次 RZ：这颗从"自己写一遍梯子"改成<b>转发</b> <see cref="FileSizeText.Human"/>——
    /// 原先两份的档位与取整恰好一样，但一个锁了文化一个没锁，同一个体积在卡片与占用行里会不同形。
    /// 留着这个名字是因为它有 12 个调用方；<b>转发不许长出自己的判断</b>（RX 的同一条口径，由闸门钉）。</para>
    /// </summary>
    public static string DescribeBytes(long bytes) => FileSizeText.Human(bytes);

    /// <summary>图片条目的标题（§1b 裁决：尺寸本身就是要说给用户看的第一个事实）。</summary>
    public static string DescribeTitle(int width, int height)
        => width > 0 && height > 0 ? $"{width}×{height}" : "图片";

    // ==================== 启动对账（§3-Q6 三分类） ====================

    /// <summary>
    /// 对账结果。<b>三类各说一件事，且只有一类动数据库、零类动文件</b>：
    /// <list type="bullet">
    /// <item><description><c>MissingRowIds</c>：行有图无（用户手删文件 / 旧备份恢复过来）⇒ 条目<b>保留</b>，只打 <c>clipMissing</c>。删行等于替用户决定他不再需要它。</description></item>
    /// <item><description><c>OrphanNames</c>：图有行无（删行时文件删失败 / 用户自己拷进来）⇒ <b>只数不删</b>。目录对用户可见，静默批量删除是最难解释的越权。</description></item>
    /// <item><description><c>TempNames</c>：临时件（<c>*.tmp</c>）——写盘走"临时名 + 原子改名"，这一类本应由构造消灭，出现即说明有一次没写完。</description></item>
    /// </list>
    /// </summary>
    public readonly record struct ReconcileResult(
        IReadOnlyList<long> MissingRowIds,
        IReadOnlyList<string> OrphanNames,
        IReadOnlyList<long> RestoredRowIds,
        IReadOnlyList<string> TempNames)
    {
        public static readonly ReconcileResult Empty = new(Array.Empty<long>(), Array.Empty<string>(),
            Array.Empty<long>(), Array.Empty<string>());
        public bool NothingToDo => MissingRowIds.Count == 0 && OrphanNames.Count == 0
            && RestoredRowIds.Count == 0 && TempNames.Count == 0;
    }

    /// <summary>
    /// 把"库里的图片行"与"目录里的文件"对一次账。
    /// <para><b>比对一律 OrdinalIgnoreCase</b>：NTFS 大小写不敏感，而我们写出去的名字来自
    /// <c>ToString("yyyy-MM-dd_HHmm")</c> 与十六进制哈希（可能是大写 A–F），备份恢复回来的名字
    /// 却可能被改过大小写——按序数敏感比就会把"其实在"的图判成缺失，用户看到的是"我的图打不开了"。</para>
    /// <para><paramref name="rows"/> 的 <c>HasFlag</c> 带进来是为了第三类反向：<b>文件又被用户放回来了</b>
    /// （比如从别的机器拷回整个目录）时要把上次打的 <c>clipMissing</c> 清掉，否则那半句假话会永远挂着。</para>
    /// </summary>
    public static ReconcileResult Reconcile(
        IEnumerable<ClipboardEntry.ClipAssetRow> rows, IEnumerable<string> fileNames)
    {
        var files = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<long>();
        var restored = new List<long>();

        foreach (var (id, main, thumb, flagged) in rows)
        {
            if (!string.IsNullOrWhiteSpace(main)) claimed.Add(main!);
            if (!string.IsNullOrWhiteSpace(thumb)) claimed.Add(thumb!);

            var here = !string.IsNullOrWhiteSpace(main) && files.Contains(main!);
            // 四格判据一格都不能串：文件在+标着 ⇒ 清标记；文件没+没标 ⇒ 打标记；
            // 文件没+已标着 ⇒ 什么都不做（幂等，不然每启动都重写一遍全表）。
            if (here && flagged) restored.Add(id);
            if (here || string.IsNullOrWhiteSpace(main)) continue;
            if (!flagged) missing.Add(id);
        }

        var orphans = new List<string>();
        var temps = new List<string>();
        foreach (var name in files)
        {
            if (IsTempName(name)) { temps.Add(name); continue; }
            if (!claimed.Contains(name)) orphans.Add(name);
        }

        return new ReconcileResult(missing, orphans, restored, temps);
    }
}
