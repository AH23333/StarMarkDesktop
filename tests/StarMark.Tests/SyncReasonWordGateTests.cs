#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Sync;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 同步「哪一源、为什么」那一族的闸门（批次 WI-81·P-212 走 B 的落地腿）。
/// <para>要守的东西有两层，缺任何一层这一条都还没结案：</para>
/// <para><b>①形状</b>：<see cref="SourceSyncResult.Error"/> 从前是 <c>string?</c>，住的是
/// 「拉取超时或被取消（未中断其余源）」那种<b>写死的中文</b>；<see cref="SourceSyncErrorKind"/> 的注释
/// 承诺"供设置页/同步状态给出可操作文案"而<b>全仓零读者</b>（＝坑表 #588：注释写的是打算，不是现场）。
/// 编译器对这两件事一声不响——所以「交的是键而⛔ 成品字」与「那颗 enum 有没有读者」只能这样数。</para>
/// <para><b>②时机</b>：屏幕上那一行从前只报"失败 N 源"那颗数，<b>为什么</b>一处都没答。
/// 现在 <see cref="SyncSummary.FailureLines"/> 逐源交「源名＋原因」，而<b>源名与原因都是套在外层句子里的
/// <see cref="LocMessage"/></b>：它们⛔ 在 Core 里念完（那样换语言只会重贴外层，括号里停在上一门话＝
/// ✅WI-79 那族 P-211 的同一个形状）。</para>
/// <para>这一档挂了 <c>[Collection("LocaleGlobal")]</c>：它按 <c>I18nWiringGateTests</c> 那颗闸门的要求
/// 会改进程级那颗语言静态，⛔ 与别的证人类并跑（✅WI-79 第一次跑全量就被这颗点了红）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class SyncReasonWordGateTests : IDisposable
{
    private const string Coord = "src/StarMark.Core/Sync/SyncCoordinator.cs";
    private const string Status = "src/StarMark.UI/MainWindow.Status.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";

    /// <summary>这扇闸守的 15 颗：11 颗 sync.src.* ＋顶栏那张卡的两对标题/那句汇总与那颗就地按钮。</summary>
    private static readonly string[] NewKeys =
    [
        "sync.src.unavailable", "sync.src.timeout", "sync.src.auth", "sync.src.rateLimit",
        "sync.src.forbidden", "sync.src.server", "sync.src.githubOther", "sync.src.other",
        "sync.src.line", "sync.src.lineJoin", "sync.src.truncated",
        "top.syncPartialTitle", "top.syncFixToken", "top.syncDoneTruncated", "top.syncTruncatedTitle",
    ];

    /// <summary>两颗<b>刻意同形</b>的胶水位：它们连的是别人的读数，没有词可翻（理由登记在
    /// <c>I18nContractTests</c> 那份三列同形名单里，⛔ 在这里靠豁免表放宽判据）。</summary>
    private static readonly string[] Skeletons = ["sync.src.line", "sync.src.lineJoin"];

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static SyncSummary Summary(params SourceSyncResult[] rows)
        => new() { StartedAt = 0, ElapsedMs = 12, Sources = rows.ToList() };

    private static SourceSyncResult Failed(string id, string key, SourceSyncErrorKind kind = SourceSyncErrorKind.Unknown)
        => new() { SourceId = id, Success = false, PulledCount = 0, Error = LocMessage.Of(key), ErrorKind = kind };

    /// <summary>全 src 里"问表要这句话"的那一行（⛔ 算进表自己的那一行）。</summary>
    private static int AsksInCode(string key)
        => ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Sum(f => Count(Code(f.Text), "\"" + key + "\""));

    /// <summary>①那一族的出口⛔ 再交成品字：类型是 <see cref="LocMessage"/>，而赋值处⛔ 直接跟一串字。
    /// <para>补上的第二根尺数的是<b>不同键的颗数</b>而不只<b>问表那颗动作的颗数</b>：两源各说一句却被挤进
    /// 同一颗键时，出口颗数一点没变（台架 M11 那一臂当场把这件事演示给我看——只数 occurrences 的那颗尺
    /// 对它全绿）。</para></summary>
    [Fact]
    public void TheOutletsHandOutKeysAndNeverFinishedSentences()
    {
        var code = Code(ReadRepoFile(Coord));
        Assert.Equal(1, Count(code, "public LocMessage? Error { get; init; }"));
        Assert.Equal(0, Count(code, "public string? Error"));
        Assert.Equal(0, Count(code, "Error = \""));                    // 成品中文直接赋值＝这一族回来的形状
        Assert.Equal(11, Count(code, "LocMessage.Of(\"sync.src."));    // 九颗原因＋两颗胶水位（P-58 起＋「没拉全」那颗）
        var keys = KeyedCallSites(code).Select(s => s.Key)
            .Where(k => k.StartsWith("sync.src.", StringComparison.Ordinal)).ToList();
        Assert.Equal(11, keys.Distinct(StringComparer.Ordinal).Count());   // 一颗键只说一句事实
    }

    /// <summary>⑨句厂里⛔ 提前把套着的那句念完。这颗与①<b>分家是分意的</b>：①数"出口交什么型"，
    /// 这一颗数"什么时候取词"——红了才知道是哪一层（台架 M1／M2 两臂各点这一颗与④）。
    /// <para>正件在同一格里：那两槽交出去的必须还是<b>两颗对象</b>，⛔ 一把只数 0 的尺。</para></summary>
    [Fact]
    public void TheFactoryNeverWordsTheNestedReasonEarly()
    {
        var code = Code(ReadRepoFile(Coord));
        Assert.Equal(0, Count(code, "Error?.Render()"));                                  // 原因先在 Core 念完＝P-212 本体
        Assert.Equal(0, Count(code, "SourceNames.NameOf(s.SourceId).Render()"));           // 源名那一槽同理（✅WI-57／P-187）
        Assert.Equal(1, Count(code, "SourceNames.NameOf(s.SourceId), reason(s)"));         // 正件：两颗都还是对象
    }

    /// <summary>②15 颗句：表里各一颗、代码里各一处主人（⛔ 第二处），而<b>槽数＝实参数</b>。</summary>
    [Fact]
    public void TheFifteenSentencesLiveInTheTableEachWithOneOwner()
    {
        foreach (var key in NewKeys)
        {
            Assert.Equal(1, Count(ReadRepoFile(Table), "new Entry(\"" + key + "\""));
            Assert.Equal(1, AsksInCode(key));
        }
        foreach (var (key, args, _) in KeyedCallSites(Code(ReadRepoFile(Coord))))
            Assert.Equal(args, RowSlots(key));   // 递错颗数不崩，只是屏幕上凭空少一个数
    }

    /// <summary>③逐源一行，每行既有「谁」也有「为什么」——只报颗数的那半截（P-212 的后半）⛔ 回来。</summary>
    [Fact]
    public void TheReasonLinesSayWhoAndWhy()
    {
        Locale.TrySet(AppLanguage.Chinese);
        var block = Summary(
            Failed(ItemSources.Chrome, "sync.src.timeout"),
            Failed(ItemSources.Edge, "sync.src.rateLimit"),
            new SourceSyncResult { SourceId = ItemSources.GitHub, Success = true, PulledCount = 42 })
            .FailureLines()!.Render();

        Assert.Equal(2, block.Split('\n').Length);            // 几颗失败几行：成功的源⛔ 混进来
        Assert.Contains("Chrome 书签", block, StringComparison.Ordinal);
        Assert.Contains("拉取超时或被取消（未中断其余源）", block, StringComparison.Ordinal);
        Assert.Contains("Edge 书签", block, StringComparison.Ordinal);
        Assert.DoesNotContain("GitHub Stars", block, StringComparison.Ordinal);
    }

    /// <summary>④那颗<b>套着</b>的整块读数：同一个 <see cref="LocMessage"/> 对象，三语各念自己那一句。
    /// <para>这一格是 P-212 本体的反面证据——①②③全绿而它红＝键交了、句子却在 Core 里已经念完，
    /// 换语言那一刻屏幕上只剩外层跟着翻，里面那几行停在上一门话。</para>
    /// <para>⚠ 写法上的讲究：那颗对象<b>只造一次</b>（在中文那一刻），之后换语言去重贴同一颗——这才是界面上的顺序
    /// （<c>LocText.SetMessage</c> 把"键＋实参"记进注册项，换语言只重贴<b>手里那一颗</b>）。
    /// 每种语言各调一次 <c>FailureLines()</c> 的写法<b>看着在测同一件事，其实永远绿</b>：
    /// 句子是在当时的语言下现造的，"提前念完"那种毛病当场量不出来（台架 M1／M2 两臂靠的就是这一条）。</para></summary>
    [Fact]
    public void TheWholeBlockIsWordedAtPasteTime()
    {
        var block = Summary(
            Failed(ItemSources.Chrome, "sync.src.timeout"),
            Failed(ItemSources.GitHub, "sync.src.auth", SourceSyncErrorKind.Auth));

        Locale.TrySet(AppLanguage.Chinese);
        var held = block.FailureLines();            // 造出来那一刻＝中文；之后⛔ 有人重新造
        Assert.NotNull(held);

        Locale.TrySet(AppLanguage.English);
        var en = held!.Render();
        Locale.TrySet(AppLanguage.Japanese);
        var ja = held.Render();
        Locale.TrySet(AppLanguage.Chinese);
        var zh = held.Render();

        Assert.Contains("Chrome bookmarks", en, StringComparison.Ordinal);   // 源名也重新念了
        Assert.Contains("timed out", en, StringComparison.Ordinal);
        Assert.Contains("Credentials are invalid", en, StringComparison.Ordinal);
        Assert.Contains("Chrome ブックマーク", ja, StringComparison.Ordinal);
        Assert.Contains("タイムアウト", ja, StringComparison.Ordinal);
        Assert.Contains("認証情報", ja, StringComparison.Ordinal);
        Assert.Contains("Chrome 书签", zh, StringComparison.Ordinal);
        Assert.Contains("凭据无效或已过期", zh, StringComparison.Ordinal);
        Assert.True(en != ja && ja != zh && en != zh);
    }

    /// <summary>⑤颗数对账：几颗失败就有几行，一颗都没失败时<b>没有那半张卡</b>（⛔ 空句顶在完成消息下面）。
    /// <para>顺带钉住一条没被改动的语义：源"不可用"记的是 <c>Success = true</c>，它进日志而⛔ 混进失败名单。</para></summary>
    [Fact]
    public void TheLineCountIsTheFailedSourceCount()
    {
        Assert.Null(Summary().FailureLines());
        Assert.Null(Summary(new SourceSyncResult { SourceId = ItemSources.Ditto, Success = true, PulledCount = 3 })
            .FailureLines());

        var skipped = Summary(new SourceSyncResult
        {
            SourceId = "nope", Success = true, PulledCount = 0, Error = LocMessage.Of("sync.src.unavailable"),
        });
        Assert.Null(skipped.FailureLines());
        Assert.Equal(3, Summary(Failed("a", "sync.src.other"), Failed("b", "sync.src.server"),
            Failed("c", "sync.src.rateLimit")).FailureLines()!.Render().Split('\n').Length);
    }

    /// <summary>⑥那张卡改脸的那一刀住在界面上：只有"有源没成"那一支才换 Warning＋新标题，
    /// 而 Success 那一支仍只有一处（⛔ 把"没做完"念成"已完成"）。行分隔那颗<b>布局</b>由默认实参递进去，
    /// ⛔ 进表（规则 #100）。</summary>
    [Fact]
    public void TheCardChangesItsFaceOnlyWhenSomethingFailed()
    {
        var code = Code(ReadRepoFile(Status));
        Assert.Equal(1, Count(code, "summary.FailedCount == 0"));
        Assert.Equal(1, Count(code, "ShowInfoBar(InfoBarSeverity.Success, LocMessage.Of(\"top.syncDoneTitle\")"));
        Assert.Equal(1, Count(code, "LocMessage.Of(\"top.syncPartialTitle\")"));
        Assert.Equal(1, Count(code, "summary.FailureLines()"));
        Assert.Equal(0, Count(code, "summary.FailureLines(\""));    // 换行那颗留在 Core 的默认实参里，一处就够
    }

    /// <summary>⑦那颗 enum 终于有了读者，而它注释里那句<b>过期承诺改口</b>：
    /// 从前写"供设置页/同步状态给出可操作文案"而全仓零读者（坑表 #588）。
    /// <para>禁项与正件钉在同一次比较里：那句旧承诺⛔ 再出现，<c>Auth</c> 那一档在界面上被读<b>恰一颗</b>——
    /// 它换出来的就是那句「去常规页换 Token」的按钮（P-54：程序能做的动作⛔ 写成用户作业）。</para></summary>
    [Fact]
    public void TheErrorKindFinallyHasAReaderAndItsPromiseIsRewritten()
    {
        var raw = ReadRepoFile(Coord);
        Assert.Equal(0, Count(raw, "供设置页/同步状态给出可操作文案"));      // 那句死承诺⛔ 留在档里
        Assert.Equal(1, Count(raw, "「去常规页换 Token」的按钮"));            // 正件：注释点名的是真读者
        Assert.Equal(1, Count(raw, "SourceSyncErrorKind.Auth"));      // Core 自己那一档映射
        var code = Code(ReadRepoFile(Status));
        Assert.Equal(1, Count(code, "s.ErrorKind == SourceSyncErrorKind.Auth"));
        Assert.Equal(1, Count(code, "OpenSettingsPage(SettingsTabIds.General)"));
        Assert.Equal(1, Count(code, "LocMessage.Of(\"top.syncFixToken\")"));
    }

    /// <summary>⑧十三句有词的话三语互异，两颗胶水位则是<b>点名的同形</b>：它们的形状逐颗钉死，
    /// 换语言时变的只有里面那两颗读数。</summary>
    [Fact]
    public void TheThirteenWordsDifferPerLanguageAndTheTwoSkeletonsArePinned()
    {
        foreach (var key in NewKeys.Where(k => !Skeletons.Contains(k, StringComparer.Ordinal)))
        {
            Assert.True(UiStrings.TryGet(key, out var row), $"表里没有这颗键：{key}");
            Assert.True(row.Zh != row.Ja && row.En != row.Ja && row.Zh != row.En, $"{key} 三列里有两列撞在一起");
        }
        // 认不出档的那两档才是"把原文当读数递进来"的出口：各一颗槽，多一颗就是有人开始拼句子。
        Assert.Equal(1, RowSlots("sync.src.githubOther"));
        Assert.Equal(1, RowSlots("sync.src.other"));
        Assert.Equal(2, Skeletons.Select(RowSlots).Distinct().Count());       // 一颗 2 槽、一颗 3 槽
        Assert.True(UiStrings.TryGet("sync.src.line", out var line));
        Assert.Equal("{0}：{1}", line.Zh);
        Assert.Equal("{0}: {1}", line.En);
        Assert.True(UiStrings.TryGet("sync.src.lineJoin", out var join));
        Assert.Equal("{0}{2}{1}", join.Zh);
    }
}
