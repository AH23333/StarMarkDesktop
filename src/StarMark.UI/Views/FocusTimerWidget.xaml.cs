#nullable enable
using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 番茄钟组件：25+5 可自定义、可带本轮任务名、阶段结束提醒。
/// <para>
/// 第一版刻意<b>不做统计持久化</b>（发起人 2026-09-23 裁决），因此关窗口/重启会回到空闲——
/// 界面显示的"准备开始一轮专注"就是真实状态，不存在"看起来还在跑其实已经没了"那种假象。
/// </para>
/// </summary>
public sealed partial class FocusTimerWidget : UserControl, IWidgetTicker
{
    public FocusTimerViewModel ViewModel { get; }

    private readonly WidgetManager _manager;
    private readonly string _instanceId;
    private DispatcherQueueTimer? _timer;
    private bool _ready;

    public FocusTimerWidget(WidgetInstanceConfig config, WidgetManager manager)
    {
        var settings = config.Focus ?? new FocusTimerConfig();
        ViewModel = new FocusTimerViewModel(settings);
        _manager = manager;
        _instanceId = config.Id;
        InitializeComponent();
        ViewModel.ConfigChanged += OnConfigChanged;
        ViewModel.PhaseFinished += OnPhaseFinished;
        ViewModel.Refresh(DateTimeOffset.Now);
        // NumberBox 的 Value 是 OneWay 绑定的，构造后直接把初始值写进控件，避免首帧显示空
        FocusBox.Value = ViewModel.FocusMinutes;
        RestBox.Value = ViewModel.RestMinutes;
        _ready = true;
    }

    /// <summary>供「待办条目右键 → 开始专注这个待办」调用。返回是否真的开始了一轮。</summary>
    public bool StartFromTodo(string? title) => ViewModel.StartExternal(title);

    private void OnConfigChanged()
    {
        if (!_ready) return;
        _ = _manager.SaveFocusConfigAsync(_instanceId, ViewModel.ToConfig());
    }

    private void OnPhaseFinished(FocusPhase ended, bool restSkippedAsStale)
    {
        var title = ended == FocusPhase.Focusing ? "专注结束" : "休息结束";
        var body = ended == FocusPhase.Focusing
            ? (restSkippedAsStale ? "时间早已到点（机器可能睡眠过），不自动进休息" : "该休息一下了")
            : "休息结束，可以开始下一轮";
        if (!NoticeCard.Show("番茄钟 · " + title, body))
            StarLog.Info($"番茄钟提醒（提示卡没能贴上屏幕，仅组件内显示）：{title} — {body}");
    }

    private void Primary_Click(object sender, RoutedEventArgs e) => ViewModel.Primary();

    private void Stop_Click(object sender, RoutedEventArgs e) => ViewModel.Stop();

    private void FocusBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_ready) return;
        // double.NaN 是 NumberBox 清空文本时的值，不是"用户要 0 分钟"
        ViewModel.ApplyDurations(double.IsNaN(args.NewValue) ? ViewModel.FocusMinutes : args.NewValue,
            double.IsNaN(RestBox.Value) ? ViewModel.RestMinutes : RestBox.Value);
    }

    private void RestBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_ready) return;
        ViewModel.ApplyDurations(double.IsNaN(FocusBox.Value) ? ViewModel.FocusMinutes : FocusBox.Value,
            double.IsNaN(args.NewValue) ? ViewModel.RestMinutes : args.NewValue);
    }

    /// <summary>
    /// 滚轮微调（去掉上下按钮后的第二条调节路径，用户明确要求"直接输入或滚轮"）。
    /// WinUI 的 NumberBox 不原生响应滚轮 ⇒ 在控件上显式接一次，按 SmallChange 步进并夹进 Min/Max。
    /// </summary>
    private void FocusBox_PointerWheelChanged(object sender, PointerRoutedEventArgs e) => WheelStep(FocusBox, e);

    private void RestBox_PointerWheelChanged(object sender, PointerRoutedEventArgs e) => WheelStep(RestBox, e);

    private static void WheelStep(NumberBox box, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(box).Properties.MouseWheelDelta;
        if (delta == 0) return;
        var step = double.IsNaN(box.SmallChange) || box.SmallChange == 0 ? 1 : box.SmallChange;
        var current = double.IsNaN(box.Value) ? box.Minimum : box.Value;
        box.Value = Math.Clamp(Math.Round(current + (delta > 0 ? step : -step)), box.Minimum, box.Maximum);
        e.Handled = true;
    }

    public void UpdateRunning(bool windowVisible)
    {
        if (windowVisible)
        {
            _timer ??= DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick -= Tick;
            _timer.Tick += Tick;
            _timer.Start();
            ViewModel.Tick();
        }
        else
        {
            _timer?.Stop();
        }
    }

    public void Stop() => _timer?.Stop();

    private void Tick(DispatcherQueueTimer sender, object args) => ViewModel.Tick();
}
