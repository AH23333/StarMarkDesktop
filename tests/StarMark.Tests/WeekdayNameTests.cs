#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「今天星期几」这句中文怎么印——判据与三个真读者（批次 SF，P-128）。
/// <para>
/// 判据住在 <c>Abstractions</c>（<see cref="DateTimeText"/>），所以这里出的是<b>真行为测</b>而不是形状闸门
/// （#184 说反方向的话：UI 层那颗只出得了闸门）。这批把六处抄本并成一颗：
/// 词根（<c>日／一／…／六</c>）唯一，全称＝"星期"＋词根，短名＝"周"＋词根，月历表头＝词根本身。
/// </para>
/// <para>
/// 为什么要并：并之前<b>每个读数各印各的表</b>，改一个字要同时记起六处；而其中两处在 <c>StarMark.UI</c>，
/// 测试工程读不到（RY 当时因此写下"全称只有一颗"，那句是错的）。现在漂移只剩一条路——
/// 有人新写一张表，那由 <see cref="WeekdayNameGateTests"/> 拦。
/// </para>
/// </summary>
public sealed class WeekdayNameTests
{
    // 2026-09-20 是周日、09-21 周一 … 09-26 周六：一个整周，七天各来一次。

    // ────────── ① 判据本身：词根是唯一一张表 ──────────

    [Theory]
    [InlineData(DayOfWeek.Sunday, "日")]
    [InlineData(DayOfWeek.Monday, "一")]
    [InlineData(DayOfWeek.Tuesday, "二")]
    [InlineData(DayOfWeek.Wednesday, "三")]
    [InlineData(DayOfWeek.Thursday, "四")]
    [InlineData(DayOfWeek.Friday, "五")]
    [InlineData(DayOfWeek.Saturday, "六")]
    public void WeekdayStem_PinsAllSeven(DayOfWeek day, string expected)
        => Assert.Equal(expected, DateTimeText.WeekdayStem(day));

    [Theory]
    [InlineData(DayOfWeek.Sunday, "周日")]
    [InlineData(DayOfWeek.Monday, "周一")]
    [InlineData(DayOfWeek.Tuesday, "周二")]
    [InlineData(DayOfWeek.Wednesday, "周三")]
    [InlineData(DayOfWeek.Thursday, "周四")]
    [InlineData(DayOfWeek.Friday, "周五")]
    [InlineData(DayOfWeek.Saturday, "周六")]
    public void WeekdayShort_PinsAllSeven(DayOfWeek day, string expected)
        => Assert.Equal(expected, DateTimeText.WeekdayShort(day));

    /// <summary>
    /// 全称与短名<b>不许对"哪天"有分歧</b>：去掉各自前缀之后必须是同一个字。
    /// 并表之前这恰恰是可能出事的形状——两张表各转一格，界面上"时钟写星期二、待办写周三"没人报障。
    /// </summary>
    [Fact]
    public void FullAndShortNeverDisagreeOnTheDayCharacter()
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
            Assert.Equal(DateTimeText.WeekdayShort(day)[1..], DateTimeText.Weekday(day)["星期".Length..]);
    }

    /// <summary>
    /// 位序陷阱：按"周一是一周第一天"的直觉排那张表 ⇒ 七格整体错一天，而界面上完全看不出（只有周三对得上）。
    /// 钉法取<b>不变量</b>而不是写死 7：<c>DayOfWeek</c> 有几个值就要有几个互不相同的词根。
    /// </summary>
    [Fact]
    public void StemsAreSevenDistinctValuesOrderedByDayOfWeek()
    {
        var days = Enum.GetValues<DayOfWeek>();
        var stems = days.Select(DateTimeText.WeekdayStem).ToList();
        Assert.Equal(days.Length, stems.Distinct().Count());
        Assert.Equal("日", DateTimeText.WeekdayStem(DayOfWeek.Sunday));   // 序号 0，不是 6
        Assert.Equal("一", DateTimeText.WeekdayStem(DayOfWeek.Monday));   // 序号 1，不是 0
    }

    [Theory]
    [InlineData((DayOfWeek)(-1))]
    [InlineData((DayOfWeek)7)]
    [InlineData((DayOfWeek)99)]
    public void NeitherFormEverGoesEmptyForOutOfRange(DayOfWeek impossible)
    {
        // 空串在界面上＝那一格凭空消失，比错一天更难报障（并表前三处里两处就是这么写的）。
        Assert.Equal("周日", DateTimeText.WeekdayShort(impossible));
        Assert.Equal("星期日", DateTimeText.Weekday(impossible));
    }

    // ────────── ② 三个真读者：拿的都是同一颗判据 ──────────

    [Theory]
    [InlineData(AlarmDays.Monday)]
    [InlineData(AlarmDays.Tuesday)]
    [InlineData(AlarmDays.Wednesday)]
    [InlineData(AlarmDays.Thursday)]
    [InlineData(AlarmDays.Friday)]
    [InlineData(AlarmDays.Saturday)]
    [InlineData(AlarmDays.Sunday)]
    public void DaysLabel_ForASingleDay_IsExactlyTheJudgesShortName(AlarmDays oneDay)
    {
        var day = Enum.GetValues<DayOfWeek>().First(d => AlarmPolicy.Matches(oneDay, d));
        Assert.Equal(DateTimeText.WeekdayShort(day), AlarmPolicy.DaysLabel(oneDay));
    }

    /// <summary>多天组合：菜单标签逐格等于判据短名按"周一在前"拼起来——次序是 AlarmPolicy 自己的习惯，字不是。</summary>
    [Fact]
    public void DaysLabel_ForACombination_ListsMondayFirstUsingTheJudgeForTheWords()
    {
        Assert.Equal("周一 周三 周六",
            AlarmPolicy.DaysLabel(AlarmDays.Monday | AlarmDays.Wednesday | AlarmDays.Saturday));
    }

    /// <summary>
    /// 待办那一格（"本周内"）与判据同字。<see cref="LocalItemStateTests"/> 已把七格逐字钉死，
    /// 这里补的是<b>漂移</b>那一半：那颗判据哪天改了词根，这一格必须跟着改，不许留在旧写法上。
    /// 逐天挪 <c>now</c> 而不是只挪 <c>due</c>：days=3 恒落在"本周内"那条分支里，七个绝对星期就都走遍了。
    /// </summary>
    [Theory]
    [InlineData(2026, 9, 14)]   // 周一 → due 周四
    [InlineData(2026, 9, 15)]
    [InlineData(2026, 9, 16)]
    [InlineData(2026, 9, 17)]
    [InlineData(2026, 9, 18)]
    [InlineData(2026, 9, 19)]
    [InlineData(2026, 9, 20)]   // 周日 → due 周三
    public void DescribeDue_WithinAWeek_ReadsTheSameWordAsTheJudge(int nowY, int nowM, int nowD)
    {
        var now = LocalNoon(nowY, nowM, nowD);
        var due = now.Date.AddDays(3);
        Assert.Equal(
            DateTimeText.WeekdayShort(due.DayOfWeek),
            LocalItemState.DescribeDue(LocalItemState.DayStartUnix(due), now));
    }

    private static DateTimeOffset LocalNoon(int y, int m, int d)
        => new(new DateTime(y, m, d, 12, 0, 0, DateTimeKind.Local));

    // ────────── ③ 月历：表头那一格与格子本身必须对得上 ──────────

    [Fact]
    public void MonthGridHeaders_AreTheJudgesStemsInDayOfWeekOrder()
    {
        Assert.Equal(
            Enum.GetValues<DayOfWeek>().Select(DateTimeText.WeekdayStem),
            MonthGrid.WeekHeaders);
    }

    /// <summary>
    /// 这批真正防的那件事：<b>表头按周日起排、格子按 (int)DayOfWeek 落位，两处一旦不同序就行行错位</b>
    /// （症状是"1 号写在周二那列底下"）。并表之前表头与日期名分居两处，这种错只能靠人眼在真机上发现。
    /// </summary>
    [Theory]
    [InlineData(2026, 2)]    // 春节那个月：2/17 是周二，月初补格最多
    [InlineData(2026, 9)]
    [InlineData(2026, 12)]
    [InlineData(2027, 1)]
    public void EveryCellSitsUnderTheHeaderForItsOwnWeekday(int year, int month)
    {
        var rows = MonthGrid.Build(year, month, new DateOnly(year, month, 1));
        var start = MonthGrid.FirstCell(year, month);
        Assert.NotEmpty(rows);
        for (var r = 0; r < rows.Count; r++)
        {
            Assert.Equal(MonthGrid.Columns, rows[r].Count);
            for (var c = 0; c < rows[r].Count; c++)
            {
                var date = start.AddDays(r * MonthGrid.Columns + c);
                Assert.Equal(MonthGrid.WeekHeaders[c], DateTimeText.WeekdayStem(date.DayOfWeek));
            }
        }
    }

    /// <summary>
    /// 表头列与格子落位必须对得上，用<b>独立事实</b>钉：2026-09-01 是周二 ⇒ 首行开头补两格，
    /// 而那一天的格子要落在写着"二"的那一列底下。<see cref="FirstSlot"/> 与 <see cref="MonthGrid.WeekHeaders"/>
    /// 共用同一个序号，这条红的时候就是"1 号写在别的星期底下"那种肉眼错位。
    /// </summary>
    [Fact]
    public void FirstSlot_IsTheSundayFirstIndexTheHeadersAssume()
    {
        Assert.Equal(2, MonthGrid.FirstSlot(2026, 9));
        Assert.Equal("二", MonthGrid.WeekHeaders[2]);
    }
}
