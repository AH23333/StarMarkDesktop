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
/// 屏幕画布的编排（规格 §16）：热键进入 → 每屏一块透明玻璃 → 画笔／荧光笔／橡皮 → 快照或退出。
/// <para>
/// 三条设计线：
/// ① <b>持久笔迹与荧光段分两张缓冲</b>——持久层（模型那一份是 <see cref="InkDoc"/>，缓冲是
/// <c>Screen.Persistent</c>）留在
/// <c>Screen.Persistent</c> 里，屏幕上那块 <c>Window.Pixels</c> 每次提交由"铺持久层 → 叠荧光段 →
/// 叠光晕"重算。合成到一处写的代价是淡出/擦除时回不到"原来那块地方画了什么"。
/// ② <b>只提交脏区</b>：4K 全屏整帧提交是 33MB/帧，60fps 下根本不可能（§16.7）。
/// ③ <b>退出只走显式动作</b>（工具条 ✕／Esc／热键／托盘），没有"过一会儿自己关掉"——
/// 讲到一半板子自己消失，比没有板子更糟。
/// </para>
/// </summary>
public static class CanvasService
{
    /// <summary>渲染循环的节拍。30fps 足够让淡出与光晕看着连续，空闲时开销可忽略。</summary>
    private static readonly TimeSpan FrameGap = TimeSpan.FromMilliseconds(33);

    /// <summary>拖动期间的提交节流：鼠标事件比帧密，每个点都提交等于把带宽花在中间态上。</summary>
    private const long DragThrottleMs = 16;

    /// <summary>一块屏上的画布：窗口 + 它自己的两层墨迹 + 待提交区域。</summary>
    private sealed class Screen
    {
        public required LayeredCanvasWindow Window { get; init; }
        public required IntRect Bounds { get; init; }

        /// <summary>这块屏自己的缩放。工具条的尺寸与摆位要用它，混屏时才不会半截在屏外。</summary>
        public required double Scale { get; init; }
        public required uint[] Persistent { get; init; }

        /// <summary>
        /// 这块屏上<b>留下来的</b>笔迹与它自己的历史——类型是两侧共用的那一个 <see cref="InkDoc"/>，
        /// 归属标成 <see cref="SurfaceRole.Board"/>＋屏号（方案 §3.4／§7：渲放宿主只是 InkDoc 的窗口）。
        /// </summary>
        public required InkDoc Ink { get; init; }

        /// <summary>
        /// 手上正在拖的那一条（<b>还没落定</b>）。它刻意不住在 <see cref="Ink"/> 里：
        /// 那一叠是"用户已经画成的东西"，而这条还在每个移动事件里变——把半成品放进历史载体，
        /// 就会出现"撤销退到一半的椭圆"那一类只有真机看得见的错（批次 WO）。
        /// 收手时整份 <c>ToAnnotation()</c> 落进 InkDoc，从此它只是模型里的一条。
        /// </summary>
        public CanvasStroke? Drawing { get; set; }

        public required EphemeralInk Trail { get; init; }

        public List<IntRect> Dirty { get; } = new();

        /// <summary>
        /// <b>曾经被烤进持久层的那一片</b>（不是"现在还剩什么"）。撤销与丢弃一笔时，
        /// <see cref="Recomposite"/> 必须把它连同剩下的笔迹一起擦掉——只按剩下的算区域，
        /// 被撤掉那条自己占过、而剩余笔迹没覆盖到的地方就永远没人清。
        /// <para>真机反馈："撤销只能撤销绘制图形的部分（比如只撤销一个完整椭圆的一半）"：
        /// 椭圆是一条笔迹，撤掉它之后屏幕上还留半只，因为那半只在<b>剩余</b>笔迹的包围盒之外。</para>
        /// </summary>
        public IntRect Composited { get; set; }

        /// <summary>
        /// 上一帧那团光标光晕占的地方。<b>只有它需要无条件复原</b>：光晕跟着鼠标走，
        /// 旧位置不擦就成一坨赖着不走的光斑。
        /// <para>
        /// 这里刻意<b>不再</b>记"上一帧所有荧光段叠过的地方"——那是批次 WG 之前的做法，
        /// 它让每一帧的脏区等于整条笔迹的包围盒，而脏区一大，"从持久层重铺 + 整段重画"就跟着变大，
        /// 一条长笔迹就能把 UI 线程钉到几百毫秒一帧。哪一帧该重算哪一块，现在由
        /// <see cref="EphemeralInk.Segment.PaintScale"/> 与 <see cref="CanvasStroke.TailBounds"/> 分别说。
        /// </para>
        /// </summary>
        public IntRect LastGlow { get; set; }

        /// <summary>这一帧要不要在光标处叠一团光晕、叠在哪（由帧循环按光标落在哪块屏决定）。</summary>
        public PixelPoint? GlowAt { get; set; }

        public long LastFlushMs { get; set; }
    }

    private static readonly List<Screen> Screens = new();
    private static CanvasToolbarWindow? _toolbar;
    private static DispatcherQueueTimer? _frame;
    private static bool _busy;

    private static CanvasTool _tool = CanvasTool.Pen;
    private static int _colorIndex;
    private static int _widthStep = CanvasWidths.DefaultStepIndex;

    /// <summary>
    /// 穿不穿透<b>不在这里存</b>（架构方案 §3.2：宿主不持模式布尔位）。
    /// 状态机在 <see cref="AnnotationHub"/>，这一句只是给工具条那类旧接线留的读法——
    /// 它读的是会话态，不是第二份旗标，所以"工具条说绘制中、窗口却带着穿透位"这种分岔
    /// 少了一个来源（剩下的那一个由 <see cref="LayerDirector.ReconcileStyles"/> 每帧兜）。
    /// </summary>
    private static bool ClickThroughHere => !AnnotationHub.Stage.GlassTakesPointer();
    private static bool _haloEnabled = true;

    /// <summary>
    /// 手上一按是什么性质。<b>穿透态收不到 WM_LBUTTONDOWN</b>（那一次按下归了下层应用），
    /// 所以"荧光笔按住即画 / Ctrl+Alt 快速圈画"只能由帧循环轮询按键状态发现，
    /// 发现后临时摘掉穿透、自己补一次 SetCapture，抬起再恢复。
    /// </summary>
    private enum Press { None, Drawing, Ephemeral, QuickPen, Shape, PolyLine }

    private static Press _press;

    /// <summary>拖图形时那一按的起点（本屏物理像素）。终点就是当前光标，所以只留起点。</summary>
    private static PixelPoint _shapeFrom;

    /// <summary>
    /// 正在勾的<b>折线</b>：已经定形的顶点（本屏物理像素）。null＝没在勾。
    /// <para>它与"一次拖拽定形"的四种图形不同，是<b>跨按</b>的：一次按下拖一段，抬手把终点定成顶点，
    /// 折线还开着；收口由"再点当前工具／Esc／换工具／换粗细"负责。所以它必须有一份
    /// 自己记下的颜色与粗细——中途换了设置，正在勾的这条不能悄悄变浓变细（症状："画着画着笔自己变了"）。</para>
    /// </summary>
    private static List<PixelPoint>? _polyPoints;
    private static Screen? _polyScreen;
    private static int _polyColour;
    private static int _polyWidth;

    /// <summary>手上有笔时它属于哪块屏（抬起/读态收尾都要用它，光标可能已经飘到别的屏）。</summary>
    private static Screen? _pressScreen;

    /// <summary>画布模式是否开着——问的是"有没有玻璃在场"，不再另存一份旗标。</summary>
    public static bool IsRunning => Screens.Count > 0;

    /// <summary>当前是不是鼠标穿透态（工具条据此画那颗按钮的高亮）。</summary>
    public static bool IsClickThrough => ClickThroughHere;

    /// <summary>手上正有一笔吗（每帧验层要跳过它：那一笔已经归画布画完）。</summary>
    public static bool PressInFlight => _press != Press.None;

    /// <summary>有没有"半件事"在场（勾到一半的折线）——Esc 的第一级退的就是它。</summary>
    public static bool HasWorkInProgress => _polyPoints is { Count: > 0 };

    public static CanvasTool Tool => _tool;

    public static int ColorIndex => _colorIndex;

    public static int WidthStep => _widthStep;

    public static bool HaloEnabled => _haloEnabled;

    /// <summary>颜色表沿用截图标注那一份（一条事实一个出处：两处色表迟早分岔）。</summary>
    public static IReadOnlyList<AnnotationColor> Palette => Annotation.Palette;

    /// <summary>状态变了（工具/颜色/粗细/穿透/光晕）——工具条订阅它刷新高亮。</summary>
    public static event Action? StateChanged;

    // ────────── 开关：都是递给 Hub 的事件，这里不自己改态 ──────────

    /// <summary>开或关画板（热键、托盘、主窗菜单都走这一句）。</summary>
    public static void Toggle() => AnnotationHub.Raise(SessionEvent.ToggleBoard);

    /// <summary>旧接线名：开画板。画布已在场时什么都不做（截图期间玻璃也在场，不该被"重开"一次）。</summary>
    public static void Start()
    {
        if (!IsRunning) Toggle();
    }

    /// <summary>旧接线名：关画板（设置页把总开关关掉时调这里）。截图进行中先取消截图，再拆窗。</summary>
    public static void Stop() => AnnotationHub.EnsureIdle();

    /// <summary>
    /// 进入画布模式的<b>宿主动作</b>：每屏一块玻璃。判据与状态都在 <see cref="AnnotationHub"/>，这里只建窗与起帧循环。
    /// <para>某一屏建不起来（刚拔屏、显存吃紧）不牵连别的屏，但<b>一块都没建起来时必须回报并返回 false</b>
    /// ——Hub 会把会话态退回原处，"按了热键屏幕什么都没变"是这套功能最坏的失败方式。</para>
    /// </summary>
    public static bool OpenBoardHost()
    {
        if (IsRunning) return true;
        if (_busy) return false;
        // 总开关（设置 → 拓展功能 →「屏幕画布」）。所有入口都汇到 Start()，所以闸门只在这一处：
        // 关着时热键那条只剩 canvas.toggle 还注册着（见 SettingsStore.GetRegisterableHotkeyBindings），
        // 按它要听见这句原因——一条什么都不发生的哑键是最坏的收尾。
        if (!EnabledBySetting)
        {
            Report("屏幕画布已关闭", "要在 设置 → 拓展功能 的「屏幕画布」里打开；打开后这条快捷键就回来了");
            return false;
        }
        _busy = true;
        try
        {
            // 玻璃的命中测试要问"这一点在不在自家条子上"。判据只有 LayerDirector 那一份（它读名册里
            // 那些条子窗的矩形，不另存旗标），这里只把它接到那扇纯 Win32 窗上——那一层不认识工具条，也不认识 Core。
            LayeredCanvasWindow.ChromeUnderPoint = LayerDirector.IsPointOnChrome;
            var monitors = WindowInterop.ListMonitors();
            if (monitors.Count == 0)
            {
                Report("画布打不开", "系统没有报告任何显示器");
                return false;
            }
            foreach (var monitor in monitors)
            {
                var bounds = new IntRect(monitor.Bounds.X, monitor.Bounds.Y,
                    monitor.Bounds.Width, monitor.Bounds.Height);
                try
                {
                    var window = new LayeredCanvasWindow(bounds);
                    // 换分辨率／拔屏之后的重建也要把背景态原样还回来（真值只在 Hub 那一份，这里只是交给新建的这扇窗）。
                    // 少这一句的症状很具体：重新插一次显示器，别的屏还是白板、这块屏变成桌面。
                    window.SetBackdrop(CanvasBackdropMath.ArgbOf(AnnotationHub.Backdrop));
                    // 持久层同样要以"空白"起步：它是 Flush 时铺到屏幕上的那张底图，
                    // 留 0 就等于把"这块玻璃在鼠标眼里不存在"重新写回去（只在擦过的地方发作）
                    var persistent = new uint[window.Width * window.Height];
                    Array.Fill(persistent, LayeredCanvasWindow.BlankPixel);
                    var screen = new Screen
                    {
                        Window = window,
                        Bounds = bounds,
                        Scale = monitor.Scale,
                        Persistent = persistent,
                        Ink = new InkDoc(new InkSurface(SurfaceRole.Board, Screens.Count)),
                        Trail = new EphemeralInk { CursorHaloEnabled = _haloEnabled },
                    };
                    window.PointerPressed += p => OnPressed(screen, p);
                    window.PointerMoved += p => OnMoved(screen, p);
                    window.PointerReleased += p => OnReleased(screen, p);
                    // 右键＝把鼠标交还给下面的应用。画布上右键没有别的用途，而"看不见光标、找不到工具条"时
                    // 人只剩点鼠标这一件事可做——热键与托盘都是键盘式出口，不算自救路径。
                    window.RightPressed += () => SetClickThrough(true);
                    // 分辨率/拓扑一变，这块缓冲的尺寸就是错的了：重建，不凑合画
                    window.DisplayChanged += () =>
                    {
                        StarLog.Info("[Canvas] 分辨率或显示器变了，重建画布");
                        RebuildHost();
                    };
                    // 登记进 Z 序名册，并把"读样式位 / 按状态改样式位"两句话交给 LayerDirector：
                    // 每帧对账要问的"窗口此刻真的带着穿透位吗"只有这一处答得出，别处再写一份就是第二把尺子。
                    LayerDirector.Register(SurfaceRole.Board, window.Hwnd,
                        () => window.StyleClickThrough, on => window.SetClickThrough(on));
                    Screens.Add(screen);
                }
                catch (Exception ex)
                {
                    StarLog.Error($"[Canvas] 这一屏的透明层建不起来（{bounds.Width} × {bounds.Height}）", ex);
                }
            }
            if (Screens.Count == 0)
            {
                Report("画布打不开", "每一屏的透明层都没能建起来（原因见日志）");
                return false;
            }

            // 帧循环与工具条都要在 UI 线程上建（Hub 的 Raise 已经把回调搬到 UI 线程，这里只是兜底）
            var queue = App.MainWindow?.DispatcherQueue;
            if (queue is null)
            {
                CloseBoardHost();
                Report("画布打不开", "主窗还不存在（应用还没起完？）");
                return false;
            }
            _frame ??= BuildFrameTimer(queue);
            _frame.Stop();
            _frame.Start();
            ShowToolbar();
            StarLog.Info($"[Canvas] 画布宿主已就位：{Screens.Count} 屏，工具={_tool}");
            return true;
        }
        catch (Exception ex)
        {
            CloseBoardHost();
            StarLog.Error("[Canvas] 开启画布模式失败", ex);
            Report("画布打不开", ex.Message);
            return false;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// 退出画布模式的<b>宿主动作</b>：拆窗、停帧循环、收条子。笔迹随之丢弃（要留就先"存图／贴图"）。
    /// <para>穿透那位不在这里复位：下一次开板子落在哪一态由状态机定（默认穿透），
    /// 在这里顺手改一次就是第四份状态书写点。</para>
    /// </summary>
    public static void CloseBoardHost()
    {
        _frame?.Stop();
        CloseToolbar();
        foreach (var screen in Screens)
        {
            LayerDirector.Unregister(screen.Window.Hwnd);
            try { screen.Window.Dispose(); }
            catch (Exception ex) { StarLog.Warn($"[Canvas] 透明层没关干净：{ex.Message}"); }
        }
        Screens.Clear();
        LayerDirector.ForgetRole(SurfaceRole.Board);
        _press = Press.None;
        // 勾到一半的折线随窗口一起忘掉：层都没了，还留着点引用就是把已释放的对象留在静态字段上
        _polyPoints = null;
        _polyScreen = null;
        AnnotationHub.QuickPressInFlight = false;
        StarLog.Info("[Canvas] 画布模式关闭");
    }

    /// <summary>分辨率/拓扑变了：按新拓扑重建宿主，<b>会话态原样保留</b>（用户没关画布，不该被重建顺手关掉）。</summary>
    private static void RebuildHost()
    {
        if (!IsRunning) return;
        CloseBoardHost();
        if (OpenBoardHost()) ApplyStage(AnnotationHub.Stage);
        else AnnotationHub.Raise(SessionEvent.ToggleBoard);   // 一块都建不起来：会话态也得跟着退回 Idle
    }

    /// <summary>
    /// 按会话态把<b>玻璃与条子的显隐、样式位</b>落一遍（Hub 副作用的第二步）。
    /// <para><b>Sheet 期间玻璃与画布工具条都不在场</b>：遮罩窗显示的是冻帧，玻璃再亮着就是同一份笔迹的两份
    /// （重影），两条栏同时飘着就是"哪一条管当前这件事"说不清。截图带不带笔迹只由
    /// <c>ScreenshotService.Grab</c> 那一下决定，与会话进行中玻璃亮不亮无关。</para>
    /// </summary>
    public static void ApplyStage(AnnotationStage stage)
    {
        // 换态之前先把手上那条收掉：不先收的话，症状是"画着画着笔自己变粗/换了颜色还接到上一条上"，
        // 而切到穿透态时那一笔永远不会收到"抬起"（穿透之后鼠标归了下层应用）。
        CommitOpenStroke();
        var visible = stage.GlassVisible();
        // 穿透位怎么给，只问 Core 那张按态给的表。"点得到工具条"不押在这一位上（那是每帧翻的，晚一帧
        // 就是把那一按画到条子上）——它由玻璃的命中测试当场让开，见 LayeredCanvasWindow.ChromeUnderPoint。
        var glassThrough = LayerRules.ShouldGlassBeClickThrough(stage);
        if (!visible) LayeredCanvasWindow.ReleasePointerCapture();
        foreach (var screen in Screens)
        {
            screen.Window.SetVisible(visible);
            screen.Window.SetClickThrough(glassThrough);
            screen.Window.SetDrawCursor(!glassThrough);
        }
        if (_toolbar is not null) _toolbar.SetStripVisible(stage.BoardStripVisible());
        RaiseStateChanged();
    }

    // ────────── 工具与动作（工具条／热键／托盘都走这里）──────────

    /// <summary>
    /// 选工具＝顺带决定这一态拦不拦鼠标，<b>但方向判据不在这儿也不在 Hub 现写</b>：
    /// 由 <see cref="AnnotationSessions.BoardAfterToolSelect"/> 给（Core 纯函数，三臂单测）。
    /// 这里只把"选了哪支笔"这个事实报给状态机，穿透位由状态机反过来指挥窗口。
    /// </summary>
    public static void SelectTool(CanvasTool tool)
    {
        CommitOpenStroke();                       // 先收手上那条：不然它会接到新工具的设置上
        _tool = tool;
        // 2026-09-27 用户改判：<b>换工具不再翻穿透态</b>（从前选荧光笔留在穿透、选画笔进绘制，是 §16.5.2
        // 的"零摩擦"，也是"点一下工具结果鼠标归属变了"的来源）。要画就用那颗「穿透」按钮或 canvas.through
        // 关掉穿透，两态各管各的。所以这里没有 Raise：不产生迁移，只把当前态该长的样子重放一遍
        // （高亮、光标、状态行都要跟着换笔走——Raise 在态没变时短路，不重放就会出现"点了没反应"）。
        if (AnnotationHub.Stage == AnnotationStage.Idle)
        {
            // 唯一的例外：板子还没开着时按那三条工具热键（画笔／荧光笔／橡皮）不该是哑键——
            // 这一按的意思是"我要开始标注了"，所以把板子开起来（默认落穿透态，由转移表保证）。
            AnnotationHub.Raise(SessionEvent.ToggleBoard);
        }
        ApplyStage(AnnotationHub.Stage);
    }

    /// <summary>再点当前选中的笔＝收笔回穿透态（与截图/贴图那条"再点取消选择"同一交互语言）。</summary>
    public static void ToggleTool(CanvasTool tool)
    {
        // 换工具本身不再改穿透态（见 SelectTool）；这里只剩"再点当前那一支＝取消选择"，
        // 它等价于把鼠标还给下层应用——所以仍然是一次真正的会话事件，不是特例。
        if (_tool == tool && !ClickThroughHere) AnnotationHub.Raise(SessionEvent.GivePointerBack);
        else SelectTool(tool);
    }

    public static void SelectColor(int index)
    {
        if (index < 0 || index >= Palette.Count) return;
        _colorIndex = index;
        RaiseStateChanged();
    }

    public static void SelectWidth(int step)
    {
        CommitOpenStroke();
        _widthStep = Math.Clamp(step, 0, CanvasWidths.Steps.Length - 1);
        RaiseStateChanged();
    }

    /// <summary>
    /// 鼠标穿透。<b>开启后画布收不到任何鼠标事件</b>，所以出口不能只有工具条上那一颗：
    /// 全局热键、托盘、以及"工具条自己始终能被点"三条一起兜着（§16.6 点名的"找不回"）。
    /// <para>这一句现在只是<b>把意图折成事件递给 Hub</b>。以前它自己改那一位再逐屏写样式，
    /// 于是"用户按穿透按钮"与"另一个程序占了那一层"和"截图结束复位"三条路各写一次同一位——
    /// 那就是状态与样式分岔的三个来源（批次 WO）。收口之后只有一条路能改这一位。</para>
    /// </summary>
    public static void SetClickThrough(bool on)
        => AnnotationHub.Raise(on ? SessionEvent.GivePointerBack : SessionEvent.TakePointer);

    /// <summary>光标光晕开关（关掉＝荧光笔态下鼠标不再有那团颜色跟着走）。</summary>
    public static void SetHalo(bool on)
    {
        _haloEnabled = on;
        foreach (var screen in Screens) screen.Trail.CursorHaloEnabled = on;
        RaiseStateChanged();
    }

    /// <summary>
    /// 把背景态落到每块屏的那扇玻璃上。<b>只由 <see cref="AnnotationHub"/> 调用</b>（事件那一侧）：
    /// 宿主不自己翻这块底，否则"按钮说白板开着、屏幕却还是桌面"就有了第二个书写点（§3.2 同一课）。
    /// <para>逐屏换底之后各窗自己整块重交一次（<c>SetBackdrop</c> 里做），因为 DIB 里存的是上一次合成好的结果，
    /// 只补脏区会把脏区之外那些行留在旧底上——屏幕上就留下一块洗不掉的白或一块没有底的透明。</para>
    /// </summary>
    public static void ApplyBackdrop(CanvasBackdrop backdrop)
    {
        var argb = CanvasBackdropMath.ArgbOf(backdrop);
        foreach (var screen in Screens) screen.Window.SetBackdrop(argb);
        RaiseStateChanged();
    }

    /// <summary>
    /// 幕布那块亮区跟着鼠标走：把"旧圈 ∪ 新圈"那一小片并进脏区——只报新位置的话，旧位置那一圈就赖在"亮"上暗不回来。
    /// <para>与光标光晕同一形状、同一预算（都很小，且只在幕布开着时才算）。半径按<b>这块屏自己的缩放</b>换算：
    /// 写死像素数就是在 150% 屏上只剩半个亮区。</para>
    /// </summary>
    private static void UpdateCurtainFocus(Screen screen, int cursorX, int cursorY)
    {
        if (!CanvasBackdropMath.HasFocusHole(AnnotationHub.Backdrop)) return;
        var radius = (int)Math.Round(CanvasBackdropMath.FocusRadiusDip * screen.Scale);
        var touched = screen.Window.SetFocus(new PixelPoint(cursorX - screen.Bounds.X, cursorY - screen.Bounds.Y), radius);
        if (!touched.IsEmpty) screen.Dirty.Add(touched);
    }

    /// <summary>白板底是否在场（工具条那颗据此高亮；读的是会话态，宿主不再记一份）。</summary>
    public static bool IsWhiteboard => AnnotationHub.Backdrop == CanvasBackdrop.Whiteboard;

    /// <summary>幕布是否在场（压暗 + 鼠标那块亮区）。</summary>
    public static bool IsCurtain => AnnotationHub.Backdrop == CanvasBackdrop.Curtain;

    /// <summary>「白板」那颗与 <c>canvas.board</c>：折成事件交给 Hub，这里不判方向。</summary>
    public static void ToggleWhiteboard() => AnnotationHub.Raise(SessionEvent.ToggleWhiteboard);

    /// <summary>「幕布」那颗与 <c>canvas.curtain</c>：同上。</summary>
    public static void ToggleCurtain() => AnnotationHub.Raise(SessionEvent.ToggleCurtain);

    /// <summary>广播状态（工具/颜色/粗细/穿透/光晕/让位原因）——工具条的状态行是唯一读者。</summary>
    public static void RaiseStateChanged() => StateChanged?.Invoke();

    /// <summary>
    /// 截图抓那一帧时把画布那块玻璃收起来（<b>只给 <c>ScreenshotService.Grab</c> 用，那里成对调用</b>）。
    /// <para>
    /// 收的是玻璃本身，不是"擦掉笔迹"：笔迹留在 <see cref="Screen.Persistent"/> 与荧光段里，
    /// 还回来之后一切照旧——用户按「截图带画布＝关」是要给别人一张干净的图，不是要把黑板擦掉。
    /// </para>
    /// <para>
    /// <b>工具条不跟着收</b>：这一句只活几毫秒，条子跟着闪一下比留着它更难解释。
    /// 整场截图期间条子收起由 <see cref="ApplyStage"/> 负责（那是会话态的属性，不是抓帧的副作用）。
    /// </para>
    /// </summary>
    public static void SetHiddenForCapture(bool hidden)
    {
        if (!IsRunning) return;
        foreach (var screen in Screens) screen.Window.SetVisible(!hidden);
    }

    /// <summary>
    /// 撤销<b>全机最后落的那一笔</b>（方案 §13 用例 9 的 LIFO，§7 的"栈全局化、按 surface 归属"）。
    /// <para>从前这里是 <c>foreach (screen) screen.Ink.Undo()</c>——一次按键把<b>每块屏各退一条</b>：
    /// 双屏上按一次撤销，两块屏同时各掉一笔，而掉的那两条根本不是同一时刻画的（"撤错东西"比"撤不动"更糟）。</para>
    /// </summary>
    public static void Undo()
    {
        if (_polyPoints is { } open)
        {
            // 勾到一半时"撤销"退的是<b>最后一个顶点</b>，不是板上那条旧笔迹：
            // 用户眼睛正盯着这条折线，撤错对象比多按一次难受得多。退到只剩起点仍然开着
            // （还能从那一点接着拖），一个点都不剩才整条丢掉。
            var where = _polyScreen;
            open.RemoveAt(open.Count - 1);
            if (open.Count == 0) CancelOpenPolyLine();
            else { ShowPolyPreview(null); if (where is not null) Flush(where); }
            StateChanged?.Invoke();
            return;
        }
        var target = Screens.Where(s => s.Ink.Count > 0)
            .OrderByDescending(s => s.Ink.LastOrder)
            .FirstOrDefault();
        if (target is null || !target.Ink.Undo()) return;
        // 只重烤这一块屏：它自己的落点与"曾经烤过的那一片"由 Recomposite 一起算（批次 WO），
        // 别的屏的墨一笔没动，不该跟着重烤一遍（4K 上一帧是几十兆）。
        Recomposite(target);
        Flush(target);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 重做：把刚被撤销掉的那一笔放回去，<b>只放一块屏</b>。
    /// <para>与 <see cref="Undo"/> 同一个形状：目标用的是 <see cref="InkDoc.NextOrder"/>
    /// （这一叠"重做会回来的那一笔"是全机第几笔），不是 <c>LastOrder</c>——撤销之后
    /// <c>LastOrder</c> 指的是"还留着的那一笔"，两块屏都撤销过时会挑错那一叠。</para>
    /// <para>没有可重做的东西时安静地什么都不做（工具条那颗与「撤销」对称，不灰、不弹说明）；
    /// 全局热键那一路由 <see cref="RequireRunning"/> 负责"板子没开着"的可见原因。</para>
    /// </summary>
    public static void Redo()
    {
        var target = Screens.Where(s => s.Ink.CanRedo)
            .OrderByDescending(s => s.Ink.NextOrder)
            .FirstOrDefault();
        if (target is null || !target.Ink.Redo()) return;
        Recomposite(target);
        Flush(target);
        StateChanged?.Invoke();
    }

    public static void ClearAll()
    {
        CancelOpenPolyLine();               // 勾到一半的折线不能被"清空"顺手提交出去
        foreach (var screen in Screens)
        {
            // 连手上那条一起丢：清空之后鼠标还动着的话，那条"幽灵笔迹"会在清完的板子上补出一截
            // （以前那个容器把清空与丢笔迹做成了一次，拆成"模型 + 实时缓冲"两层之后必须自己说清楚）
            screen.Drawing = null;
            screen.Ink.Clear();
            DropTrail(screen);
            Recomposite(screen);
        }
        FlushAll();
    }

    /// <summary>
    /// 丢掉这块屏上所有还活着的荧光段。<b>必须先把它们占过的地方并进脏区</b>：段一从 list 里消失就再没人
    /// 画它，而它贴过的那一层像素还留在分层窗缓冲里——症状是"清空之后屏幕上留着一块擦不掉的光"。
    /// </summary>
    private static void DropTrail(Screen screen)
    {
        var was = screen.Trail.LiveBounds;
        screen.Trail.Clear();
        if (!was.IsEmpty) screen.Dirty.Add(was);
    }

    /// <summary>
    /// 把板子上<b>笔迹那一块</b>合成一张钉到桌面上，钉在它原来所在的位置（§16.5 的"快照为贴图"，S4-⑤ 定形）。
    /// <para><b>刻意不贴整块屏</b>：贴图在 Z 序名册里压在画布玻璃之上（穿透态 Pin=3、Board=4），
    /// 而且默认吃鼠标——整屏一张的意思就是"按一次贴图，整块桌面被一张静止的图盖住，之后每一按都落在它身上"。
    /// 裁到笔迹那一块之后，它和截图链贴出来的那一张才是同一形态：看得见、能拖、能缩放、不占地方。</para>
    /// <para>板子上那一份墨<b>原样留着</b>（2026-09-29 用户裁决）：这一条是"复制出去一份"，不是"收走"，
    /// 讲完这一段还要在同一块板上接着写。</para>
    /// </summary>
    public static void SnapshotToPin()
    {
        var shot = Compose();
        if (!shot.Ok)
        {
            Report("贴图失败", shot.Reason);
            return;
        }
        if (shot.Ink.IsEmpty)
        {
            // 一块墨都没有就没有"哪一块"可贴。这里不能退回"那就贴整屏"——那正是本批修掉的病；
            // 而"按了没反应"也不算交代，所以把另一条出口点名给用户，并说他**当前实际绑着的那颗键**
            // （写死键名就是把人支使去按一颗可能没用的键）。
            Report("贴图失败", "板上还没有笔迹可贴——「贴图」贴的就是笔迹那一块；" +
                $"要截整块屏幕请用截图（{BindingText(HotkeyActions.ScreenCapture)}），框选后再贴出");
            return;
        }
        // 边距是 DIP，必须按这块屏自己的缩放换算：写死像素数，150% 屏上的边就缩水一半（CanvasBackdropMath 同一条纪律）
        var padding = (int)Math.Round(CanvasSnapshotMath.PaddingDip * shot.Scale);
        var region = CanvasSnapshotMath.FrameOf(shot.Ink, shot.Width, shot.Height, padding);
        // 落点＝这块屏的原点 + 裁块在这幅画面里的偏移 ⇒ 贴出来那一块正好盖在眼睛看到的那一块上
        ScreenshotService.PinPixels(
            CanvasSnapshotMath.CropBgra(shot.Pixels, shot.Width, shot.Height, region),
            region.Width, region.Height,
            new IntRect(shot.Screen.X + region.X, shot.Screen.Y + region.Y, region.Width, region.Height));
    }

    /// <summary>把"屏幕 + 笔迹"合成一张交给剪贴板。<b>整幅交出去</b>（与存图同口径：留档的是一整页）。</summary>
    public static void SnapshotToClipboard()
    {
        var shot = Compose();
        if (!shot.Ok)
        {
            Report("复制失败", shot.Reason);
            return;
        }
        _ = ScreenshotService.CopyPixelsAsync(shot.Pixels, shot.Width, shot.Height, "画布");
    }

    /// <summary>把"屏幕 + 笔迹"合成一张存成 PNG。<b>整幅交出去</b>（与复制到剪贴板同口径）。</summary>
    public static void SavePng()
    {
        var shot = Compose();
        if (!shot.Ok)
        {
            Report("存图失败", shot.Reason);
            return;
        }
        _ = ScreenshotService.SavePixelsAsync(shot.Pixels, shot.Width, shot.Height, "画布");
    }

    // ────────── 输入 ──────────

    private static void OnPressed(Screen screen, CanvasPointer pointer)
    {
        if (screen.Window.IsClickThrough) return;          // 穿透态不该收到，真收到也不能画（鼠标本来要给下面的应用）
        // 这一按落在自家条子的矩形里 ⇒ 画布<b>不接</b>。正常情况下命中测试已经把那一格让开了
        // （<c>HTTRANSPARENT</c>，按下根本不会寄给我们）；走到这里说明有条子窗没登记进名册，
        // 或某个系统不认那个返回值。兜住它的价值是"绝不把轨迹画在自己的工具条上"这条底线
        // 不依赖任何 Win32 行为；日志留着当下一次归因的证据（反复出现＝让位那一环没生效）。
        if (LayerDirector.IsPointOnChrome(screen.Bounds.X + pointer.At.X, screen.Bounds.Y + pointer.At.Y))
        {
            StarLog.WarnThrottled("canvas:chrome-press",
                "[Canvas] 绘制态收到落在工具条矩形内的按下：画布没有接这一按（下一次点击会归条子）", windowMs: 5_000);
            return;
        }
        BeginPress(screen, pointer.At, _tool switch
        {
            CanvasTool.Highlighter => Press.Ephemeral,
            // 折线要跨按接段，所以按下时不能像别的图形那样"这一按就是一条"
            CanvasTool.PolyLine => Press.PolyLine,
            // 图形与画笔都吃"拦截态"那一按（穿透位由 Core 那张按态表给），但落笔方式不同：一次拖拽定形，不是跟着手走
            { } tool when tool.IsShape() => Press.Shape,
            _ => Press.Drawing,
        });
    }

    /// <summary>
    /// 落一笔。<paramref name="kind"/> 决定这一笔是什么，<b>不看 <c>_tool</c></b>：
    /// 穿透态下按住 Ctrl+Alt 圈画时选中的可能是荧光笔，但墨迹要落进持久层。
    /// </summary>
    private static void BeginPress(Screen screen, PixelPoint at, Press kind)
    {
        _press = kind;
        _pressScreen = screen;
        var colour = Palette[_colorIndex].Bgra;
        var now = Environment.TickCount64;
        if (kind == Press.Ephemeral)
        {
            var segment = screen.Trail.Begin(at, colour, CanvasWidths.At(_widthStep), now);
            screen.Dirty.Add(segment.Stroke.Bounds);
        }
        else if (kind == Press.Shape)
        {
            // 预览落在临时层，不落持久层：拖到一半取消、拖过头再拉回来，都不该留下一条撤不掉的笔迹
            _shapeFrom = at;
            screen.Dirty.Add(screen.Trail.SetPreview(ShapeStroke(at, at)));
        }
        else if (kind == Press.PolyLine)
        {
            // <b>折线不跨屏</b>：顶点表整份是"那一块屏自己的物理像素"（坐标系原点在那块屏左上角），
            // 在另一块屏上点第二下就会把两个坐标系的数写进同一条笔迹——症状是那一段飞到主屏的另一头。
            // 所以换屏先把手上这条收口（用户已经画成的那几段不能丢），再从这一点起一条新的。
            if (_polyScreen is not null && _polyScreen != screen) FinishOpenPolyLine();
            // 已经在勾就只是"接着拖下一段"——顶点由上一段的抬手定，这里不能再加一个（否则每段开头都多一个重点）
            _polyPoints ??= new List<PixelPoint> { at };
            if (_polyScreen is null)
            {
                _polyScreen = screen;
                _polyColour = colour;                       // 颜色与粗细在起勾那一刻定死，中途换设置不悄悄改它
                _polyWidth = WidthFor(CanvasTool.PolyLine);
            }
            ShowPolyPreview(at);
        }
        else
        {
            var tool = kind == Press.QuickPen ? CanvasTool.Pen : _tool;
            var stroke = new CanvasStroke(tool, colour, WidthFor(tool), at);
            screen.Drawing = stroke;
            screen.Dirty.Add(CanvasCompositor.Paint(
                screen.Persistent, screen.Window.Width, screen.Window.Height, stroke));
        }
        Flush(screen);
    }

    private static void OnMoved(Screen screen, CanvasPointer pointer)
    {
        var now = Environment.TickCount64;
        var dirty = false;
        if (_press == Press.Ephemeral)
        {
            if (screen.Trail.Extend(pointer.At, now))
            {
                // 只把"新走的那一小条"并进脏区（不是整条笔迹的包围盒）：这一句加上 Flush 的裁剪，
                // 才是"荧光笔拖动跟手"的全部凭据
                screen.Dirty.Add(screen.Trail.Segments[^1].Stroke.TailBounds);
                dirty = true;
            }
        }
        else if (_press == Press.Shape)
        {
            // 每一帧整份替换预览：脏区是"旧的那份 + 新的这份"，所以拖过去的那条影子会被擦回来
            screen.Dirty.Add(screen.Trail.SetPreview(ShapeStroke(_shapeFrom, pointer.At)));
            dirty = true;
        }
        else if (_polyPoints is not null && screen == _polyScreen)
        {
            // 折线<b>跨按</b>开着：没按住的时候那根橡皮筋也要跟着手走，否则"下一个顶点落在哪"全无预告，
            // 就只能凭感觉点。预览整份替换，脏区含旧那份，所以拖过的影子会被擦回来。
            ShowPolyPreview(pointer.At);
            dirty = true;
        }
        else if (screen.Drawing is { } stroke && stroke.AddPoint(pointer.At))
        {
            screen.Dirty.Add(CanvasCompositor.PaintTail(
                screen.Persistent, screen.Window.Width, screen.Window.Height, stroke));
            dirty = true;
        }
        if (dirty && now - screen.LastFlushMs >= DragThrottleMs) Flush(screen);
    }

    private static void OnReleased(Screen screen, CanvasPointer pointer)
    {
        FinishPress(screen, pointer.At);
        // 轮询接手的那两种按下（荧光笔按住 / Ctrl+Alt 圈画）是"临时摘掉穿透"换来的，
        // 抬起必须还回去——否则一次圈画之后整台机器的鼠标就被我们扣住了。
        if (AnnotationHub.QuickPressInFlight) EndTemporaryPress();
    }

    /// <summary>
    /// 收手上那一笔（不碰穿透态）：荧光段交给 TTL 淡出，持久笔迹要定形。
    /// <paramref name="at"/> 只有折线用——抬手那一点就是它刚拖出来的那个顶点。
    /// </summary>
    private static void FinishPress(Screen? screen, PixelPoint? at = null)
    {
        if (screen is null) { _press = Press.None; return; }
        if (_press == Press.Ephemeral) Flush(screen);          // 段留在 Trail 里按 TTL 淡，不进持久层
        else if (_press == Press.PolyLine)
        {
            // 抬手<b>不收口</b>：只是把这一段的终点定成顶点，折线还开着等下一按。
            // 所以这里既不 Commit 也不 Recomposite——它眼下整条都还活在预览槽里。
            // 只在折线自己那块屏上定顶点：顶点表是"那块屏自己的物理像素"，别的屏的坐标混进来，
            // 那一段就会飞到另一块屏的另一头去。
            if (screen == _polyScreen) AddVertex(at);
            ShowPolyPreview(null);
            Flush(screen);
        }
        else if (_press == Press.Shape)
        {
            // 定形＝把预览那份"换个归属"：同一串点从临时层挪进持久层，不重算几何。
            // 重算就会出现"预览一个样、落下另一个样"（拖的时候是圆的、松手变有角）。
            var preview = screen.Trail.Preview;
            screen.Dirty.Add(screen.Trail.DropPreview());
            if (preview is not null) screen.Ink.Add(preview.ToAnnotation());
            Recomposite(screen);
            Flush(screen);
        }
        else
        {
            // 这一条如果不够格留下来（橡皮点一下），它按下时已经烤进持久层的那一小片要单独交回去擦——
            // 否则屏幕上留下一个"没有任何笔迹对应、撤销里也没有"的洞
            var footprint = screen.Drawing?.Bounds ?? default;
            if (!CommitLiveStroke(screen)) Recomposite(screen, footprint);
            else Recomposite(screen);
            Flush(screen);
        }
        _press = Press.None;
        AnnotationHub.QuickPressInFlight = false;
    }

    /// <summary>
    /// 把手上那条落成模型里的一笔：<b>整份转成 <see cref="Annotation"/> 交给这块屏的 InkDoc</b>，
    /// 实时缓冲随即清空。够不够格留（橡皮点一下不算一笔）由笔迹自己说，留不下就返回 false，
    /// 调用方要把它的落点交回去擦——屏幕上不能留一片"没有笔迹对应、撤销里也没有"的洞（批次 WO）。
    /// </summary>
    private static bool CommitLiveStroke(Screen screen)
    {
        var stroke = screen.Drawing;
        screen.Drawing = null;
        if (stroke is null || !stroke.WorthKeeping) return false;
        screen.Ink.Add(stroke.ToAnnotation());
        return true;
    }

    /// <summary>
    /// 穿透态下的"按住即画"。规格 §16.5.2 要的是<b>零模式切换摩擦</b>：荧光笔按住才有、
    /// 松开即透；Ctrl+Alt+拖动直接圈画。可穿透态下我们收不到 <c>WM_LBUTTONDOWN</c>
    /// （那一次按下归了下层应用），所以只能每帧看按键状态——发现按下才临时摘掉穿透，
    /// 之后的移动与抬起才归我们。
    /// </summary>
    private static void PollPress(int cursorX, int cursorY)
    {
        var down = LayeredCanvasWindow.LeftButtonDown;

        // 抬起发生在我们还没接管的那一帧里（<33ms 的短按）：轮询补一次收尾，否则笔永远"没松"
        if (_press is Press.Ephemeral or Press.QuickPen && !down)
        {
            FinishPress(_pressScreen);
            EndTemporaryPress();
            return;
        }
        // 会话闸门（方案 §4 的 C2）：读态<b>只在 Board·穿透态</b>跑。绘制态收得到按下，再叠一套读态
        // 就是同一按两家用；截图期间这块玻璃连显示都不被允许，抢一次就把截图打断在别的程序手里。
        if (_press != Press.None || AnnotationHub.Stage != AnnotationStage.BoardPenetrating || !down) return;

        // 穿透态只保留一种抢按：<b>显式的 Ctrl+Alt 圈画</b>。2026-09-27 用户改判，删掉"当前选的是荧光笔
        // 就抢按"那一臂——他要"穿透态按下去就是下层应用的点击"，要画荧光笔得先关掉穿透
        // （那颗按钮或 canvas.through）。方向判据在 CanvasModes 一处，接线层不许自己现写布尔（WF-1 口径）。
        if (!CanvasModes.ClaimsPressInPenetrating(LayeredCanvasWindow.CtrlAltDown)) return;
        // 抢之前先问"这一点落在自家条子的矩形里吗"（§6.3）：光标停在工具条／快捷键面板上时那一按是<b>点按钮</b>，
        // 抢走它就长成"点菜单栏却画出一条轨迹、那颗按钮没反应"（真机原话）。问矩形而不问命中到谁——
        // 命中测试永远答最上面那一层，绘制态它就是玻璃自己（同一问题只有一个出处）。
        // 贴图不算 chrome：穿透态下在贴图上 Ctrl+Alt 圈注是要留的能力（§13 用例 5）。
        if (LayerDirector.IsPointOnChrome(cursorX, cursorY)) return;

        var screen = ScreenAt(new PixelPoint(cursorX, cursorY));
        if (screen is null) return;

        foreach (var s in Screens) { s.Window.SetClickThrough(false); s.Window.SetDrawCursor(true); }
        screen.Window.Capture();                                   // 那一次按下不会再来，抓取要自己补
        AnnotationHub.QuickPressInFlight = true;                   // 每帧对账这一刻要跳过（唯一合法的不一致）
        BeginPress(screen, new PixelPoint(cursorX - screen.Bounds.X, cursorY - screen.Bounds.Y), Press.QuickPen);
    }

    /// <summary>把临时摘掉的穿透还回去（<b>会话态没动过</b>，所以工具条那行字不会跳）。</summary>
    private static void EndTemporaryPress()
    {
        _press = Press.None;
        AnnotationHub.QuickPressInFlight = false;
        if (AnnotationHub.Stage != AnnotationStage.BoardPenetrating) return;    // 本来就在绘制态，不用恢复
        foreach (var s in Screens) { s.Window.SetClickThrough(true); s.Window.SetDrawCursor(false); }
    }
    private static Screen? ScreenAt(PixelPoint point)
        => Screens.FirstOrDefault(s => s.Bounds.X <= point.X && point.X < s.Bounds.Right
            && s.Bounds.Y <= point.Y && point.Y < s.Bounds.Bottom);

    private static int WidthFor(CanvasTool tool)
        => tool == CanvasTool.Eraser ? CanvasWidths.EraserDiameter : CanvasWidths.At(_widthStep);

    /// <summary>
    /// 按当前工具与粗细，把一次拖拽展成一条笔迹。<b>拖拽期间的预览与松手时的定形共用这一句</b>：
    /// 两处各算一遍几何，就会长成"拖的时候一个样、松手另一个样"。
    /// </summary>
    /// <remarks>
    /// 颜色与粗细<b>从当前设置里取</b>，不作参数：它们本就该跟着工具条上那颗走，
    /// 而把 <c>colour</c> 做成参数会让"预览用 A 色、定形用 B 色"这种错法编译得过（批次 WF 那条口径：
    /// 一个布尔/取值的含义只在一处时，接线处现写就是必然出错）。
    /// </remarks>
    private static CanvasStroke ShapeStroke(PixelPoint from, PixelPoint to)
    {
        var width = WidthFor(_tool);
        return CanvasStroke.FromPoints(_tool, Palette[_colorIndex].Bgra, width,
            CanvasShapes.Outline(_tool, from, to, width));
    }

    /// <summary>
    /// 折线的顶点：<b>抬手那一点只有在"真的拖出了一段"时才算一个顶点</b>。
    /// 原地按一下也定顶点的话，屏幕上会攒出一串看不见的重点，而它们两两之间是零长度段——
    /// 收口时就只剩"一个圆帽孤零零地留在板上"这种说不清的症状。
    /// </summary>
    private static void AddVertex(PixelPoint? at)
    {
        if (at is not { } p || _polyPoints is not { } points) return;
        if (points.Count > 0 && points[^1].Equals(p)) return;
        points.Add(p);
    }

    /// <summary>
    /// 换掉折线的预览：<b>已定形的顶点 + 伸向光标的那一段橡皮筋</b>（<paramref name="rubber"/> 为 null＝刚抬手，
    /// 只画已定形的部分）。点不足两个时丢掉预览而不是画一个点。
    /// <para>它只弄脏 <see cref="Screen.Dirty"/>，<b>不提交</b>——提交时机由调用方决定（拖拽期要走节流）。</para>
    /// </summary>
    private static void ShowPolyPreview(PixelPoint? rubber)
    {
        if (_polyScreen is not { } screen || _polyPoints is not { } points) return;
        var preview = CanvasShapes.PolyLinePreview(points, rubber);
        screen.Dirty.Add(preview.Count < 2
            ? screen.Trail.DropPreview()
            : screen.Trail.SetPreview(CanvasStroke.FromPoints(CanvasTool.PolyLine, _polyColour, _polyWidth, preview)));
    }

    /// <summary>
    /// 收口正在勾的折线：<b>整条作为一条笔迹</b>进持久层。
    /// <para>为什么不是每个顶点一条：撤销一格要退掉"刚才画的那条折线"，而不是它的一小段
    /// （用户按 Ctrl+Z 的心智单位是"我画的那个东西"）；而橡皮、存图、贴图全都按笔迹走，
    /// 拆成多条只会让同一件事有五种表现。</para>
    /// </summary>
    private static void FinishOpenPolyLine()
    {
        var screen = _polyScreen;
        var points = _polyPoints;
        _polyPoints = null;
        _polyScreen = null;
        if (screen is null || points is null) return;
        screen.Dirty.Add(screen.Trail.DropPreview());
        if (points.Count >= 2)
        {
            screen.Ink.Add(CanvasStroke.FromPoints(CanvasTool.PolyLine, _polyColour, _polyWidth, points).ToAnnotation());
            Recomposite(screen);
        }
        Flush(screen);
    }

    /// <summary>丢掉正在勾的折线且<b>不提交</b>（清空笔迹／退出画布：手上一半的东西不该落进结果里）。</summary>
    private static void CancelOpenPolyLine()
    {
        var screen = _polyScreen;
        _polyPoints = null;
        _polyScreen = null;
        if (screen is null) return;
        screen.Dirty.Add(screen.Trail.DropPreview());
        Flush(screen);
    }

    /// <summary>
    /// 画布里的 Esc：<b>两级</b>。正在勾折线时先收口这一条（板子继续开着），没有手上一半的东西才退出画布。
    /// <para>只有一级"Esc＝退出画布"的话，勾到一半想停下就得整块板子一起没——而那条折线也没画成。
    /// 真机症状会是"按 Esc 之后我的笔迹全没了"（退出即丢弃）。</para>
    /// </summary>
    public static void Escape() => AnnotationHub.EscapeBoard();

    /// <summary>Esc 的第一级：收掉手上那半件事（勾到一半的折线），板子继续开着。</summary>
    public static void CloseWorkInProgress()
    {
        if (_polyPoints is null) return;
        FinishOpenPolyLine();
        RaiseStateChanged();
    }

    // ────────── 渲染 ──────────

    private static DispatcherQueueTimer BuildFrameTimer(DispatcherQueue queue)
    {
        var timer = queue.CreateTimer();
        timer.Interval = FrameGap;
        timer.Tick -= OnFrameTick;
        timer.Tick += OnFrameTick;
        return timer;
    }

    private static void OnFrameTick(DispatcherQueueTimer sender, object args)
    {
        if (!IsRunning) return;
        var now = Environment.TickCount64;
        WindowInterop.GetCursorPos(out var cursor);
        // 仲裁三件事全部交给 Hub 在这一处问：样式位与状态对不对账、光标那一层归谁、归谁之后做什么。
        // 原来这里是三个本地方法各问一遍、各读一份自己认为的状态——那正是"状态说的与窗口做的不一致"的温床。
        AnnotationHub.AuditFrame(cursor.X, cursor.Y);
        // 穿透态收不到鼠标消息，"这一按是不是要画"只能在这里看按键状态（§16.5.2 的零摩擦入口）。
        // 跑不跑这一问由会话态决定，而不是宿主自己再判一遍穿不穿（判据在 Core，QuickDrawReads）。
        if (AnnotationHub.Stage.QuickDrawReads()) PollPress(cursor.X, cursor.Y);
        if (Screens.Count == 0) return;
        foreach (var screen in Screens)
        {
            // 到期那一段要把它占过的地方交回脏区：删掉之后没人再画它，那块光就赖在屏幕上了
            var expired = screen.Trail.Tick(now);
            if (!expired.IsEmpty) screen.Dirty.Add(expired);
            // <b>只有"浓度与贴在屏幕上的那一层不一致"的段才整段重算</b>。正按住拖的那一段每帧都被
            // 刷新（AlphaScale 恒为 1），于是它进脏区的只有新走过的那一小条（见 OnMoved）——
            // 以前这里是无条件把每一段、每帧、整条从几何重画一遍，4K 粗档实测几百毫秒一帧。
            foreach (var segment in screen.Trail.Segments)
                MarkSegmentIfFading(screen, segment);
            // 幕布的亮区每帧跟着鼠标挪（只有幕布开着才算，代价与下面那个光晕同一量级）
            UpdateCurtainFocus(screen, cursor.X, cursor.Y);
            // 光晕跟着鼠标走：旧位置要复原、新位置要叠上（两块都很小）
            if (!screen.LastGlow.IsEmpty) screen.Dirty.Add(screen.LastGlow);
            screen.GlowAt = null;
            if (_tool == CanvasTool.Highlighter && screen.Trail.CursorHaloEnabled
                && screen.Bounds.X <= cursor.X && cursor.X < screen.Bounds.Right
                && screen.Bounds.Y <= cursor.Y && cursor.Y < screen.Bounds.Bottom)
            {
                var local = new PixelPoint(cursor.X - screen.Bounds.X, cursor.Y - screen.Bounds.Y);
                var radius = CanvasWidths.RadiusFor(CanvasTool.Highlighter, CanvasWidths.At(_widthStep));
                screen.GlowAt = local;
                screen.LastGlow = new IntRect(local.X - radius, local.Y - radius, radius * 2 + 1, radius * 2 + 1);
                screen.Dirty.Add(screen.LastGlow);
            }
            else
            {
                screen.LastGlow = default;
            }
        }
        FlushAll();
    }

    /// <summary>
    /// 这一段淡到与"屏幕上此刻那一层"不一致了吗？不一致才需要把整段重算，并把记号推到新浓度上。
    /// <para>
    /// <b>先记 <c>PaintScale</c> 再等 Flush</b>是有意的：Flush 由谁触发（帧循环、拖动节流、收笔）都不该
    /// 改变"这一帧该用多淡"的结论，否则同一段在两次 Flush 之间会被画成两种浓度。
    /// </para>
    /// </summary>
    private static void MarkSegmentIfFading(Screen screen, EphemeralInk.Segment segment)
    {
        if (Math.Abs(segment.PaintScale - segment.AlphaScale) <= 1e-9) return;
        segment.PaintScale = segment.AlphaScale;
        screen.Dirty.Add(segment.Stroke.Bounds);
    }

    private static void FlushAll()
    {
        foreach (var screen in Screens) Flush(screen);
    }

    /// <summary>
    /// 重算并提交一块屏的脏区：铺持久层 → 叠荧光段 → 叠光晕。
    /// <para>
    /// <b>叠的那一段只叠到脏区里</b>（<see cref="CanvasCompositor.PaintClipped"/>）：脏区外那些像素
    /// 上一帧就已经贴对了，重画它们除了把 UI 线程拖住之外没有任何效果——裁剪之所以安全，
    /// 是因为这里的合成是取大，每个像素的结论只取决于落在它身上那些笔点。
    /// </para>
    /// <para>
    /// 浓度取 <c>PaintScale</c> 而不是 <c>AlphaScale</c>：前者是"此刻该贴在屏幕上的那一层"，
    /// 由 <see cref="MarkSegmentIfFading"/> 与脏区一起更新。两者错开就会出现"淡出被反复重画"或
    /// "淡到一半停住"。
    /// </para>
    /// </summary>
    private static void Flush(Screen screen)
    {
        var width = screen.Window.Width;
        var height = screen.Window.Height;
        var rect = CanvasCompositor.Union(screen.Dirty, width, height);
        screen.Dirty.Clear();
        if (rect.IsEmpty) return;
        var since = Stopwatch.GetTimestamp();
        CanvasCompositor.CopyRect(screen.Persistent, screen.Window.Pixels, width, height, rect);
        foreach (var segment in screen.Trail.Segments)
            CanvasCompositor.PaintClipped(screen.Window.Pixels, width, height, segment.Stroke, rect, segment.PaintScale);
        // 正在拖的那个图形：它不在持久层里，所以每一帧都是从 Persistent 之上重新叠出来的一份——
        // 叠在荧光段之后，与松手之后它作为一条持久笔迹所处的顺序一致（同一帧里不会跳色）。
        if (screen.Trail.Preview is { } preview)
            CanvasCompositor.PaintClipped(screen.Window.Pixels, width, height, preview, rect);
        if (screen.GlowAt is { } glow)
            CanvasCompositor.PaintGlow(screen.Window.Pixels, width, height, glow,
                CanvasWidths.RadiusFor(CanvasTool.Highlighter, CanvasWidths.At(_widthStep)),
                screen.Trail.HaloColorBgra);
        screen.Window.Present(rect);
        screen.LastFlushMs = Environment.TickCount64;
        ReportSlowFrame(since, rect, screen);
    }

    /// <summary>
    /// 一帧重算太慢就在日志里留一行带原因的（拖动帧、淡出帧都从这里走）。
    /// <para>
    /// <b>为什么值得留</b>：淡出期必须把"正在淡的那一段占过的整片"重算一遍，这在长笔迹上仍是
    /// 随笔迹长度增长的开销（批次 WG 把拖动帧收敛成常数，淡出帧没收敛）。只报"有点卡"定不了改法，
    /// 报"哪一块多大、几段几点、多少毫秒"才能判断要不要再上一层（缓存浓度图／快照位图）。
    /// </para>
    /// <para>阈值取 12 ms ≈ 30fps 那 33 ms 预算的三分之一；节流 10 秒，免得一行日志自己变成卡顿源。</para>
    /// </summary>
    private static void ReportSlowFrame(long since, IntRect rect, Screen screen)
    {
        var ms = (Stopwatch.GetTimestamp() - since) * 1000d / Stopwatch.Frequency;
        if (ms < SlowFrameMs) return;
        var points = 0;
        foreach (var segment in screen.Trail.Segments) points += segment.Stroke.Points.Count;
        StarLog.WarnThrottled("canvas:frame",
            $"[Canvas] 一帧重算 {ms:F1} ms：脏区 {rect.Width}x{rect.Height}" +
            $"（{rect.Width * (long)rect.Height / 1000}K 像素）、荧光 {screen.Trail.Segments.Count} 段共 {points} 点",
            windowMs: 10_000);
    }

    private const double SlowFrameMs = 12;

    /// <summary>
    /// 持久层重算：清掉"<b>现在还剩的笔迹</b> ∪ <b>曾经烤出去的那一片</b> ∪ <paramref name="alsoErase"/>"
    /// 再按顺序全部重画一遍。
    /// <para>为什么不能只清"现在还剩的"：<see cref="Screen.Composited"/> 那条——撤销一条笔迹之后，
    /// 它占过的地方如果不在剩余笔迹的包围盒里，就<b>没有任何一步会去擦它</b>，屏幕上留下半只椭圆，
    /// 而撤销栈里已经没有东西能把它退掉（真机反馈的"只能撤销图形的一半"）。</para>
    /// <para>区域也不取整块屏幕（那是 33MB 的提交）。橡皮那条不幂等（按比例减 alpha，走两次擦过头），
    /// 所以必须一次画成，不能增量补。</para>
    /// </summary>
    private static void Recomposite(Screen screen, IntRect alsoErase = default)
    {
        var width = screen.Window.Width;
        var height = screen.Window.Height;
        // 要擦的 = 上一次烤过的那一片 ∪ 这次被丢掉的那一条（区域算法与"为什么不能只擦剩下的"都在 Core 那条注释里）
        var toErase = CanvasCompositor.Union(new[] { screen.Composited, alsoErase }, width, height);
        var erase = CanvasCompositor.Rebake(screen.Persistent, width, height, Brushes(screen),
            toErase, out var remaining);
        screen.Composited = remaining;
        if (!erase.IsEmpty) screen.Dirty.Add(erase);
    }

    /// <summary>
    /// 把这一叠模型还原成渲放要的笔迹串。<b>每次重烤现算，不留第二份缓存</b>：
    /// 缓存一份"笔迹形状"就等于把同一叠墨存两处，撤销／清空之后两边对不对得上线程说了算（R2 双引擎的老路）。
    /// 重烤只发生在落笔/撤销/清空这类离散动作上，一叠几百条的换算代价换掉的是"两份真值"这一整类缺陷。
    /// </summary>
    private static List<CanvasStroke> Brushes(Screen screen)
    {
        var marks = screen.Ink.Marks;
        var strokes = new List<CanvasStroke>(marks.Count);
        for (var i = 0; i < marks.Count; i++) strokes.Add(CanvasStroke.FromAnnotation(marks[i]));
        return strokes;
    }

    /// <summary>换工具/换粗细之前先把手上那条收掉，免得它接到新设置下去（症状："画着画着笔自己变粗了"）。</summary>
    private static void CommitOpenStroke()
    {
        // 轮询抢来的那一按（荧光笔/Ctrl+Alt 圈画）也要在这里收口：换工具时它还挂着的话，
        // 抬起事件会被新工具吃掉，屏幕上就留下一条"永远在画"的笔迹
        if (_press is Press.Ephemeral or Press.QuickPen or Press.Shape or Press.PolyLine)
        {
            FinishPress(_pressScreen);
            EndTemporaryPress();
        }
        // 折线单独收：它按定义就是"跨按还开着"的那一条，上面那个 switch 只收了手上这一段
        FinishOpenPolyLine();
        var changed = false;
        foreach (var screen in Screens)
            if (screen.Drawing is not null)
            {
                CommitLiveStroke(screen);
                Recomposite(screen);
                changed = true;
            }
        if (changed) FlushAll();
    }

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

    // ────────── 快照 ──────────

    /// <summary>
    /// 一次合成的全部产出：整幅画面、这块屏在虚拟桌面里的位置与缩放，以及<b>笔迹占的那一块</b>。
    /// <para>三条落点（贴图／复制／存图）读的是同一份像素，差别只在贴图要照 <see cref="Shot.Ink"/> 裁一刀。
    /// 做成一个返回值而不是三份 <c>out</c>：从前七个 <c>out</c> 的形状里，"哪一条落点用了哪个数"只能靠读调用点，
    /// 而这一格的全部风险都在"落点之间悄悄分岔"（记忆 ⑧）。</para>
    /// </summary>
    private readonly record struct Shot(bool Ok, byte[] Pixels, int Width, int Height,
        IntRect Screen, IntRect Ink, double Scale, string Reason);

    /// <summary>合成就失败了：画面还没开始叠，reason 是给用户看得见的那一句。</summary>
    private static Shot NoShot(string reason)
        => new(false, Array.Empty<byte>(), 0, 0, new IntRect(), CanvasCompositor.Nothing, 1d, reason);

    /// <summary>
    /// 取"鼠标所在那块屏"的画面 + 笔迹。<b>只合成一块屏</b>：跨屏一张大图会把另一块屏的内容也截进来，
    /// 而钉上去之后它既不属于这块屏也不属于那块——用户要的是"我圈的那块黑板"。
    /// </summary>
    private static Shot Compose()
    {
        if (Screens.Count == 0) return NoShot("画布没开着");
        WindowInterop.GetCursorPos(out var cursor);
        var screen = Screens.FirstOrDefault(s => s.Bounds.X <= cursor.X && cursor.X < s.Bounds.Right
            && s.Bounds.Y <= cursor.Y && cursor.Y < s.Bounds.Bottom) ?? Screens[0];

        // 抓的是<b>干净桌面</b>（那一帧里这块玻璃不上屏），墨由下面自己叠：抓屏抓到的是已经合成完的屏幕，
        // 玻璃上的笔迹会在那一帧里进图一次，OverlayOntoFrame 又叠一次＝"图里有两份"（方案 §1 的 C5，
        // 也是"屏幕上一份、图里另一份"这类对不上的总根源）。收/还只在 CaptureWithoutCanvas 那一处写。
        //
        // <b>白板态走另一条底</b>：那块玻璃本身就是不透明的整屏白，用户看见的就是"一张白纸 + 墨"，
        // 抓来的桌面他根本没看见。仍按桌面当底就会出现"板上是白纸、贴出来是别人的桌面"——
        // 与批次 WK 那条同一类缺口：凡是"屏幕上有"的东西（预览槽、背景态）都必须进这张图。
        byte[] baseFrame;
        int boardWidth, boardHeight;
        if (CanvasBackdropMath.IsOpaque(AnnotationHub.Backdrop))
        {
            boardWidth = screen.Window.Width;
            boardHeight = screen.Window.Height;
            baseFrame = BoardFrame(boardWidth, boardHeight, CanvasBackdropMath.WhiteboardArgb);
        }
        else
        {
            var captured = ScreenshotService.CaptureWithoutCanvas();
            if (!captured.Ok || captured.Frame is not { } frame)
                return NoShot(captured.Error ?? "系统没有返回画面");
            if (ScreenshotService.TryCrop(frame, screen.Bounds) is not { } crop)
                return NoShot("这一块屏在截到的画面外面（显示器可能刚被拔掉）");
            boardWidth = crop.Width;
            boardHeight = crop.Height;
            baseFrame = crop.Pixels;
        }

        // 笔迹（持久层 + 还活着的荧光段）合成到这张底之上；光晕是"提示我在什么模式"，不进快照
        var ink = new uint[boardWidth * boardHeight];
        Array.Copy(screen.Persistent, ink, Math.Min(screen.Persistent.Length, ink.Length));
        foreach (var segment in screen.Trail.Segments)
            CanvasCompositor.Paint(ink, boardWidth, boardHeight, segment.Stroke, segment.AlphaScale);
        // 正在拖的那个图形／还开着的折线也算"屏幕上有"：不叠它就会出现"板上看得见一条，贴出来的图没有"。
        // 顺序与 Flush 一致（叠在荧光段之后），这样同一帧里不会跳色。
        if (screen.Trail.Preview is { } preview)
            CanvasCompositor.Paint(ink, boardWidth, boardHeight, preview);
        // <b>先量墨在哪，再叠幕布那层底</b>：底是半透明的，叠完之后每个像素的 alpha 都高过空白位，
        // 这一句就会返回"整幅"，"只裁笔迹那一块"当场失效（症状：幕布开着时贴出来的又是整屏、桌面照样被盖住），
        // 而 InkRegionOf 的单测照旧全绿——它测的是判据本身，不是顺序。所以顺序由接线闸门钉住。
        var inkRegion = CanvasSnapshotMath.InkRegionOf(ink, boardWidth, boardHeight);
        // 幕布那块半透明的底也是"屏幕上有"的东西：把它叠进这张墨缓冲（亮区里不叠），交出去的才是眼睛看到的那一层。
        // 圆的几何与那条混合式只有窗口那一份（<c>CompositeForSnapshot</c>）——在这儿重算一次圆就是两份出处。
        // 白板底不走这里：它是不透明的，直接当整张图的底（上面那条臂），两者读的都是 <c>AnnotationHub.Backdrop</c>。
        if (CanvasBackdropMath.HasFocusHole(AnnotationHub.Backdrop)) screen.Window.CompositeForSnapshot(ink);
        CanvasCompositor.OverlayOntoFrame(baseFrame, boardWidth, boardHeight,
            new IntRect(0, 0, 0, 0), ink, screen.Window.Width, screen.Window.Height);

        return new Shot(true, baseFrame, boardWidth, boardHeight, screen.Bounds, inkRegion, screen.Scale, string.Empty);
    }

    /// <summary>
    /// 一块纯底色的 BGRA 帧（白板快照的那张底）。<b>字节序与分层窗缓冲一致</b>（B、G、R、A），
    /// 所以这里的位移顺序不能照着"ARGB"那个名字念反——念反了白底看不出来，彩色底就是一张错色图。
    /// </summary>
    private static byte[] BoardFrame(int width, int height, uint argb)
    {
        var frame = new byte[width * height * 4];
        var b = (byte)(argb & 0xFF);
        var g = (byte)(argb >> 8 & 0xFF);
        var r = (byte)(argb >> 16 & 0xFF);
        for (var i = 0; i < frame.Length; i += 4)
        {
            frame[i] = b;
            frame[i + 1] = g;
            frame[i + 2] = r;
            frame[i + 3] = 255;         // 交出去的图不能留透明洞（与 OverlayOntoFrame 那一句同一条纪律）
        }
        return frame;
    }

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
    /// 快捷键面板（工具条上那颗「⌨」调出来）。<b>它跟工具条一样不参与穿透</b>，
    /// 定序时排在"工具条之下、画布之上"——它是来看一眼的，不该挡住手边的按钮。
    /// </summary>
    private static CanvasHotkeyPanelWindow? _panel;

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

    private static void Report(string title, string message)
    {
        var shown = false;
        try { shown = App.MainWindow?.TryShowTrayNotification(title, message) == true; }
        catch (Exception ex) { StarLog.Warn($"[Canvas] 回报没送出去：{ex.Message}"); }
        if (!shown)
        {
            try { App.MainWindow?.ShowError(title, message); }
            catch (Exception ex) { StarLog.Warn($"[Canvas] 主窗提示条也没能显示：{ex.Message}"); }
        }
        StarLog.Info($"[Canvas] {title}：{message}");
    }
}
