#nullable enable
using System;

namespace StarMark.Abstractions.Feed;

/// <summary>
/// 一个网址来源（RSS 2.0 或 Atom 订阅地址）。
/// <para>
/// <b>本仓库里它的定位是"信息来源"，不是"订阅流"</b>（D4 裁决）：源里的条目只是<b>候选</b>，
/// 用户点「收藏」才会写进库。所以这里没有"已读/未读""自动同步"这类状态——那些是阅读器的概念。
/// </para>
/// </summary>
/// <param name="Id">稳定标识（存进设置里的自增/哈希 id；改名不影响它）。</param>
/// <param name="Name">用户看到的名字。留空时由域名兜底——<b>不允许出现空白条目</b>。</param>
/// <param name="Url">订阅地址（http/https）。</param>
/// <param name="Enabled">关掉＝不抓取，但保留配置（用户常常是"暂时不想看"而不是"删掉"）。</param>
public sealed record RssSourceConfig(int Id, string Name, string Url, bool Enabled = true)
{
    /// <summary>单个源允许的最大条目数（解析与展示同一口径）：源方给 500 条时，
    /// 全部搬进内存再丢掉一半，不如在解析阶段就按这个数停手。</summary>
    public const int MaxEntries = 60;

    /// <summary>这一栏能不能用：只有 http/https 值得去抓。</summary>
    public static string? UrlProblem(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "地址是空的";
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return "认不出这是一个完整地址（要带 http:// 或 https://）";
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return $"只支持 http / https，这个是 {parsed.Scheme}";
        return null;
    }

    /// <summary>没填名字时用什么显示：域名（去掉 www.），拿不到就退回原地址。</summary>
    public static string FallbackName(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host))
            return url?.Trim() ?? string.Empty;
        var host = parsed.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? parsed.Host[4..] : parsed.Host;
        return host.Length == 0 ? parsed.Host : host;
    }
}

/// <summary>
/// 一条从源里读到的条目。<b>它还没有进库</b>——进库要用户点「收藏」。
/// </summary>
/// <param name="Title">标题；源没给时由链接兜底。</param>
/// <param name="Link">条目自己的地址（已尽量补成绝对地址）。</param>
/// <param name="PublishedAt">发布时间；源没给或给的形式认不出时为 null（<b>不拿"现在"冒充</b>，
/// 否则用户会以为这是一条刚发布的内容）。</param>
/// <param name="Summary">摘要（纯文本，标签与实体都已剥掉）。可为空。</param>
/// <param name="Author">作者/来源署名。可为空。</param>
/// <param name="SourceId">来自哪个源（<see cref="RssSourceConfig.Id"/>）。</param>
/// <param name="SourceName">源名，随条目一起带出来：列表里要标"这条是哪个源给的"，
/// 否则用户无法判断该不该收藏。</param>
public sealed record RssEntry(
    string Title,
    string Link,
    DateTimeOffset? PublishedAt,
    string Summary,
    string? Author,
    int SourceId,
    string SourceName);

/// <summary>
/// 一条 RSS 候选被用户点「收藏」后，写进库时用的源内标识。
/// <para>
/// <b>前缀与 <c>trending-bookmark:</c> 同族，而不是直接用 URL</b>：同一个地址可能有多个生产者
/// （浏览器导入的书签、热榜收藏、这里收藏），按 <c>(source, source_id)</c> 删才不会连别的生产者一起删掉
/// （P-65 同一形状）。
/// </para>
/// </summary>
public static class RssEntryIdentity
{
    /// <summary>钉死的前缀。<b>改了它，之前收藏进来的行就查不回来了</b>（按前缀列不出、按 id 删不掉），
    /// 所以这里只允许加，不允许改。</summary>
    public const string BookmarkSourcePrefix = "rss-bookmark:";

    /// <summary>去掉首尾空白：地址带一个空格就会多出一行看起来一模一样的收藏。</summary>
    public static string BookmarkSourceId(string link) => BookmarkSourcePrefix + (link ?? string.Empty).Trim();
}

/// <summary>
/// RSS 收藏落到哪个文件夹（判据只在这里说一次）。
/// <para>不新建一套"订阅文件夹"实体：<c>BookmarkMeta.FolderPaths</c> 已经是文件夹树的唯一来源，
/// 再立一个存储就会两边各长出一棵树。源名一到一级，收藏动作就只是把这一层写进 <c>ExtraJson</c>。</para>
/// </summary>
public static class RssFolders
{
    /// <summary>所有订阅源共用的根。<b>名字钉死</b>：改了它，之前收藏进去的行仍留在旧名字的文件夹里，
    /// 用户会同时看到"RSS订阅"与新的那一棵，而没人知道为什么会有两棵。</summary>
    public const string Root = "RSS订阅";

    /// <summary>
    /// 某个源的收藏落点：根 + 源名，两级。<b>数组的每个元素就是一级</b>（见 <c>FolderPathUtil.BookmarkSegments</c>），
    /// 所以源名里带 "/" 也必须整段留着——拆成两级等于把一个源劈成两个文件夹。
    /// 源名为空时只给根（不编一个"未命名"出来：源列表在 Normalize 阶段就不会有空名走到这里）。
    /// </summary>
    public static string[] PathFor(string? sourceName)
        => string.IsNullOrWhiteSpace(sourceName) ? new[] { Root } : new[] { Root, sourceName.Trim() };

    /// <summary>给人看的那一条路径（"RSS订阅 / 源名"）。文案与真实层级必须同源，否则提示会指一个不存在的路径。</summary>
    public static string DisplayPath(string? sourceName) => string.Join(" / ", PathFor(sourceName));
}
