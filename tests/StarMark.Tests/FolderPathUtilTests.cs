#nullable enable
using System.Text.Json;
using Xunit;
using StarMark.Abstractions;

namespace StarMark.Tests;

/// <summary>
/// 文件夹树分组路径纯函数测试（FolderPathUtil）：
/// 书签层级 / 文件目录 / GitHub / 剪贴板伪根 / 兜底分组。
/// </summary>
public sealed class FolderPathUtilTests
{
    [Fact]
    public void Bookmark_NestedFolderPath_SplitsSegments()
    {
        var item = new Item
        {
            Type = ItemType.Bookmark,
            ExtraJson = JsonSerializer.Serialize(new BookmarkMeta
            {
                FolderPaths = new List<string> { "书签栏/技术/AI" },
            }),
        };
        Assert.Equal(new[] { "书签栏", "技术", "AI" }, FolderPathUtil.BookmarkSegments(item));
    }

    [Fact]
    public void Bookmark_NoMeta_FallsBackToOtherBookmarks()
    {
        var item = new Item { Type = ItemType.Bookmark };
        Assert.Equal(new[] { "其他书签" }, FolderPathUtil.BookmarkSegments(item));
    }

    [Fact]
    public void Bookmark_CorruptJson_FallsBackToOtherBookmarks()
    {
        var item = new Item { Type = ItemType.Bookmark, ExtraJson = "{not json" };
        Assert.Equal(new[] { "其他书签" }, FolderPathUtil.BookmarkSegments(item));
    }

    [Fact]
    public void Bookmark_EmptyFolderPath_FallsBackToOtherBookmarks()
    {
        var item = new Item
        {
            Type = ItemType.Bookmark,
            ExtraJson = JsonSerializer.Serialize(new BookmarkMeta { FolderPaths = new List<string> { "///" } }),
        };
        Assert.Equal(new[] { "其他书签" }, FolderPathUtil.BookmarkSegments(item));
    }

    [Fact]
    public void File_Uri_DirectorySegmentsExcludeFileName()
    {
        var item = new Item { Type = ItemType.File, Uri = "file:///C:/a/b/file.txt" };
        // Uri.LocalPath → C:\a\b\file.txt；目录层级去掉文件名 → 盘符根 + 两级目录
        Assert.Equal(new[] { "C:", "a", "b" }, FolderPathUtil.FileSegments(item));
    }

    [Fact]
    public void File_AtDriveRoot_ReturnsDriveAsRootNode()
    {
        var item = new Item { Type = ItemType.File, Uri = "file:///C:/root.md" };
        Assert.Equal(new[] { "C:" }, FolderPathUtil.FileSegments(item));
    }

    [Fact]
    public void File_NonFileUri_FallsBack()
    {
        var item = new Item { Type = ItemType.File, Uri = "https://example.com/x.txt" };
        Assert.Equal(new[] { "其他文件" }, FolderPathUtil.FileSegments(item));
    }

    [Fact]
    public void GitHubStar_PseudoRoot()
    {
        Assert.Equal(new[] { "⭐ GitHub Stars" }, FolderPathUtil.GetSegments(new Item { Type = ItemType.GitHubStar }));
    }

    [Fact]
    public void Clipboard_PseudoRoot()
    {
        Assert.Equal(new[] { "📋 剪贴板" }, FolderPathUtil.GetSegments(new Item { Type = ItemType.Clipboard }));
    }

    [Fact]
    public void File_NoUri_FallsBackToOtherFiles()
    {
        Assert.Equal(new[] { "其他文件" }, FolderPathUtil.GetSegments(new Item { Type = ItemType.File }));
    }
}