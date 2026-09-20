#nullable enable
using System;
using Xunit;
using StarMark.Abstractions;
using StarMark.Integrations.Everything;

namespace StarMark.Tests;

/// <summary>
/// 本地路径身份基元单测：确保 Everything 查询侧、CSV 解析侧与「拖入登记」侧对同一路径算出<b>相同</b>的
/// (source, source_id, uri)——这是 items 表 (source, source_id) 唯一索引上去重/合并的唯一依据。
/// </summary>
public sealed class LocalFileIdentityTests
{
    [Fact]
    public void UriForPath_UsesTwoSlashForwardSlashForm()
    {
        Assert.Equal("file://C:/Data/demo/readme.md", LocalFileIdentity.UriForPath(@"C:\Data\demo\readme.md"));
    }

    [Fact]
    public void SourceIdForPath_IsStable_CaseInsensitive_AndFixedLength()
    {
        var a = LocalFileIdentity.SourceIdForPath(@"C:\Data\Demo\Readme.md");
        var b = LocalFileIdentity.SourceIdForPath(@"c:\data\demo\readme.md");
        var again = LocalFileIdentity.SourceIdForPath(@"C:\Data\Demo\Readme.md");
        Assert.Equal(a, b);          // 大小写归一
        Assert.Equal(a, again);      // 稳定
        Assert.Equal(16, a.Length);  // 8 字节 → 16 位十六进制
    }

    [Fact]
    public void SourceIdForPath_DiffersForDifferentPaths()
    {
        Assert.NotEqual(
            LocalFileIdentity.SourceIdForPath(@"C:\a.txt"),
            LocalFileIdentity.SourceIdForPath(@"C:\b.txt"));
    }

    [Fact]
    public void EverythingParseCsvLine_AndDragSide_ShareIdenticalIdentity()
    {
        // Everything CSV：Name,Path → 组合成全路径 C:\Data\demo\readme.md。
        var item = EverythingInterop.ParseCsvLine(
            "readme.md,C:\\Data\\demo,1024,2024-05-01 12:00:00",
            EverythingInterop.RequestFlags.FullPath | EverythingInterop.RequestFlags.Size);
        Assert.NotNull(item);

        // 同一磁盘路径经「拖入登记」侧（LocalFileIdentity）派生的业务键必须一致 → 落库合并为一条、不分裂。
        const string fullPath = @"C:\Data\demo\readme.md";
        Assert.Equal(LocalFileIdentity.SourceIdForPath(fullPath), item!.SourceId);
        Assert.Equal(LocalFileIdentity.UriForPath(fullPath), item.Uri);
        Assert.Equal(ItemSources.FileSystem, item.Source);
    }

    [Fact]
    public void FromPath_BuildsUnpersistedFileItem()
    {
        var item = LocalFileIdentity.FromPath(@"C:\Data\demo\readme.md", "重命名");
        Assert.Equal(0, item.Id);   // 未入库
        Assert.Equal(ItemType.File, item.Type);
        Assert.Equal(ItemSources.FileSystem, item.Source);
        Assert.Equal("重命名", item.Title);
        Assert.Equal(@"C:\Data\demo", item.Subtitle);
        Assert.Equal(LocalFileIdentity.SourceIdForPath(@"C:\Data\demo\readme.md"), item.SourceId);
        Assert.Equal("file://C:/Data/demo/readme.md", item.Uri);
    }

    [Fact]
    public void TryPathFromUri_RoundTripsBothSlashForms()
    {
        // 两斜杠（Everything 侧拼法）
        Assert.True(LocalFileIdentity.TryPathFromUri("file://C:/Data/demo/readme.md", out var p1));
        Assert.Equal(@"C:\Data\demo\readme.md", p1);

        // 三斜杠（DB / 标准 file:/// 拼法）—— 剥多余前导斜杠后须与两斜杠结果一致
        Assert.True(LocalFileIdentity.TryPathFromUri("file:///C:/Data/demo/readme.md", out var p2));
        Assert.Equal(@"C:\Data\demo\readme.md", p2);

        // UriForPath → TryPathFromUri 往返一致（拖出侧依赖此不变式）
        Assert.True(LocalFileIdentity.TryPathFromUri(LocalFileIdentity.UriForPath(@"D:\工作 报告\x.pdf"), out var p3));
        Assert.Equal(@"D:\工作 报告\x.pdf", p3);
    }

    [Fact]
    public void TryPathFromUri_RejectsNonFileOrMissingDrive()
    {
        Assert.False(LocalFileIdentity.TryPathFromUri(null, out _));
        Assert.False(LocalFileIdentity.TryPathFromUri("", out _));
        Assert.False(LocalFileIdentity.TryPathFromUri("https://example.com/x", out _));
        // file:// 但缺盘符（如 UNC 或相对）→ false，不臆造路径
        Assert.False(LocalFileIdentity.TryPathFromUri("file://server/share", out _));
    }
}
