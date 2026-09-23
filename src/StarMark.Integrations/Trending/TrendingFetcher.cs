#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Trending;

namespace StarMark.Integrations.Trending;

/// <summary>
/// GitHub 热榜抓取：热榜页 HTML 优先，失败自动回退 Search API（与浏览器扩展同一条路线）。
/// 契约类型（<see cref="TrendingSource"/> / <see cref="TrendingFetchResult"/> / <see cref="TrendingException"/> /
/// <see cref="ITrendingSource"/>）都在 <c>StarMark.Abstractions.Trending</c>：编排服务在 Core，
/// 而本仓依赖方向是 <c>Core → Integrations</c>，只有 Abstractions 能同时被两边看见。
/// <para>
/// 三条与安全/稳定性有关的硬约束，改这里前先读：
/// ① <b>Token 只发给 <c>api.github.com</c>，且只按请求附加</b>——绝不放进 <see cref="HttpClient.DefaultRequestHeaders"/>，
///    否则它会被一起发给 <c>github.com</c>（抓 HTML 那条腿），把凭据交给一个我们只解析其 HTML 的站点。
/// ② <b>取消必须往外抛，不许静默降级</b>：兜底是"这条腿失败了"的处置，不是"用户掐了"的处置；
///    把取消当成失败去走兜底，等于用户按了停止却看到另一批数据（P-55 同一失序面）。
/// ③ 响应体<b>有字节上限</b>：这是外部服务器的输入，代理/门户可能回一个巨型页面，
///    一次性 <c>ReadAsStringAsync</c> 会把常驻进程直接撑爆。
/// </para>
/// </summary>
public sealed class TrendingFetcher : ITrendingSource, IDisposable
{
    private const string SearchUrl = "https://api.github.com/search/repositories";
    private const string UserAgent = "StarMarkDesktop/1.0";

    /// <summary>单次响应的字节上限（热榜页典型几百 KB；上限只防异常巨大响应，不防正常长页）。</summary>
    private const long MaxBodyBytes = 4_000_000;

    /// <summary>退避等待的上限：对方要我们等更久就直接走兜底，不挂住页面。</summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;
    private readonly string? _token;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly bool _ownsHttp;

    /// <param name="http">注入以做无网单测；不注入时给一个 20 s 有界的实例（默认 100 s 会冻住界面）。</param>
    /// <param name="token">仅用于 Search API 兜底那条腿（可匿名，带 Token 只是提高配额）。为空则不发凭据。</param>
    /// <param name="delay">等待函数，测试里换成不真等的实现。</param>
    public TrendingFetcher(HttpClient? http = null, string? token = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        _delay = delay ?? Task.Delay;
    }

    /// <summary>抓一期热榜。两条腿都失败 ⇒ 抛 <see cref="TrendingException"/>（消息含两条腿各自的原因）。</summary>
    public async Task<TrendingFetchResult> FetchAsync(TrendingPeriod period, string? language, CancellationToken ct)
    {
        string? htmlNotice = null;
        try
        {
            var (body, notice) = await GetWithOneRetryAsync(
                TrendingHtmlParser.BuildTrendingUrl(period, language), accept: "text/html", sendToken: false, ct);
            htmlNotice = notice;
            if (body is not null)
            {
                var parsed = TrendingHtmlParser.Parse(body);
                if (parsed.Count > 0) return new TrendingFetchResult(parsed, TrendingSource.TrendingHtml, notice);
                htmlNotice = (notice is null ? "" : notice + "；") + "热榜页解析为 0 条（页面可能改版或被拦截成验证页）";
            }
        }
        catch (OperationCanceledException) { throw; }        // 见类注释 ②
        catch (Exception ex) { htmlNotice = "热榜页请求失败：" + ex.Message; }

        try
        {
            var url = SearchUrl + "?" + TrendingHtmlParser.BuildSearchQuery(period, language, DateTimeOffset.UtcNow);
            var (body, notice) = await GetWithOneRetryAsync(url, accept: "application/vnd.github+json", sendToken: true, ct);
            if (body is not null)
            {
                var repos = TrendingHtmlParser.FromSearchApi(ParseSearchRepos(body));
                if (repos.Count > 0)
                    return new TrendingFetchResult(repos, TrendingSource.SearchApi,
                        Compose("热榜页抓取失败，已用 GitHub 搜索接口兜底", htmlNotice, notice));
                notice = Compose("搜索接口没有返回符合该周期/语言的仓库", notice);
            }
            throw new TrendingException(Compose("热榜页与 GitHub 搜索接口都没能给出结果", htmlNotice, notice));
        }
        catch (OperationCanceledException) { throw; }
        catch (TrendingException) { throw; }
        catch (Exception ex)
        {
            throw new TrendingException(Compose("热榜抓取失败", htmlNotice, ex.Message));
        }
    }

    /// <summary>发一次 GET；429/5xx 时按 Retry-After 退避重试<b>一次</b>（扩展有这一步，桌面原先没有）。</summary>
    private async Task<(string? Body, string? Notice)> GetWithOneRetryAsync(
        string url, string accept, bool sendToken, CancellationToken ct)
    {
        string? notice = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.ParseAdd(accept);
            // 只在这一条请求上附加凭据：抓 github.com 页面那趟必须裸奔（见类注释 ①）
            if (sendToken && _token is not null)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            using var resp = await _http.SendAsync(req, ct);

            if (resp.IsSuccessStatusCode)
                return (await ReadCappedAsync(resp.Content, ct), notice);

            var status = (int)resp.StatusCode;
            if (attempt == 0 && (status == 429 || status >= 500) && await BackoffAsync(resp, ct))
            {
                notice = $"第 1 次请求被拒（{status}），已按 Retry-After 退避重试";
                continue;
            }
            return (null, $"HTTP {status} {resp.StatusCode}");
        }
        return (null, notice ?? "重试后仍未成功");
    }

    /// <summary>退避：读 Retry-After（秒数形式）， capped 到 <see cref="MaxBackoff"/>；等不起就返回 false 让调用方走兜底。</summary>
    private async Task<bool> BackoffAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var seconds = resp.Headers.RetryAfter?.Delta;
        if (seconds is null) return false;                       // 日期形式与缺失都不猜，直接兜底
        var wait = TimeSpan.FromSeconds(Math.Clamp(seconds.Value.TotalSeconds, 1, MaxBackoff.TotalSeconds));
        await _delay(wait, ct);
        return true;
    }

    /// <summary>带字节上限地读响应体（超限＝视为失败，而不是把内存吃满）。</summary>
    private static async Task<string> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[64 * 1024];
        var total = 0L;
        using var ms = new MemoryStream();
        while (true)
        {
            var n = await stream.ReadAsync(buffer, ct);
            if (n <= 0) break;
            total += n;
            if (total > MaxBodyBytes)
                throw new TrendingException($"响应超过 {MaxBodyBytes / 1024 / 1024} MB 上限，已放弃解析");
            ms.Write(buffer, 0, n);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Search API 的 items → 我们需要的字段。手工读 JSON：兜底格式是外部输入，缺字段是常态而非异常。</summary>
    private static List<SearchApiRepo> ParseSearchRepos(string json)
    {
        var list = new List<SearchApiRepo>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var it in items.EnumerateArray())
        {
            if (it.ValueKind != JsonValueKind.Object) continue;
            var fullName = Text(it, "full_name");
            if (string.IsNullOrWhiteSpace(fullName)) continue;
            list.Add(new SearchApiRepo(
                fullName.Trim(), Text(it, "html_url") ?? string.Empty,
                Text(it, "description"), Text(it, "language"), Number(it, "stargazers_count")));
        }
        return list;
    }

    private static string? Text(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long Number(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
           && v.TryGetInt64(out var n) ? n : 0;


    /// <summary>
    /// 拼一句给人看的话，并<b>把两条腿各自的原因都带上</b>。
    /// 只报最后一条腿的码会让人去查错的那条腿：热榜页 403 而搜索接口 401 时，
    /// 用户看到"401"就会去折腾 Token，而真正的原因是页面那头被拦。
    /// </summary>
    private static string Compose(string headline, params string?[] notes)
    {
        var kept = new List<string>();
        foreach (var n in notes)
            if (!string.IsNullOrWhiteSpace(n)) kept.Add(n!.TrimEnd('。', '；'));
        return kept.Count == 0 ? headline : headline + "（" + string.Join("；", kept) + "）";
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();     // 注入来的可能是共享实例，不动它
    }
}
