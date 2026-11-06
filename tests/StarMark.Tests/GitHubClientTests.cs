#nullable enable
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Xunit;
using StarMark.Integrations.GitHub;

namespace StarMark.Tests;

/// <summary>
/// GitHubClient 的 P1-4 行为：条件请求（304 短路）、ETag 捕获、错误分类（401/403/429）。
/// 用脚本化的 HttpMessageHandler 模拟 GitHub REST 响应，不触网。
/// </summary>
public sealed class GitHubClientTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }

    private static GitHubOptions ConfiguredOptions()
        => new() { Token = "ghp_testtoken", PageSize = 100 };

    [Fact]
    public async Task NotModified_OnFirstPage_ReturnsEmptyAndKeepsPriorEtag()
    {
        const string prior = "\"etag-keep\"";
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified)
        {
            Headers = { ETag = new EntityTagHeaderValue(prior) },
        });
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler))
        {
            CachedETag = prior,
        };

        var all = await client.GetAllStarredAsync(CancellationToken.None);

        Assert.Empty(all);
        Assert.Equal(prior, client.CachedETag);
    }

    [Fact]
    public async Task Success_StoresEtagAndParsesItems()
    {
        const string etag = "\"etag-new\"";
        // P-6A：/user/starred 假桩一律用 star+json 信封形（{"starred_at":…,"repo":{…}}）。
        var json = "[{\"starred_at\":\"2024-05-01T12:00:00Z\",\"repo\":{\"id\":1,\"full_name\":\"owner/repo\",\"language\":\"C#\",\"stargazers_count\":42}}]";
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Headers = { ETag = new EntityTagHeaderValue(etag) },
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler))
        {
            CachedETag = "\"stale\"",
        };

        var all = await client.GetAllStarredAsync(CancellationToken.None);

        Assert.Single(all);
        Assert.Equal("owner/repo", all[0].FullName);
        Assert.Equal("2024-05-01T12:00:00Z", all[0].StarredAtRaw);   // P-6A：信封解开、真值盖进了模型
        Assert.Equal(etag, client.CachedETag); // 首页 ETag 已刷新
    }

    [Fact]
    public async Task Unauthorized_ThrowsAuthError()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(
            () => client.GetAllStarredAsync(CancellationToken.None));
        Assert.Equal(GitHubErrorKind.Auth, ex.Kind);
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task TooManyRequests_ThrowsRateLimit()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage((HttpStatusCode)429));
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(
            () => client.GetAllStarredAsync(CancellationToken.None));
        Assert.Equal(GitHubErrorKind.RateLimit, ex.Kind);
    }

    [Fact]
    public async Task Forbidden_WithRateLimitHeader_ThrowsRateLimit()
    {
        var handler = new ScriptedHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Forbidden);
            r.Headers.Add("X-RateLimit-Remaining", "0");
            return r;
        });
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(
            () => client.GetAllStarredAsync(CancellationToken.None));
        Assert.Equal(GitHubErrorKind.RateLimit, ex.Kind);
    }

    [Fact]
    public async Task Forbidden_WithoutRateLimitHeader_ThrowsForbidden()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(
            () => client.GetAllStarredAsync(CancellationToken.None));
        Assert.Equal(GitHubErrorKind.Forbidden, ex.Kind);
    }

    [Fact]
    public async Task ServerError_ThrowsServer()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(
            () => client.GetAllStarredAsync(CancellationToken.None));
        Assert.Equal(GitHubErrorKind.Server, ex.Kind);
    }

    [Fact]
    public async Task ConditionalRequest_SendsIfNoneMatchOnFirstPage()
    {
        const string prior = "\"etag-sent\"";
        string? sent = null;
        var handler = new ScriptedHandler(req =>
        {
            sent = req.Headers.IfNoneMatch.FirstOrDefault()?.Tag;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        });
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler))
        {
            CachedETag = prior,
        };

        await client.GetAllStarredAsync(CancellationToken.None);

        Assert.Equal(prior, sent); // 首页带上了 If-None-Match
    }

    // ---- 批次 AK：GetAllStarredAsync 分页循环护栏（此前仅测单页，翻页停止条件无覆盖）----

    private static string RepoArray(int count, int idOffset)
    {
        // P-6A：假桩与真实 Accept 讲价结果同形——star+json 信封（starred_at＋内嵌 repo）。
        var sb = new StringBuilder("[");
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"starred_at\":\"2024-05-01T12:00:00Z\",\"repo\":{\"id\":").Append(idOffset + i)
              .Append(",\"full_name\":\"o/r").Append(idOffset + i).Append("\"}}");
        }
        return sb.Append(']').ToString();
    }

    private static HttpResponseMessage OkJson(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static int PageOf(HttpRequestMessage req)
    {
        // URL 形如 .../user/starred?per_page=100&page=N —— 锚定 "&page=" 以免误命中 "per_page="。
        var query = req.RequestUri!.Query;
        const string key = "&page=";
        var idx = query.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        return idx >= 0 && int.TryParse(query.AsSpan(idx + key.Length), out var p) ? p : 1;
    }

    [Fact]
    public async Task GetAllStarredAsync_PaginatesWhilePagesAreFull_AndStopsOnPartialPage()
    {
        // PageSize=100：页1/页2 各满 100 → 继续翻页；页3 只有 30（<PageSize）→ 停止。
        var requested = new List<int>();
        var handler = new ScriptedHandler(req =>
        {
            var page = PageOf(req);
            requested.Add(page);
            var n = page <= 2 ? 100 : 30;
            return OkJson(RepoArray(n, (page - 1) * 100));
        });
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler));

        var all = await client.GetAllStarredAsync(CancellationToken.None);

        Assert.Equal(230, all.Count);
        Assert.Equal(new[] { 1, 2, 3 }, requested); // 恰请求三页，页3 后未再请求页4
        Assert.Equal(0, all[0].Id);
        Assert.Equal(229, all[^1].Id);
    }

    [Fact]
    public async Task GetAllStarredAsync_StopsImmediatelyOnEmptyFirstPage()
    {
        // 空列表（0 stars 或首页即空）→ 一次请求即停，不请求页2。
        var requested = new List<int>();
        var handler = new ScriptedHandler(req => { requested.Add(PageOf(req)); return OkJson("[]"); });
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler));

        var all = await client.GetAllStarredAsync(CancellationToken.None);

        Assert.Empty(all);
        Assert.Equal(new[] { 1 }, requested);
    }

    [Fact]
    public async Task GetAllStarredAsync_ExactlyOneFullPageThenEmptyStops()
    {
        // 边界：用户 star 数恰为 PageSize 整数倍。页1 满 100 → 继续；页2 空 → break（非 partial 判定，走空判定）。
        var requested = new List<int>();
        var handler = new ScriptedHandler(req =>
        {
            var page = PageOf(req);
            requested.Add(page);
            return OkJson(page == 1 ? RepoArray(100, 0) : "[]");
        });
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler));

        var all = await client.GetAllStarredAsync(CancellationToken.None);

        Assert.Equal(100, all.Count);
        Assert.Equal(new[] { 1, 2 }, requested);
    }

    // ---- 批次 AZ-3：per_page 越界（github.json 属外部输入）→ 与 GitHub 100 上限的同源钳制 ----

    private static int PerPageOf(HttpRequestMessage req)
    {
        // URL 查询形如 per_page=N&page=M —— 锚定 "per_page="，取到下一个 '&' 为止的数值。
        var query = req.RequestUri!.Query;
        const string key = "per_page=";
        var idx = query.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return -1;
        var start = idx + key.Length;
        var amp = query.IndexOf('&', start);
        var digits = amp >= 0 ? query[start..amp] : query[start..];
        return int.TryParse(digits, out var n) ? n : -1;
    }

    [Fact]
    public async Task GetAllStarredAsync_PageSizeAboveGitHubCap_StillFetchesAllPages()
    {
        // 复现缺陷：PageSize=200 但 GitHub 静默按 100/页返回。旧实现把停止判定用原始 PageSize，
        // 于是「返回 100 < 200」被误判末页 → 只拉一页、静默丢页2+ 的 Star。钳制修复后须拉全 240 条。
        var requestedPerPage = new List<int>();
        var handler = new ScriptedHandler(req =>
        {
            requestedPerPage.Add(PerPageOf(req));
            var page = PageOf(req);
            // GitHub 真实行为：无论请求多大，每页最多 100。页1/页2 各 100，页3 只有 40。
            return OkJson(page <= 2 ? RepoArray(100, (page - 1) * 100) : RepoArray(40, 200));
        });
        using var client = new GitHubClient(
            new GitHubOptions { Token = "ghp_test", PageSize = 200 }, new HttpClient(handler));

        var all = await client.GetAllStarredAsync(CancellationToken.None);

        Assert.Equal(240, all.Count);
        Assert.All(requestedPerPage, pp => Assert.Equal(100, pp)); // 每页请求都被钳到 100
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(101, 100)]
    [InlineData(5000, 100)]
    [InlineData(100, 100)]
    public async Task GetStarredPageAsync_PerPageSent_IsClampedToGitHubRange(int pageSize, int expectedPerPage)
    {
        // per_page 发给 GitHub 必须落在 [1,100]：越界 github.json 配置不得原样拼进 URL。
        var sent = -1;
        var handler = new ScriptedHandler(req =>
        {
            sent = PerPageOf(req);
            return OkJson("[]");
        });
        using var client = new GitHubClient(
            new GitHubOptions { Token = "ghp_test", PageSize = pageSize }, new HttpClient(handler));

        await client.GetStarredPageAsync(1, CancellationToken.None);

        Assert.Equal(expectedPerPage, sent);
    }

    // ---- 片214·P-6A：star+json 信封（starred_at 真值）的请求腿与解析腿 ----

    [Fact]
    public async Task GetStarredPageAsync_SendsStarPlusJsonAccept_ExactlyOncePerRequest()
    {
        // starred_at 真值靠请求级 Accept 讲价，外发头恰为 star+json 这一颗——ctor 补进
        // DefaultRequestHeaders 的 vnd.github+json 只在请求缺席 Accept 时才合并进来；
        // 若哪天两组被合并、star+json 被挤到第二位，GitHub 的内容协商就可能退回裸 repo 数组，
        // 这颗断言就是为那种合并病准备的（外发现场由 handler 作证，不是推断）。
        string? accept = null;
        var handler = new ScriptedHandler(req =>
        {
            accept = string.Join(", ", req.Headers.Accept.Select(h => h.MediaType));
            return OkJson("[]");
        });
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler));

        await client.GetStarredPageAsync(1, CancellationToken.None);

        Assert.Equal("application/vnd.github.star+json", accept);
    }

    [Fact]
    public async Task GetStarredPageAsync_ParsesEnvelope_AndStampsStarredAtRaw()
    {
        const string starredAt = "2026-02-03T04:05:06Z";
        var json = $"[{{\"starred_at\":\"{starredAt}\",\"repo\":{{\"id\":7,\"full_name\":\"o/r7\"}}}}]";
        var handler = new ScriptedHandler(_ => OkJson(json));
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler));

        var page = await client.GetStarredPageAsync(1, CancellationToken.None);

        Assert.Single(page);
        Assert.Equal("o/r7", page[0].FullName);
        Assert.Equal(starredAt, page[0].StarredAtRaw);
    }

    [Fact]
    public async Task GetStarredPageAsync_EnvelopeWithoutRepo_ThrowsLoudly()
    {
        // 响应形状与 Accept 不符（中间盒剥头、GitHub 退回裸 repo 数组）→ 抛而不是静默吞成"少一批"：
        // 裸数组解进信封是 Repo=null，必须当场炸给界面。
        var json = "[{\"id\":1,\"full_name\":\"owner/repo\"}]";
        var handler = new ScriptedHandler(_ => OkJson(json));
        using var client = new GitHubClient(ConfiguredOptions(), new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(
            () => client.GetStarredPageAsync(1, CancellationToken.None));
        Assert.Equal(GitHubErrorKind.Unknown, ex.Kind);
    }

    // ---- 片212·P-23：注入 HttpClient 的所有权与头的幂等 ----

    /// <summary>
    /// 记得自己有没有被 Dispose 的 handler：注入那条臂上"客户端是否代为释放"由它作证，
    /// ⛔ 靠"下一发请求碰巧还能用"那种间接读数。
    /// </summary>
    private sealed class WatchfulHandler : HttpMessageHandler
    {
        public bool Disposed;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(OkJson("[]"));

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void InjectedHttpClient_IsNotDisposed_WithTheClient()
    {
        // 注入的 HttpClient 归调用方所有：客户端 Dispose 代为释放＝双重释放＋共享者的连接被拔。
        var handler = new WatchfulHandler();
        using var http = new HttpClient(handler);
        using var client = new GitHubClient(ConfiguredOptions(), http);

        client.Dispose();

        Assert.False(handler.Disposed);   // 客户端的 Dispose ⛔ 碰注入的 HttpClient

        // 所有权没被抢走、只是没被代劳：注入方自己的 Dispose 仍然收得走。
        http.Dispose();
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task SharedHttpClient_StillServesRequests_AfterClientDispose()
    {
        // 释放之后共享实例照常干活：这颗读数把"没代为 Dispose"与"没被动过手脚"一起证掉。
        var handler = new WatchfulHandler();
        using var http = new HttpClient(handler);
        using var client = new GitHubClient(ConfiguredOptions(), http);

        client.Dispose();

        using var resp = await http.GetAsync("https://unit.test/");
        Assert.True(resp.IsSuccessStatusCode);
    }

    [Fact]
    public async Task OwnedHttpClient_IsDisposed_WithTheClient()
    {
        // 自建那条臂（DI 单例今天走的就是它）行为不变：Dispose 仍收走自己 new 的 HttpClient。
        // 读数用 SendAsync 入口：它开头就有 disposed 守卫（在碰 handler/网络之前同步抛），
        // ⛔ 用 DefaultRequestHeaders 那条路——释放后它照常可摸，读不出"已收走"。
        using var client = new GitHubClient(ConfiguredOptions());

        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.GetStarredPageAsync(1, CancellationToken.None));
    }

    [Fact]
    public void Constructor_LeavesTheOwnersUserAgentAlone_AndDoesNotStackOurs()
    {
        // 主人带着自己的 UA：既不被顶掉（wipe），也不叠出第二份（无条件 ParseAdd 的旧病）。
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OwnerApp/2.0");
        using var client = new GitHubClient(ConfiguredOptions(), http);

        Assert.Equal("OwnerApp/2.0", http.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public void Constructor_AddsUserAgentAndAccept_WhenOwnerCarriesNone()
    {
        // 缺席才加的另一臂：主人没带，GitHub 要的 UA 与 Accept 由客户端补齐。
        using var http = new HttpClient();
        using var client = new GitHubClient(ConfiguredOptions(), http);

        Assert.Equal("StarMarkDesktop/1.0", http.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Equal("application/vnd.github+json", http.DefaultRequestHeaders.Accept.Single().MediaType);
    }

    [Fact]
    public void TwoClientsOnOneHttpClient_DoNotStackHeaders()
    {
        // 幂等：同一颗 HttpClient 先后被两颗客户端接管（共享注入的未来形状），头不叠份。
        using var http = new HttpClient();
        using var first = new GitHubClient(ConfiguredOptions(), http);
        using var second = new GitHubClient(ConfiguredOptions(), http);

        Assert.Single(http.DefaultRequestHeaders.UserAgent);
        Assert.Single(http.DefaultRequestHeaders.Accept, h => h.MediaType == "application/vnd.github+json");
    }
}
