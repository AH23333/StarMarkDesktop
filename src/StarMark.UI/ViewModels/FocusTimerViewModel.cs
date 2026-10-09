#nullable enable
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 番茄钟 ViewModel：持有状态机，把阶段/剩余时间翻成界面文本。
/// 全部规则在 <see cref="FocusTimer"/>（可单测），本类只做"传 now、取显示串"。
/// <para>
/// <b>取词发生在 <see cref="Refresh"/> 里</b>（批次 WI-59／P-188）：这颗宿主靠自己那一拍每秒重算，
/// 那一拍就是它的重贴 ⇒ ⛔ 挂 <c>LocText</c>、也⛔ 为它新开一个 <c>Locale.Changed</c> 订阅
/// （与 ✅WI-44 那颗时钟同一口径）。所以这里存的是<b>键</b>（<see cref="_hint"/>），
/// 而交到绑定属性上的才是那一刻的字。
/// </para>
/// </summary>
public sealed partial class FocusTimerViewModel : ObservableObject
{
    private readonly FocusTimer _timer;

    /// <summary>那颗提示的<b>身份</b>（键＋实参）：它只在事件上换，靠每秒那次重算跟着语言走。</summary>
    private LocMessage? _hint;

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _taskText = string.Empty;
    [ObservableProperty] private string _primaryText = string.Empty;
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
        StatusText = _timer.StatusMessage(now).Render();
        CountdownText = FocusTimer.Format(_timer.Remaining(now));
        PrimaryText = PrimaryMessage(_timer.Phase).Render();
        CanStop = _timer.Phase != FocusPhase.Idle;
        TaskText = _timer.TaskTitle
                   ?? (string.IsNullOrWhiteSpace(EditTitle) ? LocMessage.Of("widget.pomo.taskHint").Render() : EditTitle);
        // 那颗提示⛔ 存成品字：它只在事件上换，而换语言那一刻没人再发一次事件——
        // 存键＋每一拍重算，屏幕上那一行才跟着语言走（与上面三颗同一条路子）。
        HintText = _hint?.Render() ?? string.Empty;
    }

    /// <summary>那颗主按钮在当前阶段该说什么（<b>四颗键</b>，⛔ 在这里取词＝批次 WI-59／P-188）。</summary>
    private static LocMessage PrimaryMessage(FocusPhase phase) => phase switch
    {
        FocusPhase.Idle => LocMessage.Of("widget.pomo.btn.start"),
        FocusPhase.Paused => LocMessage.Of("widget.pomo.btn.resume"),
        FocusPhase.Resting => LocMessage.Of("widget.pomo.btn.skipRest"),
        _ => LocMessage.Of("widget.pomo.btn.pause"),
    };

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
                _hint = null;
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
        _hint = LocMessage.Of("widget.pomo.hint.stopped");
        Refresh(DateTimeOffset.Now);
    }

    /// <summary>从别处（待办条目右键「开始专注」）拉起一轮专注。返回是否真的开始。</summary>
    public bool StartExternal(string? title)
    {
        var now = DateTimeOffset.Now;
        if (_timer.Phase != FocusPhase.Idle)
        {
            // 已经在计时 ⇒ 不偷换任务，也不把 deadline 往后推，只把这件事告诉用户
            _hint = LocMessage.Of("widget.pomo.hint.runningElseStop");
            Refresh(now);
            return false;
        }
        EditTitle = title ?? string.Empty;
        var started = _timer.Start(now, title);
        // 两颗键各自写全（⛔ `Of(started ? "…" : "…")`）：把三元塞进<b>键名那一格</b>，
        // 那颗"按实参数比槽数"与"每颗键都有人问"的尺都读不到这两处调用点＝盲区（坑表 #127 那一族）。
        _hint = started ? LocMessage.Of("widget.pomo.hint.startedFromTodo")
                        : LocMessage.Of("widget.pomo.hint.cannotStart");
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
            _hint = LocMessage.Of("widget.pomo.hint.clamped", FocusTimerConfig.MinMinutes, FocusTimerConfig.MaxMinutes);
        else if (_timer.Phase is FocusPhase.Focusing or FocusPhase.Resting or FocusPhase.Paused)
            _hint = LocMessage.Of("widget.pomo.hint.nextRound");
        else
            _hint = null;

        Refresh(DateTimeOffset.Now);        // 提示存的是键，交到绑定属性上的是<b>此刻</b>的字（⛔ 在这里 Render 之外没有第二条路）
        ConfigChanged?.Invoke();
    }

    public FocusTimerConfig ToConfig() => new()
    {
        FocusMinutes = (int)FocusMinutes,
        RestMinutes = (int)RestMinutes,
    };
}
