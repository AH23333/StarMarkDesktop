#nullable enable
using System.Diagnostics;
using StarMark.Abstractions;

namespace StarMark.UI.Helpers;

/// <summary>
/// 设置页打开链路的分步计时取证（片245）：首建（ctor・缓存刀后每应用会话只跑一次）
/// 与进页刷新（<c>SettingsPage.OnNavigatedTo</c>・每次导航必到）各拿一只表——
/// 慢步留名、总账补一行，快路径零日志。
/// <para>与 <c>MainWindow.Present</c> 那套五步取证同一风格：单步差值超阈值才记
/// （<see cref="StepLogMs"/>），全程超阈值才补总账（<see cref="TotalLogMs"/>）——
/// 取证只在慢现场说话，快路径一条日志都不发。</para>
/// </summary>
internal static class SettingsOpenTrace
{
    /// <summary>单步差值超过这数才记一行。</summary>
    internal const int StepLogMs = 100;

    /// <summary>全程超过这数才补一行总账。</summary>
    internal const int TotalLogMs = 300;

    /// <summary>单步差值取证：<paramref name="sw"/> 不停表，读的是相邻两步的差。</summary>
    internal static long Step(Stopwatch sw, long fromMs, string label)
    {
        var now = sw.ElapsedMilliseconds;
        if (now - fromMs >= StepLogMs)
            StarLog.Info($"[设置页] {label}：{now - fromMs} ms");
        return now;
    }

    /// <summary>全程总账：慢的开进慢的账，快路径一条不发。</summary>
    internal static void Total(Stopwatch sw, string label)
    {
        if (sw.ElapsedMilliseconds >= TotalLogMs)
            StarLog.Info($"[设置页] {label}：{sw.ElapsedMilliseconds} ms");
    }
}
