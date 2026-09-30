#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace StarMark.Abstractions;

/// <summary>
/// 本地文件/文件夹的<b>身份基元</b>：由磁盘路径派生统一的 <c>source_id</c> 与 <c>uri</c>，
/// 作为「同一路径在系统各处（Everything 实时查询、后台全量索引、资源管理器/自绘拖拽登记）
/// 合并为同一条 items 记录」的唯一依据。
/// <para>
/// <b>为何要单一真源</b>：<c>items(source, source_id)</c> 上有唯一索引，去重与合并全部按 <c>source_id</c> 判定，
/// 与 <c>uri</c> 字符串无关。因此只要各来源对同一路径算出<b>相同</b>的 <c>source_id</c>，登记与同步就会天然合并、
/// 绝不产生重复行。<see cref="SourceIdForPath"/> 即沿用原 Everything 侧的哈希口径，供 Everything 适配器与
/// 拖拽登记共用，杜绝两处各写一份哈希造成的漂移。
/// </para>
/// <para><b>只登记索引记录，绝不改动磁盘上的实际文件</b>（不移动 / 改名 / 删除）。</para>
/// </summary>
public static class LocalFileIdentity
{
    /// <summary>文件系统来源标识（与 <see cref="ItemSources.FileSystem"/> 同值）。</summary>
    public const string Source = ItemSources.FileSystem;

    /// <summary>
    /// 路径的稳定哈希作为 <c>source_id</c>：UTF-8 小写归一 → SHA256 → 取前 8 字节的十六进制。
    /// 与 Everything 查询侧、全量索引侧完全一致，保证同一路径幂等合并。
    /// </summary>
    public static string SourceIdForPath(string fullPath)
    {
        var bytes = Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 8);
    }

    /// <summary>统一 URI：两斜杠 <c>file://X:/dir/name</c>（反斜杠转正斜杠）。沿用 Everything 既有拼法。</summary>
    public static string UriForPath(string fullPath) => "file://" + fullPath.Replace('\\', '/');

    /// <summary>
    /// 把 <c>file://</c> URI 还原成 Windows 磁盘路径。<b>同时兼容两斜杠（Everything 侧 <c>file://C:/x</c>）
    /// 与三斜杠（DB 侧 <c>file:///C:/x</c>）两种历史拼法</b>，并且<b>刻意不做 percent 解码</b>。
    /// 非 <c>file://</c>、缺盘符或空串时返回 false。不校验磁盘是否存在。
    /// <para><b>不解码的理由（批次 SH 实测，net9-windows；以前这条注释给的是另一套说法，那套是错的）</b>：
    /// <c>new Uri(uri).LocalPath</c> 在两斜杠形态上其实<b>能</b>给出正确路径（<c>file://C:/My Doc/x.png</c>
    /// ⇒ <c>C:\My Doc\x.png</c>，老注释说"会把盘符当主机名解析成 <c>\\c\x</c>"——今天复现不出来），
    /// 但它在另外两格上会<b>认错文件</b>：① 裸 <c>#</c> 被当片段截断
    /// （<c>file://C:/docs/C#入门.docx</c> ⇒ <c>C:\docs\C</c>，同一目录里 <c>C#1.txt</c> 与 <c>C#2.txt</c> 还会在去重键上塌成一条）；
    /// ② 文件名里<b>真的</b>带 <c>%XX</c> 的会被解掉（<c>file://C:/b/100%20.txt</c> ⇒ <c>C:\b\100 .txt</c>，指到另一个名字上去）。
    /// ⇒ 所以本颗只管"剥前缀＋换斜杠"，要不要解码一律交给 <see cref="TryExistingPath"/> 用磁盘事实决定。</para>
    /// </summary>
    public static bool TryPathFromUri(string? uri, out string fullPath)
    {
        fullPath = string.Empty;
        const string prefix = "file://";
        if (string.IsNullOrEmpty(uri) || !uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = uri.Substring(prefix.Length).Replace('/', '\\').TrimStart('\\');
        if (rest.Length >= 2 && char.IsLetter(rest[0]) && rest[1] == ':')
        {
            fullPath = rest;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 取"磁盘上真的放着东西"那一格：<see cref="TryPathFromUri"/> 的<b>原始形态</b>优先，其次
    /// <c>new Uri(uri).LocalPath</c> 的<b>解码形态</b>，两个候选都要交给调用方的 <paramref name="existsOnDisk"/>。
    /// <para>
    /// <b>为什么必须两个都试（这条规则原先在 <c>LauncherEx</c> 与 <c>PreviewHost</c> 各写一遍，
    /// 而 <c>ItemCardActions</c>／<c>ItemDragHelper</c> 只写了一半）</b>：仓里 <c>file://</c> 有<b>两类互补的生产者</b>——
    /// ① <see cref="UriForPath"/>／Everything／Ditto 侧＝<b>不编码</b>（<c>file://C:/x</c> 或 <c>file:///C:/x</c>，
    ///   <c>#</c>、空格、中文一律原样）。只有 <see cref="TryPathFromUri"/> 能原样还原
    ///   （<c>LocalPath</c> 会把裸 <c>#</c> 之后当片段截断，两斜杠形态还会把 <c>C:</c> 当主机名）。
    /// ② <c>new Uri(path).AbsoluteUri</c> 侧＝<b>percent 编码</b>（剪贴板图片行的 <c>UriOf</c>、快捷启动的选择器/拖入）。
    ///   只有 <c>LocalPath</c> 能解回来（<see cref="TryPathFromUri"/> <b>故意</b>不解 <c>%XX</c>，
    ///   以守住与 <see cref="UriForPath"/> 的往返契约——解了就会把真名叫 <c>100%.txt</c> 的文件认错）。
    /// </para>
    /// <para>
    /// <b>为什么"只在磁盘上有对应文件时"才换用解码形态</b>：两斜杠／三斜杠＋原样 <c>#</c> 这三种线索并不能唯一确定
    /// 一份 URI 出自哪个生产者（<c>DittoSource.ToFileUri</c> 就是"三斜杠但不编码"），<b>靠文本猜就会猜错一次而认错文件</b>。
    /// 磁盘上存在与否是当场可验的事实 ⇒ 猜不到就不猜：两个候选都不存在时返回 false，由调用方按自己的语义兜
    /// （打不开就报原因、复制就照抄原串、拖出就退成文本），<b>绝不静默造一个路径</b>。
    /// </para>
    /// <para><paramref name="existsOnDisk"/> 由调用方拥有：预览要"存在的<b>文件</b>且已归一"（拒绝 <c>..</c> 遍历），
    /// 打开／定位要"存在的文件或<b>目录</b>"，语义不同而<b>还原规则相同</b>——所以还原规则在这一颗，判定留在宿主。</para>
    /// <para>非 <c>file://</c> 前缀直接 false（不去猜别的协议）。<b>缺盘符／UNC（<c>file://server/share</c>）不在此列</b>：
    /// <see cref="TryPathFromUri"/> 对它返回 false，但解码那一格仍能还原成 <c>\\server\share</c>，
    /// 而那是 <c>LauncherEx</c> 今天已有的能力——本颗不许顺手把它收窄掉（台架 SH4 钉着这条）。</para>
    /// </summary>
    public static bool TryExistingPath(string? uri, Func<string, bool> existsOnDisk, out string fullPath)
    {
        fullPath = string.Empty;
        var (raw, decoded) = PathCandidates(uri);
        if (raw.Length > 0 && existsOnDisk(raw)) { fullPath = raw; return true; }
        if (decoded.Length > 0 && existsOnDisk(decoded)) { fullPath = decoded; return true; }
        return false;
    }

    /// <summary>
    /// <see cref="TryExistingPath"/> 的两个候选，<b>不做任何磁盘判定</b>：
    /// <c>Raw</c>＝纯字符串剥前缀（保留 <c>#</c> 与 <c>%</c>，非 <c>file://</c> 或缺盘符时为空串）、
    /// <c>Decoded</c>＝<c>new Uri(uri).LocalPath</c>（解 <c>%XX</c>，非法 URI 时为空串）。
    /// 调用方拿它做"有名字就显示"这类不需要磁盘的场合（快捷启动的默认标题）；
    /// <b>需要"真的存在"时请用 <see cref="TryExistingPath"/>，别在这里自己拼顺序。</b>
    /// </summary>
    public static (string Raw, string Decoded) PathCandidates(string? uri)
    {
        const string prefix = "file://";
        if (string.IsNullOrEmpty(uri) || !uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return (string.Empty, string.Empty);
        TryPathFromUri(uri, out var raw);   // 缺盘符／UNC 时返回 false 且 raw 为空串——按"没有这一格"处理
        string decoded;
        try { decoded = new Uri(uri).LocalPath; }
        catch (Exception e) when (e is UriFormatException or InvalidOperationException) { decoded = string.Empty; }
        return (raw, decoded);
    }

    /// <summary>
    /// 显示用的那一条路径（快捷启动的默认标题这类"不必动作、但要看对"的场合）：
    /// <b>先问磁盘</b>——两格里哪一格真的放着东西就用哪格；<b>两格都不在时退回原始那格</b>
    /// （原始那格至少保得住 <c>#</c>，而解码那格会把它拦腰截断）。
    /// <para>为什么不能像以前那样"剥到前缀就算"：手输／文本拖进来的链接会先经 <c>new Uri(text).AbsoluteUri</c>，
    /// 名字段是<b>percent 编码</b>的（中文与空格都在里面）。只认原始那格，新条目的默认名就印成
    /// <c>%E5%B7%A5%E4%BD%9C%20%E6%8A%A5%E5%91%8A.docx</c> 这种乱码——而它是要留在卡片上给用户看的。
    /// 要加进去的文件通常在盘上 ⇒ 问一次磁盘就能选对，且不需要猜这份 URI 出自哪个生产者。</para>
    /// </summary>
    public static string PreferredPathFromUri(string? uri, Func<string, bool> existsOnDisk)
    {
        if (TryExistingPath(uri, existsOnDisk, out var found)) return found;
        var (raw, decoded) = PathCandidates(uri);
        return raw.Length > 0 ? raw : decoded;
    }

    /// <summary>
    /// 由磁盘路径构造一条<b>未入库</b>（<c>Id=0</c>）的文件条目：<c>source_id</c>/<c>uri</c> 走本类基元，
    /// 标题取文件名、副标题取所在目录。供「拖入即登记」等按路径写入主库的入口使用；
    /// 落库交给 <c>IItemRepository.RecordItemAsync</c>（幂等 upsert，重建 search_text、保留用户态）。
    /// </summary>
    public static Item FromPath(string fullPath, string? title = null)
    {
        var name = Path.GetFileNameWithoutExtension(fullPath);
        if (string.IsNullOrWhiteSpace(name)) name = fullPath;   // 目录或根：GetFileName 可能为空，退回整路径
        var dir = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var shown = string.IsNullOrWhiteSpace(title) ? name : title!;
        return new Item
        {
            Type = ItemType.File,
            Source = Source,
            SourceId = SourceIdForPath(fullPath),
            Title = shown,
            Subtitle = dir,
            Uri = UriForPath(fullPath),
            SearchText = shown + ' ' + dir,
        };
    }
}
