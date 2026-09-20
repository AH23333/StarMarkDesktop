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
    public void Duplicates_EmptyOrPunctuationTitles_AreNotGrouped()
    {
        // 多条"无有效标题"（空 / 纯标点）条目归一化后都塌成 ""。旧实现把它们并成一个幽灵重复组误扣分；
        // 归一无意义者不应参与重复判定。
        var items = new List<Item>
        {
            MakeItem("", tags: new() { "t" }),
            MakeItem("!!!", tags: new() { "t" }),
            MakeItem("？？？", tags: new() { "t" }),
        };
        var r = InsightsService.BuildHealthReport(items, now: FixedNow);
        Assert.DoesNotContain(r.Factors, f => f.Key == "duplicates");
        Assert.Empty(r.DuplicateTop);
        Assert.Equal(100, r.Score);
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

    [Fact]
    public void Duplicates_CrossOwnerSameTitle_DifferentUri_AreNotGrouped()
    {
        // 两个不同 owner 的仓库，标题同为 "{Repo} · {Language}"（NormalizeTitle 后同为 "tool rust"），Uri 不同。
        // 旧实现仅按归一化标题分组 → 误判为疑似重复（−10）。加入 host+path 维度后各归各的，不再误判。
        var items = new List<Item>
        {
            MakeItem("Tool · Rust", ItemType.GitHubStar, tags: new() { "t" }, uri: "https://github.com/ownerA/tool"),
            MakeItem("Tool · Rust", ItemType.GitHubStar, tags: new() { "t" }, uri: "https://github.com/ownerB/tool"),
        };
        var r = InsightsService.BuildHealthReport(items, now: FixedNow);
        Assert.DoesNotContain(r.Factors, f => f.Key == "duplicates");
        Assert.Empty(r.DuplicateTop);
        Assert.Equal(100, r.Score);
    }

    [Fact]
    public void Duplicates_SameTitleSameDestination_StillGrouped()
    {
        // 同一目的地（host+path 相同，仅尾斜杠差异）的两条书签仍应算重复——加 uri 维度不该放松真正的重复。
        var items = new List<Item>
        {
            MakeItem("My Page", tags: new() { "t" }, uri: "https://example.com/page"),
            MakeItem("My Page", tags: new() { "t" }, uri: "https://example.com/page/"),
        };
        var r = InsightsService.BuildHealthReport(items, now: FixedNow);
        var f = Assert.Single(r.Factors, x => x.Key == "duplicates");
        Assert.Equal(10, f.Deduction);
        Assert.Single(r.DuplicateTop);
        Assert.Equal(2, r.DuplicateTop[0].Count);
        Assert.Equal(90, r.Score);
    }

    [Fact]
    public void NewTrend_NegativeDays_DoesNotThrow_ReturnsEmptyTrend()
    {
        // days 为负曾让 new List<>/new Dictionary<> 的负 capacity 抛 ArgumentOutOfRangeException。
        var items = new List<Item> { MakeItem("x", createdAt: FixedNow().ToUnixTimeSeconds()) };
        var r = InsightsService.BuildHealthReport(items, days: -5, now: FixedNow);
        Assert.Empty(r.NewTrend);
    }

    [Fact]
    public void NewTrend_CrossYearSameMonthDay_UsesDistinctBuckets()
    {
        // days>365 时旧的 "MM-dd" 内部键会跨年碰撞：2025-09-16 与 2026-09-16 都渲染 "09-16" → 塌进同一桶（旧计 2、另一桶空）。
        // 内部键改 "yyyy-MM-dd" 后各归各桶，显示标签仍是 "MM-dd"。
        var items = new List<Item>
        {
            MakeItem("this year", createdAt: FixedNow().ToUnixTimeSeconds()),               // 2026-09-16
            MakeItem("last year", createdAt: FixedNow().AddDays(-365).ToUnixTimeSeconds()), // 2025-09-16（同年非闰，恰隔 365 天）
        };
        var r = InsightsService.BuildHealthReport(items, days: 400, now: FixedNow);
        Assert.Equal(400, r.NewTrend.Count);
        Assert.Equal(2, r.NewTrend.Count(t => t.Count == 1)); // 两个不同桶各 1（旧实现此处会是 1 桶计 2）
        Assert.Equal(2, r.NewTrend.Sum(t => t.Count));
    }
}
