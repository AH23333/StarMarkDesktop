#nullable enable
using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 时钟组件 ViewModel（R3 收尾）：承载时间 / 日期两行文本，
/// 由 ClockWidget 的 DispatcherQueueTimer 每秒调用 <see cref="Update"/> 刷新。
/// </summary>
public sealed partial class ClockWidgetViewModel : ObservableObject
{
    [ObservableProperty]
    private string _timeText = DateTime.Now.ToString("HH:mm:ss");

    [ObservableProperty]
    private string _dateText = DateTime.Now.ToString("yyyy-MM-dd dddd");

    /// <summary>用当前时间刷新两个文本（定时器每次 Tick / 启动校准时调用）。</summary>
    public void Update()
    {
        var now = DateTime.Now;
        TimeText = now.ToString("HH:mm:ss");
        DateText = now.ToString("yyyy-MM-dd dddd");
    }
}
