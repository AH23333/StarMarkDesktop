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
}
