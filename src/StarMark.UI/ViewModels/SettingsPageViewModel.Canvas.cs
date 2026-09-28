#nullable enable
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using StarMark.Abstractions;
using StarMark.Abstractions.Insights;
using StarMark.Integrations.Clipboard;
using StarMark.Core.Backup;
using StarMark.Core.Hotkeys;
using StarMark.Core.Insights;
using StarMark.Core.Performance;
using StarMark.UI.Helpers;
using Windows.UI;

namespace StarMark.UI.ViewModels;

/// <summary>
/// SettingsPageViewModel 的这一段——屏幕画布这一头：总开关、热键只读一览的文本从哪来。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public partial class SettingsPageViewModel
{

    /// <summary>键位改了之后重算这一览（「保存快捷键」与「重试注册」两条路都调它，否则这里会显示旧键位）。</summary>
    public void RefreshCanvasHotkeySheet()
    {
        CanvasHotkeySheet = BuildCanvasHotkeySheet();
        CanvasStatus = BuildCanvasStatus();
    }

    private string BuildCanvasStatus()
    {
        var open = HotkeyText(HotkeyActions.CanvasToggle);
        return CanvasEnabled
            ? $"已开启：按 {open} 进入画布（进去是穿透态，下层应用照常操作，画布不会吃掉鼠标）。" +
              "画布上的工具条有那颗「⌨」，随时能把下面这张表原样调出来。"
            : $"已关闭：托盘里不再有「屏幕画布」，画布内那批快捷键也不再占用系统组合键；" +
              $"按 {open} 只会提示一句“要先在设置里打开”，不会静默。";
    }

    private string BuildCanvasHotkeySheet()
        => string.Join("\n", HotkeyActions.Canvas.Select(a => $"{HotkeyActions.DisplayName(a)}　{HotkeyText(a)}"));

    /// <summary>一条动作当前的键位文本（与画布那块面板同一个出处：设置页显示的与真生效的是同一份）。</summary>
    private string HotkeyText(string action)
    {
        var gesture = _settings.GetHotkeyBindings().GetValueOrDefault(action);
        return gesture is { IsEmpty: false } bound ? HotkeyDisplay.Display(bound) : "未绑定";
    }

    partial void OnTrendingEnabledChanged(bool value)
    {
        if (_suppressTrendingApply) return;
        _settings.SaveTrendingEnabled(value);
        App.MainWindow?.ApplyTrendingNavVisibility(value);
        TrendingStatus = value
            ? "已开启：导航栏「剪贴板」右侧现在有「热榜」。热榜本身不需要 Token（读侧匿名），只有 Star 按钮需要。"
            : "已关闭：导航栏的「热榜」项已移除，不再发起任何抓取；已缓存的那份留在本机，重新开启时直接用。";
        if (value) _ = AskTrendingGlanceAsync();
    }

    partial void OnTrendingGlanceEnabledChanged(bool value)
    {
        if (_suppressTrendingApply) return;
        _settings.SaveTrendingGlanceEnabled(value);
        TrendingStatus = value
            ? "「今日速览」已加上热榜块（默认日榜），原「常看」排在它下面。"
            : "「今日速览」不再显示热榜块；导航栏的「热榜」页不受影响。";
        // 组件即时跟上：这条广播就是各组件"数据变了重载一次"的既有通道，不必等重启、也不新写一套通知。
        StarMark.Abstractions.DataChangeHub.Notify();
    }
}
