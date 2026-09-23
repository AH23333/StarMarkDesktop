#nullable enable
using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Core.Performance;
using StarMark.Core.Widgets;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 系统监控组件：CPU / 内存 / 网速三行实时读数（可只留其中几行）。
/// <para>
/// 采样启停走 <see cref="IWidgetTicker"/>，间隔由 <see cref="SystemMonitorPolicy.SampleIntervalMs"/> 决定
/// （<b>窗口不可见＝0＝一次都不采</b>，D7）。三条通道的算术判据全在 Core 并有单测，
/// 本类只负责"什么时候采、把结果摆到哪"。
/// </para>
/// </summary>
public sealed partial class SystemMonitorWidget : UserControl, IWidgetTicker
{
    public SystemMonitorViewModel ViewModel { get; }

    private readonly WidgetManager _manager;
    private readonly string _instanceId;
    private DispatcherQueueTimer? _timer;
    private bool _ready;

    public SystemMonitorWidget(WidgetInstanceConfig config, WidgetManager manager)
    {
        _manager = manager;
        _instanceId = config.Id;
        ViewModel = new SystemMonitorViewModel(WindowMs());
        InitializeComponent();
        ViewModel.Load(config.MonitorMetrics);
        _ready = true;
    }

    /// <summary>省资源模式改的是"多久采一次"，窗长跟着间隔走（窗比间隔短等于没有平均）。</summary>
    private static int WindowMs()
        => SystemMonitorPolicy.WindowMs(SystemMonitorPolicy.SampleIntervalMs(
            windowVisible: true, PerformanceSettingsPolicy.ResourceSaverActive()));

    public void UpdateRunning(bool windowVisible)
    {
        if (!windowVisible)
        {
            _timer?.Stop();
            return;
        }
        var intervalMs = SystemMonitorPolicy.SampleIntervalMs(true, PerformanceSettingsPolicy.ResourceSaverActive());
        if (intervalMs <= 0) return;
        _timer ??= DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        _timer.Tick -= Tick;
        _timer.Tick += Tick;
        _timer.Start();
        ViewModel.Tick();   // 立刻采一次：否则刚显示出来要先空一拍
    }

    public void Stop() => _timer?.Stop();

    private void Tick(DispatcherQueueTimer sender, object args) => ViewModel.Tick();

    private void PickToggle_Click(object sender, RoutedEventArgs e)
        => PickPanel.Visibility = PickPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    /// <summary>勾选即时落盘：这类"看着改了却没存"的项必须当场生效并留住（P-54 口径）。</summary>
    private async void Pick_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        await _manager.SaveMonitorMetricsAsync(_instanceId, ViewModel.ApplyPicks());
    }
}
