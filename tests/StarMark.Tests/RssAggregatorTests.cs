#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Feed;
using StarMark.Core.Feed;
using StarMark.Integrations.Feed;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 一轮抓取编排的判断：<b>坏一个源不许拖垮整页</b>、顺序稳定、时间缺失不冒充最新、
/// 以及"用户取消"与"源坏了"是两件事。
/// 抓取以委托注入，所以这些都能在没有网络的情况下断言。
/// </summary>
public sealed class RssAggregatorTests
{
    private static RssSourceConfig Src(int id, string url = "https://a.test/feed", bool enabled = true)
        => new(id, "源" + id, url, enabled);

    private static RssFetchResult Content(string xml) => new(RssFetchStatus.Content, xml, null, null, null);

    private static string Feed(params (string Title, string Link, string? Date)[] items)
        => "<rss><channel>" + string.Concat(items.Select(i =>
            $"<item><title>{i.Title}</title><link>{i.Link}</link>" +
            (i.Date is null ? string.Empty : $"<pubDate>{i.Date}</pubDate>") + "</item>")) +
            "</channel></rss>";

    private static Task<RssRunResult> Run(
        IReadOnlyList<RssSourceConfig> sources, Func<RssSourceConfig, Task<RssFetchResult>> fetch, CancellationToken ct = default)
        => RssAggregator.RunAsync(sources, fetch, ct);

    [Fact]
    public async Task OneBrokenSourceDoesNotBlankTheWholeRun()
    {
        var result = await Run(new[] { Src(1), Src(2) }, source => Task.FromResult(
            source.Id == 1 ? Content(Feed(("好的一条", "https://a.test/1", null))) : new RssFetchResult(RssFetchStatus.Failure, null, null, null, "源返回 503")));

        Assert.Equal(2, result.Outcomes.Count);
        Assert.True(result.Outcomes[0].Ok);
        Assert.False(result.Outcomes[1].Ok);
        Assert.Equal("源返回 503", result.Outcomes[1].Error);
        Assert.Equal(1, result.FailedCount);
        Assert.Single(result.Entries);                      // 好源的内容照常给出
        Assert.Equal(1, result.Outcomes[0].Source.Id);      // 顺序与配置一致，刷新不会换位置
    }

    [Fact]
    public async Task ExceptionsFromTheFetcherAreIsolatedToo()
    {
        var result = await Run(new[] { Src(9) }, _ => throw new HttpRequestLike());
        Assert.False(result.Outcomes[0].Ok);
        Assert.Contains("异常", result.Outcomes[0].Error);
    }

    private sealed class HttpRequestLike : Exception { }

    [Fact]
    public async Task DisabledSourcesAreNeverFetched()
    {
        var called = 0;
        var result = await Run(new[] { Src(1, enabled: false), Src(2, enabled: false) },
            _ => { called++; return Task.FromResult(Content(Feed(("x", "https://a.test/x", null)))); });

        Assert.Equal(0, called);
        Assert.Empty(result.Entries);
        Assert.Equal(0, result.FailedCount);                // 关掉的不是"坏了的"
        Assert.All(result.Outcomes, o => Assert.True(o.Ok));
    }

    [Fact]
    public async Task AnUnusableUrlFailsThatSourceWithoutTouchingTheNetwork()
    {
        var called = 0;
        var result = await Run(new[] { Src(1, "blog.example.com") },
            _ => { called++; return Task.FromResult(Content("<rss><channel/></rss>")); });
        Assert.Equal(0, called);
        Assert.Contains("完整地址", result.Outcomes[0].Error);
    }

    [Fact]
    public async Task NotModifiedIsSuccessNotFailure()
    {
        // 源回 304 是好消息。混进失败里，用户会去修一个根本没坏的地址
        var result = await Run(new[] { Src(1) }, _ => Task.FromResult(new RssFetchResult(RssFetchStatus.NotModified, null, "W/1", null, null)));
        Assert.True(result.Outcomes[0].Ok);
        Assert.True(result.Outcomes[0].NotModified);
        Assert.Null(result.Outcomes[0].Error);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public async Task EntriesAreSortedNewestFirstAndUndatedOnesGoLast()
    {
        var xml = Feed(
            ("没有日期", "https://a.test/none", null),
            ("旧的", "https://a.test/old", "Fri, 02 Jan 2026 00:00:00 GMT"),
            ("新的", "https://a.test/new", "Wed, 04 Mar 2026 00:00:00 GMT"));
        var result = await Run(new[] { Src(1) }, _ => Task.FromResult(Content(xml)));

        Assert.Equal(new[] { "新的", "旧的", "没有日期" }, result.Entries.Select(e => e.Title));
        Assert.Null(result.Entries[2].PublishedAt);         // 兜底排到最后，而不是被编成"刚刚"
    }

    [Fact]
    public async Task FlattenedEntriesAreCappedSoManySourcesCannotFloodTheList()
    {
        var many = Feed(Enumerable.Range(0, RssSourceConfig.MaxEntries)
            .Select(i => ($"条{i}", $"https://a.test/{i}", (string?)null)).ToArray());
        var sources = Enumerable.Range(1, 6).Select(i => Src(i)).ToList();
        var result = await Run(sources, _ => Task.FromResult(Content(many)));

        Assert.Equal(RssAggregator.MaxTotalEntries, result.Entries.Count);
        Assert.True(result.FailedCount == 0);
    }

    [Fact]
    public async Task UserCancellationPropagatesInsteadOfBeingRecordedAsABrokenSource()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Run(new[] { Src(1) }, _ => Task.FromResult(Content("<rss><channel/></rss>")), cts.Token));
    }
}
