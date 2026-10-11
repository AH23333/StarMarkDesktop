#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-41（P-169 ① 最后一格 ＝ P-188 第四刀·AI 那一栏）的闸门：<b>整栏那 70 颗键只许住在表里</b>
/// （另有一颗 <c>settings.engine.sizeApprox</c> 住在 <c>settings.engine.*</c> 门下，由 ③ 那一格单独盯）。
/// <para>
/// 射程＝<b>落在 AI 那一屏上的每一行 UI 层屏幕字</b>，按屏幕宿主划⛔ 按我原先那份名单划：
/// 除 <c>SettingsPage.Ai.cs</c>／<c>SettingsPage.AiUsage.cs</c>／<c>SettingsPage.Engine.cs</c> 三档，
/// 还含 <c>Services/AiClassifyService.cs</c>（状态回调用 <c>onStatus</c> 直接递给 <c>AiStatusText</c> 那 8 行）
/// 与 <c>Helpers/SettingsStore.Ai.cs</c>（消毒原因那句回给 <c>AiTimeoutNoteText</c> 那 2 行）——
/// ⚠ 那两档从前⛔ 在这把尺里：<b>P-188 的名单只走了 Views＋ViewModels 两棵目录</b>（见
/// <c>bin/WI59/census_59a.py</c> 的 <c>for d in (...)</c>），本批按屏幕宿主补进来（登记在账本 P-188 补记）。
/// </para>
/// <para>
/// <b>本批刻意没动的两类</b>：<c>AiGroupRow.TagLine/ItemLine</c> 那 3 行是「数据项＋模板」，
/// 换语言那一刻没有东西会重贴绑定（✅P-175 缺口②，归 WI-40-3）；<c>NameOf</c> 那 4 行进的是<b>发给模型的提示词</b>，
/// ⛔ 上屏幕（与 <c>AiClassify.cs</c> 那 25 行提示词同一口径）。两者各由下面一格钉住"没被半搬"。
/// </para>
/// <para>
/// 机制⛔ 新东西：交「键＋实参」＋ <c>LocText.SetMessage</c> 是 ✅WI-61b/c 走通的门，
/// 组合句交可空槽是 ✅WI-61c 那颗 <c>settings.data.autoBackupSentence</c> 的门，
/// 跨层成品字走 <c>common.rawValue</c> 是 ✅WI-61d 那颗门的第二次使用。
/// 设置页仍⛔ 成为第十位 <c>Locale.Changed</c> 主人——重贴归 <c>LocText</c> 那唯一一处全局订阅。
/// </para>
/// <para>颗数一律现读（<c>bin/WI41/census_41.txt</c>／<c>bin/WI41/sites_41_after.txt</c>，2026-10-10 现场），⛔ 凭记忆（#159/#160）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class AiWordGateTests : IDisposable
{
    private const string Ai = "src/StarMark.UI/Views/SettingsPage.Ai.cs";
    private const string Usage = "src/StarMark.UI/Views/SettingsPage.AiUsage.cs";
    private const string Engine = "src/StarMark.UI/Views/SettingsPage.Engine.cs";
    private const string Store = "src/StarMark.UI/Helpers/SettingsStore.Ai.cs";
    private const string Service = "src/StarMark.UI/Services/AiClassifyService.cs";
    private const string PageXaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    /// <summary>本批交进表里的键，逐颗点名（计数型判据抓不到"换名"＝#164）。</summary>
    private static readonly string[] Keys =
    {
        "settings.ai.status.configBlocked",
        "settings.ai.status.whereHint",
        "settings.ai.status.cancellingProbe",
        "settings.ai.status.probing",
        "settings.ai.status.probeCancelled",
        "settings.ai.status.probeFailed",
        "settings.ai.status.organiseBlocked",
        "settings.ai.status.estimating",
        "settings.ai.status.preparing",
        "settings.ai.status.batchDone",
        "settings.ai.status.stoppedWithLeft",
        "settings.ai.status.organiseFailed",
        "settings.ai.status.stopping",
        "settings.ai.status.writeFailed",
        "settings.ai.status.usageReadFailed",
        "settings.ai.status.budgetSaveFailed",
        "settings.ai.status.unpauseFailed",
        "settings.ai.fillModel",
        "settings.ai.batchLimits",
        "settings.ai.timeoutNote",
        "settings.ai.timeoutBlank",
        "settings.ai.timeoutNotInt",
        "settings.ai.timeoutOutOfRange",
        "settings.ai.timeoutStoredOutOfRange",
        "settings.ai.confirm.title",
        "settings.ai.confirm.detailRuled",
        "settings.ai.confirm.detailPlain",
        "settings.ai.confirm.body",
        "settings.ai.confirm.go",
        "settings.ai.result.full",
        "settings.ai.result.none",
        "settings.ai.result.count",
        "settings.ai.result.missing",
        "settings.ai.result.unknown",
        "settings.ai.result.dropped",
        "settings.ai.result.droppedWith",
        "settings.ai.result.stopped",
        "settings.ai.result.failed",
        "settings.ai.result.failedWith",
        "settings.ai.result.tail",
        "settings.ai.resumeIdle",
        "settings.ai.resumeCount",
        "settings.ai.applyScopeAll",
        "settings.ai.applyScopeGroup",
        "settings.ai.tagJoin",
        "settings.ai.applyNothing",
        "settings.ai.applyDone",
        "settings.ai.applyNoop",
        "settings.ai.rejectedAll",
        "settings.ai.rejectedOne",
        "settings.ai.usage.warnNote",
        "settings.ai.usage.warnRejected",
        "settings.ai.usage.noLedger",
        "settings.ai.usage.noneThisMonth",
        "settings.ai.usage.window",
        "settings.ai.usage.estimated",
        "settings.ai.usage.featureJoin",
        "settings.ai.usage.featureBlock",
        "settings.ai.usage.full",
        "settings.ai.usage.warnTail",
        "settings.ai.usage.pausedWithButton",
        "settings.ai.usage.paused",
        "settings.ai.service.loadingCandidates",
        "settings.ai.service.nothingToOrganise",
        "settings.ai.service.customRulesIgnored",
        "settings.ai.service.planRuled",
        "settings.ai.service.planPlain",
        "settings.ai.service.budgetTripped",
        "settings.ai.service.budgetBlocked",
        "settings.ai.service.channelBlocked",
    };

    /// <summary>那五档里"这一格的话"⛔ 再由宿主或标记贴第二遍。</summary>
    private static readonly string[] Cells =
    {
        "AiStatusText", "AiUsageText", "AiWarnRatioNoteText", "AiTimeoutNoteText", "AiBatchLimitsText",
    };

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>五档合起来才是这一栏的语料：只数三档 Views 会漏掉本批补进来的那两档（＝射程那一课）。</summary>
    private static string AllSources() =>
        string.Join("\n", new[] { CodeOf(Ai), CodeOf(Usage), CodeOf(Engine), CodeOf(Store), CodeOf(Service) });

    /// <summary>点名形状：<b>名单里已是整颗键</b>（由 <c>table_41</c> 现生成，⛔ 手打也⛔ 在这里再补前缀——
    /// 补了就是 <c>settings.ai.settings.ai.…</c>，而"表里没有这一行"听起来像表漏了）。
    /// 整颗键连<b>收尾那颗引号</b>一起数——⛔ 只认 <c>LocMessage.Of(</c> 打头：两颗连接键是当<b>形参</b>
    /// 交给 <c>Joined(…)</c> 的（那里要的是键，不是成品句）。收尾引号也⛔ 省：省了 <c>result.dropped</c>
    /// 会把 <c>result.droppedWith</c> 也算成自己点的名。（34 就是双引号：⛔ 在本档再叠一层反斜杠转义，
    /// 生成器与 C# 两头都要数引号，本批已经在这里数错过两次。）</summary>
    private static string Of(string key) => (char)34 + key + (char)34;

    /// <summary>出现次数：整颗 needle 一起数（⛔ 按行或按字符类数＝#149 那一族假读数）。</summary>
    private static int Times(string haystack, string needle) =>
        haystack.Length == 0 || needle.Length == 0 ? 0 : haystack.Split(new[] { needle }, StringSplitOptions.None).Length - 1;

    /// <summary>①颗数：名单长度当场对平（这颗数的是<b>我自己抄进本档的那一串</b>，加一颗不登记就红）。</summary>
    [Fact]
    public void TheRosterHasExactlyTheKeysThisBatchHandedIn()
    {
        Assert.Equal(70, Keys.Length);
        Assert.Equal(70, Keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>②双向对账：每颗在表里恰好一行、在五档里恰好被点名一次、在标记里一次都没有。</summary>
    [Fact]
    public void EveryKeyIsDeclaredOnceAndNamedExactlyOnce()
    {
        var all = AllSources();
        var xaml = SourceGate.ReadRepoFile(PageXaml);
        foreach (var key in Keys)
        {
            Assert.Single(UiStrings.All, entry => entry.Key == key);
            Assert.Equal(1, Times(all, Of(key)));
            Assert.Equal(0, Times(xaml, key));        // 句子⛔ 由标记挂：这一栏那几格全是代码当场算的
        }
    }

    /// <summary>③形状禁项：那五格⛔ 再直接写字，回型⛔ 悄悄退回 <c>string</c>。</summary>
    [Fact]
    public void ThoseFiveCellsHandIdentitiesAndNotSentences()
    {
        var all = AllSources();
        foreach (var cell in Cells)
        {
            Assert.Equal(0, Times(all, cell + ".Text = \""));
            Assert.Equal(0, Times(all, cell + ".Text = $\""));
        }
        Assert.Equal(0, Times(all, "AiFillModelButton.Content = \""));
        Assert.Equal(0, Times(all, "AiResumeButton.Content = \""));
        Assert.Contains("private async Task ApplyAsync(ClassifyPlan plan, LocMessage label)", CodeOf(Ai));
        Assert.Contains("public (int Seconds, LocMessage? Reason) LoadAiBatchTimeout", CodeOf(Store));
        Assert.Contains("    LocMessage? FirstError)", CodeOf(Service));
        Assert.Contains("int limit, Action<LocMessage> onStatus,", CodeOf(Service));
        Assert.Contains("private static LocMessage Describe(OrganiseOutcome outcome)", CodeOf(Ai));
        Assert.Contains("private static LocMessage DescribeFixedBatchLimits()", CodeOf(Ai));
        Assert.Contains("private static LocMessage? Joined(", CodeOf(Ai));
        // 「约」那颗词从前坐在数据实参里（`（约 1.2 MB）` 整块交进 deleteBody 的槽）⇒ 换语言它还是中文。
        Assert.Contains("Of(\"settings.engine.sizeApprox\", FileSizeText.Human(bytes))", CodeOf(Engine));
    }

    /// <summary>
    /// ④组合句⛔ 退回"代码里拼一句"。
    /// <para>那两句长话（一轮整理的汇总、用量面板那一长行）从前是 <c>text +=</c> 一段一段接出来的：
    /// 交表时最容易做成"外层还是拼接、只把每段换成 <c>Locale.T</c>"——那样换语言那一刻仍然只有<b>外层那句</b>被重贴，
    /// 而外层早在算出来那一瞬定稿 ⇒ 屏幕上留的是上一次的语言（＝P-188 那个原始缺陷的换皮形状）。</para>
    /// </summary>
    [Fact]
    public void TheComposedSentencesJoinThroughKeysNotThePlusOperator()
    {
        var all = AllSources();
        // 禁的是"逐段拼接"那个形状：种句⛔ 再从一个字面量开出来（`var text = "` 是梯子头，
        // ⛔ 写裸的 `var text = `——本栏另有两处把输入框的当前文本读进局部，那不是梯子，误伤会把闸门拗成背字符串）。
        Assert.Equal(0, Times(CodeOf(Ai), "text += "));
        Assert.Equal(0, Times(CodeOf(Ai), "var text = " + (char)34));
        Assert.Equal(0, Times(CodeOf(Usage), "text += "));
        Assert.Equal(0, Times(CodeOf(Usage), "var text = " + (char)34));
        Assert.Equal(0, Times(CodeOf(Service), "status += "));
        Assert.DoesNotContain("string.Join(\"；\"", all, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"settings.ai.result.full\"", CodeOf(Ai));
        Assert.Contains("LocMessage.Of(\"settings.ai.usage.full\"", CodeOf(Usage));
    }

    /// <summary>⑤那颗确认框改道收身份的工厂；裸 <c>ContentDialog</c>⛔ 再在这条路上出现。</summary>
    [Fact]
    public void TheEstimateDialogGoesThroughTheDialogFactory()
    {
        var confirm = SourceGate.MethodBody(CodeOf(Ai), "private async Task<bool> ConfirmOrganiseAsync");
        Assert.Contains("CenteredDialog.ConfirmAsync(", confirm);
        Assert.Contains("dedupeKey: \"aiorganiseconfirm\"", confirm);
        Assert.Equal(0, Times(confirm, "new ContentDialog"));
        Assert.Equal(0, Times(confirm, "ContentDialogButton."));
        //  ⚠ 改口（账本 P-188 补记）：那格从前指着「token 预算与用量」——那五个字只活在两份注释里，屏幕上从来没有它。
        Assert.DoesNotContain("token 预算与用量", CodeOf(Ai));
    }

    /// <summary>
    /// ⑥语言读数：每颗键三语都答话、槽数三列一致，实参⛔ 被吞也⛔ 露 <c>{0}</c>。
    /// <para>槽数由<b>那一行自己</b>报出来（⛔ 在这里再抄一份手打的数＝第二份事实源）；
    /// 真正独立的那一条是"三列给的槽必须一模一样"，它由 <see cref="I18nContractTests"/> 与本格一起守。</para>
    /// </summary>
    [Fact]
    public void TheSentencesAnswerInEveryLanguageAndCarryTheirReadouts()
    {
        var languages = new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese };
        foreach (var key in Keys)
        {
            Assert.True(UiStrings.TryGet(key, out var entry), "表里没有 " + key);
            var slots = Slots(entry.Zh);
            Assert.Equal(slots, Slots(entry.En));
            Assert.Equal(slots, Slots(entry.Ja));
            if (slots.Length == 0) continue;
            var args = slots.Select(i => (object)("M" + i)).ToArray();
            var said = new List<string>();
            foreach (var language in languages)
            {
                Locale.TrySet(language);
                Assert.Equal(language, Locale.Current);   // ⛔ 拿 TrySet 的返回值当"换没换到"（#112）
                var text = Locale.Fmt(key, args);
                Assert.DoesNotContain("{", text);
                Assert.DoesNotContain("}", text);
                Assert.Contains("M0", text);              // 实参进了槽，⛔ 被句子吞掉
                said.Add(text);
            }
            Assert.NotEmpty(said);
        }

        static int[] Slots(string text) =>
            System.Text.RegularExpressions.Regex.Matches(text, "\\{(\\d+)\\}")
                .Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(i => i).ToArray();
    }

    /// <summary>
    /// ⑦那两类本批刻意没动的，钉住"⛔ 半搬"：它们要么整句走、要么整句不动。
    /// <para>只搬一半的表现是<b>同一行里两种语言</b>，而那种"看着像做完了"的状态比原缺陷更难发现
    /// （✅WI-61c 那颗 <c>TheFourCrossLayerRowsAreNeitherHalfTranslatedNorPasted</c> 同一口径）。</para>
    /// </summary>
    [Fact]
    public void TheTemplateRowsAndThePromptDataAreNeitherHalfMovedNorPasted()
    {
        var ai = CodeOf(Ai);
        var row = SourceGate.MethodBody(ai, "public AiGroupRow(TagGroup group, IReadOnlyList<TagProposal> proposals)");
        // 那三行仍是字面量（由 {Binding} 递给模板），且⛔ 有人给它半贴：
        Assert.Contains("TagLine = string.Join(\"、\"", row);
        Assert.Contains("ItemLine = \"条目编号 \"", row);
        var xaml = SourceGate.ReadRepoFile(PageXaml);
        Assert.Contains("{Binding TagLine}", xaml);   // 仍由模板绑定＝换语言那一刻没人重贴的那个形状
        Assert.Contains("{Binding ItemLine}", xaml);
        Assert.Equal(0, Times(ai, "\"settings.ai.tagLine\""));   // ⛔ 半搬：不许只把标签那半交出去
        Assert.Equal(0, Times(ai, "\"settings.ai.itemLine\""));

        var nameOf = SourceGate.MethodBody(CodeOf(Service), "private static string NameOf(Item item)");
        Assert.Contains("ItemType.File => \"本地文件\"", nameOf);   // 那是发给模型的提示词数据，⛔ 上屏幕
        Assert.Equal(0, Times(CodeOf(Service), "\"settings.ai.kind"));
    }

    /// <summary>
    /// ⑧跨层那扇门只此一道：本栏把别层念完的句子交回界面时，一律走 <c>common.rawValue</c>，
    /// 且⛔ 在这一栏里再长出第二道（那意味着有人开始把成品字往骨架上直接拼）。
    /// </summary>
    [Fact]
    public void CrossLayerSentencesAllComeThroughTheOneRawValueDoor()
    {
        var all = AllSources();
        const string door = "LocMessage.Of(\"common.rawValue\"";
        Assert.Equal(14, Times(all, door));   // bin/WI41/named_41.txt 现场数：Ai 8 ＋ AiUsage 4 ＋ Service 2
        // 每一处都必须在实参位上（⛔ 把成品字当整句贴：那正是 P-188 的确证形状）。
        Assert.Contains("LocText.SetMessage(AiStatusText, LocMessage.Of(\"common.rawValue\", probe.Report))", CodeOf(Ai));
    }

    /// <summary>⑨纯胶水那四颗⛔ 被"为了不同形"改掉：三列同形／中日同形是<b>事实</b>，不是没翻。</summary>
    [Fact]
    public void TheGlueKeysSayTheSameThingInAllThreeLanguages()
    {
        Row("settings.ai.result.full", "{0}{1}{2}{3}{4}{5}{6}");
        Row("settings.ai.usage.full", "{0}{1}{2}{3}");
        Row("settings.ai.tagJoin", "{0}、{1}", "{0}, {1}", "{0}、{1}");
        Row("settings.ai.usage.featureBlock", "。{0}。", ". {0}.", "。{0}。");

        static void Row(string key, string zh, string? en = null, string? ja = null)
        {
            Assert.True(UiStrings.TryGet(key, out var entry), "表里没有 " + key);
            Assert.Equal(zh, entry.Zh);
            Assert.Equal(en ?? zh, entry.En);
            Assert.Equal(ja ?? zh, entry.Ja);
        }
    }
}
