#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Abstractions.I18n;
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

    public static void HotkeyClickThrough() => RequireRunning("canvas.act.through", () => SetClickThrough(!ClickThroughHere));

    public static void HotkeyUndo() => RequireRunning("canvas.act.undo", Undo);

    public static void HotkeyRedo() => RequireRunning("canvas.act.redo", Redo);

    public static void HotkeyClear() => RequireRunning("canvas.act.clear", ClearAll);

    public static void HotkeySave() => RequireRunning("canvas.act.save", SavePng);

    public static void HotkeyCopy() => RequireRunning("canvas.act.copy", SnapshotToClipboard);

    public static void HotkeyPin() => RequireRunning("canvas.act.pin", SnapshotToPin);

    /// <summary>
    /// 画布内动作的闸门：<b>板子没开着时按这些键要给一句看得见的原因</b>，不能"按了没反应"——
    /// 那在用户眼里与功能坏了是同一件事。两种"没开着"要分开说：功能被关掉时指向快捷键是指错路，
    /// 所以那里说的是"去设置里打开"。
    /// <para>调用点交的是<b>动作名的那颗键</b>而不是句子：这句要上屏幕，字只能住在表里（P-176）。
    /// 那里用的是短名（<c>canvas.act.*</c>）而不是快捷键栏那一行的标签（<c>hotkey.act.canvas.*</c>，
    /// 带「画布 · 」前缀）——前缀是那张<b>平铺清单</b>为区分六个分组而加的脚手架，不是动作的名字，
    /// 念进这一句就成了「画布 · 撤销上一笔」要先打开屏幕画布。</para>
    /// </summary>
    private static void RequireRunning(string whatKey, Action run)
    {
        if (IsRunning)
        {
            run();
            return;
        }
        var what = Locale.T(whatKey);
        // 「屏幕画布」与托盘那一项的名字都由调用点把表里的实参递进来（⛔ 抄进句子＝第二个出处，#353 同族）。
        Report(LocMessage.Of("canvas.notice.notRunningTitle"), EnabledBySetting
            ? LocMessage.Of("canvas.hotkey.notRunning", what, LocMessage.Of("settings.extend.canvasTitle"),
                  BindingText(HotkeyActions.CanvasToggle), LocMessage.Of("tray.canvas"))
            : LocMessage.Of("canvas.hotkey.disabled", what, LocMessage.Of("settings.extend.canvasTitle")));
    }

    /// <summary>某动作当前绑定的键位文本（没绑定／读不到设置时回表里那颗 <c>hotkey.unbound</c>，绝不回一个假键位）。</summary>
    public static string BindingText(string action)
    {
        try
        {
            var settings = App.Services?.GetService(typeof(SettingsStore)) as SettingsStore;
            if (settings is null) return Locale.T("hotkey.unbound");
            var gesture = settings.GetHotkeyBindings().GetValueOrDefault(action);
            return gesture is { IsEmpty: false } bound ? HotkeyDisplay.Display(bound) : Locale.T("hotkey.unbound");
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[Canvas] 键位文本没取到：{ex.Message}");
            return Locale.T("hotkey.unbound");
        }
    }
}
