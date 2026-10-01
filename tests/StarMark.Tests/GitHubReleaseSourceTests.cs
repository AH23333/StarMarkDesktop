#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Updates;
using StarMark.Core.Updates;
using StarMark.Integrations.Updates;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// "去 GitHub 问一次"这条路本身（批次 UE）。全程假传输，不触网——
/// 但每一条断言钉的都是<b>真发出去的那个请求</b>（地址、凭据、是否跟跳转），因为这条线上
/// 最贵的错都不是解析错，而是"把 Token 发给别人"和"跟了一跳被别人换了答案"。
/// </summary>
public sealed class GitHubReleaseSourceTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<(string Url, string? Auth, string Agent)> Calls { get; } = new();
        public Func<string, HttpResponseMessage> Respond { get; set; } =
            _ => Json("{\"tag_name\":\"v1.1.0\",\"name\":\"StarMark 1.1.0\",\"prerelease\":false,"
                + "\"html_url\":\"https://github.com/AH23333/StarMarkDesktop/releases/tag/v1.1.0\","
                + "\"published_at\":\"2026-10-01T10:00:00Z\"}");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Calls.Add((url, request.Headers.Authorization?.Parameter,
                request.Headers.UserAgent.FirstOrDefault()?.ToString() ?? ""));
            return Task.FromResult(Respond(url));
        }
    }

    private static HttpResponseMessage Json(string body)
        => new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(System.Net.HttpStatusCode code, string? retryRemaining = null)
    {
        var response = new HttpResponseMessage(code) { Content = new StringContent("{}") };
        if (retryRemaining is not null) response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", retryRemaining);
        return response;
    }

    [Fact]
    public async Task ItAsksTheLatestReleaseOfExactlyTheConfiguredRepository()
    {
        var handler = new FakeHandler();
        var probe = new GitHubReleaseSource(tokenProvider: null, handler);

        var result = await probe.ProbeAsync("AH23333/StarMarkDesktop");

        Assert.Equal(ReleaseProbeStatus.Found, result.Status);
        Assert.Equal("v1.1.0", result.Release!.Tag);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), result.Release.PublishedUtc);
        Assert.Equal(["https://api.github.com/repos/AH23333/StarMarkDesktop/releases/latest"],
            handler.Calls.Select(c => c.Url).ToArray());
        // GitHub 对没有 User-Agent 的请求直接 403，而假处理器会照常回 200 —— 这一格是"真机上根本发不出去"的唯一凭据。
        Assert.Equal("StarMarkDesktop-update-check", Assert.Single(handler.Calls).Agent);
    }

    /// <summary>
    /// 对方回的 <c>html_url</c> 不许进任何数据结构。它想写哪儿就写哪儿，而这条线上唯一往屏幕外走的一步
    /// 就是"用浏览器打开一个地址"——那个地址必须由本程序按配置里的仓库自己拼（<c>UpdatePolicy.ReleasePageUrl</c>）。
    /// </summary>
    [Theory]
    [InlineData("{\"tag_name\":\"v1.1.0\",\"html_url\":\"https://evil.example.test/phish\"}")]
    [InlineData("{\"tag_name\":\"v1.1.0\",\"html_url\":\"javascript:alert(1)\"}")]
    [InlineData("{\"tag_name\":\"v1.1.0\",\"tarball_url\":\"https://evil.example.test/a.zip\"}")]
    public async Task ALinkFromTheOtherSideIsNotEvenReadIntoTheResult(string body)
    {
        var handler = new FakeHandler { Respond = _ => Json(body) };
        var result = await new GitHubReleaseSource(null, handler).ProbeAsync("a/b");

        Assert.Equal(ReleaseProbeStatus.Found, result.Status);
        Assert.Equal("v1.1.0", result.Release!.Tag);
        Assert.DoesNotContain("evil.example.test", result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>没配凭据就<b>根本不发 Authorization</b>：这条功能匿名可读，凭据不是必需品。</summary>
    [Fact]
    public async Task NoTokenMeansNoAuthorizationHeaderAtAll()
    {
        var handler = new FakeHandler();
        await new GitHubReleaseSource(tokenProvider: () => "   ", handler).ProbeAsync("a/b");
        Assert.Null(Assert.Single(handler.Calls).Auth);
    }

    [Fact]
    public async Task AConfiguredTokenIsAttachedToTheSingleApiRequest()
    {
        var handler = new FakeHandler();
        await new GitHubReleaseSource(tokenProvider: () => "secret-token", handler).ProbeAsync("a/b");
        Assert.Equal("secret-token", Assert.Single(handler.Calls).Auth);
    }

    /// <summary>
    /// Token 不许跟着跳转跑出去。这条不修的话，凭据可能落在一个我们只想要版本号的站点上（BJ 那批的口径）。
    /// <para>这里能证的是<b>我们的代码收到 3xx 后不再发第二发</b>；"连框架那一跳也不给"由下面
    /// <see cref="TheProductionHandlerDisallowsRedirects"/> 的形态闸门钉住（注入假处理器时框架那条臂根本不会走到）。</para>
    /// </summary>
    [Fact]
    public async Task ARedirectIsNotFollowedEvenWhenItPointsSomewhereFriendly()
    {
        var handler = new FakeHandler { Respond = _ => Redirect("https://evil.example.test/repos/a/b") };

        var result = await new GitHubReleaseSource(tokenProvider: () => "secret-token", handler).ProbeAsync("a/b");

        Assert.Equal(ReleaseProbeStatus.Unreadable, result.Status);
        Assert.Single(handler.Calls);                             // 第二跳根本没发出去
        Assert.Contains("重定向", result.Detail);
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found) { Content = new StringContent("") };
        response.Headers.Location = new Uri(location);
        return response;
    }

    /// <summary>
    /// 404 有两种意思，而这一批第一次上线时<b>后一种才是事实</b>：分不清就只能说"查不到"，
    /// 于是用户以为功能坏了，而它说的其实是"还没有东西可更"。
    /// </summary>
    [Fact]
    public async Task AForbiddenRepositoryAndAnUnpublishedOneAreToldApart()
    {
        var published = new FakeHandler { Respond = url => url.EndsWith("/releases/latest")
            ? Status(System.Net.HttpStatusCode.NotFound) : Json("{}") };
        var missing = new FakeHandler { Respond = _ => Status(System.Net.HttpStatusCode.NotFound) };

        Assert.Equal(ReleaseProbeStatus.NothingPublished,
            (await new GitHubReleaseSource(null, published).ProbeAsync("a/b")).Status);
        Assert.Equal(ReleaseProbeStatus.RepositoryNotVisible,
            (await new GitHubReleaseSource(null, missing).ProbeAsync("a/b")).Status);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Unauthorized, null, ReleaseProbeStatus.Unauthorized)]
    [InlineData(System.Net.HttpStatusCode.Forbidden, null, ReleaseProbeStatus.Unauthorized)]
    [InlineData(System.Net.HttpStatusCode.Forbidden, "0", ReleaseProbeStatus.RateLimited)]
    [InlineData(System.Net.HttpStatusCode.InternalServerError, null, ReleaseProbeStatus.ServerError)]
    [InlineData(System.Net.HttpStatusCode.BadGateway, null, ReleaseProbeStatus.ServerError)]
    [InlineData(System.Net.HttpStatusCode.NotAcceptable, null, ReleaseProbeStatus.Unreadable)]
    public async Task EachAnswerFromTheOtherSideKeepsItsOwnKind(
        System.Net.HttpStatusCode code, string? remaining, ReleaseProbeStatus expected)
    {
        var handler = new FakeHandler { Respond = _ => Status(code, remaining) };
        var result = await new GitHubReleaseSource(null, handler).ProbeAsync("a/b");
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task AnUnreachableHostIsAnAnswerNotAnAccident()
    {
        var handler = new FakeHandler { Respond = _ => throw new System.Net.Http.HttpRequestException("no route") };
        var result = await new GitHubReleaseSource(null, handler).ProbeAsync("a/b");
        Assert.Equal(ReleaseProbeStatus.NotReachable, result.Status);
        Assert.Contains("no route", result.Detail);
    }

    [Fact]
    public async Task AStoppedHostIsBackOfTheCancelledTokenAndNotDowngradedToAFailure()
    {
        var handler = new FakeHandler { Respond = _ => throw new OperationCanceledException() };
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GitHubReleaseSource(null, handler).ProbeAsync("a/b", cancel.Token));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"tag_name\":\"\"}")]
    [InlineData("{\"name\":\"v1.1.0\"}")]                  // 名字里有版本也不许拿它凑
    [InlineData("{\"tag_name\":123}")]
    [InlineData("[]")]
    [InlineData("<html>login</html>")]                     // 代理门户回一整页 HTML
    public async Task ABodyWithoutAVersionTagIsReportedAsUnreadable(string body)
    {
        var handler = new FakeHandler { Respond = _ => Json(body) };
        var result = await new GitHubReleaseSource(null, handler).ProbeAsync("a/b");
        Assert.Equal(ReleaseProbeStatus.Unreadable, result.Status);
    }

    [Fact]
    public async Task AnOversizedAnswerIsRefusedRatherThanBuffered()
    {
        var handler = new FakeHandler { Respond = _ => Json("{\"tag_name\":\"v1.1.0\",\"body\":\"" + new string('x', 400_000) + "\"}") };
        var result = await new GitHubReleaseSource(null, handler).ProbeAsync("a/b");
        Assert.Equal(ReleaseProbeStatus.Unreadable, result.Status);   // 外部服务器的输入要有上限（TrendingFetcher 同一条）
    }

    /// <summary>
    /// 仓库那串会被拼进请求路径，所以造完必须再验一次"这确实还在 api.github.com 上"——
    /// 传进来一个带协议头的东西时，宁可不问。
    /// </summary>
    [Theory]
    [InlineData("a/../../evil")]
    [InlineData("evil.test/#/x")]
    public async Task ARepositoryStringThatCouldMoveTheHostIsRefusedBeforeAnyRequest(string repository)
    {
        var handler = new FakeHandler();
        var result = await new GitHubReleaseSource(null, handler).ProbeAsync(repository);
        Assert.Equal(ReleaseProbeStatus.Unreadable, result.Status);
        Assert.Empty(handler.Calls);                     // 一次网都没上，而不是"发出去再指望框架拦住"
    }

    // ---- 分层：契约在 Abstractions、抓取在 Integrations、编排才在 Core ----

    [Fact]
    public void TheProbeNeitherWritesNorJudgesTheVerdict()
    {
        var code = Code(ReadRepoFile("src/StarMark.Integrations/Updates/GitHubReleaseSource.cs"));
        Assert.Equal(1, Count(code, "api.github.com"));                       // 主机名只这一处，别处拼不出来
        Assert.DoesNotContain("File.", code, StringComparison.Ordinal);         // 只读：不写盘、不换文件（✅P-144 的边界）
        Assert.DoesNotContain("Process.Start", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultRequestHeaders", code, StringComparison.Ordinal);   // 凭据只按请求附加
        Assert.DoesNotContain("UpdateVerdict", code, StringComparison.Ordinal);           // 分类是 Core 的事
        Assert.Contains("Task<ReleaseProbeResult> ProbeAsync", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// 生产那条路必须<b>把框架的自动跳转关掉</b>。上面那条行为用例注入的是假处理器，
    /// 框架那一跳根本不会走到——真机上"关掉"这件事只有照着构造处才钉得住（同 #184：UI 层之外也要认形态判据）。
    /// </summary>
    [Fact]
    public void TheProductionHandlerDisallowsRedirects()
    {
        var code = Code(ReadRepoFile("src/StarMark.Integrations/Updates/GitHubReleaseSource.cs"));
        Assert.Equal(1, Count(code, "AllowAutoRedirect = false"));
        Assert.Equal(1, Count(code, "Timeout = TimeSpan.FromSeconds(10)"));     // 有界，且只这一处
    }
}
