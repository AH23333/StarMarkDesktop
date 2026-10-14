#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.I18n;

namespace StarMark.Integrations.Ai;

/// <summary>一次 HTTP 往返的结果。<b>"没连上"与"连上了但报错"必须是两种形状</b>：
/// 前者只有异常文字可用，后者有状态码与响应体，而 <see cref="AiFailures"/> 要靠后者分类。</summary>
internal sealed record AiTransport(
    bool Connected,
    int? Status,
    string? Body,
    AiFailureKind Failure = AiFailureKind.Unknown,
    LocMessage? Problem = null,
    bool Truncated = false)
{
    public bool Succeeded => Connected && Status is >= 200 and <= 299;
}

/// <summary>
/// AI 通道的传输层。<b>只干"发出去、有界地读回来、把异常翻成种类"这三件事</b>，
/// 请求体的形状与答复的解读都在 <see cref="AiWire"/>（那边是纯函数，可单测）。
/// <para>
/// 超时由每一次调用自己带（本地大模型首次装载可能要一两分钟，而"列一下有哪些模型"要快），
/// 所以 <see cref="HttpClient.Timeout"/> 一律置为无限——<b>把两种时限混在同一个地方，
/// 结果一定是其中一种被另一种将就</b>。
/// </para>
/// </summary>
internal static class AiHttp
{
    /// <summary>响应体上限。批量分类一批 50 条的正常答复在几十 KB 量级，超过这个数就是答疯了或有人在灌。</summary>
    public const long MaxReplyBytes = 4L * 1024 * 1024;

    /// <summary>把"这一发答了什么"翻成给用户看的那句明细。<b>只这一处</b>：两家的四发出口都走它，
    /// ⛔ 在每一发那里各写一遍"先试它的错误字段、读不出来就交原文"（同一件事两处实现迟早分岔）。
    /// <para>原文（响应体、异常正文）是<b>机器事实</b> ⇒ 走 <c>common.rawValue</c> 那道数据门；
    /// 被截断那一支另判：那条信息说的是"我们为什么没读完"，⛔ 冒充成响应体塞进 <see cref="AiTransport.Body"/>
    /// 让解析器去撞（今天就是这么做的，于是那句中文成品字挂在一个数据字段里，⛔ 被取词也⛔ 被翻译）。</para></summary>
    internal static LocMessage? DetailOf(AiTransport transport)
    {
        if (transport.Truncated) return LocMessage.Of("ai.transport.replyTooBig", MaxReplyBytes / 1024 / 1024);
        var text = AiWire.ReadErrorText(transport.Body) ?? transport.Body;
        return text is null ? null : LocMessage.Of("common.rawValue", text);
    }


    public static async Task<AiTransport> PostAsync(
        HttpClient http, string url, string json, IReadOnlyDictionary<string, string>? headers,
        int timeoutSeconds, CancellationToken ct)
    {
        using var linked = Link(timeoutSeconds, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        Apply(request, headers);
        return await SendAsync(http, request, linked.Token, ct);
    }

    public static async Task<AiTransport> GetAsync(
        HttpClient http, string url, IReadOnlyDictionary<string, string>? headers, int timeoutSeconds, CancellationToken ct)
    {
        using var linked = Link(timeoutSeconds, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Apply(request, headers);
        return await SendAsync(http, request, linked.Token, ct);
    }

    private static CancellationTokenSource Link(int timeoutSeconds, CancellationToken ct)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        return linked;
    }

    private static void Apply(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null) return;
        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);     // Key 里出现怪字符时宁可缺头也不要抛在界面上
    }

    private static async Task<AiTransport> SendAsync(
        HttpClient http, HttpRequestMessage request, CancellationToken bounded, CancellationToken userToken)
    {
        try
        {
            // ResponseHeadersRead + 限长读：服务方一直喂也不会把内存吃掉
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded);
            var (body, truncated) = await ReadBoundedAsync(response, bounded);
            return new AiTransport(true, (int)response.StatusCode, body, Truncated: truncated);
        }
        catch (OperationCanceledException) when (userToken.IsCancellationRequested)
        {
            throw;      // 用户按了停止：这是控制流，不能记成"这个服务连不上"
        }
        catch (OperationCanceledException)
        {
            return new AiTransport(false, null, null, AiFailureKind.TimedOut, LocMessage.Of("ai.transport.waitedOut"));
        }
        catch (HttpRequestException ex)
        {
            // 端口没人监听 / DNS 解析不出来 / 连接被重置，全都从这里出去；Message 里有 Win32 编号
            return new AiTransport(false, null, null, AiFailureKind.Unreachable,
                LocMessage.Of("common.rawValue", ex.Message));
        }
        catch (Exception ex)
        {
            return new AiTransport(false, null, null, AiFailureKind.Unknown,
                LocMessage.Of("common.rawValue", ex.Message));
        }
    }

    private static async Task<(string? Body, bool Truncated)> ReadBoundedAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0) break;
            if (buffer.Length + read > MaxReplyBytes)
                return (null, true);
            buffer.Write(chunk, 0, read);
        }
        return (Encoding.UTF8.GetString(buffer.ToArray()), false);
    }
}
