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
    public async Task FetchAsync_IsAlwaysEmpty_BecauseTheBulkIndexingArmWasRemoved()
    {
        // 批次 VQ：「索引进库」整条拆掉（那颗按钮只落盘配置、真进库要等顶栏同步、四条失败出口全都无声，
        // 而同步完无条件弹一句「索引同步完成」）。本源现在只服务实时搜索，FetchAsync 恒空。
        // 这条断言是 ItemCardPolicy.CanDeletePermanently 放开文件行的前提：删了不会被同步拉回来。
        var source = new EverythingSource(new EverythingQueryQueue(), new FileIndexOptions { Enabled = true });
        Assert.Empty(await source.FetchAsync(new SyncContext(), CancellationToken.None));
    }

    [Fact]
    public void SourceId_IsFileSystem()
    {
        var source = new EverythingSource(new EverythingQueryQueue(), new FileIndexOptions());
        Assert.Equal(ItemSources.FileSystem, source.SourceId);
        Assert.Equal("本地文件 (Everything)", source.DisplayName);
    }

    /// <summary>
    /// 默认关闸门契约（本地磁盘搜索「opt-in / 0 内存」护栏）：
    /// <c>Enabled=false</c> 时 <c>IsAvailable</c> 的 <c>&amp;&amp;</c> 在调用原生 <c>EverythingInterop.IsRunning()</c>
    /// 之前即短路返回 false。⇒ 关态可确定性判定、绝不触碰原生窗口探测，统一搜索据此跳过本源。
    /// 这正是批次 FL 设置开关所翻动的那个位——锁死以防未来把短路写成两段求值或改了默认值而静默破功。
    /// </summary>
    [Fact]
    public void IsAvailable_IsFalse_WhenDisabled_ShortCircuitsBeforeNative()
    {
        var source = new EverythingSource(new EverythingQueryQueue(), new FileIndexOptions { Enabled = false });
        Assert.False(source.IsAvailable);
    }

    /// <summary>
    /// 关态不发查询契约：<c>Enabled=false</c> 时 <c>SearchAsync</c> 走 <c>!IsAvailable</c> 早退分支，
    /// 同步返回空、绝不发起任何 Everything IPC（同样因短路而不触原生）。保障默认关「零打扰」——
    /// 用户未开启本地磁盘搜索时，逐按键搜索不会向 Everything 发任何请求。
    /// </summary>
    [Fact]
    public async Task SearchAsync_ReturnsEmpty_WhenDisabled_WithoutQuerying()
    {
        var source = new EverythingSource(new EverythingQueryQueue(), new FileIndexOptions { Enabled = false });
        var items = await source.SearchAsync("anything", new SearchFilter(), CancellationToken.None);
        Assert.Empty(items);
    }
}
