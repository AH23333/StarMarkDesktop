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
        Loaded += (_, _) => ApplyAdaptiveFontSize();
    }

    /// <summary>
    /// 时钟现在可以缩放：时间字号按窗口尺寸自适应（照搬 DeskBox Glance 的做法），
    /// 拉大窗口时间就大，缩小时自动收小，不会溢出行或缩成一团。
    /// </summary>
    private void ClockBody_SizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyAdaptiveFontSize();

    private void ApplyAdaptiveFontSize()
    {
        if (ClockBody is null || TimeBlock is null || DateBlock is null) return;
        var w = ClockBody.ActualWidth;
        var h = ClockBody.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // min(宽*0.19, 高*0.34) 再夹到 [22, 72]：宽窗口不至于字太小，高窗口不至于溢出
        var size = Math.Clamp(Math.Min(w * 0.19, h * 0.34), 22, 72);
        TimeBlock.FontSize = Math.Round(size);
        DateBlock.FontSize = Math.Clamp(Math.Round(size * 0.34), 10, 22);
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
