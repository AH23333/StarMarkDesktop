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
/// 贴图名册：谁在桌面上、有没有被收起、是不是穿透，以及<b>哪些算一组</b>（批次 S4-③）。
/// 窗体就是 <see cref="CaptureOverlayWindow"/> 的贴图态——那扇窗本身就是截图那条标注链（批次 PN），
/// 所以名册只管"有哪几张、谁跟谁一组"，一笔一画都不从这里过。
/// <para>
/// 贴图窗<b>刻意不进组件体系</b>（D3 裁决）：不写进 widgets.json、不参与布局与快照、退出程序就没了。
/// 组也一样——它是"这一屏期间先把课件那几张收起来"的现场工具，不是一份要长期维护的清单。
/// </para>
/// <para>
/// <b>状态只有一个真值，就是每张贴图自己那扇窗</b>。<see cref="AreHidden"/> 与 <see cref="ClickThrough"/>
/// 是<b>派生</b>属性而不是存储：以前这里存着两个"全体"开关，而双击一张贴图确实能只收起那一张——
/// 那两个开关于是会说谎（名册说没隐藏，屏幕上少了一张）。有了组之后更不能再存一份：
/// 组说"已隐藏"而里面那张其实还在屏幕上，是名册与窗体两份账的经典分岔（批次 WO/WD-7 同一族）。
/// 组只记成员，动作按成员下发，聚合态现算。
/// </para>
/// <para>
/// 每条动作都要留得下的说法：一组贴图收起后它的工具条也一起没了，那时<b>唯一的出口是托盘与全局热键</b>
/// （F4 显示所有）。所以"整组穿透""整组隐藏"允许，但托盘必须永远列得出每一组——这也是
/// <see cref="PinGrouping.MaxGroups"/> 存在的理由：多到翻不动的菜单等于没有出口。
/// </para>
/// </summary>
public static class PinManager
{
    private static readonly List<CaptureOverlayWindow> Pins = new();
    private static readonly List<PinGroup> GroupRoster = new();
    private static int _nextSerial;

    /// <summary>
    /// 一个贴图组：<b>只有名字和成员</b>，没有 Hidden / ClickThrough 字段——那两个真值住在窗上，
    /// 这里只做"整组是否都处于某状态"的现算（一张都不在屏幕上时这一组会被 <c>Prune</c> 掉，
    /// 所以空组不会在托盘上留下一行点不动的假目标）。
    /// </summary>
    public sealed class PinGroup(int serial, CaptureOverlayWindow first)
    {
        public int Serial { get; } = serial;
        public string Name { get; } = PinGrouping.NameOf(serial);
        public List<CaptureOverlayWindow> Members { get; } = [first];

        /// <summary>整组都收起了才算"这一组已隐藏"——决定那条菜单写「显示」还是「隐藏」。</summary>
        public bool AllHidden => PinGrouping.AllIn(Members.Select(static pin => pin.IsHidden).ToList());

        /// <summary>整组都穿透才算"这一组忽略鼠标"。</summary>
        public bool AllThrough => PinGrouping.AllIn(Members.Select(static pin => pin.IsClickThrough).ToList());
    }

    public static int Count => Pins.Count;

    /// <summary>托盘与条上那一栏都按这一份生成；顺序即创建顺序。<b>只读</b>：改成员一律走本类的动作方法。</summary>
    public static IReadOnlyList<PinGroup> Groups => GroupRoster;

    /// <summary>整组都收起了才算"已隐藏"。空名册返回 false（那里没有东西可隐藏）。</summary>
    public static bool AreHidden => AllIn(Pins, static pin => pin.IsHidden);

    /// <summary>整组都穿透才算"忽略鼠标"。</summary>
    public static bool ClickThrough => AllIn(Pins, static pin => pin.IsClickThrough);

    private static bool AllIn(List<CaptureOverlayWindow> pins, Func<CaptureOverlayWindow, bool> of)
        => PinGrouping.AllIn(pins.Select(of).ToList());

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
                // 先取聚合态再建窗：新建的这张默认既可见又收鼠标，会把"全部已收起"这个聚合值打成 false。
                var wasHidden = AreHidden;
                var wasThrough = ClickThrough;
                var pin = new CaptureOverlayWindow(bgra, width, height, placement, zoom: 1.0);
                Pins.Add(pin);
                // 新贴的一张要跟随当前全局状态，否则"明明收起了却冒出一张新的"
                if (wasHidden) pin.HidePin();
                if (wasThrough && !pin.ApplyClickThrough(true))
                    TrayReporter.Report("贴图", "穿透未跟上新贴图", "系统拒绝了这一张的扩展样式，其余几张仍然穿透");
                TrayReporter.Report("贴图", "已钉住",
                    CaptureGeometry.FormatSize(width, height) + $"（共 {Pins.Count} 张，上限 {CaptureGeometry.MaxPins} 张）");
            }
            catch (Exception ex)
            {
                StarLog.Error("[Pin] 贴图未能钉住", ex);
                TrayReporter.Report("贴图", "钉住失败", ex.Message);
            }
        });

    /// <summary>显示 / 收起所有贴图（F4）。这一条同时是"某一组收起了找不到出口"时的兜底出口。</summary>
    public static void ToggleHidden() => OnUi(() =>
    {
        if (CaptureGeometry.PinCommandProblem(Pins.Count) is { } none)
        {
            TrayReporter.Report("贴图", "没有可操作的贴图", none);
            return;
        }
        ApplyHiddenTo(Pins, PinGrouping.NextHidden(AreHidden), "全部贴图");
    });

    /// <summary>切换所有贴图的鼠标穿透。</summary>
    public static void ToggleClickThrough() => OnUi(() =>
    {
        if (CaptureGeometry.PinCommandProblem(Pins.Count) is { } none)
        {
            TrayReporter.Report("贴图", "没有可操作的贴图", none);
            return;
        }
        ApplyThroughTo(Pins, PinGrouping.NextThrough(ClickThrough), "全部贴图");
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
        GroupRoster.Clear();          // 名册空了就没有"组"可言；留着会显示成能点却什么都不做的空行
        StarLog.Info($"[Pin] 已关闭 {closed} 张贴图");
    });

    // ────────── 贴图组（批次 S4-③）──────────

    /// <summary>
    /// 把这一张单独分成一组。<b>一张最多属一组</b>：两组各自隐藏时，"这一组到底隐没隐"会失去答案。
    /// 到上限时说清"关掉一组再说"，而不是把那几颗按钮藏起来。
    /// </summary>
    public static void NewGroup(CaptureOverlayWindow pin) => OnUi(() =>
    {
        if (PinGrouping.LimitProblem(GroupRoster.Count) is { } full)
        {
            TrayReporter.Report("贴图组", "不能再分组", full);
            return;
        }
        Detach(pin);
        var group = new PinGroup(++_nextSerial, pin);
        GroupRoster.Add(group);
        StarLog.Info($"[Pin] {group.Name}＝1 张");
        TrayReporter.Report("贴图组", "已分组", $"{group.Name}（1 张）。托盘「贴图组」里能整组隐藏 / 穿透 / 关闭");
    });

    /// <summary>把这一张并进已有的一组（先从原来的组里摘出来，组号因此可能变少一行）。</summary>
    public static void JoinGroup(CaptureOverlayWindow pin, int serial) => OnUi(() =>
    {
        var target = Find(serial);
        if (target is null)
        {
            // 菜单是右键那一刻现取的，走到这里时那一组可能已经被关掉了：说清楚，别让人以为并进去了。
            TrayReporter.Report("贴图组", "那一组已经不在了", $"组 {serial} 在本次操作前被关掉，这张仍按原样留在桌面上");
            return;
        }
        Detach(pin);
        target.Members.Add(pin);
        StarLog.Info($"[Pin] 并入 {target.Name}＝{target.Members.Count} 张");
        TrayReporter.Report("贴图组", "已并入", $"{target.Name}（{target.Members.Count} 张）");
    });

    /// <summary>从当前组里出来。整组只剩一张时它出来就等于空组 ⇒ 那一组一起消失。</summary>
    public static void LeaveGroup(CaptureOverlayWindow pin) => OnUi(() =>
    {
        var from = GroupRoster.FirstOrDefault(g => g.Members.Contains(pin));
        if (from is null) return;
        Detach(pin);
        StarLog.Info($"[Pin] 已从 {from.Name} 移出");
        TrayReporter.Report("贴图组", "已移出组", $"{from.Name} 还剩 {from.Members.Count} 张");
    });

    /// <summary>
    /// 托盘「贴图组」子菜单的唯一入口：命令号在这里解码成（组，动作），动作只认 Core 那三个值。
    /// <para>解不出组（那一组在右键之后被关掉）也要回执：菜单上的行是快照，点了没反应最难归因。</para>
    /// </summary>
    public static void RunGroupCommand(int tag)
    {
        if (!PinGrouping.IsGroupTag(tag)) return;
        var serial = PinGrouping.SerialOf(tag);
        var action = PinGrouping.ActionOf(tag);
        OnUi(() =>
        {
            if (Find(serial) is not { } group)
            {
                TrayReporter.Report("贴图组", "那一组已经不在了", $"组 {serial} 在本次点击前被关掉");
                return;
            }
            switch (action)
            {
                case PinGrouping.Action.Show:
                    ApplyHiddenTo(group.Members, PinGrouping.NextHidden(All(group.Members, static p => p.IsHidden)), group.Name);
                    break;
                case PinGrouping.Action.Through:
                    ApplyThroughTo(group.Members, PinGrouping.NextThrough(All(group.Members, static p => p.IsClickThrough)), group.Name);
                    break;
                case PinGrouping.Action.Close:
                    CloseGroup(group);
                    break;
            }
        });
    }

    private static void CloseGroup(PinGroup group)
    {
        var closed = group.Members.Count;
        // 快照后逐张关：Close 会同步回调 Unregister→Detach，边遍历边删会跳元素（与 CloseAll 同一条理由）。
        foreach (var pin in group.Members.ToList())
        {
            try { pin.Close(); }
            catch (Exception ex) { StarLog.Warn($"[Pin] 关闭{group.Name}中的一张贴图失败：{ex.Message}"); }
        }
        Prune();
        StarLog.Info($"[Pin] 已关闭 {group.Name}（{closed} 张）");
        TrayReporter.Report("贴图组", "已关闭这一组", $"{group.Name}（{closed} 张）");
    }

    private static void ApplyHiddenTo(List<CaptureOverlayWindow> targets, bool hidden, string scope)
    {
        foreach (var pin in targets)
        {
            if (hidden) pin.HidePin();
            else pin.Present();
        }
        StarLog.Info($"[Pin] {(hidden ? "收起" : "显示")}{scope}（{targets.Count} 张）");
    }

    /// <summary>整批套穿透并数出被系统拒绝的张数——部分成功是最坏的形状，必须如实说出来。</summary>
    private static void ApplyThroughTo(List<CaptureOverlayWindow> targets, bool through, string scope)
    {
        var failed = targets.Count(pin => !pin.ApplyClickThrough(through));
        if (failed > 0)
        {
            TrayReporter.Report("贴图", "穿透未能全部生效",
                $"{failed} / {targets.Count} 张（{scope}）拒绝了这个改动（系统拒绝了扩展样式），在那几张的工具条上再点一次穿透");
            return;
        }
        StarLog.Info($"[Pin] 鼠标穿透＝{(through ? "开" : "关")}（{scope} {targets.Count} 张）");
    }

    private static bool All(List<CaptureOverlayWindow> members, Func<CaptureOverlayWindow, bool> of)
        => PinGrouping.AllIn(members.Select(of).ToList());

    /// <summary>这一张当前在哪一组（没分组返回 null）。条上那颗的说明与选择栏的勾选态都读它。</summary>
    public static PinGroup? GroupOf(CaptureOverlayWindow pin) => GroupRoster.FirstOrDefault(g => g.Members.Contains(pin));

    private static PinGroup? Find(int serial) => GroupRoster.FirstOrDefault(g => g.Serial == serial);

    /// <summary>从所有组里摘掉这一张（不删空组——调用方紧接着会 Prune，或在建新组时把它填回去）。</summary>
    private static void Detach(CaptureOverlayWindow pin)
    {
        foreach (var group in GroupRoster) group.Members.Remove(pin);
    }

    private static void Prune() => GroupRoster.RemoveAll(group => PinGrouping.ShouldDrop(group.Members.Count));

    /// <summary>贴图自己关闭时（Esc / 条上的 ✕ / Alt+F4 / 关掉整组）从名册和所有组里摘掉。</summary>
    internal static void Unregister(CaptureOverlayWindow pin)
    {
        if (!Pins.Remove(pin)) return;
        // 少了这一步，组里会留着已经关掉的窗：托盘那行写着「组 1（3 张）」，点隐藏却只作用到剩下的两张。
        Detach(pin);
        Prune();
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
