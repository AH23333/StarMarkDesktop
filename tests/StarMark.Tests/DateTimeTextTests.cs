#nullable enable
using System;
using System.Globalization;
using Xunit;
using StarMark.Abstractions;

namespace StarMark.Tests;

/// <summary>
/// 显示侧日期/时间文本的唯一出处 <see cref="DateTimeText"/>（批次 RY，P-122）。
/// <para>
/// 这批要防的不是"格式写错"，是<b>同一个界面里混进两套历法</b>：本产物没开
/// <c>InvariantGlobalization</c>（<c>StarMark.UI.runtimeconfig.json</c> 里没有 <c>System.Globalization.Invariant</c>
/// 那个键），所以隐式 <c>ToString(格式串)</c> 跟着当前文化走——佛历区会把 2026 打成 2569。
/// 文件名那条路早就逐条锁了 <c>InvariantCulture</c>，显示侧漏了——账本 P-122 按 <c>grep ToString("</c> 数出 12 处，
/// 本批把四种写法摊开重数是 <b>26 个调用点</b>（清单见 <c>DisplayDateFormatGateTests</c> 的类注释与 <c>ry_head_census.txt</c>）。
/// </para>
/// <para>
/// 两条口径必须分开钉，这是本批的判据核心：<b>纯数字锁不变文化</b>；<b>星期名不许锁不变文化</b>
/// ——那会把"星期日"打成 <c>Sunday</c>，在中文界面里是另一种坏，所以走 <see cref="DateTimeText.Weekday"/> 这张表。
/// </para>
/// </summary>
public sealed class DateTimeTextTests
{
    // 2026-09-29 是星期二（本批所有逐字期望值都按这一天算）；毫秒给 7，好让 TickStamp 的 .fff 不被零值糊过去
    private static readonly DateTime Tue = new(2026, 9, 29, 14, 5, 7, 7, DateTimeKind.Unspecified);
    // 偏移取"这台机器的当前偏移"：CaptureStamp 内部走 .LocalDateTime，写死 +8 会让用例跟着跑测试的时区变脸
    private static readonly DateTimeOffset TueOffset =
        new(2026, 9, 29, 14, 5, 7, DateTimeOffset.Now.Offset);

    // ────────── ① 症状本身：这些用例先在"没修"的代码上成立，才配当回归 ──────────

    /// <summary>
    /// 同一个 <see cref="DateTime"/>，交给佛历／希吉来历地区去打，年份就不是 2026——
    /// 这条不是在测我们的代码，是<b>把 P-122 的前提钉成可执行证据</b>：
    /// 哪天产物真的开了 <c>InvariantGlobalization</c>，这条会红，那时就该按 #141 更正账本，而不是删掉它。
    /// </summary>
    [Theory]
    [InlineData("th-TH")]   // 佛历：2026 → 2569
    [InlineData("ar-SA")]   // 希吉来历：2026 → 1447/1448，数字也可能换成阿拉伯-印度数字
    public void ImplicitFormatting_IsGenuinelyCultureDependent_ThatIsTheDefect(string cultureName)
    {
        var culture = new CultureInfo(cultureName);
        Assert.NotEqual("2026-09-29", Tue.ToString("yyyy-MM-dd", culture));
        // 星期全称同样跟着文化走 ⇒ 中文界面里会出现泰文/阿拉伯文星期名
        Assert.DoesNotContain("星期二", Tue.ToString("dddd", culture), StringComparison.Ordinal);
    }

    /// <summary>
    /// 修好之后：判据对<b>当前线程文化</b>免疫。
    /// <para>这里必须真的把 <c>CultureInfo.CurrentCulture</c> 设成目标文化再调判据——
    /// 只传 <c>culture</c> 实参测的是 <c>ToString</c>，测不到判据内部用的是哪个文化
    /// （我第一版就是这么软的：把 <c>InvariantCulture</c> 换成 <c>CurrentCulture</c> 它也照样绿）。
    /// 文化是线程局部的，用 try/finally 还原，不污染同线程的后续用例。</para>
    /// </summary>
    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void DateTimeText_IsImmuneToTheCurrentCulture(string cultureName)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            Assert.Equal("2026-09-29", DateTimeText.Day(Tue));
            Assert.Equal("2026-09-29 14:05", DateTimeText.Minute(Tue));
            Assert.Equal("14:05:07", DateTimeText.Clock(Tue, withSeconds: true));
            Assert.Equal("14:05", DateTimeText.Clock(Tue));
            Assert.Equal("2026-09-29 星期二", DateTimeText.DayWithWeekday(Tue));
            Assert.Equal("9-29 14:05", DateTimeText.MonthDayClock(TueOffset));
            Assert.Equal("09-29 14:05", DateTimeText.MonthDayMinute(TueOffset));
            Assert.Equal("20260929-140507", DateTimeText.BackupStamp(TueOffset));
            Assert.Equal("2026-09-29 140507", DateTimeText.CaptureStamp(TueOffset));
            Assert.Equal("14:05:07.007", DateTimeText.TickStamp(Tue));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>时长读数在非公历/非拉丁数字文化下也不许变形（音乐进度与番茄钟共用这一颗）。</summary>
    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    public void Duration_IsImmuneToTheCurrentCulture(string cultureName)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            Assert.Equal("5:07", DateTimeText.Duration(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(7))));
            Assert.Equal("1:02:03", DateTimeText.Duration(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2)
                + TimeSpan.FromSeconds(3), withHours: true));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ────────── ② 逐字口径：中文用户看到的的样子不许变 ──────────

    [Fact]
    public void Day_Minute_Clock_UsePunctuationSeparatorsAndLatinDigits()
    {
        Assert.Equal("2026-09-29", DateTimeText.Day(Tue));
        Assert.Equal("2026-09-29", DateTimeText.Day(TueOffset));
        Assert.Equal("2026-09-29 14:05", DateTimeText.Minute(TueOffset));
        Assert.Equal("2026-09-29 14:05", DateTimeText.Minute(Tue));
        Assert.Equal("14:05:07", DateTimeText.Clock(TueOffset, withSeconds: true));
        Assert.Equal("14:05", DateTimeText.Clock(TueOffset));
    }

    /// <summary>
    /// <c>MonthDayClock</c> 用的是 <c>M-d</c>（不补零）：这是从旧代码逐字搬来的口径，
    /// 补零会变成"09-05"，虽然不算错，但那是一次没有需求的可见改动。
    /// </summary>
    [Fact]
    public void MonthDayClock_KeepsTheUnpaddedOldForm()
    {
        var early = new DateTimeOffset(2026, 9, 5, 8, 3, 0, TimeSpan.FromHours(8));
        Assert.Equal("9-5 08:03", DateTimeText.MonthDayClock(early));
    }

    /// <summary>时钟那一行 = 日期那一颗 + 一颗空格 + 星期全称（不许出现第二种日期写法）。</summary>
    [Fact]
    public void DayWithWeekday_ReusesTheSameDayPrefix()
        => Assert.Equal($"{DateTimeText.Day(Tue)} 星期二", DateTimeText.DayWithWeekday(Tue));

    // ────────── ③ 星期表：全称的唯一出处，位序陷阱钉死 ──────────

    [Theory]
    [InlineData(DayOfWeek.Sunday, "星期日")]
    [InlineData(DayOfWeek.Monday, "星期一")]
    [InlineData(DayOfWeek.Tuesday, "星期二")]
    [InlineData(DayOfWeek.Wednesday, "星期三")]
    [InlineData(DayOfWeek.Thursday, "星期四")]
    [InlineData(DayOfWeek.Friday, "星期五")]
    [InlineData(DayOfWeek.Saturday, "星期六")]
    public void Weekday_PinsAllSevenLongNames(DayOfWeek day, string expected)
        => Assert.Equal(expected, DateTimeText.Weekday(day));

    /// <summary>
    /// 位序跟 <see cref="DayOfWeek"/>（Sunday＝0）走，与 <c>AlarmPolicy.BitFor</c> 同一口径。
    /// 按"周一是一周第一天"的直觉排这张表 ⇒ 七个值整体错一天，而界面上完全看不出问题
    /// （只有"星期二显示成星期二"这种日子才对得上，随机错一天没人报障）。
    /// </summary>
    [Fact]
    public void Weekday_TableIsOrderedByDayOfWeekNotByMondayFirst()
    {
        Assert.Equal("星期日", DateTimeText.Weekday(DayOfWeek.Sunday));   // 位序 0，不是 6
        Assert.Equal("星期一", DateTimeText.Weekday(DayOfWeek.Monday));   // 位序 1，不是 0
    }

    /// <summary>越界（库里存着本二进制不认识的序号）也要给一个能看的值，不许给空串。</summary>
    [Theory]
    [InlineData((DayOfWeek)(-1))]
    [InlineData((DayOfWeek)99)]
    public void Weekday_NeverReturnsEmptyForOutOfRange(DayOfWeek impossible)
    {
        var text = DateTimeText.Weekday(impossible);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.StartsWith("星期", text, StringComparison.Ordinal);
    }

    /// <summary>每个真实星期的输出互不相同：两个不同日子印出同一个星期名＝排表错位暴露不出来。</summary>
    [Fact]
    public void Weekday_SevenDaysProduceSevenDistinctNames()
        => Assert.Equal(7, System.Linq.Enumerable.Distinct(
            System.Linq.Enumerable.Select(System.Enum.GetValues<DayOfWeek>(), DateTimeText.Weekday)).Count());
}
