#nullable enable
using System;
using System.Globalization;

namespace StarMark.Core.Widgets;

/// <summary>
/// 「今日速览（Glance）」组件的日历计算：农历、节日、下一个节日倒计时。
/// <para>
/// 纯逻辑、不依赖 WinUI，便于单测。农历与清明节算法照搬 DeskBox 的
/// <c>Services/GlanceFestivalService.cs</c>——底层直接用 .NET 内置的
/// <see cref="ChineseLunisolarCalendar"/>，因此**不需要自建闰月表 / 节气表**。
/// 在 DeskBox 基础上补充了 DeskBox 没有的公历固定节日（元旦 / 国庆 …）。
/// </para>
/// </summary>
public static class GlanceCalendar
{
    /// <summary>下一个节日的倒计时信息。</summary>
    /// <param name="Name">节日名。</param>
    /// <param name="Date">节日日期。</param>
    /// <param name="Days">距今天数（0 = 就是今天）。</param>
    public sealed record FestivalCountdown(string Name, DateOnly Date, int Days);

    // ChineseLunisolarCalendar 只覆盖约 1901-02-19 ~ 2100-01-29，超出会抛
    // ArgumentOutOfRangeException——所有入口都兜底，绝不把异常冒到 UI 线程。
    private static readonly ChineseLunisolarCalendar Calendar = new();

    /// <summary>
    /// 农历文本（如「八月初五」「闰四月初一」）；不支持的日期返回空串。
    /// </summary>
    public static string LunarText(DateOnly date)
    {
        try
        {
            var (month, day, isLeap) = GetChineseDate(date);
            if (month <= 0 || day <= 0) return string.Empty;
            var monthName = month is >= 1 and <= 12 ? LunarMonthNames[month] : month.ToString();
            return (isLeap ? "闰" : string.Empty) + monthName + "月" + LunarDayName(day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 该日期的节日名（农历节日 / 公历固定节日 / 清明）；无节日返回 null。
    /// 同一天撞多个节日时优先农历节日（与 DeskBox 一致：春节/中秋等优先于公历节日）。
    /// </summary>
    public static string? Festival(DateOnly date)
    {
        try
        {
            // 清明：DeskBox 的寿星公式（每年日期浮动，需按年计算）
            if (date.Month == 4 && date.Day == GetQingmingDay(date.Year))
                return "清明";

            var (month, day, isLeap) = GetChineseDate(date);
            if (!isLeap)
            {
                var lunarFestival = (month, day) switch
                {
                    (1, 1) => "春节",
                    (1, 15) => "元宵",
                    (5, 5) => "端午",
                    (7, 7) => "七夕",
                    (7, 15) => "中元",
                    (8, 15) => "中秋",
                    (9, 9) => "重阳",
                    (12, 8) => "腊八",
                    _ => null,
                };
                if (lunarFestival is not null) return lunarFestival;

                // 除夕：农历十二月可能是 29 或 30 天，故用「次日是正月初一」反推，
                // 比硬编码「腊月三十」稳（DeskBox 同思路）。
                var next = GetChineseDate(date.AddDays(1));
                if (!next.IsLeap && next.Month == 1 && next.Day == 1) return "除夕";
            }

            return SolarFestival(date);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// 从 <paramref name="from"/> 起（含当天）最近的下一个节日；400 天内找不到返回 null。
    /// </summary>
    public static FestivalCountdown? NextFestival(DateOnly from, int maxDays = 400)
    {
        for (var i = 0; i <= maxDays; i++)
        {
            var date = from.AddDays(i);
            var name = Festival(date);
            if (name is not null) return new FestivalCountdown(name, date, i);
        }
        return null;
    }

    /// <summary>星期的中文短名（周一 … 周日）。</summary>
    public static string WeekdayText(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        DayOfWeek.Sunday => "周日",
        _ => string.Empty,
    };

    // ── 公历固定节日（DeskBox 没有，StarMark 补充）──
    private static string? SolarFestival(DateOnly date) => (date.Month, date.Day) switch
    {
        (1, 1) => "元旦",
        (2, 14) => "情人节",
        (3, 8) => "妇女节",
        (3, 12) => "植树节",
        (4, 1) => "愚人节",
        (5, 1) => "劳动节",
        (5, 4) => "青年节",
        (6, 1) => "儿童节",
        (7, 1) => "建党节",
        (8, 1) => "建军节",
        (9, 10) => "教师节",
        (10, 1) => "国庆节",
        (11, 11) => "双十一",
        (12, 24) => "平安夜",
        (12, 25) => "圣诞节",
        _ => null,
    };

    private static (int Month, int Day, bool IsLeap) GetChineseDate(DateOnly date)
    {
        var value = date.ToDateTime(TimeOnly.MinValue);
        var year = Calendar.GetYear(value);
        var calendarMonth = Calendar.GetMonth(value);
        var leapMonth = Calendar.GetLeapMonth(year);
        var isLeap = leapMonth > 0 && calendarMonth == leapMonth;
        var month = leapMonth > 0 && calendarMonth >= leapMonth
            ? calendarMonth - 1
            : calendarMonth;
        return (month, Calendar.GetDayOfMonth(value), isLeap);
    }

    /// <summary>清明节在当年的日号（寿星公式，照搬 DeskBox）。</summary>
    private static int GetQingmingDay(int year)
    {
        var shortYear = year % 100;
        var centuryConstant = year < 2000 ? 5.59 : 4.81;
        return (int)Math.Floor(shortYear * 0.2422 + centuryConstant) - (shortYear / 4);
    }

    private static readonly string[] LunarMonthNames =
        { string.Empty, "正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊" };

    private static string LunarDayName(int day) => day switch
    {
        10 => "初十",
        20 => "二十",
        30 => "三十",
        < 10 => "初" + Num(day),
        < 20 => "十" + Num(day - 10),
        < 30 => "廿" + Num(day - 20),
        _ => day.ToString(),
    };

    private static string Num(int n) => n switch
    {
        1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五",
        6 => "六", 7 => "七", 8 => "八", 9 => "九",
        _ => string.Empty,
    };
}
