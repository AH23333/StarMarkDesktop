#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.I18n;
using StarMark.Core.Ai;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// §20.1 计量在 wire 层的读取口径。<b>钉的不是"能不能解析 JSON"，是三类语义分岔</b>：
/// "服务没回计量"（null→上层转估算）与"服务说用了 0"（真实入账）必须是两个可区分的值；
/// 字符串形态的数字不收，是因为 usage 没有哪家写成字符串——收了就是把"字段其实是别的东西"读成用量。
/// </summary>
public sealed class AiUsageWireTests
{
    [Fact]
    public void ReadOllama_PicksEvalCounts()
    {
        var usage = AiWire.ReadOllamaUsage("""{"message":{"content":"好"},"prompt_eval_count":213,"eval_count":45}""");
        Assert.NotNull(usage);
        Assert.Equal(213, usage!.InputTokens);
        Assert.Equal(45, usage.OutputTokens);
        Assert.False(usage.Estimated);
        Assert.Equal(258, usage.TotalTokens);
    }

    [Fact]
    public void ReadOllama_MissingEitherSide_IsNull_NotZero()
    {
        // 只回一半：宁可整体按"没回计量"处理，也不要"一半实测一半 0"的假账
        Assert.Null(AiWire.ReadOllamaUsage("""{"prompt_eval_count":10}"""));
        Assert.Null(AiWire.ReadOllamaUsage("""{"eval_count":10}"""));
        Assert.Null(AiWire.ReadOllamaUsage(null));
        Assert.Null(AiWire.ReadOllamaUsage("这不是JSON"));
    }

    [Fact]
    public void ReadOllama_ZeroZero_IsRealUsage()
    {
        var usage = AiWire.ReadOllamaUsage("""{"prompt_eval_count":0,"eval_count":0}""");
        Assert.NotNull(usage);                       // "回了 0"与"没回"不是一回事（前者入账、后者转估算）
        Assert.Equal(0, usage!.TotalTokens);
    }

    [Fact]
    public void ReadOllama_StringNumber_AndNegative_AreBothRejected()
    {
        Assert.Null(AiWire.ReadOllamaUsage("""{"prompt_eval_count":"21","eval_count":5}"""));   // 字符串不收
        Assert.Null(AiWire.ReadOllamaUsage("""{"prompt_eval_count":-3,"eval_count":5}"""));      // 负数不收
    }

    [Fact]
    public void ReadOpenAi_StandardShape()
    {
        var usage = AiWire.ReadOpenAiUsage("""{"choices":[{"message":{"content":"ok"}}],"usage":{"prompt_tokens":4021,"completion_tokens":498}}""");
        Assert.NotNull(usage);
        Assert.Equal(4021, usage!.InputTokens);
        Assert.Equal(498, usage.OutputTokens);
        Assert.False(usage.Estimated);
    }

    [Fact]
    public void ReadOpenAi_UsageAbsentIsNotFailure()
    {
        // 有正文没有 usage：正文照收、计量走估算兜底——两件事互不拦路（provider 侧的 ?? 接线，见 AiProviders）
        Assert.Null(AiWire.ReadOpenAiUsage("""{"choices":[{"message":{"content":"ok"}}]}"""));
    }

    [Fact]
    public void Estimate_CjkCountsOneLatinFour()
    {
        Assert.Equal(4, AiWire.EstimateTokens("前端开发"));                      // 4 个汉字 = 4
        Assert.Equal(2, AiWire.EstimateTokens("12345678"));                       // 8 字符 → ⌈8/4⌉ = 2
        Assert.Equal(2, AiWire.EstimateTokens("abc de"));                          // 6 字符 → ⌈6/4⌉ = 2
    }

    [Fact]
    public void Estimate_SmallRemainderRoundsUp()
    {
        Assert.Equal(1, AiWire.EstimateTokens("ab"));     // 2 字符 → ⌈2/4⌉ = 1（非 CJK 一律向上取整，宁可估多）
        Assert.Equal(2, AiWire.EstimateTokens(". , !"));  // ". , !" 含两空格共 5 字符 → ⌈5/4⌉ = 2；标点和空格都按非 CJK 计
        Assert.Equal(0, AiWire.EstimateTokens(""));
        Assert.Equal(0, AiWire.EstimateTokens(null));
    }

    [Fact]
    public void Approximate_IsMarkedEstimated()
    {
        var usage = AiWire.Approximate("给这批条目标标签", "ok");
        Assert.True(usage.Estimated);            // 折算出来的数必须自带"估算"身份
        Assert.Equal(8, usage.InputTokens);      // 8 个汉字，逐字=1——口径钉死在此：谁改折算规则谁先看这条红
        Assert.Equal(1, usage.OutputTokens);     // "ok" 2 字符 → ⌈2/4⌉ = 1
    }
}

/// <summary>Runner 的接线：<b>账随答复走</b>——成功批透传 usage，失败批一个数字都不造。</summary>
public sealed class AiUsageRunnerTests
{
    private static ClassifyItem Item(long id, string title) => new(id, title, null, null, "github", Array.Empty<string>());

    [Fact]
    public async Task SuccessBatch_ForwardsUsage()
    {
        var items = new[] { Item(1, "dotnet/runtime"), Item(2, "power Toys") };
        var reply = AiReply.Success("""{"items":[{"id":1,"tags":["开发"]},{"id":2,"tags":["开发"]}]}""",
            new AiUsage(1234, 56, Estimated: false));

        var report = await ClassifyRunner.RunAsync(items, new[] { "开发" },
            _ => Task.FromResult(reply), null, CancellationToken.None);

        Assert.NotNull(report.Batches[0].Usage);
        Assert.Equal(1234, report.Batches[0].Usage!.InputTokens);
    }

    [Fact]
    public async Task FailedBatch_HasNoUsage()
    {
        var items = new[] { Item(1, "a") };
        var report = await ClassifyRunner.RunAsync(items, Array.Empty<string>(),
            _ => Task.FromResult(AiReply.Fail(AiFailureKind.TimedOut, LocMessage.Of("ai.transport.waitedOut"))),
                null, CancellationToken.None);

        Assert.False(report.Batches[0].Ok);
        Assert.Null(report.Batches[0].Usage);   // 没成的批不猜消耗
    }
}

/// <summary>账本仓储：临时库真建表真读写（MigrationRunner 的幂等建表顺带被钉——老库升级全靠它）。</summary>
public sealed class AiUsageRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AiUsageRepository _repo;

    public AiUsageRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_usage_{Guid.NewGuid():N}.db");
        var factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(factory).EnsureSchema();
        _repo = new AiUsageRepository(factory);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    private static AiUsageEntry Entry(int daysAgo, string feature, string? model, int input, int output, bool estimated = false)
        => new(DateTimeOffset.UtcNow.AddDays(-daysAgo), feature, "ollama", model, new AiUsage(input, output, estimated));

    [Fact]
    public async Task Log_ThenTotals_RoundTrip()
    {
        await _repo.LogAsync(Entry(0, "classify", "qwen2.5:7b", 100, 20));
        await _repo.LogAsync(Entry(1, "classify", null, 50, 10, estimated: true));
        await _repo.LogAsync(Entry(2, "other", "llama3", 7, 3));

        var totals = await _repo.TotalsSinceAsync(0);
        Assert.Equal(3, totals.Calls);
        Assert.Equal(157, (await _repo.TotalsSinceAsync(0)).InputTokens);
        Assert.Equal(33, totals.OutputTokens);
        Assert.Equal(1, totals.EstimatedCalls);
        Assert.Equal(190, totals.TotalTokens);
    }

    [Fact]
    public async Task Window_CutsOffOldRows()
    {
        await _repo.LogAsync(Entry(5, "classify", "m", 10, 1));
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        Assert.Equal(0, (await _repo.TotalsSinceAsync(cutoff)).Calls);   // 5 天前 → 近 1 天窗口外
        Assert.Equal(11, (await _repo.TotalsSinceAsync(0)).TotalTokens);
    }

    [Fact]
    public async Task ByFeature_AndByModel_GroupAndOrder()
    {
        await _repo.LogAsync(Entry(0, "classify", "big", 100, 10));
        await _repo.LogAsync(Entry(0, "classify", "small", 10, 5));
        await _repo.LogAsync(Entry(0, "search", "small", 5, 5));
        await _repo.LogAsync(Entry(0, "classify", null, 1, 1));

        var features = await _repo.ByFeatureSinceAsync(0);
        Assert.Equal("classify", features[0].Name);          // 消耗大的排前
        Assert.Equal(3, features[0].Calls);
        Assert.Equal("search", features[1].Name);

        var models = await _repo.ByModelSinceAsync(0);
        Assert.Contains(models, m => m.Name == "big");
        Assert.Contains(models, m => m.Name == string.Empty);   // 没记模型的行归空名切片，不悄悄丢行

        // EstimatedOnly：全组都是估算才算"估算组"
        await _repo.LogAsync(Entry(0, "probe", "est", 1, 1, estimated: true));
        var estModel = (await _repo.ByModelSinceAsync(0)).Single(m => m.Name == "est");
        Assert.True(estModel.EstimatedOnly);
        Assert.False((await _repo.ByModelSinceAsync(0)).Single(m => m.Name == "big").EstimatedOnly);
    }

    [Fact]
    public async Task MaxRecorded_EmptyThenValue()
    {
        Assert.Null(await _repo.MaxRecordedAtAsync());
        await _repo.LogAsync(Entry(0, "classify", "m", 2, 1));
        Assert.NotNull(await _repo.MaxRecordedAtAsync());
    }

    [Fact]
    public async Task Recent_NewestFirst_LimitedAndRoundTrips()
    {
        for (var i = 0; i < 5; i++)
            await _repo.LogAsync(Entry(i, i == 3 ? "instant" : "classify", i % 2 == 0 ? "m" : null,
                10 + i, 2, estimated: i == 3));

        var top3 = await _repo.RecentAsync(3);
        Assert.Equal(3, top3.Count);                        // 上限生效
        for (var i = 1; i < top3.Count; i++)
            Assert.True(top3[i - 1].At >= top3[i].At);      // 新→旧

        var all = await _repo.RecentAsync(10);
        Assert.Equal(5, all.Count);
        var newest = all[0];                                // 0 天前那条整圈往返
        Assert.Equal("classify", newest.Feature);
        Assert.Equal("ollama", newest.Provider);
        Assert.Equal("m", newest.Model);
        Assert.False(newest.Usage.Estimated);
        Assert.Equal(10, newest.Usage.InputTokens);
        Assert.Null(all.First(e => e.Model is null).Model);          // model 没记的行照读成 null，不悄悄丢行
        Assert.Equal("instant", all.Single(e => e.Usage.Estimated).Feature);   // 估算位往返
    }

    [Fact]
    public async Task ByDay_GroupsIntoLocalDayStringsAscending()
    {
        // 同一时刻的两行＝同一个本地日，保证不跨午夜；第三行隔整 3 天＝另一个本地日
        var now = DateTimeOffset.Now;
        await _repo.LogAsync(new AiUsageEntry(now, "classify", "ollama", "m", new AiUsage(10, 2, false)));
        await _repo.LogAsync(new AiUsageEntry(now, "instant", "ollama", "m", new AiUsage(5, 5, false)));
        await _repo.LogAsync(new AiUsageEntry(now.AddDays(-3), "classify", "ollama", "m", new AiUsage(7, 3, false)));

        var days = await _repo.ByDaySinceAsync(0);
        Assert.Equal(2, days.Count);                                        // 分组不丢行：3 行并成 2 天
        Assert.Equal(1, days[0].Calls);
        Assert.Equal(10, days[0].TotalTokens);                              // 3 天前那天
        Assert.Equal(2, days[1].Calls);                                     // 同日的两行合一天
        Assert.Equal(22, days[1].TotalTokens);
        Assert.True(string.CompareOrdinal(days[0].Day, days[1].Day) < 0);   // 升序
        Assert.All(days, d => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", d.Day));   // 本地日字符串
    }

    [Fact]
    public async Task Log_OnMissingTable_SwallowsAndKeepsGoing()
    {
        // 账写不上也绝不拦功能（§20.1 写侧纪律）：拿一个没建表的库来验证"不抛"
        var orphan = Path.Combine(Path.GetTempPath(), $"starmark_usage_orphan_{Guid.NewGuid():N}.db");
        try
        {
            var bare = new AiUsageRepository(new DbConnectionFactory(orphan));
            await bare.LogAsync(Entry(0, "classify", "m", 1, 1));   // 表不存在 → 内部吞掉，不抛
        }
        finally
        {
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(orphan); } catch { }
        }
    }
}

/// <summary>按天趋势的铺日纯函数：末日锚定、缺日补零、窗外丢弃——仪表盘那张小图的三条诚实底线。</summary>
public sealed class AiUsageTrendTests
{
    /// <summary>锚点用本地时区构造：LastDays 铺的是 anchor 的<b>本地日</b>，测试机在哪个时区都该锚在 2026-11-07。</summary>
    private static readonly DateTimeOffset Anchor = CreateAnchor();

    private static DateTimeOffset CreateAnchor()
    {
        var local = new DateTime(2026, 11, 7, 18, 30, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    [Fact]
    public void LastDays_AnchorsAtAnchorLocalDay_PadsMissingWithZero()
    {
        var raw = new[]
        {
            new AiUsageDay("2026-11-05", 3, 300),
            new AiUsageDay("2026-11-07", 1, 100),
        };

        var days = AiUsageTrend.LastDays(raw, Anchor, 4);

        Assert.Equal(4, days.Count);
        Assert.Equal("2026-11-04", days[0].Day);
        Assert.Equal(0, days[0].TotalTokens);            // 没账的日子补零占位："空格"与"零消耗"都得在图上
        Assert.Equal("2026-11-05", days[1].Day);
        Assert.Equal(300, days[1].TotalTokens);
        Assert.Equal("2026-11-06", days[2].Day);
        Assert.Equal(0, days[2].TotalTokens);
        Assert.Equal("2026-11-07", days[3].Day);         // anchor 的本地日永远是最后一格
        Assert.Equal(100, days[3].TotalTokens);
    }

    [Fact]
    public void LastDays_DropsOutsideWindow_AndLastWinsOnDuplicate()
    {
        var raw = new[]
        {
            new AiUsageDay("2026-10-20", 9, 900),        // 窗外旧账：并进来会把窗内的日子挤掉
            new AiUsageDay("2026-11-06", 2, 200),
            new AiUsageDay("2026-11-06", 5, 500),        // 同名后写胜
        };

        var days = AiUsageTrend.LastDays(raw, Anchor, 2);

        Assert.Equal(2, days.Count);
        Assert.Equal("2026-11-06", days[0].Day);
        Assert.Equal(500, days[0].TotalTokens);          // 11-06 取后写那条
        Assert.Equal("2026-11-07", days[1].Day);
        Assert.Equal(0, days[1].TotalTokens);            // 10-20 被丢弃；11-07 无账补零
        Assert.DoesNotContain(days, d => d.Day == "2026-10-20");
    }

    [Fact]
    public void LastDays_ZeroCount_ReturnsEmpty()
    {
        Assert.Empty(AiUsageTrend.LastDays(Array.Empty<AiUsageDay>(), Anchor, 0));
        Assert.Empty(AiUsageTrend.LastDays(Array.Empty<AiUsageDay>(), Anchor, -1));
    }
}
