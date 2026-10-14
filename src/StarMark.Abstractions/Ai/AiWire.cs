#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using StarMark.Abstractions.I18n;

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

    /// <summary>这一发出得出去吗。<b>交 <see cref="LocMessage"/> 而⛔ 交成品字</b>（批次 WI-67 片7）：
    /// 这五句顺着 <c>AiReply.Detail</c> 走到「批量整理」那句"这批为什么没成"上屏，
    /// 在产地取词就是那一族半中半英（坑表 #393／P-196 的"第二时刻"）。</summary>
    public LocMessage? Problem()
    {
        if (string.IsNullOrWhiteSpace(UserPrompt)) return LocMessage.Of("ai.request.noUserPrompt");
        if (SystemPrompt is null) return LocMessage.Of("ai.request.noSystemPrompt");
        if (SystemPrompt.Length + UserPrompt.Length > MaxPromptChars)
            return LocMessage.Of("ai.request.tooLong", SystemPrompt.Length + UserPrompt.Length, MaxPromptChars);
        if (TimeoutSeconds <= 0) return LocMessage.Of("ai.request.badTimeout");
        if (Temperature is < 0 or > 2) return LocMessage.Of("ai.request.badTemperature");
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

    /// <summary>Ollama 的 token 账：<c>/api/chat</c> 回 <c>prompt_eval_count / eval_count</c>。
    /// <b>读不到一律返回 null 而不是 0</b>——"这个模型没回计量"和"它说用了 0 个 token"
    /// 在账单上是两回事；前者该转估算，后者可以直接入账。</summary>
    public static AiUsage? ReadOllamaUsage(string? json)
    {
        var root = TryParse(json);
        var input = AsInt(root?["prompt_eval_count"]);
        var output = AsInt(root?["eval_count"]);
        return input is null || output is null ? null : new AiUsage(input.Value, output.Value, Estimated: false);
    }

    /// <summary>OpenAI 兼容端的 token 账：<c>usage.prompt_tokens / usage.completion_tokens</c>。
    /// 非流式响应默认带 usage（<c>stream_options</c> 只有流式才需要，本通道不发流式）。
    /// 同样：<b>缺任何一侧都按读不到处理</b>。</summary>
    public static AiUsage? ReadOpenAiUsage(string? json)
    {
        var usage = TryParse(json)?["usage"];
        var input = AsInt(usage?["prompt_tokens"]);
        var output = AsInt(usage?["completion_tokens"]);
        return input is null || output is null ? null : new AiUsage(input.Value, output.Value, Estimated: false);
    }

    /// <summary>服务没回 usage 时的字符折算（§20.5）：<b>与中文检索侧同源，用
    /// <see cref="Text.CjkTokenizer.IsCjk"/> 而不是再抄一份 Unicode 区间</b>——
    /// 中文 1 字≈1 token、其余 4 字符≈1 token 都是粗颗粒近似，口径写进"估算"标签而不是假装精确。</summary>
    public static AiUsage Approximate(string? prompt, string? reply)
        => new(EstimateTokens(prompt), EstimateTokens(reply), Estimated: true);

    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var cjk = 0;
        var other = 0;
        foreach (var ch in text)
        {
            if (Text.CjkTokenizer.IsCjk(ch)) cjk++;
            else other++;
        }
        return cjk + other / 4 + (other % 4 == 0 ? 0 : 1);   // 非 CJK 余数向上取整：宁可估多不估少
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

    /// <summary>列表进提示语时最多列几个：本机装了三十个模型是常事，全列会把那一行撑成一段。
    /// <para>交的是「一串数据＋两颗胶水键」：连接号（顿号／逗号）与"共 N 个"那半句都随语言变，
    /// 所以在这一行就 <c>string.Join(Locale.T(连接键), …)</c> 会把它们钉死在算出那一刻——
    /// 换语言重贴只重算了外层那句，屏幕上剩下「a、b, c」这种（坑表 #362／#393 那一族的第三个表面）。
    /// <see cref="LocJoin"/> 因此留到被贴上去那一刻才连（批次 WI-67 片7）。</para></summary>
    public static LocMessage Preview(IReadOnlyList<string> names, int max = 6)
    {
        if (names.Count == 0) return LocMessage.Of("ai.preview.none");
        var shown = names.Take(max).Select(name => (object?)name).ToList();
        // 没超出上限＝没有后半句：那一槽交 null，格式化时它自己变空（⛔ 在这里拼字符串）
        return LocMessage.Of("ai.preview.list", new LocJoin(shown, "ai.preview.join"),
            names.Count > max ? LocMessage.Of("ai.preview.more", names.Count) : null);
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

    /// <summary>token 计数读取：只认"数值类型且非负整数"。<b>字符串形态的数字不收</b>——
    /// usage 字段没有哪家把它写成字符串，收了反而会把"字段其实是别的东西"误读成用量。</summary>
    private static int? AsInt(JsonNode? node)
        => node is JsonValue value && value.TryGetValue(out long v) && v >= 0 && v <= int.MaxValue
            ? (int)v
            : null;
}
