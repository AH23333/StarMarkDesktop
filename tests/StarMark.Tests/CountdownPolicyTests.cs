#nullable enable
using System;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 倒计时 / 纪念日纯逻辑测试：墙上时钟+时区的换算、每年推进（含闰日）、剩余量格式化、
/// 到点提醒的窗口与防重、输入校验。
/// </summary>
[Collection("LocaleGlobal")]
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
    [InlineData(0, 0, 30, "widget.cd.remainClock")]
    [InlineData(0, 5, 0, "widget.cd.remainClock")]
    [InlineData(0, 0, 0, "widget.cd.underMinute")]
    [InlineData(1, 2, 3, "widget.cd.remainDays")]
    [InlineData(365, 0, 0, "widget.cd.remainDays")]
    public void FormatRemaining_HandsOutTheKeyOfThatShape(int d, int h, int m, string expectedKey)
        => Assert.Equal(expectedKey, CountdownPolicy.FormatRemaining(
            TimeSpan.FromDays(d) + TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m)).Key);

    [Fact]
    public void FormatRemaining_PassesTheNumbersOutAsArgs()
    {
        // 键对了⛔ 够：天数与钟点对调，屏幕上是一格"看着像"的错话，只有实参位置拦得住
        var one = CountdownPolicy.FormatRemaining(TimeSpan.FromDays(1) + TimeSpan.FromHours(2) + TimeSpan.FromMinutes(3));
        Assert.Equal("widget.cd.remainDays", one.Key);
        Assert.Equal(1, one.Args![0]);
        Assert.Equal("02:03", one.Args[1]);
        Assert.Equal("00:30", CountdownPolicy.FormatRemaining(TimeSpan.FromMinutes(30)).Args![0]);
        Assert.Empty(CountdownPolicy.FormatRemaining(TimeSpan.FromSeconds(30)).Args ?? Array.Empty<object>());
    }

    [Theory]
    [InlineData(0, 0, 42, "time.unit.minutes")]
    [InlineData(0, 0, 0, "time.unit.minutes")]        // 刚过一秒也要说"1 分钟"，不能显示 0 分钟像卡住
    [InlineData(0, 7, 0, "time.unit.hours")]
    [InlineData(3, 0, 0, "time.unit.days")]
    public void FormatDuration_GrowsThroughTheSharedUnitKeys(int d, int h, int m, string expectedKey)
        => Assert.Equal(expectedKey, CountdownPolicy.FormatDuration(
            TimeSpan.FromDays(d) + TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m)).Key);

    [Fact]
    public void FormatDuration_KeepsTheOneMinuteFloor()
        => Assert.Equal(1, CountdownPolicy.FormatDuration(TimeSpan.FromSeconds(20)).Args![0]);

    [Fact]
    public void Describe_CountingDownVersusPastDue()
    {
        var soon = Item("交稿", new DateTime(2026, 1, 1, 12, 0, 0));
        var view = CountdownPolicy.Describe(soon, new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.FromHours(8)));
        Assert.False(view.IsPast);
        Assert.Equal("widget.cd.remainClock", view.Headline.Key);
        Assert.Equal("01:00", view.Headline.Args![0]);
        Assert.Equal("widget.cd.dueAt", view.Subline.Key);

        var late = CountdownPolicy.Describe(soon, new DateTimeOffset(2026, 1, 4, 12, 0, 0, TimeSpan.FromHours(8)));
        Assert.True(late.IsPast);
        Assert.Equal("widget.cd.past", late.Headline.Key);
        // 套娃那一层也得是键：外层"已过 {0}"里塞的若是已经取好的那句，换语言就只剩外层跟着走
        var passed = Assert.IsType<LocMessage>(late.Headline.Args![0]);
        Assert.Equal("time.unit.days", passed.Key);
        Assert.Equal(3, passed.Args![0]);
        Assert.False(late.IsDueNow);   // 过去 3 天：不再算"刚到点"
    }

    [Fact]
    public void AnniversaryLine_NeverGluesThisYearOntoTheAnniversaryWord()
    {
        // 旧写法把兜底词"今年"与片段" 周年"拼在一起 ⇒ 屏幕上出过「今年 周年」。
        // 钉的是"每个分支各交一颗整句"：今年那一档里⛔ 出现周年那颗键。
        var thisYear = Item("今年办的", new DateTime(2026, 3, 5, 9, 0, 0), yearly: true);
        var now = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.FromHours(8));
        Assert.Equal("widget.cd.thisYearAt", CountdownPolicy.Describe(thisYear, now).Subline.Key);

        var third = Item("三年前办的", new DateTime(2023, 3, 5, 9, 0, 0), yearly: true);
        var view = CountdownPolicy.Describe(third, now);
        Assert.Equal("widget.cd.annivAt", view.Subline.Key);
        Assert.Equal(3, view.Subline.Args![1]);
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
            $"{date} {time} 应可解析：{error?.Key}");
        Assert.Null(error);
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0), wall);
    }

    [Theory]
    [InlineData("2026-13-01", "10:00", "widget.cd.errDate")]      // 月份越界
    [InlineData("明年某天", "10:00", "widget.cd.errDate")]
    [InlineData("2026-12-31", "25:00", "widget.cd.errTime")]      // 时刻越界：不能折成次日 1 点
    [InlineData("2026-12-31", "8:60", "widget.cd.errTime")]
    [InlineData("2026-12-31", "上午8点", "widget.cd.errTime")]
    public void TryParseInput_NamesTheHalfThatIsWrong(string date, string time, string expectedKey)
    {
        // "哪一半错了"是键层面的事：交一句"格式不对"上来，屏幕上是能看、但改了也没用的那句话
        Assert.False(CountdownPolicy.TryParseInput(date, time, requireTime: true, out _, out var error));
        Assert.Equal(expectedKey, error!.Key);
    }

    [Fact]
    public void TryParseInput_RequiresTimeOnlyForOneShot()
    {
        Assert.False(CountdownPolicy.TryParseInput("2026-12-31", "", requireTime: true, out _, out var err));
        Assert.Equal("widget.cd.errTimeRequired", err!.Key);
        Assert.True(CountdownPolicy.TryParseInput("2026-12-31", "", requireTime: false, out var wall, out _));
        Assert.Equal(new DateTime(2026, 12, 31), wall);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    [InlineData("项目 A", "项目 A")]
    public void TitleStored_KeepsOnlyWhatTheUserTyped(string? title, string expected)
    {
        // ⛔ 把生成的默认名连同"当时那门语言"一起存进档：那是把念法当身份存了（✅WI-46c 那颗组名同族）
        Assert.Equal(expected, CountdownPolicy.TitleStored(title));
    }

    [Fact]
    public void TitleMessage_HandsOutTheDerivedNameAsAKeyWithDateArgs()
    {
        // 月与日<b>各一颗</b>：那个「-」住在表里（各语言自己写日期形状），⛔ 由代码先拼成 "6-6"
        var anniv = CountdownPolicy.TitleMessage(new DateTime(2020, 6, 6), yearly: true);
        Assert.Equal("widget.cd.nameAnniv", anniv.Key);
        Assert.Equal(6, anniv.Args![0]);
        Assert.Equal(6, anniv.Args[1]);

        var due = CountdownPolicy.TitleMessage(new DateTime(2026, 12, 31, 18, 0, 0), yearly: false);
        Assert.Equal("widget.cd.nameDue", due.Key);
        Assert.Equal("2026-12-31", due.Args![0]);
        Assert.Equal("18:00", due.Args[1]);
    }

    [Fact]
    public void TitleText_RendersOldGeneratedNamesAndKeepsWhatTheUserTyped()
    {
        // 老档里存着"当年生成的默认名"：认得出来就按派生名现算（⛔ 悄悄改档——这里只改念法）
        var legacy = new CountdownItem { Title = "截止（2026-12-31 18:00）", At = new DateTime(2026, 12, 31, 18, 0, 0) };
        Assert.Equal(CountdownPolicy.TitleMessage(legacy.At, yearly: false).Render(), CountdownPolicy.TitleText(legacy));

        var blank = new CountdownItem { Title = "", At = new DateTime(2020, 6, 6), Yearly = true };
        Assert.Equal(CountdownPolicy.TitleMessage(blank.At, yearly: true).Render(), CountdownPolicy.TitleText(blank));

        var user = new CountdownItem { Title = "交稿", At = new DateTime(2026, 12, 31, 18, 0, 0) };
        Assert.Equal("交稿", CountdownPolicy.TitleText(user));

        // 一颗长得很像默认名、其实是用户自己起的（日期对不上）⛔ 被顺手换成派生名
        var lookalike = new CountdownItem { Title = "截止（2030-1-1 08:00）", At = new DateTime(2026, 12, 31, 18, 0, 0) };
        Assert.Equal("截止（2030-1-1 08:00）", CountdownPolicy.TitleText(lookalike));
    }

    [Fact]
    public void TitleText_OldGeneratedNameFollowsTheInterfaceLanguage()
    {
        // 这一颗是本批**真跑的语言读数**：换语言之后那一行念的是当前语言（期望那一侧现取同一颗键，⛔ 抄一句英文进测试）
        var before = Locale.Current;
        try
        {
            var legacy = new CountdownItem { Title = "截止（2026-12-31 18:00）", At = new DateTime(2026, 12, 31, 18, 0, 0) };
            Locale.TrySet(AppLanguage.English);
            var english = CountdownPolicy.TitleText(legacy);
            Assert.DoesNotContain("截止", english);
            Assert.Equal(CountdownPolicy.TitleMessage(legacy.At, yearly: false).Render(), english);
        }
        finally
        {
            Locale.TrySet(before);
        }
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
