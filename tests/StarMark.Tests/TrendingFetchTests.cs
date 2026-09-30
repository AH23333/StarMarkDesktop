#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions.Trending;
using StarMark.Integrations.Trending;

namespace StarMark.Tests;

/// <summary>
/// 热榜抓取（两条腿 + 退避 + 上限）与缓存编解码的契约护栏。全程假传输缝，不触网。
/// <para>三条安全/稳定性约束各占一组断言：Token 只发给 api.github.com、取消绝不降级成兜底、
/// 响应体有字节上限。它们的共同点是<b>出问题时界面上一切正常</b>，只有读代码与跑测试能发现。</para>
/// </summary>
public sealed class TrendingFetchTests
{
    private const string GoodHtml =
        "<html><body><article><h2 class=\"h3 lh-condensed\"><a href=\"/octocat/hello\">o / h</a></h2>"
        + "<p class=\"col-9\">A repo</p></article></body></html>";
    private const string SearchJson =
        "{\"items\":[{\"full_name\":\"a/b\",\"html_url\":\"https://github.com/a/b\",\"description\":\"d\"," +
        "\"language\":\"Go\",\"stargazers_count\":1234}]}";

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<string, int, HttpResponseMessage> _respond;
        private readonly Dictionary<string, int> _seen = new();
        public List<(string Url, string? Auth)> Calls { get; } = new();

        public FakeHandler(Func<string, int, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            _seen[url] = _seen.TryGetValue(url, out var n) ? n + 1 : 1;
            Calls.Add((url, request.Headers.Authorization?.ToString()));
            return Task.FromResult(_respond(url, _seen[url]));
        }
    }

    private static HttpResponseMessage Html(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode code, TimeSpan? retryAfter = null)
    {
        var resp = new HttpResponseMessage(code) { Content = new StringContent("", Encoding.UTF8, "text/plain") };
        if (retryAfter is { } t) resp.Headers.RetryAfter = new RetryConditionHeaderValue(t);
        return resp;
    }

    private static bool IsTrendingPage(string url) => url.StartsWith("https://github.com/trending", StringComparison.Ordinal);

    // ===== 两条腿 =====

    [Fact]
    public async Task HtmlLeg_Succeeds_ReportsHtmlSourceWithoutNotice()
    {
        var h = new FakeHandler((_, _) => Html(GoodHtml));
        using var f = new TrendingFetcher(new HttpClient(h));

        var r = await f.FetchAsync(TrendingPeriod.Weekly, null, CancellationToken.None);

        Assert.Equal(TrendingSource.TrendingHtml, r.Via);
        Assert.Null(r.Notice);
        Assert.Equal("octocat/hello", Assert.Single(r.Repos).FullName);
        Assert.False(h.Calls.Any(c => IsApi(c.Url)), "热榜页成功时不该再去搜索接口");
    }

    [Fact]
    public async Task HtmlLeg_Fails_FallsBackToSearchApi_WithVisibleReason()
    {
        var h = new FakeHandler((url, _) => IsTrendingPage(url)
            ? Status(HttpStatusCode.InternalServerError)
            : Json(SearchJson));
        using var f = new TrendingFetcher(new HttpClient(h));

        var r = await f.FetchAsync(TrendingPeriod.Daily, "rust", CancellationToken.None);

        Assert.Equal(TrendingSource.SearchApi, r.Via);
        Assert.NotNull(r.Notice);                      // 兜底必须被标注，不能伪装成"热榜本来就这样"
        Assert.Equal("a/b", Assert.Single(r.Repos).FullName);
        Assert.Contains("language:rust", Uri.UnescapeDataString(h.Calls.Last().Url));   // 语言筛选确实带到兜底查询里
    }

    /// <summary>200 但解析出 0 条（改版/被拦成验证页）＝失败，也要走兜底并留原因。</summary>
    [Fact]
    public async Task HtmlParsesEmpty_CountsAsFailure()
    {
        var h = new FakeHandler((url, _) => IsTrendingPage(url)
            ? Html("<html><body>We noticed unusual traffic from your network</body></html>")
            : Json(SearchJson));
        using var f = new TrendingFetcher(new HttpClient(h));

        var r = await f.FetchAsync(TrendingPeriod.Monthly, null, CancellationToken.None);

        Assert.Equal(TrendingSource.SearchApi, r.Via);
        Assert.Contains("解析为 0 条", r.Notice);
    }

    [Fact]
    public async Task BothLegsFail_ThrowsWithBothReasons()
    {
        var h = new FakeHandler((url, _) => IsTrendingPage(url)
            ? Status(HttpStatusCode.Forbidden)
            : Status(HttpStatusCode.Unauthorized));
        using var f = new TrendingFetcher(new HttpClient(h));

        var ex = await Assert.ThrowsAsync<TrendingException>(
            () => f.FetchAsync(TrendingPeriod.Weekly, null, CancellationToken.None));

        Assert.Contains("403", ex.Message);
        Assert.Contains("401", ex.Message);            // 只报一条腿的码会让人去查错的那条腿
    }

    // ===== 退避重试 =====

    [Fact]
    public async Task RetryAfter_IsHonouredOnce_AndNeverMoreThanOnce()
    {
        var h = new FakeHandler((url, n) => IsTrendingPage(url)
            ? Status((HttpStatusCode)429, TimeSpan.FromSeconds(3))
            : Json(SearchJson));
        var waited = new List<TimeSpan>();
        using var f = new TrendingFetcher(new HttpClient(h),
            delay: (t, ct) => { waited.Add(t); return Task.CompletedTask; });

        await f.FetchAsync(TrendingPeriod.Weekly, null, CancellationToken.None);

        Assert.Single(waited);                                              // 重试一次，不是循环重试
        Assert.Equal(TimeSpan.FromSeconds(3), waited[0]);
        Assert.Equal(2, h.Calls.Count(c => IsTrendingPage(c.Url)));
    }

    /// <summary>对方要求等一小时时不能真等：挂住界面不如走兜底。</summary>
    [Fact]
    public async Task RetryAfter_IsCappedToMaxBackoff()
    {
        var h = new FakeHandler((url, _) => IsTrendingPage(url)
            ? Status((HttpStatusCode)429, TimeSpan.FromHours(1))
            : Json(SearchJson));
        var waited = new List<TimeSpan>();
        using var f = new TrendingFetcher(new HttpClient(h),
            delay: (t, ct) => { waited.Add(t); return Task.CompletedTask; });

        var r = await f.FetchAsync(TrendingPeriod.Weekly, null, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(waited));
        Assert.Equal(TrendingSource.SearchApi, r.Via);
    }

    [Fact]
    public async Task NonRetriableStatus_IsNotRetried()
    {
        var h = new FakeHandler((url, _) => IsTrendingPage(url)
            ? Status(HttpStatusCode.NotFound)
            : Json(SearchJson));
        using var f = new TrendingFetcher(new HttpClient(h));

        await f.FetchAsync(TrendingPeriod.Weekly, null, CancellationToken.None);

        Assert.Single(h.Calls, c => IsTrendingPage(c.Url));
    }

    // ===== 安全：Token 只发给 API 主机 =====

    [Fact]
    public async Task Token_IsSentOnlyToApiGitHubHost_NeverToTheScrapedPage()
    {
        var h = new FakeHandler((url, _) => IsTrendingPage(url)
            ? Status(HttpStatusCode.InternalServerError)
            : Json(SearchJson));
        using var f = new TrendingFetcher(new HttpClient(h), token: "super-secret-token");

        await f.FetchAsync(TrendingPeriod.Weekly, null, CancellationToken.None);

        var page = h.Calls.First(c => IsTrendingPage(c.Url));
        var api = h.Calls.First(c => IsApi(c.Url));
        Assert.Null(page.Auth);                       // 抓 HTML 那趟必须裸奔：凭据不该交给一个我们只解析其页面的站点
        Assert.Equal("Bearer super-secret-token", api.Auth);
    }

    [Fact]
    public async Task WithoutToken_NeitherCallCarriesCredentials()
    {
        var h = new FakeHandler((url, _) => IsTrendingPage(url)
            ? Status(HttpStatusCode.InternalServerError)
            : Json(SearchJson));
        using var f = new TrendingFetcher(new HttpClient(h), token: "   ");

        await f.FetchAsync(TrendingPeriod.Weekly, null, CancellationToken.None);

        Assert.All(h.Calls, c => Assert.Null(c.Auth));
    }

    /// <summary>
    /// DI 注册用的是 <c>tokenProvider</c>（每次请求现取），本条钉住"现取"这个语义：
    /// 构造期快照 Token 会让"设置里改了 Token"必须重启才生效——而界面上一切正常，只有配额还是旧的。
    /// </summary>
    [Fact]
    public async Task TokenProvider_IsReadPerRequest_SoAChangedTokenTakesEffectAtOnce()
    {
        var h = new FakeHandler((url, _) => IsTrendingPage(url)
            ? Status(HttpStatusCode.InternalServerError)
            : Json(SearchJson));
        var current = "tk-old";
        using var f = new TrendingFetcher(new HttpClient(h), tokenProvider: () => current);

        await f.FetchAsync(TrendingPeriod.Weekly, null, CancellationToken.None);
        current = "  tk-new  ";                     // 用户刚在设置里换了一个（顺带夹着空白）
        await f.FetchAsync(TrendingPeriod.Daily, null, CancellationToken.None);

        var apiCalls = h.Calls.Where(c => IsApi(c.Url)).ToList();
        Assert.Equal(2, apiCalls.Count);
        Assert.Equal("Bearer tk-old", apiCalls[0].Auth);
        Assert.Equal("Bearer tk-new", apiCalls[1].Auth);   // 空白被清掉，而不是带着空格发出去
    }

    [Fact]
    public async Task TokenProvider_ReturningBlank_SendsNoCredentials()
    {
        var h = new FakeHandler((url, _) => IsTrendingPage(url)
            ? Status(HttpStatusCode.InternalServerError)
            : Json(SearchJson));
        using var f = new TrendingFetcher(new HttpClient(h), tokenProvider: () => "   ");

        await f.FetchAsync(TrendingPeriod.Weekly, null, CancellationToken.None);

        Assert.All(h.Calls, c => Assert.Null(c.Auth));
    }

    // ===== 取消 ≠ 失败 =====

    [Fact]
    public async Task Cancellation_Propagates_AndNeverSilentlyFallsBack()
    {
        var h = new FakeHandler((url, _) => throw new OperationCanceledException());
        using var f = new TrendingFetcher(new HttpClient(h));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => f.FetchAsync(TrendingPeriod.Weekly, null, CancellationToken.None));

        Assert.Single(h.Calls);                       // 用户掐了就停，不该转去再发一条兜底请求
    }

    // ===== 响应体上限 =====

    [Fact]
    public async Task OversizedHtmlBody_FallsBack_InsteadOfBufferingForever()
    {
        var huge = new string('x', 5_000_000);
        var h = new FakeHandler((url, _) => IsTrendingPage(url) ? Html(huge) : Json(SearchJson));
        using var f = new TrendingFetcher(new HttpClient(h));

        var r = await f.FetchAsync(TrendingPeriod.Weekly, null, CancellationToken.None);

        Assert.Equal(TrendingSource.SearchApi, r.Via);
        Assert.Contains("MB 上限", r.Notice);
    }

    [Fact]
    public async Task MalformedSearchJson_IsReportedNotSwallowed()
    {
        var h = new FakeHandler((url, _) => IsTrendingPage(url)
            ? Status(HttpStatusCode.InternalServerError)
            : Json("<html>not json at all"));
        using var f = new TrendingFetcher(new HttpClient(h));

        await Assert.ThrowsAsync<TrendingException>(() => f.FetchAsync(TrendingPeriod.Weekly, null, CancellationToken.None));
    }

    private static bool IsApi(string url) => url.StartsWith("https://api.github.com/", StringComparison.Ordinal);
}

/// <summary>缓存编解码与"同一本地日历日"判定（纯函数）。</summary>
public sealed class TrendingCacheCodecTests
{
    [Fact]
    public void Entry_RoundTripsThroughJson()
    {
        var entry = new TrendingCachedEntry(
            new[] { new TrendingRepo("o/r", "https://github.com/o/r", "描述", "Go", 10, 3) }, 1_800_000_000, "trending-html");

        var back = TrendingCacheCodec.Decode(TrendingCacheCodec.Encode(entry));

        var repo = Assert.Single(back!.Repos);
        Assert.Equal("o/r", repo.FullName);
        Assert.Equal("描述", repo.Description);
        Assert.Equal(3, repo.StarsToday);
        Assert.Equal(entry.FetchedAt, back.FetchedAt);
        Assert.Equal(entry.Via, back.Via);
        Assert.True(back.HasContent);
    }

    /// <summary>坏缓存一律当"没有缓存"：它是可重抓的临时数据，坏一次不该让页面打不开。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    public void Decode_TreatsGarbageAsMissing(string? json)
        => Assert.Null(TrendingCacheCodec.Decode(json));

    [Fact]
    public void StateKey_IsNamespacedAndLanguageNormalised()
    {
        Assert.Equal("trending:weekly|rust", TrendingCacheCodec.CacheStateKey(TrendingPeriod.Weekly, " Rust "));
        Assert.Equal("trending:daily|", TrendingCacheCodec.CacheStateKey(TrendingPeriod.Daily, null));
        // 前缀必须有：sync_state 是全局键空间，撞名会把同步检查点当成热榜（或反过来）
        Assert.StartsWith("trending:", TrendingCacheCodec.CacheStateKey(TrendingPeriod.Monthly, "go"));
    }

    /// <summary>跨日判定必须按<b>本地</b>日历日：同一对瞬间在 +08:00 下同日、在 UTC 下分属两日。</summary>
    [Fact]
    public void IsSameLocalDay_DependsOnTheOffset()
    {
        var a = new DateTimeOffset(2026, 9, 22, 22, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var b = new DateTimeOffset(2026, 9, 23, 2, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        Assert.True(TrendingCacheCodec.IsSameLocalDay(a, b, TimeSpan.FromHours(8)));
        Assert.False(TrendingCacheCodec.IsSameLocalDay(a, b, TimeSpan.Zero));
    }

    [Fact]
    public void IsSameLocalDay_DetectsLocalMidnightCrossing()
    {
        var before = new DateTimeOffset(2026, 9, 22, 23, 50, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds();
        var after = new DateTimeOffset(2026, 9, 23, 0, 10, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds();

        Assert.False(TrendingCacheCodec.IsSameLocalDay(before, after, TimeSpan.FromHours(8)));
        Assert.True(TrendingCacheCodec.IsSameLocalDay(before, before, TimeSpan.FromHours(8)));
    }

    [Theory]
    [InlineData(0)]         // 没有抓取时刻
    [InlineData(-5)]        // 非法
    public void DescribeAge_OnMissingTimestamp_SaysNothing(long fetchedAt)
        => Assert.Equal(string.Empty, TrendingCacheCodec.DescribeAge(fetchedAt, DateTimeOffset.UtcNow));

    [Fact]
    public void DescribeAge_UsesMinutesHoursDays()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("刚刚", TrendingCacheCodec.DescribeAge(now.AddSeconds(30).ToUnixTimeSeconds(), now));
        Assert.Equal("10 分钟前", TrendingCacheCodec.DescribeAge(now.AddMinutes(-10).ToUnixTimeSeconds(), now));
        Assert.Contains("小时前", TrendingCacheCodec.DescribeAge(now.AddHours(-3).AddMinutes(-20).ToUnixTimeSeconds(), now));
        Assert.Contains("天前", TrendingCacheCodec.DescribeAge(now.AddDays(-2).ToUnixTimeSeconds(), now));
        // 机器时钟被往回调过：不显示"-2 小时"这种荒谬值
        Assert.Equal("刚刚", TrendingCacheCodec.DescribeAge(now.AddHours(2).ToUnixTimeSeconds(), now));
    }

    /// <summary>
    /// 小时那一档<b>截断而不是四舍五入</b>（批次 SJ）。上面那句只写 <c>Contains("小时前")</c>，
    /// 挡不住"3 小时 59 分写成 4 小时前"——而那正是热榜与 RSS 两处曾差一小时的成因
    /// （<c>NumberText.Grouped(double)</c> 的 "N0" 会四舍五入）。取整方式必须有逐字断言，
    /// 边界要挑<b>真的会翻档</b>的那个值（#194 的空转教训）。
    /// </summary>
    [Fact]
    public void DescribeAge_TruncatesHoursRatherThanRoundingThem()
    {
        var now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("3 小时前",
            TrendingCacheCodec.DescribeAge(now.AddHours(-3).AddMinutes(-59).ToUnixTimeSeconds(), now));
        Assert.Equal("59 分钟前",
            TrendingCacheCodec.DescribeAge(now.AddMinutes(-59).AddSeconds(-40).ToUnixTimeSeconds(), now));
        Assert.Equal("2 天前",
            TrendingCacheCodec.DescribeAge(now.AddDays(-2).AddHours(-20).ToUnixTimeSeconds(), now));
    }
}
