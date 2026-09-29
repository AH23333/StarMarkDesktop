#nullable enable
using System;
using System.Text.Json;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 闹钟的判据（批次 RU）。全部<b>注入时刻</b>，不起表也不碰窗口——"哪一天响、什么时候算到点、
/// 待确认挂多久"这三件事每一条都只在真机上看得见，而真机验证要等一个真实的早晨，
/// 所以先把它们钉死在这里。
/// </summary>
public sealed class AlarmPolicyTests
{
    /// <summary>2026-09-29 是<b>周二</b>（本批写就时的当天，日期与星期一并钉在下面那几枚测里）。</summary>
    private static DateTimeOffset Tue0700() => new(2026, 9, 29, 7, 0, 0, TimeSpan.FromHours(8));

    private static DateTimeOffset At(int hour, int minute) => new(2026, 9, 29, hour, minute, 0, TimeSpan.FromHours(8));

    private static AlarmItem Item(int minute, AlarmDays days = AlarmDays.Daily, bool enabled = true, string label = "") => new()
    {
        Id = 1,
        MinuteOfDay = minute,
        Days = days,
        Enabled = enabled,
        Label = label,
    };

    // ────────── 星期位的形状（落进存档的就是这个整数）──────────

    /// <summary>
    /// 位序<b>钉死</b>：存档里存的是这个整数，位序变了＝用户手里的"工作日闹钟"静默换成了别的几天
    /// （同批次 EV 给 ItemType 立下的口径）。数值同时钉出 Sunday＝bit0 这一条，
    /// 因为它是"跟着 DayOfWeek 走"这个决定的全部技术内容。
    /// </summary>
    [Theory]
    [InlineData(AlarmDays.None, 0)]
    [InlineData(AlarmDays.Sunday, 1)]
    [InlineData(AlarmDays.Monday, 2)]
    [InlineData(AlarmDays.Saturday, 64)]
    [InlineData(AlarmDays.Weekdays, 62)]           // 周一到周五，不含周日那一位
    [InlineData(AlarmDays.Weekends, 65)]           // 周六 + 周日
    [InlineData(AlarmDays.Daily, 127)]
    public void DayBits_ArePinned(AlarmDays days, int wire) => Assert.Equal(wire, (int)days);

    [Theory]
    [InlineData(DayOfWeek.Sunday)]
    [InlineData(DayOfWeek.Monday)]
    [InlineData(DayOfWeek.Tuesday)]
    [InlineData(DayOfWeek.Wednesday)]
    [InlineData(DayOfWeek.Thursday)]
    [InlineData(DayOfWeek.Friday)]
    [InlineData(DayOfWeek.Saturday)]
    public void Daily_MatchesEveryDay(DayOfWeek day) => Assert.True(AlarmPolicy.Matches(AlarmDays.Daily, day));

    [Fact]
    public void Weekdays_And_Weekends_SplitTheWeekWithoutOverlap()
    {
        for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
        {
            var work = AlarmPolicy.Matches(AlarmDays.Weekdays, day);
            var rest = AlarmPolicy.Matches(AlarmDays.Weekends, day);
            Assert.True(work ^ rest, $"{day} 要么落在工作日、要么落在周末，不能两头都不能两头都算");
        }
        Assert.True(AlarmPolicy.Matches(AlarmDays.Weekdays, DayOfWeek.Monday));
        Assert.False(AlarmPolicy.Matches(AlarmDays.Weekdays, DayOfWeek.Sunday));
    }

    // ────────── 今天这一轮 / 下一次 ──────────

    /// <summary>
    /// 菜单上"按星期勾"那一排只许用这两条出口改旗标（UI 里再写一遍移位＝第二份位序真值，
    /// 错的时候的症状是"勾了周三、响在周四"）。这里把两条的互逆关系钉住。
    /// </summary>
    [Fact]
    public void TurningADayOnAndOff_RoundTripsThroughTheSameBit()
    {
        var onlyWednesday = AlarmPolicy.TurnOn(AlarmDays.None, DayOfWeek.Wednesday);
        Assert.Equal(AlarmDays.Wednesday, onlyWednesday);
        Assert.True(AlarmPolicy.Matches(onlyWednesday, DayOfWeek.Wednesday));
        Assert.False(AlarmPolicy.Matches(onlyWednesday, DayOfWeek.Tuesday));

        var both = AlarmPolicy.TurnOn(onlyWednesday, DayOfWeek.Tuesday);
        Assert.Equal(AlarmDays.Tuesday | AlarmDays.Wednesday, both);
        Assert.Equal(AlarmDays.Wednesday, AlarmPolicy.TurnOff(both, DayOfWeek.Tuesday));
        Assert.Equal(AlarmDays.Tuesday, AlarmPolicy.TurnOff(both, DayOfWeek.Wednesday));
        Assert.Equal(AlarmDays.None, AlarmPolicy.TurnOff(onlyWednesday, DayOfWeek.Wednesday));
        // 勾两次不会变成两笔不同的位（旗标语义，不是计数器）
        Assert.Equal(onlyWednesday, AlarmPolicy.TurnOn(onlyWednesday, DayOfWeek.Wednesday));
    }

    [Fact]
    public void TodayOccurrence_IsThatMinuteOnTodaysLocalDate()
    {
        // 07:30 = 450 分钟；now 是 07:00 ⇒ 今天那一轮在 07:30（还没到，但"今天该响"已经成立）
        Assert.Equal(At(7, 30), AlarmPolicy.TodayOccurrence(Item(450), Tue0700()));
    }

    [Fact]
    public void TodayOccurrence_IsNull_WhenTodayIsNotOneOfTheDays()
    {
        Assert.Null(AlarmPolicy.TodayOccurrence(Item(450, AlarmDays.Monday), Tue0700()));   // 周二，档里只有周一
    }

    /// <summary>单次档"不含哪一天"这一说：它按下一个还没到的钟点算，所以每天都算候选。</summary>
    [Fact]
    public void Once_Alarm_IsStillCandidateToday()
        => Assert.Equal(At(7, 30), AlarmPolicy.TodayOccurrence(Item(450, AlarmDays.None), Tue0700()));

    [Theory]
    [InlineData(450, 29, 7, 30)]        // 今天 07:00，07:30 还没到 ⇒ 就是今天
    [InlineData(360, 30, 6, 0)]          // 今天 06:00 已过 ⇒ 明天（每天档）
    public void NextOccurrence_SkipsTheMomentAlreadyPast(int minute, int day, int wantHour, int wantMinute)
        => Assert.Equal(new DateTimeOffset(2026, 9, day, wantHour, wantMinute, 0, TimeSpan.FromHours(8)),
            AlarmPolicy.NextOccurrence(Item(minute), Tue0700()));

    [Fact]
    public void NextOccurrence_ForMondayOnly_JumpsToTheNextMonday()
        => Assert.Equal(new DateTimeOffset(2026, 10, 5, 7, 30, 0, TimeSpan.FromHours(8)),
            AlarmPolicy.NextOccurrence(Item(450, AlarmDays.Monday), Tue0700()));

    [Fact]
    public void NextOccurrence_ForOnceAlarm_NotTodayIsTomorrow()
        => Assert.Equal(new DateTimeOffset(2026, 9, 30, 6, 0, 0, TimeSpan.FromHours(8)),
            AlarmPolicy.NextOccurrence(Item(360, AlarmDays.None), Tue0700()));

    [Theory]
    [InlineData(-1)]
    [InlineData(1440)]
    [InlineData(99999)]
    public void BrokenMinute_NeverProducesAnOccurrence(int minute)
    {
        // 宁可这条永远不响，也不要按猜出来的钟点在半夜响一次：猜错的那一条没有任何出口能提前看见
        Assert.Null(AlarmPolicy.TodayOccurrence(Item(minute), Tue0700()));
        Assert.Null(AlarmPolicy.NextOccurrence(Item(minute), Tue0700()));
        Assert.False(AlarmPolicy.ShouldNotify(Item(minute), At(7, 30)));
        Assert.Equal("时间无效", AlarmPolicy.FormatMinute(minute));
    }

    // ────────── 该不该弹 ──────────

    [Fact]
    public void ShouldNotify_OnlyAfterTheMoment_InsideTheWindow()
    {
        Assert.False(AlarmPolicy.ShouldNotify(Item(450), At(7, 29)));     // 还没到
        Assert.True(AlarmPolicy.ShouldNotify(Item(450), At(7, 30)));      // 正好到：这一臂不能漏
        Assert.True(AlarmPolicy.ShouldNotify(Item(450), At(7, 44)));      // 窗口内（开机晚了十几分钟仍要弹）
        Assert.True(AlarmPolicy.ShouldNotify(Item(450), At(7, 45)));      // 窗口右端是<b>含</b>的
        Assert.False(AlarmPolicy.ShouldNotify(Item(450), At(7, 46)));     // 过期不补：错过半小时的"起床"只是打扰
    }

    [Fact]
    public void ShouldNotify_SkipsClosedAlarms_AndDaysThatDoNotMatch()
    {
        Assert.False(AlarmPolicy.ShouldNotify(Item(450, enabled: false), At(7, 30)));
        Assert.False(AlarmPolicy.ShouldNotify(Item(450, AlarmDays.Monday), At(7, 30)));   // 周二
    }

    [Fact]
    public void ShouldNotify_DoesNotRepeat_WhileThatRoundIsAwaitingConfirmation()
    {
        var item = Item(450);
        AlarmPolicy.MarkNotified(item, At(7, 30));
        Assert.False(AlarmPolicy.ShouldNotify(item, At(7, 31)));        // 同一轮：已经挂着待确认了
        // 明天那一轮是<b>另一个时刻</b>，所以"昨天那轮没确认"不该把今天这一发也压住
        Assert.True(AlarmPolicy.ShouldNotify(item, new DateTimeOffset(2026, 9, 30, 7, 31, 0, TimeSpan.FromHours(8))));
    }

    [Fact]
    public void MarkNotified_DisablesOnlyTheOnceAlarm()
    {
        var daily = Item(450);
        AlarmPolicy.MarkNotified(daily, At(7, 30));
        Assert.True(daily.Enabled);                                     // 每天档：响过还得留着
        Assert.Equal(At(7, 30), daily.PendingSince);

        var once = Item(450, AlarmDays.None);
        AlarmPolicy.MarkNotified(once, At(7, 30));
        Assert.False(once.Enabled);                                     // 单次档：叫过一回就该自己下场
        Assert.Equal(At(7, 30), once.PendingSince);                     // 但仍挂着待确认——他没确认之前不算看完
    }

    [Fact]
    public void IsPending_ExpiresAfterTwelveHours()
    {
        var item = Item(450);
        item.PendingSince = At(7, 30);
        Assert.True(AlarmPolicy.IsPending(item, At(19, 29)));            // 11 小时 59 分：还挂着
        Assert.False(AlarmPolicy.IsPending(item, At(19, 31)));           // 过 12 小时：安静收掉，别在桌上挂一天
        Assert.False(AlarmPolicy.IsPending(item, At(7, 0)));             // 时刻在未来（手改过的存档）⇒ 不算待确认
        item.PendingSince = null;
        Assert.False(AlarmPolicy.IsPending(item, At(8, 0)));
    }

    [Fact]
    public void Confirm_ClearsThePendingRound_ButKeepsTheSwitch()
    {
        var once = Item(450, AlarmDays.None);
        AlarmPolicy.MarkNotified(once, At(7, 30));
        AlarmPolicy.Confirm(once);
        Assert.Null(once.PendingSince);             // 确认关掉的是"还在等确认"
        Assert.False(once.Enabled);                 // 不是"以后不叫了"——单次档本来就自己下场了
        var daily = Item(450);
        AlarmPolicy.MarkNotified(daily, At(7, 30));
        AlarmPolicy.Confirm(daily);
        Assert.True(daily.Enabled);                 // 每天档确认之后仍然开着
    }

    // ────────── 措辞 ──────────

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(60, "01:00")]
    [InlineData(450, "07:30")]
    [InlineData(1439, "23:59")]
    public void FormatMinute_PadsToFourDigits(int minute, string want) => Assert.Equal(want, AlarmPolicy.FormatMinute(minute));

    [Theory]
    [InlineData(AlarmDays.None, "单次")]
    [InlineData(AlarmDays.Daily, "每天")]
    [InlineData(AlarmDays.Weekdays, "工作日")]
    [InlineData(AlarmDays.Weekends, "周末")]
    [InlineData(AlarmDays.Monday | AlarmDays.Wednesday | AlarmDays.Friday, "周一 周三 周五")]
    [InlineData(AlarmDays.Sunday, "周日")]
    public void DaysLabel_NamesThePresetOtherwiseListsInMondayFirstOrder(AlarmDays days, string want)
        => Assert.Equal(want, AlarmPolicy.DaysLabel(days));

    [Fact]
    public void LineOf_CarriesMinuteLabelAndDays()
    {
        Assert.Equal("07:30 起床 · 每天", AlarmPolicy.LineOf(Item(450, label: "起床")));
        Assert.Equal("06:00 · 单次", AlarmPolicy.LineOf(Item(360, AlarmDays.None)));   // 没起名就只报钟点，不硬造"闹钟"字样
    }

    [Fact]
    public void PendingLine_SaysEarliestRound_AndCountsTheRest()
    {
        Assert.Equal(string.Empty, AlarmPolicy.PendingLine(new[] { Item(450) }, At(8, 0)));   // 没弹过 ⇒ 时钟上不多一行
        var one = Item(450);
        one.PendingSince = At(7, 30);
        Assert.Equal("闹钟 07:30 待确认", AlarmPolicy.PendingLine(new[] { one }, At(8, 0)));

        var later = Item(540);
        later.PendingSince = At(9, 0);
        Assert.Equal("闹钟 07:30 等 2 条待确认", AlarmPolicy.PendingLine(new[] { later, one }, At(9, 30)));
        // 过了 12 小时的那一条不再报（桌上不该挂着上午就该确认掉的东西），剩下的那条照常报钟点
        Assert.Equal("闹钟 09:00 待确认", AlarmPolicy.PendingLine(new[] { later, one }, At(20, 0)));
    }

    [Fact]
    public void TrimLabel_CutsAtTheMenuReadableLength()
    {
        var longText = new string('字', AlarmPolicy.MaxLabelLength + 6);
        var trimmed = AlarmPolicy.TrimLabel(longText);
        Assert.Equal(AlarmPolicy.MaxLabelLength + 1, trimmed.Length);       // 截到上限再补一个省略号
        Assert.EndsWith("…", trimmed, StringComparison.Ordinal);
        Assert.Equal("短", AlarmPolicy.TrimLabel("  短  "));                 // 两头空白先去掉
    }

    // ────────── 输入解析 ──────────

    [Theory]
    [InlineData("7:30 起床", 450, "起床")]
    [InlineData("7:30起床", 450, "起床")]              // 没有空格也要认：他明明写了时间
    [InlineData("07:30", 450, "")]
    [InlineData("23:59 最后一条", 1439, "最后一条")]
    [InlineData("0:05 晨跑", 5, "晨跑")]
    public void TryParseInput_AcceptsClockThenName(string text, int wantMinute, string wantLabel)
    {
        Assert.True(AlarmPolicy.TryParseInput(text, out var minute, out var label, out var error), error);
        Assert.Equal(wantMinute, minute);
        Assert.Equal(wantLabel, label);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]                                     // 空的
    [InlineData("   ")]
    [InlineData("起床")]                                 // 只有名字
    [InlineData("7.30")]                                 // 点不是冒号
    [InlineData("7:5")]                                  // 少了分钟位
    [InlineData("7:30:15 起床")]                          // 秒不需要
    [InlineData("24:00")]                                 // 越界要说"请写 0:00"，不是"格式不对"
    public void TryParseInput_Rejects_WithSomethingActionable(string text)
    {
        Assert.False(AlarmPolicy.TryParseInput(text, out _, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>
    /// 三条拒绝要各说各的边界。<c>TimeSpan.TryParseExact</c> 会把"24:00"一并判成"格式不对"，
    /// 于是他照着提示改完还是 24 点——"我明明写了时间"却被告知写法错，是最招烦的一种红。
    /// </summary>
    [Theory]
    [InlineData("24:00", "0–23")]
    [InlineData("12:75", "0–59")]
    [InlineData("7:5", "两位")]
    [InlineData("7.30 起床", "7:30")]
    public void TryParseInput_NamesTheBoundaryItViolated(string text, string wantInError)
    {
        Assert.False(AlarmPolicy.TryParseInput(text, out _, out _, out var error));
        Assert.Contains(wantInError, error);
    }

    // ────────── 存档形状 ──────────

    /// <summary>
    /// 条目要能原样进 <c>widgets.json</c> 再原样回来（含待确认那个时刻）。
    /// <b>时刻用类型化的 <see cref="DateTimeOffset"/> 而不是字符串键</b>，就是为了这一趟往返之后
    /// "同一轮已经弹过"仍然判得对——手工格式化再解析回来那一类代码，坏的那一支通常静默变成"永远待确认"。
    /// </summary>
    [Fact]
    public void Item_RoundTripsThroughJson()
    {
        var item = Item(450, AlarmDays.Monday | AlarmDays.Friday, enabled: false, label: "起床");
        AlarmPolicy.MarkNotified(item, At(7, 30));
        var json = JsonSerializer.Serialize(item);
        var back = JsonSerializer.Deserialize<AlarmItem>(json)!;
        Assert.Equal(item.MinuteOfDay, back.MinuteOfDay);
        Assert.Equal(item.Days, back.Days);
        Assert.Equal(item.Enabled, back.Enabled);
        Assert.Equal(item.Label, back.Label);
        Assert.Equal(item.PendingSince, back.PendingSince);
        Assert.True(AlarmPolicy.IsPending(back, At(8, 0)));       // 往返之后，判据仍然认得这是同一轮
    }

    [Fact]
    public void Clone_KeepsItsOwnPendingMark()
    {
        var item = Item(450);
        item.PendingSince = At(7, 30);
        var copy = item.Clone();
        AlarmPolicy.Confirm(item);
        Assert.Null(item.PendingSince);
        Assert.Equal(At(7, 30), copy.PendingSince);               // 快照不许跟着在用的一份一起变（同倒计时那条理由）
    }
}
