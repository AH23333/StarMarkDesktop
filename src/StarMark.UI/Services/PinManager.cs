#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Abstractions.Clipboard;
using StarMark.Abstractions.I18n;
using StarMark.Core.Capture;
using StarMark.Integrations.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// 贴图名册：谁在桌面上、有没有被收起、是不是穿透，以及<b>哪些算一组</b>（批次 S4-③）。
/// 窗体就是 <see cref="CaptureOverlayWindow"/> 的贴图态——那扇窗本身就是截图那条标注链（批次 PN），
/// 所以名册只管"有哪几张、谁跟谁一组"，一笔一画都不从这里过。
/// <para>
/// 贴图窗<b>刻意不进组件体系</b>（D3 裁决）：不写进 widgets.json、不参与布局与快照。
/// 但自 ✅P-80 起<b>退出不再是没了</b>：每张贴图＝一份 PNG（pin-{Id}.png）＋一行元数据（pins.json），
/// 启动时按元数据逐张重建；组仍不持久化——它是"这一屏期间先把课件那几张收起来"的现场工具，
/// 不是一份要长期维护的清单。
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

        /// <summary>组名的<b>念法</b>：每次现算（批次 WI-46c）。⛔ 存进字段——存下来＝在建组那一刻定死，换语言后不动。</summary>
        public LocMessage Name => PinGrouping.NameOf(Serial);
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
                TrayReporter.Report(LocMessage.Of("pin.card.category"), LocMessage.Of("pin.card.limitTitle"), full);
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
                    TrayReporter.Report(LocMessage.Of("pin.card.category"), LocMessage.Of("pin.card.throughMissTitle"),
                        LocMessage.Of("pin.card.throughMissBody"));
                // ✅P-80：首建登记要在全局状态套用之后、toast 之前——这一拍把"发 Id＋登记＋首行落盘"一次做完。
                PersistNew(pin);
                TrayReporter.Report(LocMessage.Of("pin.card.category"), LocMessage.Of("pin.card.pinnedTitle"),
                    LocMessage.Of("common.rawValue", CaptureGeometry.FormatSize(width, height)
                        + Locale.Fmt("pin.card.pinnedBody", Pins.Count, CaptureGeometry.MaxPins)));
            }
            catch (Exception ex)
            {
                StarLog.Error("[Pin] 贴图未能钉住", ex);
                TrayReporter.Report(LocMessage.Of("pin.card.category"), LocMessage.Of("pin.card.pinFailedTitle"),
                    LocMessage.Of("common.rawValue", ex.Message));
            }
        });

    /// <summary>显示 / 收起所有贴图（F4）。这一条同时是"某一组收起了找不到出口"时的兜底出口。</summary>
    public static void ToggleHidden() => OnUi(() =>
    {
        if (CaptureGeometry.PinCommandProblem(Pins.Count) is { } none)
        {
            // ✅WI-65 片3 第二刀：那颗判据自己交「键＋实参」⇒ 直接递出去（common.rawValue 那道门只装念完的成品字）。
            TrayReporter.Report(LocMessage.Of("pin.card.category"), LocMessage.Of("pin.card.noneTitle"), none);
            return;
        }
        ApplyHiddenTo(Pins, PinGrouping.NextHidden(AreHidden), LocMessage.Of("pin.card.scopeAll"));
    });

    /// <summary>切换所有贴图的鼠标穿透。</summary>
    public static void ToggleClickThrough() => OnUi(() =>
    {
        if (CaptureGeometry.PinCommandProblem(Pins.Count) is { } none)
        {
            // ✅WI-65 片3 第二刀：那颗判据自己交「键＋实参」⇒ 直接递出去（common.rawValue 那道门只装念完的成品字）。
            TrayReporter.Report(LocMessage.Of("pin.card.category"), LocMessage.Of("pin.card.noneTitle"), none);
            return;
        }
        ApplyThroughTo(Pins, PinGrouping.NextThrough(ClickThrough), LocMessage.Of("pin.card.scopeAll"));
    });

    public static void CloseAll() => OnUi(() =>
    {
        if (CaptureGeometry.PinCommandProblem(Pins.Count) is { } none)
        {
            // ✅WI-65 片3 第二刀：那颗判据自己交「键＋实参」⇒ 直接递出去（common.rawValue 那道门只装念完的成品字）。
            TrayReporter.Report(LocMessage.Of("pin.card.category"), LocMessage.Of("pin.card.noneTitle"), none);
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
            // ✅WI-65 片4：那颗判据自己交「键＋实参」⇒ 直接递出去（common.rawValue 那道门只装念完的成品字）。
            TrayReporter.Report(LocMessage.Of("pin.group.title"), LocMessage.Of("pin.card.groupFullTitle"),
                full);
            return;
        }
        Detach(pin);
        var group = new PinGroup(++_nextSerial, pin);
        GroupRoster.Add(group);
        StarLog.Info($"[Pin] {group.Name.Render()}＝1 张");
        TrayReporter.Report(LocMessage.Of("pin.group.title"), LocMessage.Of("pin.card.groupedTitle"),
            LocMessage.Of("pin.card.groupedBody", PinGrouping.GroupLabel(group.Name, 1), LocMessage.Of("pin.group.title")));
    });

    /// <summary>把这一张并进已有的一组（先从原来的组里摘出来，组号因此可能变少一行）。</summary>
    public static void JoinGroup(CaptureOverlayWindow pin, int serial) => OnUi(() =>
    {
        var target = Find(serial);
        if (target is null)
        {
            // 菜单是右键那一刻现取的，走到这里时那一组可能已经被关掉了：说清楚，别让人以为并进去了。
            TrayReporter.Report(LocMessage.Of("pin.group.title"), LocMessage.Of("pin.card.groupGoneTitle"),
                LocMessage.Of("pin.card.goneBeforeBody", PinGrouping.NameOf(serial)));
            return;
        }
        Detach(pin);
        target.Members.Add(pin);
        StarLog.Info($"[Pin] 并入 {target.Name.Render()}＝{target.Members.Count} 张");
        TrayReporter.Report(LocMessage.Of("pin.group.title"), LocMessage.Of("pin.card.joinedTitle"),
            PinGrouping.GroupLabel(target.Name, target.Members.Count));
    });

    /// <summary>从当前组里出来。整组只剩一张时它出来就等于空组 ⇒ 那一组一起消失。</summary>
    public static void LeaveGroup(CaptureOverlayWindow pin) => OnUi(() =>
    {
        var from = GroupRoster.FirstOrDefault(g => g.Members.Contains(pin));
        if (from is null) return;
        Detach(pin);
        StarLog.Info($"[Pin] 已从 {from.Name.Render()} 移出");
        TrayReporter.Report(LocMessage.Of("pin.group.title"), LocMessage.Of("pin.card.leftTitle"),
            LocMessage.Of("pin.card.leftBody", from.Name, from.Members.Count));
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
                TrayReporter.Report(LocMessage.Of("pin.group.title"), LocMessage.Of("pin.card.groupGoneTitle"),
                    LocMessage.Of("pin.card.goneOnClickBody", PinGrouping.NameOf(serial)));
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
            catch (Exception ex) { StarLog.Warn($"[Pin] 关闭{group.Name.Render()}中的一张贴图失败：{ex.Message}"); }
        }
        Prune();
        StarLog.Info($"[Pin] 已关闭 {group.Name.Render()}（{closed} 张）");
        TrayReporter.Report(LocMessage.Of("pin.group.title"), LocMessage.Of("pin.card.groupClosedTitle"),
            PinGrouping.GroupLabel(group.Name, closed));
    }

    private static void ApplyHiddenTo(List<CaptureOverlayWindow> targets, bool hidden, LocMessage scope)
    {
        foreach (var pin in targets)
        {
            if (hidden) pin.HidePin();
            else pin.Present();
        }
        StarLog.Info($"[Pin] {(hidden ? "收起" : "显示")}{scope.Render()}（{targets.Count} 张）");
    }

    /// <summary>整批套穿透并数出被系统拒绝的张数——部分成功是最坏的形状，必须如实说出来。</summary>
    private static void ApplyThroughTo(List<CaptureOverlayWindow> targets, bool through, LocMessage scope)
    {
        var failed = targets.Count(pin => !pin.ApplyClickThrough(through));
        if (failed > 0)
        {
            TrayReporter.Report(LocMessage.Of("pin.card.category"), LocMessage.Of("pin.card.throughPartialTitle"),
                LocMessage.Of("pin.card.throughPartialBody", failed, targets.Count, scope));
            return;
        }
        StarLog.Info($"[Pin] 鼠标穿透＝{(through ? "开" : "关")}（{scope.Render()} {targets.Count} 张）");
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
        // ✅P-80：关掉＝记录与 PNG 一起摘（退出收尾时 DropPersisted 自己会早退保记录）。
        DropPersisted(pin);
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

    // ────────── ✅P-80：贴图持久化（每张＝PNG＋一行元数据，启动按元数据重建） ──────────

    /// <summary>贴图存档（pins.json＋pin-{Id}.png；缺档/坏档/降位的口径见 <see cref="PinStore"/>）。</summary>
    private static readonly PinStore Store = new();

    /// <summary>窗 → 它那行记录的 Id。首建由 <see cref="PersistNew"/> 独占登记，关掉时 <see cref="DropPersisted"/> 摘除。</summary>
    private static readonly Dictionary<CaptureOverlayWindow, string> Records = new();

    /// <summary>正在写 PNG 的窗（串行化：同一张贴图同时只允许一趟在写）。</summary>
    private static readonly HashSet<CaptureOverlayWindow> PngWriting = new();

    /// <summary>这一趟写完还要再写一次的窗（写盘途中内容又变了——落盘以最后一次为准，不丢）。</summary>
    private static readonly HashSet<CaptureOverlayWindow> PngPending = new();

    /// <summary>
    /// 正在恢复存档：恢复链带出来的钩子（<c>HidePin</c>/<c>SetOpacity</c>/<c>ApplyClickThrough</c> 的尾钩）
    /// 一律早退，否则恢复一张就 Upsert 一张——拿刚读出来的值再写回去，白做一趟还可能与随后的删除竞争。
    /// </summary>
    private static bool _restoring;

    /// <summary>退出收尾中：关窗不删记录（下次启动要回来），状态钩子不再写盘。</summary>
    private static bool _shutdown;

    /// <summary>落盘失败的 toast 本会话只弹一次（其后走节流日志）：弹十二次与弹一次说的是同一句话。</summary>
    private static bool _persistFailNoticed;

    /// <summary>
    /// 退出收尾的第一步（主窗 <c>ExitApp</c> 首行调用；更新交棒走同一条路）。
    /// 此后关窗不删记录、状态钩子不再写盘——名册里那几张的元数据原样留给下一次启动。
    /// </summary>
    internal static void EnterShutdown() => _shutdown = true;

    /// <summary>
    /// 状态变了（穿透/收起/唤回/透明度/缩放落定/拖动落定）：只重写那一行元数据，像素没变不动 PNG。
    /// 只对 Records 里已登记的窗落盘——首建走 <see cref="PersistNew"/>，登记之前的状态变化由首建那一拍一并收进。
    /// </summary>
    internal static void NotePinStateChanged(CaptureOverlayWindow pin)
    {
        if (_restoring || _shutdown) return;
        if (!Records.TryGetValue(pin, out var id)) return;
        Upsert(pin, id);
    }

    /// <summary>内容变了（Rebake 重烤）：元数据与 PNG 都要重写——标注烤进画面之后，落盘的那份像素才是真的。</summary>
    internal static void NotePinContentChanged(CaptureOverlayWindow pin)
    {
        if (_restoring || _shutdown) return;
        if (!Records.TryGetValue(pin, out var id)) return;
        Upsert(pin, id);
        QueuePinPng(pin, id);
    }

    /// <summary>把这张窗的当前状态写回它那行记录（按 Id 找；找不到＝记录行不在了，补一行——自愈，不让单行坏档拖累整张窗）。</summary>
    private static void Upsert(CaptureOverlayWindow pin, string id)
    {
        var rect = WindowInterop.GetWindowRect(pin);
        Store.Mutate(data =>
        {
            var rec = data.Pins.FirstOrDefault(r => r.Id == id);
            if (rec is null)
            {
                rec = new PinRecord { Id = id };
                data.Pins.Add(rec);
            }
            rec.X = rect.X;
            rec.Y = rect.Y;
            rec.Zoom = pin.Zoom;
            rec.Opacity = pin.Opacity;
            rec.ClickThrough = pin.IsClickThrough;
            rec.Hidden = pin.IsHidden;
        });
    }

    /// <summary>
    /// 首建登记（Add 尾调用）：发 Id、登记名册、写第一行记录、落第一份 PNG。
    /// <b>独占这件事的理由</b>：登记之前的尾钩子（Add 流程里 HidePin/ApplyClickThrough 带出来的）查不到登记只会早退，
    /// 若不把"发 Id＋登记＋落盘"并在同一拍，那张窗就有一段"看得见、没记录"的窗口期。
    /// </summary>
    private static void PersistNew(CaptureOverlayWindow pin)
    {
        var id = Guid.NewGuid().ToString("N");
        Records[pin] = id;
        Upsert(pin, id);
        QueuePinPng(pin, id);
    }

    /// <summary>把这张窗的像素排进写盘队列。上一趟还在写时只挂名（<see cref="PngPending"/>），
    /// 写完那趟的循环自会接上；双集合（Writing/Pending）保证同一张窗同时只有一趟在写，且"写盘中途又变了"不丢。</summary>
    private static void QueuePinPng(CaptureOverlayWindow pin, string id)
    {
        if (PngWriting.Contains(pin))
        {
            PngPending.Add(pin);
            return;
        }
        WritePinPngFileAsync(pin, id);
    }

    /// <summary>
    /// PNG 落盘（async void：调用点全在 UI 线程的事件链上，这里的异常自己收——漏收就是又一次"关不掉也看不见"的闪退）。
    /// 编码走 <c>GdiScreenCapture.EncodePngAsync</c>（已 Seek(0)；编码只有这一处主人）；
    /// <b>刻意不用 SavePngAsync</b>——它把一切异常吞成一句 false，"没存下来"从此说不出原因。
    /// 写完检查 <see cref="PngPending"/>：内容又变过就再写一趟，直到追平。
    /// <para>已知残留竞态（接受并记录）：这一趟还在写时窗被关掉，写完落在 File.Delete 之后——
    /// 留下一份孤儿 PNG，由下一次启动的 <see cref="PruneOrphanPngs"/> 收走。</para>
    /// </summary>
    private static async void WritePinPngFileAsync(CaptureOverlayWindow pin, string id)
    {
        PngWriting.Add(pin);
        try
        {
            while (true)
            {
                if (pin.PinContent is { } pixels)
                {
                    var encoded = await GdiScreenCapture.EncodePngAsync(pixels, pin.ContentWidth, pin.ContentHeight);
                    if (encoded is null)
                        StarLog.Warn($"[Pin] PNG 编码交不出来（尺寸或缓冲不对），保留旧档 ({Store.PngPathOf(id)})");
                    else
                    {
                        Directory.CreateDirectory(Store.PngDirectory);
                        using (var file = File.Create(Store.PngPathOf(id)))
                            await encoded.AsStreamForRead().CopyToAsync(file);
                        encoded.Dispose();
                    }
                }
                if (!PngPending.Remove(pin)) break;
            }
        }
        catch (Exception ex)
        {
            ReportPersistFailure(ex);
        }
        finally
        {
            PngWriting.Remove(pin);
        }
    }

    /// <summary>
    /// 落盘失败要开口（toast 本会话一次，其后节流日志）：恒报成功是一句谎话——
    /// 重启后那张不在了，用户无从知道为什么。元数据（pins.json）那边 Save 自己收异常只记日志：
    /// 两份档性质不同，像素丢一份还能活，元数据失败时把 toast 弹成瀑布才是新问题。
    /// </summary>
    private static void ReportPersistFailure(Exception ex)
    {
        StarLog.Error("[Pin] 贴图落盘失败", ex);
        if (_persistFailNoticed)
        {
            StarLog.WarnThrottled("pin-persist-fail", $"[Pin] 贴图落盘再次失败：{ex.Message}");
            return;
        }
        _persistFailNoticed = true;
        TrayReporter.Report(LocMessage.Of("pin.card.category"), LocMessage.Of("pin.card.persistFailedTitle"),
            LocMessage.Of("pin.card.persistFailedBody", LocMessage.Of("common.rawValue", ex.Message)));
    }

    /// <summary>
    /// 这一张被用户关掉：摘挂名、摘登记、删记录行、删 PNG。
    /// 退出收尾（<see cref="_shutdown"/>）时直接早退——那时关窗是收尾的一部分，记录要留给下次启动，删了就是"退出即清空"。
    /// </summary>
    private static void DropPersisted(CaptureOverlayWindow pin)
    {
        if (_shutdown) return;
        PngPending.Remove(pin);
        if (!Records.Remove(pin, out var id)) return;
        Store.Mutate(data => data.Pins.RemoveAll(r => r.Id == id));
        try { File.Delete(Store.PngPathOf(id)); } catch { }
    }

    /// <summary>
    /// 启动恢复：按 pins.json 逐条重建（主窗在组件恢复任务之后单独 enqueue 这一趟）。
    /// <para>上限（<see cref="CaptureGeometry.MaxPins"/>）满了就停在原地：多出来的记录<b>不删</b>——
    /// 今天满着，下次启动未必；删了就把"关掉一张就能回来"变成"再也回不来"。
    /// PNG 缺档/解不开的记录当场清行，读盘瞬时失败的记录保住下次再试；
    /// 最后把目录里没有记录引用的孤儿 PNG 收走。</para>
    /// </summary>
    internal static void RestorePersisted()
    {
        if (_shutdown) return;
        var data = Store.Load();
        var removed = new List<string>();
        _restoring = true;
        try
        {
            foreach (var rec in data.Pins)
            {
                if (Pins.Count >= CaptureGeometry.MaxPins) break;
                RestoreOne(rec, removed);
            }
        }
        finally
        {
            _restoring = false;
        }
        if (removed.Count > 0)
            Store.Mutate(d => d.Pins.RemoveAll(r => removed.Contains(r.Id)));
        PruneOrphanPngs();
    }

    /// <summary>
    /// 重建一张。PNG 三态：<b>缺档</b>（FileNotFoundException）删记录、<b>瞬时读失败</b>保记录下次再试、
    /// <b>解不开</b>删记录；建窗先按全屏并集粗收边，窗体链自己会再按所在屏的工作区精收边（两条判据同一主人 PinOrigin）。
    /// </summary>
    private static void RestoreOne(PinRecord rec, List<string> removed)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(Store.PngPathOf(rec.Id));
        }
        catch (FileNotFoundException)
        {
            removed.Add(rec.Id);
            StarLog.Warn($"[Pin] 恢复跳过一张：PNG 缺档 ({rec.Id})");
            return;
        }
        catch (Exception ex)
        {
            StarLog.WarnThrottled($"pins-restore:{rec.Id}", $"[Pin] 读贴图 PNG 失败，记录保留下次再试：{ex.Message}");
            return;
        }
        if (!ClipboardPayload.TryDecodePng(bytes, out var frame, out var decodeProblem))
        {
            removed.Add(rec.Id);
            StarLog.Warn($"[Pin] 恢复跳过一张：PNG 解不开 ({rec.Id})：{decodeProblem}");
            return;
        }
        try
        {
            var (w, h) = CaptureGeometry.PinPixelSize(frame.Width, frame.Height, rec.Zoom);
            var origin = CaptureGeometry.PinOrigin((int)Math.Round(rec.X), (int)Math.Round(rec.Y), w, h, UnionMonitorBounds());
            var pin = new CaptureOverlayWindow(frame.Bgra, frame.Width, frame.Height,
                new IntRect(origin.X, origin.Y, frame.Width, frame.Height), rec.Zoom);
            Records[pin] = rec.Id;
            Pins.Add(pin);
            pin.ApplyPersistedState(rec);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[Pin] 恢复一张贴图失败，记录保留 ({rec.Id})：{ex.Message}");
        }
    }

    /// <summary>
    /// 全部显示器的整屏矩形并集（物理像素）。一块都枚举不到时交回空矩形——
    /// <see cref="CaptureGeometry.PinOrigin"/> 对空工作区的口径是"原样放行"，恢复的窗不会因此被甩到 (0,0)。
    /// </summary>
    private static IntRect UnionMonitorBounds()
    {
        var union = new IntRect(0, 0, 0, 0);
        foreach (var monitor in WindowInterop.ListMonitors())
        {
            var bounds = new IntRect(monitor.Bounds.X, monitor.Bounds.Y, monitor.Bounds.Width, monitor.Bounds.Height);
            if (union.IsEmpty)
            {
                union = bounds;
                continue;
            }
            var left = Math.Min(union.X, bounds.X);
            var top = Math.Min(union.Y, bounds.Y);
            union = new IntRect(left, top,
                Math.Max(union.Right, bounds.Right) - left,
                Math.Max(union.Bottom, bounds.Bottom) - top);
        }
        return union;
    }

    /// <summary>
    /// 收走目录里没有记录引用的 pin-*.png（记录清行之后留下的像素、写盘竞态的孤儿）。
    /// 列目录失败只记一行：清理是收尾的收尾，不该挡住恢复主路径。
    /// </summary>
    private static void PruneOrphanPngs()
    {
        try
        {
            if (!Directory.Exists(Store.PngDirectory)) return;
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in Store.Load().Pins)
                referenced.Add(Path.GetFileName(Store.PngPathOf(rec.Id)));
            foreach (var file in Directory.GetFiles(Store.PngDirectory, "pin-*.png"))
                if (!referenced.Contains(Path.GetFileName(file)))
                    try { File.Delete(file); } catch { }
        }
        catch (Exception ex)
        {
            StarLog.WarnThrottled("pins-prune", $"[Pin] 清理孤儿贴图 PNG 未完成：{ex.Message}");
        }
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
