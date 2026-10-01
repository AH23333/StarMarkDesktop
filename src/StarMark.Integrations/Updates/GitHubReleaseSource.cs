#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Updates;

namespace StarMark.Integrations.Updates;

/// <summary>
/// 向 GitHub 问一次"最新发的是哪一版"（批次 UE）。<b>只读，且只读一个 URL</b>。
/// <para>
/// 三条口径是从别处的坑里带来的，不是这里发明的：
/// ① <b>Token 按请求附加、且只发给 <c>api.github.com</c></b>（<c>TrendingFetcher</c> 的同一条）——
///    挂在 <c>DefaultRequestHeaders</c> 上会被任何一次跳转带去做别的请求，那等于把凭据发给一个我们只想要版本号的站点；
///    这一条更是干脆：没有 Token 也能问（公开仓库匿名可读），所以<b>没配凭据时根本不发 Authorization</b>。
/// ② <b>关自动跳转，3xx 一律不跟</b>。跟跳转的 updater 是把"下载源"交给中间人的一条经典路径；
///    今天这里不下载任何东西，但"读到的版本号"同样能被人用一跳 302 换掉，
///    于是界面会一脸诚实地说"有新版本 99.0"。
/// ③ <b>响应体有字节上限</b>：这是外部服务器的输入，代理门户可能回一整页 HTML。
/// </para>
/// <para>
/// 为什么要多发一发去问 <c>/repos/{owner}/{name}</c>：GitHub 对"仓库不存在／你看不见"和"仓库在但一个版都没发"
/// 都回 404。分不清就只能对用户说"查不到"，而这个应用<b>此刻的正确答案恰恰是后者</b>
/// （远端还没有任何 Release）——两种情况的处置完全不同：一个是改配置，一个是"还没有，等着"。
/// </para>
/// </summary>
public sealed class GitHubReleaseSource : IReleaseSource, IDisposable
{
    private const string ApiHost = "api.github.com";
    private const string UserAgent = "StarMarkDesktop-update-check";

    /// <summary>版本号那一句不需要大响应体；超过这个字节数就当它不是 JSON。</summary>
    private const long MaxBodyBytes = 200_000;

    private readonly HttpClient _http;
    private readonly Func<string?>? _tokenProvider;

    /// <param name="tokenProvider">现取 GitHub Token（可空＝匿名问）。按请求附加，见类注释 ①。</param>
    /// <param name="handler">测试缝：假传输。不注入时用"关跳转＋10 秒有界"的默认处理器。</param>
    public GitHubReleaseSource(Func<string?>? tokenProvider = null, HttpMessageHandler? handler = null)
    {
        _tokenProvider = tokenProvider;
        _http = handler is null
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }, disposeHandler: true)
            : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<ReleaseProbeResult> ProbeAsync(string repository, CancellationToken ct = default)
    {
        var releaseUrl = BuildUrl(repository, "/releases/latest");
        if (releaseUrl is null)
            return new ReleaseProbeResult(ReleaseProbeStatus.Unreadable, Detail: "仓库写法不是 owner/name");

        ReleaseProbeResult result;
        try
        {
            var response = await SendAsync(releaseUrl, ct).ConfigureAwait(false);
            if (response is null)
                return new ReleaseProbeResult(ReleaseProbeStatus.TimedOut);

            result = await ReadAsync(response, repository, ct).ConfigureAwait(false);
        }
        catch (ReleaseUnreachableException ex)
        {
            // 出不去是<b>结局</b>不是异常：这一格要一路走到界面那句"这台机器现在连不上 GitHub"，
            // 而不是在日志里留一句"检查异常"（同一族的坑：把可预期的失败当事故处理，就没人再去看它说了什么）。
            return new ReleaseProbeResult(ReleaseProbeStatus.NotReachable, Detail: ex.Message);
        }
        return result;
    }

    /// <summary>拿到答复之后的分流（状态码、跳转、404 的两种含义、正文解析）。</summary>
    private async Task<ReleaseProbeResult> ReadAsync(HttpResponseMessage response, string repository, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        if (status is >= 300 and <= 399)
        {
            response.Dispose();
            // 不跟：见类注释 ②。宁可回答"读不懂"，也不去问一个不是我们写出来的地址。
            return new ReleaseProbeResult(ReleaseProbeStatus.Unreadable, Detail: $"被重定向到别处（HTTP {status}），不跟");
        }

        if (status == 404)
        {
            response.Dispose();
            return await ClassifyNotFoundAsync(repository, ct).ConfigureAwait(false);
        }

        var failure = ClassifyFailure(response);
        if (failure is not null)
        {
            response.Dispose();
            return failure;
        }

        try
        {
            return Parse(await ReadBodyAsync(response, ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ReleaseProbeResult(ReleaseProbeStatus.Unreadable, Detail: $"{ex.GetType().Name}");
        }
        finally
        {
            response.Dispose();
        }
    }

    /// <summary>
    /// 只允许 <c>https://api.github.com/repos/&lt;owner&gt;/&lt;name&gt;/…</c> 这一种形状，而且<b>先验串再看造出来的 URI</b>。
    /// <para>两道各有分工：形状那一道拦的是"这串里带着 URL 的标点"（<c>#</c> 会把后半截变成片段、
    /// <c>?</c> 变成查询、多余的 <c>/</c> 变成别的层级——它们都还在 api.github.com 上，所以只查主机的第二道拦不住，
    /// 而答案已经被换掉了）；主机那一道拦的是"绕过形状规则把宿主换掉"的写法，判据用造完之后的
    /// <see cref="Uri.Host"/> 而不是"传进来的串里有没有 api.github.com"——后者会被
    /// <c>api.github.com.evil.test</c> 这种写法顶过去。</para>
    /// </summary>
    private static string? BuildUrl(string repository, string suffix)
    {
        if (!IsPlainRepositoryName(repository)) return null;
        var builder = new UriBuilder(Uri.UriSchemeHttps, ApiHost, -1, "/repos/" + repository.Trim('/') + suffix);
        if (builder.Uri is not { } uri) return null;
        return !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, ApiHost, StringComparison.OrdinalIgnoreCase) ? null : uri.ToString();
    }

    /// <summary>仓库那串必须是"一段/一段"，且只含 GitHub 实际允许的字符（一次斜杠、无百分号、无空白）。</summary>
    private static bool IsPlainRepositoryName(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository)) return false;
        var slash = repository.IndexOf('/');
        if (slash <= 0 || slash != repository.LastIndexOf('/') || slash == repository.Length - 1) return false;
        foreach (var c in repository)
        {
            if (c == '/') continue;
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') continue;
            return false;
        }
        return true;
    }

    private async Task<HttpResponseMessage?> SendAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(new ProductHeaderValue(UserAgent)));
        var token = _tokenProvider?.Invoke();
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);   // 只在这一次、只对这个地址
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;                                       // 是我们自己的 10 秒上限掐的，不是用户掐的
        }
        catch (OperationCanceledException) { throw; }          // 用户／关机掐的：原样交回，不许降级成"失败"
        catch (HttpRequestException ex)
        {
            throw new ReleaseUnreachableException(ex.Message); // 离线／DNS／代理，见 ClassifyFailure 的调用方
        }
    }

    /// <summary>把 404 分成"仓库看不见"与"仓库还没发布"。</summary>
    private async Task<ReleaseProbeResult> ClassifyNotFoundAsync(string repository, CancellationToken ct)
    {
        try
        {
            var repoUrl = BuildUrl(repository, "");
            if (repoUrl is null) return new ReleaseProbeResult(ReleaseProbeStatus.RepositoryNotVisible);
            using var probe = await SendAsync(repoUrl, ct).ConfigureAwait(false);
            if (probe is null) return new ReleaseProbeResult(ReleaseProbeStatus.TimedOut);
            return (int)probe.StatusCode == 200
                ? new ReleaseProbeResult(ReleaseProbeStatus.NothingPublished, Detail: "仓库在，但一个 Release 都没发")
                : new ReleaseProbeResult(ReleaseProbeStatus.RepositoryNotVisible, Detail: $"HTTP {(int)probe.StatusCode}");
        }
        catch (ReleaseUnreachableException)
        {
            // 第一发已经拿到 404，说明连接本身是通的；第二发却出不去了——按"还没发布"回答更诚实，
            // 因为对方明确说了"这个 releases/latest 没有"，而"看不见"需要仓库级的证据。
            return new ReleaseProbeResult(ReleaseProbeStatus.NothingPublished);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ReleaseProbeResult(ReleaseProbeStatus.Unreadable, Detail: ex.GetType().Name);
        }
    }

    private static ReleaseProbeResult? ClassifyFailure(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (status < 400) return null;
        var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)
            ? System.Linq.Enumerable.FirstOrDefault(values) : null;
        if (status == 403 && string.Equals(remaining?.Trim(), "0", StringComparison.Ordinal))
            return new ReleaseProbeResult(ReleaseProbeStatus.RateLimited, Detail: "X-RateLimit-Remaining: 0");
        if (status == 401 || status == 403)
            return new ReleaseProbeResult(ReleaseProbeStatus.Unauthorized, Detail: $"HTTP {status}");
        if (status == 404) return new ReleaseProbeResult(ReleaseProbeStatus.RepositoryNotVisible);
        if (status >= 500) return new ReleaseProbeResult(ReleaseProbeStatus.ServerError, Detail: $"HTTP {status}");
        return new ReleaseProbeResult(ReleaseProbeStatus.Unreadable, Detail: $"HTTP {status}");
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream);
        var buffer = new char[4096];
        var total = 0L;
        var text = new System.Text.StringBuilder();
        while (await reader.ReadAsync(buffer, ct).ConfigureAwait(false) is var read and > 0)
        {
            total += read;
            if (total > MaxBodyBytes) throw new InvalidOperationException("响应体超出上限");
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }

    /// <summary>只认这几个字段；<c>tag_name</c> 不在或空＝读不懂（不许拿名字或链接凑一个版本号出来）。</summary>
    private static ReleaseProbeResult Parse(string body)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "null" : body);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return new ReleaseProbeResult(ReleaseProbeStatus.Unreadable, Detail: "回来的不是对象");
        var root = document.RootElement;
        if (!root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(tag.GetString()))
            return new ReleaseProbeResult(ReleaseProbeStatus.Unreadable, Detail: "没有 tag_name");

        DateTimeOffset? published = root.TryGetProperty("published_at", out var at)
            && at.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(at.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

        var release = new RemoteRelease(
            tag.GetString()!,
            root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null,
            root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True,
            published);
        // 对方回的 html_url 不读：那条链接由外部服务器说了算，见 RemoteRelease 的注释。
        // 发布页地址是 Core 按配置里的仓库自己拼的（UpdatePolicy.ReleasePageUrl）。
        return new ReleaseProbeResult(ReleaseProbeStatus.Found, release);
    }

    public void Dispose() => _http.Dispose();

    /// <summary>"这台机器出不去"这一类：<see cref="UpdatePolicy"/> 要把它折成 NotReachable，而不是当异常崩到界面上。</summary>
    public sealed class ReleaseUnreachableException : Exception
    {
        public ReleaseUnreachableException(string message) : base(message) { }
    }
}
