#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions;
using StarMark.Abstractions.Feed;
using StarMark.Abstractions.I18n;
using StarMark.Core.Feed;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-65 片2（P-194 第二颗）：<b>主窗「RSS」那一列每一行那句状态</b>的两层同批交表。
/// <para>这一行从前是 <c>RssSourceStatus</c> 当场 <c>Locale.Fmt</c> 念完整句 → <c>RssSourceSection.Status</c>（<c>string</c>）
/// → 建树时 <c>Text = section.Status</c>。那一行住在<b>长命</b>的树里，而这一页与那一档的 <c>Locale.Changed</c> 从前<b>一颗都没有</b>
/// ⇒ 换语言之后它停在上一门语言，且夹在外层句子里那半段（多久以前／什么时候到期）连翻都不翻。</para>
/// <para>现在两层一起：判据交<b>没取词的</b>「键＋实参」，跨层载体（<c>Status</c>／<c>SetStatus</c>）持 <see cref="LocMessage"/>，
/// 贴上屏幕走宿主那一拍（<c>LocText.SetMessage</c>）；而那两处「主句＋那半句」的拼接交的是<b>表里已有的那颗纯胶水键</b>
/// <c>rss.statusJoin</c>——⛔ 在 C# 里用 <c>+</c> 拼，因为语序与标点归表（同页表头那三处早就是这颗键的主人）。</para>
/// <para>本批<b>没有新造一颗键</b>（第⑧格逐颗点名，多一颗少一颗都红）。锚点一律写成带引号的整条字面（坑表 #171/#179）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class RssWordGateTests : IDisposable
{
    private const string Judge = "src/StarMark.Core/Feed/RssSourceStatus.cs";
    private const string Vm = "src/StarMark.UI/ViewModels/RssPageViewModel.cs";
    private const string Page = "src/StarMark.UI/Views/RssPage.xaml.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";

    private const long Now = 1_770_000_000;
    private const long Hour = 3_600;

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static int Times(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    private static RssSourceConfig Source(bool enabled = true) => new(1, "开源中国", "https://a.test/feed", enabled);

    private static RssEntry Entry() => new("标题", "https://a.test/x", null, "摘要", null, 1, "源");

    private static RssSourceOutcome Outcome(bool stopped = false, bool enabled = true, bool notModified = false,
                                            string? error = null, int entries = 0)
        => new(Source(enabled), Enumerable.Range(0, entries).Select(_ => Entry()).ToList(), error, notModified, stopped);

    /// <summary>①判据那一档现在只交「键＋实参」：三颗公开出口都换型，两颗私有助手交可空的半句，
    /// 且<b>整档一处都没当场取词</b>（＝✅WI-65a 称出的那一档 13 处全部搬走）。</summary>
    [Fact]
    public void TheJudgeHandsOutKeysAndNeverReadsThemItself()
    {
        var code = Code(ReadRepoFile(Judge));
        Assert.Equal(3, Times(code, "public static LocMessage"));       // NeverFetched／FromCache／Describe
        Assert.Equal(2, Times(code, "private static LocMessage?"));      // Grew／StillThere＝可空的半句
        Assert.Equal(1, Times(code, "private static LocMessage Join"));
        Assert.Equal(0, Times(code, "Locale.T(") + Times(code, "Locale.Fmt("));
        Assert.Equal(14, Times(code, "LocMessage.Of("));                 // 正本＝bin/WI65/census_65.txt A.2
        Assert.Equal(0, Times(code, "public static string"));            // ⛔ 留一颗交字符串的出口＝这一行又是半中半英
    }

    /// <summary>②那两处「主句＋半句」的拼接归<b>表里那颗纯胶水键</b>：⛔ 在 C# 里用 + 拼成品字
    /// （拼完的那一刻语言就定死了，而两颗键各自的标点本来是三语各写各的）。</summary>
    [Fact]
    public void TheTwoHalvesAreJoinedByTheTableRatherThanByAPlusSign()
    {
        var code = Code(ReadRepoFile(Judge));
        Assert.Equal(1, Times(code, "LocMessage.Of(\"rss.statusJoin\", head, tail)"));
        Assert.Equal(0, Times(code, "\" + StillThere("));
        Assert.Equal(0, Times(code, "\" + Grew("));
        // 那颗胶水键在表里恰好一行（三语逐字相同＝纯胶水，I18nContractTests 名下有白名单）
        Assert.Equal(1, Times(ReadRepoFile(Table), "new Entry(\"rss.statusJoin\""));
    }

    /// <summary>③那一行不再被赋值成品串：建树时交元素名，宿主那一拍贴，取词发生在这一刻</summary>
    [Fact]
    public void TheRowIsPastedByTheHostRatherThanAssigned()
    {
        var page = Code(ReadRepoFile(Page));
        Assert.Equal(0, Times(page, "Text = section.Status"));
        Assert.Equal(1, Times(page, "LocText.SetMessage(statusText, section.Status)"));
        // ⛔ 为这一行在页面上再挂一位 Locale.Changed 主人：重贴归 LocText 那份名单（✅WI-40-4 那颗成对闸门盯着）
        Assert.Equal(Times(page, "Locale.Changed +=") * 2, Times(page, "Locale.Changed -="));
    }

    /// <summary>④跨层载体换型，且那颗"同一句话就别再通知"的短路⛔ 用 <c>Render()</c> 或 record 相等复原：
    /// 前者把取词放回界面那一路（<c>I18nWiringGateTests</c> 钉着这一档 <c>.Render()</c> 命中 0），
    /// 后者对数组只比引用＝永远不相等（坑表 #480）。</summary>
    [Fact]
    public void TheCarrierHoldsKeysAndTheOldShortCircuitDoesNotComeBack()
    {
        var vm = Code(ReadRepoFile(Vm));
        Assert.Equal(1, Times(vm, "public LocMessage Status { get; private set; } = RssSourceStatus.NeverFetched;"));
        Assert.Equal(1, Times(vm, "public void SetStatus(LocMessage status)"));
        Assert.Equal(0, Times(vm, "Status == status"));
        Assert.Equal(0, Times(vm, ".Render()"));
        Assert.Equal(5, Times(vm, "section.SetStatus(RssSourceStatus."));   // 五个回填点都还在（少一个＝有人把回填删了）
    }

    /// <summary>⑤这颗批次的核心承诺：同一颗消息在三种语言下各念一次，那半句的<b>实参</b>照样带得过去。</summary>
    [Fact]
    public void TheSameMessageRendersInWhateverLanguageIsCurrentAtPasteTime()
    {
        var m = RssSourceStatus.Describe(Outcome(stopped: true), 9);
        Locale.TrySet(AppLanguage.Chinese);
        var zh = m.Render();
        Locale.TrySet(AppLanguage.English);
        var en = m.Render();
        Locale.TrySet(AppLanguage.Japanese);
        var ja = m.Render();
        Assert.Equal(3, new[] { zh, en, ja }.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, new[] { zh, en, ja }.Count(s => s.Contains("9", StringComparison.Ordinal)));
    }

    /// <summary>⑥套娃那一格：外层重贴时<b>里面那半段相对时间</b>跟着一起换语言——这一族最初的事故形状。
    /// <para>⚠ 这一格⛔ 只看外层：外层翻了就以为整句翻了，正是屏幕上「Last fetched 3 天前: …」的来源。</para></summary>
    [Fact]
    public void TheNestedAgeSegmentRendersWithTheOuterSentence()
    {
        var cached = new RssCachedSource { SourceId = 1, FetchedAtUnix = Now - 3 * Hour, Entries = new List<RssCachedEntry>() };
        var m = RssSourceStatus.FromCache(cached, Now);

        Locale.TrySet(AppLanguage.Chinese);
        Assert.Contains(DateTimeText.HoursAgoMessage(3).Render(), m.Render(), StringComparison.Ordinal);
        Locale.TrySet(AppLanguage.English);
        Assert.Contains(DateTimeText.HoursAgoMessage(3).Render(), m.Render(), StringComparison.Ordinal);
        Assert.NotEqual(m.Render(), Locale.T("rss.src.cacheLine"));   // 那句模板本身⛔ 就是成品字
    }

    /// <summary>⑦那半句只在两个数真的不等时才出现（相等时重复一遍数字是噪音；没有那个数时保持原样）。</summary>
    [Fact]
    public void TheTailClauseAppearsOnlyWhenThoseTwoNumbersReallyDiffer()
    {
        Locale.TrySet(AppLanguage.Chinese);
        var one = Outcome(entries: 1);
        Assert.Contains("缓存共 20 条", RssSourceStatus.Describe(one, 20).Render(), StringComparison.Ordinal);
        Assert.DoesNotContain("缓存共", RssSourceStatus.Describe(one, 1).Render(), StringComparison.Ordinal);
        Assert.DoesNotContain("缓存共", RssSourceStatus.Describe(one).Render(), StringComparison.Ordinal);
        // 停用那一格⛔ 被那半句糊掉（批次 NF 的教训：五种"没有内容"分不开，用户就不知道该改地址还是查网络）
        Assert.DoesNotContain("仍然摆着缓存里的", RssSourceStatus.Describe(Outcome(enabled: false), 9).Render(), StringComparison.Ordinal);
    }

    /// <summary>⑧这一批⛔ 新造键：十三颗 <c>rss.src.*</c> 加那颗胶水键，逐颗点名＝表里各一行、判据里各一处。</summary>
    [Fact]
    public void EveryKeyTheJudgeNamesIsDeclaredOnceAndNoKeyWasInvented()
    {
        var table = ReadRepoFile(Table);
        var code = Code(ReadRepoFile(Judge));
        var keys = new[]
        {
            "rss.src.neverFetched", "rss.src.cacheLine", "rss.src.dueNow", "rss.src.dueIn",
            "rss.src.notInRound", "rss.src.stopped", "rss.src.disabled", "rss.src.notModified",
            "rss.src.failed", "rss.src.emptyOk", "rss.src.fetched", "rss.src.grew", "rss.src.stillThere",
            "rss.statusJoin",
        };
        Assert.Equal(14, keys.Length);       // 13 颗状态句＋那颗胶水（本批新增 0 颗）
        foreach (var key in keys)
        {
            Assert.Equal(1, Times(table, "new Entry(\"" + key + "\""));
            Assert.Equal(1, Times(code, "\"" + key + "\""));
        }
    }
}
