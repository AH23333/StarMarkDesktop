#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 闹钟的判据（批次 RU）。全部<b>注入时刻</b>，不起表也不碰窗口——"哪一天响、什么时候算到点、
/// 待确认挂多久"这三件事每一条都只在真机上看得见，而真机验证要等一个真实的早晨，
/// 所以先把它们钉死在这里。
/// <para>批次 WI-37-C 起这一族还兼一件事：字进了表，<b>换语言那一刻是否真的整体跟着翻</b>
/// 只能在这里当场量（三档语言各读一遍再两两比对）。跑完必须把语言还回去——
/// 全工程只有这一颗订阅 <c>Locale.Changed</c> 的机制住在 UI，测试进程里换了语言不还原，
/// 下一颗读到的是别档的字（<c>DisableTestParallelization</c> 让这条可行）。</para>
/// </summary>
public sealed class AlarmPolicyTests : IDisposable
{
    private readonly AppLanguage _languageBefore = Locale.Current;

    public void Dispose() => Locale.TrySet(_languageBefore);
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

    // ────────── 时钟表面那一排（批次 VO）──────────

    /// <summary>表面那一排要的是<b>各条有自己的 id</b>（签名带 id），上面的 <see cref="Item"/> 恒等 id＝1 不够用。</summary>
    private static AlarmItem Row(long id, int minute, AlarmDays days = AlarmDays.Daily, bool enabled = true,
                                 string label = "", DateTimeOffset? pending = null)
        => new()
        {
            Id = id,
            MinuteOfDay = minute,
            Days = days,
            Enabled = enabled,
            Label = label,
            PendingSince = pending,
        };

    /// <summary>
    /// 表面的行序：<b>按钟点升序，坏值垫底但绝不被藏起来</b>。
    /// 关掉的那条照旧占一行（"关了"与"删了"是两件事，留着那条才谈得上再开），
    /// 而手改存档来的越界分钟要看得见——藏起来的后果是他以为那条还在，到第二天才发现没响。
    /// </summary>
    [Fact]
    public void FaceRows_AreInClockOrder_AndKeepBothTheClosedAndTheBroken()
    {
        var rows = AlarmPolicy.FaceRows(new[]
        {
            Row(2, 540, label: "跑步"),
            Row(1, 450, label: "起床"),
            Row(3, 60, enabled: false, label: "早课"),
            Row(4, 2000, label: "坏值"),                  // 越界：LineOf 写成"时间无效"，垫在最后
        }, At(8, 0));

        // Core 交出的是<b>还没取词的那句</b>（批次 WI-37-C）⇒ 这一格先按当时的语言（默认档＝中文）渲染再比，
        // 期望值一字未动：改的是"从哪儿拿字"，不是"字该说什么"。
        Assert.Equal(new[] { "01:00 早课 · 每天", "07:30 起床 · 每天", "09:00 跑步 · 每天", "时间无效 坏值 · 每天" },
                     rows.Select(row => row.Text.Render()).ToArray());
        Assert.Equal(new long[] { 3, 1, 2, 4 }, rows.Select(row => row.Id).ToArray());
        Assert.Equal(new[] { false, true, true, true }, rows.Select(row => row.Enabled).ToArray());
        Assert.All(rows, row => Assert.False(row.Pending));   // 谁都没弹过 ⇒ 一行"待确认"都不该有
    }

    /// <summary>到点没确认的那一条要在表面上标出来（<c>Pending</c>），确认掉就退回常态。</summary>
    [Fact]
    public void FaceRows_MarkTheRoundStillAwaitingConfirmation()
    {
        var fired = Row(1, 450, AlarmDays.None, pending: At(7, 30));
        var quiet = Row(2, 540);
        var rows = AlarmPolicy.FaceRows(new[] { quiet, fired }, At(8, 0));
        Assert.Equal(new[] { true, false }, rows.Select(row => row.Pending).ToArray());

        AlarmPolicy.Confirm(fired);
        Assert.All(AlarmPolicy.FaceRows(new[] { quiet, fired }, At(8, 0)), row => Assert.False(row.Pending));
    }

    /// <summary>
    /// 那一排"变没变"的签名<b>一次都不看现在是几点</b>：时钟每秒刷一整张表，签名里掺进钟点或倒计时，
    /// 表面就变成每秒销毁重建一遍控件（把"改一行字"升级成"一次布局重排"，代价由常驻桌面的小窗付）。
    /// 反过来，开关翻一下、改个钟点、挂上一次待确认都必须换签名——否则点了没反应。
    /// </summary>
    [Fact]
    public void FaceSignature_IgnoresTheTickingClock_ButNotAnyRealChange()
    {
        var baseline = AlarmPolicy.FaceSignature(
            AlarmPolicy.FaceRows(new[] { Row(1, 450, label: "起床"), Row(2, 540, enabled: false) }, At(8, 0)));


        // 同一套定义，时刻走到半夜：签名不许变（这一句钉的是"每秒那趟不会重画"）
        Assert.Equal(baseline, AlarmPolicy.FaceSignature(
            AlarmPolicy.FaceRows(new[] { Row(1, 450, label: "起床"), Row(2, 540, enabled: false) }, At(23, 59))));

        // 开关翻一下
        Assert.NotEqual(baseline, AlarmPolicy.FaceSignature(
            AlarmPolicy.FaceRows(new[] { Row(1, 450, label: "起床"), Row(2, 540) }, At(8, 0))));

        // 改名字／改钟点
        Assert.NotEqual(baseline, AlarmPolicy.FaceSignature(
            AlarmPolicy.FaceRows(new[] { Row(1, 450, label: "起床啦"), Row(2, 540, enabled: false) }, At(8, 0))));
        Assert.NotEqual(baseline, AlarmPolicy.FaceSignature(
            AlarmPolicy.FaceRows(new[] { Row(1, 451, label: "起床"), Row(2, 540, enabled: false) }, At(8, 0))));

        // 挂上一次待确认
        Assert.NotEqual(baseline, AlarmPolicy.FaceSignature(
            AlarmPolicy.FaceRows(new[] { Row(1, 450, label: "起床", pending: At(7, 30)), Row(2, 540, enabled: false) }, At(8, 0))));

        // 两条互不相干的闹钟不能撞签名（每一位都带上自己的源字段，不是"条数＋长度"那种退化形状）
        Assert.NotEqual(
            AlarmPolicy.FaceSignature(AlarmPolicy.FaceRows(new[] { Row(1, 450, label: "起床") }, At(8, 0))),
            AlarmPolicy.FaceSignature(AlarmPolicy.FaceRows(new[] { Row(2, 450, label: "起床") }, At(8, 0))));

        Assert.Equal(string.Empty, AlarmPolicy.FaceSignature(Array.Empty<AlarmPolicy.AlarmFaceRow>()));
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
        // 拒绝那句现在交的是键＋实参（WI-37-C），失败消息要看得见 ⇒ 当场渲染一次。
        Assert.True(AlarmPolicy.TryParseInput(text, out var minute, out var label, out var error), error?.Render());
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
        // 拒了就得有话说（键没登记时 Locale.T 会回 ⟨key⟩ 那种形状，也是一句可见的话，但下面那颗逐条点名的格会红）。
        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error.Render()));
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
        Assert.NotNull(error);
        Assert.Contains(wantInError, error.Render());
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

    // ────────── 批次 WI-37-C：字进了表，换语言那一刻必须整体跟着走 ──────────

    /// <summary>
    /// 三副星期面孔在<b>每一档语言里都取自那一档的那一列</b>（逐天逐键对账，七格一颗不漏）。
    /// <para>为什么不只测中文：中文是唯一形状的时代早已测绿过，而"外面翻了、这一排停在旧语言"
    /// 恰好只有换语言那一刻才看得见（坑表 #344）。<see cref="DateTimeText"/> 里那三张键表哪天写错一行，
    /// 屏幕上就是 ⟨time.weekday.short.3⟩ 那种形状——这颗当场红。</para>
    /// <para><see cref="Locale.TrySet"/> 返回 false 时直接红而不是跳过：读不到那一档＝这一格没量到东西，
    /// 不算过（三态口径）。</para>
    /// </summary>
    [Fact]
    public void EveryWeekdaySpellingComesFromTheColumnThatLanguageUses()
    {
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            Assert.Equal(language, Locale.Current);      // TrySet 在"原样点回去"时回 false，所以看的是当前档而不是返回值
            foreach (var day in new[]
                     {
                         DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                         DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
                     })
            {
                Assert.Equal(FromTable(DateTimeText.WeekdayKey(day)), DateTimeText.Weekday(day));
                Assert.Equal(FromTable(DateTimeText.WeekdayShortKey(day)), DateTimeText.WeekdayShort(day));
                Assert.Equal(FromTable(DateTimeText.WeekdayStemKey(day)), DateTimeText.WeekdayStem(day));
            }
        }
    }

    /// <summary>
    /// 闹钟那一整行：档位那句跟着语言走，<b>用户自己打的名字一颗都不许动</b>。
    /// <para>后半条是这一族的边界：把「起床」也送进翻译，屏幕上会出现没人起过的那句话；
    /// 反过来若整行停在中文，就是"外面翻了、这一排没翻"（真机报的第三条同一形状）。</para>
    /// <para>⚠ "三档读数两两不同"必须比<b>档位那句本身</b>，⛔ 只比整行：台架 A3 把 `alarm.days.weekdays`
    /// 的日文列抄成中文列，整行仍然两两不同（`alarm.line` 三列的分隔符本来就不同：中文 <c> · </c>、日文 <c>・</c>），
    /// 于是那一臂红在别的证人上而我这颗绿——**尺子读不到 ≠ 缺陷不存在，先换尺子再归责**。</para>
    /// </summary>
    [Fact]
    public void TheAlarmLineFollowsTheLanguage_ButNeverTheUsersOwnName()
    {
        var dayWords = new HashSet<string>(StringComparer.Ordinal);
        var lines = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            Assert.Equal(language, Locale.Current);      // TrySet 在"原样点回去"时回 false，所以看的是当前档而不是返回值
            var days = FromTable("alarm.days.weekdays");
            Assert.Equal(days, AlarmPolicy.DaysLabel(AlarmDays.Weekdays));
            Assert.True(dayWords.Add(days),
                $"星期档那句在 {language} 档与前面某档逐字相同 ⇒ 那一列抄了别档（表里三列分不开，屏幕上就是半中半日）：{days}");

            var line = AlarmPolicy.LineOf(Item(450, AlarmDays.Weekdays, label: "起床"));
            Assert.StartsWith("07:30", line, StringComparison.Ordinal);   // 钟点是数字，不参与翻译
            Assert.Contains("起床", line, StringComparison.Ordinal);        // 名字是用户打的字
            Assert.Contains(days, line, StringComparison.Ordinal);         // 档位那句取自当前语言那一列
            Assert.True(lines.Add(line), $"整行在 {language} 档与前面某档逐字相同 ⇒ 这一族没接到表上：{line}");
        }
        Assert.Equal(3, dayWords.Count);                  // 三档都真进过循环（⛔ 空循环冒充绿灯）
        Assert.Equal(3, lines.Count);
    }

    /// <summary>
    /// 星期档那十颗键（四档预设＋六档逐天组合）与<b>真能走到的那些组合</b>当场对账，两个方向都比。
    /// <para>这是"表里这一行没人问"那颗闸门<b>看不见</b>的一半：<c>PickKeys</c> 里每颗键名都成串，
    /// 死键闸门因此永远绿；可"勾满七天"就是「每天」那颗预设，<b>pick7 没有任何输入能走到</b>。
    /// 留着的后果是那一行谎报覆盖面（有人照着表数七档，实际只有六档）。穷举 128 种组合法，⛔ 抽样。</para>
    /// </summary>
    [Fact]
    public void DaysMessage_CoversExactlyTheRowsARealCombinationCanReach()
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        for (var raw = 0; raw <= (int)AlarmDays.Daily; raw++)
            reached.Add(AlarmPolicy.DaysMessage((AlarmDays)raw).Key);

        Assert.Equal(
            new[]
            {
                "alarm.days.daily", "alarm.days.once", "alarm.days.pick1", "alarm.days.pick2", "alarm.days.pick3",
                "alarm.days.pick4", "alarm.days.pick5", "alarm.days.pick6", "alarm.days.weekdays", "alarm.days.weekends",
            },
            reached.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// 一条组合句里的<b>实参颗数＝勾了几格</b>，次序＝周一在前，每颗实参交的是<b>短名那颗键</b>（不是成品字）。
    /// <para>⛔ 一条七槽模板拿空格填空：那会在"只勾两天"时留下看不见的空格，而空格的宽度在窄列里就是"那一行怪怪的"。
    /// 名字交给表 ⇒ 这里只许核对形状，一颗字都不许比对（比对字就等于又写了一份真值）。</para>
    /// </summary>
    [Fact]
    public void DaysMessage_CarriesOneArgumentPerCheckedDay_InReadingOrder()
    {
        var combos = 0;
        for (var raw = 0; raw <= (int)AlarmDays.Daily; raw++)
        {
            var message = AlarmPolicy.DaysMessage((AlarmDays)raw);
            if (!message.Key.StartsWith("alarm.days.pick", StringComparison.Ordinal)) continue;
            var bits = System.Numerics.BitOperations.PopCount((uint)raw);
            Assert.Equal($"alarm.days.pick{bits}", message.Key);   // 键按条数选，一位一槽
            Assert.Equal(bits, message.Args!.Length);              // 勾几格就几个实参
            var want = new List<string>();
            for (var offset = 0; offset < 7; offset++)             // 阅读顺序：周一在前
            {
                var day = (DayOfWeek)((offset + 1) % 7);
                if ((raw & (1 << (int)day)) != 0) want.Add(DateTimeText.WeekdayShortKey(day));
            }
            Assert.Equal(want, message.Args.Select(a => ((LocMessage)a!).Key));
            combos++;
        }
        Assert.True(combos >= 120, $"逐天组合那一路只走了 {combos} 种（128 种里去掉四档预设应有 124 种）——循环本身坏了");
    }

    /// <summary>
    /// <b>同一份定义在三档语言里给出同一串签名</b>：时钟每秒问一次"要不要重画那一排"，
    /// 签名掺进成品字就等于"换一次语言把他正盯着的那几颗控件销毁重建一遍"（常驻小窗为此多付一次布局重排，
    /// 而换语言那一刻该由 <c>LocText</c> 重贴，见 <see cref="AlarmWiringGateTests"/>）。
    /// <para>他起的名字<b>照旧进签名</b>（那是内容，改了当然要重画），所以这一格只许比对<b>表里那三列的词</b>：
    /// 它们一颗都不许出现。</para>
    /// </summary>
    [Fact]
    public void FaceSignature_IsTheSameStringInEveryLanguage()
    {
        var items = new[] { Item(450, AlarmDays.Weekdays, label: "起床"), Item(420, AlarmDays.None) };
        var readings = new List<string>();
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            Assert.Equal(language, Locale.Current);      // TrySet 在"原样点回去"时回 false，所以看的是当前档而不是返回值
            readings.Add(AlarmPolicy.FaceSignature(AlarmPolicy.FaceRows(items, At(8, 0))));
        }

        Assert.Equal(readings[0], readings[1], StringComparer.Ordinal);
        Assert.Equal(readings[0], readings[2], StringComparer.Ordinal);
        Assert.Contains("alarm.line", readings[0], StringComparison.Ordinal);        // 进串的是键，不是成品句
        Assert.Contains("起床", readings[0], StringComparison.Ordinal);              // 内容变了仍然要重画
        foreach (var word in new[] { "工作日", "每天", "单次", "Weekdays", "Daily", "Once", "平日", "毎日" })
            Assert.DoesNotContain(word, readings[0], StringComparison.Ordinal);
    }

    /// <summary>按<b>当前</b>那一档去表里取对应的列（对账用；取不到键直接红，不许静默落到 ⟨键⟩）。</summary>
    private static string FromTable(string key)
    {
        var entry = UiStrings.All.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));
        Assert.True(entry is not null, $"表里没有这一行：{key}");
        return Locale.Current switch
        {
            AppLanguage.English => entry!.En,
            AppLanguage.Japanese => entry!.Ja,
            _ => entry!.Zh,
        };
    }
}
