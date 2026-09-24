#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StarMark.Abstractions.Ai;

/// <summary>
/// 一次对话请求。<b>批次 A 只做非流式</b>：批量分类要的是"一整段可解析的 JSON 答案"，
/// 流式反而要先攒包再拼回去（多一处会拼错的地方，且没有任何一处需要边看边出字）。
/// </summary>
/// <param name="SystemPrompt">角色与输出格式约束。<b>必须明写"只输出 JSON"</b>——否则模型会在前后加解说文字，
/// 而解析侧无论多宽容也认不出一句"好的，下面是结果："。</param>
/// <param name="UserPrompt">这一批的数据本体。</param>
public sealed record AiRequest(
    string SystemPrompt,
    string UserPrompt,
    double Temperature = 0.2,
    int MaxTokens = 1200,
    int TimeoutSeconds = 120)
{
    /// <summary>提示词过长时给出原因而不是直接发出去：本地小模型窗口有限，超窗的表现是"它开始编"，
    /// 比报错更难发现。</summary>
    public const int MaxPromptChars = 24_000;

    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(UserPrompt)) return "没有要发给模型的内容";
        if (SystemPrompt is null) return "没有格式约束，模型会自由发挥";
        if (SystemPrompt.Length + UserPrompt.Length > MaxPromptChars)
            return $"这一批太长（{SystemPrompt.Length + UserPrompt.Length} 字 > {MaxPromptChars}），请缩小批量";
        if (TimeoutSeconds <= 0) return "超时必须为正数";
        if (Temperature is < 0 or > 2) return "温度只接受 0–2";
        return null;
    }
}

/// <summary>
/// 两家的 wire 形式在这里互转。<b>整类是"字符串进、字符串出"的纯函数</b>，
/// 这样"发出去的东西对不对""答回来的形状认不认得出"都能在单元测试里断言，
/// 而不需要真起一个 Ollama——需要真机才能验的部分只剩"网络到底通不通"。
/// </summary>
public static class AiWire
{
    /// <summary>用 <see cref="System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>：
    /// 默认编码器会把每个中文字变成 <c>\uXXXX</c>（请求体胖三倍，日志里也读不出是什么），
    /// 而这条路是 HTTPS/本机 HTTP 上的 JSON，不进 HTML 上下文，没有"少转义会被当标签解析"的风险。</summary>
    private static readonly JsonSerializerOptions Body = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Ollama 的 <c>POST /api/chat</c> 请求体。<c>stream:false</c> 是必须的：
    /// 默认是流式，漏掉它拿回来的是一串换行分片而不是一份 JSON。</summary>
    public static string OllamaChatBody(AiSettings settings, AiRequest request)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = request.UserPrompt },
        };
        var body = new JsonObject
        {
            ["model"] = settings.Model?.Trim(),
            ["messages"] = messages,
            ["stream"] = false,
            ["options"] = new JsonObject
            {
                ["temperature"] = request.Temperature,
                ["num_predict"] = request.MaxTokens,
            },
        };
        return body.ToJsonString(Body);
    }

    /// <summary>OpenAI 兼容端点的 <c>POST {base}/chat/completions</c> 请求体。</summary>
    public static string OpenAiChatBody(AiSettings settings, AiRequest request)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = request.UserPrompt },
        };
        var body = new JsonObject
        {
            ["model"] = settings.Model?.Trim(),
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens,
        };
        return body.ToJsonString(Body);
    }

    /// <summary>Ollama 的答案文字。<b>两种形状都认</b>：<c>/api/chat</c> 给 <c>message.content</c>，
    /// 而不少代理照 <c>/api/generate</c> 的样式给 <c>response</c>。</summary>
    public static string? ReadOllamaText(string? json)
    {
        var root = TryParse(json);
        if (root is null) return null;
        return NonBlank(AsText(root["message"]?["content"])) ?? NonBlank(AsText(root["response"]));
    }

    /// <summary>OpenAI 的答案文字。<c>choices[0].message.content</c> 是标准形状；
    /// <c>choices[0].text</c> 是补全式端点（有些代理只有这个），两者都收。</summary>
    public static string? ReadOpenAiText(string? json)
    {
        var root = TryParse(json);
        var first = root?["choices"]?.AsArray().FirstOrDefault() as JsonObject;
        if (first is null) return null;
        return NonBlank(AsText(first["message"]?["content"])) ?? NonBlank(AsText(first["text"]));
    }

    /// <summary>服务在错误响应里给的原因。两家的字段不一样：<c>{"error":"..."}</c>（Ollama）与
    /// <c>{"error":{"message":"..."}}</c>（OpenAI）；<b>读出来才能把这句原话贴给用户</b>，
    /// 否则"400"这种码值对用户毫无意义。</summary>
    public static string? ReadErrorText(string? json)
    {
        var root = TryParse(json);
        if (root is null) return string.IsNullOrWhiteSpace(json) ? null : json.Trim();
        var error = root["error"];
        return AsText(error) ?? AsText(error?["message"]) ?? AsText(root["message"]);
    }

    /// <summary>Ollama <c>/api/tags</c> 的模型名清单（<c>{"models":[{"name":...}]}</c>）。</summary>
    public static IReadOnlyList<string> ReadOllamaModelNames(string? json)
        => Names(TryParse(json)?["models"], "name");

    /// <summary>OpenAI <c>/v1/models</c> 的模型名清单（<c>{"data":[{"id":...}]}</c>）。</summary>
    public static IReadOnlyList<string> ReadOpenAiModelNames(string? json)
        => Names(TryParse(json)?["data"], "id");

    private static IReadOnlyList<string> Names(JsonNode? array, string field)
    {
        if (array is not JsonArray rows) return Array.Empty<string>();
        return rows.OfType<JsonObject>()
            .Select(row => AsText(row[field]))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!.Trim())
            .ToList();
    }

    /// <summary>配置的模型名在服务侧列表里有没有。
    /// <para><b>唯一的宽容是"没写标签就补 <c>:latest</c>"，因为那正是 Ollama 自己做的解析</b>——
    /// 这里多宽一分，界面就会对一个其实调不通的名字打 ✓（填 <c>qwen2.5</c> 而本机只有 <c>qwen2.5:7b</c>
    /// 是真的会失败的，家族名相同不算数）。</para></summary>
    public static bool ListsModel(string? configured, IReadOnlyList<string> available)
    {
        var want = configured?.Trim();
        if (string.IsNullOrEmpty(want) || available.Count == 0) return false;
        if (!want.Contains(':')) want += ":latest";
        return available.Any(name => string.Equals(name.Trim(), want, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>列表进提示语时最多列几个：本机装了三十个模型是常事，全列会把那一行撑成一段。</summary>
    public static string Preview(IReadOnlyList<string> names, int max = 6)
    {
        if (names.Count == 0) return "（本机一个模型也没有）";
        var shown = names.Take(max).ToList();
        return string.Join("、", shown) + (names.Count > max ? $"…（共 {names.Count} 个）" : string.Empty);
    }

    private static JsonObject? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;    // 半截 JSON 是真会发生的：代理返回 HTML 错误页时
        }
    }

    /// <summary>空串与全空白统一读成 null："这个形状里没有正文"与"正文是空的"对调用方是同一件事，
    /// 分成两种返回值只会让每个调用点都各判一次 <c>Length > 0</c>。</summary>
    private static string? NonBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static string? AsText(JsonNode? node) => node is null ? null : node.GetValueKind() switch
    {
        JsonValueKind.String => node.GetValue<string>(),
        JsonValueKind.Number => node.ToString(),
        _ => null,      // 对象/数组/bool/null 都不是"一句话原因"
    };
}
