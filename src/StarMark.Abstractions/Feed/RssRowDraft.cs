#nullable enable
using System;
using System.Globalization;

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
        };
    }
}
