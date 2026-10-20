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
/// ⛔ 动代码⛔ 动中文／日文列⛔ 增删键。剩下那 4 行（<c>rss.gapDaysPlus</c>／<c>rss.statusCacheDue</c>／
/// <c>rss.statusCacheFresh</c>／<c>rss.fetchingDue</c>）的复数名词是<b>时间单位</b>，label 形救不了
/// ⇒它们要和整表那 91 颗一起裁（三条出路的代价记在账本 P-217）。</para>
/// <para>⚠ 那颗被钉住的<b>候选数</b>是存量冻结用的尺：形状谓词只认「槽紧跟那颗词以 s 结尾」，
/// 所以 <c>{0} opens</c> 这类<b>动词</b>也计入候选⇒候选数⛔ 等于缺陷数。缺陷数由中文列那一把尺定，
/// 两个数都写进报告，⛔ 互相冒充（名册 443／444）。</para>
/// </summary>
public class RssPluralGateTests
{
    /// <summary>那颗形状：槽后面<b>紧跟</b>一颗至少三颗字母、以 <c>s</c> 收尾的英文词（"1 feeds"／"1 days" 那一形）。</summary>
    private static readonly Regex Tight = new(@"\{\d\}\s*[A-Za-z]{3,}s\b", RegexOptions.Compiled);

    /// <summary>本批把英文列改成 label 形的那 10 行（逐颗点名＝谁也不许悄悄把它改回名词在后的写法）。</summary>
    private static readonly string[] Reworded =
    {
        "rss.src.cacheLine", "rss.src.fetched", "rss.src.grew", "rss.fetchingAll",
        "rss.summaryNone", "rss.summaryAuto", "rss.summaryManual", "rss.summaryFailed",
        "rss.summaryStopped", "rss.rowTip",
    };

    /// <summary>还留在候选里的 rss.* 那 4 行：时间单位那一族，等整表那一批一起裁（⛔ 把它们也算成"已修"）。</summary>
    private static readonly string[] RssResidual =
    {
        "rss.gapDaysPlus", "rss.statusCacheDue", "rss.statusCacheFresh", "rss.fetchingDue",
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
        const int FrozenCandidates = 100;   // 本格自己现读（2026-10-20）｜另一把尺＝bin/WI92/census_48b.py 现读 98，差的 2 颗正是它剔掉的 opens／press
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
}
