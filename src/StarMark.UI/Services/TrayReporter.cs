#nullable enable
using System;
using StarMark.Abstractions;

namespace StarMark.UI.Services;

/// <summary>
/// 一次动作做完之后的回报通道：右下角提示卡 + 日志，并且<b>报告这条通道自己有没有真的贴到屏幕上</b>。
/// <para>
/// 截图与贴图共用一份：此前两处各写一遍"托盘没启用时就只留日志"，同一个判断写两遍
/// 迟早有一遍漏掉某个分支（这条口径在批次 MJ 的网速退路里已经验证过一次）。
/// </para>
/// <para>
/// 批次 RV 之前这里发的是<b>托盘气泡</b>，而它在 Windows 11 上"API 返回成功、屏幕上什么都没有"，
/// 于是那句"已经提醒过了"是假的、下面这条兜底也永远不会走。现在只认 <see cref="NoticeCard.Show"/>
/// 给的"窗口可见 + 有实际尺寸 + 落在某块屏的工作区内"。
/// </para>
/// </summary>
internal static class TrayReporter
{
    public static void Report(string category, string title, string body)
    {
        StarLog.Info($"[{category}] {title}：{body}");
        var shown = false;
        try { shown = NoticeCard.Show($"{category} · {title}", body); }
        catch (Exception ex) { StarLog.Warn($"回报没能贴上屏幕：{ex.Message}"); }
        if (!shown) StarLog.Info($"（提示卡没能贴上屏幕，结果只留在日志）{title}：{body}");
    }
}
