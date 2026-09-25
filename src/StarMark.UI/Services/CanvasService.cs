#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
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

        /// <summary>上一帧叠过的地方（光晕 + 荧光段）。复原它们才需要这块记号。</summary>
        public IntRect LastOverlay { get; set; }

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
    private static bool _clickThrough;
    private static bool _haloEnabled = true;

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
                    var screen = new Screen
                    {
                        Window = window,
                        Bounds = bounds,
                        Scale = monitor.Scale,
                        Persistent = new uint[window.Width * window.Height],
                        Ink = new CanvasInk(),
                        Trail = new EphemeralInk { CursorHaloEnabled = _haloEnabled },
                    };
                    window.PointerPressed += p => OnPressed(screen, p);
                    window.PointerMoved += p => OnMoved(screen, p);
                    window.PointerReleased += p => OnReleased(screen, p);
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
            StarLog.Info($"[Canvas] 画布模式开启：{Screens.Count} 屏，工具={_tool}，穿透={_clickThrough}");
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
        // 下次进来该是"能画"那一态：把穿透留在开着的状态会让下一次按热键像"画不上"
        _clickThrough = false;
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

    public static void SelectTool(CanvasTool tool)
    {
        CommitOpenStroke();                       // 先收手上那条：不然它会接到新工具的设置上
        _tool = tool;
        StateChanged?.Invoke();
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
        CommitOpenStroke();
        foreach (var screen in Screens)
        {
            screen.Window.SetClickThrough(on);
            screen.Window.SetDrawCursor(!on);
        }
        StateChanged?.Invoke();
    }

    /// <summary>光标光晕开关（关掉＝荧光笔态下鼠标不再有那团颜色跟着走）。</summary>
    public static void SetHalo(bool on)
    {
        _haloEnabled = on;
        foreach (var screen in Screens) screen.Trail.CursorHaloEnabled = on;
        StateChanged?.Invoke();
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
            screen.Trail.Clear();
            Recomposite(screen);
        }
        FlushAll();
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
        var colour = Palette[_colorIndex].Bgra;
        var width = WidthFor(_tool);
        var now = Environment.TickCount64;
        if (_tool == CanvasTool.Highlighter)
        {
            var segment = screen.Trail.Begin(pointer.At, colour, width, now);
            screen.Dirty.Add(segment.Stroke.Bounds);
            Flush(screen);
            return;
        }
        var stroke = screen.Ink.Begin(_tool, colour, width, pointer.At);
        screen.Dirty.Add(CanvasCompositor.Paint(
            screen.Persistent, screen.Window.Width, screen.Window.Height, stroke));
        Flush(screen);
    }

    private static void OnMoved(Screen screen, CanvasPointer pointer)
    {
        var now = Environment.TickCount64;
        var dirty = false;
        if (_tool == CanvasTool.Highlighter)
        {
            if (pointer.LeftDown && screen.Trail.Extend(pointer.At, now))
            {
                screen.Dirty.Add(screen.Trail.Segments[^1].Stroke.Bounds);
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
        if (_tool == CanvasTool.Highlighter)
        {
            Flush(screen);                 // 段留在Trail里按 TTL 淡，不进持久层
            return;
        }
        screen.Ink.End();
        Recomposite(screen);               // 定形：橡皮这类"取大不管"的结果要从干净区域一次画成
        Flush(screen);
    }

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
        foreach (var screen in Screens)
        {
            // 上一帧叠过的地方必须先加进脏区：否则淡掉的那团光与走过的光晕会赖在屏幕上
            screen.Dirty.Add(screen.LastOverlay);
            screen.Trail.Tick(now);
            var overlay = screen.LastOverlay;
            foreach (var segment in screen.Trail.Segments)
            {
                screen.Dirty.Add(segment.Stroke.Bounds);
                overlay = Bigger(overlay, segment.Stroke.Bounds);
            }

            screen.GlowAt = null;
            if (_tool == CanvasTool.Highlighter && screen.Trail.CursorHaloEnabled
                && screen.Bounds.X <= cursor.X && cursor.X < screen.Bounds.Right
                && screen.Bounds.Y <= cursor.Y && cursor.Y < screen.Bounds.Bottom)
            {
                var local = new PixelPoint(cursor.X - screen.Bounds.X, cursor.Y - screen.Bounds.Y);
                var radius = CanvasWidths.RadiusFor(CanvasTool.Highlighter, CanvasWidths.At(_widthStep));
                screen.GlowAt = local;
                screen.Dirty.Add(new IntRect(local.X - radius, local.Y - radius, radius * 2 + 1, radius * 2 + 1));
                overlay = Bigger(overlay, new IntRect(local.X - radius, local.Y - radius, radius * 2 + 1, radius * 2 + 1));
            }
            screen.LastOverlay = overlay;
        }
        FlushAll();
    }

    private static void FlushAll()
    {
        foreach (var screen in Screens) Flush(screen);
    }

    /// <summary>重算并提交一块屏的脏区：铺持久层 → 叠荧光段 → 叠光晕。</summary>
    private static void Flush(Screen screen)
    {
        var width = screen.Window.Width;
        var height = screen.Window.Height;
        var rect = CanvasCompositor.Union(screen.Dirty, width, height);
        screen.Dirty.Clear();
        if (rect.IsEmpty) return;
        CanvasCompositor.CopyRect(screen.Persistent, screen.Window.Pixels, width, height, rect);
        foreach (var segment in screen.Trail.Segments)
            CanvasCompositor.Paint(screen.Window.Pixels, width, height, segment.Stroke, segment.AlphaScale);
        if (screen.GlowAt is { } glow)
            CanvasCompositor.PaintGlow(screen.Window.Pixels, width, height, glow,
                CanvasWidths.RadiusFor(CanvasTool.Highlighter, CanvasWidths.At(_widthStep)),
                screen.Trail.HaloColorBgra);
        screen.Window.Present(rect);
        screen.LastFlushMs = Environment.TickCount64;
    }

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

    private static IntRect Bigger(IntRect a, IntRect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new IntRect(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
    }

    /// <summary>换工具/换粗细之前先把手上那条收掉，免得它接到新设置下去（症状："画着画着笔自己变粗了"）。</summary>
    private static void CommitOpenStroke()
    {
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
        }
        catch (Exception ex)
        {
            // 工具条建不起来不牵连画布本身：热键、Esc、托盘三条出口仍然在
            StarLog.Error("[Canvas] 工具条没出现（画布仍可用：热键／托盘都在）", ex);
        }
    }

    private static void CloseToolbar()
    {
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
