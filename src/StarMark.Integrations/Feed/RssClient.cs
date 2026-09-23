#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StarMark.Integrations.Feed;

/// <summary>一次抓取的结果状态。<b>304 与失败必须分开</b>："源说没变化"是好消息，
/// 混进失败里会让用户去修一个根本没坏的地址。</summary>
public enum RssFetchStatus
{
    Content,
    NotModified,
    Failure,
}

/// <param name="Status">见 <see cref="RssFetchStatus"/>。</param>
/// <param name="Body">文档原文（仅 <see cref="RssFetchStatus.Content"/> 时有值）。</param>
/// <param name="ETag">源返回的强/弱校验符，下次带回去（可为 null：很多源不给）。</param>
/// <param name="LastModified">同上。</param>
/// <param name="Error">失败原因，带 HTTP 状态码——"403"与"404"要做的完全是两件事。</param>
public sealed record RssFetchResult(
    RssFetchStatus Status,
    string? Body,
    string? ETag,
    string? LastModified,
    string? Error)
{
    public bool Ok => Status != RssFetchStatus.Failure;
}

/// <summary>
/// 抓一份订阅文档。<b>本类不解析任何东西</b>（那在 <c>Core.Feed.RssParser</c>，可单测），
/// 这里只负责"拿得到就说拿到、拿不到就说清是哪一种拿不到"。
/// <para>
/// 与 GitHub 那条链同一套纪律：<b>自己 new 的 HttpClient 必须有界</b>（默认 100 秒，
/// 代理/门户网络下 TCP 连上但不回包时会把整页 UI 卡住），并且响应体<b>限长</b>——
/// 订阅地址是用户输入的，对方返回 2GB 也是可能的。
/// </para>
/// </summary>
public sealed class RssClient : IDisposable
{
    /// <summary>响应体上限（字节）。超过就断开不读了。</summary>
    public const long MaxBodyBytes = 4L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public RssClient(HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (_ownsClient)
        {
            // 不少站点没有 UA 直接 403；也不该让源方看到的是 ".NET Core 3.0" 这种没法归因的串
            _http.DefaultRequestHeaders.UserAgent.Clear();
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StarMark", "1.0"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/rss+xml"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/atom+xml"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        }
    }

    /// <summary>
    /// 抓取。<paramref name="etag"/>/<paramref name="lastModified"/> 是上次源给的校验符，带上就走条件请求
    /// （源回 304 时不重复下载，也不覆盖已经解析好的列表）。
    /// </summary>
    public async Task<RssFetchResult> FetchAsync(string url, string? etag, string? lastModified, CancellationToken ct)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var target)
            || (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
            return new RssFetchResult(RssFetchStatus.Failure, null, null, null, "这个地址不是 http/https，没有去抓");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            if (!string.IsNullOrWhiteSpace(etag)) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            if (!string.IsNullOrWhiteSpace(lastModified)) request.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);

            // ResponseHeadersRead：先把头部拿回来，正文限长读完就断——不然对方可以一直喂
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotModified)
                return new RssFetchResult(RssFetchStatus.NotModified, null, etag, lastModified, null);
            if (!response.IsSuccessStatusCode)
                return new RssFetchResult(RssFetchStatus.Failure, null, null, null,
                    $"源返回 {(int)response.StatusCode} {response.ReasonPhrase}");

            var bytes = await ReadBoundedAsync(response, ct);
            if (bytes is null)
                return new RssFetchResult(RssFetchStatus.Failure, null, null, null,
                    $"这个源返回的内容超过 {MaxBodyBytes / 1024 / 1024}MB，已中止读取");

            return new RssFetchResult(RssFetchStatus.Content, Decode(bytes),
                response.Headers.ETag?.Tag ?? First(response, "ETag"),
                First(response, "Last-Modified"), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;                                  // 用户按了取消就原样上抛，别记成"源坏了"
        }
        catch (Exception ex)
        {
            return new RssFetchResult(RssFetchStatus.Failure, null, null, null, ex.Message);
        }
    }

    private static string? First(HttpResponseMessage response, string header)
    {
        if (response.Headers.TryGetValues(header, out var values))
            foreach (var value in values) return value;
        if (response.Content.Headers.TryGetValues(header, out var contentValues))
            foreach (var value in contentValues) return value;
        return null;
    }

    private static async Task<byte[]?> ReadBoundedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        // 预分配大小要在转 int 之前夹住：Content-Length 是个可以谎报很大的头
        var declared = response.Content.Headers.ContentLength ?? 32_768;
        var capacity = (int)Math.Clamp(declared <= 0 ? 32_768 : declared, 32_768, MaxBodyBytes);
        using var buffer = new MemoryStream(capacity);
        var chunk = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read <= 0) break;
            total += read;
            if (total > MaxBodyBytes) return null;      // 超了：不继续读，直接判坏源
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// 按声明的编码解码，没有声明就按 UTF-8（并吃掉 BOM）。
    /// <b>不能直接用 <c>ReadAsStringAsync</c></b>：那会把整份响应无上限读进内存，
    /// 而上面那条限长就没有意义了。
    /// </summary>
    private static string Decode(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
