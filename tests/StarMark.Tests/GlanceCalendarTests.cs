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

    /// <summary>
    /// 回归 AT：本类契约「所有入口绝不把异常冒到 UI 线程」。NextFestival 的 AddDays(i) 在 from
    /// 逼近 DateOnly.MaxValue 时抛 ArgumentOutOfRangeException（旧实现未钳窗口）。修后须返回 null 而非抛。
    /// </summary>
    [Fact]
    public void NextFestival_NearDateOnlyMaxValue_ReturnsNullNotThrow()
    {
        Assert.Null(GlanceCalendar.NextFestival(DateOnly.MaxValue));
        Assert.Null(GlanceCalendar.NextFestival(new DateOnly(9999, 12, 30), 5));
    }

    /// <summary>
    /// 契约护栏（DC）：除夕按「次日即正月初一」反推（`GlanceCalendar.cs:75-78`），刻意**不**硬编码
    /// 「腊月三十」——因农历十二月有 29 天（无年三十）与 30 天两种，硬编码会在 29 天年份**静默漏掉除夕**。
    /// 该分支此前无测（`Festival_KnownDates` 仅覆盖春节/公历节日）。以本文件已钉死的三个春节（正月初一）
    /// 锚点日各减一天即除夕，跨年验证反推恒命中，且再前一天不得是除夕（排除把窗口算宽）；春节锚点自洽复核。
    /// 断言纯由既有农历锚点定义性推导，无需离线推算农历日号。
    /// </summary>
    [Theory]
    [InlineData(2026, 2, 17)] // 正月初一 → 除夕 02-16
    [InlineData(2025, 1, 29)] // 正月初一 → 除夕 01-28
    [InlineData(2024, 2, 10)] // 正月初一 → 除夕 02-09
    public void Festival_Chuxi_IsDerivedFromDayBeforeSpringFestival(int y, int m, int d)
    {
        var springFestival = new DateOnly(y, m, d);
        var chuxi = springFestival.AddDays(-1);

        Assert.Equal("除夕", GlanceCalendar.Festival(chuxi));                 // 次日即正月初一 → 命中除夕
        Assert.NotEqual("除夕", GlanceCalendar.Festival(chuxi.AddDays(-1)));  // 再往前一天不得也是除夕
        Assert.Equal("春节", GlanceCalendar.Festival(springFestival));        // 锚点自洽：正月初一即春节
    }

    /// <summary>
    /// 批次 DH：承 DB→DG 透镜（审到**分支进入 + 输出值**粒度）。本类文件头注释（<c>GlanceCalendarTests.cs:9-11</c>）
    /// 白纸黑字称「闰月处理容易差一位，故用若干已知阳历↔农历锚点日钉死，防止算法回归」——但现有全部锚点
    /// （<see cref="LunarText_SpringFestival_IsFirstDayOfFirstMonth"/> 的三个正月初一）**都在任何闰月之前**，
    /// 于是 <c>GetChineseDate</c> 的闰月判据 <c>isLeap</c> 与「闰月当月及其后月份号 <c>-1</c>」的调整
    /// （<c>GlanceCalendar.cs:146-149</c>）、以及 <c>LunarText</c> 的「闰」前缀（<c>:38</c>）此前**从未被任何用例进入**。
    /// 回归面正是注释担心的那一类且**静默**：若把 <c>calendarMonth &gt;= leapMonth</c> 误写成 <c>&gt;</c>（闰月当月漏 <c>-1</c>），
    /// 闰二月初一会画成「闰<b>三</b>月」；若整个 <c>-1</c> 调整被删，闰月之后**每个月份号整体 +1**、农历文本逐月错位——现有测全绿无感。
    /// 用经 .NET <c>ChineseLunisolarCalendar</c> 与权威历法事实核对的**两个不同闰月年**（2023 闰二月 lm=3 / 2025 闰六月 lm=7）
    /// 钉死三段语义：闰月前（cm&lt;lm 不调整）、闰月当月（cm==lm 触发 isLeap + <c>-1</c>）、闰月次月（cm&gt;lm 仍 <c>-1</c>）；
    /// 取不同 lm 值证明该调整不依赖闰月的具体位置。真值经一次性探针实测、非手算（探针已删除）。
    /// </summary>
    [Theory]
    [InlineData(2023, 3, 21, "二月三十")]    // cm=2 < lm=3：闰月前，月号不调整
    [InlineData(2023, 3, 22, "闰二月初一")]  // cm=3 == lm：isLeap=true 且 3-1=2 →「闰二月」
    [InlineData(2023, 4, 19, "闰二月廿九")]  // 闰月末（day=29→「廿九」，同时校验闰月内的日名派生）
    [InlineData(2023, 4, 20, "三月初一")]    // cm=4 > lm=3：4-1=3 → 次月正确续号
    [InlineData(2025, 7, 24, "六月三十")]    // cm=6 < lm=7：另一闰月位置前，不调整
    [InlineData(2025, 7, 25, "闰六月初一")]  // cm=7 == lm：isLeap + 7-1=6 →「闰六月」
    [InlineData(2025, 8, 23, "七月初一")]    // cm=8 > lm=7：8-1=7 → 次月正确续号
    public void LunarText_LeapMonth_RenderingAndMonthNumbering(int y, int m, int d, string expected)
        => Assert.Equal(expected, GlanceCalendar.LunarText(new DateOnly(y, m, d)));
}
