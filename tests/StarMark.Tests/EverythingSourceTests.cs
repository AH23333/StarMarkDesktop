#nullable enable
using System.Security.Cryptography;
using System.Text;
using StarMark.Abstractions;
using StarMark.Integrations.Everything;
using Xunit;

namespace StarMark.Tests;

/// <summary>P0-1b Everything 入库：映射契约与空根行为。</summary>
public class EverythingSourceTests
{
    private static string StableHash(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path.ToLowerInvariant());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 8);
    }

    [Fact]
    public void ParseCsvLine_MapsFileItem_WithStableSourceId()
    {
        // Everything -csv 数据行：Name,Path,Size,Date Modified,Date Created
        var line = "report.pdf,C:\\Users\\me\\Docs,1234,2023-01-02 03:04:05,2023-01-01 00:00:00";
        var item = EverythingInterop.ParseCsvLine(
            line, EverythingInterop.RequestFlags.FullPath | EverythingInterop.RequestFlags.Size | EverythingInterop.RequestFlags.DateModified);

        Assert.NotNull(item);
        Assert.Equal(ItemType.File, item!.Type);
        Assert.Equal(ItemSources.FileSystem, item.Source);

        // source_id = SHA256(path.ToLowerInvariant()) 前 8 字节 hex（文档 §5.1 幂等契约）
        var fullPath = "C:\\Users\\me\\Docs\\report.pdf";
        Assert.Equal(StableHash(fullPath), item.SourceId);
        Assert.Equal(16, item.SourceId.Length); // 8 字节 = 16 hex 字符
        Assert.True(item.SourceId.All(c => Uri.IsHexDigit(c)), "source_id 应为十六进制");

        Assert.Equal("report.pdf", item.Title);
        Assert.Equal("C:\\Users\\me\\Docs", item.Subtitle);
        Assert.Equal("file://C:/Users/me/Docs/report.pdf", item.Uri);
        Assert.Equal(1234L, item.FileSize);
    }

    [Fact]
    public void ParseCsvLine_SamePath_ProducesSameSourceId_Idempotent()
    {
        var a = EverythingInterop.ParseCsvLine("a.txt,C:\\X,0,2023-01-01 00:00:00,", EverythingInterop.RequestFlags.FullPath);
        var b = EverythingInterop.ParseCsvLine("a.txt,C:\\X,0,2024-02-02 00:00:00,", EverythingInterop.RequestFlags.FullPath);
        Assert.NotNull(a);
        Assert.NotNull(b);
        // 仅日期不同，路径相同 → source_id 应一致（幂等入库的前提）
        Assert.Equal(a!.SourceId, b!.SourceId);
    }

    [Fact]
    public async Task FetchAsync_ReturnsEmpty_ForNonexistentRoots()
    {
        // 根目录不存在时无论 Everything 是否运行都应返回空（不抛异常、不入库）。
        var options = new FileIndexOptions { Roots = new[] { @"Z:\__starmark_nonexistent_root__\xyz" } };
        var source = new EverythingSource(new EverythingQueryQueue(), options);

        var items = await source.FetchAsync(new SyncContext(), CancellationToken.None);

        Assert.Empty(items);
    }

    [Fact]
    public void SourceId_IsFileSystem()
    {
        var source = new EverythingSource(new EverythingQueryQueue(), new FileIndexOptions());
        Assert.Equal(ItemSources.FileSystem, source.SourceId);
        Assert.Equal("本地文件 (Everything)", source.DisplayName);
    }
}
