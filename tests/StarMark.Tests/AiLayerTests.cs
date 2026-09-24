#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Ai;
using StarMark.Integrations.Ai;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// AI 基础层里<b>不需要网络就能成立</b>的那一半：配置闸门、两家的 wire 形式、失败分型。
/// <para>批次 A 刻意把"发什么/读什么"全部做成字符串进字符串出的纯函数——
/// 真机才能验的部分因此只剩下"这个端口到底通不通"，其余都能在这里断言。</para>
/// </summary>
public sealed class AiLayerTests
{
    // ────────── 配置闸门：唯一一处说"能不能用"的地方 ──────────

    [Fact]
    public void OffByDefaultAndThatIsThePoint()
    {
        var fresh = new AiSettings();
        Assert.False(fresh.Enabled);
        Assert.Equal("AI 功能没有开启", fresh.Problem());
    }

    [Fact]
    public void OllamaNeedsNoKey_TheExemptionTheExtensionGotWrongTwice()
    {
        var settings = new AiSettings(Enabled: true, Provider: AiProviderKind.Ollama, Model: "qwen2.5:7b");
        Assert.Null(settings.ApiKey);
        Assert.True(settings.IsUsable);
    }

    [Fact]
    public void ModelNameIsRequiredRegardlessOfChannel()
    {
        Assert.Equal("没有填模型名", new AiSettings(Enabled: true, Model: "   ").Problem());
        Assert.Equal("没有填模型名",
            new AiSettings(Enabled: true, Provider: AiProviderKind.OpenAiCompatible, ApiKey: "sk-x").Problem());
    }

    [Fact]
    public void LocalChannelMayStayUnencryptedButPublicOneMayNot()
    {
        var viaLocal = new AiSettings(
            Enabled: true, Provider: AiProviderKind.OpenAiCompatible, Model: "m",
            ApiKey: "sk-x", BaseUrl: "http://127.0.0.1:1234/v1");
        Assert.True(viaLocal.IsUsable);                       // LM Studio / Ollama 的 openai 模式就是本机明文

        var viaPublic = viaLocal with { BaseUrl = "http://api.example.com/v1" };
        Assert.Contains("https", viaPublic.Problem());        // 带 Key 走公网明文＝把凭据送给路径上任何人
    }

    [Theory]
    [InlineData("", AiProviderKind.OpenAiCompatible)]
    [InlineData("api.openai.com/v1", AiProviderKind.OpenAiCompatible)]
    [InlineData("file:///c:/x", AiProviderKind.OpenAiCompatible)]
    [InlineData("http://:11434", AiProviderKind.Ollama)]
    public void MalformedServiceAddressesAreRejectedEarly(string url, AiProviderKind kind)
        => Assert.NotNull(AiSettings.UrlProblem(url, kind));

    [Fact]
    public void DefaultsArePinnedBecauseTheyAreUserVisibleText()
    {
        var settings = new AiSettings();
        Assert.Equal("http://127.0.0.1:11434", settings.EffectiveOllamaBaseUrl);
        Assert.Equal("https://api.openai.com/v1",
            new AiSettings(Provider: AiProviderKind.OpenAiCompatible).EffectiveBaseUrl);
        // 127.0.0.1 而不是 localhost：后者在 IPv6 优先的机器上先解析到 ::1，而 Ollama 只听 IPv4
        Assert.DoesNotContain("localhost", AiSettings.DefaultOllamaBaseUrl);
    }

    [Fact]
    public void TrailingSlashDoesNotDoubleUpInThePath()
        => Assert.Equal("http://127.0.0.1:11434",
            new AiSettings(OllamaBaseUrl: "http://127.0.0.1:11434///").EffectiveOllamaBaseUrl);

    /// <summary>档位序号是持久化值：改了它，用户设置里那一格就会指向另一个通道。</summary>
    [Theory]
    [InlineData(AiProviderKind.Ollama, 0)]
    [InlineData(AiProviderKind.OpenAiCompatible, 1)]
    public void ProviderNumbersAreNailedDown(AiProviderKind kind, int stored)
        => Assert.Equal(stored, (int)kind);

    [Fact]
    public void WhereWeReachedIsSpelledOutForEachChannel()
    {
        Assert.Equal("本机 Ollama（http://127.0.0.1:11434，模型 a）",
            new AiSettings(Provider: AiProviderKind.Ollama, Model: "a").DescribeWhere());
        Assert.Equal("OpenAI 兼容端点（https://api.openai.com/v1，模型 b）",
            new AiSettings(Provider: AiProviderKind.OpenAiCompatible, Model: "b").DescribeWhere());
    }

    // ────────── 请求体 ──────────

    [Fact]
    public void OllamaBodyMustSayNonStreaming()
    {
        var body = AiWire.OllamaChatBody(
            new AiSettings(Model: "qwen2.5"),
            new AiRequest("只要 JSON", "标题：浏览器扩展"));

        Assert.Contains("\"stream\":false", body);            // 漏了它拿回来的是换行分片，不是一份 JSON
        Assert.Contains("\"model\":\"qwen2.5\"", body);
        Assert.Contains("\"role\":\"system\"", body);
        Assert.Contains("只要 JSON", body);                    // 中文不转义成 \uXXXX：可读性与体积都要
        Assert.Contains("\"num_predict\":1200", body);
    }

    [Fact]
    public void OpenAiBodyUsesItsOwnFieldNames()
    {
        var body = AiWire.OpenAiChatBody(new AiSettings(Model: "gpt-4o-mini"), new AiRequest("s", "u", MaxTokens: 300));

        Assert.Contains("\"max_tokens\":300", body);
        Assert.Contains("\"model\":\"gpt-4o-mini\"", body);
        Assert.DoesNotContain("num_predict", body);            // 两家的字段不能串台
    }

    [Fact]
    public void QuotesAndBracesInThePromptSurviveAsData()
    {
        var body = AiWire.OllamaChatBody(new AiSettings(Model: "m"), new AiRequest("s", "他说\"干净\"并留下 { }"));

        Assert.Contains("\\\"干净\\\"", body);                 // 引号必须转义，否则一整批都发不出去
        Assert.DoesNotContain("他说\"干净\"", body);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("x", false)]
    public void AnEmptyBatchIsRefusedBeforeItReachesTheWire(string user, bool rejected)
    {
        var problem = new AiRequest("s", user).Problem();
        Assert.Equal(rejected, problem is not null);
    }

    [Fact]
    public void OverlongPromptsAreNamedBySize()
    {
        var big = new AiRequest("s", new string('字', AiRequest.MaxPromptChars));
        Assert.NotNull(big.Problem());
        Assert.Contains("缩小批量", big.Problem());
    }

    // ────────── 读答复：形状漂移是常态，两种都得认 ──────────

    [Theory]
    [InlineData("""{"message":{"role":"assistant","content":"结果"}}""", "结果")]
    [InlineData("""{"response":"旧的 generate 形式"}""", "旧的 generate 形式")]
    public void OllamaRepliesOfBothShapes(string json, string expected)
        => Assert.Equal(expected, AiWire.ReadOllamaText(json));

    [Theory]
    [InlineData("""{"choices":[{"message":{"content":"甲"}}]}""", "甲")]
    [InlineData("""{"choices":[{"text":"乙"}]}""", "乙")]
    public void OpenAiRepliesOfBothShapes(string json, string expected)
        => Assert.Equal(expected, AiWire.ReadOpenAiText(json));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"message":{"content":""}}""")]
    public void UnreadableRepliesReadAsNothingRatherThanCrashing(string? json)
    {
        Assert.Null(AiWire.ReadOllamaText(json));
        Assert.Null(AiWire.ReadOpenAiText(json));
    }

    [Theory]
    [InlineData("""{"error":"model not found"}""", "model not found")]
    [InlineData("""{"error":{"message":"Incorrect API key"}}""", "Incorrect API key")]
    public void BothErrorShapesAreUnwrapped(string json, string expected)
        => Assert.Equal(expected, AiWire.ReadErrorText(json));

    [Fact]
    public void HtmlErrorPagesComeBackAsThemselves()
        => Assert.Contains("<html>", AiWire.ReadErrorText("<html><body>502 Bad Gateway</body></html>")!);

    [Fact]
    public void ModelListsAreReadFromEachShapesOwnField()
    {
        Assert.Equal(new[] { "llama3.2:latest", "qwen2.5:7b" },
            AiWire.ReadOllamaModelNames("""{"models":[{"name":"llama3.2:latest"},{"name":"qwen2.5:7b"}]}"""));
        Assert.Equal(new[] { "gpt-4o-mini" },
            AiWire.ReadOpenAiModelNames("""{"data":[{"id":"gpt-4o-mini"},{"id":null},{"object":"model"}]}"""));
    }

    [Theory]
    [InlineData("qwen2.5", "qwen2.5:7b", false)]      // 家族名相同不算：Ollama 会去找 qwen2.5:latest 而失败
    [InlineData("llama3", "llama3:latest", true)]     // 没写标签＝就是那个 latest
    [InlineData("LLAMA3", "llama3:latest", true)]
    [InlineData("mistral", "llama3:latest", false)]
    public void TagLessModelNamesMatchTheirFamily(string configured, string available, bool found)
        => Assert.Equal(found, AiWire.ListsModel(configured, new[] { available }));

    [Fact]
    public void EmptyModelListIsSaidOutLoud()
    {
        Assert.Equal("（本机一个模型也没有）", AiWire.Preview(Array.Empty<string>()));
        Assert.Equal("a、b", AiWire.Preview(new[] { "a", "b" }));
        Assert.Contains("共 7 个", AiWire.Preview(new[] { "a", "b", "c", "d", "e", "f", "g" }));
    }

    // ────────── 失败分型：决定用户下一步做什么 ──────────

    [Theory]
    [InlineData(null, "unable to connect", AiFailureKind.Unreachable)]
    [InlineData(401, "bad key", AiFailureKind.Unauthorized)]
    [InlineData(403, "forbidden", AiFailureKind.Unauthorized)]
    [InlineData(429, "slow down", AiFailureKind.RateLimited)]
    [InlineData(408, null, AiFailureKind.TimedOut)]
    [InlineData(504, null, AiFailureKind.TimedOut)]
    [InlineData(500, "boom", AiFailureKind.ServiceError)]
    [InlineData(400, "bad request", AiFailureKind.BadResponse)]
    [InlineData(404, "no route", AiFailureKind.BadResponse)]
    public void StatusCodesMapToKinds(int? status, string? body, AiFailureKind expected)
        => Assert.Equal(expected, AiFailures.FromStatus(status, body));

    [Theory]
    [InlineData("model \"qwen2.5:7b\" not found")]
    [InlineData("no such model")]
    [InlineData("The model does not exist.")]
    public void A404ThatNamesTheModelIsADifferentThingThanA404OnARoute(string body)
        => Assert.Equal(AiFailureKind.ModelNotFound, AiFailures.FromStatus(404, body));

    [Fact]
    public void EveryReasonTellsWhereToLook()
    {
        var where = "本机 Ollama（http://127.0.0.1:11434，模型 q）";
        foreach (AiFailureKind kind in Enum.GetValues<AiFailureKind>())
        {
            var reason = AiFailures.Reason(kind, "x", where);
            Assert.False(string.IsNullOrWhiteSpace(reason));
            // "连不上/模型名不对"这两类必须指名地址，否则用户不知道改哪一格；
            // "未配置"要说的是"去设置那一栏"。超时不同：该改的是模型或时限，地址写对了也一样会超时。
            if (kind is AiFailureKind.Unreachable or AiFailureKind.ModelNotFound)
                Assert.Contains("Ollama", reason);
            if (kind == AiFailureKind.NotConfigured)
                Assert.Contains("设置", reason);
        }
    }

    [Fact]
    public void LongServiceReasonsDoNotBreakTheRow()
    {
        var reason = AiFailures.Reason(AiFailureKind.ServiceError, new string('长', 400), "w");
        Assert.True(reason.Length < 200, reason.Length.ToString());
        Assert.DoesNotContain("\n", reason);
    }

    [Fact]
    public void InventoryReportCarriesChannelListAndVerdict()
    {
        var ok = new AiModelInventory(true, new[] { "llama3", "qwen2.5:7b" });
        Assert.Contains("✓", ok.Explain("本机 Ollama", "qwen2.5:7b"));

        var miss = ok.Explain("本机 Ollama", "mistral");
        Assert.Contains("没有", miss);
        Assert.Contains("llama3", miss);                      // 报"现有几个"是这一步唯一有用的信息
    }

    // ────────── 网关：闸门在前，网络在后 ──────────

    [Fact]
    public async Task UnconfiguredSettingsNeverTouchTheNetwork()
    {
        using var gateway = new AiGateway();
        var reply = await gateway.CompleteAsync(new AiSettings(Enabled: false, Model: null),
            new AiRequest("s", "u"), CancellationToken.None);

        Assert.False(reply.Ok);
        Assert.Equal(AiFailureKind.NotConfigured, reply.Failure);
        Assert.Contains("没有开启", reply.Detail);
    }

    [Fact]
    public async Task ProbingIsAllowedWhileUnconfigured_ItIsHowOneGetsConfigured()
    {
        using var gateway = new AiGateway();
        // 端口上没人监听：要拿到"连不上 + 连的是哪儿"，而不是被闸门挡下来说"你还没配置"
        var probe = await gateway.ProbeAsync(
            new AiSettings(Provider: AiProviderKind.Ollama, Model: "m", OllamaBaseUrl: "http://127.0.0.1:1"),
            CancellationToken.None);

        Assert.False(probe.Ok);
        Assert.Contains("127.0.0.1:1", probe.Report);
        Assert.Contains("没在服务端口上监听", probe.Report);
    }

    [Fact]
    public async Task ACallTheServiceRefusesSurfacesTheServiceOwnWords()
    {
        using var gateway = new AiGateway();
        var reply = await gateway.CompleteAsync(
            new AiSettings(Enabled: true, Provider: AiProviderKind.OpenAiCompatible,
                Model: "nope", ApiKey: "sk-x", BaseUrl: "http://127.0.0.1:1/v1"),
            new AiRequest("s", "u", TimeoutSeconds: 5), CancellationToken.None);
        Assert.False(reply.Ok);
        Assert.Equal(AiFailureKind.Unreachable, reply.Failure);   // 连不上就说是连不上，别报成"答的形式不对"
    }

    [Fact]
    public void UnknownProviderNumberHasANameInItsComplaint()
    {
        using var gateway = new AiGateway();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => gateway.Of((AiProviderKind)99));
        Assert.Equal("kind", ex.ParamName);
    }
}
