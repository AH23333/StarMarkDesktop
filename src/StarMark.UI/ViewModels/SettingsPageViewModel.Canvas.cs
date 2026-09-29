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

    /// <summary>
    /// 键位改了之后重算这几处（「保存快捷键」与「重试注册」两条路都调它，否则这里会显示旧键位）。
    /// <para>画布那一览、画布状态行、截屏状态行<b>三处都引用键位文本</b>，所以三处一起重算——
    /// 少带一处，那一处就会一直显示改之前的键，而"照着说明按，没反应"正是这类残骸的症状。</para>
    /// </summary>
    public void RefreshCanvasHotkeySheet()
    {
        CanvasHotkeyRows = new System.Collections.ObjectModel.ObservableCollection<StarMark.UI.Views.CanvasHotkeyRow>(
            BuildCanvasHotkeyRows());
        CanvasStatus = BuildCanvasStatus();
        CaptureStatus = BuildCaptureStatus();
    }

    /// <summary>
    /// 一张表生成十行，<b>每行两个字段</b>：动作名与键位各归各的列。
    /// <para>旧做法是拼成一条多行字符串（<c>$"{名字}　{键位}"</c>）。那样渲染出来的是<b>十个左对齐的段落</b>：
    /// 键位的起点跟着名字的长度跑，十行里没有一个共同的右界，卡片右侧那一大片始终空着——
    /// 这就是发起人点名的"文字全部挤在一起，而右侧却有很大空间"。分成两列之后名字吃掉剩余宽度，键位贴右缘，读起来才是一张表。</para>
    /// </summary>
    private IEnumerable<StarMark.UI.Views.CanvasHotkeyRow> BuildCanvasHotkeyRows()
        => HotkeyActions.Canvas.Select(a => new StarMark.UI.Views.CanvasHotkeyRow(HotkeyActions.DisplayName(a), HotkeyText(a)));

    private string BuildCanvasStatus()
    {
        var open = HotkeyText(HotkeyActions.CanvasToggle);
        return CanvasEnabled
            ? $"已开启：按 {open} 进入画布（进去是穿透态，下层应用照常操作，画布不会吃掉鼠标）。" +
              "画布上的工具条有那颗「⌨」，随时能把下面这张表原样调出来。"
            : $"已关闭：托盘里不再有「屏幕画布」，画布内那批快捷键也不再占用系统组合键；" +
              $"按 {open} 只会提示一句“要先在设置里打开”，不会静默。";
    }

    /// <summary>
    /// 一条动作当前的键位文本。<b>整条链只有 <c>CanvasService.BindingText</c> 一份实现</b>：
    /// 设置页这一览、画布上那块 ⌨ 面板、还有"要先打开屏幕画布（××键…）"那句里引用的，都是它。
    /// 两处各判一次"没绑定时显示什么"，迟早有一处编出一个看着对、按下去没反应的键位（那正是这块面板要防的事）。
    /// </summary>
    private static string HotkeyText(string action) => StarMark.UI.Services.CanvasService.BindingText(action);

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
