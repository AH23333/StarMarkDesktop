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
/// CanvasService 的这一段——开与关这一头：起停那块板子、每屏一块玻璃的建与拆、会话态落到窗上、截图期间把玻璃收起来。
/// <para>这是按访问面拆出来的 partial：<b>不持有任何状态</b>——字段、嵌套类型与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public static partial class CanvasService
{

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
                        Trail = new EphemeralInk(),
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
}
