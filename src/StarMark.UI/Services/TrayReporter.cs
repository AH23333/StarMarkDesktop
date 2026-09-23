#nullable enable
using System;
using StarMark.Abstractions;

namespace StarMark.UI.Services;

/// <summary>
/// 一次动作做完之后的回报通道：托盘气泡 + 日志，并且<b>报告这条通道自己有没有真的送出去</b>。
/// <para>
/// 截图与贴图共用一份：此前两处各写一遍"托盘没启用时就只留日志"，同一个判断写两遍
/// 迟早有一遍漏掉某个分支（这条口径在批次 MJ 的网速退路里已经验证过一次）。
/// </para>
/// </summary>
internal static class TrayReporter
{
    public static void Report(string category, string title, string body)
    {
        StarLog.Info($"[{category}] {title}：{body}");
        var shown = false;
        try { shown = App.MainWindow?.TryShowTrayNotification($"{category} · {title}", body) == true; }
        catch (Exception ex) { StarLog.Warn($"回报未能送到托盘：{ex.Message}"); }
        if (!shown) StarLog.Info($"（托盘未启用，结果只留在日志）{title}：{body}");
    }
}
