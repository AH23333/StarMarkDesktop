#nullable enable
using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 时钟组件 ViewModel（R3 收尾）：承载时间 / 日期两行文本，
/// 由 ClockWidget 的 DispatcherQueueTimer 每秒调用 <see cref="Update"/> 刷新。
/// <para>
/// 第三行是<b>护眼休息提醒的"下一次/休息中"</b>（批次 WC-3，用户裁决"护眼和时钟组件合并"）：
/// 引擎仍然只有一个 <c>EyeRestService</c>，这里<b>只读它的状态、不自己记节拍</b>——
/// 组件与设置页各攒一套时间迟早对不上，而对不上的那次是用户先看见。
/// </para>
/// </summary>
public sealed partial class ClockWidgetViewModel : ObservableObject
{
    [ObservableProperty]
    private string _timeText = DateTime.Now.ToString("HH:mm:ss");

    [ObservableProperty]
    private string _dateText = DateTime.Now.ToString("yyyy-MM-dd dddd");

    /// <summary>护眼那一行的文本；<see cref="HasRestLine"/> 为假时整行不占位。</summary>
    [ObservableProperty]
    private string _restText = string.Empty;

    /// <summary>只有"提醒开着"时才有这一行——默认关着的人不该在时钟上多看见一行空位。</summary>
    [ObservableProperty]
    private bool _hasRestLine;

    /// <summary>用当前时间刷新三个文本（定时器每次 Tick / 启动校准时调用）。</summary>
    public void Update()
    {
        var now = DateTime.Now;
        TimeText = now.ToString("HH:mm:ss");
        DateText = now.ToString("yyyy-MM-dd dddd");
        RefreshRest(now);
    }

    private void RefreshRest(DateTime now)
    {
        // 「休息中」优先：暗幕已经盖下来了，这一行再说"下一次 15:35"就是两套事实。
        if (StarMark.UI.Services.EyeRestService.IsResting)
        {
            RestText = "休息中";
            HasRestLine = true;
            return;
        }
        if (StarMark.UI.Services.EyeRestService.NextDueAt is not { } due)
        {
            RestText = string.Empty;
            HasRestLine = false;
            return;
        }
        // 到点时刻是"大约"：节拍判据在 Core，界面上不承诺精确到分。
        var local = due.ToLocalTime();
        var sameDay = local.Date == now.Date;
        RestText = $"休息 {(sameDay ? local.ToString("HH:mm") : local.ToString("M-d HH:mm"))}";
        HasRestLine = true;
    }
}
