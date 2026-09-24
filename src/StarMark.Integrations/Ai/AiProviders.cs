#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Ai;

namespace StarMark.Integrations.Ai;

/// <summary>
/// 一个模型通道。<b>批次 A 只有两个实现</b>，接口按"D6 说的往后还要接 API"的形状写：
/// 选择哪个实现、能不能用、失败分型都在 <see cref="AiGateway"/>，这里只负责"按这一家的格式发一次"。
/// </summary>
public interface IAiProvider
{
    AiProviderKind Kind { get; }

    /// <summary>发一次非流式对话。返回 <see cref="AiReply"/> 而不是抛：<b>"AI 没答上来"是这个功能的常态，
    /// 不是异常</b>——用异常传这种信息会让每个调用点都被迫写 try。</summary>
    Task<AiReply> CompleteAsync(AiSettings settings, AiRequest request, CancellationToken ct);

    /// <summary>问服务"你那儿有哪些模型"。「测试连接」的真正内容在这里：
    /// 只报"通了"帮不上"模型名写错了"这一大类故障。</summary>
    Task<AiModelInventory> ListModelsAsync(AiSettings settings, CancellationToken ct);
}

/// <summary>Ollama（本机）。免 Key、默认 <c>127.0.0.1:11434</c>；桌面应用发请求没有 Origin 头，
/// 所以扩展侧那个 <c>OLLAMA_ORIGINS</c> 白名单问题在这里根本不存在。</summary>
public sealed class OllamaProvider : IAiProvider, IDisposable
{
    /// <summary>清点模型要快：这一下是用户点「测试连接」时等的，模型列表本身很小。</summary>
    public const int InventoryTimeoutSeconds = 10;

    private readonly HttpClient _http;

    public OllamaProvider(HttpClient? http = null) => _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

    public AiProviderKind Kind => AiProviderKind.Ollama;

    public async Task<AiReply> CompleteAsync(AiSettings settings, AiRequest request, CancellationToken ct)
    {
        if (request.Problem() is { } bad) return AiReply.Fail(AiFailureKind.NotConfigured, bad);
        var transport = await AiHttp.PostAsync(_http, settings.EffectiveOllamaBaseUrl + "/api/chat",
            AiWire.OllamaChatBody(settings, request), null, request.TimeoutSeconds, ct);
        return Read(transport);
    }

    public async Task<AiModelInventory> ListModelsAsync(AiSettings settings, CancellationToken ct)
    {
        var transport = await AiHttp.GetAsync(_http, settings.EffectiveOllamaBaseUrl + "/api/tags",
            null, InventoryTimeoutSeconds, ct);
        if (!transport.Connected)
            return AiModelInventory.Fail(transport.Failure, transport.Problem);
        if (!transport.Succeeded)
            return AiModelInventory.Fail(AiFailures.FromStatus(transport.Status, transport.Body),
                AiWire.ReadErrorText(transport.Body) ?? transport.Body);

        var names = AiWire.ReadOllamaModelNames(transport.Body);
        // 答了但读不出清单：这既不是"没模型"也不是"连不上"，必须单列，否则会把版本不匹配说成没装
        if (names.Count == 0 && transport.Body?.Contains("models") != true)
            return AiModelInventory.Fail(AiFailureKind.BadResponse, "答回来的形式不是模型清单");
        return new AiModelInventory(true, names);
    }

    private static AiReply Read(AiTransport transport)
    {
        if (!transport.Connected)
            return AiReply.Fail(transport.Failure, transport.Problem);
        if (!transport.Succeeded)
            return AiReply.Fail(AiFailures.FromStatus(transport.Status, transport.Body),
                AiWire.ReadErrorText(transport.Body) ?? transport.Body);
        return AiWire.ReadOllamaText(transport.Body) is { Length: > 0 } text
            ? AiReply.Success(text)
            : AiReply.Fail(AiFailureKind.BadResponse, "服务答了，但答里没有一个字的正文");
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>任意 OpenAI 兼容端点（OpenAI 本体、各类代理、LM Studio 的 openai 服务模式都算）。
/// <b>只认 <c>/chat/completions</c> 与 <c>/models</c> 这两个路径</b>，基址由用户填到 <c>/v1</c> 那一层。</summary>
public sealed class OpenAiCompatibleProvider : IAiProvider, IDisposable
{
    public const int InventoryTimeoutSeconds = 15;

    private readonly HttpClient _http;

    public OpenAiCompatibleProvider(HttpClient? http = null) => _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

    public AiProviderKind Kind => AiProviderKind.OpenAiCompatible;

    public async Task<AiReply> CompleteAsync(AiSettings settings, AiRequest request, CancellationToken ct)
    {
        if (request.Problem() is { } bad) return AiReply.Fail(AiFailureKind.NotConfigured, bad);
        var transport = await AiHttp.PostAsync(_http, settings.EffectiveBaseUrl + "/chat/completions",
            AiWire.OpenAiChatBody(settings, request), Auth(settings), request.TimeoutSeconds, ct);

        if (!transport.Connected) return AiReply.Fail(transport.Failure, transport.Problem);
        if (!transport.Succeeded)
            return AiReply.Fail(AiFailures.FromStatus(transport.Status, transport.Body),
                AiWire.ReadErrorText(transport.Body) ?? transport.Body);
        return AiWire.ReadOpenAiText(transport.Body) is { Length: > 0 } text
            ? AiReply.Success(text)
            : AiReply.Fail(AiFailureKind.BadResponse, "服务答了，但 choices 里没有正文（这个端点可能不支持 chat 形式）");
    }

    public async Task<AiModelInventory> ListModelsAsync(AiSettings settings, CancellationToken ct)
    {
        var transport = await AiHttp.GetAsync(_http, settings.EffectiveBaseUrl + "/models",
            Auth(settings), InventoryTimeoutSeconds, ct);
        if (!transport.Connected)
            return AiModelInventory.Fail(transport.Failure, transport.Problem);
        if (!transport.Succeeded)
            return AiModelInventory.Fail(AiFailures.FromStatus(transport.Status, transport.Body),
                AiWire.ReadErrorText(transport.Body) ?? transport.Body);

        var names = AiWire.ReadOpenAiModelNames(transport.Body);
        if (names.Count == 0)
            return AiModelInventory.Fail(AiFailureKind.BadResponse, "端点答了，但模型清单是空的或形式不对");
        return new AiModelInventory(true, names);
    }

    /// <summary>Key 只进请求头，<b>不进日志、不进 URL</b>（URL 会被各级代理记进访问日志）。</summary>
    private static IReadOnlyDictionary<string, string>? Auth(AiSettings settings)
        => string.IsNullOrWhiteSpace(settings.ApiKey)
            ? null
            : new Dictionary<string, string> { ["Authorization"] = "Bearer " + settings.ApiKey.Trim() };

    public void Dispose() => _http.Dispose();
}
