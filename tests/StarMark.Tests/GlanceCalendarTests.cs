#nullable enable
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 今日速览（Glance）的农历 / 节日计算测试。
/// 农历底层是 .NET 内置 <see cref="System.Globalization.ChineseLunisolarCalendar"/>，
/// 闰月处理容易差一位，故用若干「已知阳历↔农历」的锚点日钉死，防止算法回归。
/// </summary>
public sealed class GlanceCalendarTests
{
    [Theory]
    // 2026 春节：正月初一 = 2026-02-17
    [InlineData(2026, 2, 17, "正月初一")]
    // 2025 春节：正月初一 = 2025-01-29
    [InlineData(2025, 1, 29, "正月初一")]
    // 2024 春节：正月初一 = 2024-02-10
    [InlineData(2024, 2, 10, "正月初一")]
    public void LunarText_SpringFestival_IsFirstDayOfFirstMonth(int y, int m, int d, string expected)
    {
        Assert.Equal(expected, GlanceCalendar.LunarText(new DateOnly(y, m, d)));
    }

    [Theory]
    [InlineData(2026, 2, 17, "春节")]
    [InlineData(2025, 1, 29, "春节")]
    [InlineData(2026, 10, 1, "国庆节")]
    [InlineData(2026, 1, 1, "元旦")]
    [InlineData(2026, 12, 25, "圣诞节")]
    public void Festival_KnownDates(int y, int m, int d, string expected)
    {
        Assert.Equal(expected, GlanceCalendar.Festival(new DateOnly(y, m, d)));
    }

    [Fact]
    public void Festival_Qingming_IsInApril()
    {
        // 清明按寿星公式浮动在 4/4~4/6，只能落在 4 月且必须是该年算出的那一天。
        for (var year = 2024; year <= 2030; year++)
        {
            var hit = -1;
            for (var day = 1; day <= 30; day++)
            {
                if (GlanceCalendar.Festival(new DateOnly(year, 4, day)) == "清明") hit = day;
            }
            Assert.InRange(hit, 4, 6);
        }
    }

    [Fact]
    public void Festival_OrdinaryDay_IsNull()
    {
        // 2026-03-10（农历正月廿二）：既非农历节日、也非公历节日、更不在清明所在的 4 月。
        // 注意别挑 3/3——那天是正月十五元宵节。
        Assert.Null(GlanceCalendar.Festival(new DateOnly(2026, 3, 10)));
    }

    [Fact]
    public void NextFestival_ReturnsNonPastAndMatchingDay()
    {
        var from = new DateOnly(2026, 3, 3);
        var next = GlanceCalendar.NextFestival(from);

        Assert.NotNull(next);
        Assert.True(next!.Days >= 0, "倒计时不能为负");
        Assert.Equal(next.Days, next.Date.DayNumber - from.DayNumber);
        Assert.Equal(next.Name, GlanceCalendar.Festival(next.Date));
    }

    [Fact]
    public void NextFestival_OnFestivalDay_IsSameDay()
    {
        var newYear = new DateOnly(2026, 1, 1);
        var next = GlanceCalendar.NextFestival(newYear);

        Assert.NotNull(next);
        Assert.Equal(0, next!.Days);
        Assert.Equal("元旦", next.Name);
    }

    [Fact]
    public void LunarText_OutOfCalendarRange_ReturnsEmpty()
    {
        // ChineseLunisolarCalendar 只覆盖约 1901~2100，越界必须静默兜底而不是抛异常
        Assert.Equal(string.Empty, GlanceCalendar.LunarText(new DateOnly(1800, 1, 1)));
        Assert.Null(GlanceCalendar.Festival(new DateOnly(1800, 1, 1)));
    }

    [Theory]
    [InlineData(2026, 9, 18, "周五")]
    [InlineData(2026, 9, 20, "周日")]
    public void WeekdayText_Matches(int y, int m, int d, string expected)
    {
        Assert.Equal(expected, GlanceCalendar.WeekdayText(new DateOnly(y, m, d)));
    }
}
