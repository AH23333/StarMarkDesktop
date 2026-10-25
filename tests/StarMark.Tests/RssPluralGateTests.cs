#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using StarMark.Abstractions.I18n;

namespace StarMark.Tests;

/// <summary>
/// <b>英文列那句「数量紧跟复数名词」的形状闸门</b>（P-217）。
/// <para>片9 在英文界面上量到过原话：<c>StatusTextText</c> 整颗＝「… 1 feeds failed — …」，而那一列
/// <b>确实只有 1 颗源</b>。中文／日文没有单复数变形⇒这一族只在英文列成问题；而它⛔ 是某一颗键的毛病：
/// 只读码数过一次＝全表候选 108 颗槽、其中<b>计数槽 91 颗</b>（<c>bin/WI92/census_48b.py</c> 正本，
/// 那颗槽是数字还是名字由<b>中文列紧跟量词与否</b>定，⛔ 靠猜调用点实参形状）。</para>
/// <para>本批收的是<b>片9 屏幕上确证的那一路</b>：10 行英文列改成与数量无关的摆法（名词摆到数字前面），
/// ⛔ 动代码⛔ 动中文／日文列⛔ 增删键。剩下那 4 行当时记作「复数名词是<b>时间单位</b>、label 形救不了」——
/// <b>那句到片109 为止为真，现读已改口</b>：那颗形状尺对这 4 行的命中逐颗是 <c>{0} feeds</c>×3 与
/// <c>over {0} days</c>×1，前三行（<c>rss.statusCacheDue</c>／<c>rss.statusCacheFresh</c>／<c>rss.fetchingDue</c>）
/// 与上面那 10 行<b>同族</b>＝片109 一起收了；只有 <c>rss.gapDaysPlus</c> 才是时间单位，而
/// <c>RssFeedCache.AutoRefreshGap</c> 现读＝一颗 24 小时常数 ⇒ 它那句「1 days」在当前常数下<b>结构性不可达</b>。</para>
/// <para>片109 另收一整维＝<b>槽紧跟 be/have 动词</b>（<c>{2} are overdue</c>／<c>{1} were written back</c>）：
/// 那颗 <see cref="Tight"/> 只认「以 s 结尾的词」，对这一维<b>一颗也量不到</b>＝⛔ 让它冒充已覆盖，
/// 由 <see cref="BeVerb"/> 配正件与对照件。</para>
/// <para>⚠ 那颗被钉住的<b>候选数</b>是存量冻结用的尺：形状谓词只认「槽紧跟那颗词以 s 结尾」，
/// 所以 <c>{0} opens</c> 这类<b>动词</b>也计入候选⇒候选数⛔ 等于缺陷数。缺陷数由中文列那一把尺定，
/// 两个数都写进报告，⛔ 互相冒充（名册 443／444）。</para>
/// </summary>
public class RssPluralGateTests
{
    /// <summary>那颗形状：槽后面<b>紧跟</b>一颗至少三颗字母、以 <c>s</c> 收尾的英文词（"1 feeds"／"1 days" 那一形）。</summary>
    private static readonly Regex Tight = new(@"\{\d\}\s*[A-Za-z]{3,}s\b", RegexOptions.Compiled);

    /// <summary>片48 把英文列改成 label 形的那 10 行（逐颗点名＝谁也不许悄悄把它改回名词在后的写法）。</summary>
    private static readonly string[] Reworded =
    {
        "rss.src.cacheLine", "rss.src.fetched", "rss.src.grew", "rss.fetchingAll",
        "rss.summaryNone", "rss.summaryAuto", "rss.summaryManual", "rss.summaryFailed",
        "rss.summaryStopped", "rss.rowTip",
    };

    /// <summary>还留在候选里的 rss.* 那<b>一颗</b>：只有它是时间单位（片109 现读改口前三行是 <c>feeds</c>・已随本批收掉）。
    /// <para>⚠ 名单变短＝<b>正件</b>而⛔ 放宽：那颗 <see cref="Tight"/> 在这一族里少咬三颗，
    /// 与整表那颗候选数（<see cref="TheWholeTableHoldsNoNewCountAdjacentPlurals"/>）同向，两笔账一起改。</para></summary>
    private static readonly string[] RssResidual =
    {
        "rss.gapDaysPlus",
    };

    /// <summary>片109 把英文列改成 label 形的那 6 行（其中三行同时收掉 be/have 那一维・逐颗点名⛔ 悄悄改回去）。</summary>
    private static readonly string[] Reworded109 =
    {
        "rss.statusCacheDue", "rss.statusCacheFresh", "rss.fetchingDue",
        "settings.ai.confirm.detailRuled", "settings.data.backup.clipCarried", "settings.data.cleanPartlyBlocked",
    };

    /// <summary>那颗第二维的形状：槽后面紧跟一颗 be/have（"1 are overdue"／"1 were written back" 那一形）。</summary>
    private static readonly Regex BeVerb = new(@"\{\d\}\s*,?\s+(are|is|was|were|has|have)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>片109 收掉 be/have 那一维的那 4 行（<see cref="Tight"/> 对它们一颗也量不到＝第二把眼睛的射程）。</summary>
    private static readonly string[] BeReworded =
    {
        "rss.statusCacheDue", "settings.ai.confirm.detailRuled",
        "settings.data.backup.clipCarried", "settings.data.cleanPartlyBlocked",
    };

    /// <summary>
    /// <b>改完那 10 行必须读得出「一颗」也读得出「多颗</b>：同一份英文列正文在 <c>{0} = 1</c> 与
    /// <c>{0} = 7</c> 两档下都⛔ 出现那颗形状。另钉一句「那颗数还⛔ 被删掉」——
    /// 把数字整个删了也能让第一半变绿，而那就不是修语法而是把读数藏了。
    /// </summary>
    [Theory]
    [MemberData(nameof(RewordedKeys))]
    public void TheRewordedRowsReadTheSameForOneAndForMany(string key)
    {
        var entry = Assert.Single(UiStrings.All, e => e.Key == key);
        Assert.Matches(new Regex(@"\{\d\}"), entry.En);
        Assert.DoesNotMatch(Tight, entry.En);
    }

    public static TheoryData<string> RewordedKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in Reworded) data.Add(key);
        foreach (var key in Reworded109) data.Add(key);   // 两批各留一份名单＝谁被收掉还看得到出处（⛔ 合成一颗无主名单）
        return data;
    }

    /// <summary>
    /// <b>存量那颗数⛔ 许悄悄长大，也⛔ 许悄悄改写</b>：全表英文列里那颗形状的候选数钉成现读值。
    /// 变多＝新写的英文句又犯了同一件事；变少＝有人真修掉了一颗，那就把钉子改小并在账本记一笔。
    /// <para>这颗钉子是<b>双向</b>的（⛔ 只写「⛔ 许超过」＝把"少了几颗"也一并放过，名册 427）。</para>
    /// </summary>
    [Fact]
    public void TheWholeTableHoldsNoNewCountAdjacentPlurals()
    {
        const int FrozenCandidates = 94;    // 本格自己现读（2026-10-25・片109）｜上一钉＝100（片48 那次现读）
                                            // 那颗数<b>变小</b>＝本批自己收掉的 6 颗：改前那一次红把两头的数都写下来了
                                            // （正本 bin/WI99/p109_r1/gate109_r1.txt・Expected 100 / Actual 94・只这一格红）
                                            // 另一把尺＝bin/WI92/census_48b.py（那颗剔掉 opens／press 这类动词＝本格比它宽两颗・两笔数都写进报告）
        var hits = UiStrings.All.Sum(e => Tight.Matches(e.En).Count);
        Assert.True(hits > 0, "这把尺一颗都没认到＝它聋了（名册 485），⛔ 把 0 当成一张干净的表");
        Assert.Equal(FrozenCandidates, hits);
    }

    /// <summary>
    /// <b>那一族的 rss.* 残余只剩点名的 4 行</b>：别的 rss 行⛔ 许再有那颗形状（本批那 10 行归零），
    /// 而这 4 行⛔ 许被悄悄删出名单（名单＝两颗尺对账，⛔ 只数总数）。
    /// </summary>
    [Fact]
    public void TheOnlyRssRowsLeftInTheResidualAreTheTimeUnits()
    {
        var left = UiStrings.All
            .Where(e => e.Key.StartsWith("rss.") && Tight.Matches(e.En).Count > 0)
            .Select(e => e.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(RssResidual.OrderBy(k => k, StringComparer.Ordinal), left);
    }

    /// <summary>
    /// <b>这把尺认不认得自己</b>：片9 屏幕上那颗原话必须被认到；本批改完的写法必须⛔ 被认到；
    /// 而那颗<b>动词</b>（<c>{0} opens</c>）也<b>照样被认到</b>——这一颗是刻意钉的：
    /// 它说明候选数里混着⛔ 缺陷的形状，谁也不许拿那颗总数当缺陷数用。
    /// </summary>
    [Fact]
    public void TheShapeRulerRecognisesTheDefectTheLabelAndTheVerb()
    {
        Assert.Single(Tight.Matches("  {0} feeds failed — the reason is written on their row"));
        Assert.Empty(Tight.Matches("  Failed feeds: {0} — the reason is written on their row"));
        Assert.Single(Tight.Matches("On — {0} opens the canvas."));
    }

    /// <summary>
    /// <b>第二把眼睛的正件</b>：片109 收掉 be/have 那一维的那 4 行，改完在两档读数（一颗／多颗）下
    /// 都⛔ 出现那颗形状，而那颗数与那颗槽还<u>在</u>（⛔ 拿"把读数藏了"当"把语法修了"）。
    /// <para>⚠ 这一维<b>另立一颗格</b>而⛔ 并进上面那颗 Theory＝两颗尺量的是两种形状，
    /// 合成一颗断言会让"只改名词不改动词"那一副坏脸从两格里同时溜走（台架 A2 臂专打这一维）。</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(BeRewordedKeys))]
    public void TheRewordedVerbRowsReadTheSameForOneAndForMany(string key)
    {
        var entry = Assert.Single(UiStrings.All, e => e.Key == key);
        Assert.Matches(new Regex(@"\{\d\}"), entry.En);
        Assert.Empty(BeVerb.Matches(entry.En));
        Assert.Empty(Tight.Matches(entry.En));
    }

    public static TheoryData<string> BeRewordedKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in BeReworded) data.Add(key);
        return data;
    }

    /// <summary>
    /// <b>那颗新尺自己认得自己</b>（与 <see cref="TheShapeRulerRecognisesTheDefectTheLabelAndTheVerb"/> 同一条规矩）：
    /// 原话要被认到・本批改完的写法⛔ 被认到・整表那颗候选数钉成现读值（双向・变多＝新写的英文句又犯了，
    /// 变少＝有人真修掉一颗，要连同这颗钉子与账本 P-217 那笔账一起改）。
    /// <para>那颗<b>假阳性对照件</b>照旧钉着：全表这一维的候选数<b>包含</b> <c>pin.card.limitBody</c>
    /// （<c>{1} is the limit</c>＝动词后那颗名词是定冠词单数、⛔ 随槽变）＝候选数⛔ 等于缺陷数（名册 443／444）。</para>
    /// </summary>
    [Fact]
    public void TheBeVerbRulerIsNotDeafAndCountsTheWholeTable()
    {
        const int FrozenBeVerbCandidates = 14;   // 本格自己现读（2026-10-25・片109 收掉 4 颗之后）
        Assert.Single(BeVerb.Matches("{0} feeds, {1} enabled; {2} are overdue by more than {3}."));
        Assert.Empty(BeVerb.Matches("Feeds: {0}, enabled: {1}. Overdue by more than {3}: {2} — re-fetched automatically."));
        var hits = UiStrings.All.Sum(e => BeVerb.Matches(e.En).Count);
        Assert.True(hits > 0, "这把尺一颗都没认到＝它聋了（名册 485），⛔ 把 0 当成一张干净的表");
        Assert.Equal(FrozenBeVerbCandidates, hits);
        Assert.Single(BeVerb.Matches(UiStrings.All.Single(e => e.Key == "pin.card.limitBody").En));
    }
}
