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
    /// <summary>
    /// 这些用例只测字符串分层，一律告诉政策层"盘上什么都没有"
    /// （＝两格都不在时按原始那格分组，与批次 VR 之前的行为逐字一致）。真实磁盘的选择见文件末尾那两格。
    /// </summary>
    private static bool Nowhere(string? _) => false;
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
        Assert.Equal(new[] { "C:", "a", "b" }, FolderPathUtil.FileSegments(item, Nowhere));
    }

    [Fact]
    public void File_AtDriveRoot_ReturnsDriveAsRootNode()
    {
        var item = new Item { Type = ItemType.File, Uri = "file:///C:/root.md" };
        Assert.Equal(new[] { "C:" }, FolderPathUtil.FileSegments(item, Nowhere));
    }

    [Fact]
    public void File_NonFileUri_FallsBack()
    {
        var item = new Item { Type = ItemType.File, Uri = "https://example.com/x.txt" };
        Assert.Equal(new[] { "其他文件" }, FolderPathUtil.FileSegments(item, Nowhere));
    }

    [Fact]
    public void File_FolderNameWithHash_KeepsFullHierarchy()
    {
        // 回归：Uri.LocalPath 在 '#' 处截断。旧实现下 D:\C#项目\x.txt → LocalPath=D:\C
        // → 只剩 [D:]，丢掉整层文件夹。TryPathFromUri 保留 '#'，须还原完整目录层级。
        var item = new Item { Type = ItemType.File, Uri = "file:///D:/C#项目/x.txt" };
        Assert.Equal(new[] { "D:", "C#项目" }, FolderPathUtil.FileSegments(item, Nowhere));

        // 文件名（非目录）含 '#'：目录层级不含该名字，仍应正确（截断只会削掉文件名，不影响此处断言）。
        var fileNamed = new Item { Type = ItemType.File, Uri = "file:///D:/docs/C#入门.docx" };
        Assert.Equal(new[] { "D:", "docs" }, FolderPathUtil.FileSegments(fileNamed, Nowhere));
    }

    [Fact]
    public void GitHubStar_PseudoRoot()
    {
        Assert.Equal(new[] { "⭐ GitHub Stars" }, FolderPathUtil.GetSegments(new Item { Type = ItemType.GitHubStar }, Nowhere));
    }

    [Fact]
    public void Clipboard_PseudoRoot()
    {
        Assert.Equal(new[] { "📋 剪贴板" }, FolderPathUtil.GetSegments(new Item { Type = ItemType.Clipboard }, Nowhere));
    }

    [Fact]
    public void File_NoUri_FallsBackToOtherFiles()
    {
        Assert.Equal(new[] { "其他文件" }, FolderPathUtil.GetSegments(new Item { Type = ItemType.File }, Nowhere));
    }

    // ───────── DS：未覆盖分支 / 未断言输出值（纯函数契约护栏；重构即回退）─────────

    [Fact]
    public void GetSegments_TodoAndNoteType_FallsBackToOther()
    {
        // switch 默认臂（:27 _ => OtherGroup）此前从未被进入——既有仅钉 Bookmark/File/GitHub/Clipboard 四臂。
        // Todo/Note 是统一条目模型下的一等类型，无外部来源分组，须落到单一「其他」伪根。
        Assert.Equal(new[] { "其他" }, FolderPathUtil.GetSegments(new Item { Type = ItemType.Todo }, Nowhere));
        Assert.Equal(new[] { "其他" }, FolderPathUtil.GetSegments(new Item { Type = ItemType.Note }, Nowhere));
    }

    [Fact]
    public void FileSegments_UppercaseFileScheme_StillParsesToHierarchy()
    {
        // :52 用 OrdinalIgnoreCase 匹配 file:// 前缀；既有测全为小写，把它换成大小写敏感即静默回归到「其他文件」。
        // 与 DJ（LocalFileIdentity 内层 :48）是不同函数、不同行——DJ 全绿仍漏此调用点的回归。
        // 仅断言文化无关结果（ASCII 大小写折叠），不绑定宿主 CurrentCulture/ICU。
        var item = new Item { Type = ItemType.File, Uri = "FILE:///C:/a/b/file.txt" };
        Assert.Equal(new[] { "C:", "a", "b" }, FolderPathUtil.FileSegments(item, Nowhere));
    }

    [Fact]
    public void FileSegments_BareDriveRoot_FallsBackToOtherFiles()
    {
        // TryPathFromUri 认 "C:" 为合法本地路径（返 true），但 FileSegments 的 segs.Length>=2 门拒单段——
        // 否则丢「文件名」后目录层级为空数组，会破坏 GetSegments 的非空不变式。此 false 落空臂此前未被覆盖。
        var item = new Item { Type = ItemType.File, Uri = "file:///C:" };
        Assert.Equal(new[] { "其他文件" }, FolderPathUtil.FileSegments(item, Nowhere));
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

    // ───────── VR：编码态 URI 的文件夹名到底印哪一格（真临时目录，不碰用户文件）─────────

    [Fact]
    public void FileSegments_EncodedUriOnDisk_GroupsByTheHumanName()
    {
        // 现场：用户库里有一条历史行 file:///D:/Visual%20Studio%20Code/…，树上就把文件夹印成「Visual%20Studio%20Code」。
        // 政策层现在必须先问磁盘——解码那格真在盘上，就用它分组；这样键与显示同源，也不会"解开了名字、点进去又找不到"。
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StarMarkVrTree", "工作 报告");
        var file = System.IO.Path.Combine(dir, "a.md");
        var sandbox = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StarMarkVrTree");
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(file, string.Empty);   // 盘上要有这条本身：判据问的是"这一格在不在"，不是它的父目录
            var item = new Item
            {
                Type = ItemType.File,
                Uri = new System.Uri(file).AbsoluteUri,   // percent 编码态（含中文与空格）
            };
            var segs = FolderPathUtil.FileSegments(item, static p =>
                System.IO.File.Exists(p) || System.IO.Directory.Exists(p));
            Assert.Contains("工作 报告", segs);                       // 真名字
            Assert.DoesNotContain(segs, s => s.Contains('%'));       // 不许把 %E5%B7%A5 这类字面串印给用户
        }
        finally
        {
            try { System.IO.Directory.Delete(sandbox, true); }
            catch (Exception) { /* 取证临时目录清不掉不影响结论（测试自己的沙盒） */ }
        }
    }

    [Fact]
    public void FileSegments_EncodedUriNotOnDisk_KeepsTheStoredForm()
    {
        // 两格都不在盘上时<b>不许凭空造一个名字</b>：仍按库里的原样那格分组（与 VR 之前逐字一致）。
        // 这一格是"为什么不直接 Unescape"的证人——真名叫 100%20.txt 的文件被解开会指到另一个名字上。
        var item = new Item { Type = ItemType.File, Uri = "file:///Q:/Never%20Here/x.md" };
        Assert.Equal(new[] { "Q:", "Never%20Here" },
            FolderPathUtil.FileSegments(item, static p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p)));
    }
}