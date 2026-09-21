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
        var json = "[{\"id\":1,\"full_name\":\"owner/repo\",\"language\":\"C#\",\"stargazers_count\":42}]";
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
        var sb = new StringBuilder("[");
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(idOffset + i).Append(",\"full_name\":\"o/r").Append(idOffset + i).Append("\"}");
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
}
