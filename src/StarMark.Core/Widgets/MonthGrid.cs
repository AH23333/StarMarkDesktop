#nullable enable
using StarMark.Abstractions;

namespace StarMark.Core.Widgets;

/// <summary>月历的一格：日号 + 是否属于本月 + 是否今天 + 是否节日（节日格要能标出来）。</summary>
public readonly record struct MonthCell(int Day, bool InMonth, bool IsToday, bool IsFestival);

/// <summary>
/// 月历网格（纯函数，供「今日速览」的月历视图使用）。
/// <para>
/// 周日起排（与 Windows 中文区域设置的印法一致），首尾都补齐到整周：这样每一行的星期都对得上表头，
/// 不必让界面去处理"最后一行只有 3 格"的错位。
/// </para>
/// </summary>
public static class MonthGrid
{
    /// <summary>
    /// 表头那七个字。<b>字由 <see cref="DateTimeText.WeekdayStem"/> 给，"周日起排"这个次序才是这里的事实</b>——
    /// 它与 <see cref="FirstSlot"/> 用同一个序号（Sunday＝0）决定每格落在第几列，两处不同序就会行行错位。
    /// </summary>
    public static readonly string[] WeekHeaders =
    [
        DateTimeText.WeekdayStem(DayOfWeek.Sunday), DateTimeText.WeekdayStem(DayOfWeek.Monday),
        DateTimeText.WeekdayStem(DayOfWeek.Tuesday), DateTimeText.WeekdayStem(DayOfWeek.Wednesday),
        DateTimeText.WeekdayStem(DayOfWeek.Thursday), DateTimeText.WeekdayStem(DayOfWeek.Friday),
        DateTimeText.WeekdayStem(DayOfWeek.Saturday),
    ];

    public const int Columns = 7;

    /// <summary>该月 1 号落在周几（周日 = 0）＝ 网格开头要补的空白天数。</summary>
    public static int FirstSlot(int year, int month) => (int)new DateOnly(year, month, 1).DayOfWeek;

    /// <summary>网格第一格对应的日期（可能是上个月的月末）。</summary>
    public static DateOnly FirstCell(int year, int month) => new DateOnly(year, month, 1).AddDays(-FirstSlot(year, month));

    /// <summary>整个网格共几格（42＝6 行、35＝5 行）。</summary>
    public static int CellCount(int year, int month)
        => (int)Math.Ceiling((FirstSlot(year, month) + DateTime.DaysInMonth(year, month)) / (double)Columns) * Columns;

    /// <summary>
    /// 生成整月网格。<paramref name="today"/> 只用来标出"今天"这一格，不参与年月计算
    /// ⇒ 翻到上月时，今天的格子不会跟着跑到别月去。
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<MonthCell>> Build(int year, int month, DateOnly today)
    {
        if (!IsValidMonth(year, month)) return Array.Empty<IReadOnlyList<MonthCell>>();

        var start = FirstCell(year, month);
        var count = CellCount(year, month);
        var cells = new List<MonthCell>(count);
        for (var i = 0; i < count; i++)
        {
            var date = start.AddDays(i);
            var inMonth = date.Year == year && date.Month == month;
            cells.Add(new MonthCell(date.Day, inMonth, date == today, inMonth && GlanceCalendar.Festival(date) is not null));
        }

        var rows = new List<IReadOnlyList<MonthCell>>(count / Columns);
        for (var r = 0; r < count / Columns; r++)
            rows.Add(cells.GetRange(r * Columns, Columns).AsReadOnly());
        return rows;
    }

    /// <summary>合法年月：越界一律拒绝（界面拿到空网格，而不是让 DateOnly 构造函数把异常抛到 UI 线程）。</summary>
    public static bool IsValidMonth(int year, int month) => year is >= 1 and <= 9999 && month is >= 1 and <= 12;

    /// <summary>上一月/下一月（跨年自然进位），供"翻月"用。</summary>
    public static (int Year, int Month) Shift(int year, int month, int delta)
    {
        var index = year * 12 + (month - 1) + delta;
        return (index / 12, index % 12 + 1);
    }
}
