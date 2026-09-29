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
/// CanvasService 的这一段——每帧那一段：节拍、算脏区、重铺持久层、叠荧光段、叠光晕、只提交脏区，以及慢帧取证。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段、嵌套类型与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public static partial class CanvasService
{

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
            // 光晕跟着鼠标走：旧位置要复原、新位置要叠上。
            // <b>这一块圆的半径与幕布亮区是同一个数、同一处换算</b>（批次 S4-⑥，用户裁"合并成一块圆，两个读数"）：
            // 从前光晕按荧光笔笔宽档算、亮区按 160 DIP 算，同一帧上鼠标处就有两个不同大小的圆——只有眼睛能看出来。
            if (!screen.LastGlow.IsEmpty) screen.Dirty.Add(screen.LastGlow);
            screen.GlowAt = null;
            if (CursorCircle.ShowsHalo(_haloAlways, AnnotationHub.Backdrop)
                && screen.Bounds.X <= cursor.X && cursor.X < screen.Bounds.Right
                && screen.Bounds.Y <= cursor.Y && cursor.Y < screen.Bounds.Bottom)
            {
                var local = new PixelPoint(cursor.X - screen.Bounds.X, cursor.Y - screen.Bounds.Y);
                var radius = CursorCircle.RadiusInPixels(screen.Scale, CursorCircleRadiusDip);
                screen.GlowAt = local;
                screen.GlowRadius = radius;
                screen.LastGlow = CursorCircle.BoxOf(local, radius);
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
                screen.GlowRadius, screen.Trail.HaloColorBgra);
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
}
