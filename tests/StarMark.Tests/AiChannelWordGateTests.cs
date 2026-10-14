#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-67 <b>片7</b>（P-193 普查里剩下的那半扇＝<b>AI 上屏句子链</b>）的闸门：
/// 那句"为什么不能用/为什么失败/连到了哪儿"从前在 <c>Abstractions</c>／<c>Integrations</c>／<c>Core</c>
/// 三层里<b>当场就念成了中文</b>，再由界面层原样贴到屏幕上——换语言那一刻，屏幕上留的是上一次的语言。
/// <para>
/// 本片的口径与 ✅WI-61d／✅WI-62b／✅WI-65 片1～4 同一族：<b>两层同批交表</b>——句厂交 <see cref="LocMessage"/>（键＋实参），
/// 界面层⛔ 再套一道数据门。⚠ 与它们不同的是<b>这一扇没有第四位 <c>Locale.Changed</c> 主人</b>：
/// 设置页那一格收的是 <see cref="LocMessage"/>，而 <c>LocText.SetMessage</c> 早已挂在全局那一处重贴上（✅WI-61b 的门），
/// 槽里嵌的句子内层再套 <see cref="LocMessage"/>/<see cref="LocJoin"/> ⇒ 取词时刻＝贴上去那一刻（⑦⑧ 两格读的就是这个时刻）。
/// </para>
/// <para>
/// ⛔ 只进日志的那几句进表（<c>AiGateway</c> 两发 <c>StarLog</c> 里的 <c>DescribeWhere().Render()</c>、
/// <c>WidgetStartupPolicy.DescribeSkipped</c>、<c>Of(kind)</c> 那颗 <see cref="ArgumentOutOfRangeException"/>）＝⑥ 那一格钉住；
/// 发给模型的提示词⛔ 交表（<c>ClassifyRules</c>／<c>AiClassifyService.NameOf</c>）由 ✅WI-41 那颗
/// <see cref="AiWordGateTests"/> ⑦ 那一格守着，本档不重复实现。
/// </para>
/// <para>颗数一律现读（<c>bin/WI67/probe_67k.txt</c>，2026-10-14 本批现场；名单主人＝<c>bin/WI67/rows_67j.py</c>），⛔ 凭记忆（#159/#160）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class AiChannelWordGateTests : IDisposable
{
    private const string Settings = "src/StarMark.Abstractions/Ai/AiSettings.cs";
    private const string Failures = "src/StarMark.Abstractions/Ai/AiFailures.cs";
    private const string Results = "src/StarMark.Abstractions/Ai/AiResults.cs";
    private const string Wire = "src/StarMark.Abstractions/Ai/AiWire.cs";
    private const string Classify = "src/StarMark.Abstractions/Ai/AiClassify.cs";
    private const string Gateway = "src/StarMark.Integrations/Ai/AiGateway.cs";
    private const string Http = "src/StarMark.Integrations/Ai/AiHttp.cs";
    private const string Providers = "src/StarMark.Integrations/Ai/AiProviders.cs";
    private const string Runner = "src/StarMark.Core/Ai/ClassifyRunner.cs";
    private const string Page = "src/StarMark.UI/Views/SettingsPage.Ai.cs";
    private const string Service = "src/StarMark.UI/Services/AiClassifyService.cs";
    private const string Policy = "src/StarMark.Core/Widgets/WidgetStartupPolicy.cs";
    private const string PageXaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    /// <summary>本扇交进表里的 50 颗键，逐颗点名（计数型判据抓不到"换名"＝#164；名单由 <c>rows_67j.py</c> 现生成）。</summary>
    private static readonly string[] Keys =
    {
        "ai.problem.notEnabled",
        "ai.problem.noModel",
        "ai.problem.noApiKey",
        "ai.problem.insecureUrl",
        "ai.url.none",
        "ai.url.notAbsolute",
        "ai.url.badScheme",
        "ai.url.noHost",
        "ai.where.ollama",
        "ai.where.openAi",
        "ai.where.channel",
        "ai.provider.ollama",
        "ai.provider.openAi",
        "ai.failure.notConfigured",
        "ai.failure.notConfiguredWith",
        "ai.failure.unreachable",
        "ai.failure.modelNotFound",
        "ai.failure.unauthorized",
        "ai.failure.rateLimited",
        "ai.failure.timedOut",
        "ai.failure.serviceError",
        "ai.failure.badResponse",
        "ai.failure.unknown",
        "ai.failure.noReason",
        "ai.request.noUserPrompt",
        "ai.request.noSystemPrompt",
        "ai.request.tooLong",
        "ai.request.badTimeout",
        "ai.request.badTemperature",
        "ai.reply.answered",
        "ai.inventory.answeredList",
        "ai.inventory.answeredHasModel",
        "ai.inventory.answeredLacksModel",
        "ai.preview.none",
        "ai.preview.join",
        "ai.preview.list",
        "ai.preview.more",
        "ai.transport.waitedOut",
        "ai.transport.replyTooBig",
        "ai.transport.notModelList",
        "ai.transport.emptyReply",
        "ai.transport.noChoices",
        "ai.transport.emptyInventory",
        "ai.probe.urlMalformed",
        "ai.run.batchFailed",
        "ai.parse.nothingReadable",
        "ai.parse.itemsNotArray",
        "ai.parse.categoriesNotObject",
        "ai.parse.noKnownShape",
        "ai.parse.jsonBroken",
    };

    /// <summary><b>那一行没有词可翻</b>的两颗：连接号与纯骨架。中日同形／三列同形是事实，⛔ 当"没翻"（同 I18nContractTests 那份 reasons）。</summary>
    private static readonly string[] NothingToTranslate =
    {
        "ai.preview.join",      // 「、」／", "：连的是服务侧的模型名，那是数据
        "ai.preview.list",      // 「{0}{1}」：把「那一串」与「也许没有的后半句」并起来
    };

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>句厂三层的十一档：交出去的那一句⛔ 在这里定语言，也⛔ 在这里有两个主人。</summary>
    private static readonly string[] Family =
    {
        Settings, Failures, Results, Wire, Classify, Gateway, Http, Providers, Runner, Page, Service,
    };

    private static string AllFamily() => string.Join("\n", Family.Select(CodeOf));

    /// <summary>整颗键连收尾引号一起数（⛔ 只认前缀：<c>ai.failure.unknown</c> 会把 <c>ai.failure.unknownX</c> 也算成自己点的名）。</summary>
    private static string Of(string key) => (char)34 + key + (char)34;

    private static int Times(string haystack, string needle) =>
        haystack.Length == 0 || needle.Length == 0 ? 0 : haystack.Split(new[] { needle }, StringSplitOptions.None).Length - 1;

    /// <summary>U+4E00～U+9FFF：屏幕上念出这一族里的任何一个字，就说明词是在产地取的（与 I18nContractTests 同一把尺）。</summary>
    private static bool HasHan(string text) => text.Any(c => c >= 0x4E00 && c <= 0x9FFF);

    /// <summary>①名单颗数：这颗数的是<b>我自己抄进本档的那一串</b>，加一颗不登记就红。</summary>
    [Fact]
    public void TheRosterIsExactlyTheFiftySentencesThisSliceHandedIn()
    {
        Assert.Equal(50, Keys.Length);
        Assert.Equal(50, Keys.Distinct(StringComparer.Ordinal).Count());
        // 前缀⛔ 混：settings.ai.* 那 71 颗的主人是界面层（✅WI-41），ai.* 这 50 颗的主人是句厂三层——
        // 两族各一把尺，混成一把就分不清"是谁在产地取的词"。
        Assert.All(Keys, key => Assert.StartsWith("ai.", key, StringComparison.Ordinal));
        Assert.DoesNotContain(Keys, key => key.StartsWith("settings.ai.", StringComparison.Ordinal));
    }

    /// <summary>②双向对账：每颗在表里恰好一行、在十一档里恰好被点名一次、在标记里一次都没有。
    /// <para>0 次＝白进表（那句还是有人手写），2 次＝两个主人（改一处漏一处）。现场数：50/50 都是 1（<c>probe_67k.txt</c> ②）。</para></summary>
    [Fact]
    public void EverySentenceHasOneRowAndExactlyOneVoice()
    {
        var all = AllFamily();
        var xaml = SourceGate.ReadRepoFile(PageXaml);
        foreach (var key in Keys)
        {
            Assert.True(UiStrings.TryGet(key, out _), "表里没有 " + key);
            Assert.Single(UiStrings.All, entry => entry.Key == key);
            Assert.Equal(1, Times(all, Of(key)));
            Assert.Equal(0, Times(xaml, key));       // 这一族的句子⛔ 由标记挂：全部是代码当场算出来的身份
        }
    }

    /// <summary>③语言读数：50 颗三语各答话、槽数三列一致、实参⛔ 被吞；除两颗纯胶水外三语⛔ 同形。
    /// <para>形状尺（④）只说得了"代码交出的是键"，说不了"那句在屏幕上念得出来"——Ja 列漏一格、槽没填满都由这一格先红。</para></summary>
    [Fact]
    public void TheSentencesAnswerInEveryLanguage()
    {
        var languages = new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese };
        foreach (var key in Keys)
        {
            Assert.True(UiStrings.TryGet(key, out var entry), "表里没有 " + key);
            var slots = Slots(entry.Zh);
            Assert.Equal(slots, Slots(entry.En));
            Assert.Equal(slots, Slots(entry.Ja));
            // 改口一处（本批报告"改口"那一节）：明细前那颗全角空格从前<b>由代码补</b>，本批摘掉——⛔ 改成塞进表里当胶水，
            // 因为换语言那一刻它孤零零挂在拉丁句子中间。台架 A5 第一次跑读绿，就是这一格当时⛔ 看胶水（坑表登记）。
            foreach (var cell in new[] { entry.Zh, entry.En, entry.Ja })
                Assert.DoesNotContain("\u3000", cell, StringComparison.Ordinal);   // U+3000：表里⛔ 有这一颗

            var args = slots.Select(i => (object)("M" + i)).ToArray();
            var said = new HashSet<string>(StringComparer.Ordinal);
            foreach (var language in languages)
            {
                Locale.TrySet(language);
                Assert.Equal(language, Locale.Current);       // ⛔ 拿 TrySet 的返回值当"换没换到"（#112）
                var text = Locale.Fmt(key, args);
                Assert.False(string.IsNullOrWhiteSpace(text), key + " 那一格答的是空话");
                Assert.DoesNotContain("{", text, StringComparison.Ordinal);
                Assert.DoesNotContain("}", text, StringComparison.Ordinal);
                foreach (var i in slots) Assert.Contains("M" + i, text, StringComparison.Ordinal);
                if (!NothingToTranslate.Contains(key)) Assert.True(said.Add(text),
                    $"{key} 有两门语言念出来一模一样：{text}");
            }
        }

        static int[] Slots(string text) =>
            System.Text.RegularExpressions.Regex.Matches(text, "\\{(\\d+)\\}")
                .Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(i => i).ToArray();
    }

    /// <summary>④形状禁项：那四颗句厂与那几处 DTO 交的必须是身份，⛔ 悄悄退回 <c>string</c>。
    /// <para>旧签名（<c>string? Problem()</c>／<c>string Reason(…)</c>）一回来，屏幕那句话就在产地定死了语言——
    /// 而编译⛔ 报错：界面层那时只知道一个 <c>string</c>。所以这一格钉的是"改回去还会红"这个事实（#195：⛔ 留着一条永不为红的判据）。</para></summary>
    [Fact]
    public void TheFactoriesHandIdentitiesNotSentences()
    {
        Assert.Contains("public LocMessage? Problem()", CodeOf(Settings));
        Assert.Contains("public static LocMessage? UrlProblem(string? url, AiProviderKind provider)", CodeOf(Settings));
        Assert.Contains("public LocMessage DescribeWhere() => Provider switch", CodeOf(Settings));
        Assert.Contains("public static string? NameKeyOf(AiProviderKind provider)", CodeOf(Settings));

        Assert.Contains("public static LocMessage Reason(AiFailureKind kind, LocMessage? detail, LocMessage where) => kind switch", CodeOf(Failures));

        Assert.Contains("LocMessage? Detail = null, AiUsage? Usage = null)", CodeOf(Results));
        Assert.Contains("public LocMessage Explain(LocMessage where) =>", CodeOf(Results));
        Assert.Contains("public LocMessage Explain(LocMessage where, string? configuredModel)", CodeOf(Results));

        Assert.Contains("public static LocMessage Preview(IReadOnlyList<string> names, int max = 6)", CodeOf(Wire));

        Assert.Contains("internal sealed record AiTransport(", CodeOf(Http));
        Assert.Contains("LocMessage? Problem = null,", CodeOf(Http));
        Assert.Contains("bool Truncated = false)", CodeOf(Http));
        Assert.Contains("internal static LocMessage? DetailOf(AiTransport transport)", CodeOf(Http));

        Assert.Contains("public sealed record AiProbe(bool Ok, LocMessage Report, IReadOnlyList<string> Models)", CodeOf(Gateway));
        Assert.Contains("public static AiProbe Bad(LocMessage report", CodeOf(Gateway));
        Assert.Contains("LocMessage? Error", CodeOf(Runner));
        Assert.Contains("LocMessage? Error = null)", CodeOf(Classify));

        // ⛔ 退回成品字：旧形状一颗都不许回来
        Assert.Equal(0, Times(AllFamily(), "public string? Problem()"));
        Assert.Equal(0, Times(AllFamily(), "public static string UrlProblem"));
        Assert.Equal(0, Times(AllFamily(), "public string DescribeWhere"));
        Assert.Equal(0, Times(AllFamily(), "public static string Reason(AiFailureKind"));
        Assert.Equal(0, Times(AllFamily(), "string Detail = null"));
        Assert.Equal(0, Times(AllFamily(), "public string Report"));
    }

    /// <summary>⑤两颗死出口⛔ 悄悄回来：<c>AiGateway.ConfigProblem</c> 与 <c>AiFailures.NameOf(AiFailureKind)</c>
    /// 都是<b>零消费者</b>（现读＝<c>recon_67i.txt</c> 的消费者名单：两名的调用点各 0 处），本批按 ✅WI-65 片5c 那一族删掉。
    /// <para>普查扫<b>整棵 src</b>而不是那两个文件：只钉原档的禁项，会因为出口搬去第三档而假绿（SourceGate 的口径）。</para></summary>
    [Fact]
    public void TheTwoZeroConsumerExitsStayDeleted()
    {
        var seen = false;
        foreach (var (rel, text) in SourceGate.ReadRepoUnder("src"))
        {
            if (rel.EndsWith("AiFailures.cs", StringComparison.Ordinal)) seen = true;
            var code = SourceGate.Code(text);
            Assert.Equal(0, Times(code, "ConfigProblem"));
            Assert.Equal(0, Times(code, "NameOf(AiFailureKind"));
        }
        // 正向对照：普查真的看到了那两档，否则"零违规"只是空转（与 ThemeRulesGateTests 的 callSites 同一条规矩）
        Assert.True(seen, "普查没扫到 AiFailures.cs，路径错了");
        Assert.Contains("public static LocMessage Reason(", CodeOf(Failures));
    }

    /// <summary>⑥只进日志的那几句⛔ 被顺手搬进表（本批刻意没搬的第二方向）。
    /// <para>搬过去付的是"29 颗段名 ×3 语"那一档的代价（✅P-191 他裁同一口径），而读者是日志不是眼睛：
    /// <c>StarLog</c> 那两发要在<b>写它的那一刻</b>取一次词，所以交的是 <c>DescribeWhere().Render()</c>。</para></summary>
    [Fact]
    public void WhatOnlyTheLogReadsStaysAString()
    {
        Assert.Equal(2, Times(CodeOf(Gateway), "DescribeWhere().Render()"));
        Assert.Contains("StarLog.Error($\"[AI] 调用失败（{settings.DescribeWhere().Render()}）\", ex);", CodeOf(Gateway));
        Assert.Contains("StarLog.Warn($\"[AI] 连接自检异常（{settings.DescribeWhere().Render()}）：{ex.Message}\");", CodeOf(Gateway));

        // 屏上⛔ 有这一发：那颗 ArgumentOutOfRangeException 的段落名只给开发者读（✅WI-67 片2 同口径）
        Assert.Contains("throw new ArgumentOutOfRangeException(nameof(kind)", CodeOf(Gateway));
        // 开机让路那句说明只写进日志，⛔ 为它编键（它的主人住在 片8 的射程之外）
        Assert.Contains("public static string DescribeSkipped(int instances)", CodeOf(Policy));
        Assert.Equal(0, Times(CodeOf(Policy), "LocMessage"));
    }

    /// <summary>⑦<b>同一颗 <c>LocMessage</c> 实例</b>在换语言前后各念一次：整条"原因"行连<b>槽里嵌的那句</b>一起跟着语言走。
    /// <para>这一格是本批唯一的运行时读数，它验的三件事都是形状尺说不了的：<b>①</b> 槽里那句「连不上哪儿」由
    /// <c>DescribeWhere()</c> 现取（⛔ 把地址翻成另一种说法）；<b>②</b> <c>AiFailures.Reason</c> 把服务明细包成延迟取词的实参
    /// （⛔ 在产地截断＋定死语言，那样换语言只有外层跟着变，屏幕上念出两种语言拼的一行）；<b>③</b> 明细为空时那句兜底话
    /// 也在<b>贴上去那一刻</b>才取词（本批登记的改口：那颗词从前硬写成中文「未给出原因」，还带一颗全角空格当胶水）。
    /// 末了点名<b>哪几支带明细、哪几支整句自足</b>——那是片7 之前的既有口径，本批⛔ 改判，而它从前一颗证人也没有。</para></summary>
    [Fact]
    public void TheReasonLineIsTakenWhenItIsReadNotWhenItIsBuilt()
    {
        var settings = new AiSettings(Enabled: true, Provider: AiProviderKind.Ollama, Model: "qwen2.5:7b");
        var where = settings.DescribeWhere();

        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal(AppLanguage.Chinese, Locale.Current);
        // ①「连不上哪儿」这一支：地址那句嵌在槽里，槽跟着这一门语言念，而地址本身⛔ 被翻译（它是机器事实）。
        var reach = AiFailures.Reason(AiFailureKind.Unreachable, null, where);
        var reachZh = reach.Render();
        Assert.Contains("127.0.0.1:11434", reachZh, StringComparison.Ordinal);     // 该改哪一格：地址必须指名
        Assert.Contains("本机", reachZh, StringComparison.Ordinal);

        Locale.TrySet(AppLanguage.English);
        Assert.Equal(AppLanguage.English, Locale.Current);
        var reachEn = reach.Render();
        Assert.NotEqual(reachZh, reachEn, StringComparer.Ordinal);
        Assert.False(HasHan(reachEn), "换语言后同一行仍念出汉字，说明词是在产地取的：" + reachEn);
        Assert.Contains("127.0.0.1:11434", reachEn, StringComparison.Ordinal);     // 变的只有句子，⛔ 变的是事实

        // ②「服务侧报错」这一支：明细被包成延迟取词的实参——⛔ 在产地截断＋定死语言，
        // 那样换语言只有外层跟着变，屏幕上念出的是两种语言拼的一行。
        var line = AiFailures.Reason(AiFailureKind.ServiceError, LocMessage.Of("common.rawValue", "500 boom"), where);
        Assert.Contains("500 boom", line.Render(), StringComparison.Ordinal);
        // 改口一处：那颗明细从前由代码补一颗全角空格当胶水（换语言它就孤零零挂在英文句子中间），现在句子自己带标点
        Assert.DoesNotContain("\u3000", line.Render(), StringComparison.Ordinal);   // 全角空格⛔ 由代码补（写成转义是怕它看不见）
        Locale.TrySet(AppLanguage.Chinese);
        Assert.Contains("500 boom", line.Render(), StringComparison.Ordinal);
        // 改口二处：BadResponse 那一支从前"没明细就什么都不补"，现在与另两支共用同一颗兜底键（⛔ 一件事三种说法）
        Assert.Contains(Locale.T("ai.failure.noReason"),
            AiFailures.Reason(AiFailureKind.BadResponse, null, where).Render(), StringComparison.Ordinal);

        // 兜底那一支：⚠ 只在<b>一门</b>语言里比"期望＝现取的那颗键"是⛔ 看不见的——硬写的中文与取词的键
        // 在中文那一刻逐字相等（台架 A4 第一跑就是被这个形状骗绿的）。同一颗实例必须在两门语言里各念一次。
        Locale.TrySet(AppLanguage.English);
        Assert.Equal(AppLanguage.English, Locale.Current);
        var blank = AiFailures.Reason(AiFailureKind.Unknown, null, where);
        Assert.Contains(Locale.T("ai.failure.noReason"), blank.Render(), StringComparison.Ordinal);
        Assert.False(HasHan(blank.Render()), "明细为空时那句兜底话冻在中文了：" + blank.Render());
        Locale.TrySet(AppLanguage.Chinese);
        Assert.Contains(Locale.T("ai.failure.noReason"), blank.Render(), StringComparison.Ordinal);

        // 有哪几支把服务原文念进槽、有哪几支整句自足（⛔ 再念一遍服务那句）。这是<b>片7 之前既有口径</b>，本批⛔ 改判；
        // 它从前没有证人——有人删掉一支的槽，屏幕上那句明细就整条消失（＝✅P-195 那一族的形状），只有这一格看得见。
        var carriesDetail = new[]
        {
            AiFailureKind.NotConfigured,   // 有明细走 notConfiguredWith，没有走 notConfigured 那句落点
            AiFailureKind.ServiceError,
            AiFailureKind.BadResponse,
            AiFailureKind.Unknown,
        };
        foreach (var kind in Enum.GetValues<AiFailureKind>())
        {
            var said = AiFailures.Reason(kind, LocMessage.Of("common.rawValue", "SRV-TEXT"), where).Render();
            if (carriesDetail.Contains(kind))
                Assert.Contains("SRV-TEXT", said, StringComparison.Ordinal);
            else
                Assert.DoesNotContain("SRV-TEXT", said, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(said), $"{kind} 那一支答的是空话");
        }
    }

    /// <summary>⑧模型清单那一串：连接号与「共几个」都在<b>念出来那一刻</b>才取（<see cref="LocJoin"/> 的第二位句厂宿主）。
    /// <para>⛔ 在这里拼 <c>string.Join("、")</c>：那正是 ✅WI-41 ⑦ 那一格钉过的失效形状（外层重贴了而那一串停在上一门）。</para></summary>
    [Fact]
    public void TheModelPreviewIsJoinedWhenItIsReadNotWhenItIsBuilt()
    {
        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal(AppLanguage.Chinese, Locale.Current);
        var three = AiWire.Preview(new[] { "llama3", "qwen2.5:7b", "mistral" });
        var zh = three.Render();
        Assert.Equal("llama3、qwen2.5:7b、mistral", zh, StringComparer.Ordinal);   // 名字是数据，⛔ 一个都不许改

        Locale.TrySet(AppLanguage.English);
        Assert.Equal(AppLanguage.English, Locale.Current);
        var en = three.Render();
        Assert.DoesNotContain("、", en, StringComparison.Ordinal);                  // 连接号跟着这一门语言走
        Assert.Contains(", ", en, StringComparison.Ordinal);
        Assert.NotEqual(zh, en, StringComparer.Ordinal);

        // 超出上限那一支：后半句在贴那一刻才决定念不念——缺的那一槽交 null，⛔ 拼一句中文成品字
        var many = AiWire.Preview(new[] { "a", "b", "c", "d", "e", "f", "g" });
        Assert.Contains("7", many.Render(), StringComparison.Ordinal);
        Assert.DoesNotContain("{", many.Render(), StringComparison.Ordinal);
        var few = AiWire.Preview(new[] { "a", "b" });
        Assert.DoesNotContain("7", few.Render(), StringComparison.Ordinal);

        var none = AiWire.Preview(Array.Empty<string>());
        Assert.Contains(Locale.T("ai.preview.none"), none.Render(), StringComparison.Ordinal);
    }
}
