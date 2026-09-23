#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions.Trending;
using StarMark.Core.Trending;

namespace StarMark.Tests;

/// <summary>
/// 热榜编排的契约护栏：什么时候发请求、什么时候读缓存、失败时给什么。
/// <para>
/// 这一层最容易出的错不是崩，而是<b>"看起来成功"</b>：把取消当成失败去回读旧缓存、
/// 把空缓存当有效结果返回、缓存写失败却静默。所以断言的重点是"用户此刻看到的是什么、
/// 网络此刻到底发没发"。
/// </para>
/// </summary>
public sealed class TrendingServiceTests
{
    private static readonly TrendingRepo[] Repos = { new("o/r", "https://github.com/o/r", "d", "Go", 10, 1) };

    private sealed class FakeSource : ITrendingSource
    {
        public int Calls;
        public List<(TrendingPeriod Period, string? Language)> Called { get; } = new();
        public TrendingFetchResult? Result;
        public Exception? Throw;

        public Task<TrendingFetchResult> FetchAsync(TrendingPeriod period, string? language, CancellationToken ct)
        {
            Calls++;
            Called.Add((period, language));
            if (Throw is not null) throw Throw;
            return Task.FromResult(Result ?? new TrendingFetchResult(Repos, TrendingSource.TrendingHtml));
        }
    }

    private sealed class Harness
    {
        public Dictionary<string, string> Store = new();
        public FakeSource Source = new();
        public DateTimeOffset Now = new(2026, 9, 23, 9, 0, 0, TimeSpan.FromHours(8));
        public Func<string, string, CancellationToken, Task>? OnWrite;
        public List<string> ReadKeys = new();

        public TrendingService Service() => new(Source,
            readCache: (key, ct) => { ReadKeys.Add(key); return Task.FromResult(Store.TryGetValue(key, out var v) ? v : null); },
            writeCache: async (key, value, ct) =>
            {
                if (OnWrite is not null) await OnWrite(key, value, ct);
                Store[key] = value;
            },
            now: () => Now);

        public string Key(TrendingPeriod p = TrendingPeriod.Weekly, string? lang = null)
            => TrendingCacheCodec.CacheStateKey(p, lang);

        public void SeedCache(TrendingPeriod p = TrendingPeriod.Weekly, long? fetchedAt = null, int repos = 1)
        {
            var list = new List<TrendingRepo>();
            for (var i = 0; i < repos; i++) list.Add(Repos[0] with { FullName = "seed/" + i });
            Store[Key(p)] = TrendingCacheCodec.Encode(new TrendingCachedEntry(list,
                fetchedAt ?? Now.AddHours(-3).ToUnixTimeSeconds(), TrendingSource.SearchApi.Code()));
        }
    }

    [Fact]
    public async Task SameLocalDayCacheHit_ReturnsCacheWithoutTouchingNetwork()
    {
        var h = new Harness();
        h.SeedCache();

        var r = await h.Service().GetAsync(TrendingPeriod.Weekly, null, force: false, CancellationToken.None);

        Assert.Equal(0, h.Source.Calls);
        Assert.True(r.FromCache);
        Assert.False(r.Stale);
        Assert.Equal("seed/0", Assert.Single(r.Repos).FullName);
        Assert.Equal(TrendingSource.SearchApi, r.Via);       // 缓存里记的来源要一路带到界面
    }

    [Fact]
    public async Task ForceRefresh_RefetchesAndReplacesCacheTimestamp()
    {
        var h = new Harness();
        h.SeedCache();

        var r = await h.Service().GetAsync(TrendingPeriod.Weekly, null, force: true, CancellationToken.None);

        Assert.Equal(1, h.Source.Calls);
        Assert.False(r.FromCache);
        Assert.Equal(h.Now.ToUnixTimeSeconds(), r.FetchedAt);
        Assert.Equal("o/r", Assert.Single(r.Repos).FullName);
    }

    [Fact]
    public async Task YesterdayCache_TriggersFetch_AndWritesUnderTheSameKey()
    {
        var h = new Harness();
        h.SeedCache(fetchedAt: h.Now.AddDays(-1).ToUnixTimeSeconds());
        var written = new List<string>();
        h.OnWrite = (key, _, _) => { written.Add(key); return Task.CompletedTask; };

        await h.Service().GetAsync(TrendingPeriod.Weekly, null, force: false, CancellationToken.None);

        Assert.Equal(new[] { h.Key() }, written);
        Assert.Equal(h.Now.ToUnixTimeSeconds(),
            TrendingCacheCodec.Decode(h.Store[h.Key()])!.FetchedAt);
    }

    /// <summary>缓存里 0 条不是"今天的热榜是空的"，是"没有可用缓存"——必须去抓。</summary>
    [Fact]
    public async Task EmptyCachedPayload_IsNotTreatedAsFresh()
    {
        var h = new Harness();
        h.SeedCache(repos: 0);

        var r = await h.Service().GetAsync(TrendingPeriod.Weekly, null, force: false, CancellationToken.None);

        Assert.Equal(1, h.Source.Calls);
        Assert.False(r.FromCache);
    }

    [Fact]
    public async Task FetchFails_WithCache_ShowsStaleDataWithReason()
    {
        var h = new Harness { Source = { Throw = new TrendingException("热榜页与 GitHub 搜索接口都没能给出结果（HTTP 503）") } };
        h.SeedCache(fetchedAt: h.Now.AddDays(-1).ToUnixTimeSeconds());   // 昨日的缓存才会真的触发一次抓取

        var r = await h.Service().GetAsync(TrendingPeriod.Weekly, null, force: false, CancellationToken.None);

        Assert.True(r.Stale);
        Assert.True(r.FromCache);
        Assert.Contains("503", r.Notice);
        Assert.Equal("seed/0", Assert.Single(r.Repos).FullName);
    }

    [Fact]
    public async Task FetchFails_WithoutCache_RethrowsSoTheUiCanGiveAReason()
    {
        var h = new Harness { Source = { Throw = new TrendingException("两条腿都失败") } };

        await Assert.ThrowsAsync<TrendingException>(() =>
            h.Service().GetAsync(TrendingPeriod.Weekly, null, force: false, CancellationToken.None));
    }

    /// <summary>取消不许被伪装成"回读旧缓存的成功"（P-55 的失序面：用户按了停止却看到一批数据）。</summary>
    [Fact]
    public async Task Cancellation_NeverDegradesIntoStaleSuccess()
    {
        var h = new Harness { Source = { Throw = new OperationCanceledException() } };
        h.SeedCache(fetchedAt: h.Now.AddDays(-1).ToUnixTimeSeconds());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Service().GetAsync(TrendingPeriod.Weekly, null, force: false, CancellationToken.None));

        Assert.True(h.Store.ContainsKey(h.Key()));   // 旧缓存保持原样，没被改写成"刚抓的"
    }

    /// <summary>缓存写失败：已经抓到的数据照出，但必须说一句（否则用户以为明天会自动更新，实际天天重抓）。</summary>
    [Fact]
    public async Task CacheWriteFailure_KeepsDataAndTellsWhy()
    {
        var h = new Harness();
        h.OnWrite = (_, _, _) => throw new InvalidOperationException("disk full");

        var r = await h.Service().GetAsync(TrendingPeriod.Weekly, null, force: false, CancellationToken.None);

        Assert.Single(r.Repos);
        Assert.False(r.Stale);
        Assert.Contains("缓存未能写入", r.Notice);
    }

    [Fact]
    public async Task LanguageAndPeriod_HaveTheirOwnKeysAndArePassedThrough()
    {
        var h = new Harness();
        h.SeedCache();                                   // 只给"周榜 · 全部语言"种了缓存

        var r = await h.Service().GetAsync(TrendingPeriod.Daily, "rust", force: false, CancellationToken.None);

        Assert.Equal(1, h.Source.Calls);                 // 换周期/换语言不能拿别的键的缓存
        Assert.Equal((TrendingPeriod.Daily, "rust"), h.Source.Called[0]);
        Assert.Equal(h.Now.ToUnixTimeSeconds(), r.FetchedAt);
        Assert.True(h.Store.ContainsKey(h.Key(TrendingPeriod.Daily, "rust")));
    }

    [Fact]
    public async Task SuccessWithFallbackNotice_PassesItThrough()
    {
        var h = new Harness
        {
            Source = { Result = new TrendingFetchResult(Repos, TrendingSource.SearchApi, "热榜页抓取失败，已用 GitHub 搜索接口兜底") }
        };

        var r = await h.Service().GetAsync(TrendingPeriod.Weekly, null, force: false, CancellationToken.None);

        Assert.Equal(TrendingSource.SearchApi, r.Via);
        Assert.Contains("兜底", r.Notice);
        Assert.Equal("search-api", TrendingCacheCodec.Decode(h.Store[h.Key()])!.Via);
    }
}
