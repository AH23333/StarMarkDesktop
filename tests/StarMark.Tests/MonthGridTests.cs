#nullable enable
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 月历网格测试：星期对齐、补齐整周、跨月白天数标记、今天只标一格、节日标记、翻月进位。
/// </summary>
public sealed class MonthGridTests
{
    private static readonly DateOnly AnyToday = new(2026, 9, 18);

    [Fact]
    public void FirstSlot_PlacesTheFirstDayUnderTheRightWeekday()
    {
        // 2026-09-01 是周二 ⇒ 首槽 2（周日=0），网格前两格属于 8 月
        Assert.Equal(2, MonthGrid.FirstSlot(2026, 9));
        Assert.Equal(new DateOnly(2026, 8, 30), MonthGrid.FirstCell(2026, 9));
    }

    [Fact]
    public void Build_PadsToWholeWeeksAndMarksForeignDays()
    {
        var rows = MonthGrid.Build(2026, 9, AnyToday);
        Assert.All(rows, row => Assert.Equal(MonthGrid.Columns, row.Count));
        Assert.InRange(rows.Count, 5, 6);

        var inMonth = rows.SelectMany(r => r).Count(c => c.InMonth);
        Assert.Equal(DateTime.DaysInMonth(2026, 9), inMonth);
        Assert.Equal(2, rows[0].Count(c => !c.InMonth));            // 9/1 是周二 ⇒ 首行开头补 8/30、8/31
        Assert.Equal(30, rows[^1].Where(c => c.InMonth).Last().Day);
    }

    [Fact]
    public void Build_TodayFlagAppearsExactlyOnce()
    {
        var cells = MonthGrid.Build(2026, 9, AnyToday).SelectMany(r => r).ToList();
        Assert.Single(cells, c => c.IsToday);
        Assert.Equal(18, cells.Single(c => c.IsToday).Day);

        // 看别的月份时，"今天"不该跟着跑进那一月
        Assert.DoesNotContain(MonthGrid.Build(2026, 10, AnyToday).SelectMany(r => r), c => c.IsToday);
    }

    [Theory]
    [InlineData(2026, 2, 28)]   // 平年 2 月：28 天 + 首槽 ⇒ 5 行够
    [InlineData(2024, 2, 29)]   // 闰年 2 月
    [InlineData(2026, 12, 31)]
    public void EveryMonth_CoversExactlyItsDays(int year, int month, int days)
    {
        var cells = MonthGrid.Build(year, month, AnyToday).SelectMany(r => r).ToList();
        Assert.Equal(days, cells.Count(c => c.InMonth));
        Assert.Equal(0, cells.Count(c => c.InMonth && c.Day > days));
        Assert.Equal(MonthGrid.CellCount(year, month), cells.Count);
        Assert.Equal(0, cells.Count % MonthGrid.Columns);   // 永远补齐整周，界面才能对表头
    }

    [Fact]
    public void FestivalFlag_OnlySetOnInMonthDays()
    {
        // 国庆：2026-10-01 有节日名 ⇒ 该格要标出来；不属于本月的补齐格一律不标
        var cells = MonthGrid.Build(2026, 10, AnyToday).SelectMany(r => r).ToList();
        Assert.Contains(cells, c => c.InMonth && c.IsFestival);
        Assert.DoesNotContain(cells, c => !c.InMonth && c.IsFestival);
    }

    [Fact]
    public void WeekHeaders_AreSevenAndStartOnSunday()
    {
        Assert.Equal(7, MonthGrid.WeekHeaders.Length);
        Assert.Equal("日", MonthGrid.WeekHeaders[0]);
        Assert.Equal("六", MonthGrid.WeekHeaders[^1]);
    }

    [Theory]
    [InlineData(2026, 1, -1, 2025, 12)]
    [InlineData(2026, 12, 1, 2027, 1)]
    [InlineData(2026, 6, 0, 2026, 6)]
    [InlineData(2026, 3, -14, 2025, 1)]
    public void Shift_CarriesAcrossYearBoundaries(int y, int m, int delta, int ey, int em)
        => Assert.Equal((ey, em), MonthGrid.Shift(y, m, delta));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(2026, -2)]
    public void InvalidMonths_ProduceEmptyGridInsteadOfThrowing(int year, int month)
    {
        Assert.False(MonthGrid.IsValidMonth(year, month));
        Assert.Empty(MonthGrid.Build(year, month, AnyToday));
    }
}
