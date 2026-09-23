#nullable enable
using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        if (App.MainWindow?.TryShowTrayNotification("番茄钟 · " + title, body) != true)
            StarLog.Info($"番茄钟提醒（托盘未启用，仅组件内显示）：{title} — {body}");
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
