#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using StarMark.Core.Hotkeys;
using StarMark.Integrations.Canvas;
using StarMark.Integrations.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// CanvasService 的这一段——画布工具条与快捷键面板那两扇小窗：建、藏、跟随状态刷新。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段、嵌套类型与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public static partial class CanvasService
{

    // ────────── 工具条 ──────────

    private static void ShowToolbar()
    {
        try
        {
            _toolbar ??= new CanvasToolbarWindow();
            _toolbar.ShowAt(Screens[0].Bounds, Screens[0].Scale);
            LayerDirector.EnforceOrder(AnnotationHub.Stage);
        }
        catch (Exception ex)
        {
            // 工具条建不起来不牵连画布本身：热键、Esc、托盘三条出口仍然在
            StarLog.Error("[Canvas] 工具条没出现（画布仍可用：热键／托盘都在）", ex);
        }
    }

    /// <summary>
    /// 「⌨」那颗按钮：开／收快捷键面板。走到"画布没开着"这一支说明状态已经不对了
    /// （这颗按钮只可能在画布里出现）——仍然要说出来，不能静默。
    /// </summary>
    public static void ToggleHotkeyPanel()
    {
        if (_panel is not null) { HideHotkeyPanel(); return; }
        if (!IsRunning || Screens.Count == 0 || _toolbar is null)
        {
            Report("画布已经关了", "快捷键面板是画布的一部分，先重新打开屏幕画布");
            return;
        }
        try
        {
            var anchor = WindowInterop.GetWindowRect(_toolbar);
            _panel = new CanvasHotkeyPanelWindow();
            _panel.ShowAt(new IntRect(anchor.X, anchor.Y, anchor.Width, anchor.Height),
                Screens[0].Bounds, Screens[0].Scale);
            // 面板排在工具条<b>之下</b>：两个都要点得到，但按钮排在更上面
            // （面板长在条子下面，真重叠时把"再点一次收起"那颗挡住的就是它自己）
            _panel.PlaceUnder(_toolbar.Hwnd);
            // 面板自己已经登记进 Strip 组并插到工具条之下；这里只把画布按角色表归位
            LayerDirector.EnforceOrder(AnnotationHub.Stage);
            StateChanged?.Invoke();           // 那颗「⌨」要亮起来，否则"再点一次收起"看不出来
        }
        catch (Exception ex)
        {
            _panel = null;
            StarLog.Error("[Canvas] 快捷键面板没出现（工具条与热键都还在）", ex);
            Report("快捷键面板没打开", ex.Message);
        }
    }

    /// <summary>面板是否开着——工具条那颗「⌨」据此画高亮，不然"再点一次收起"没有交代。</summary>
    public static bool IsHotkeyPanelOpen => _panel is not null;

    /// <summary>收掉面板（画布继续开着——这块面板只是"看一眼"）。</summary>
    public static void HideHotkeyPanel()
    {
        if (_panel is null) return;
        LayerDirector.Unregister(_panel.Hwnd);
        try { _panel.ClosePanel(); }
        catch (Exception ex) { StarLog.Warn($"[Canvas] 快捷键面板没收掉：{ex.Message}"); }
        _panel = null;
        // 面板是画布的锚点，它一走锚点就换回工具条：不重排一次，玻璃会留在"面板之下"那个已经不存在的位置上
        LayerDirector.EnforceOrder(AnnotationHub.Stage);
        StateChanged?.Invoke();
    }

    /// <summary>面板自己关掉了（✕、退出画布、系统收尾）：编排这边必须忘掉它，不然「⌨」再点就打不开。</summary>
    public static void PanelClosed(CanvasHotkeyPanelWindow panel)
    {
        if (!ReferenceEquals(_panel, panel)) return;
        LayerDirector.Unregister(panel.Hwnd);
        _panel = null;
        LayerDirector.EnforceOrder(AnnotationHub.Stage);
        StateChanged?.Invoke();       // 让那颗按钮的高亮跟着掉回去
    }

    private static void CloseToolbar()
    {
        // 面板挂在工具条下面：条子收了还留着面板，它就成了一块"没有主人的浮窗"（退出画布后仍在屏幕上）
        HideHotkeyPanel();
        if (_toolbar is null) return;
        LayerDirector.Unregister(_toolbar.Hwnd);
        try { _toolbar.CloseToolbar(); }
        catch (Exception ex) { StarLog.Warn($"[Canvas] 工具条没收掉：{ex.Message}"); }
        _toolbar = null;
    }
}
