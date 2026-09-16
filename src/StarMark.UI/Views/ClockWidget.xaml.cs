#nullable enable
using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 时钟组件（R3 收尾）：XAML + ViewModel，原 WidgetWindow.BuildClock() 的手工构建
/// 与每秒定时器一并迁入本组件。宿主窗口持有可见性状态，在 Reveal / HideTemporary /
/// AppWindow 可见性变化时通过 <see cref="UpdateRunning"/> 启停刷新，关闭时调用
/// <see cref="Stop"/>；隐藏期间停表（与原行为一致，常驻应用省电）。
/// </summary>
public sealed partial class ClockWidget : UserControl
{
    public ClockWidgetViewModel ViewModel { get; }

    private DispatcherQueueTimer? _timer;

    public ClockWidget()
    {
        ViewModel = new ClockWidgetViewModel();
        InitializeComponent();
        Unloaded += (_, _) => Stop();
    }

    /// <summary>按宿主窗口是否可见启停每秒刷新；启动时立即校准一次显示。</summary>
    public void UpdateRunning(bool windowVisible)
    {
        if (windowVisible)
        {
            _timer ??= DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick -= Timer_Tick;
            _timer.Tick += Timer_Tick;
            _timer.Start();
            ViewModel.Update();
        }
        else
        {
            _timer?.Stop();
        }
    }

    /// <summary>停止刷新（窗口关闭 / 组件移除时由宿主调用）。</summary>
    public void Stop() => _timer?.Stop();

    private void Timer_Tick(DispatcherQueueTimer sender, object args) => ViewModel.Update();
}
