#nullable enable

namespace StarMark.Core.Widgets;

/// <summary>番茄钟阶段。</summary>
public enum FocusPhase
{
    /// <summary>空闲：没在计时。</summary>
    Idle,
    /// <summary>专注中。</summary>
    Focusing,
    /// <summary>暂停：保留剩余时间，不消耗。</summary>
    Paused,
    /// <summary>休息中。</summary>
    Resting,
}

/// <summary>番茄钟时长设置（随实例持久化；统计量刻意不持久化——第一版不做统计）。</summary>
public sealed class FocusTimerConfig
{
    public int FocusMinutes { get; set; } = DefaultFocusMinutes;
    public int RestMinutes { get; set; } = DefaultRestMinutes;

    public const int DefaultFocusMinutes = 25;
    public const int DefaultRestMinutes = 5;

    public const int MinMinutes = 1;
    public const int MaxMinutes = 180;

    /// <summary>夹到合法区间。界面数字框可以随便填，状态机不该收到 0 或负数。</summary>
    public int ClampFocus(int minutes) => Math.Clamp(minutes, MinMinutes, MaxMinutes);

    public int ClampRest(int minutes) => Math.Clamp(minutes, MinMinutes, MaxMinutes);
}

/// <summary>一次 Tick 的产出：是否有阶段刚刚结束（界面据此提醒并落盘），以及结束的是哪一个。</summary>
public readonly record struct FocusTick(bool PhaseEnded, FocusPhase EndedPhase, bool RestSkippedAsStale);

/// <summary>
/// 番茄钟状态机（纯逻辑，时间由调用方传入 ⇒ 可在测试里推进）。
/// <para>
/// 用<b>绝对截止时刻</b>而不是"每秒减一"：桌面组件的定时器会被节流、窗口会隐藏、机器会睡眠，
/// 累减法的误差与这些行为同源；记一个 deadline 则任何一次 Tick 都能算出真实剩余。
/// </para>
/// <para>
/// 睡眠唤醒后可能出现"专注早已结束、现在才第一次 Tick 到"。此时仍报阶段结束（用户该知道时间到了），
/// 但<b>不再自动进入休息</b>——醒来才发现要休息 5 分钟没有意义，超过 <see cref="StaleGrace"/> 直接回空闲。
/// </para>
/// </summary>
public sealed class FocusTimer
{
    /// <summary>阶段结束后仍允许"顺接续做"的宽限期；超出则视为早已过去（睡眠/关机）。</summary>
    public static readonly TimeSpan StaleGrace = TimeSpan.FromMinutes(5);

    private readonly FocusTimerConfig _config;
    private DateTimeOffset _deadline;
    private TimeSpan _pausedRemaining;
    private FocusPhase _pausedFrom = FocusPhase.Focusing;

    public FocusTimer(FocusTimerConfig config) => _config = config;

    public FocusPhase Phase { get; private set; } = FocusPhase.Idle;

    /// <summary>本轮在专注什么（可为空）。第一版只带这一条文本，不做与待办表的绑定。</summary>
    public string? TaskTitle { get; private set; }

    /// <summary>剩余时间；空闲时为对应阶段的完整时长（界面显示"下一轮会是多少"）。</summary>
    public TimeSpan Remaining(DateTimeOffset now) => Phase switch
    {
        FocusPhase.Paused => _pausedRemaining,
        FocusPhase.Idle => TimeSpan.FromMinutes(_config.FocusMinutes),
        _ => MaxZero(_deadline - now),
    };

    public static TimeSpan MaxZero(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;

    /// <summary>阶段名（界面/提醒共用一处措辞，避免两个书写点）。</summary>
    public static string PhaseLabel(FocusPhase phase) => phase switch
    {
        FocusPhase.Focusing => "专注",
        FocusPhase.Resting => "休息",
        FocusPhase.Paused => "已暂停",
        _ => "空闲",
    };

    public string StatusLine(DateTimeOffset now) => Phase switch
    {
        FocusPhase.Idle => "准备开始一轮专注",
        FocusPhase.Paused => $"已暂停（剩 {Format(Remaining(now))}）",
        _ => $"{PhaseLabel(Phase)}中 · 剩 {Format(Remaining(now))}",
    };

    /// <summary>计时读数 mm:ss；超过一小时才带小时位（休息/常规专注都不该出现，出现说明上限设得太宽）。</summary>
    public static string Format(TimeSpan t)
    {
        var total = (int)Math.Ceiling(t.TotalSeconds);
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }

    /// <summary>开始一轮专注。已在计时中则忽略（防重入：连点两下不该把 deadline 往后推）。</summary>
    public bool Start(DateTimeOffset now, string? title = null)
    {
        if (Phase is FocusPhase.Focusing or FocusPhase.Resting or FocusPhase.Paused) return false;
        TaskTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Phase = FocusPhase.Focusing;
        _deadline = now.AddMinutes(_config.FocusMinutes);
        return true;
    }

    /// <summary>暂停：记下剩余量。非计时态无效。</summary>
    public bool Pause(DateTimeOffset now)
    {
        if (Phase is not (FocusPhase.Focusing or FocusPhase.Resting)) return false;
        _pausedFrom = Phase;
        _pausedRemaining = MaxZero(_deadline - now);
        Phase = FocusPhase.Paused;
        return true;
    }

    /// <summary>继续：从暂停点重算 deadline（不是"接上原来的绝对时刻"，那样暂停的时间也会被算进去）。</summary>
    public bool Resume(DateTimeOffset now)
    {
        if (Phase != FocusPhase.Paused) return false;
        Phase = _pausedFrom;
        _deadline = now + _pausedRemaining;
        return true;
    }

    /// <summary>手动结束当前阶段：专注→进休息；其余（休息/暂停/空闲）→回空闲。</summary>
    public FocusPhase Skip(DateTimeOffset now)
    {
        if (Phase == FocusPhase.Focusing) EnterRest(now);
        else Reset();
        return Phase;
    }

    /// <summary>彻底停止并清空本轮。</summary>
    public void Reset()
    {
        Phase = FocusPhase.Idle;
        TaskTitle = null;
        _pausedRemaining = TimeSpan.Zero;
        _pausedFrom = FocusPhase.Focusing;
    }

    private void EnterRest(DateTimeOffset now)
    {
        Phase = FocusPhase.Resting;
        _deadline = now.AddMinutes(_config.RestMinutes);
    }

    /// <summary>
    /// 推进状态机。返回本轮是否刚跨过阶段边界——跨过就要提醒，并且要落盘（虽然本版本不落统计，
    /// 但调用方需要知道"这一刻有事件发生"以便提醒）。
    /// </summary>
    public FocusTick Tick(DateTimeOffset now)
    {
        if (Phase is not (FocusPhase.Focusing or FocusPhase.Resting)) return new FocusTick(false, Phase, false);
        if (now < _deadline) return new FocusTick(false, Phase, false);

        var ended = Phase;
        var stale = now - _deadline > StaleGrace;
        if (ended == FocusPhase.Focusing && !stale) EnterRest(now);
        else Reset();
        // 休息自然结束、或专注过期太久而没接续休息 ⇒ 都回空闲
        return new FocusTick(true, ended, ended == FocusPhase.Focusing && stale);
    }
}
