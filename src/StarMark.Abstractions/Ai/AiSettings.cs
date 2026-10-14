#nullable enable
using System;
using StarMark.Abstractions.I18n;

namespace StarMark.Abstractions.Ai;

/// <summary>
/// 可选的模型通道。<b>序号是持久化值</b>（设置档里存的就是这个整数），所以逐字钉死、只能加不能改。
/// <para>
/// 这里刻意只有两个通道：批次 A 只接"本机 Ollama"与"任意 OpenAI 兼容 HTTP 端点"，
/// 后者已经覆盖 OpenAI / 各类代理 / LM Studio。<b>Anthropic 之类按文档需要时再加</b>——
/// 为一个本机用户几乎不会填的端点先写一份 HTTP 客户端，等于多一处没人验证过的地方会错。
/// </para>
/// </summary>
public enum AiProviderKind
{
    Ollama = 0,
    OpenAiCompatible = 1,
}

/// <summary>
/// AI 通道的配置。<b>默认关闭</b>：这是一个会把用户库里的标题/摘要发出去的开关，
/// 未明确要求前不得处于工作状态（§15.5 边界）。
/// <para>
/// <see cref="Problem"/> 是"能不能用"的<b>唯一入口</b>。扩展项目出过两次故障，根因都是
/// 各处零散地判断 <c>apiKey 非空</c>，而其中一处漏了"Ollama 不需要 Key"——于是本机明明能用，
/// 界面却说没配置。这类判断只能有一个地方说得出话。
/// </para>
/// </summary>
public sealed record AiSettings(
    bool Enabled = false,
    AiProviderKind Provider = AiProviderKind.Ollama,
    string? Model = null,
    string? ApiKey = null,
    string? OllamaBaseUrl = null,
    string? BaseUrl = null,
    string? ClassifyModel = null)
{
    /// <summary>Ollama 默认监听地址。<b>写 127.0.0.1 而不是 localhost</b>：后者在某些 hosts/IPv6 优先的配置下
    /// 先解析到 <c>::1</c>，而 Ollama 默认只监听 IPv4，于是"明明装了却说连不上"。</summary>
    public const string DefaultOllamaBaseUrl = "http://127.0.0.1:11434";

    /// <summary>分类专用模型（§19 O6）：打标是封闭集分配任务，小模型足够且便宜十倍以上的档位——
    /// 高端模型留给将来生成类任务，两者共用一格就会互相绑架（为了分类把主模型换小，
    /// 将来「AI 润色」跟着变蠢）。空＝沿用 <see cref="Model"/>。</summary>
    public string? EffectiveClassifyModel =>
        string.IsNullOrWhiteSpace(ClassifyModel) ? Model : ClassifyModel.Trim();

    /// <summary>把"这一轮是分类用"折叠进配置本身——<b>调用方不需要知道降级规则</b>，
    /// 也不许在 Gateway/Runner 里各写一次"ClassifyModel 空就用 Model"（两处判一处漏是分岔的开始）。</summary>
    public AiSettings ForClassify() =>
        string.IsNullOrWhiteSpace(ClassifyModel) ? this : this with { Model = ClassifyModel.Trim() };

    /// <summary>OpenAI 兼容端点的默认地址（只有真的填了 Key 才会用到）。</summary>
    public const string DefaultOpenAiBaseUrl = "https://api.openai.com/v1";

    public string EffectiveOllamaBaseUrl =>
        string.IsNullOrWhiteSpace(OllamaBaseUrl) ? DefaultOllamaBaseUrl : OllamaBaseUrl.Trim().TrimEnd('/');

    public string EffectiveBaseUrl => Provider switch
    {
        AiProviderKind.Ollama => EffectiveOllamaBaseUrl,
        _ => string.IsNullOrWhiteSpace(BaseUrl) ? DefaultOpenAiBaseUrl : BaseUrl.Trim().TrimEnd('/'),
    };

    /// <summary>配置不成立时给出<b>能照着改</b>的原因；null＝可用。
    /// <para>交的是<b>没取词的</b>「键＋实参」（批次 WI-67 片7）：这一句既上设置页那一格，也进
    /// <see cref="AiFailures.Reason"/> 拼成的失败原因，还进日志。在产地就把词取走，屏幕上就会
    /// 留下「英文骨架＋中文原因」那半句（P-193 口径②），而日志那一刻要的又是另一门语言。</para></summary>
    public LocMessage? Problem()
    {
        if (!Enabled) return LocMessage.Of("ai.problem.notEnabled");
        if (string.IsNullOrWhiteSpace(Model)) return LocMessage.Of("ai.problem.noModel");

        var url = Provider switch
        {
            AiProviderKind.Ollama => EffectiveOllamaBaseUrl,
            _ => EffectiveBaseUrl,
        };
        if (UrlProblem(url, Provider) is { } bad) return bad;

        // Ollama 免 Key 豁免：本机进程之间没有"凭据"这回事，要求填 Key 只会让人去编一个
        if (Provider == AiProviderKind.Ollama) return null;

        if (string.IsNullOrWhiteSpace(ApiKey)) return LocMessage.Of("ai.problem.noApiKey");
        // Key 会随请求头长期外发，明文走公网等于把凭据送给路径上任何人
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !IsLocalHost(url))
            return LocMessage.Of("ai.problem.insecureUrl");
        return null;
    }

    public bool IsUsable => Problem() is null;

    /// <summary>只支持 http/https 的绝对地址——其余形式（<c>file:</c>、裸 <c>:11434</c>、带空格）都会
    /// 在 <see cref="System.Net.Http.HttpClient"/> 里变成一个看不懂的异常，不如在这里挡住。</summary>
    public static LocMessage? UrlProblem(string? url, AiProviderKind provider)
    {
        if (string.IsNullOrWhiteSpace(url)) return LocMessage.Of("ai.url.none");
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
            return LocMessage.Of("ai.url.notAbsolute", url);
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return LocMessage.Of("ai.url.badScheme", parsed.Scheme);
        if (string.IsNullOrEmpty(parsed.Host)) return LocMessage.Of("ai.url.noHost");
        return null;
    }

    /// <summary>本机地址（明文的唯一豁免）。含 <c>localhost</c> 与常见拼法，避免"本机代理"被误判成公网。
    /// <b>不含 <c>0.0.0.0</c></b>：那是监听地址，作为目标地址连不出去，放行等于给明文开口子。</summary>
    public static bool IsLocalHost(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return false;
        var host = parsed.Host.ToLowerInvariant();
        return host is "localhost" or "127.0.0.1" or "::1" or "[::1]"
            || host.StartsWith("127.", StringComparison.Ordinal);
    }

    /// <summary>给日志与界面用的一行说明：<b>说清"按哪个通道、发到哪儿、用哪个模型"</b>。
    /// 只写"AI 调用失败"等于没写——用户需要知道该去改哪一格。
    /// <para>交「键＋读数」而⛔ 交成品字：这一句既上设置页那一格、也嵌进
    /// <see cref="AiFailures.Reason"/> 那句失败原因，另外还单独进日志。
    /// 日志那两处自己在<b>写日志的那一刻</b> <c>Render()</c>（＝<c>RestoreResult.Message</c> 同一口径），
    /// 于是屏幕上按当前语言念、日志里也按当时那门语言落字。</para></summary>
    public LocMessage DescribeWhere() => Provider switch
    {
        AiProviderKind.Ollama => LocMessage.Of("ai.where.ollama", EffectiveOllamaBaseUrl, Model?.Trim()),
        _ => LocMessage.Of("ai.where.openAi", EffectiveBaseUrl, Model?.Trim()),
    };

    /// <summary>通道下拉里那一颗名字的<b>键</b>（批次 WI-67 片7）。交键而⛔ 交成品字：
    /// 下拉是「数据项＋模板」那一族，格子与展开列表都在<b>被贴上去那一刻</b>按当前语言取词，
    /// 所以这里只能说"用哪一颗键"，把词交出去就会让那一格永远念旧语言（坑表 #272／#275）。
    /// <para>读不到键＝枚举加了新值而这里没跟上 ⇒ 交 <b>null</b>（这一个 null 只有这一个意思：
    /// "没有登记过的名字可念"）。界面退成念自名，⛔ 在这里编一句没进表的话。</para></summary>
    public static string? NameKeyOf(AiProviderKind provider) => provider switch
    {
        AiProviderKind.Ollama => "ai.provider.ollama",
        AiProviderKind.OpenAiCompatible => "ai.provider.openAi",
        _ => null,
    };
}
