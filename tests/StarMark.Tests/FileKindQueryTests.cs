#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 搜索栏「类型」多选 → Everything 检索式的翻译契约。锁三件事：
/// ① 类别之间的「或」必须挤进单个 ext:（官方唯一示范的多扩展名写法），不赌 (a|b) 括号语法；
/// ② 文件夹与扩展名类别互斥（文件夹没有扩展名，两者同 AND 必然零结果）；
/// ③ 拼出来的表达式只进请求，用户关键词逐字保留。
/// </summary>
public sealed class FileKindQueryTests
{
    private const string PictureExt = "png;jpg;jpeg;gif;bmp;webp;tif;tiff;ico;svg";
    private const string DocumentExt = "doc;docx;xls;xlsx;ppt;pptx;pdf;txt;md;rtf;csv;wps;et;dps;odt;ods;odp";

    [Fact]
    public void NullSelection_ProducesNoFragments() => Assert.Empty(FileKindQuery.Fragments(null));

    [Fact]
    public void EmptySelection_ProducesNoFragments()
        => Assert.Empty(FileKindQuery.Fragments(Array.Empty<FileKind>()));

    [Fact]
    public void SingleCategory_EmitsOneExtTermWithSemicolonList()
    {
        var parts = FileKindQuery.Fragments(new[] { FileKind.Picture });

        // 逐字钉住发给 Everything 的表达式：官方文档的写法是分号列表、不带点、不带空格。
        Assert.Equal(new[] { "ext:" + PictureExt }, parts);
    }

    [Fact]
    public void MultipleCategories_StayInsideOneExtTerm_OrNotAnd()
    {
        // 勾「文档 + 图片」要的是「这一类 OR 那一类」：拆成两个 ext: 词条会被 Everything 判成
        // AND（同一文件不可能既是 docx 又是 png）→ 恒零结果。故必须合成单个 ext: 并集。
        var parts = FileKindQuery.Fragments(new[] { FileKind.Document, FileKind.Picture });

        var ext = Assert.Single(parts);
        Assert.Equal(1, ext.Count(c => c == ':'));
        Assert.EndsWith(PictureExt, ext, StringComparison.Ordinal);
        Assert.Contains("odp;" + PictureExt, ext, StringComparison.Ordinal);
    }

    [Fact]
    public void CategoryOrder_FollowsDeclaration_NotSelectionOrder()
    {
        // 逐字稳定：同一组勾选无论点选顺序如何，产生的检索式相同（可断言、可对比日志）。
        var a = FileKindQuery.Fragments(new[] { FileKind.Music, FileKind.Document });
        var b = FileKindQuery.Fragments(new[] { FileKind.Document, FileKind.Music });

        Assert.Equal(a, b);
        Assert.StartsWith("ext:" + DocumentExt + ";", a[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Folder_Alone_EmitsFolderFunction()
        => Assert.Equal(new[] { "folder:" }, FileKindQuery.Fragments(new[] { FileKind.Folder }));

    [Fact]
    public void FolderWithCategories_SuppressesExtTerm()
    {
        // 文件夹没有扩展名：folder: 与 ext: 同时 AND 必然零结果，故文件夹优先、扩展名类别让位。
        var parts = FileKindQuery.Fragments(new[] { FileKind.Folder, FileKind.Video });

        Assert.DoesNotContain(parts, p => p.StartsWith("ext:", StringComparison.Ordinal));
        Assert.Contains("folder:", parts);
    }

    [Fact]
    public void Modifiers_AndedAfterCategory_InFixedOrder()
    {
        var parts = FileKindQuery.Fragments(new[] { FileKind.Recent, FileKind.Large, FileKind.Archive });

        Assert.Equal(3, parts.Count);
        Assert.StartsWith("ext:", parts[0], StringComparison.Ordinal);
        Assert.Equal("size:>100mb", parts[1]);
        Assert.Equal("dm:7days", parts[2]);
    }

    [Fact]
    public void Modifiers_WithoutCategory_EmitOnlyModifiers()
    {
        Assert.Equal(
            new[] { "size:>100mb", "dm:7days" },
            FileKindQuery.Fragments(new[] { FileKind.Large, FileKind.Recent }));
    }

    [Fact]
    public void DuplicateSelections_DoNotDuplicateExtensions()
    {
        Assert.Equal(
            FileKindQuery.Fragments(new[] { FileKind.Picture }),
            FileKindQuery.Fragments(new[] { FileKind.Picture, FileKind.Picture }));
    }

    [Fact]
    public void Compose_AppendsFragmentsAndKeepsKeywordVerbatim()
    {
        var fragments = FileKindQuery.Fragments(new[] { FileKind.Document, FileKind.Large });

        Assert.Equal(
            "报告 ext:" + DocumentExt + " size:>100mb",
            FileKindQuery.Compose("  报告  ", fragments));
    }

    [Fact]
    public void Compose_EmptyKeyword_StillCarriesFragments()
    {
        // 只勾类型、不打字：也要能浏览（Everything 收到纯检索式词条）。
        var folder = FileKindQuery.Fragments(new[] { FileKind.Folder });
        Assert.Equal("folder:", FileKindQuery.Compose(null, folder));
        Assert.Equal("folder:", FileKindQuery.Compose("   ", folder));
    }

    [Fact]
    public void Compose_NoFragments_ReturnsKeywordUntouched()
    {
        // 关键：用户手敲的 Everything 语法必须原样直通（不得被改写/加引号）。
        Assert.Equal("ext:cpp;h  ", FileKindQuery.Compose("ext:cpp;h  ", null));
        Assert.Equal("a|b", FileKindQuery.Compose("a|b", Array.Empty<string>()));
        Assert.Equal("x", FileKindQuery.Compose("x", new[] { "", "  " }));
        Assert.Equal(string.Empty, FileKindQuery.Compose(null, null));
    }
}
