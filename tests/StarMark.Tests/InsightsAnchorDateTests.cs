#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions;
using StarMark.Core.Insights;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// AnchorDate 对越界 epoch 秒的健壮性：extra_json.StarredAt 若是毫秒级/荒谬 long
/// （导入/手改备份可注入），FromUnixTimeSeconds 会抛 ArgumentOutOfRangeException，
/// 因趋势循环无 try 包裹而击穿整份 BuildHealthReport（分数/语言/直方图全丢）。
/// 契约：纯聚合函数对单条坏元数据不得崩。
/// </summary>
public sealed class InsightsAnchorDateTests
{
    private static readonly Func<DateTimeOffset> FixedNow =
        () => new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    private static Item MakeStar(string title, string? extraJson, long createdAt) => new()
    {
        Type = ItemType.GitHubStar,
        Source = "github",
        SourceId = "id-" + Guid.NewGuid(),
        Title = title,
        Uri = "https://github.com/o/" + title,
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
        Tags = new List<string>(),
        ExtraJson = extraJson,
    };

    [Theory]
    [InlineData("""{"Language":"C#","StarredAt":1712345678901}""")]  // 毫秒级 → 超上限
    [InlineData("""{"Language":"C#","StarredAt":999999999999}""")]   // 荒谬大值
    [InlineData("""{"Language":"C#","StarredAt":-99999999999999}""")] // 荒谬负值
    public void BuildHealthReport_OutOfDomainStarredAt_DoesNotThrow(string extraJson)
    {
        var items = new[] { MakeStar("repo", extraJson, createdAt: FixedNow().ToUnixTimeSeconds()) };

        var ex = Record.Exception(() => InsightsService.BuildHealthReport(items, days: 14, now: FixedNow));

        Assert.Null(ex);   // 修复前：ArgumentOutOfRangeException（整份报告崩）
    }

    [Fact]
    public void BuildHealthReport_OutOfDomainStarredAt_FallsBackToCreatedAt()
    {
        // 5 天前入库、StarredAt 却是非法毫秒值 → 应落回 CreatedAt 分桶，趋势当日 +1。
        long created = FixedNow().AddDays(-5).ToUnixTimeSeconds();
        var items = new[] { MakeStar("repo", """{"Language":"C#","StarredAt":1712345678901}""", created) };

        var r = InsightsService.BuildHealthReport(items, days: 14, now: FixedNow);

        Assert.Equal(14, r.NewTrend.Count);
        Assert.Equal(1, r.NewTrend.Sum(d => d.Count));   // 恰好落进窗口内的一天，非整表崩也非丢计数
    }

    [Fact]
    public void BuildHealthReport_ValidStarredAt_StillBucketsByStarDate()
    {
        // 合法秒级 StarredAt：8 天前 star、当天入库 → 归到 8 天前那个桶。
        long starred = FixedNow().AddDays(-8).ToUnixTimeSeconds();
        long created = FixedNow().ToUnixTimeSeconds();
        var items = new[] { MakeStar("repo", $$"""{"Language":"C#","StarredAt":{{starred}}}""", created) };

        var r = InsightsService.BuildHealthReport(items, days: 14, now: FixedNow);

        Assert.Equal(1, r.NewTrend.Sum(d => d.Count));
    }
}
