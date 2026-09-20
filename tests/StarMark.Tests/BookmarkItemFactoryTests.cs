#nullable enable
using Xunit;
using StarMark.Abstractions;
using StarMark.Integrations.Bookmarks;

namespace StarMark.Tests;

/// <summary>
/// <see cref="BookmarkItemFactory.MapItem"/> 契约测试：它把已解析的 BookmarkEntry 映射为落库 Item，
/// 直接决定去重主键 / 标题兜底 / 标签取位 / 收藏时间兜底——这些都会静默影响数据正确性，此前无单测锁定。
/// 纯函数（无 I/O、无真机依赖），逐条钉死当前正确行为以防回归。
/// </summary>
public sealed class BookmarkItemFactoryTests
{
    private const string Source = "chrome_bookmarks";
    private const long Now = 1_700_000_000L;

    private static BookmarkEntry Entry(string url, string title, List<string>? folders = null, long bookmarkedAt = 0)
        => new() { Url = url, Title = title, FolderPaths = folders ?? new(), BookmarkedAt = bookmarkedAt };

    [Fact]
    public void MapItem_UsesNormalizedUrlAsDedupKeyAndUri()
    {
        // source_id/uri 必须是「归一后的 URL」，让同一资源的尾斜杠/utm 等变体在 (source, source_id) 上合并为一条。
        var withSlash = BookmarkItemFactory.MapItem(Entry("https://github.com/a/b/", "R"), Source, Now);
        var variant = BookmarkItemFactory.MapItem(Entry("https://github.com/a/b?utm_source=x", "R"), Source, Now);

        Assert.Equal("https://github.com/a/b", withSlash.SourceId);
        Assert.Equal("https://github.com/a/b", variant.SourceId);
        Assert.Equal(withSlash.SourceId, variant.SourceId);   // 同资源 → 同键（否则标签/笔记分裂）
        Assert.Equal(withSlash.Uri, withSlash.SourceId);       // uri 与去重键同源
        Assert.Equal(Source, withSlash.Source);                // 浏览器源键落在 Source，非 SourceId
        Assert.Equal(ItemType.Bookmark, withSlash.Type);
    }

    [Fact]
    public void MapItem_FallsBackToUrlWhenTitleBlank()
    {
        var item = BookmarkItemFactory.MapItem(Entry("https://example.com/x", "   "), Source, Now);
        Assert.Equal("https://example.com/x", item.Title);   // 空/白标题退回 URL（标题参与 FTS，不得留空）
    }

    [Fact]
    public void MapItem_TagIsOnlyDeepestFolderName()
    {
        var item = BookmarkItemFactory.MapItem(
            Entry("https://example.com", "T", new List<string> { "技术", "前端" }), Source, Now);
        Assert.Equal(new[] { "前端" }, item.Tags);   // 仅最深层文件夹作标签
    }

    [Fact]
    public void MapItem_NoFolders_YieldsNoTag()
    {
        var item = BookmarkItemFactory.MapItem(Entry("https://example.com", "T"), Source, Now);
        Assert.Empty(item.Tags);
    }

    [Theory]
    [InlineData(0)]                       // date_added 缺失/为零
    [InlineData(-11_644_473_600L)]        // date_added="0" → FromChromeTime 产出的负值
    public void MapItem_UnknownOrNegativeBookmarkDate_FallsBackToNow(long bookmarkedAt)
    {
        var item = BookmarkItemFactory.MapItem(Entry("https://example.com", "T", bookmarkedAt: bookmarkedAt), Source, Now);
        Assert.Equal(Now, item.CreatedAt);   // 未知/负时间不得泄漏（否则排到 1601 年）
        Assert.Equal(Now, item.UpdatedAt);
    }

    [Fact]
    public void MapItem_KeepsPositiveBookmarkDate_AndStampsSyncedAtNow()
    {
        const long added = 1_600_000_000L;
        var item = BookmarkItemFactory.MapItem(Entry("https://example.com", "T", bookmarkedAt: added), Source, Now);
        Assert.Equal(added, item.CreatedAt);
        Assert.Equal(added, item.UpdatedAt);
        Assert.Equal(Now, item.SyncedAt);
    }

    [Fact]
    public void MapItem_SubtitleIsHostDomain()
    {
        var item = BookmarkItemFactory.MapItem(Entry("https://doc.rust-lang.org/book/", "Rust"), Source, Now);
        Assert.Equal("doc.rust-lang.org", item.Subtitle);
    }
}
