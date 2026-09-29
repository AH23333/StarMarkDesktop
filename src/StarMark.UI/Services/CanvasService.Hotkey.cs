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
/// CanvasService 的这一段——全局热键那一头：九条键落到哪个动作、功能总开关关着时给可见原因、报出用户当前实际绑着的键。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段、嵌套类型与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public static partial class CanvasService
{

    /// <summary>
    /// 工具类全局键：<b>画布还没开就先把它开起来</b>再选这支笔。讲解的人按"画笔"是要画画，
    /// 不是要先按另一个键把板子叫出来——多一步就是缺陷（发起人定的口径）。
    /// 再按同一个键＝收笔回穿透态，与工具条上那颗同一语义。
    /// </summary>
    public static void HotkeyTool(CanvasTool tool)
    {
        // 这里不再自己写"没开就先 Start()"那条平行逻辑：Idle + 选一支要留痕的笔 ⇒ 绘制态，
        // 由转移表给（讲解的人按「画笔」就是要画画，多一步先叫板子是缺陷）。
        // 再按同一个键＝收笔回穿透态，与工具条上那颗同一语义（用户裁决，三条链统一）。
        ToggleTool(tool);
    }

    /// <summary>
    /// 总开关读的是磁盘上那一份（不缓存）：设置页里改完立刻生效，不需要重启也不需要"通知一遍"，
    /// 而漏通知正是"开关是关的、功能还在跑"这种鬼状态的来源。
    /// </summary>
    public static bool EnabledBySetting
        => (App.Services?.GetService(typeof(SettingsStore)) as SettingsStore)?.LoadCanvasEnabled() ?? true;

    public static void HotkeyClickThrough() => RequireRunning("交出 / 收回鼠标", () => SetClickThrough(!ClickThroughHere));

    public static void HotkeyUndo() => RequireRunning("撤销上一笔", Undo);

    public static void HotkeyRedo() => RequireRunning("重做那一笔", Redo);

    public static void HotkeyClear() => RequireRunning("清空笔迹", ClearAll);

    public static void HotkeySave() => RequireRunning("存为图片", SavePng);

    public static void HotkeyCopy() => RequireRunning("复制到剪贴板", SnapshotToClipboard);

    public static void HotkeyPin() => RequireRunning("贴到桌面", SnapshotToPin);

    /// <summary>
    /// 画布内动作的闸门：<b>板子没开着时按这些键要给一句看得见的原因</b>，不能"按了没反应"——
    /// 那在用户眼里与功能坏了是同一件事。两种"没开着"要分开说：功能被关掉时指向快捷键是指错路，
    /// 所以那里说的是"去设置里打开"。
    /// </summary>
    private static void RequireRunning(string what, Action run)
    {
        if (IsRunning)
        {
            run();
            return;
        }
        Report("画布没开着", EnabledBySetting
            ? $"「{what}」要先打开屏幕画布（{BindingText(HotkeyActions.CanvasToggle)}，或托盘菜单「屏幕画布」）"
            : $"「{what}」要先在 设置 → 拓展功能 里打开「屏幕画布」");
    }

    /// <summary>某动作当前绑定的键位文本（没绑定／读不到设置时回"未绑定"，绝不回一个假键位）。</summary>
    public static string BindingText(string action)
    {
        try
        {
            var settings = App.Services?.GetService(typeof(SettingsStore)) as SettingsStore;
            if (settings is null) return "未绑定";
            var gesture = settings.GetHotkeyBindings().GetValueOrDefault(action);
            return gesture is { IsEmpty: false } bound ? HotkeyDisplay.Display(bound) : "未绑定";
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[Canvas] 键位文本没取到：{ex.Message}");
            return "未绑定";
        }
    }
}
