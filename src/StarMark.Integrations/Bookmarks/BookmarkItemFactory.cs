#nullable enable
using System.Text.Json;
using StarMark.Abstractions;

namespace StarMark.Integrations.Bookmarks;

/// <summary>把解析出的书签记录映射为统一 Item（供 Chrome/Edge 两个源共用）。</summary>
public static class BookmarkItemFactory
{
    public static Item MapItem(BookmarkEntry e, string sourceId, long now)
    {
        var domain = TryGetDomain(e.Url);
        var added = e.BookmarkedAt > 0 ? e.BookmarkedAt : now;

        // URL 归一化只用于 source_id（去重主键）：把同源不同变体（尾斜杠 / utm 参数 / 默认端口 /
        // GitHub 的 tab= 查询、深层 blob 路径）统一为同一 source_id，避免重复条目分裂标签与笔记
        // （扩展对比方案 P1-5）。但 Uri 保留用户原始 URL——它是 LauncherEx 实际打开的目标，
        // 归一串会丢掉 #锚点/GitHub 深链，导致书签点开到错误位置且原始精确链接无处可存。
        var normalizedUrl = StarMark.Abstractions.UriNormalizer.Normalize(e.Url);

        var meta = new BookmarkMeta
        {
            FolderPaths = e.FolderPaths,
            BookmarkedAt = added,
        };

        // 用最深层文件夹名作为标签（仅有根级时不打标签）
        var tag = e.FolderPaths.Count > 0 ? e.FolderPaths[^1] : null;

        return new Item
        {
            Type = ItemType.Bookmark,
            Source = sourceId,
            SourceId = normalizedUrl,
            Title = string.IsNullOrWhiteSpace(e.Title) ? e.Url : e.Title,
            Subtitle = domain,
            Uri = e.Url,
            CreatedAt = added,
            UpdatedAt = added,
            SyncedAt = now,
            Tags = tag is null ? new List<string>() : new List<string> { tag },
            ExtraJson = JsonSerializer.Serialize(meta),
        };
    }

    public static string TryGetDomain(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            return uri.Host;
        return string.Empty;
    }
}