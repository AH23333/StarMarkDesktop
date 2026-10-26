#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「把几颗话连成一句」这一族只许有<b>一位主人</b>（批次 片116／P-196）。
/// <para><b>为什么会开这一条</b>：账本 P-196 数到那颗 <c>{0}；{1}</c> 形状的"按语言选连接符"的键有三位主人，
/// 而三位的实现<b>各写了一遍同一件事</b>——<c>Aggregate((head, next) =&gt; Of(key, head, next))</c>（热榜两路＋速览那一行）
/// 与 <c>acc is null ? part : Of(key, acc, part)</c>（AI 那一栏的私有 <c>Joined</c>）是同一个左折的两种写法，
/// 加上 <c>Combine</c>／<c>Join</c> 那两颗只为"缺的那半怎么办"存在的私有件。本批把<b>机制</b>收进
/// <c>LocMessage.Fold(skeletonKey, parts)</c> 与它的<b>主句打底</b>重载，
/// 而<b>每族句子用哪颗骨架</b>照旧各归各的键（那是一次口径裁决，账本把它留给 P-196 的下一笔）。</para>
/// <para><b>⛔ 并表里的键</b>：现读纯胶水行（≥2 槽、抹掉槽之后三列都不含字母数字与汉字）共 <b>39</b> 颗，
/// 其中"热榜那条用中文分号、组件那条用顿号、用量面板那颗按语言给三种"是<b>事实而不是分岔</b>；
/// 把它们并成一颗 <c>common.join</c> 要先定"一句里两件事有几种语气"＝账本明说⛔ 混进交表批次顺手做，本批⛔ 动表。</para>
/// <para><b>本颗类挂在串行集合 <c>LocaleGlobal</c> 上</b>：下面那一格会按进程级语言（<c>Locale.TrySet</c>），
/// 而本工程既没有 <c>xunit.runner.json</c> 也没有 assembly 级 <c>CollectionBehavior</c> ⇒ 不挂就是
/// "并发读到别人那一档语言"的假红（<c>I18nWiringGateTests.EveryLocaleMutatingTestClassIsInTheSerialCollection</c> 现读点名）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public class LocFoldGateTests
{
    private const string Owner = "src/StarMark.Abstractions/I18n/Locale.cs";
    private const string FetcherCs = "src/StarMark.Integrations/Trending/TrendingFetcher.cs";
    private const string ServiceCs = "src/StarMark.Core/Trending/TrendingService.cs";
    private const string MonitorVm = "src/StarMark.UI/ViewModels/SystemMonitorViewModel.cs";
    private const string GlanceCs = "src/StarMark.UI/Views/GlanceWidget.xaml.cs";
    private const string TrendingVm = "src/StarMark.UI/ViewModels/TrendingPageViewModel.cs";
    private const string RssJudge = "src/StarMark.Core/Feed/RssSourceStatus.cs";
    private const string RssVm = "src/StarMark.UI/ViewModels/RssPageViewModel.cs";
    private const string AiCs = "src/StarMark.UI/Views/SettingsPage.Ai.cs";
    private const string AiUsageCs = "src/StarMark.UI/Views/SettingsPage.AiUsage.cs";
    private const string BackupCs = "src/StarMark.UI/Views/SettingsPage.Backup.cs";

    /// <summary>用一颗真的两槽骨架当被测骨架（⛔ 自造键：表里没有的键会让闸门自己造出一个假形状）。</summary>
    private const string Join = "widget.mon.note.join";

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    private static LocMessage First() => LocMessage.Of("widget.mon.accruing");
    private static LocMessage Second() => LocMessage.Of("widget.mon.net.counterReset");
    private static LocMessage Third() => LocMessage.Of("widget.mon.net.virtualFallback", 2);

    /// <summary>
    /// <b>一颗都没有 ⇒ 交回 null，而不是交回一句空话</b>：这一族三个宿主各有各的兜底
    /// （热榜句厂退回主句、监控那一行不说、速览那一格空着），兜底归调用点而不归机制。
    /// <para>⛔ 让它交回 <c>LocMessage.Of(Join)</c> 那种"只有一颗槽的骨架"：屏幕上会是一句带空槽的话，
    /// 而 <c>{0}；{1}</c> 少递实参时 <c>string.Format</c> 会把槽留在原地（P-194 那一族的形状）。</para>
    /// </summary>
    [Fact]
    public void NothingToConnectIsNoSentenceRatherThanAnEmptyOne()
    {
        Assert.Null(LocMessage.Fold(Join, Array.Empty<LocMessage>()));
        Assert.Null(LocMessage.Fold(Join, new LocMessage?[] { null, null, null }));
    }

    /// <summary>
    /// <b>只有一颗 ⇒ 原样交回那一颗</b>（⛔ 给它套一层骨架）。
    /// 这一格钉的是"套一层"与"不套"看得见差别的那一维：套了的外层形状是 <c>Of(join, x)</c>＝槽少一颗，
    /// <c>Render()</c> 会把 <c>{1}</c> 原样留在屏幕上（台架规则：恒真要问"什么输入会让它响"——
    /// 这里把 <c>Args</c> 与那颗键一起钉，改坏任何一头的写法都红）。</summary>
    [Fact]
    public void ASinglePartIsHandedBackUnwrapped()
    {
        var folded = LocMessage.Fold(Join, new[] { First() });
        Assert.NotNull(folded);
        Assert.Equal("widget.mon.accruing", folded!.Key);
        Assert.Null(folded.Args);                       // ⛔ 多出来的那层骨架
        Assert.Equal(Locale.T("widget.mon.accruing"), folded.Render());
    }

    /// <summary>
    /// <b>三颗⇒左折 ((a＋b)＋c)</b>：语序是这一族的事实，所以它钉<b>形状</b>而⛔ 只钉渲染结果
    /// （右折 <c>(a＋(b＋c))</c> 在两颗槽同形的骨架上渲染出<b>同一句话</b>＝只比字符串的断言证不了语序）。
    /// </summary>
    [Fact]
    public void ThreePartsFoldLeftSoThePairingIsVisible()
    {
        var folded = LocMessage.Fold(Join, new[] { First(), Second(), Third() })!;
        Assert.Equal(Join, folded.Key);
        Assert.NotNull(folded.Args);
        Assert.Equal(2, folded.Args!.Length);

        var last = Assert.IsType<LocMessage>(folded.Args![1]);
        Assert.Equal("widget.mon.net.virtualFallback", last.Key);      // 末片＝第三颗，⛔ 排错顺序

        var inner = Assert.IsType<LocMessage>(folded.Args![0]);        // 前两颗先连成一颗
        Assert.Equal(Join, inner.Key);
        Assert.Equal("widget.mon.accruing", Assert.IsType<LocMessage>(inner.Args![0]).Key);
        Assert.Equal("widget.mon.net.counterReset", Assert.IsType<LocMessage>(inner.Args![1]).Key);
    }

    /// <summary>
    /// <b>缺的那几颗跳过而顺序不动</b>：<c>null, a, null, b, null</c> 与 <c>a, b</c> 折出同一形状。
    /// <para>这一格是热榜那一路的真需求（抓取那句与写缓存那句都可能缺席），也是本批替掉
    /// <c>notes.Where(n =&gt; n is not null).ToArray()</c> 那份预筛的凭据：跳过归主人，调用点⛔ 再筛一遍。</para>
    /// </summary>
    [Fact]
    public void MissingPartsAreSkippedWithoutShufflingTheRest()
    {
        var folded = LocMessage.Fold(Join, new LocMessage?[] { null, First(), null, Second(), null })!;
        Assert.Equal(Join, folded.Key);
        Assert.NotNull(folded.Args);
        Assert.Equal(2, folded.Args!.Length);            // 三颗 null⛔ 留下一颗空槽
        Assert.Equal("widget.mon.accruing", Assert.IsType<LocMessage>(folded.Args![0]).Key);
        Assert.Equal("widget.mon.net.counterReset", Assert.IsType<LocMessage>(folded.Args![1]).Key);

        // 与"缺席的那几颗压根没进过名单"折出的形状逐颗相等：跳过 null ⛔ 许顺手丢掉在场的那颗，也⛔ 多折一层。
        var straight = LocMessage.Fold(Join, new[] { First(), Second() })!;
        Assert.Equal(straight.Key, folded.Key);
        Assert.Equal(straight.Args!.Length, folded.Args!.Length);
    }

    /// <summary>
    /// <b>主句打底那一型：签名保证交回非 null</b>。
    /// 这一型存在的理由＝"缺的那半"与"一颗都没有"是两件事（表头那句、状态行主句都在场），
    /// 让调用点自己写 <c>?? head</c> 就是把同一条规则再抄一份（＝P-196 数的第三、第四位主人）。
    /// </summary>
    [Fact]
    public void TheHeadFloorOverloadAlwaysKeepsTheHeadAndPutsItLeftmost()
    {
        var headOnly = LocMessage.Fold(Join, First());                    // 后面一颗都没有
        Assert.Equal("widget.mon.accruing", headOnly.Key);
        Assert.Null(headOnly.Args);

        var allMissing = LocMessage.Fold(Join, First(), null, null);      // 全缺⇒还是主句
        Assert.Equal("widget.mon.accruing", allMissing.Key);
        Assert.Null(allMissing.Args);

        var withBoth = LocMessage.Fold(Join, First(), Second(), null);
        Assert.Equal(Join, withBoth.Key);
        Assert.Equal("widget.mon.accruing", Assert.IsType<LocMessage>(withBoth.Args![0]).Key);   // 主句在左
        Assert.Equal("widget.mon.net.counterReset", Assert.IsType<LocMessage>(withBoth.Args![1]).Key);

        // 与那一型"可能一颗都没有"的重载在同一串上落回同一形状（两型⛔ 各有一套语序）
        Assert.Equal(LocMessage.Fold(Join, new LocMessage?[] { First(), Second() })!.Render(), withBoth.Render());
    }

    /// <summary>
    /// <b>折好的那句仍然在"贴上去那一刻"才取词</b>——收机制⛔ 收掉取词时机（P-190／P-192 的原始缺陷形状）。
    /// <para>正件：折在中文档、念在英文档 ⇒ 连接符与两半各自的词<b>整条</b>跟着改口。
    /// 反证（如果有人把 Fold 实现成"当场 <c>Locale.Fmt</c> 拼成字符串"）：这一格会读到旧语言的连接符。</para>
    /// </summary>
    [Fact]
    public void FoldingKeepsTheMomentOfTakingWords()
    {
        var before = Locale.Current;
        try
        {
            Locale.TrySet(AppLanguage.Chinese);
            var zhHead = Locale.T("widget.mon.accruing");
            var folded = LocMessage.Fold(Join, First(), Second());
            var zh = folded.Render();
            Assert.Contains("；", zh, StringComparison.Ordinal);

            Locale.TrySet(AppLanguage.English);
            var en = folded.Render();                                     // 同一颗对象，换语言之后才念
            Assert.DoesNotContain("；", en, StringComparison.Ordinal);      // 连接符⛔ 留在中文那一档
            Assert.DoesNotContain(zhHead, en, StringComparison.Ordinal);    // 半句也⛔ 停在旧语言
            Assert.Contains(Locale.T("widget.mon.accruing"), en, StringComparison.Ordinal);
            Assert.DoesNotContain("{", en, StringComparison.Ordinal);       // 槽数对不上会在这里露出来（#393）
        }
        finally { Locale.TrySet(before); }
    }

    /// <summary>
    /// <b>折叠机制在 src 只许有一位主人</b>：三种手搓形状（LINQ 聚合折 / 累加器三元 / 键当形参递进 <c>Of</c>）
    /// 加上本批退休的私有件名字，逐档扫<b>抹掉注释之后</b>的源码（规则：注释里引用旧写法是为了讲清为什么，⛔ 算第二个出口）。
    /// <para>同一格先在<b>主人那一档</b>数到那两份循环＝证明这把尺⛔ 聋（读数 0 与"根本没扫到"必须分得开，名册 #483）。</para>
    /// </summary>
    [Fact]
    public void TheFoldMechanismHasExactlyOneOwnerInSrc()
    {
        var ownerCode = CodeOf(Owner);
        Assert.Equal(2, SourceGate.Count(ownerCode, "if (part is not null)"));     // 两型各一份循环＝尺认得这一族
        Assert.Equal(2, SourceGate.Count(ownerCode, "Of(skeletonKey, acc, part)"));

        var retired = new[]
        {
            "private static LocMessage? Joined(",   // AI 那一栏的私有折叠（两个调用点本批直接递键）
            "private static LocMessage Combine(",   // 热榜编排的私有二元套（唯一调用点本批就地交给主人）
            "LocMessage.Of(joinKey,",               // 键当形参递进 Of＝折叠的第二主人
            "acc = acc is null ? part : LocMessage.Of(",
        };
        var hits = new List<string>();
        foreach (var (rel, text) in SourceGate.ReadRepoUnder("src"))
        {
            if (rel.Replace('\\', '/').EndsWith("I18n/Locale.cs", StringComparison.Ordinal)) continue;
            var code = SourceGate.Code(text);
            hits.AddRange(retired.Where(needle => code.Contains(needle, StringComparison.Ordinal))
                .Select(needle => $"{rel} 里还有 {needle}"));
            // LINQ 折这一族：整行同时出现 `.Aggregate(` 与 `LocMessage` 才算（⛔ 禁掉一切 Aggregate——
            // 那是别的算术的正当工具，把禁令写宽只会教下一个人把闸门改松而不是把这一族改对）
            hits.AddRange(code.Split('\n')
                .Where(line => line.Contains(".Aggregate(", StringComparison.Ordinal)
                               && line.Contains("LocMessage", StringComparison.Ordinal))
                .Select(line => $"{rel} 里还有一行 Aggregate 折 LocMessage：{line.Trim()}"));
        }
        Assert.Empty(hits);
    }

    /// <summary>
    /// <b>正面名册：哪一宿主用哪颗骨架</b>，逐档数<b>恰几颗</b>。
    /// <para>只问"这一档里出现了那颗键"会怎样？——键还留在<b>另一处</b>（比如同一行的二元套）时它照绿，
    /// 于是搬走的那一半没人说（坑表 #1020 那颗"撞对"）。颗数正本＝本批 <c>bin/WI99/p116_r*/</c> 现读。</para>
    /// </summary>
    [Fact]
    public void EachHostFoldsItsOwnGlueKeyThroughTheOwner()
    {
        var roster = new (string Rel, string Needle, int Times)[]
        {
            (FetcherCs, "LocMessage.Fold(\"trending.notice.join\"", 2),
            (ServiceCs, "LocMessage.Fold(\"trending.notice.join\"", 1),
            (MonitorVm, "LocMessage.Fold(\"widget.mon.note.join\", notes)", 1),
            (GlanceCs, "LocMessage.Fold(\"trending.statusNotice\", notes)", 1),
            (TrendingVm, "LocMessage.Fold(\"trending.statusNotice\", head, r.Notice)", 1),
            (RssJudge, "LocMessage.Fold(\"rss.statusJoin\", head, tail)", 1),
            (RssVm, "LocMessage.Fold(\"rss.statusJoin\"", 2),
            (AiCs, "LocMessage.Fold(\"settings.ai.tagJoin\"", 1),
            (AiUsageCs, "LocMessage.Fold(\"settings.ai.usage.featureJoin\"", 1),
        };
        var total = 0;
        foreach (var (rel, needle, times) in roster)
        {
            var found = SourceGate.Count(CodeOf(rel), needle);
            Assert.True(found == times, $"{rel} 里「{needle}」现读 {found} 颗（应为 {times} 颗）");
            total += found;
        }
        Assert.Equal(11, total);        // 主人自己的两型⛔ 算进调用点（这一格数的是宿主递出去的颗数）

        // 折叠这一族交出去的实参颗数＝那颗骨架行声明的槽数（"两颗槽、每次递两颗"是同一件事的两面）。
        // 这一维从前由 <c>KeyedCallSites</c> 那把尺顺带管着，✅片116 把键搬进 Fold 之后就归这里钉（名册：搬家要带证人）。
        foreach (var key in new[]
                 {
                     "trending.notice.join", "widget.mon.note.join", "trending.statusNotice",
                     "rss.statusJoin", "settings.ai.tagJoin", "settings.ai.usage.featureJoin",
                 })
            Assert.Equal(2, SourceGate.RowSlots(key));
    }

    /// <summary>
    /// <b>那位主人管不到的那一型照旧留着，并且说清为什么</b>：<c>settings.data.backup.lineJoin</c> 是三槽行，
    /// 第三颗槽交的是<b>换行</b>那颗布局（规则 #100：表里⛔ 有控制字符，换行由实参递进来），
    /// 两颗槽的折叠签名收不进它 ⇒ 它留在 <c>SettingsPage.Backup.Stack</c>。
    /// <para>这一格的存在是为了让"收成一位主人"这句话⛔ 说过头：本批并没有让所有拼接都归一处（名册：措辞与实现对齐）。</para>
    /// </summary>
    [Fact]
    public void TheLayoutRowWithAGapArgumentIsKnownNotToBeFoldedByTheOwner()
    {
        var code = CodeOf(BackupCs);
        Assert.Equal(1, SourceGate.Count(code, "LocMessage.Of(\"settings.data.backup.lineJoin\", head, tail, gap)"));
        Assert.Equal(3, SourceGate.RowSlots("settings.data.backup.lineJoin"));   // {0}{2}{1}：第三颗槽交的是那颗换行
    }

    /// <summary>
    /// <b>本批⛔ 动语言表</b>：那三颗骨架键各自的槽数与"中日同形、en 半角"这两件事照旧（现读自表，⛔ 抄措辞）。
    /// <para>这一格钉的是"<b>机制</b>收成一位主人而<b>每族的标点</b>一颗没并"。如果哪天有人为了少写一处循环
    /// 而顺手把两颗标点并成一颗 <c>common.join</c>，动的就是这张表，而那是账本留给 P-196 下一笔的<b>口径裁决</b>
    /// （⛔ 交表批次顺手做＝#115／#126 那一族）。表里总行数那类硬常数住在这里只会多造一个要跟着动的地方（名册 971）⇒ 不钉。</para>
    /// </summary>
    [Fact]
    public void TheGlueKeysThemselvesAreNotMergedByThisBatch()
    {
        foreach (var key in new[] { "trending.notice.join", "widget.mon.note.join", "settings.ai.usage.featureJoin" })
            Assert.Equal(2, SourceGate.RowSlots(key));                   // 两槽骨架：并成一颗通用键就不是这一族了

        Assert.True(UiStrings.TryGet("trending.notice.join", out var noticeJoin));
        Assert.True(UiStrings.TryGet("widget.mon.note.join", out var monJoin));
        Assert.Equal(noticeJoin.Zh, noticeJoin.Ja);                      // 中日同形（同形豁免名单里在册）
        Assert.Equal(monJoin.Zh, monJoin.Ja);
        Assert.NotEqual(noticeJoin.Zh, noticeJoin.En);                   // en 那一列是半角：那颗宽度是这一列的事实
        Assert.True(UiStrings.TryGet("settings.ai.usage.featureJoin", out var featureJoin));
        Assert.NotEqual(featureJoin.En, monJoin.En);                     // 用量面板那颗给的是第三种（"; "）＝标点不同是事实而⛔ 分岔
    }
}
