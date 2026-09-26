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
/// ① <b>持久笔迹与荧光段分两张缓冲</b>——持久层（<see cref="CanvasInk"/>）留在
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
        public required CanvasInk Ink { get; init; }
        public required EphemeralInk Trail { get; init; }

        public List<IntRect> Dirty { get; } = new();

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
    private static bool _running;
    private static bool _busy;

    private static CanvasTool _tool = CanvasTool.Pen;
    private static int _colorIndex;
    private static int _widthStep = CanvasWidths.DefaultStepIndex;

    /// <summary>
    /// 是否穿透。<b>默认开</b>（规格 §16.5.2 的"③穿透态（默认）"）：进入画布模式不该把整台机器的
    /// 鼠标吃掉——那样一来 PPT 翻不了、下层应用点不动，而"按了热键之后电脑像死了"就是它。
    /// 要画持久笔迹得由工具条/热键显式进绘制态，或在穿透态里按住 Ctrl+Alt 直接圈画。
    /// </summary>
    private static bool _clickThrough = true;
    private static bool _haloEnabled = true;

    /// <summary>
    /// 手上一按是什么性质。<b>穿透态收不到 WM_LBUTTONDOWN</b>（那一次按下归了下层应用），
    /// 所以"荧光笔按住即画 / Ctrl+Alt 快速圈画"只能由帧循环轮询按键状态发现，
    /// 发现后临时摘掉穿透、自己补一次 SetCapture，抬起再恢复。
    /// </summary>
    private enum Press { None, Drawing, Ephemeral, QuickPen }

    private static Press _press;

    /// <summary>手上有笔时它属于哪块屏（抬起/轮询收尾都要用它，光标可能已经飘到别的屏）。</summary>
    private static Screen? _pressScreen;

    /// <summary>这一按是不是轮询"抢"来的（临时摘了穿透），抬起必须还回去。</summary>
    private static bool _wasTemporary;

    /// <summary>画布模式是否开着。</summary>
    public static bool IsRunning => _running;

    /// <summary>当前是不是鼠标穿透态（工具条据此画那颗按钮的高亮）。</summary>
    public static bool IsClickThrough => _clickThrough;

    public static CanvasTool Tool => _tool;

    public static int ColorIndex => _colorIndex;

    public static int WidthStep => _widthStep;

    public static bool HaloEnabled => _haloEnabled;

    /// <summary>颜色表沿用截图标注那一份（一条事实一个出处：两处色表迟早分岔）。</summary>
    public static IReadOnlyList<AnnotationColor> Palette => Annotation.Palette;

    /// <summary>状态变了（工具/颜色/粗细/穿透/光晕）——工具条订阅它刷新高亮。</summary>
    public static event Action? StateChanged;

    public static void Toggle()
    {
        if (_running) Stop();
        else Start();
    }

    /// <summary>
    /// 进入画布模式。每屏一块玻璃；某一屏建不起来（刚拔屏、显存吃紧）不牵连别的屏，
    /// 但<b>一块都没建起来时必须回报</b>——"按了热键屏幕什么都没变"是这套功能最坏的失败方式。
    /// </summary>
    public static void Start()
    {
        // 窗口与定时器都要在 UI 线程上建：托盘/菜单那类入口的回调线程不保证
        var queue = App.MainWindow?.DispatcherQueue;
        if (queue is { HasThreadAccess: false })
        {
            queue.TryEnqueue(Start);
            return;
        }
        if (queue is null)
        {
            Report("画布打不开", "主窗还不存在（应用还没起完？）");
            return;
        }
        if (_running || _busy) return;
        // 总开关（设置 → 拓展功能 →「屏幕画布」）。所有入口都汇到 Start()，所以闸门只在这一处：
        // 关着时热键那条只剩 canvas.toggle 还注册着（见 SettingsStore.GetRegisterableHotkeyBindings），
        // 按它要听见这句原因——一条什么都不发生的哑键是最坏的收尾。
        if (!EnabledBySetting)
        {
            Report("屏幕画布已关闭", "要在 设置 → 拓展功能 的「屏幕画布」里打开；打开后这条快捷键就回来了");
            return;
        }
        _busy = true;
        try
        {
            var monitors = WindowInterop.ListMonitors();
            if (monitors.Count == 0)
            {
                Report("画布打不开", "系统没有报告任何显示器");
                return;
            }
            foreach (var monitor in monitors)
            {
                var bounds = new IntRect(monitor.Bounds.X, monitor.Bounds.Y,
                    monitor.Bounds.Width, monitor.Bounds.Height);
                try
                {
                    var window = new LayeredCanvasWindow(bounds);
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
                        Ink = new CanvasInk(),
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
                        Restart();
                    };
                    window.SetClickThrough(_clickThrough);
                    window.SetDrawCursor(!_clickThrough);
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
                return;
            }

            _frame ??= BuildFrameTimer(queue);
            _frame.Stop();
            _frame.Start();
            _running = true;
            ShowToolbar();
            StarLog.Info($"[Canvas] 画布模式开启：{Screens.Count} 屏，工具={_tool}，" +
                         $"{(_clickThrough ? "穿透态（按住 Ctrl+Alt 直接圈画，或点工具条选画笔）" : "绘制态")}");
        }
        catch (Exception ex)
        {
            Stop();
            StarLog.Error("[Canvas] 开启画布模式失败", ex);
            Report("画布打不开", ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>退出画布模式：笔迹随之丢弃（要留就先在工具条上"存图／贴图"）。</summary>
    public static void Stop()
    {
        _frame?.Stop();
        _running = false;
        CloseToolbar();
        foreach (var screen in Screens)
        {
            try { screen.Window.Dispose(); }
            catch (Exception ex) { StarLog.Warn($"[Canvas] 透明层没关干净：{ex.Message}"); }
        }
        Screens.Clear();
        // 下次进来还是穿透态（§16.5.2 的默认态）：把"拦截全屏"留成默认，等于让每次按热键都像把电脑弄死
        _clickThrough = true;
        _press = Press.None;
        Notice = null;
        _yieldedTo = IntPtr.Zero;
        StateChanged?.Invoke();
        StarLog.Info("[Canvas] 画布模式关闭");
    }

    /// <summary>分辨率变了：先按新的拓扑重建（不能拿旧尺寸的缓冲接着画）。</summary>
    private static void Restart()
    {
        var wasClickThrough = _clickThrough;
        Stop();
        _clickThrough = wasClickThrough;
        Start();
    }

    // ────────── 工具与动作（工具条／热键／托盘都走这里）──────────

    /// <summary>
    /// 选工具＝顺带决定这一态拦不拦鼠标（规格 §16.5.2 的关键分岔）。<b>方向判据不在这里现写</b>，
    /// 由 <see cref="CanvasModes.IsClickThroughAfter"/> 给（纯函数，可单测）：把两支笔塞进同一个模式
    /// 开关正是冲突的来源，而布尔表达式写反在这里编译不过不了真机——它只会变成"点画笔永远画不上"。
    /// </summary>
    public static void SelectTool(CanvasTool tool)
    {
        CommitOpenStroke();                       // 先收手上那条：不然它会接到新工具的设置上
        _tool = tool;
        SetClickThrough(CanvasModes.IsClickThroughAfter(tool));
    }

    /// <summary>再点当前选中的笔＝收笔回穿透态（与截图/贴图那条"再点取消选择"同一交互语言）。</summary>
    public static void ToggleTool(CanvasTool tool)
    {
        if (_tool == tool && !_clickThrough) SetClickThrough(true);
        else SelectTool(tool);
    }

    public static void SelectColor(int index)
    {
        if (index < 0 || index >= Palette.Count) return;
        _colorIndex = index;
        StateChanged?.Invoke();
    }

    public static void SelectWidth(int step)
    {
        CommitOpenStroke();
        _widthStep = Math.Clamp(step, 0, CanvasWidths.Steps.Length - 1);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 鼠标穿透。<b>开启后画布收不到任何鼠标事件</b>，所以出口不能只有工具条上那一颗：
    /// 全局热键、托盘、以及"工具条自己始终能被点"三条一起兜着（§16.6 点名的"找不回"）。
    /// </summary>
    public static void SetClickThrough(bool on)
    {
        _clickThrough = on;
        Notice = null;                                // 用户自己动手了，上一条"为什么让位"就该翻篇
        _yieldedTo = IntPtr.Zero;                     // 也允许对同一个窗再说一次（不然改了也没反馈）
        CommitOpenStroke();
        foreach (var screen in Screens)
        {
            screen.Window.SetClickThrough(on);
            screen.Window.SetDrawCursor(!on);
        }
        // 摘/加穿透时补的那一发 FRAMECHANGED 带了 NOZORDER，不会自己往上蹿；
        // 但态一换就顺手把定序再做一遍——工具条点不动的代价是"整个功能出不去"。
        PlaceLayersBelowChrome();
        StateChanged?.Invoke();
    }

    /// <summary>光标光晕开关（关掉＝荧光笔态下鼠标不再有那团颜色跟着走）。</summary>
    public static void SetHalo(bool on)
    {
        _haloEnabled = on;
        foreach (var screen in Screens) screen.Trail.CursorHaloEnabled = on;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 截图抓那一帧时把画布那块玻璃收起来（<b>只给 <c>ScreenshotService.Grab</c> 用，那里成对调用</b>）。
    /// <para>
    /// 收的是玻璃本身，不是"擦掉笔迹"：笔迹留在 <see cref="Screen.Persistent"/> 与荧光段里，
    /// 还回来之后一切照旧——用户按「截图带画布＝关」是要给别人一张干净的图，不是要把黑板擦掉。
    /// </para>
    /// <para>
    /// <b>工具条不跟着收</b>：WinUI 窗重新点亮必然抢前台（见批次 WA 那条配方），把刚建起来的遮罩窗的
    /// 焦点抢走会让整次截图失去键盘出口。工具条只是屏幕边上一条小条，不是"画布上的内容"。
    /// </para>
    /// </summary>
    public static void SetHiddenForCapture(bool hidden)
    {
        if (!_running) return;
        foreach (var screen in Screens) screen.Window.SetVisible(!hidden);
    }

    /// <summary>每屏各撤各的最后一条：用户看的是"刚才那一笔"，而它落在哪块屏只有层自己知道。</summary>
    public static void Undo()
    {
        var changed = false;
        foreach (var screen in Screens)
            if (screen.Ink.Undo())
            {
                Recomposite(screen);
                changed = true;
            }
        if (changed) FlushAll();
    }

    public static void ClearAll()
    {
        foreach (var screen in Screens)
        {
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

    /// <summary>把"屏幕 + 笔迹"合成一张钉到桌面上（§16.5 的"快照为贴图"）。</summary>
    public static void SnapshotToPin()
    {
        if (!TryCompose(out var pixels, out var width, out var height, out var source, out var reason))
        {
            Report("贴图失败", reason);
            return;
        }
        ScreenshotService.PinPixels(pixels, width, height, source);
    }

    public static void SnapshotToClipboard()
    {
        if (!TryCompose(out var pixels, out var width, out var height, out _, out var reason))
        {
            Report("复制失败", reason);
            return;
        }
        _ = ScreenshotService.CopyPixelsAsync(pixels, width, height, "画布");
    }

    public static void SavePng()
    {
        if (!TryCompose(out var pixels, out var width, out var height, out _, out var reason))
        {
            Report("存图失败", reason);
            return;
        }
        _ = ScreenshotService.SavePixelsAsync(pixels, width, height, "画布");
    }

    // ────────── 输入 ──────────

    private static void OnPressed(Screen screen, CanvasPointer pointer)
    {
        if (screen.Window.IsClickThrough) return;          // 穿透态不该收到，真收到也不能画（鼠标本来要给下面的应用）
        BeginPress(screen, pointer.At, _tool == CanvasTool.Highlighter ? Press.Ephemeral : Press.Drawing);
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
        else
        {
            var stroke = screen.Ink.Begin(kind == Press.QuickPen ? CanvasTool.Pen : _tool,
                colour, WidthFor(kind == Press.QuickPen ? CanvasTool.Pen : _tool), at);
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
        else if (screen.Ink.Drawing is { } stroke && stroke.AddPoint(pointer.At))
        {
            screen.Dirty.Add(CanvasCompositor.PaintTail(
                screen.Persistent, screen.Window.Width, screen.Window.Height, stroke));
            dirty = true;
        }
        if (dirty && now - screen.LastFlushMs >= DragThrottleMs) Flush(screen);
    }

    private static void OnReleased(Screen screen, CanvasPointer pointer)
    {
        FinishPress(screen);
        // 轮询接手的那两种按下（荧光笔按住 / Ctrl+Alt 圈画）是"临时摘掉穿透"换来的，
        // 抬起必须还回去——否则一次圈画之后整台机器的鼠标就被我们扣住了。
        if (_wasTemporary) EndTemporaryPress();
    }

    /// <summary>收手上那一笔（不碰穿透态）：荧光段交给 TTL 淡出，持久笔迹要定形。</summary>
    private static void FinishPress(Screen? screen)
    {
        if (screen is null) { _press = Press.None; return; }
        if (_press == Press.Ephemeral) Flush(screen);          // 段留在 Trail 里按 TTL 淡，不进持久层
        else
        {
            screen.Ink.End();
            Recomposite(screen);                               // 定形：橡皮这类"取大不管"的结果要一次画成
            Flush(screen);
        }
        _press = Press.None;
        _wasTemporary = false;
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
        if (_press != Press.None || !_clickThrough || !down) return;

        var quick = LayeredCanvasWindow.CtrlAltDown;
        if (!quick && _tool != CanvasTool.Highlighter) return;     // 这一按该归下层应用，别抢
        var screen = ScreenAt(new PixelPoint(cursorX, cursorY));
        if (screen is null) return;

        foreach (var s in Screens) { s.Window.SetClickThrough(false); s.Window.SetDrawCursor(true); }
        screen.Window.Capture();                                   // 那一次按下不会再来，抓取要自己补
        _wasTemporary = true;
        BeginPress(screen, new PixelPoint(cursorX - screen.Bounds.X, cursorY - screen.Bounds.Y),
            quick ? Press.QuickPen : Press.Ephemeral);
    }

    /// <summary>把临时摘掉的穿透还回去（<see cref="_clickThrough"/> 本身没动，所以工具条状态不会跳）。</summary>
    private static void EndTemporaryPress()
    {
        _press = Press.None;
        _wasTemporary = false;
        if (!_clickThrough) return;                                // 本来就在绘制态，不用恢复
        foreach (var s in Screens) { s.Window.SetClickThrough(true); s.Window.SetDrawCursor(false); }
    }

    /// <summary>
    /// "画布主动把鼠标交回去了"的原因（工具条状态行跟着显示一次）。没有这句话，用户只会觉得
    /// "刚才能画现在不能画，这软件自己抽风了"。
    /// </summary>
    public static string? Notice { get; private set; }

    /// <summary>
    /// <b>绘制态每一帧问一句实话：光标这一层到底是谁？</b>（发起人点名的"没有实时监测光标位于哪一层"）
    /// <para>
    /// 画布声称"绘制中"却不再是最上层时，那一次按下会同时被两家用：下面那个应用把它当成框选/选文字，
    /// 我们这边还可能去抢着画——真机反馈的"画布和应用交互冲突"就是这么来的。最常见的触发是
    /// 按 Win 呼出开始菜单/搜索、系统弹窗、别的全屏应用——它们都是能盖住我们那块玻璃的顶层窗。
    /// </para>
    /// <para>
    /// 判据是逐像素问 <see cref="LayeredCanvasWindow.WindowAt"/>（不是猜前台窗口），而<b>分界只看进程</b>：
    /// 命中窗口属于别的程序才交回鼠标。穿透态跳过这条（那时"不是我们"是设计本意）；
    /// 手上正有一笔也跳过（那一笔已经归画布画完）。
    /// </para>
    /// </summary>
    private static void YieldIfNotOurLayer(int cursorX, int cursorY)
    {
        if (_clickThrough || _press != Press.None || !_running) return;
        var hit = LayeredCanvasWindow.WindowAt(cursorX, cursorY);
        if (hit == IntPtr.Zero || hit == _yieldedTo) return;               // 取不到不下判断；同一个窗只说一次
        if (WindowInterop.GetWindowThreadProcessId(hit, out var pid) == 0) return;
        // 判据按进程，绝不按"句柄等于工具条/面板/玻璃"。真因（批次 WD-7 自己造成的回归）：
        // WindowFromPoint 给的是这一点上<b>最深</b>的那个 HWND——WinUI 3 的条子内容住在它自己的子窗里，
        // tooltip 与浮层更是另开的顶层窗，跟 GetHwnd() 拿到的那一个必然不相等。于是光标一停在条子上
        // 就被判成"别人盖住了画布"，每帧把状态退回穿透：点「画笔」「穿透」全都"没反应"。
        // 自家窗口拿走这一按不会造成"一次按下两家用"（消息根本到不了画布，最坏只是那块画不上）。
        if (pid == (uint)Environment.ProcessId) return;
        StarLog.Warn($"[Canvas] 绘制态发现光标那一层已被另一个程序占走（进程 {pid}），主动交回鼠标");
        SetClickThrough(true);
        Notice = "已自动交回鼠标：另一个程序的窗口（例如按 Win 呼出的开始菜单）盖住了画布；要接着画请再点「画笔」";
        // 同一件事只说一次：不然那个窗一直压在上面的话，每帧都会"让位 + 一条 WARN"，
        // 状态行还会来回跳。用户下次自己动手（SetClickThrough）时这个记号就清掉。
        _yieldedTo = hit;
        StateChanged?.Invoke();
    }

    private static IntPtr _yieldedTo;

    private static Screen? ScreenAt(PixelPoint point)
        => Screens.FirstOrDefault(s => s.Bounds.X <= point.X && point.X < s.Bounds.Right
            && s.Bounds.Y <= point.Y && point.Y < s.Bounds.Bottom);

    private static int WidthFor(CanvasTool tool)
        => tool == CanvasTool.Eraser ? CanvasWidths.EraserDiameter : CanvasWidths.At(_widthStep);

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
        if (!_running || Screens.Count == 0) return;
        var now = Environment.TickCount64;
        WindowInterop.GetCursorPos(out var cursor);
        // 先验层，再决定这一按要不要抢：别人已经把最上层占走了还去"按住即画"，
        // 就会同时出现"应用在框选/选文字" + "画布在画"两件事（真机反馈的交互冲突）
        YieldIfNotOurLayer(cursor.X, cursor.Y);
        // 穿透态收不到鼠标消息，"这一按是不是要画"只能在这里看按键状态（§16.5.2 的零摩擦入口）
        PollPress(cursor.X, cursor.Y);
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
    /// 持久层重算：清掉"所有笔迹可能碰到的地方"再按顺序全部重画一遍。
    /// 区域不取整块屏幕（那是 33MB 的提交），也不取单条笔迹——<b>橡皮是按比例减 alpha 的，
    /// 同一条橡皮走两次会擦过头</b>，所以必须从一块干净的区域一次画成。
    /// </summary>
    private static void Recomposite(Screen screen)
    {
        var width = screen.Window.Width;
        var height = screen.Window.Height;
        var region = CanvasCompositor.Union(screen.Ink.Strokes.Select(stroke => stroke.Bounds).ToList(), width, height);
        if (region.IsEmpty)
        {
            CanvasCompositor.Clear(screen.Persistent);
            screen.Dirty.Add(new IntRect(0, 0, width, height));
            return;
        }
        CanvasCompositor.ClearRect(screen.Persistent, width, height, region);
        foreach (var stroke in screen.Ink.Strokes)
            CanvasCompositor.Paint(screen.Persistent, width, height, stroke);
        screen.Dirty.Add(region);
    }

    /// <summary>换工具/换粗细之前先把手上那条收掉，免得它接到新设置下去（症状："画着画着笔自己变粗了"）。</summary>
    private static void CommitOpenStroke()
    {
        // 轮询抢来的那一按（荧光笔/Ctrl+Alt 圈画）也要在这里收口：换工具时它还挂着的话，
        // 抬起事件会被新工具吃掉，屏幕上就留下一条"永远在画"的笔迹
        if (_press is Press.Ephemeral or Press.QuickPen)
        {
            FinishPress(_pressScreen);
            EndTemporaryPress();
        }
        var changed = false;
        foreach (var screen in Screens)
            if (screen.Ink.Drawing is not null)
            {
                screen.Ink.End();
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
        if (!_running) Start();
        if (!_running) return;            // Start 失败时它自己已经报过原因，这里不再补一条
        ToggleTool(tool);
    }

    public static void HotkeyClickThrough() => RequireRunning("交出 / 收回鼠标", () => SetClickThrough(!_clickThrough));

    public static void HotkeyUndo() => RequireRunning("撤销上一笔", Undo);

    public static void HotkeyClear() => RequireRunning("清空笔迹", ClearAll);

    public static void HotkeySave() => RequireRunning("存为图片", SavePng);

    public static void HotkeyCopy() => RequireRunning("复制到剪贴板", SnapshotToClipboard);

    public static void HotkeyPin() => RequireRunning("贴到桌面", SnapshotToPin);

    /// <summary>
    /// 总开关读的是磁盘上那一份（不缓存）：设置页里改完立刻生效，不需要重启也不需要"通知一遍"，
    /// 而漏通知正是"开关是关的、功能还在跑"这种鬼状态的来源。
    /// </summary>
    private static bool EnabledBySetting
        => (App.Services?.GetService(typeof(SettingsStore)) as SettingsStore)?.LoadCanvasEnabled() ?? true;

    /// <summary>
    /// 画布内动作的闸门：<b>板子没开着时按这些键要给一句看得见的原因</b>，不能"按了没反应"——
    /// 那在用户眼里与功能坏了是同一件事。两种"没开着"要分开说：功能被关掉时指向快捷键是指错路，
    /// 所以那里说的是"去设置里打开"。
    /// </summary>
    private static void RequireRunning(string what, Action run)
    {
        if (_running)
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
    /// 取"鼠标所在那块屏"的画面 + 笔迹。<b>只合成一块屏</b>：跨屏一张大图会把另一块屏的内容也截进来，
    /// 而钉上去之后它既不属于这块屏也不属于那块——用户要的是"我圈的那块黑板"。
    /// </summary>
    private static bool TryCompose(out byte[] pixels, out int width, out int height,
        out IntRect source, out string reason)
    {
        pixels = Array.Empty<byte>();
        width = height = 0;
        source = new IntRect();
        reason = string.Empty;
        if (Screens.Count == 0)
        {
            reason = "画布没开着";
            return false;
        }
        WindowInterop.GetCursorPos(out var cursor);
        var screen = Screens.FirstOrDefault(s => s.Bounds.X <= cursor.X && cursor.X < s.Bounds.Right
            && s.Bounds.Y <= cursor.Y && cursor.Y < s.Bounds.Bottom) ?? Screens[0];

        var captured = GdiScreenCapture.CaptureVirtualScreen();
        if (!captured.Ok || captured.Frame is not { } frame)
        {
            reason = captured.Error ?? "系统没有返回画面";
            return false;
        }
        if (ScreenshotService.TryCrop(frame, screen.Bounds) is not { } crop)
        {
            reason = "这一块屏在截到的画面外面（显示器可能刚被拔掉）";
            return false;
        }

        // 笔迹（持久层 + 还活着的荧光段）合成到这块屏的画面之上；光晕是"提示我在什么模式"，不进快照
        var ink = new uint[crop.Width * crop.Height];
        Array.Copy(screen.Persistent, ink, Math.Min(screen.Persistent.Length, ink.Length));
        foreach (var segment in screen.Trail.Segments)
            CanvasCompositor.Paint(ink, crop.Width, crop.Height, segment.Stroke, segment.AlphaScale);
        CanvasCompositor.OverlayOntoFrame(crop.Pixels, crop.Width, crop.Height,
            new IntRect(0, 0, 0, 0), ink, screen.Window.Width, screen.Window.Height);

        pixels = crop.Pixels;
        width = crop.Width;
        height = crop.Height;
        source = screen.Bounds;
        return true;
    }

    // ────────── 工具条 ──────────

    private static void ShowToolbar()
    {
        try
        {
            _toolbar ??= new CanvasToolbarWindow();
            _toolbar.ShowAt(Screens[0].Bounds, Screens[0].Scale);
            PlaceLayersBelowChrome();
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
        if (!_running || Screens.Count == 0 || _toolbar is null)
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
            PlaceLayersBelowChrome();
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
        try { _panel.ClosePanel(); }
        catch (Exception ex) { StarLog.Warn($"[Canvas] 快捷键面板没收掉：{ex.Message}"); }
        _panel = null;
        StateChanged?.Invoke();
    }

    /// <summary>面板自己关掉了（✕、退出画布、系统收尾）：编排这边必须忘掉它，不然「⌨」再点就打不开。</summary>
    public static void PanelClosed(CanvasHotkeyPanelWindow panel)
    {
        if (!ReferenceEquals(_panel, panel)) return;
        _panel = null;
        StateChanged?.Invoke();       // 让那颗按钮的高亮跟着掉回去
    }

    /// <summary>
    /// 定序：工具条 → 快捷键面板 → 画布 → 下层应用，一条链显式排出来。
    /// <para>
    /// <b>不能只"提"工具条</b>：它本来就在 topmost 带里，对这样的窗口再传一次
    /// <c>HWND_TOPMOST</c> 只换带、不在带内重排（＝什么都没做）。真机症状就是
    /// "按过穿透／右键之后，工具条一颗按钮都点不动"——而工具条是唯一看得见的出口。
    /// 所以这里反过来做：把每块画布显式插到面板之下、面板插到工具条之下，一次定序，不靠运气。
    /// </para>
    /// </summary>
    private static void PlaceLayersBelowChrome()
    {
        var chrome = _toolbar?.Hwnd ?? IntPtr.Zero;
        if (chrome == IntPtr.Zero) return;
        var above = chrome;
        if (_panel is not null)
        {
            _panel.PlaceUnder(chrome);          // 面板在工具条之下：两个都要点得到，但按钮排在更上面
            above = _panel.Hwnd;                // 画布压在面板之下
        }
        foreach (var screen in Screens) screen.Window.PlaceBelow(above);
    }

    private static void CloseToolbar()
    {
        // 面板挂在工具条下面：条子收了还留着面板，它就成了一块"没有主人的浮窗"（退出画布后仍在屏幕上）
        HideHotkeyPanel();
        if (_toolbar is null) return;
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
