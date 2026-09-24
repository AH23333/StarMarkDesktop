#nullable enable
using System;
using System.Globalization;
using System.Linq;

namespace StarMark.Abstractions.Feed;

/// <summary>
/// RSS 候选 → 卡片行的形状（纯函数）。与 <c>TrendingRowDraft</c> 同一分工：
/// <b>这一条不入库</b>（<c>Id = 0</c>、<c>Source = ItemSources.Rss</c>），靠 <c>ItemCardPolicy</c>
/// 把一切会写主库的动作关掉；真正入库只发生在用户点「收藏到文件夹」那一下。
/// </summary>
public static class RssRowDraft
{
    /// <summary>副标题这一行的时间格式。<b>源没给时间就明说</b>：留空会让整列看起来像缺了什么，
    /// 而写成"刚刚"更是把不知道画成有把握（同一套口径也用在别处的 PublishedAt 上）。</summary>
    public static string PublishedText(RssEntry entry) => entry.PublishedAt is { } at
        ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        : "源没给时间";

    public static Item ForCandidate(RssEntry entry)
    {
        // 地址两侧的空白先去掉：源里偶尔会给出 " https://… "，带着空格它既过不了 Uri.TryCreate
        // （点这一行就变成"打了字什么都没发生"那一类静默失效），又会让同一篇文章在库里多出两行。
        var link = (entry.Link ?? string.Empty).Trim();
        return new Item
        {
            Id = 0,
            Type = ItemType.Bookmark,                     // 只为取书签的图标与"有链接"这套既有渲染口径；这行不入库
            Source = ItemSources.Rss,
            // 与收藏后那一行的身份同一个函数：卡片要能拿它去查"这条是不是已经收过了"
            SourceId = RssEntryIdentity.BookmarkSourceId(link),
            Title = entry.Title,
            // 副标题不再重复源名（页面按源分组成文件夹，源名已经是分组标题），这里给发布时间
            Subtitle = PublishedText(entry),
            Uri = link,
            Description = string.IsNullOrWhiteSpace(entry.Summary) ? null : entry.Summary.Trim(),
            // 落点随行携带：收藏只是把这一行的 Source 换成本地，文件夹层级不再另算一处，
            // 于是"提示里说的那个文件夹"与"真正写进去的那个文件夹"不可能是两个东西。
            ExtraJson = BookmarkMetaJson(entry.SourceName),
        };
    }

    /// <summary>
    /// 点「收藏到文件夹」时真正写进库的那一行：由候选行<b>原样搬过来</b>，只改三件事——
    /// <c>Source</c> 从 <see cref="ItemSources.Rss"/> 换成 <see cref="ItemSources.Local"/>
    /// （进库后它就是本机书签，若仍标着 rss，<see cref="ItemCardPolicy"/> 会把它的标签/置顶全关掉，
    /// 用户刚收进来的那条反而成了二等公民）、补上两个时间戳、<c>Id</c> 归零交给仓储判定。
    /// <para>刻意不做成"另起一份形状"：两份各写字段的结果就是热榜那边已经出现过的那类偏差——
    /// 卡片上显示一个标题，收进库变成另一个。</para>
    /// </summary>
    public static Item ForCollect(Item candidate, long nowUnixSeconds) => new()
    {
        Id = 0,
        Type = ItemType.Bookmark,
        Source = ItemSources.Local,
        SourceId = candidate.SourceId,
        Title = candidate.Title,
        Subtitle = candidate.Subtitle,
        Uri = candidate.Uri,
        Description = candidate.Description,
        ExtraJson = candidate.ExtraJson,
        CreatedAt = nowUnixSeconds,
        UpdatedAt = nowUnixSeconds,
    };

    /// <summary>收藏落点写成书签元信息（<c>FolderPaths</c> 数组每个元素即一级，见 <c>FolderPathUtil.BookmarkSegments</c>）。</summary>
    private static string BookmarkMetaJson(string? sourceName)
        => System.Text.Json.JsonSerializer.Serialize(new BookmarkMeta
        {
            FolderPaths = RssFolders.PathFor(sourceName).ToList(),
        });
}
