#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// 贴图名册：谁在桌面上、有没有被隐藏、是不是穿透。窗体就是 <see cref="CaptureOverlayWindow"/> 的贴图态
/// ——那扇窗本身就是截图那条标注链（批次 PN），所以名册只管"有哪几张、处于什么全局状态"，
/// 一笔一画都不从这里过。
/// <para>
/// 贴图窗<b>刻意不进组件体系</b>（D3 裁决）：不写进 widgets.json、不参与布局与快照、
/// 退出程序就没了。它是一次性的工具窗，不是"用户摆在那儿的一台组件"。
/// </para>
/// <para>
/// 三件事必须在这一个地方说清，否则会出现"界面显示已隐藏、其实还挂在屏幕上"那类错位：
/// ① 数量上限（每张都常驻一份像素在内存里）；② 隐藏/显示是<b>全体</b>动作；③ 鼠标穿透也是。
/// 穿透之所以要做成全体而不是单张：一旦某张窗收不到鼠标，唯一还能操作它的通道就是全局热键与托盘，
/// 而这两条通道只会说"所有贴图"。
/// </para>
/// </summary>
public static class PinManager
{
    private static readonly List<CaptureOverlayWindow> Pins = new();

    public static int Count => Pins.Count;

    /// <summary>当前所有贴图是不是被收起的（托盘勾选项直接读它）。</summary>
    public static bool AreHidden { get; private set; }

    /// <summary>当前所有贴图是不是穿透鼠标的。</summary>
    public static bool ClickThrough { get; private set; }

    /// <summary>贴一张：受上限约束。放不下的时候必须给原因与出路（"先关掉不用的"），不能只是没动静。</summary>
    public static void Add(byte[] bgra, int width, int height, IntRect placement)
        => OnUi(() =>
        {
            if (CaptureGeometry.PinLimitProblem(Pins.Count) is { } full)
            {
                TrayReporter.Report("贴图", "已达上限", full);
                return;
            }
            try
            {
                var pin = new CaptureOverlayWindow(bgra, width, height, placement, zoom: 1.0);
                Pins.Add(pin);
                // 新贴的一张要跟随当前全局状态，否则"明明收起了却冒出一张新的"
                if (AreHidden) pin.HidePin();
                if (ClickThrough && !pin.ApplyClickThrough(true)) ClickThrough = Pins.All(p => p.IsClickThrough);
                TrayReporter.Report("贴图", "已钉住",
                    CaptureGeometry.FormatSize(width, height) + $"（共 {Pins.Count} 张，上限 {CaptureGeometry.MaxPins} 张）");
            }
            catch (Exception ex)
            {
                StarLog.Error("[Pin] 贴图未能钉住", ex);
                TrayReporter.Report("贴图", "钉住失败", ex.Message);
            }
        });

    /// <summary>显示 / 收起所有贴图（F4）。这一条同时是"贴图都点不动了"的出口。</summary>
    public static void ToggleHidden() => OnUi(() =>
    {
        if (CaptureGeometry.PinCommandProblem(Pins.Count) is { } none)
        {
            TrayReporter.Report("贴图", "没有可操作的贴图", none);
            return;
        }
        AreHidden = !AreHidden;
        foreach (var pin in Pins)
        {
            if (AreHidden) pin.HidePin();
            else pin.Present();
        }
        StarLog.Info($"[Pin] {(AreHidden ? "收起" : "显示")}全部贴图（{Pins.Count} 张）");
    });

    /// <summary>切换所有贴图的鼠标穿透。</summary>
    public static void ToggleClickThrough() => OnUi(() =>
    {
        if (CaptureGeometry.PinCommandProblem(Pins.Count) is { } none)
        {
            TrayReporter.Report("贴图", "没有可操作的贴图", none);
            return;
        }
        var wanted = !ClickThrough;
        var failed = Pins.Count(pin => !pin.ApplyClickThrough(wanted));
        ClickThrough = wanted && failed == 0;
        if (failed > 0)
        {
            // 部分成功是最坏的形状：有的收鼠标有的不收。如实说出来，别让用户自己一张一张试出来。
            TrayReporter.Report("贴图", "穿透未能全部生效",
                $"{failed} / {Pins.Count} 张拒绝了这个改动（系统拒绝了扩展样式），在那几张的工具条上再点一次穿透");
            return;
        }
        StarLog.Info($"[Pin] 鼠标穿透＝{(ClickThrough ? "开" : "关")}（{Pins.Count} 张）");
    });

    public static void CloseAll() => OnUi(() =>
    {
        if (CaptureGeometry.PinCommandProblem(Pins.Count) is { } none)
        {
            TrayReporter.Report("贴图", "没有可操作的贴图", none);
            return;
        }
        var closed = Pins.Count;
        // 先摸一份快照：Close 会同步走 Closed→Unregister，边遍历边删会跳元素
        foreach (var pin in Pins.ToList())
        {
            try { pin.Close(); }
            catch (Exception ex) { StarLog.Warn($"[Pin] 关闭贴图失败：{ex.Message}"); }
        }
        Pins.Clear();
        ResetState();
        StarLog.Info($"[Pin] 已关闭 {closed} 张贴图");
    });

    /// <summary>贴图自己关闭时（Esc / 条上的 ✕ / Alt+F4）从名册里摘掉。</summary>
    internal static void Unregister(CaptureOverlayWindow pin)
    {
        if (!Pins.Remove(pin)) return;
        if (Pins.Count == 0) ResetState();
        else if (ClickThrough) ClickThrough = Pins.All(p => p.IsClickThrough);
    }

    /// <summary>
    /// 全局「撤销／重做」交回<b>正在吃键盘的那张贴图</b>：按前台句柄在名册里找到它，把这一按还给它。
    /// <para><b>那一张贴图没东西可撤时也算"处理了"</b>（返回 true 就不再回落画布）：注意力在这张图上时，
    /// 凭空少掉别处的最后一笔比"这一按什么都没做"更糟，而且前者看起来是成功了。</para>
    /// <para>返回 false＝名册里没有这一张（层名册与贴图名册是两份账，正在关闭的那一刻会错开）。
    /// 那时调用方会回落到画布，而这里留一行日志——一次"按了没反应"是最难归因的缺陷形状。</para>
    /// </summary>
    public static bool UndoRedoAtFocusedPin(IntPtr foreground, bool redo)
    {
        foreach (var pin in Pins)
        {
            if (WindowInterop.GetHwnd(pin) != foreground) continue;
            pin.HotkeyUndoRedo(redo);
            return true;
        }
        StarLog.Warn($"[Pin] 撤销/重做没找到前台句柄对应的贴图（0x{foreground.ToInt64():X}），回落到画布");
        return false;
    }

    /// <summary>
    /// 一张不剩时把两个全局开关放回初始位：留下的话，下一张贴图会被静默隐藏或静默穿透，
    /// 而那时用户已经把上一张关掉了——名册空了，状态也就没有归属了。
    /// </summary>
    private static void ResetState()
    {
        AreHidden = false;
        ClickThrough = false;
    }

    /// <summary>建窗与改窗都必须站在 UI 线程上（热键回调本来就在，托盘/菜单的回调线程不保证）。</summary>
    private static void OnUi(Action action)
    {
        var queue = App.MainWindow?.DispatcherQueue;
        if (queue is { HasThreadAccess: false })
        {
            queue.TryEnqueue(() => action());
            return;
        }
        action();
    }
}
