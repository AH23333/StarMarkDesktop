#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Abstractions.Insights;
using StarMark.Core.Insights;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// InsightsService.BuildHealthReport 的扣分制与聚合输出测试（移植自扩展 insights.test.ts）。
/// 每个用例通过显式控制 tags / updatedAt 来隔离单个因子，避免扣分相互叠加。
/// </summary>
public sealed class InsightsServiceTests
{
    private static readonly Func<DateTimeOffset> FixedNow =
        () => new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    private static Item MakeItem(
        string title,
        ItemType type = ItemType.Bookmark,
        long? createdAt = null,
        long? updatedAt = null,
        List<string>? tags = null,
        string? extraJson = null,
        string uri = "")
    {
        var now = FixedNow().ToUnixTimeSeconds();
        return new Item
        {
            Id = 0,
            Type = type,
            Source = "test",
            SourceId = "id-" + Guid.NewGuid(),
            Title = title,
            Uri = uri,
            CreatedAt = createdAt ?? now,
            UpdatedAt = updatedAt ?? now,
            Tags = tags ?? new List<string>(),
            ExtraJson = extraJson,
        };
    }

    private static long DaysAgo(int days) => FixedNow().AddDays(-days).ToUnixTimeSeconds();

    [Fact]
    public void EmptyCollection_Scores100_NoFactors()
    {
        var r = InsightsService.BuildHealthReport(Array.Empty<Item>(), now: FixedNow);
        Assert.Equal(100, r.Score);
        Assert.Empty(r.Factors);
        Assert.Equal(0, r.Total);
    }

    [Fact]
    public void Duplicates_OneGroup_Deducts10()
    {
        var items = new List<Item>
        {
            MakeItem("Hello World", tags: new() { "t" }),
            MakeItem("hello world", tags: new() { "t" }),
            MakeItem("HELLO WORLD", tags: new() { "t" }),
        };
        var r = InsightsService.BuildHealthReport(items, now: FixedNow);
        Assert.Equal(90, r.Score); // 1 组 → min(40, 10) = 10
        var f = Assert.Single(r.Factors);
        Assert.Equal("duplicates", f.Key);
        Assert.Equal(10, f.Deduction);
        Assert.Single(r.DuplicateTop);
        Assert.Equal(3, r.DuplicateTop[0].Count);
    }

    [Fact]
    public void Duplicates_ManyGroups_CappedAt40()
    {
        var items = new List<Item>();
        for (var i = 0; i < 6; i++)
        {
            items.Add(MakeItem($"dup {i}", tags: new() { "t" }));
            items.Add(MakeItem($"dup {i}", tags: new() { "t" })); // 6 组
        }
        var r = InsightsService.BuildHealthReport(items, now: FixedNow);
        Assert.Equal(60, r.Score); // min(40, 6*10) = 40
        Assert.Equal(6, r.DuplicateTop.Count);
    }

    [Fact]
    public void Untagged_HighRatio_Deducts()
    {
        var items = new List<Item>();
        for (var i = 0; i < 9; i++) items.Add(MakeItem($"no tag {i}")); // 9 未打标签（updatedAt 默认新鲜）
        items.Add(MakeItem("tagged", tags: new() { "ok" }));            // 1 已打标签
        var r = InsightsService.BuildHealthReport(items, now: FixedNow);
        // ratio 0.9 → min(25, floor(45)) = 25
        Assert.Equal(75, r.Score);
        Assert.Contains(r.Factors, f => f.Key == "untagged" && f.Deduction == 25);
    }

    [Fact]
    public void Untagged_AtThreshold_NoPenalty()
    {
        // 1/5 = 0.2，未超过阈值 0.2 → 不罚
        var items = new List<Item>
        {
            MakeItem("only untagged"),
            MakeItem("t1", tags: new() { "a" }),
            MakeItem("t2", tags: new() { "b" }),
            MakeItem("t3", tags: new() { "c" }),
            MakeItem("t4", tags: new() { "d" }),
        };
        var r = InsightsService.BuildHealthReport(items, now: FixedNow);
        Assert.DoesNotContain(r.Factors, f => f.Key == "untagged");
        Assert.Equal(100, r.Score);
    }

    [Fact]
    public void Stale_HighRatio_Deducts()
    {
        var items = new List<Item>();
        for (var i = 0; i < 5; i++)
            items.Add(MakeItem($"stale {i}", updatedAt: DaysAgo(200), tags: new() { "t" })); // 5 条陈旧、已打标签
        items.Add(MakeItem("fresh", updatedAt: DaysAgo(1), tags: new() { "t" }));            // 1 条新鲜
        var r = InsightsService.BuildHealthReport(items, now: FixedNow);
        // ratio 5/6=0.83 > 0.4 → min(15, floor(0.83*30)=24) = 15
        Assert.Equal(85, r.Score);
        Assert.Contains(r.Factors, f => f.Key == "stale" && f.Deduction == 15);
    }

    [Fact]
    public void CombinedFactors_StackDeductions()
    {
        // 1 组重复（扣 10）+ 未打标签 9/9（扣 25）+ 陈旧 9/9（扣 15）= 50 → score 50
        var items = new List<Item>
        {
            MakeItem("same title", updatedAt: DaysAgo(200)),
            MakeItem("same title", updatedAt: DaysAgo(200)),
        };
        for (var i = 0; i < 7; i++)
            items.Add(MakeItem($"u {i}", updatedAt: DaysAgo(200)));
        var r = InsightsService.BuildHealthReport(items, now: FixedNow);
        Assert.Equal(50, r.Score);
        Assert.Equal(3, r.Factors.Count);
    }

    [Fact]
    public void LanguageTop_AggregatesGitHubLanguages()
    {
        string Meta(string lang) => JsonSerializer.Serialize(new GitHubStarMeta { Language = lang });
        var items = new List<Item>
        {
            MakeItem("r1", ItemType.GitHubStar, extraJson: Meta("C#")),
            MakeItem("r2", ItemType.GitHubStar, extraJson: Meta("C#")),
            MakeItem("r3", ItemType.GitHubStar, extraJson: Meta("Python")),
            MakeItem("b1", ItemType.Bookmark),
        };
        var r = InsightsService.BuildHealthReport(items, now: FixedNow);
        Assert.Equal(2, r.LanguageTop.Count);
        Assert.Equal("C#", r.LanguageTop[0].Language);
        Assert.Equal(2, r.LanguageTop[0].Count);
    }

    [Fact]
    public void DistinctDomains_CountsHttpHosts()
    {
        var items = new List<Item>
        {
            MakeItem("a", uri: "https://github.com/x"),
            MakeItem("b", uri: "https://github.com/y"),
            MakeItem("c", uri: "https://example.com/z"),
            MakeItem("d", uri: "file:///C:/tmp/a.txt"),
        };
        var r = InsightsService.BuildHealthReport(items, now: FixedNow);
        Assert.Equal(2, r.DistinctDomains); // github.com + example.com
    }

    [Fact]
    public void NewTrend_BucketsByAnchorDate()
    {
        var items = new List<Item>
        {
            MakeItem("today", createdAt: FixedNow().ToUnixTimeSeconds()),
            MakeItem("yesterday", createdAt: FixedNow().AddDays(-1).ToUnixTimeSeconds()),
            MakeItem("old", createdAt: FixedNow().AddDays(-30).ToUnixTimeSeconds()),
        };
        var r = InsightsService.BuildHealthReport(items, days: 14, now: FixedNow);
        Assert.Equal(14, r.NewTrend.Count);
        Assert.Equal(2, r.NewTrend.Sum(t => t.Count)); // 仅近 14 天内的 2 条
        Assert.Contains(r.NewTrend, t => t.Count == 1);
    }
}
