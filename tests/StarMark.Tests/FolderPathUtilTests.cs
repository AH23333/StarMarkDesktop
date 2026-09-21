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
    public void Bookmark_MultiLevelPathArray_EachElementIsLevel()
    {
        var item = new Item
        {
            Type = ItemType.Bookmark,
            ExtraJson = JsonSerializer.Serialize(new BookmarkMeta
            {
                FolderPaths = new List<string> { "书签栏", "技术", "AI" },
            }),
        };
        Assert.Equal(new[] { "书签栏", "技术", "AI" }, FolderPathUtil.BookmarkSegments(item));
    }

    [Fact]
    public void Bookmark_SlashInFolderName_IsNotSplit()
    {
        var item = new Item
        {
            Type = ItemType.Bookmark,
            ExtraJson = JsonSerializer.Serialize(new BookmarkMeta
            {
                FolderPaths = new List<string> { "AI/LLM 资料" },
            }),
        };
        Assert.Equal(new[] { "AI/LLM 资料" }, FolderPathUtil.BookmarkSegments(item));
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
    public void File_FolderNameWithHash_KeepsFullHierarchy()
    {
        // 回归：Uri.LocalPath 在 '#' 处截断。旧实现下 D:\C#项目\x.txt → LocalPath=D:\C
        // → 只剩 [D:]，丢掉整层文件夹。TryPathFromUri 保留 '#'，须还原完整目录层级。
        var item = new Item { Type = ItemType.File, Uri = "file:///D:/C#项目/x.txt" };
        Assert.Equal(new[] { "D:", "C#项目" }, FolderPathUtil.FileSegments(item));

        // 文件名（非目录）含 '#'：目录层级不含该名字，仍应正确（截断只会削掉文件名，不影响此处断言）。
        var fileNamed = new Item { Type = ItemType.File, Uri = "file:///D:/docs/C#入门.docx" };
        Assert.Equal(new[] { "D:", "docs" }, FolderPathUtil.FileSegments(fileNamed));
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

    // ───────── DS：未覆盖分支 / 未断言输出值（纯函数契约护栏；重构即回退）─────────

    [Fact]
    public void GetSegments_TodoAndNoteType_FallsBackToOther()
    {
        // switch 默认臂（:27 _ => OtherGroup）此前从未被进入——既有仅钉 Bookmark/File/GitHub/Clipboard 四臂。
        // Todo/Note 是统一条目模型下的一等类型，无外部来源分组，须落到单一「其他」伪根。
        Assert.Equal(new[] { "其他" }, FolderPathUtil.GetSegments(new Item { Type = ItemType.Todo }));
        Assert.Equal(new[] { "其他" }, FolderPathUtil.GetSegments(new Item { Type = ItemType.Note }));
    }

    [Fact]
    public void FileSegments_UppercaseFileScheme_StillParsesToHierarchy()
    {
        // :52 用 OrdinalIgnoreCase 匹配 file:// 前缀；既有测全为小写，把它换成大小写敏感即静默回归到「其他文件」。
        // 与 DJ（LocalFileIdentity 内层 :48）是不同函数、不同行——DJ 全绿仍漏此调用点的回归。
        // 仅断言文化无关结果（ASCII 大小写折叠），不绑定宿主 CurrentCulture/ICU。
        var item = new Item { Type = ItemType.File, Uri = "FILE:///C:/a/b/file.txt" };
        Assert.Equal(new[] { "C:", "a", "b" }, FolderPathUtil.FileSegments(item));
    }

    [Fact]
    public void FileSegments_BareDriveRoot_FallsBackToOtherFiles()
    {
        // TryPathFromUri 认 "C:" 为合法本地路径（返 true），但 FileSegments 的 segs.Length>=2 门拒单段——
        // 否则丢「文件名」后目录层级为空数组，会破坏 GetSegments 的非空不变式。此 false 落空臂此前未被覆盖。
        var item = new Item { Type = ItemType.File, Uri = "file:///C:" };
        Assert.Equal(new[] { "其他文件" }, FolderPathUtil.FileSegments(item));
    }

    [Fact]
    public void BookmarkSegments_EmptyElementDropped_SiblingsSurvive()
    {
        // Where 双合取（!IsNullOrEmpty && Trim('/').Length>0）对「混合」数组逐元素剔除：空串删、其余层级留。
        // 既有仅覆盖「全删→兜底」与「全留→返回」，从未断言部分删除后的 ["技术","AI"] 这一独立输出值。
        var item = new Item
        {
            Type = ItemType.Bookmark,
            ExtraJson = JsonSerializer.Serialize(new BookmarkMeta
            {
                FolderPaths = new List<string> { "技术", "", "AI" },
            }),
        };
        Assert.Equal(new[] { "技术", "AI" }, FolderPathUtil.BookmarkSegments(item));
    }
}