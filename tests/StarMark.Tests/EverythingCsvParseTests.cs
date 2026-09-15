#nullable enable
using Xunit;
using StarMark.Abstractions;
using StarMark.Integrations.Everything;

namespace StarMark.Tests;

/// <summary>
/// Everything CLI CSV 解析单元测试（EverythingInterop.ParseCsvLine，internal，经 InternalsVisibleTo 暴露）。
/// CSV 格式：name,path[,size][,date_modified]，以逗号分隔。
/// </summary>
public sealed class EverythingCsvParseTests
{
    private static readonly EverythingInterop.RequestFlags Flags =
        EverythingInterop.RequestFlags.FileName | EverythingInterop.RequestFlags.Path |
        EverythingInterop.RequestFlags.Size | EverythingInterop.RequestFlags.DateModified;

    [Fact]
    public void ParseCsvLine_NamePathSizeDate()
    {
        var item = EverythingInterop.ParseCsvLine("readme.md,C:\\Data\\demo,1024,2024-05-01 12:00:00", Flags);
        Assert.NotNull(item);
        Assert.Equal(ItemType.File, item!.Type);
        Assert.Equal("readme.md", item.Title);
        Assert.Equal("C:\\Data\\demo", item.Subtitle);
        Assert.Equal(1024, item.FileSize);
        Assert.Equal("file://C:/Data/demo/readme.md", item.Uri);
    }

    [Fact]
    public void ParseCsvLine_QuotedFilenameWithComma()
    {
        var item = EverythingInterop.ParseCsvLine("\"my, file.txt\",C:\\Data,0", Flags);
        Assert.NotNull(item);
        Assert.Equal("my, file.txt", item!.Title);
        Assert.Equal("C:\\Data", item.Subtitle);
        Assert.Equal("file://C:/Data/my, file.txt", item.Uri);
    }

    [Fact]
    public void ParseCsvLine_QuotesAreStrippedFromField()
    {
        var item = EverythingInterop.ParseCsvLine("\"a simple name\",C:\\Data,0", Flags);
        Assert.NotNull(item);
        Assert.Equal("a simple name", item!.Title);
    }

    [Fact]
    public void ParseCsvLine_PathAndNameCombined()
    {
        var item = EverythingInterop.ParseCsvLine("report.pdf,D:\\Docs,0", Flags);
        Assert.NotNull(item);
        Assert.Equal("file://D:/Docs/report.pdf", item!.Uri);
    }

    [Fact]
    public void ParseCsvLine_WithoutSizeFlag_LeavesSizeNull()
    {
        var flags = EverythingInterop.RequestFlags.FileName | EverythingInterop.RequestFlags.Path;
        var item = EverythingInterop.ParseCsvLine("a.txt,C:\\x", flags);
        Assert.NotNull(item);
        Assert.Null(item!.FileSize);
    }

    [Fact]
    public void ParseCsvLine_DateParsesToUnixSeconds()
    {
        var localDt = DateTime.Parse("2024-05-01 12:00:00");
        var expected = new DateTimeOffset(localDt).ToUniversalTime().ToUnixTimeSeconds();

        var item = EverythingInterop.ParseCsvLine("x.exe,C:\\x,0,2024-05-01 12:00:00", Flags);
        Assert.NotNull(item);
        Assert.Equal(expected, item!.CreatedAt);
        Assert.Equal(expected, item.UpdatedAt);
    }

    [Fact]
    public void ParseCsvLine_EmptyLine_ReturnsNull()
    {
        Assert.Null(EverythingInterop.ParseCsvLine("", Flags));
    }
}