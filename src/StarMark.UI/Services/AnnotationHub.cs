#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Abstractions;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using StarMark.Core.Hotkeys;
using StarMark.UI.Helpers;

namespace StarMark.UI.Services;

/// <summary>
/// 屏幕标注系统的<b>唯一裁判</b>（架构方案 §3.1）：整台机器上"此刻谁在吃鼠标"只有这里有一份答案。
/// 画布玻璃、截图遮罩、贴图、工具条都不再自己记模式布尔位——它们是这个状态机的<b>执行者</b>。
/// <para>
/// <b>为什么必须有这一处</b>：三套宿主各自持有屏幕所有权、各自自愈，任何一方都无法在按下热键那一刻
/// 回答"现在归谁"，于是重影、串键、截胡、Z 序拉扯只能靠不断新增特判来压（方案 §1 的 R1）。
/// 特判数量随宿主组合数增长，这是治标的稳态而非暂态。
/// </para>
/// <para>三条硬规矩：</para>
/// <para>① <b>只有 <see cref="Raise"/> 能改会话态</b>，且转移表在 Core（<see cref="AnnotationSessions"/>）。
/// 任何公开入口（热键／托盘／工具条按钮／截图收尾）都折成一个事件。</para>
/// <para>② <b>一次转移四件副作用按固定顺序做完</b>：宿主开关 → 显隐与样式 → Z 序归位 → 热键投影重注册。
/// 顺序写反过一次就是一次真机回归（摘键晚于建窗＝截图第一帧仍能唤起画布）。</para>
/// <para>③ <b>每帧只在这一处对账</b>（<see cref="AuditFrame"/>）：状态说的、窗口记的、窗口实际带的样式位，
/// 三者不一致才动窗，动完必须发事件让状态行跟着改口。</para>
/// </summary>
public static class AnnotationHub
{
    public static AnnotationStage Stage { get; private set; } = AnnotationStage.Idle;

    /// <summary>状态变了——工具条的状态行、托盘勾选、热键面板都订阅它。</summary>
    public static event Action<AnnotationStage>? StageChanged;

    public static bool IsBoardRunning => Stage.IsBoard();

    /// <summary>截图会话在场（热键投影与玻璃显隐都问这一句，不许各自再判一次 <c>_busy</c>）。</summary>
    public static bool IsSheetActive => Stage.SuppressesCanvasHotkeys();

    /// <summary>"画布主动把鼠标交回去了"的原因，工具条状态行跟着显示一次。没有这句话，用户只会觉得软件自己抽风。</summary>
    public static string? Notice { get; private set; }

    /// <summary>
    /// 画布玻璃<b>底下那块背景</b>（透明 / 白板底）——会话态的一部分，所以住在这里而不是宿主里。
    /// <para>写入点只有 <see cref="ToggleBackdrop"/> 与"板子关掉时复位"两处；宿主（CanvasService / 那扇 Win32 窗）
    /// 只是它的执行者。§3.2 那句话对这个开关同样成立：多存一份就必然分岔，这里的分岔形状是"按钮说白板开着、屏幕却是桌面"。</para>
    /// </summary>
    public static CanvasBackdrop Backdrop { get; private set; } = CanvasBackdrop.Transparent;

    /// <summary>
    /// 临时摘掉穿透的那一按正在飞行中（按住即画）。<b>这是唯一合法的"状态说穿透、窗口不穿透"时刻</b>，
    /// 每帧对账必须跳过它——按状态去"修"会把按住即画那一条打断（批次 WO 定下来的豁免）。
    /// </summary>
    public static bool QuickPressInFlight { get; set; }

    /// <summary>进截图之前画板所在的子态；离开时照着它恢复（"截完图回不来"那一类错误的结构性解法）。</summary>
    private static AnnotationStage? _boardBeforeSheet;

    private static IntPtr _yieldedTo;
    private static long _lastLayerFixMs;

    /// <summary>与自家窗来回提层的拉锯要避免：一次 SetWindowPos 每屏一发，两秒最多重来一次。</summary>
    private const long LayerFixGapMs = 2_000;

    /// <summary>
    /// 唯一的会话写入入口。四件副作用按 <see cref="Apply"/> 里的固定顺序做，做完广播新态。
    /// <para><b>Sheet 期间按画布开关要说得出原因</b>：那条键这时是故意继续注册着的
    /// （摘掉就成了哑键，用户只知道"按了没反应"）。回执与转移判定同在一处，别处再写一份就分岔。</para>
    /// </summary>
    public static void Raise(SessionEvent what)
    {
        // 窗口、定时器与热键注册都必须在 UI 线程上做：托盘/菜单那类入口的回调线程不保证，
        // 而在别的线程上 new Window 会直接崩——所以这里显式回主线程，不指望调用方恰好在对的线程上。
        var queue = App.MainWindow?.DispatcherQueue;
        if (queue is { HasThreadAccess: false })
        {
            queue.TryEnqueue(() => Raise(what));
            return;
        }
        var from = Stage;
        // 背景态是一条真正的事件（它改的是"这块玻璃底下是什么"），但它的落点不是一个新会话态，
        // 所以在这里分派，不塞进 Move 那张按态表——那张表回答的始终是"此刻谁吃鼠标"。
        if (what == SessionEvent.ToggleBackdrop)
        {
            ToggleBackdrop();
            return;
        }
        var to = AnnotationSessions.Move(from, what, _boardBeforeSheet);
        if (what == SessionEvent.ToggleBoard && from.IsSheet())
        {
            Report("画布现在按不动", HotkeyGate.ReasonNotRunning(
                HotkeyActions.CanvasToggle, true, AnnotationStage.Sheet) ?? "截图进行中");
            return;
        }
        if (to == from) return;
        // 背景态闸门（判据在 Core，与"上白板要收回鼠标"是同一枚的两面）：白板态不许落进穿透。
        // 被拦时那句原因必须看得见——"点了没反应"与"功能坏了"在用户眼里是同一件事。
        if (AnnotationSessions.IsBlockedByBackdrop(what, Backdrop))
        {
            Notice = AnnotationSessions.ReasonBlockedByBackdrop(what, Backdrop);
            CanvasService.RaiseStateChanged();
            return;
        }
        if (what == SessionEvent.BeginSheet) _boardBeforeSheet = from.IsBoard() ? from : null;
        Stage = to;
        if (what == SessionEvent.EndSheet) _boardBeforeSheet = null;
        if (!Apply(from, to)) Stage = from;      // 宿主开不起来（没屏、建窗失败）就退回原态，别把状态留在"说有板子却没有板子"
        StageChanged?.Invoke(Stage);
    }

    /// <summary>
    /// 副作用四步。<b>返回 false＝这一步没做成</b>（画布宿主建不起来），调用方把状态退回去。
    /// </summary>
    /// <summary>
    /// 副作用四步。<b>返回 false＝这一步没做成</b>（画布宿主建不起来），调用方把状态退回去。
    /// <para>
    /// <b>宿主开／关问的是"这块板子该不该存在"与"它现在在不在"，不是"进没进截图态"</b>。
    /// 截图态对画布只有一件事：把玻璃<b>藏起来</b>（§5——用户看到的是一次冻帧，不是板子被拆了又重建）。
    /// 从前这两臂写的是 <c>!from.IsBoard() &amp;&amp; to.IsBoard()</c> / <c>from.IsBoard() &amp;&amp; !to.IsBoard()</c>，
    /// 而 Sheet <b>不是</b> Board：Board→Sheet 正好落进"关宿主"那一臂，<c>CloseBoardHost()</c> 里的
    /// <c>Screens.Clear()</c> 把每块屏的笔迹连同宿主一起清掉，截图结束再建回一块空板子。
    /// 真机症状＝"画布上画的东西一按 F1 就全没了"（撤销栈里也没了，撤不回来）。
    /// </para>
    /// </summary>
    private static bool Apply(AnnotationStage from, AnnotationStage to)
    {
        if (to.IsBoard() && !CanvasService.IsRunning)
        {
            if (!CanvasService.OpenBoardHost()) return false;
        }
        else if (to == AnnotationStage.Idle && CanvasService.IsRunning)
        {
            // 板子收了，背景态跟着回到默认：留着"白板开着"下一次开板子就是一屏莫名其妙的白
            Backdrop = CanvasBackdrop.Transparent;
            CanvasService.CloseBoardHost();
        }

        CanvasService.ApplyStage(to);
        LayerDirector.EnforceOrder(to);
        ReapplyHotkeys();
        Notice = null;
        _yieldedTo = IntPtr.Zero;
        return true;
    }

    /// <summary>
    /// 换背景态（工具条那颗「白板」与 <c>canvas.board</c> 都折到这一句）。
    /// <para><b>板子还没开着时按它＝先开板子再上白板</b>：讲解的人按「白板」就是要一块白板，
    /// 多一步"先按另一颗键把板子叫出来"是缺陷（与三条工具热键同一口径，见 <see cref="CanvasService.SelectTool"/>）。</para>
    /// <para>次序是<b>先落地那块底、再补鼠标事件</b>：补的那一条会走 <see cref="Apply"/> 把显隐与样式位按新态
    /// 写一遍，底先落地就不会留下"白板已上、样式还是穿透"的那一帧（那正是批次 WO 定性过的"状态说的与窗口做的不一致"）。</para>
    /// </summary>
    private static void ToggleBackdrop()
    {
        if (Stage.IsSheet())
        {
            Report("画布现在按不动", HotkeyGate.ReasonNotRunning(
                HotkeyActions.CanvasToggle, true, AnnotationStage.Sheet) ?? "截图进行中");
            return;
        }
        var next = CanvasBackdropMath.Next(Backdrop);
        if (!Stage.IsBoard())
        {
            Raise(SessionEvent.ToggleBoard);
            if (!Stage.IsBoard()) return;         // 板子开不起来：那句原因由 OpenBoardHost 给过，不重复播报
        }
        Backdrop = next;
        CanvasService.ApplyBackdrop(next);
        // 上白板必须同时把鼠标收回给画布（Core 那一枚判据的另一面）；下白板不动鼠标归属——
        // 用户原来在绘制态就继续绘制，原来要交出去的不该被这次操作顺手改掉。
        if (AnnotationSessions.PointerConsequenceOf(next) is { } consequence) Raise(consequence);
        else CanvasService.RaiseStateChanged();   // 态没挪时 Raise 会短路，这颗按钮的高亮得自己跟着改口
    }

    /// <summary>
    /// 把会话收干净（设置里关掉「屏幕画布」那颗开关、工具条那颗 ✕）。
    /// <para><b>收尾必须重新看一眼状态，不能拿着进门时那份往下走</b>：先把截图会话取消掉，而"取消"
    /// 会自己走一次 <c>EndSheet</c>——板子本来就开着时它落回冻结前那个子态（还得再 toggle 才到 Idle），
    /// 板子没开时它直接落 <c>Idle</c>。后者要是还无条件补一发 toggle，就是"用户关画布，反手给他开了一块板子"
    /// （真机可复现：只按 F1 截一张图，同时在设置里关掉「屏幕画布」）。</para>
    /// </summary>
    public static void EnsureIdle()
    {
        if (Stage == AnnotationStage.Idle) return;
        if (Stage.IsSheet()) ScreenshotService.CancelActiveSession();
        if (Stage.IsBoard()) Raise(SessionEvent.ToggleBoard);
    }

    /// <summary>
    /// 画布内的 Esc（工具条那扇窗收到的按键）。两级：先收手上那半件事（折线没勾完），再退整块板子。
    /// <para>截图态的 Esc 不进这里——那一侧"编辑文字→收折线→收笔→取消整场"的三级是截图链的成熟资产
    /// （方案 §6.2 沿用），这里只接画布。</para>
    /// </summary>
    public static void EscapeBoard()
    {
        switch (EscapeRouter.Resolve(Stage, CanvasService.HasWorkInProgress))
        {
            case EscapeStep.CloseWorkInProgress: CanvasService.CloseWorkInProgress(); break;
            case EscapeStep.ExitBoard: Raise(SessionEvent.ToggleBoard); break;
            default: break;        // Idle·Esc＝无事发生（不许"顺手关点别的"）
        }
    }

    /// <summary>
    /// 全局「撤销／重做」的落点（方案 §7：栈全局化、按 surface 归属）——<b>注意力在哪块面上就还给它</b>。
    /// <para>
    /// 从前这两条键无条件落在画布板上：桌面上挂着贴图时，用户在贴图里画完按 Ctrl+Alt+U，
    /// 少的是画布上那一笔（他可能几分钟前就没在画布上画了）。"撤错东西"比"撤不动"糟得多，
    /// 因为它看起来是成功了，只是动了不该动的。
    /// </para>
    /// <para>判据只有一份，在 <see cref="InkRouting"/>；这里只做"问前台窗是谁 + 分派"。
    /// Sheet 不需要一条臂：截图会话期间画布那批热键整批不注册（<see cref="HotkeyGate"/>），
    /// 而截图窗自己有窗口内的 Ctrl+Z／Ctrl+Y。</para>
    /// </summary>
    public static void HotkeyUndoRedo(bool redo)
    {
        var foreground = WindowInterop.GetForegroundWindow();
        if (InkRouting.UndoBelongsToFocusedPin(LayerDirector.RoleOf(foreground))
            && PinManager.UndoRedoAtFocusedPin(foreground, redo)) return;
        if (redo) CanvasService.HotkeyRedo();
        else CanvasService.HotkeyUndo();
    }

    /// <summary>
    /// <b>每帧的三件事，只在这一处问</b>：① 样式位与状态对不对得上；② 光标那一层到底归谁；③ 归谁之后该做什么。
    /// <para>②的判据只看进程（批次 WD-8：<c>WindowFromPoint</c> 返回那一点上<b>最深</b>的 HWND，
    /// WinUI 的条子内容住在子窗里，比句柄必然不相等）；自家窗拿走那一按不构成"一次按下两家用"，
    /// 只是这一按没到画布，所以走重排而不是交回鼠标。别的程序占了那一层才交回，并说清原因（用例 7）。</para>
    /// </summary>
    public static void AuditFrame(int cursorX, int cursorY)
    {
        if (!Stage.NeedsFrameAudit()) return;

        if (!QuickPressInFlight && LayerDirector.ReconcileStyles(Stage) > 0)
        {
            // 样式位是刚补的，定序也顺手重来一次（工具条必须仍在最上），并让状态行跟着说实话
            LayerDirector.EnforceOrder(Stage);
            CanvasService.RaiseStateChanged();
        }

        // 手上正有一笔时不验层（那一笔已经归画布画完）；穿透态"不是我们"是设计本意
        if (CanvasService.PressInFlight || !Stage.GlassTakesPointer()) return;
        var ownership = LayerDirector.Classify(cursorX, cursorY, out var hit);
        if (ownership == LayerDirector.LayerOwnership.Unknown) return;
        if (hit == _yieldedTo) return;                       // 同一件事只说一次

        var ours = ownership == LayerDirector.LayerOwnership.Ours;
        if (LayerRules.ShouldReorderFor(Stage, ours))
        {
            var root = LayerDirector.RootOf(hit);
            if (LayerDirector.IsKnown(root)) return;          // 就是条子／面板／玻璃自己：什么都不做
            var now = Environment.TickCount64;
            if (now - _lastLayerFixMs < LayerFixGapMs) return;
            _lastLayerFixMs = now;
            StarLog.Warn($"[Hub] 绘制态发现自家窗口（{WindowInterop.GetClassName(root)}）压在画布上面，已把画布提回工具条之下");
            LayerDirector.EnforceOrder(Stage);
            return;
        }
        if (!LayerRules.ShouldYieldPointer(Stage, ours)) return;

        StarLog.Warn($"[Hub] 绘制态发现光标那一层已被另一个程序占走，主动交回鼠标");
        // 白板态的让位要<b>先放下白板</b>：交出鼠标会被背景态闸门拦住（整屏白墙不许穿透），
        // 而"别人的窗盖在画布上面"这件事仍然得交代清楚。先关白板再按常态交，两句都是真的。
        var droppingBoard = AnnotationSessions.IsBlockedByBackdrop(SessionEvent.GivePointerBack, Backdrop);
        if (droppingBoard) Raise(SessionEvent.ToggleBackdrop);
        Raise(SessionEvent.GivePointerBack);
        Notice = droppingBoard
            ? "已自动关掉白板底并交回鼠标：另一个程序的窗口（例如按 Win 呼出的开始菜单）盖住了画布；要接着用白板请再点「白板」"
            : "已自动交回鼠标：另一个程序的窗口（例如按 Win 呼出的开始菜单）盖住了画布；要接着画请再点「画笔」";
        _yieldedTo = hit;
        StageChanged?.Invoke(Stage);
    }

    /// <summary>
    /// 会话迁移点上重投影热键注册表（<see cref="HotkeyGate"/>）。
    /// <para><b>只重投影、不改绑定</b>：绑定的唯一真源仍是设置里那一份，这里只是把"此刻该注册哪些"
    /// 再问一次。四个 ApplyBindings 入口都调同一个投影函数，托盘/设置页读到的永远是"此刻真的生效的键"。</para>
    /// </summary>
    public static void ReapplyHotkeys()
    {
        try
        {
            if (App.Services?.GetService(typeof(SettingsStore)) is not SettingsStore settings) return;
            if (App.Services?.GetService(typeof(HotkeyService)) is not HotkeyService hotkey) return;
            hotkey.ApplyBindings(settings.LoadEnableGlobalHotKey()
                ? settings.GetRegisterableHotkeyBindings()
                : new Dictionary<string, HotkeyGesture>());
        }
        catch (Exception ex) { StarLog.Warn($"[Hub] 热键重新注册失败：{ex.Message}"); }
    }

    private static void Report(string title, string message)
    {
        var shown = false;
        try { shown = App.MainWindow?.TryShowTrayNotification(title, message) == true; }
        catch (Exception ex) { StarLog.Warn($"[Hub] 回报没送出去：{ex.Message}"); }
        if (!shown)
        {
            try { App.MainWindow?.ShowError(title, message); }
            catch (Exception ex) { StarLog.Warn($"[Hub] 主窗提示条也没能显示：{ex.Message}"); }
        }
        StarLog.Info($"[Hub] {title}：{message}");
    }
}
