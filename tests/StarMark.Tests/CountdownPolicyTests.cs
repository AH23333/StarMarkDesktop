#nullable enable
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 倒计时 / 纪念日纯逻辑测试：墙上时钟+时区的换算、每年推进（含闰日）、剩余量格式化、
/// 到点提醒的窗口与防重、输入校验。
/// </summary>
public sealed class CountdownPolicyTests
{
    private const string Beijing = "China Standard Time";

    private static CountdownItem Item(string title, DateTime at, bool yearly = false, string zone = Beijing) => new()
    {
        Id = 1,
        Title = title,
        At = at,
        ZoneId = zone,
        Yearly = yearly,
    };

    // ────────── 墙上时钟 + 时区 ──────────

    [Fact]
    public void ToInstant_KeepsTheWallClockTheUserTyped()
    {
        var at = CountdownPolicy.ToInstant(new DateTime(2026, 1, 1, 6, 0, 0), Beijing);
        Assert.Equal(TimeSpan.FromHours(8), at.Offset);
        Assert.Equal(6, at.Hour);
        Assert.Equal(1, at.Day);
        // 时区 Id 在这台机器上不存在（换机器/精简系统）时不能抛：退回本机时区照样给出一个时刻
        Assert.NotEqual(default, CountdownPolicy.ToInstant(new DateTime(2026, 1, 1), "NoSuch/Zone"));
    }

    [Fact]
    public void YearlyOccurrence_DoesNotSlipADayAcrossTimezones()
    {
        // 这条钉住"为什么存墙钟而不是 UTC 瞬间"：北京 01-01 06:00 折成 UTC 是前一年 12-31 22:00，
        // 若按 UTC 的月日推进，纪念日会永久错开一天。
        var item = Item("生日", new DateTime(2020, 1, 1, 6, 0, 0), yearly: true);
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var next = CountdownPolicy.NextOccurrence(item, now);
        Assert.Equal(2027, next.Year);          // 2026-01-01 已过 ⇒ 推到下一年
        Assert.Equal(1, next.Month);
        Assert.Equal(1, next.Day);
        Assert.Equal(6, next.Hour);
        Assert.Equal(TimeSpan.FromHours(8), next.Offset);
        Assert.Equal(new DateTime(2027, 1, 1, 6, 0, 0), next.LocalDateTime);
    }

    [Fact]
    public void YearlyOccurrence_TakesThisYearWhenStillAhead()
    {
        var item = Item("发布会", new DateTime(2019, 12, 31, 20, 0, 0), yearly: true);
        var now = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.FromHours(8));
        var next = CountdownPolicy.NextOccurrence(item, now);
        Assert.Equal(new DateTime(2026, 12, 31, 20, 0, 0), next.LocalDateTime);
    }

    [Fact]
    public void YearlyLeapDay_ClampsToLastDayOfFebruaryInPlainYears()
    {
        var item = Item("闰日", new DateTime(2024, 2, 29, 9, 0, 0), yearly: true);
        var now = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.FromHours(8));
        var next = CountdownPolicy.NextOccurrence(item, now);
        // 宁可在 2 月里提醒，也不要跳到 3 月（与"31 号到期而该月只有 30 天"顺延到月末同一直觉）
        Assert.Equal(new DateTime(2025, 2, 28, 9, 0, 0), next.LocalDateTime);
        // 闰年真的回到 2/29：从 2027 年 3 月起算，下一个 2 月是 2028-02-29
        var leapNow = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.FromHours(8));
        Assert.Equal(new DateTime(2028, 2, 29, 9, 0, 0),
            CountdownPolicy.NextOccurrence(item, leapNow).LocalDateTime);
    }

    [Fact]
    public void OneShotOccurrence_IsExactlyTheInstantChosen()
    {
        var item = Item("交稿", new DateTime(2026, 12, 31, 18, 0, 0));
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(CountdownPolicy.ToInstant(item.At, Beijing), CountdownPolicy.NextOccurrence(item, now));
    }

    // ────────── 剩余量格式化 ──────────

    [Theory]
    [InlineData(0, 0, 30, "00:30")]
    [InlineData(0, 5, 0, "05:00")]
    [InlineData(0, 0, 0, "不足 1 分钟")]
    [InlineData(1, 2, 3, "1 天 02:03")]
    [InlineData(365, 0, 0, "365 天 00:00")]
    public void FormatRemaining_ShowsDaysOnlyWhenThereAreDays(int d, int h, int m, string expected)
        => Assert.Equal(expected, CountdownPolicy.FormatRemaining(TimeSpan.FromDays(d) + TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m)));

    [Theory]
    [InlineData(0, 0, 42, "42 分钟")]
    [InlineData(0, 0, 0, "1 分钟")]        // 刚过一秒也要说"1 分钟"，不能显示 0 分钟像卡住
    [InlineData(0, 7, 0, "7 小时")]
    [InlineData(3, 0, 0, "3 天")]
    public void FormatDuration_GrowsThroughUnits(int d, int h, int m, string expected)
        => Assert.Equal(expected, CountdownPolicy.FormatDuration(
            TimeSpan.FromDays(d) + TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m)));

    [Fact]
    public void Describe_CountingDownVersusPastDue()
    {
        var soon = Item("交稿", new DateTime(2026, 1, 1, 12, 0, 0));
        var view = CountdownPolicy.Describe(soon, new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.FromHours(8)));
        Assert.False(view.IsPast);
        Assert.Equal("01:00", view.Headline);

        var late = CountdownPolicy.Describe(soon, new DateTimeOffset(2026, 1, 4, 12, 0, 0, TimeSpan.FromHours(8)));
        Assert.True(late.IsPast);
        Assert.Equal("已过 3 天", late.Headline);
        Assert.False(late.IsDueNow);   // 过去 3 天：不再算"刚到点"
    }

    // ────────── 到点提醒 ──────────

    [Fact]
    public void ShouldNotify_OnlyInsideWindowOncePerOccurrence()
    {
        var item = Item("交稿", new DateTime(2026, 1, 1, 12, 0, 0));
        var occurrence = CountdownPolicy.ToInstant(item.At, Beijing);

        Assert.False(CountdownPolicy.ShouldNotify(item, occurrence, occurrence.AddMinutes(-1)));
        Assert.True(CountdownPolicy.ShouldNotify(item, occurrence, occurrence));
        Assert.True(CountdownPolicy.ShouldNotify(item, occurrence, occurrence.AddMinutes(59)));
        Assert.False(CountdownPolicy.ShouldNotify(item, occurrence, occurrence + CountdownPolicy.NotifyWindow + TimeSpan.FromMinutes(1)));

        CountdownPolicy.MarkNotified(item, occurrence);
        Assert.False(CountdownPolicy.ShouldNotify(item, occurrence, occurrence.AddMinutes(10)));
    }

    [Fact]
    public void MarkNotified_ExpiresOnTheNextYearlyRound()
    {
        // 提醒键必须"有寿命"：把它写成永久屏蔽，明年的同一个纪念日就再也不会提醒
        var item = Item("结婚纪念日", new DateTime(2020, 6, 6, 9, 0, 0), yearly: true);
        var thisYear = new DateTimeOffset(2026, 6, 6, 9, 30, 0, TimeSpan.FromHours(8));
        CountdownPolicy.MarkNotified(item, CountdownPolicy.NextOccurrence(item, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(8))));
        Assert.False(CountdownPolicy.ShouldNotify(item, CountdownPolicy.NextOccurrence(item, new DateTimeOffset(2026, 6, 6, 9, 0, 0, TimeSpan.FromHours(8))), thisYear));

        var nextYear = new DateTimeOffset(2027, 6, 6, 9, 5, 0, TimeSpan.FromHours(8));
        var occurrence27 = CountdownPolicy.NextOccurrence(item, new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(8)));
        Assert.True(CountdownPolicy.ShouldNotify(item, occurrence27, nextYear));
    }

    [Fact]
    public void LongPastTarget_DoesNotNotifyOnTheTickThatLoadsIt()
    {
        // 补录一条早已过去的截止日：按下保存的那一刻不该弹泡。
        // 窗口判据本身就挡住了（三年前的时刻不在 1 小时内），VM 另外把那一轮标成已提醒做双保险。
        var item = Item("补录的纪念日", new DateTime(2019, 3, 3, 8, 0, 0));
        var now = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.FromHours(8));
        var first = CountdownPolicy.NextOccurrence(item, now);
        Assert.True(now > first);
        Assert.False(CountdownPolicy.ShouldNotify(item, first, now));
    }

    // ────────── 输入校验 ──────────

    [Theory]
    [InlineData("2026-12-31", "18:00", false, 2026, 12, 31, 18, 0)]
    [InlineData("2026/1/5", "9:05", false, 2026, 1, 5, 9, 5)]
    [InlineData("2026-1-5", "07:00", false, 2026, 1, 5, 7, 0)]
    [InlineData("2026-12-31", "", true, 2026, 12, 31, 0, 0)]     // 纪念日只看天
    public void TryParseInput_AcceptsReasonableForms(
        string date, string time, bool yearly, int y, int mo, int d, int h, int mi)
    {
        Assert.True(CountdownPolicy.TryParseInput(date, time, requireTime: !yearly, out var wall, out var error),
            $"{date} {time} 应可解析：{error}");
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0), wall);
    }

    [Theory]
    [InlineData("2026-13-01", "10:00")]      // 月份越界
    [InlineData("明年某天", "10:00")]
    [InlineData("2026-12-31", "25:00")]      // 时刻越界：不能折成次日 1 点
    [InlineData("2026-12-31", "8:60")]
    [InlineData("2026-12-31", "上午8点")]
    public void TryParseInput_RejectsAndNamesTheHalfThatIsWrong(string date, string time)
    {
        Assert.False(CountdownPolicy.TryParseInput(date, time, requireTime: true, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Contains(date == "2026-12-31" ? "时刻" : "日期", error);
    }

    [Fact]
    public void TryParseInput_RequiresTimeOnlyForOneShot()
    {
        Assert.False(CountdownPolicy.TryParseInput("2026-12-31", "", requireTime: true, out _, out var err));
        Assert.Contains("时刻", err);
        Assert.True(CountdownPolicy.TryParseInput("2026-12-31", "", requireTime: false, out var wall, out _));
        Assert.Equal(new DateTime(2026, 12, 31), wall);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    [InlineData("项目 A", true)]
    public void TitleOf_FallsBackToADateDerivedName(string? title, bool hasTitle)
    {
        var wall = new DateTime(2026, 12, 31, 18, 0, 0);
        var got = CountdownPolicy.TitleOf(title, wall, yearly: false);
        Assert.Equal(hasTitle ? "项目 A" : $"截止（{wall.Year}-12-31 18:00）", got);
        Assert.False(string.IsNullOrWhiteSpace(got));
        Assert.StartsWith("纪念日（6-6）", CountdownPolicy.TitleOf(null, new DateTime(2020, 6, 6), yearly: true));
    }

    [Fact]
    public void Clone_IsDeepEnoughThatLiveTicksDoNotRewriteASnapshot()
    {
        var item = Item("交稿", new DateTime(2026, 12, 31, 18, 0, 0));
        var copy = item.Clone();
        copy.Title = "改了";
        CountdownPolicy.MarkNotified(copy, CountdownPolicy.ToInstant(copy.At, copy.ZoneId));
        Assert.Equal("交稿", item.Title);
        Assert.Null(item.NotifiedOccurrence);
        Assert.NotEmpty(copy.NotifiedOccurrence!);
    }

    [Fact]
    public void OccurrenceKey_IsStableWithinRoundAndDistinctAcrossRounds()
    {
        var a = CountdownPolicy.ToInstant(new DateTime(2026, 12, 31, 18, 0, 0), Beijing);
        var b = CountdownPolicy.ToInstant(new DateTime(2027, 12, 31, 18, 0, 0), Beijing);
        Assert.Equal(CountdownPolicy.OccurrenceKey(a), CountdownPolicy.OccurrenceKey(a.AddSeconds(30)));
        Assert.NotEqual(CountdownPolicy.OccurrenceKey(a), CountdownPolicy.OccurrenceKey(b));
    }
}
