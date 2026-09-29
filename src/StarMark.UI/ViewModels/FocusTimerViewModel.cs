#nullable enable
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Core.Widgets;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 番茄钟 ViewModel：持有状态机，把阶段/剩余时间翻成界面文本。
/// 全部规则在 <see cref="FocusTimer"/>（可单测），本类只做"传 now、取显示串"。
/// </summary>
public sealed partial class FocusTimerViewModel : ObservableObject
{
    private readonly FocusTimer _timer;

    [ObservableProperty] private string _statusText = "准备开始一轮专注";
    [ObservableProperty] private string _taskText = "这一轮在做什么？可留空";
    [ObservableProperty] private string _primaryText = "开始专注";
    [ObservableProperty] private string _hintText = string.Empty;
    [ObservableProperty] private bool _canStop;
    [ObservableProperty] private string _countdownText = "--:--";
    [ObservableProperty] private double _focusMinutes;
    [ObservableProperty] private double _restMinutes;

    /// <summary>阶段刚结束（提醒 + 落盘设置的时机）。</summary>
    public event Action<FocusPhase, bool>? PhaseFinished;

    /// <summary>时长数字框改动（松手/步进后触发一次，组件据此保存设置）。</summary>
    public event Action? ConfigChanged;

    public FocusTimerViewModel(FocusTimerConfig config)
    {
        _timer = new FocusTimer(config);
        _focusMinutes = config.FocusMinutes;
        _restMinutes = config.RestMinutes;
    }

    public string? TaskTitle => _timer.TaskTitle;

    /// <summary>
    /// 推进一秒。到点时翻文本、发提醒事件——提醒通道由组件决定（右下角提示卡 + 界面常驻状态），
    /// 与倒计时那一批同一口径。
    /// </summary>
    public void Tick()
    {
        var now = DateTimeOffset.Now;
        var ended = _timer.Tick(now);
        Refresh(now);
        if (ended.PhaseEnded) PhaseFinished?.Invoke(ended.EndedPhase, ended.RestSkippedAsStale);
    }

    public void Refresh(DateTimeOffset now)
    {
        StatusText = _timer.StatusLine(now);
        CountdownText = FocusTimer.Format(_timer.Remaining(now));
        PrimaryText = _timer.Phase switch
        {
            FocusPhase.Idle => "开始专注",
            FocusPhase.Paused => "继续",
            FocusPhase.Resting => "跳过休息",
            _ => "暂停",
        };
        CanStop = _timer.Phase != FocusPhase.Idle;
        TaskText = _timer.TaskTitle
                   ?? (string.IsNullOrWhiteSpace(EditTitle) ? "这一轮在做什么？可留空" : EditTitle);
    }

    /// <summary>用户在输入框里写的本轮任务名（空闲/暂停态可改）。</summary>
    public string EditTitle { get; set; } = string.Empty;

    /// <summary>主按钮：按当前阶段做该做的事。</summary>
    public void Primary()
    {
        var now = DateTimeOffset.Now;
        switch (_timer.Phase)
        {
            case FocusPhase.Idle:
                _timer.Start(now, EditTitle);
                HintText = string.Empty;
                break;
            case FocusPhase.Focusing or FocusPhase.Resting:
                _timer.Pause(now);
                break;
            case FocusPhase.Paused:
                _timer.Resume(now);
                break;
        }
        Refresh(now);
    }

    /// <summary>提前结束当前阶段：专注→立刻进休息；其余→停止。</summary>
    public void Skip()
    {
        var now = DateTimeOffset.Now;
        _timer.Skip(now);
        Refresh(now);
    }

    public void Stop()
    {
        _timer.Reset();
        EditTitle = string.Empty;
        Refresh(DateTimeOffset.Now);
        HintText = "已停止这一轮";
    }

    /// <summary>从别处（待办条目右键「开始专注」）拉起一轮专注。返回是否真的开始。</summary>
    public bool StartExternal(string? title)
    {
        var now = DateTimeOffset.Now;
        if (_timer.Phase != FocusPhase.Idle)
        {
            // 已经在计时 ⇒ 不偷换任务，也不把 deadline 往后推，只把这件事告诉用户
            HintText = "这一轮还在跑，先停止才能换任务";
            Refresh(now);
            return false;
        }
        EditTitle = title ?? string.Empty;
        var started = _timer.Start(now, title);
        HintText = started ? "已按待办开始一轮专注" : "没能开始（状态异常）";
        Refresh(now);
        return started;
    }

    /// <summary>
    /// 数字框回写。三件事一次说清：<b>越界就被夹</b>（并把结果告诉用户，静默改值最容易被读成"没生效"）、
    /// <b>正在计时中改了不本轮生效</b>（deadline 在开始那一刻就定下了，不做中途改长度这种半截语义）、
    /// 其余情况清掉提示。
    /// </summary>
    public void ApplyDurations(double focus, double rest)
    {
        var rawFocus = (int)Math.Round(focus);
        var rawRest = (int)Math.Round(rest);
        var f = Math.Clamp(rawFocus, FocusTimerConfig.MinMinutes, FocusTimerConfig.MaxMinutes);
        var r = Math.Clamp(rawRest, FocusTimerConfig.MinMinutes, FocusTimerConfig.MaxMinutes);
        FocusMinutes = f;
        RestMinutes = r;

        if (rawFocus != f || rawRest != r)
            HintText = $"时长只收 {FocusTimerConfig.MinMinutes}–{FocusTimerConfig.MaxMinutes} 分钟，已按边界取值";
        else if (_timer.Phase is FocusPhase.Focusing or FocusPhase.Resting or FocusPhase.Paused)
            HintText = "新时长下一轮生效";
        else
            HintText = string.Empty;

        ConfigChanged?.Invoke();
    }

    public FocusTimerConfig ToConfig() => new()
    {
        FocusMinutes = (int)FocusMinutes,
        RestMinutes = (int)RestMinutes,
    };
}
