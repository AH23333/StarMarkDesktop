#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions;
using StarMark.Core.Insights;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// AnchorDate 对越界 epoch 秒的健壮性：入库锚（P-34B 后是 synced_at，空则 created_at）若是毫秒级/
/// 荒谬 long（导入/手改备份可注入），FromUnixTimeSeconds 会抛 ArgumentOutOfRangeException，
/// 因趋势循环无 try 包裹而击穿整份 BuildHealthReport（分数/语言/直方图全丢）。
/// 契约：纯聚合函数对单条坏元数据不得崩。
/// <para>P-34B（片210 拍板）换了锚：新增趋势从「源对象自身时刻」（StarredAt/CreatedAt）改读
/// <b>入库时刻</b>（SyncedAt 首见，upsert 的 conflict 臂不再刷它）——今天的收藏落进今天那个桶，
/// 不再恒为 0。本文件连同把「新锚真的赢过源侧时刻」一并钉住。</para>
/// </summary>
public sealed class InsightsAnchorDateTests
{
    private static readonly Func<DateTimeOffset> FixedNow =
        () => new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    private static Item MakeStar(string title, string? extraJson, long createdAt, long? syncedAt = null) => new()
    {
        Type = ItemType.GitHubStar,
        Source = "github",
        SourceId = "id-" + Guid.NewGuid(),
        Title = title,
        Uri = "https://github.com/o/" + title,
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
        SyncedAt = syncedAt,
        Tags = new List<string>(),
        ExtraJson = extraJson,
    };

    [Theory]
    [InlineData(1712345678901)]      // 毫秒级 → 超上限
    [InlineData(999999999999)]       // 荒谬大值
    [InlineData(-99999999999999)]    // 荒谬负值
    public void BuildHealthReport_OutOfDomainSyncedAt_DoesNotThrow(long badSynced)
    {
        // 坏值注入面换过一次（P-34B）：锚点从 extra_json.StarredAt 换成 synced_at/created_at，
        // 守的仍是同一张脸——手改/坏备份把毫秒级或荒谬 long 直接注进 synced_at 不得击穿报告。
        var items = new[] { MakeStar("repo", """{"Language":"C#"}""", createdAt: FixedNow().ToUnixTimeSeconds(), syncedAt: badSynced) };

        var ex = Record.Exception(() => InsightsService.BuildHealthReport(items, days: 14, now: FixedNow));

        Assert.Null(ex);   // 修复前：ArgumentOutOfRangeException（整份报告崩）
    }

    [Fact]
    public void BuildHealthReport_OutOfDomainSyncedAt_FallsBackToCreatedAt()
    {
        // synced_at 非法、createdAt 是 5 天前 → 锚点落回 createdAt 分桶，趋势当日 +1（不崩也非丢计数）。
        long created = FixedNow().AddDays(-5).ToUnixTimeSeconds();
        var items = new[] { MakeStar("repo", """{"Language":"C#"}""", created, syncedAt: 1712345678901) };

        var r = InsightsService.BuildHealthReport(items, days: 14, now: FixedNow);

        Assert.Equal(14, r.NewTrend.Count);
        Assert.Equal(1, r.NewTrend.Sum(d => d.Count));   // 恰好落进窗口内的一天
    }

    [Fact]
    public void BuildHealthReport_TrendAnchorsOnSyncedAt_NotSourceTime()
    {
        // P-34B 的主诉：StarredAt（源侧 star 时刻）与 CreatedAt（仓库创建）都在 14 天窗口之外，
        // 旧锚把这条「昨天才收藏」的 star 落进历史桶、今日柱恒为 0；
        // 入库锚（SyncedAt 首见＝昨天）让它落进窗口：Sum 从 0 变 1，新旧锚在此可判。
        long starred = FixedNow().AddDays(-30).ToUnixTimeSeconds();
        long created = FixedNow().AddDays(-29).ToUnixTimeSeconds();
        long synced = FixedNow().AddDays(-1).ToUnixTimeSeconds();
        var items = new[] { MakeStar("repo", $$"""{"Language":"C#","StarredAt":{{starred}}}""", created, syncedAt: synced) };

        var r = InsightsService.BuildHealthReport(items, days: 14, now: FixedNow);

        Assert.Equal(1, r.NewTrend.Sum(d => d.Count));
    }
}
