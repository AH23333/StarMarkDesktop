#nullable enable
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 番茄钟状态机测试：绝对 deadline 推进、暂停不消耗、阶段接续、睡眠唤醒后的过期宽限、
/// 防重入、读数格式化。
/// </summary>
public sealed class FocusTimerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 23, 9, 0, 0, TimeSpan.FromHours(8));

    private static FocusTimer New(int focus = 25, int rest = 5)
        => new(new FocusTimerConfig { FocusMinutes = focus, RestMinutes = rest });

    [Fact]
    public void Start_SetsFocusingAndFullDeadline()
    {
        var t = New();
        Assert.True(t.Start(T0, "写方案"));
        Assert.Equal(FocusPhase.Focusing, t.Phase);
        Assert.Equal("写方案", t.TaskTitle);
        Assert.Equal(TimeSpan.FromMinutes(25), t.Remaining(T0));
        Assert.Equal(TimeSpan.FromMinutes(20), t.Remaining(T0.AddMinutes(5)));
    }

    [Fact]
    public void Start_IsIgnoredWhileRunning_InsteadOfPushingDeadline()
    {
        // 连点两下"开始"不该把 25 分钟推成"从第二下算起的 25 分钟"
        var t = New();
        Assert.True(t.Start(T0, "A"));
        Assert.False(t.Start(T0.AddMinutes(3), "B"));
        Assert.Equal("A", t.TaskTitle);
        Assert.Equal(TimeSpan.FromMinutes(22), t.Remaining(T0.AddMinutes(3)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void BlankTitle_BecomesNull(string? title)
    {
        var t = New();
        t.Start(T0, title);
        Assert.Null(t.TaskTitle);
    }

    [Fact]
    public void Tick_AtDeadline_SwitchesToRest()
    {
        var t = New();
        t.Start(T0, "A");
        var r = t.Tick(T0.AddMinutes(25));
        Assert.True(r.PhaseEnded);
        Assert.Equal(FocusPhase.Focusing, r.EndedPhase);
        Assert.False(r.RestSkippedAsStale);
        Assert.Equal(FocusPhase.Resting, t.Phase);
        Assert.Equal(TimeSpan.FromMinutes(5), t.Remaining(T0.AddMinutes(25)));
    }

    [Fact]
    public void Tick_RestEnds_GoesBackToIdle()
    {
        var t = New();
        t.Start(T0);
        t.Tick(T0.AddMinutes(25));                       // → 休息
        var r = t.Tick(T0.AddMinutes(30));               // 休息结束
        Assert.True(r.PhaseEnded);
        Assert.Equal(FocusPhase.Resting, r.EndedPhase);
        Assert.Equal(FocusPhase.Idle, t.Phase);
        Assert.Null(t.TaskTitle);
    }

    [Fact]
    public void Tick_BeforeDeadline_DoesNotReportAnything()
    {
        var t = New();
        t.Start(T0);
        Assert.False(t.Tick(T0.AddMinutes(24).AddSeconds(50)).PhaseEnded);
        Assert.Equal(FocusPhase.Focusing, t.Phase);
    }

    [Fact]
    public void WakingFromSleep_DoesNotStartAStaleRest()
    {
        // 机器睡了 3 小时才第一次 Tick 到：该告诉用户"专注结束了"，但不该凭空塞进一段 5 分钟休息
        var t = New();
        t.Start(T0);
        var r = t.Tick(T0.AddMinutes(25) + FocusTimer.StaleGrace + TimeSpan.FromMinutes(1));
        Assert.True(r.PhaseEnded);
        Assert.True(r.RestSkippedAsStale);
        Assert.Equal(FocusPhase.Idle, t.Phase);
    }

    [Fact]
    public void StaleGrace_StillRestsInsideTheWindow()
    {
        var t = New();
        t.Start(T0);
        var r = t.Tick(T0.AddMinutes(25) + TimeSpan.FromSeconds(10));
        Assert.False(r.RestSkippedAsStale);
        Assert.Equal(FocusPhase.Resting, t.Phase);
    }

    [Fact]
    public void Pause_FreezesRemaining_Resume_RestartsWithIt()
    {
        var t = New();
        t.Start(T0);
        Assert.True(t.Pause(T0.AddMinutes(10)));
        Assert.Equal(FocusPhase.Paused, t.Phase);
        Assert.Equal(TimeSpan.FromMinutes(15), t.Remaining(T0.AddMinutes(10)));
        // 暂停 1 小时后再看，剩余不该被吃掉
        Assert.Equal(TimeSpan.FromMinutes(15), t.Remaining(T0.AddHours(1)));
        Assert.False(t.Tick(T0.AddHours(2)).PhaseEnded);

        Assert.True(t.Resume(T0.AddHours(2)));
        Assert.Equal(FocusPhase.Focusing, t.Phase);
        Assert.Equal(TimeSpan.FromMinutes(15), t.Remaining(T0.AddHours(2)));
        Assert.True(t.Tick(T0.AddHours(2).AddMinutes(15)).PhaseEnded);
    }

    [Fact]
    public void PauseAndResume_AreNoOpsWhenNotApplicable()
    {
        var t = New();
        Assert.False(t.Pause(T0));      // 空闲时无可暂停
        Assert.False(t.Resume(T0));
        t.Start(T0);
        Assert.False(t.Resume(T0));     // 没暂停过，继续不该凭空造出时间
    }

    [Theory]
    [InlineData(FocusPhase.Idle)]
    [InlineData(FocusPhase.Focusing)]
    [InlineData(FocusPhase.Resting)]
    [InlineData(FocusPhase.Paused)]
    public void Skip_AlwaysLeavesSomethingActionable(FocusPhase from)
    {
        var t = New();
        if (from is not FocusPhase.Idle) t.Start(T0);
        if (from is FocusPhase.Resting or FocusPhase.Paused) t.Tick(T0.AddMinutes(25));
        if (from == FocusPhase.Paused) t.Pause(T0.AddMinutes(26));

        var after = t.Skip(T0.AddMinutes(30));
        Assert.True(after is FocusPhase.Idle or FocusPhase.Resting);
        // 从"专注"跳过 ⇒ 进休息；其余 ⇒ 直接空闲
        Assert.Equal(from == FocusPhase.Focusing ? FocusPhase.Resting : FocusPhase.Idle, after);
    }

    [Fact]
    public void Remaining_NeverGoesNegative()
    {
        var t = New();
        t.Start(T0);
        // 越过 deadline 很久且没走到 Tick（比如窗口一直隐藏、定时器停着）：读数只能是 0，不能是负数
        Assert.Equal(TimeSpan.Zero, t.Remaining(T0.AddHours(5)));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(1, "00:01")]
    [InlineData(65, "01:05")]
    [InlineData(1500, "25:00")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3661, "1:01:01")]
    public void Format_ReadsLikeAClock(int seconds, string expected)
        => Assert.Equal(expected, FocusTimer.Format(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Format_RoundsSubSecondUp_NotDown()
    {
        // 0.2 秒显示成 00:01：倒数到 00:00 还没结束会让人以为卡住
        Assert.Equal("00:01", FocusTimer.Format(TimeSpan.FromMilliseconds(200)));
    }

    [Theory]
    [InlineData(FocusPhase.Idle, "空闲")]
    [InlineData(FocusPhase.Focusing, "专注")]
    [InlineData(FocusPhase.Resting, "休息")]
    [InlineData(FocusPhase.Paused, "已暂停")]
    public void PhaseLabel_CoversEveryPhase(FocusPhase phase, string expected)
        => Assert.Equal(expected, FocusTimer.PhaseLabel(phase));

    [Fact]
    public void StatusLine_TellsTheTruthPerPhase()
    {
        var t = New();
        Assert.Equal("准备开始一轮专注", t.StatusLine(T0));
        t.Start(T0);
        Assert.Equal("专注中 · 剩 25:00", t.StatusLine(T0));
        t.Pause(T0.AddMinutes(5));
        Assert.Equal("已暂停（剩 20:00）", t.StatusLine(T0.AddMinutes(5)));
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(25, 25)]
    [InlineData(9999, 180)]
    public void Config_ClampsIntoTheSaneRange(int input, int expected)
    {
        var c = new FocusTimerConfig();
        Assert.Equal(expected, c.ClampFocus(input));
        Assert.Equal(expected, c.ClampRest(input));
    }

    [Fact]
    public void Config_Defaults_AreTheClassicPomodoro()
    {
        var c = new FocusTimerConfig();
        Assert.Equal(25, c.FocusMinutes);
        Assert.Equal(5, c.RestMinutes);
    }

    [Fact]
    public void Config_RoundTripsThroughJson()
    {
        // 这份配置会进 widgets.json：字段名与类型必须能被原样读回
        var c = new FocusTimerConfig { FocusMinutes = 50, RestMinutes = 10 };
        var back = System.Text.Json.JsonSerializer.Deserialize<FocusTimerConfig>(
            System.Text.Json.JsonSerializer.Serialize(c))!;
        Assert.Equal(50, back.FocusMinutes);
        Assert.Equal(10, back.RestMinutes);
    }
}
