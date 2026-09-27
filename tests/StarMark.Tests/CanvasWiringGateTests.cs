#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using StarMark.Core.Hotkeys;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 屏幕画布的<b>接线</b>闸门（扫源码，不跑界面）。合成与寿命那些能拿像素断言的部分在
/// <see cref="CanvasInkTests"/>；这里守的是另一类：只有真机才看得见、但一写错就变成
/// "整个功能不可用"或"用户被自己困住"的链路约束。
/// </summary>
public sealed class CanvasWiringGateTests
{
    private const string Layer = "src/StarMark.Integrations/Canvas/LayeredCanvasWindow.cs";
    private const string Native = "src/StarMark.Integrations/Canvas/CanvasNative.cs";
    private const string Compositor = "src/StarMark.Core/Canvas/CanvasCompositor.cs";
    private const string Service = "src/StarMark.UI/Services/CanvasService.cs";
    private const string Hub = "src/StarMark.UI/Services/AnnotationHub.cs";
    private const string Director = "src/StarMark.UI/Services/LayerDirector.cs";
    private const string Session = "src/StarMark.Core/Capture/AnnotationStage.cs";
    private const string LayerRulesFile = "src/StarMark.Core/Capture/LayerRules.cs";
    private const string HotkeyGateFile = "src/StarMark.Core/Hotkeys/HotkeyGate.cs";
    private const string Screenshot = "src/StarMark.UI/Services/ScreenshotService.cs";
    private const string Toolbar = "src/StarMark.UI/Views/CanvasToolbarWindow.xaml.cs";
    private const string ToolbarXaml = "src/StarMark.UI/Views/CanvasToolbarWindow.xaml";
    private const string Panel = "src/StarMark.UI/Views/CanvasHotkeyPanelWindow.xaml.cs";
    private const string PanelXaml = "src/StarMark.UI/Views/CanvasHotkeyPanelWindow.xaml";
    private const string App = "src/StarMark.UI/App.xaml.cs";
    private const string SettingsPageCode = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string Store = "src/StarMark.UI/Helpers/SettingsStore.cs";
    private const string MainWindow = "src/StarMark.UI/MainWindow.xaml.cs";

    // ────────── 为什么是分层窗 ──────────

    // ────────── 批次 WC：真机"看不见鼠标、点不到任何东西"三条 ──────────

    [Fact]
    public void SetCursorTakesOneArgument_AndSetCursorMessageIsAnsweredTrue()
    {
        // Win32 的 SetCursor 只有一个参数（光标句柄）。按"窗口+句柄"两个参数声明时，x64 上第一个实参
        // 落到句柄位＝ SetCursor(NULL) ＝整块画布上光标直接消失（真机症状正是"看不见鼠标、点不到东西"）。
        var native = SourceGate.ReadRepoFile(Native);
        Assert.Contains("public static extern IntPtr SetCursor(IntPtr hCursor);", native);
        Assert.DoesNotContain("SetCursor(IntPtr hWnd, IntPtr hCursor)", native);

        var proc = SourceGate.MethodBody(SourceGate.ReadRepoFile(Layer), "private static IntPtr HandleMessage");
        var setCursor = proc.IndexOf("WM_SETCURSOR", StringComparison.Ordinal);
        var nextCase = proc.IndexOf("case CanvasNative.WM_ERASEBKGND", setCursor, StringComparison.Ordinal);
        var handler = proc[setCursor..nextCase];
        Assert.Contains("SetCursor(", handler);
        Assert.Contains("return new IntPtr(1);", handler);
        // 回 FALSE＝告诉系统"我没处理"，光标归属就悬了
        Assert.DoesNotContain("return IntPtr.Zero;", handler);
    }

    [Fact]
    public void RightClickOnTheCanvasHandsTheMouseBack_TheOnlyPureMouseExit()
    {
        var layer = SourceGate.MethodBody(SourceGate.ReadRepoFile(Layer), "private static IntPtr HandleMessage");
        var service = SourceGate.ReadRepoFile(Service);
        Assert.Contains("WM_RBUTTONDOWN", layer);
        Assert.Contains("RightPressed?.Invoke()", layer);
        Assert.Contains("window.RightPressed += () => SetClickThrough(true);", service);
    }

    [Fact]
    public void ClickThroughToggleNeverReZOrdersTheCanvas()
    {
        // 切换穿透后补一发 FRAMECHANGED 让命中测试立刻生效，但绝不能顺手把自己提到 topmost 链顶端：
        // 那会把工具条（穿透态下唯一的鼠标出口）盖掉。
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Layer), "public void SetClickThrough(bool on)");
        Assert.Contains("SWP_FRAMECHANGED", body);
        Assert.Contains("SWP_NOZORDER", body);
        Assert.DoesNotContain("HWND_TOPMOST", body);
    }

    [Fact]
    public void ToolbarIsMeasuredOnlyAfterTheWindowHasBeenLaidOutAndClampedInsideTheScreen()
    {
        var toolbar = SourceGate.ReadRepoFile(Toolbar);
        var show = SourceGate.MethodBody(toolbar, "public void ShowAt(IntRect screen, double scale)");
        var fit = SourceGate.MethodBody(toolbar, "private void Fit(bool centerOnScreen)");
        // 写死的尺寸会把 ✕ 挤出客户区（内容一多就发生，且只在真机看得见）
        Assert.Contains("Root.DesiredSize", fit);
        Assert.DoesNotContain("FallbackWidthDip * scale", fit);
        // 但"窗还没亮就量"同样会出事：那时按钮模板尚未应用，量到的是十几颗空按钮的 MinWidth 之和，
        // 条子只有约 360 宽，右边整段出口按钮在窗外（真机："顶部菜单栏右侧被截断"）。
        Assert.True(show.IndexOf("AppWindow.Show()") < show.IndexOf("Root.UpdateLayout()"),
            "必须先亮窗");
        Assert.True(show.IndexOf("Root.UpdateLayout()") < show.IndexOf("Fit(centerOnScreen: true)"),
            "排一遍之后才允许量尺寸");
        Assert.Contains("Root.Measure(new Windows.Foundation.Size(availableDip, double.PositiveInfinity))", fit);
        Assert.DoesNotContain("double.PositiveInfinity, double.PositiveInfinity", fit);   // 无限大测量＝比屏还宽
        Assert.Contains("Math.Clamp(x,", fit);                                            // 左右都夹回屏内
        // 状态文本会变长（穿透那句折两行）→ 刷新时必须重算尺寸，且不能把用户拖到的位置挪走
        var refresh = SourceGate.MethodBody(toolbar, "private void Refresh()");
        Assert.Contains("Fit(centerOnScreen: false);", refresh);
        Assert.Contains("RaiseAboveCanvas();", refresh);
        Assert.Contains("MaxWidth", SourceGate.ReadRepoFile(ToolbarXaml));                 // 长文本靠换行不靠撑窗
    }

    // ────────── 批次 WC-4：真机"一按画笔整块屏幕像卡死"（提交参数） ──────────

    /// <summary>
    /// 提交必须带 <c>ULW_ALPHA</c>。<b>dwFlags 留 0 不是"什么都不做"，而是"不要用混合函数"</b>：
    /// 于是 BLENDFUNCTION（含 AC_SRC_ALPHA）整个被系统忽略，那块 alpha=0 的缓冲被按不透明贴图上，
    /// 用户看到的是全屏 topmost 的一面黑墙，而它正吃着所有鼠标输入——症状写作"屏幕卡死"。
    /// 两条出口（Indirect 与老接口兜底）都要带，缺一条就是"大多数时候好的、某些机器上是黑的"。
    /// </summary>
    [Fact]
    public void EveryPresentPathCarriesUlwAlpha()
    {
        var push = SourceGate.MethodBody(SourceGate.ReadRepoFile(Layer), "private unsafe void Push");
        Assert.Contains("dwFlags = CanvasNative.ULW_ALPHA", push);
        Assert.DoesNotContain("dwFlags = 0", push);
        Assert.Equal(2, SourceGate.Count(push, "CanvasNative.ULW_ALPHA"));
        Assert.Contains("public const uint ULW_ALPHA = 0x0000_0002;", SourceGate.ReadRepoFile(Native));
    }

    /// <summary>
    /// <c>UPDATELAYEREDWINDOWINFO</c> 的槽位逐字钉住。字段名写对而<b>槽位</b>写错时：
    /// 大小对、cbSize 对、编译过、调用返回 TRUE——只有屏幕上看得见。
    /// 上一版把第 3 槽命名为 <c>pptSrc</c>，于是每次提交都在"把窗口搬到 (0,0)"。
    /// </summary>
    [Fact]
    public void UlwiStructSlotsMatchTheWin32Header()
    {
        var block = SourceGate.Between(SourceGate.ReadRepoFile(Native),
            "public struct UPDATELAYEREDWINDOWINFO", "\n    }");
        var fields = System.Text.RegularExpressions.Regex.Matches(block, @"public\s+(\S+)\s+(\w+);")
            .Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value}")
            .ToList();

        Assert.Equal(new[]
        {
            "uint cbSize", "IntPtr hdcDst", "IntPtr pptDst", "IntPtr psize", "IntPtr hdcSrc",
            "IntPtr pptSrc", "uint crKey", "IntPtr pbcf", "uint dwFlags", "IntPtr prcDirty",
        }, fields);
    }

    // ────────── 批次 WG：每帧只重算"真的变了的那一块"（真机："荧光笔绘制过程非常卡"） ──────────

    /// <summary>
    /// 帧循环的脏区必须由"这一帧到底变了什么"驱动。<b>反钉上一版那两句看着无害的写法</b>：
    /// 无条件把"上一帧所有荧光段叠过的地方"并进脏区＝脏区等于整条笔迹的包围盒，
    /// 于是"铺持久层 + 整段重画"每帧都按最长那条算（4K 粗档实测 643 ms/帧，越画越卡）。
    /// </summary>
    [Fact]
    public void FrameDirtyIsDrivenByWhatActuallyChanged()
    {
        var tick = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static void OnFrameTick");
        Assert.Contains("MarkSegmentIfFading(screen, segment)", tick);     // 整段重算只由"淡出对不上"触发
        Assert.Contains("var expired = screen.Trail.Tick(now);", tick);    // 到期段占过的地方要交回脏区
        Assert.Contains("screen.Dirty.Add(expired)", tick);
        Assert.Contains("if (!screen.LastGlow.IsEmpty) screen.Dirty.Add(screen.LastGlow);", tick);
        Assert.DoesNotContain("LastOverlay", tick);
        Assert.DoesNotContain("Dirty.Add(segment.Stroke.Bounds)", tick);   // 扫不到这行才算没退回旧写法
    }

    /// <summary>
    /// 淡出的记号只在"浓度确实换了"时才推进——每帧无脑推＝每帧都判定成"要重算"，等于没有这条判据。
    /// </summary>
    [Fact]
    public void FadeMarkerOnlyMovesWhenTheLevelChanges()
    {
        var mark = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service),
            "private static void MarkSegmentIfFading");
        Assert.Contains("if (Math.Abs(segment.PaintScale - segment.AlphaScale) <= 1e-9) return;", mark);
        Assert.Contains("segment.PaintScale = segment.AlphaScale;", mark);
        Assert.Contains("screen.Dirty.Add(segment.Stroke.Bounds);", mark);
        // 先比再赋值：反过来源码看着一样，但每帧都会把整段划成脏区
        Assert.True(mark.IndexOf("return;") < mark.IndexOf("segment.PaintScale ="),
            "必须先比较再推进记号，否则这条判据永不生效");
    }

    /// <summary>
    /// 提交那一层必须<b>裁到脏区</b>、并且按"屏幕上此刻那一层"的浓度画。
    /// 少了裁剪，脏区再小也没用（整条笔迹照样重画一遍）；拿错浓度就会"淡到一半停住"或反复重画。
    /// </summary>
    [Fact]
    public void FlushRepaintsOnlyTheDirtyRect_AtThePaintedFadeLevel()
    {
        var flush = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static void Flush(Screen screen)");
        Assert.Contains(
            "CanvasCompositor.PaintClipped(screen.Window.Pixels, width, height, segment.Stroke, rect, segment.PaintScale);",
            flush);
        Assert.DoesNotContain("CanvasCompositor.Paint(screen.Window.Pixels", flush);
        Assert.DoesNotContain("segment.AlphaScale", flush);
    }

    /// <summary>拖动中只能把"新走的那一小条"并进脏区（整条包围盒＝把开销又养回去了）。</summary>
    [Fact]
    public void DraggingDirtiesOnlyTheNewlySweptStrip()
    {
        var moved = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static void OnMoved");
        Assert.Contains("Stroke.TailBounds", moved);
        Assert.DoesNotContain("Stroke.Bounds", moved);
    }

    /// <summary>
    /// 荧光段被丢掉之前，<b>它占过的地方必须先并进脏区</b>：段一没就再没人画它，
    /// 那块光会永远留在分层窗缓冲里（症状："清空后留一块擦不掉的光"）。
    /// </summary>
    [Fact]
    public void DroppingTheTrailDirtiesWhatItCovered()
    {
        var drop = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static void DropTrail");
        Assert.Contains("var was = screen.Trail.LiveBounds;", drop);
        Assert.Contains("screen.Trail.Clear();", drop);
        Assert.Contains("if (!was.IsEmpty) screen.Dirty.Add(was);", drop);
        Assert.True(drop.IndexOf("LiveBounds") < drop.IndexOf("Trail.Clear()"), "先取范围再清，反了就取不到了");
        // 服务里不许再有"直接 Clear 而不走 DropTrail"的漏口
        var service = SourceGate.WithoutMethod(SourceGate.ReadRepoFile(Service), "private static void DropTrail");
        Assert.DoesNotContain("Trail.Clear()", service);
    }

    /// <summary>
    /// 慢帧要留一行带原因的日志。<b>淡出期仍是"整段重算"</b>（那块地方每一像素的浓度都变了），
    /// 长笔迹上它还会随笔迹变长——这批把它收敛到"看得见数字"，下次才谈要不要再上一层。
    /// </summary>
    [Fact]
    public void SlowFramesReportTheirCause()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var report = SourceGate.MethodBody(service, "private static void ReportSlowFrame");
        Assert.Contains("Stopwatch.GetTimestamp()", report);            // TickCount64 只有 15.6ms 分辨率，量不出一帧
        Assert.Contains("StarLog.WarnThrottled", report);
        Assert.Contains("windowMs: 10_000", report);                    // 一行日志自己不许成为卡顿源
        Assert.Contains("if (ms < SlowFrameMs) return;", report);
        var flush = SourceGate.MethodBody(service, "private static void Flush(Screen screen)");
        Assert.Contains("ReportSlowFrame(since, rect, screen);", flush);
        Assert.True(flush.IndexOf("Present(rect)") < flush.IndexOf("ReportSlowFrame"),
            "提交也算在这一帧里：UpdateLayeredWindowIndirect 才是那块脏区真正的代价");
    }

    // ────────── 批次 WH：截图带不带画布 ──────────

    /// <summary>
    /// 「截图不带画布」只允许有一个收/还点：<b>抓那一帧的当场收起来，<c>finally</c> 里还回去</b>。
    /// <para>
    /// 为什么只能这么干：画布是 topmost 的分层窗，抓屏抓到的是已经合成完的屏幕，笔迹事后减不回来，
    /// 只有那一下不让它上屏。而"收起来"是会留下半个状态的操作——分开写到两处，
    /// 早晚会有一条路径（抓屏抛异常、返回空、显示器数为 0）漏掉还，
    /// 症状是"截了一次图，画布再也不显示了"，比不能切换严重得多。
    /// </para>
    /// </summary>
    [Fact]
    public void CanvasIsHiddenOnlyAroundTheGrab_AndAlwaysRestored()
    {
        var shot = SourceGate.ReadRepoFile(Screenshot);
        var grab = SourceGate.MethodBody(shot, "private static CaptureResult Grab()");
        Assert.Contains("|| !CanvasService.IsRunning) return GdiScreenCapture.CaptureVirtualScreen();", grab);
        Assert.Contains("CanvasService.SetHiddenForCapture(true);", grab);
        Assert.True(grab.IndexOf("finally", StringComparison.Ordinal)
                < grab.IndexOf("CanvasService.SetHiddenForCapture(false);", StringComparison.Ordinal),
            "还玻璃必须写在 finally 里——写在 try 后面就等于会漏");
        // 抓屏只能从 Grab() 走：Start 里再出现一次直接抓屏，就等于绕过这条闸门
        var start = SourceGate.MethodBody(shot, "public static void Start(");
        Assert.Contains("var captured = Grab();", start);
        Assert.DoesNotContain("GdiScreenCapture.CaptureVirtualScreen()", start);
        // 画布自己的那三条快照动作（贴图／复制／存图）不受这条设置影响：那三条就是要笔迹进图
        var compose = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static bool TryCompose");
        Assert.DoesNotContain("CanvasInScreenshots", compose);
    }

    /// <summary>
    /// 默认必须是"带上"（从没表过态不等于"想让画布从截图里消失"），且<b>只有一个编辑入口、
    /// 截图那边只读盘不缓存</b>（缓存一份就会出现"改了设置截图照旧带上"这种半生效状态）。
    /// </summary>
    [Fact]
    public void CanvasInScreenshotsDefaultsToOn_AndIsReadFreshFromTheOneEditor()
    {
        var store = SourceGate.ReadRepoFile(Store);
        Assert.Contains(
            "public bool LoadCanvasInScreenshots() => Load() is not { } d || d.CanvasInScreenshots != false;", store);
        Assert.Contains("d.CanvasInScreenshots = include;", store);
        var xaml = SourceGate.ReadRepoFile("src/StarMark.UI/Views/SettingsPage.xaml");
        Assert.Equal(1, SourceGate.Count(xaml, "ViewModel.CanvasInScreenshots, Mode=TwoWay"));
        var shot = SourceGate.ReadRepoFile(Screenshot);
        Assert.Contains("SettingsStore)) as SettingsStore)?.LoadCanvasInScreenshots() ?? true", shot);
        Assert.Equal(1, SourceGate.Count(shot, "LoadCanvasInScreenshots"));
    }

    /// <summary>
    /// 合成核不许把"每条笔迹算一次"的东西放回像素循环里（那正是几百毫秒的来源）。
    /// 这条扫的是形状而非性能数字：性能数字没法在 CI 上守，形状走样是守得住的。
    /// </summary>
    [Fact]
    public void KernelKeepsLoopInvariantWorkOutOfThePixelLoop()
    {
        var compositor = SourceGate.ReadRepoFile(Compositor);
        var disc = SourceGate.MethodBody(compositor, "private static void PaintDisc(");
        Assert.DoesNotContain("stroke.EffectiveColorBgra", disc);       // 每条笔迹读一次就够
        Assert.DoesNotContain("Math.Round", disc);                      // 预乘像素在 Brush 里算好了
        Assert.DoesNotContain("CanvasWidths.RadiusFor", disc);
        Assert.Contains("d2 >= brush.SkipFrom", disc);                  // 平方距离判据（内部大片不开平方）
        Assert.Contains("destinationAlpha >= brush.MaxAlpha", disc);     // 取大规则下"不可能更浓"的早退
        Assert.Contains("MathF.Sqrt", disc);                            // 只允许出现在边缘斜坡那一圈
        Assert.Equal(1, SourceGate.Count(disc, "MathF.Sqrt"));
    }

    [Fact]
    public void MonitorBoundsArePhysicalPixels()
    {
        // 画布按物理像素铺每块屏；ListMonitors 若哪天改成返回 DIP，缓冲尺寸与位置会一起错
        var interop = SourceGate.ReadRepoFile("src/StarMark.UI/Helpers/WindowInterop.cs");
        var list = SourceGate.MethodBody(interop, "public static IReadOnlyList<(string Device, RectInt32 Bounds, double Scale)> ListMonitors");
        Assert.Contains("mi.RcMonitor.Left", list);
        Assert.Contains("GetMonitorScale(hmon)", list);
        Assert.True(list.IndexOf("RcMonitor") < list.IndexOf("GetMonitorScale"),
            "矩形取整屏（含任务栏），缩放另算：两者都从同一个 hMonitor 来");
    }

    [Fact]
    public void CanvasIsAPureWin32LayeredWindow_NotAWinUiWindow()
    {
        var code = SourceGate.ReadRepoFile(Layer);
        // WinUI 3 的 Window 没有"整窗透明 + 只显示笔迹像素"的合成语义；这条链一旦改用 XAML 窗就会画不上
        Assert.Contains("WS_EX_LAYERED", code);
        Assert.Contains("UpdateLayeredWindowIndirect", code);
        Assert.DoesNotContain("Microsoft.UI.Xaml", code);
        Assert.Contains("AlphaFormat = 1", SourceGate.MethodBody(code, "private unsafe void Push"));
        // BLENDFUNCTION 的 AlphaFormat=AC_SRC_ALPHA，否则预乘缓冲会被当成不透明交出去
        Assert.Equal(1, SourceGate.Count(code, "AC_SRC_ALPHA"));
    }

    [Fact]
    public void PresentationIsDirtyRectOnly_AndEmptyDirtyIsARealNoOp()
    {
        var code = SourceGate.ReadRepoFile(Layer);
        var present = SourceGate.MethodBody(code, "public void Present(IntRect dirty)");
        Assert.Contains("if (clipped.IsEmpty) return;", present);
        // 整帧那条路只允许<b>建窗</b>与<b>系统要求重绘</b>两处走（4K 整帧 = 33MB，§16.7 明令禁止每帧这么干）。
        // 数的是调用点（带分号），把方法定义本身也算进去的话，改个方法名就会让这条闸门静默失真。
        Assert.Equal(2, SourceGate.Count(code, "PresentAll();"));
        Assert.Contains("prcDirty", code);
    }

    [Fact]
    public void PressMustCaptureTheMouse_SoTheStrokeCannotGetStuckDown()
    {
        var code = SourceGate.ReadRepoFile(Layer);
        var proc = SourceGate.MethodBody(code, "private static IntPtr HandleMessage");
        var down = proc.IndexOf("WM_LBUTTONDOWN", StringComparison.Ordinal);
        var up = proc.IndexOf("WM_LBUTTONUP", StringComparison.Ordinal);
        Assert.True(down >= 0 && up > down);
        Assert.Contains("SetCapture(hWnd)", proc);
        Assert.Contains("ReleaseCapture()", proc);
        // 不抓到鼠标：笔拖出屏幕边缘就收不到抬起，笔停在"还在画"，用户只能重开画布
        Assert.True(proc.IndexOf("SetCapture(hWnd)", StringComparison.Ordinal)
            < proc.IndexOf("PointerPressed?.Invoke", StringComparison.Ordinal));
    }

    [Fact]
    public void PaintIsValidatesTheUpdateRegion_AndTheProcDelegateIsRooted()
    {
        var code = SourceGate.ReadRepoFile(Layer);
        var proc = SourceGate.MethodBody(code, "private static IntPtr HandleMessage");
        Assert.Contains("BeginPaint", proc);
        Assert.Contains("EndPaint", proc);          // 不验证区域＝系统按帧重发 WM_PAINT＝界面卡死
        Assert.Contains("private static NativeMethods.WndProcDelegate? _sharedProc;", code);
        Assert.Contains("_sharedProc ??= HandleMessage;", code);   // 被 GC 收走的症状是"消息忽然全没了"
    }

    [Fact]
    public void ResolutionChangeRebuildsTheLayers_InsteadOfDrawingOnAWrongSizedBuffer()
    {
        var code = SourceGate.ReadRepoFile(Layer);
        Assert.Contains("WM_DISPLAYCHANGE", SourceGate.MethodBody(code, "private static IntPtr HandleMessage"));
        var service = SourceGate.ReadRepoFile(Service);
        // 重建走 RebuildHost（宿主拆了再建），会话态原样带过去——那不是用户重新进入画布，
        // 所以"下一次进来还是穿透态"那条默认不该在这里生效。
        var rebuild = SourceGate.MethodBody(service, "private static void RebuildHost()");
        Assert.Contains("CloseBoardHost();", rebuild);
        Assert.Contains("ApplyStage(AnnotationHub.Stage)", rebuild);
        Assert.Contains("RebuildHost();", service);
    }

    // ────────── 出口：穿透之后必须还能找回来 ──────────

    [Fact]
    public void ToolbarItselfNeverGoesClickThrough()
    {
        var code = SourceGate.ReadRepoFile(Toolbar);
        var xaml = SourceGate.ReadRepoFile(ToolbarXaml);
        // §16.6 的第一条防线：穿透态下画布收不到任何鼠标，工具条是唯一还能用鼠标点到的地方。
        // 所以工具条<b>自己去改扩展样式</b>这条路必须堵死——它只发指令，不改自己的可点性。
        Assert.DoesNotContain("WS_EX_TRANSPARENT", code);
        Assert.DoesNotContain("GWL_EXSTYLE", code);
        Assert.DoesNotContain("TRANSPARENT", xaml);
        // 那颗「穿透」按钮本身要能再把鼠标交回来（不然只有热键/托盘两条键盘式出口）
        Assert.Contains("CanvasService.SetClickThrough(!CanvasService.IsClickThrough)", code);
    }

    [Fact]
    public void ThereAreFourWaysOutOfCanvasMode()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var toolbar = SourceGate.ReadRepoFile(Toolbar);
        var app = SourceGate.ReadRepoFile(App);
        var main = SourceGate.ReadRepoFile(MainWindow);
        Assert.Contains("VirtualKey.Escape", toolbar);                       // ① Esc
        Assert.Contains("CanvasService.Stop();", toolbar);                   // ② 工具条 ✕
        Assert.Contains("HotkeyActions.CanvasToggle", app);                  // ③ 全局热键
        Assert.Contains("TrayCanvas", main);                                 // ④ 托盘
        Assert.Contains("public static void Stop()", service);
        // 穿透态下热键与托盘之外还有一条鼠标能走的路：工具条那颗「穿透」再点一次
        Assert.Contains("SetClickThrough(!CanvasService.IsClickThrough)", toolbar);
    }

    [Fact]
    public void CanvasActionIsADocumentedHotkeyWithARealDefault()
    {
        Assert.Contains(HotkeyActions.CanvasToggle, HotkeyActions.All());
        Assert.Equal("屏幕画布", HotkeyActions.CategoryOf(HotkeyActions.CanvasToggle));
        Assert.Contains("屏幕画布", HotkeyActions.CategoryOrder);
        Assert.NotEqual(HotkeyActions.CanvasToggle, HotkeyActions.DisplayName(HotkeyActions.CanvasToggle));
        var gesture = Assert.Contains(HotkeyActions.CanvasToggle, HotkeyBindings.Defaults());
        Assert.False(gesture.IsEmpty);
        Assert.Equal(0x44u, gesture.VirtualKey);                             // Ctrl+Alt+D（§16.5 的默认键）
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat,
            gesture.Modifiers);
    }

    // ────────── 渲染与状态的单一出处 ──────────

    [Fact]
    public void PersistentAndOverlayLiveInSeparateBuffers()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var flush = SourceGate.MethodBody(service, "private static void Flush(Screen screen)");
        // 淡出与光晕都要求"能回到这块地方原来画了什么"：合成到一处就回不去
        Assert.Contains("CopyRect(screen.Persistent, screen.Window.Pixels", flush);
        Assert.Contains("PaintClipped(screen.Window.Pixels", flush);
        Assert.True(flush.IndexOf("CopyRect", StringComparison.Ordinal)
            < flush.IndexOf("Trail.Segments", StringComparison.Ordinal),
            "必须先铺持久层再叠荧光段：反了的话每帧都会把上一层擦掉");
    }

    [Fact]
    public void ColoursComeFromTheOnePalette_AndAreGeneratedNotHandWritten()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var toolbar = SourceGate.ReadRepoFile(Toolbar);
        var xaml = SourceGate.ReadRepoFile(ToolbarXaml);
        Assert.Contains("Annotation.Palette", service);                     // 与截图标注同一张色表
        Assert.Contains("CanvasService.Palette.Count", toolbar);            // 格子按表生成
        Assert.DoesNotContain("<Button", SourceGate.Between(xaml, "x:Name=\"ColourPalette\"", "</Border>"));
    }

    [Fact]
    public void SnapshotUsesTheInkNotTheScreenBuffer_WhichExcludesTheCursorHalo()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var compose = SourceGate.MethodBody(service, "private static bool TryCompose(out byte[] pixels");
        Assert.Contains("screen.Persistent", compose);
        Assert.Contains("OverlayOntoFrame", compose);
        Assert.DoesNotContain("Window.Pixels", compose);   // 那块缓冲里带着光晕，快照不该有一团红
        // 正在拖的图形／还开着的折线只活在预览槽里：不叠它就出现"板上看得见一条，贴出来的图没有"
        Assert.Contains("if (screen.Trail.Preview is { } preview)", compose);
    }

    [Fact]
    public void ChangingToolOrWidthCommitsTheOpenStrokeFirst()
    {
        var service = SourceGate.ReadRepoFile(Service);
        // 不先收笔的话，症状是"画着画着笔自己变粗/换了颜色还接到上一条上"
        Assert.Contains("CommitOpenStroke();", SourceGate.MethodBody(service, "public static void SelectTool(CanvasTool tool)"));
        Assert.Contains("CommitOpenStroke();", SourceGate.MethodBody(service, "public static void SelectWidth(int step)"));
        // 换态（穿透↔绘制、截图进出）必然改样式位，收笔与改样式位的顺序不能反：
        // 先改样式，那一笔的"抬起"就永远收不到（穿透之后鼠标归了下层应用）。
        var apply = SourceGate.MethodBody(service, "public static void ApplyStage(AnnotationStage stage)");
        Assert.True(apply.IndexOf("CommitOpenStroke();", StringComparison.Ordinal)
                    < apply.IndexOf("SetClickThrough(", StringComparison.Ordinal),
            "ApplyStage 必须先把open的那一笔收掉，再改每一块的穿透位");
        Assert.Contains("CommitOpenStroke();", apply);
    }

    [Fact]
    public void DragIsThrottledButReleaseAlwaysFlushes()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var moved = SourceGate.MethodBody(service, "private static void OnMoved(Screen screen, CanvasPointer pointer)");
        var released = SourceGate.MethodBody(service, "private static void OnReleased(Screen screen, CanvasPointer pointer)");
        var finish = SourceGate.MethodBody(service, "private static void FinishPress(Screen? screen, PixelPoint? at = null)");
        Assert.Contains("DragThrottleMs", moved);
        // 收口集中在 FinishPress：轮询抢来的那一按（荧光笔 / Ctrl+Alt 圈画）与真实按下走同一条尾。
        // 抬手那一点只有折线用得上（它就是刚拖出来的那个顶点），所以它是参数而不是又一次 GetCursorPos——
        // 后者会在多屏/缩放下取到与笔迹不同坐标系的数（踩坑 #55 那一族）
        Assert.Contains("FinishPress(screen, pointer.At);", released);
        Assert.Contains("Flush(screen);", finish);
        // 松手必须立刻定形（增量提交只保证"看着跟手"，最终形态由全量重算保证）
        Assert.Contains("Recomposite(screen);", finish);
    }

    [Fact]
    public void EveryLayerIsPerMonitor_InPhysicalPixels()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var start = SourceGate.MethodBody(service, "public static bool OpenBoardHost()");
        Assert.Contains("foreach (var monitor in monitors)", start);
        Assert.Contains("monitor.Bounds.Width", start);                     // 物理像素，不做 DIP 换算
        Assert.Contains("catch (Exception ex)", start);                     // 某一屏建不起来不牵连别的屏
        Assert.Contains("每一屏的透明层都没能建起来", start);                // 一块都没成时要回报
        // 一块都没成 ⇒ 返回 false，Hub 会把会话态退回原处（不许留下"状态说有板子、屏幕上一块都没有"）
        Assert.Contains("return false;", start);
        var apply = SourceGate.MethodBody(SourceGate.ReadRepoFile(Hub), "private static bool Apply(AnnotationStage from, AnnotationStage to)");
        Assert.Contains("if (!CanvasService.OpenBoardHost()) return false;", apply);
    }

    // ────────── 批次 WD-1：三态状态机照 §16.5 落地 ──────────

    /// <summary>
    /// <b>进入画布的第一态是穿透态</b>（§16.5.2 的"③穿透态（默认）"）。
    /// 默认拦下全屏鼠标的症状不是"画不上"，而是"按了热键之后 PPT 翻不了、桌面点不动、电脑像死了"；
    /// 而且退出时必须把它复位——否则这一次演示里进了绘制态，下次按热键还是"整台机器被扣住"。
    /// </summary>
    [Fact]
    public void CanvasOpensInThePassThroughState_AndAlwaysReopensThatWay()
    {
        // 批次 S1 之后这一位不再由宿主存：CanvasService 里不许再有 _clickThrough 字段
        // （分岔的来源就是多存一份），默认那一臂在 Core 的转移表里，接线层只递事件。
        var service = SourceGate.ReadRepoFile(Service);
        Assert.DoesNotContain("_clickThrough", service);
        Assert.Contains("AnnotationHub.Raise(SessionEvent.ToggleBoard)", service);
        var close = SourceGate.MethodBody(service, "public static void CloseBoardHost()");
        Assert.Contains("_press = Press.None;", close);     // 手上那条"临时抢来的按"不能留到下一次进入
        Assert.DoesNotContain("_clickThrough", close);
        // 默认态与"下一次进来还是这一态"由同一张表保证（AnnotationSessionTests 逐臂钉）
        Assert.Equal(AnnotationStage.BoardPenetrating,
            AnnotationSessions.Move(AnnotationStage.Idle, SessionEvent.ToggleBoard, null));
    }

    /// <summary>
    /// 选工具＝顺带决定拦不拦鼠标，<b>而"再点当前那支笔"要能把鼠标交回去</b>（与截图/贴图同一交互语言：
    /// 再点已选中的工具＝取消选择）。把两支笔塞进同一个模式开关是 §16.5.1 点名的冲突来源。
    /// <para>
    /// <b>批次 WF-1 的教训就钉在这里</b>：原先这条闸门钉的是 <c>SetClickThrough(tool != Highlighter)</c>——
    /// 一个方向写反的表达式（<c>SetClickThrough(true)</c> 是"加 WS_EX_TRANSPARENT"＝穿透开）。
    /// 它编译得过、也过得了"看起来在测这件事"的断言，真机症状却是"点画笔永远画不上、状态一直说自己
    /// 是穿透"，同时"点荧光笔反倒把整块屏的鼠标吃掉"。所以接线层只准钉<b>那条纯函数的调用</b>，
    /// 反向写法直接钉成禁止，方向本身交给 <c>CanvasInkTests</c> 的三臂单测管。
    /// </para>
    /// </summary>
    [Fact]
    public void ToolChoiceDecidesTheState_AndRepeatingItHandsTheMouseBack()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var select = SourceGate.MethodBody(service, "public static void SelectTool(CanvasTool tool)");
        // 方向不在接线处现写：这里只把 Core 给的"这一支笔该提交哪个事件"递出去
        Assert.Contains("AnnotationHub.Raise(AnnotationSessions.ToolSelectEvent(tool));", select);
        Assert.DoesNotContain("SetClickThrough(", select);
        Assert.DoesNotContain("tool != CanvasTool.Highlighter", select);
        Assert.DoesNotContain("tool == CanvasTool.Highlighter", select);
        var toggle = SourceGate.MethodBody(service, "public static void ToggleTool(CanvasTool tool)");
        Assert.Contains("AnnotationHub.Raise(SessionEvent.GivePointerBack)", toggle);
        Assert.Contains("else SelectTool(tool);", toggle);
        // 事件→态那一臂也在 Core：荧光笔那一支永远回到穿透，画笔那一支永远进绘制
        Assert.Equal(SessionEvent.PickSpotlightPen, AnnotationSessions.ToolSelectEvent(CanvasTool.Highlighter));
        Assert.Equal(SessionEvent.PickPersistentPen, AnnotationSessions.ToolSelectEvent(CanvasTool.Pen));
        // 三支笔各一颗按钮；图形那整排共用一颗处理器（按 Tag 分流，见 BuildShapeButtons）。
        // 直接绑 SelectTool 就没有"再点取消"了，所以这条链上只许出现 ToggleTool。
        var toolbar = SourceGate.ReadRepoFile(Toolbar);
        Assert.Equal(CanvasTools.Brushes.Length + 1, SourceGate.Count(toolbar, "CanvasService.ToggleTool("));
        Assert.DoesNotContain("CanvasService.SelectTool(", toolbar);
        // 批次 WM：图形那几颗<b>整排由模型生成</b>，XAML 里一颗都不写（写死就是名字的第二份出处）。
        // 漏一种图形的症状是"模型里有、条上点不到"，只有跑起来才看得见，所以在这里钉住生成与高亮两处都按表走。
        var build = SourceGate.MethodBody(toolbar, "private void BuildShapeButtons()");
        Assert.Contains("foreach (var tool in CanvasTools.Shapes)", build);
        Assert.Contains("Content = ShapeIcon(tool)", build);                 // 图标，不是汉字
        Assert.Contains("Tag = tool", build);
        Assert.Contains("Click += Shape_Click", build);
        Assert.Contains("foreach (var (tool, button) in _shapeButtons)",
            SourceGate.MethodBody(toolbar, "private void Refresh()"));       // 亮哪一颗也按表算
        Assert.DoesNotContain("RectButton", SourceGate.ReadRepoFile(ToolbarXaml));
        // 图标是画出来的图元，不是字体字形（缺字就是一个方块，而这条窗上没有第二个地方能看出是哪颗）
        Assert.Contains("private static Canvas Icon(params UIElement[] parts)", toolbar);
        // 每种图形都要有自己的图元与一句说明：图标上没有字，说明是它唯一的解释
        foreach (var shape in CanvasTools.Shapes)
        {
            Assert.Contains($"CanvasTool.{shape} =>",
                SourceGate.MethodBody(toolbar, "private static UIElement ShapeIcon("));
            Assert.Contains($"CanvasTool.{shape} =>",
                SourceGate.MethodBody(toolbar, "private static string ShapeHint("));
        }
        // 直线与折线是这条排上最容易撞车的一对：折线的图标必须带顶点记号，否则两颗看起来是同一件事
        Assert.Contains("Dot(5.6, 4.1)", SourceGate.MethodBody(toolbar, "private static UIElement ShapeIcon("));
        // 穿透态下选了图形必须说一句"这一按仍归下层应用"——不然就是"拖了半天什么都没画，以为软件坏了"
        var status = SourceGate.MethodBody(toolbar, "private string StatusText()");
        Assert.Contains("if (tool.IsShape())", status);
        // 折线是唯一"跨按还开着"的：绘制态那行要顺带说怎么收口（不然用户试出来的那一下是退出画布）
        Assert.Contains("tool == CanvasTool.PolyLine", status);
        // Esc 是两级的（先收口折线，再退出），而 ✕ 那颗仍旧一步退出
        var esc = SourceGate.MethodBody(toolbar, "private void Root_KeyDown");
        Assert.Contains("CanvasService.Escape();", esc);
        Assert.DoesNotContain("CanvasService.Stop();", esc);
    }

    /// <summary>
    /// 批次 WK：图形这一按走的是<b>"临时层预览 → 松手定形"</b>这条链，不提前落进持久层。
    /// <para>
    /// 为什么值得钉：预览若直接 <c>Ink.Begin</c> 进持久层，"拖到一半松开在原地"就留下一条撤不掉的笔迹，
    /// 而"拖过头再拉回来"会留下一个错形的框；反过来若松手时<b>重算一遍几何</b>，就会出现
    /// "拖的时候是圆的、松手变有角"。两条都在这里钉住：落笔只 <c>SetPreview</c>，定形只 <c>DropPreview + Commit</c>，
    /// 而两处用的是<b>同一个</b> <c>ShapeStroke(...)</c>。
    /// </para>
    /// <para>还钉住"预览必须被画出来"（Flush 里那一句）与"换工具/换粗细时必须收掉挂着的预览"。</para>
    /// </summary>
    [Fact]
    public void ShapesPreviewInTheEphemeralLayerAndCommitTheSameGeometry()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var begin = SourceGate.MethodBody(service, "private static void BeginPress(");
        Assert.Contains("else if (kind == Press.Shape)", begin);
        Assert.Contains("screen.Dirty.Add(screen.Trail.SetPreview(ShapeStroke(at, at)));", begin);
        // 整份编排里 <c>Ink.Begin</c> 只许出现一次（画笔/橡皮那条分支）：图形若在按下时就进持久层，
        // "拖到一半松开"会留下一条撤不掉的笔迹，而它本来什么都没画成
        Assert.Equal(1, SourceGate.Count(service, "Ink.Begin("));

        var moved = SourceGate.MethodBody(service, "private static void OnMoved(");
        Assert.Contains("ShapeStroke(_shapeFrom, pointer.At)", moved);

        // 批次 WM：颜色与粗细<b>不作参数</b>，由 ShapeStroke 自己按当前设置取。
        // 做成参数编译得过、也过得了"看起来在测它"的断言，真机上却是"预览一个色、落下另一个色"
        // （批次 WF 那条口径的第三种形态：含义只在一处时，接线处现写就是必然出错）。
        var shapeStroke = SourceGate.MethodBody(service, "private static CanvasStroke ShapeStroke(PixelPoint from, PixelPoint to)");
        Assert.Contains("Palette[_colorIndex].Bgra", shapeStroke);
        Assert.DoesNotContain("int colour)", shapeStroke);

        var finish = SourceGate.MethodBody(service, "private static void FinishPress(");
        Assert.Contains("var preview = screen.Trail.Preview;", finish);
        Assert.Contains("screen.Dirty.Add(screen.Trail.DropPreview());", finish);
        Assert.Contains("screen.Ink.Commit(preview);", finish);
        Assert.Contains("Recomposite(screen);", finish);

        // 一条几何算式两处用：预览与定形不可能长成两个样（定义那一处之外，只许 BeginPress 与 OnMoved 各一次）
        Assert.Equal(3, SourceGate.Count(service, "ShapeStroke("));
        Assert.Contains("CanvasShapes.Outline(_tool, from, to, width)", service);

        // 预览必须被叠进提交缓冲，否则"拖的时候什么都看不见"
        var flush = SourceGate.MethodBody(service, "private static void Flush(Screen screen)");
        Assert.Contains("if (screen.Trail.Preview is { } preview)", flush);
        Assert.Contains("CanvasCompositor.PaintClipped(screen.Window.Pixels, width, height, preview, rect);", flush);

        // 换工具/换粗细/开穿透之前先收掉挂着的预览：否则它会一直挂在屏幕上，且下一按接到新工具
        Assert.Contains("if (_press is Press.Ephemeral or Press.QuickPen or Press.Shape or Press.PolyLine)",
            SourceGate.MethodBody(service, "private static void CommitOpenStroke()"));
    }

    /// <summary>
    /// 批次 WM：<b>折线是唯一跨按的工具</b>——一次拖拽定一段，抬手只是把终点定成顶点，折线还开着。
    /// 这条链上四处必须同时成立，少任何一处的症状都不一样：
    /// ① 预览整条走临时层那<b>一个槽</b>（不是每段一条），且颜色与粗细在<b>起勾那一刻</b>定死
    ///    ——否则中途换设置会画出"半条老颜色半条新颜色"；
    /// ② 收口时<b>整条作为一条笔迹</b>进持久层（撤销一格＝整条折线，而不是它的一小段）；
    /// ③ 收口入口要齐：Esc（两级）、换工具、换粗细、开穿透走 <c>CommitOpenStroke</c>，
    ///    清屏与退出另走"丢掉不提交"——少一个就是"这条折线再也关不掉"；
    /// ④ 抬手那一点要<b>判重</b>：原地按一下不算顶点，否则收口时留下一颗说不清的孤点。
    /// </summary>
    [Fact]
    public void PolyLineSpansPresses_AndClosesIntoOneStroke()
    {
        var service = SourceGate.ReadRepoFile(Service);
        // 按下分流：折线绝不能落进 Press.Shape（那会"这一按就是一条"，勾不出第二段的）
        Assert.Contains("CanvasTool.PolyLine => Press.PolyLine,",
            SourceGate.MethodBody(service, "private static void OnPressed(Screen screen, CanvasPointer pointer)"));

        var begin = SourceGate.MethodBody(service, "private static void BeginPress(");
        Assert.Contains("else if (kind == Press.PolyLine)", begin);
        // 已经在勾时不能再添顶点：那一个点上一段的抬手已经定过了
        Assert.Contains("_polyPoints ??= new List<PixelPoint> { at };", begin);
        Assert.Contains("if (_polyScreen is null)", begin);
        Assert.Contains("_polyColour = colour;", begin);                   // 起勾那一刻定死
        Assert.Contains("_polyWidth = WidthFor(CanvasTool.PolyLine);", begin);
        Assert.Contains("ShowPolyPreview(at);", begin);
        // 折线不跨屏（顶点表整份是一块屏自己的物理像素）：换屏先收口，抬手也只在自己那块屏上定顶点
        Assert.Contains("if (_polyScreen is not null && _polyScreen != screen) FinishOpenPolyLine();", begin);

        // 没按住的时候橡皮筋也要跟着手走，否则"下一个顶点落在哪"全无预告
        var moved = SourceGate.MethodBody(service, "private static void OnMoved(");
        Assert.Contains("else if (_polyPoints is not null && screen == _polyScreen)", moved);
        Assert.Contains("ShowPolyPreview(pointer.At);", moved);

        // 抬手＝定顶点，不收口：这里既不能 Commit 也不能清 _polyPoints
        var finish = SourceGate.MethodBody(service, "private static void FinishPress(");
        Assert.Contains("else if (_press == Press.PolyLine)", finish);
        Assert.Contains("AddVertex(at);", finish);
        Assert.Contains("ShowPolyPreview(null);", finish);
        var addVertex = SourceGate.MethodBody(service, "private static void AddVertex(PixelPoint? at)");
        Assert.Contains("if (points.Count > 0 && points[^1].Equals(p)) return;", addVertex);   // 原地按一下

        // 收口：整条一条笔迹，且收完必须忘掉状态（否则下一按接到这一条上）
        var close = SourceGate.MethodBody(service, "private static void FinishOpenPolyLine()");
        Assert.Contains("if (points.Count >= 2)", close);
        Assert.Contains("screen.Ink.Commit(CanvasStroke.FromPoints(CanvasTool.PolyLine, _polyColour, _polyWidth, points));", close);
        Assert.Contains("Recomposite(screen);", close);
        Assert.Contains("screen.Dirty.Add(screen.Trail.DropPreview());", close);
        Assert.Contains("_polyPoints = null;", close);
        Assert.Contains("_polyScreen = null;", close);
        // 定形只有这一条链（Shape 那条走预览换归属），多一处 Commit 就多一处"半条折线被当成整条"
        Assert.Equal(2, SourceGate.Count(service, "Ink.Commit("));

        // 收口入口齐：换工具/换粗细/开穿透都要先收；清屏与退出则是"丢掉不提交"
        var commit = SourceGate.MethodBody(service, "private static void CommitOpenStroke()");
        Assert.True(commit.IndexOf("FinishOpenPolyLine();", StringComparison.Ordinal) > 0,
            "换工具时没收掉挂着的折线");
        Assert.Contains("CancelOpenPolyLine();", SourceGate.MethodBody(service, "public static void ClearAll()"));
        Assert.Contains("_polyPoints = null;", SourceGate.MethodBody(service, "public static void CloseBoardHost()"));
        var cancel = SourceGate.MethodBody(service, "private static void CancelOpenPolyLine()");
        Assert.DoesNotContain("Ink.Commit", cancel);
        Assert.Contains("screen.Dirty.Add(screen.Trail.DropPreview());", cancel);

        // 两级 Esc：先收口这一条，没有手上一半的东西才退出画布（顺序反了＝误按一次把整块板子连笔迹弄没）
        // 批次 S1：这一张路由表在 Core（EscapeRouter），Hub 只照它执行，宿主的 Escape() 只是转发。
        var hub = SourceGate.MethodBody(SourceGate.ReadRepoFile(Hub), "public static void EscapeBoard()");
        Assert.Contains("EscapeRouter.Resolve(Stage, CanvasService.HasWorkInProgress)", hub);
        Assert.True(hub.IndexOf("EscapeStep.CloseWorkInProgress", StringComparison.Ordinal)
                    < hub.IndexOf("EscapeStep.ExitBoard", StringComparison.Ordinal), "Esc 必须先收口再退出");
        var closeWork = SourceGate.MethodBody(service, "public static void CloseWorkInProgress()");
        Assert.Contains("FinishOpenPolyLine();", closeWork);
        Assert.Equal(EscapeStep.CloseWorkInProgress, EscapeRouter.Resolve(AnnotationStage.BoardDrawing, true));
        Assert.Equal(EscapeStep.ExitBoard, EscapeRouter.Resolve(AnnotationStage.BoardDrawing, false));
        Assert.Contains("public static void Escape() => AnnotationHub.EscapeBoard();", service);

        // 勾到一半时"撤销"退的是最后一个顶点，不是板上那条旧笔迹（撤错对象比多按一次难受得多）
        var undo = SourceGate.MethodBody(service, "public static void Undo()");
        Assert.Contains("if (_polyPoints is { } open)", undo);
        Assert.Contains("open.RemoveAt(open.Count - 1);", undo);
        Assert.True(undo.IndexOf("CancelOpenPolyLine();", StringComparison.Ordinal)
                    < undo.IndexOf("foreach (var screen in Screens)", StringComparison.Ordinal),
            "折线还开着时不能直接去撤持久层");

        // 橡皮筋那段几何只有一处出处：预览与收口都用 Core 那一份顶点表
        Assert.Contains("CanvasShapes.PolyLinePreview(points, rubber)",
            SourceGate.MethodBody(service, "private static void ShowPolyPreview(PixelPoint? rubber)"));
    }

    /// <summary>
    /// 穿透态下"按住即画"与"Ctrl+Alt 圈画"<b>只能靠轮询按键状态</b>实现：那一次 WM_LBUTTONDOWN
    /// 已经发给下层应用了，本窗根本收不到。少了这条链，§16.5.2 的"零模式切换摩擦"就是空的
    /// （真机症状："穿透功能形同虚设／始终都是穿透，点什么都画不上"）。
    /// 摘穿透必须自己补 SetCapture，抬起必须还回去——不还＝一次圈画之后整台机器的鼠标归我们扣着。
    /// </summary>
    [Fact]
    public void PassThroughDrawingIsDiscoveredByPolling_AndTheMouseIsGivenBack()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var poll = SourceGate.MethodBody(service, "private static void PollPress(int cursorX, int cursorY)");
        Assert.Contains("LayeredCanvasWindow.LeftButtonDown", poll);
        Assert.Contains("LayeredCanvasWindow.CtrlAltDown", poll);
        // 会话闸门（C2）：读态只在 Board·穿透态跑，判据在 Core 那张表里
        Assert.Contains("AnnotationHub.Stage != AnnotationStage.BoardPenetrating || !down", poll);
        Assert.Contains("s.Window.SetClickThrough(false);", poll);
        Assert.Contains("screen.Window.Capture();", poll);
        Assert.Contains("AnnotationHub.QuickPressInFlight = true;", poll);
        // 顺序：先摘穿透 → 再抓鼠标 → 才落笔
        Assert.True(poll.IndexOf("SetClickThrough(false)") < poll.IndexOf(".Capture();"), "没摘穿透就抓不到这一次");
        Assert.True(poll.IndexOf(".Capture();") < poll.IndexOf("BeginPress("), "抓取要在落笔之前补上");
        // 短按（抬起发生在两帧之间）也要收尾，否则笔停在"还在画"
        Assert.Contains("FinishPress(_pressScreen);", poll);
        Assert.Contains("EndTemporaryPress();", poll);
        var tick = SourceGate.MethodBody(service, "private static void OnFrameTick");
        // 跑不跑这一问由会话态决定，而不是"帧循环反正一直在"
        Assert.True(tick.IndexOf("AnnotationHub.AuditFrame(", StringComparison.Ordinal)
                    < tick.IndexOf("PollPress(cursor.X, cursor.Y);", StringComparison.Ordinal),
            "先对账/验层，再决定这一按要不要抢：别人已占走最上层还去按住即画＝一次按下两家用");
        Assert.Contains("if (AnnotationHub.Stage.QuickDrawReads()) PollPress(cursor.X, cursor.Y);", tick);
        var end = SourceGate.MethodBody(service, "private static void EndTemporaryPress()");
        Assert.Contains("if (AnnotationHub.Stage != AnnotationStage.BoardPenetrating) return;", end);  // 本来就在绘制态就别乱恢复
        Assert.Contains("s.Window.SetClickThrough(true);", end);
        Assert.Contains("AnnotationHub.QuickPressInFlight = false;", end);
        // 豁免本身由 Hub 的那一句读走（对账期间不许把这一按"修"回去——WO 定下的唯一合法不一致）
        Assert.Contains("if (!QuickPressInFlight && LayerDirector.ReconcileStyles(Stage)",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Hub), "public static void AuditFrame"));
    }

    /// <summary>Ctrl+Alt 圈画落的是<b>画笔的持久墨</b>，跟当前选中的那支笔无关（选中荧光笔时也一样）。</summary>
    [Fact]
    public void QuickCircleUsesPenInk_RegardlessOfTheSelectedTool()
    {
        var begin = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service),
            "private static void BeginPress(Screen screen, PixelPoint at, Press kind)");
        Assert.Contains("kind == Press.QuickPen ? CanvasTool.Pen : _tool", begin);
        Assert.DoesNotContain("_press = _tool", begin);
    }

    // ────────── 批次 WO：撤销要退干净＋"状态说的"与"窗口做的"必须同一件事 ──────────

    /// <summary>
    /// <b>重烤持久层必须把"上一次烤过的那一片"一起擦掉。</b>只按剩余笔迹的包围盒算区域的话，
    /// 刚被 <c>Undo</c> 掉那条没被盖到的地方永远没人擦——真机症状就是"撤销只退掉椭圆的一半"，
    /// 而撤销栈里已经没有东西能退第二次。所以这里钉三处：区域从 <c>Screen.Composited</c> 起算、
    /// 烤完把新的那片记回去、以及"不够格留下来的那一笔"要把自己的落点交回去擦。
    /// </summary>
    [Fact]
    public void RecompositeErasesThePreviouslyBakedRegion()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var recompute = SourceGate.MethodBody(service, "private static void Recomposite(Screen screen, IntRect alsoErase = default)");
        Assert.Contains("var toErase = CanvasCompositor.Union(new[] { screen.Composited, alsoErase }, width, height);", recompute);
        Assert.Contains("CanvasCompositor.Rebake(screen.Persistent, width, height, screen.Ink.Strokes,", recompute);
        Assert.Contains("toErase, out var remaining);", recompute);
        Assert.Contains("screen.Composited = remaining;", recompute);
        Assert.Contains("if (!erase.IsEmpty) screen.Dirty.Add(erase);", recompute);
        // 记号必须存在且是"曾经烤过的那一片"，不是"现在还剩的"
        Assert.Contains("public IntRect Composited { get; set; }", service);

        // 橡皮点一下那种"不够格进层"的一笔：落点要单独交回去擦，否则留下一个没有笔迹对应的洞
        var finish = SourceGate.MethodBody(service, "private static void FinishPress(");
        Assert.Contains("var footprint = screen.Ink.Drawing?.Bounds ?? default;", finish);
        Assert.Contains("if (!screen.Ink.End()) Recomposite(screen, footprint);", finish);
        Assert.Contains("else Recomposite(screen);", finish);
    }

    /// <summary>
    /// <b>每帧对一次账：状态说的（工具条那行字）与窗口做的（<c>WS_EX_TRANSPARENT</c> 那一位）必须同一件事。</b>
    /// 真机反馈"有时在未穿透状态下进行穿透后的操作且无法绘制"＝两者分岔：界面读旗标，鼠标归谁读那一位。
    /// 分岔来源不止一条（轮询抢来的那一按没还回去、某一屏没跟上、部分系统改样式半生效），
    /// 所以这里<b>不查原因只兜结果</b>：不一致就按状态改回来并留一行带三个值的日志。
    /// <para>临时摘穿透那一按（按住即画／Ctrl+Alt 圈画）期间必须跳过——那是唯一"合法的不一致"。</para>
    /// </summary>
    [Fact]
    public void ClickThroughStateIsReconciledWithTheWindowStyleEveryFrame()
    {
        // 批次 S1：三处自愈合成一处——画布的帧循环只问 Hub，Hub 只问 LayerDirector。
        var service = SourceGate.ReadRepoFile(Service);
        var tick = SourceGate.MethodBody(service, "private static void OnFrameTick");
        Assert.Contains("AnnotationHub.AuditFrame(cursor.X, cursor.Y);", tick);
        Assert.DoesNotContain("ReassertClickThrough", service);        // 旧那一份不许留在原地再存一处
        Assert.DoesNotContain("YieldIfNotOurLayer(", service);

        var hub = SourceGate.MethodBody(SourceGate.ReadRepoFile(Hub), "public static void AuditFrame");
        Assert.True(hub.IndexOf("ReconcileStyles(Stage)", StringComparison.Ordinal)
                    < hub.IndexOf("Classify(cursorX, cursorY", StringComparison.Ordinal),
            "先对账再验层：状态本身就不实时，验层结论也是错的");

        var audit = SourceGate.MethodBody(SourceGate.ReadRepoFile(Director), "public static int ReconcileStyles");
        Assert.Contains("if (!stage.NeedsFrameAudit()) return 0;", audit);   // 没板子在场时不逐屏读样式位
        Assert.Contains("var expect = !stage.GlassTakesPointer();", audit);  // 期望值来自会话态，不是宿主旗标
        Assert.Contains("entry.ReadStyle() == expect", audit);               // 读的是样式位本身
        Assert.Contains("entry.ApplyStyle?.Invoke(expect);", audit);         // 不一致就按状态改回来
        Assert.Contains("WarnThrottled(\"layer:style\"", audit);             // 别让它自己变成刷屏源
        // 窗口侧必须真的去读那一位（只比两个旗标等于什么都没查）
        Assert.Contains("WS_EX_TRANSPARENT) != 0;",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Layer), "public bool StyleClickThrough"));
    }

    /// <summary>
    /// 自家<b>桌面组件</b>窗压在画布上面时，那一次按下既没被两家用、也没到画布——
    /// 症状同样是"显示绘制中却画不上"（还能顺手点组件）。WD-8 定的"比进程不比句柄"仍然成立，
    /// 这里补的是后半句：<b>要比就拿顶层祖先比</b>（命中窗是 XAML 内容的子窗，与 <c>Hwnd</c> 永不相等），
    /// 且判完要动手（把画布按回 chrome 之下），不是只记一行日志。
    /// </summary>
    [Fact]
    public void OwnWidgetWindowAboveTheCanvasIsFixedByReordering_NotByYielding()
    {
        var hub = SourceGate.ReadRepoFile(Hub);
        var audit = SourceGate.MethodBody(hub, "public static void AuditFrame");
        // 自家进程不再"什么都不做"，但也绝不交出鼠标（那才是 WD-8 修错的地方）：两条判据在 Core 分成两臂
        Assert.Contains("LayerRules.ShouldReorderFor(Stage, ours)", audit);
        Assert.Contains("LayerRules.ShouldYieldPointer(Stage, ours)", audit);
        Assert.Contains("if (LayerDirector.IsKnown(root)) return;", audit);   // 条子/面板/玻璃自己：什么都不做
        Assert.Contains("LayerDirector.EnforceOrder(Stage);", audit);
        Assert.DoesNotContain("Raise(SessionEvent.GivePointerBack);\n        Notice = null", audit);
        Assert.Contains("if (now - _lastLayerFixMs < LayerFixGapMs) return;", audit);
        Assert.Contains("GetClassName(root)", audit);                          // 复发时要能从日志认出是谁
        // 比的是顶层祖先，不是命中句柄（命中窗是 XAML 内容的子窗，与 Hwnd 永不相等）
        Assert.Contains("WindowInterop.GetAncestor(hwnd, WindowInterop.GA_ROOT)",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Director), "public static IntPtr RootOf"));
        Assert.Contains("return root == IntPtr.Zero ? hwnd : root;",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Director), "public static IntPtr RootOf"));
        // 两臂的方向由 Core 的三臂单测钉（AnnotationSessionTests.YieldAndReorderAreDifferentQuestions）
        Assert.True(LayerRules.ShouldReorderFor(AnnotationStage.BoardDrawing, true));
        Assert.False(LayerRules.ShouldYieldPointer(AnnotationStage.BoardDrawing, true));
    }

    /// <summary>工具条与快捷键面板的说明行：内容一改就得叫上那扇窗（截图那条链的同一课，见 CaptureOverlayGateTests）。</summary>
    [Fact]
    public void CanvasToolbarHintReflowsTheToolbarWindow()
    {
        var toolbar = SourceGate.ReadRepoFile(Toolbar);
        foreach (var writer in new[] { "private void ShowHint(string hint)", "private void HideHint()" })
            Assert.Contains("Fit(centerOnScreen: false);", SourceGate.MethodBody(toolbar, writer));
    }

    // ────────── 批次 WD-2：工具条与面板永远压在画布之上 ──────────

    /// <summary>
    /// <b>空白像素不能是 0</b>：分层窗的命中测试跳过 alpha=0，那块地方直接漏给下层窗口，
    /// 于是"刚擦过的地方点不动、画不上"。擦干净有三条路（整块／区域／橡皮到底），加上持久层初值一共四处，
    /// 漏一处就留下一片"画不了的区域"，而且它正好在用户刚刚操作过的地方——最难复现的那种。
    /// <para>批次 WO 补上<b>第五条、也是最前面的一条：那块表面必须真的交给系统一次。</b>
    /// 托管缓冲填成 <c>BlankPixel</c> 还不够——分层窗在第一次 <c>UpdateLayeredWindow</c> 之前，
    /// 系统那侧是全 alpha=0，于是绘制态下光标停在<b>还没画过的地方</b>时
    /// <c>WindowFromPoint</c> 根本不报我们的窗，自校验误判成"别人占了那一层"就把鼠标交了回去
    /// （真机症状："刚点画笔就画不上，而有墨的地方正常"，看起来像"有时能画有时不能"）。
    /// 所以建窗那一刻必须 <c>PresentAll()</c> 一次，且要在后备位图建好之后。</para>
    /// </summary>
    [Fact]
    public void BlankIsTheOneHitTestablePixel_EveryClearPathUsesIt()
    {
        var layer = SourceGate.ReadRepoFile(Layer);
        Assert.Contains("public const uint BlankPixel = 0x01000000u;", layer);
        // 建窗就整块交一次（顺序：后备位图 → 显形 → 定样式 → 交表面）
        var ctor = SourceGate.MethodBody(layer, "public LayeredCanvasWindow(IntRect boundsPhys)");
        Assert.True(ctor.IndexOf("CreateArgbDib", StringComparison.Ordinal)
                    < ctor.IndexOf("PresentAll();", StringComparison.Ordinal),
            "后备位图还没建好就交表面，等于什么都没交");
        Assert.True(ctor.IndexOf("SetClickThrough(true);", StringComparison.Ordinal)
                    < ctor.IndexOf("PresentAll();", StringComparison.Ordinal),
            "样式还没定就先交，穿透态的命中语义会跟着错");
        Assert.Contains("Array.Fill(Pixels, BlankPixel);", layer);
        var compositor = SourceGate.ReadRepoFile(Compositor);
        Assert.Contains("Array.Fill(buffer, LayeredCanvasWindow.BlankPixel, y * width + r.X, r.Width);", compositor);
        Assert.Contains("buffer[index] = LayeredCanvasWindow.BlankPixel;", compositor);   // 橡皮擦到底
        Assert.Contains("Array.Fill(buffer, LayeredCanvasWindow.BlankPixel);",
            SourceGate.MethodBody(compositor, "public static void Clear(uint[] buffer)"));
        // 快照那条链不能被"空白"的那 1/255 蒙上黑：叠图时按空白那一档跳过
        Assert.Contains("if (alpha <= (int)(LayeredCanvasWindow.BlankPixel >>> 24)) continue;", compositor);
        // 全链路不许再出现"擦成全 0"
        Assert.DoesNotContain("Array.Clear(", compositor);
        Assert.DoesNotContain("Array.Clear(", layer);
        var start = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "public static bool OpenBoardHost()");
        Assert.Contains("Array.Fill(persistent, LayeredCanvasWindow.BlankPixel);", start);
    }

    /// <summary>
    /// 提层不能只提工具条：<b>对已经在 topmost 带里的窗口再传 HWND_TOPMOST 只换带、不在带内重排</b>
    /// （＝什么都没做）。真机症状＝"按过穿透／右键之后工具条一颗按钮都点不动"，而画布现在是整块 alpha=1
    /// 的玻璃，它会把这些点击全吃下去。可靠做法是反过来：把画布显式插到工具条<b>之下</b>。
    /// </summary>
    [Fact]
    public void ChromeEntersTheTopmostBandThenReordersWithinIt_CanvasNeverDemoted()
    {
        // 批次 S1：SetWindowPos 的<b>层序写入只准出现在 LayerDirector 一处</b>，
        // 条子/面板/玻璃都向它登记，不再各自提层（各自提层＝三家小算盘互相不认识，就是 C1）。
        var director = SourceGate.ReadRepoFile(Director);
        var raise = SourceGate.MethodBody(director, "public static void RaiseWithinBand");
        Assert.Contains("WindowInterop.HWND_TOPMOST", raise);       // 先进带
        Assert.Contains("WindowInterop.HWND_TOP,", raise);          // 再带内重排
        Assert.True(raise.IndexOf("HWND_TOPMOST") < raise.IndexOf("WindowInterop.HWND_TOP,"),
            "顺序反了等于没提层");

        var show = SourceGate.MethodBody(director, "public static void ShowAt");
        Assert.Contains("Register(role, hwnd);", show);              // 登记与置顶同一个调用点，新窗不会半秒不在名册里
        Assert.Contains("WindowInterop.SWP_SHOWWINDOW", show);
        // 批次 S1 起条子/遮罩/贴图都从这一处上屏，配方本体在这里钉死：进带 → 带内重排 → 显形，且全程不抢前台
        Assert.Contains("WindowInterop.HWND_TOPMOST", show);
        Assert.True(show.IndexOf("WindowInterop.HWND_TOPMOST") < show.IndexOf("WindowInterop.HWND_TOP,"),
            "只传 TOP 的窗永远留在普通层：任何应用一激活就把遮罩或贴图条盖住");
        Assert.Contains("WindowInterop.SW_SHOWNOACTIVATE", show);
        // 只改几何那两发必须带 NOZORDER：贴图拖动顺带提层＝整叠贴图按拖动先后重新洗牌（批次 MZ 同族）
        foreach (var geometryOnly in new[] { "public static void Relocate", "public static void MoveTo" })
            Assert.Contains("WindowInterop.SWP_NOZORDER", SourceGate.MethodBody(director, geometryOnly));

        // 递给 Win32 当锚点的那一个必须自己 topmost：递错会把玻璃连人带桌拽出带（WD-2 半对造成 WD-7 回归）
        var anchor = SourceGate.MethodBody(director, "public static IntPtr AnchorForBoard");
        Assert.Contains("LayerRules.IsSafeInsertAfter(IsTopmost(candidate), true)", anchor);
        Assert.Contains("!WindowInterop.IsWindow(candidate)", anchor);
        Assert.DoesNotContain("CanvasNative", director);             // 护栏不许退回画布窗自己那一份

        // 条子与面板：都只登记 + 走 Director，不再自己写 HWND_TOPMOST
        var toolbar = SourceGate.ReadRepoFile(Toolbar);
        Assert.DoesNotContain("WindowInterop.SetWindowPos", toolbar);
        Assert.Contains("LayerDirector.Register(SurfaceRole.Strip", toolbar);
        Assert.Contains("LayerDirector.RaiseWithinBand", SourceGate.MethodBody(toolbar, "public void RaiseAboveCanvas()"));
        var under = SourceGate.MethodBody(SourceGate.ReadRepoFile(Panel), "public void PlaceUnder(IntPtr insertAbove)");
        Assert.Contains("LayerDirector.RaiseWithinBand(hwnd);", under);
        Assert.Contains("LayerDirector.InsertBelow(hwnd, insertAbove);", under);

        // 三个时刻定序：工具条出现、每次会话迁移、条子每次刷新自己提一次
        Assert.Contains("LayerDirector.EnforceOrder(AnnotationHub.Stage);",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static void ShowToolbar()"));
        Assert.Contains("LayerDirector.EnforceOrder(to);",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Hub), "private static bool Apply"));
        Assert.Contains("RaiseAboveCanvas();", SourceGate.MethodBody(toolbar, "private void Refresh()"));
    }

    /// <summary>光晕只在<b>选了荧光笔</b>时跟随光标（用户裁决）：拿着画笔却满屏跟着一团颜色是噪音。</summary>
    [Fact]
    public void CursorHaloFollowsOnlyWhileTheHighlighterIsSelected()
    {
        var tick = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static void OnFrameTick");
        Assert.Contains("if (_tool == CanvasTool.Highlighter && screen.Trail.CursorHaloEnabled", tick);
    }

    /// <summary>
    /// 绘制态必须<b>每帧逐像素核实"光标这一层是谁"</b>（发起人点名的"没有实时监测光标位于哪一层"）：
    /// 画布声称"绘制中"却不再是顶层时，一次按下会被两家用——下面的应用当成框选/选文字，我们还在抢着画，
    /// 这就是真机反馈的"画布和应用交互冲突"；穿透态与"手上正有一笔"时都不判。
    /// <para>
    /// <b>但"是不是我们"只认进程，绝不认句柄相等</b>（批次 WD-8 的真机教训，症状＝"永远退不出穿透态、
    /// 点画笔没反应"）：<c>WindowFromPoint</c> 返回的是那一点上<b>最深</b>的 HWND，而 WinUI 3 的
    /// 工具条内容住在它自己的子窗里、tooltip 与浮层还是另开的顶层窗——拿它跟 <c>Hwnd</c>/
    /// <c>GetHwnd()</c> 比相等必然不等，于是光标一停在条子上就被判成"别人盖住了画布"，每帧退回穿透态。
    /// </para>
    /// </summary>
    [Fact]
    public void DrawModeProbesTheLayerUnderTheCursorEveryFrameAndYields()
    {
        var hub = SourceGate.ReadRepoFile(Hub);
        var audit = SourceGate.MethodBody(hub, "public static void AuditFrame");
        // 只在绘制态判（穿透态"不是我们"是设计本意）；手上正有一笔也跳过（那一笔已归画布画完）
        Assert.Contains("if (CanvasService.PressInFlight || !Stage.GlassTakesPointer()) return;", audit);
        Assert.Contains("hit == _yieldedTo", audit);                           // 同一个窗只说一次，不每帧刷
        Assert.Contains("Raise(SessionEvent.GivePointerBack);", audit);        // 让位走会话事件，不再直接改样式位
        Assert.Contains("另一个程序的窗口", audit);                              // 让位必须给得出原因
        Assert.Contains("Notice = \"已自动交回鼠标", audit);
        Assert.DoesNotContain("ReassertLayering", hub);

        // 逐像素问、按进程判——这两件事都在 LayerDirector 一处
        var classify = SourceGate.MethodBody(SourceGate.ReadRepoFile(Director), "public static LayerOwnership Classify");
        Assert.Contains("LayeredCanvasWindow.WindowAt(cursorX, cursorY)", classify);   // 不猜前台窗口
        Assert.Contains("GetWindowThreadProcessId(hit, out var pid) == 0", classify);   // 取不到 pid 不下判断
        Assert.Contains("pid == (uint)Environment.ProcessId", classify);                // 只认进程，绝不认句柄相等
        Assert.DoesNotContain(".Hwnd ==", classify);
        Assert.DoesNotContain(".Handle", classify);
        Assert.Contains("public static IntPtr WindowAt(int screenX, int screenY)",
            SourceGate.ReadRepoFile(Layer));
        Assert.Contains("public static extern IntPtr WindowFromPoint(NativeMethods.POINT pt);",
            SourceGate.ReadRepoFile(Native));
        // 让位的话要说给用户听，且用户自己动手后翻篇
        Assert.Contains("if (AnnotationHub.Notice is { } note) return note;",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Toolbar), "private string StatusText()"));
        Assert.Contains("Notice = null;", SourceGate.MethodBody(hub, "private static bool Apply"));
    }

    // ────────── 批次 WD-3：画布动作的全局热键（发起人点名"九个全要，带修饰键"）──────────

    /// <summary>
    /// 十条画布动作各有各的默认键，<b>且必须带 Ctrl+Alt</b>。
    /// <para>
    /// 裸键在穿透态下必须留给下层应用（用户要选文本、要翻页），画布一旦吃下裸键就成了"开着画布
    /// 别的软件都不能用"；而九条不带修饰键的字母键撞键概率极高。键位本身按"这件事叫什么"取字母
    /// （T=Through、P=Pen、H=Highlighter、R=eRaser、U=Undo、C=Clear、S=Save、K=复制、G=贴图），
    /// 猜得出比记得住更重要——工具条上那颗「⌨」也随时能把这张表调出来。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryCanvasActionHasItsOwnCtrlAltKey_AndTheLettersMatchTheWords()
    {
        var defaults = HotkeyBindings.Defaults();
        var expected = new Dictionary<string, uint>
        {
            [HotkeyActions.CanvasToggle] = 0x44,           // D
            [HotkeyActions.CanvasClickThrough] = 0x54,     // T
            [HotkeyActions.CanvasPen] = 0x50,              // P
            [HotkeyActions.CanvasHighlighter] = 0x48,      // H
            [HotkeyActions.CanvasEraser] = 0x52,           // R
            [HotkeyActions.CanvasUndo] = 0x55,             // U
            [HotkeyActions.CanvasClear] = 0x43,            // C
            [HotkeyActions.CanvasSave] = 0x53,             // S
            [HotkeyActions.CanvasCopy] = 0x4B,             // K（C 已被清屏占了）
            [HotkeyActions.CanvasPin] = 0x47,              // G
        };
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal),
            HotkeyActions.Canvas.OrderBy(k => k, StringComparer.Ordinal));   // 目录＝这份表，不漏一条
        var seen = new HashSet<uint>();
        foreach (var (action, vk) in expected)
        {
            Assert.True(HotkeyActions.IsCanvasAction(action), $"「{action}」不在 canvas. 前缀里——分类会漏掉它");
            Assert.Equal("屏幕画布", HotkeyActions.CategoryOf(action));
            var gesture = Assert.Contains(action, defaults);
            Assert.Equal(vk, gesture.VirtualKey);
            Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, gesture.Modifiers);
            Assert.True(seen.Add(vk), $"两条画布动作抢了同一个键（0x{vk:X}）：后注册的那条永远按不出来");
        }
    }

    /// <summary>
    /// <b>动作表里每一颗都得有人接</b>：绑定了却没注册 handler 的症状是"设置页里明晃晃写着 Ctrl+Alt+R，
    /// 按下去什么也没有"——这条链上最容易漏、也最难自查的一处（注册本身成功，所以没有任何报错）。
    /// </summary>
    [Fact]
    public void EveryActionConstantIsWiredToAHandler_InApp()
    {
        var app = SourceGate.ReadRepoFile(App);
        var constants = typeof(HotkeyActions)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => f.Name)
            .ToList();
        Assert.True(constants.Count >= 18, $"扫到的动作常量只有 {constants.Count} 条，反射口径是不是错了");
        foreach (var name in constants)
            Assert.Contains($"RegisterHandler(HotkeyActions.{name}", app);
    }

    /// <summary>
    /// 画布没开着时按"画布内的动作"，<b>三支笔直接把画布开起来</b>（按画笔的人是要画画，不是要先按另一个键），
    /// 其余六条给一句看得见的原因。"按了没反应"与"功能坏了"在用户眼里是同一件事。
    /// </summary>
    [Fact]
    public void CanvasHotkeysEitherOpenTheBoardOrSayWhyTheyDidNot()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var tool = SourceGate.MethodBody(service, "public static void HotkeyTool(CanvasTool tool)");
        Assert.Contains("ToggleTool(tool);", tool);
        Assert.DoesNotContain("Start();", tool);                  // "没开就先 Start()"那条平行逻辑不许回来
        Assert.DoesNotContain("Report(", tool);                   // 开不起来时 Hub/宿主已经报过，不再补一条
        // 板子没开着时按画笔＝直接进绘制态（少一步＝用户要的效果），判据在 Core 的转移表
        Assert.Equal(AnnotationStage.BoardDrawing,
            AnnotationSessions.Move(AnnotationStage.Idle, AnnotationSessions.ToolSelectEvent(CanvasTool.Pen), null));
        var require = SourceGate.MethodBody(service, "private static void RequireRunning(string what, Action run)");
        Assert.Contains("Report(\"画布没开着\"", require);
        Assert.Contains("BindingText(HotkeyActions.CanvasToggle)", require);   // 键位取自真实绑定，不写死
        Assert.Equal(6, SourceGate.Count(service, "RequireRunning(\""));       // 六条：穿透/撤销/清屏/存图/复制/贴图
    }

    // ────────── 批次 WD-4：「⌨」快捷键面板 ──────────

    /// <summary>
    /// 面板是"看一眼"的东西，它最不能犯的错是<b>自己变成第二份事实</b>：
    /// 行必须从 <see cref="HotkeyActions.Canvas"/> 与当前真实绑定生成。手写一份键位表的结局一定是
    /// "面板写着 Ctrl+Alt+R，按了没反应"——正是这块面板要防的那件事。
    /// 另外它<b>自己不穿透</b>（同工具条），并且关掉时要被编排忘掉。
    /// </summary>
    [Fact]
    public void HotkeyPanelIsGenerated_NotHandWritten_AndNeverGoesClickThrough()
    {
        var panel = SourceGate.ReadRepoFile(Panel);
        var xaml = SourceGate.ReadRepoFile(PanelXaml);
        Assert.Contains("foreach (var action in HotkeyActions.Canvas)", panel);
        Assert.Contains("HotkeyActions.DisplayName(action)", panel);
        Assert.Contains("CanvasService.BindingText(action)", panel);
        Assert.DoesNotContain("Ctrl+Alt", panel);                   // 代码里一处键面字面量都不许留
        Assert.DoesNotContain("WS_EX_TRANSPARENT", panel);
        Assert.DoesNotContain("GWL_EXSTYLE", panel);
        Assert.DoesNotContain("TRANSPARENT", xaml);
        // WC-4 那条同一课：窗没亮就量＝量到没套模板的空壳尺寸，面板会被截成一条
        var show = SourceGate.MethodBody(panel, "public void ShowAt(IntRect anchorBelow, IntRect screen, double scale)");
        Assert.True(show.IndexOf("AppWindow.Show()") < show.IndexOf("Root.UpdateLayout()"), "先亮窗");
        Assert.True(show.IndexOf("Root.UpdateLayout()") < show.IndexOf("Fit(anchorBelow)"), "排一遍之后才量");
        Assert.Contains("Math.Clamp(y,", SourceGate.MethodBody(panel, "private void Fit(IntRect anchorBelow)"));
        // 关掉的三条路：面板上那颗 ✕、⌨ 再点一次、退出画布。前两条走这里，最后一条要求"忘得掉"
        Assert.Contains("Closed += (_, _) => CanvasService.PanelClosed(this);", panel);
        Assert.Contains("CanvasService.HideHotkeyPanel();", SourceGate.MethodBody(panel, "private void Close_Click"));
    }

    /// <summary>
    /// 定序是一条链而不是一次提层：<b>工具条 → 面板 → 画布 → 下层应用</b>。
    /// 面板若排在工具条之上，"再点一次 ⌨ 收起"那颗就会被自己的面板挡住（面板正好长在它下面）。
    /// </summary>
    [Fact]
    public void ChromeOrderIsOneChain_ToolbarThenPanelThenCanvas()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var show = SourceGate.MethodBody(service, "public static void ToggleHotkeyPanel()");
        // 定序还是一条链：面板先归位到工具条之下，画布再插到（名册里最后那条 chrome＝）面板之下
        Assert.True(show.IndexOf("_panel.PlaceUnder", StringComparison.Ordinal)
                    < show.IndexOf("LayerDirector.EnforceOrder(AnnotationHub.Stage)", StringComparison.Ordinal),
            "面板先归位，画布再插到面板之下");
        var place = SourceGate.MethodBody(SourceGate.ReadRepoFile(Director), "public static IntPtr AnchorForBoard");
        Assert.Contains("if (Entries[i].Role == above) candidate = Entries[i].Hwnd;", place);   // 取最后注册那一个
        var close = SourceGate.MethodBody(service, "private static void CloseToolbar()");
        Assert.True(close.IndexOf("HideHotkeyPanel();") < close.IndexOf("_toolbar is null"),
            "收面板要早于那句提前返回：工具条已经没了时，面板更不能留在屏幕上");
        Assert.Contains("StateChanged?.Invoke();",
            SourceGate.MethodBody(service, "public static void PanelClosed(CanvasHotkeyPanelWindow panel)"));
    }

    /// <summary>面板上的键位文本取自真实绑定；读不到时说"未绑定"，绝不回一个看起来对的假键位。</summary>
    [Fact]
    public void BindingTextFallsBackToUnbound_NeverToABogusKey()
    {
        var text = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "public static string BindingText(string action)");
        Assert.Contains("gesture is { IsEmpty: false } bound", text);
        Assert.Contains("HotkeyDisplay.Display(bound)", text);
        Assert.Equal(3, SourceGate.Count(text, "\"未绑定\""));      // 没设置 / 没绑定 / 抛异常：三条兜底路都不能编出键位
    }

    // ────────── 批次 WD-5：总开关（设置 → 拓展功能）──────────

    /// <summary>
    /// 关掉一个功能要<b>同时</b>做到三件事，缺一条就是"关不掉"：
    /// ① 功能自己不启动（闸门在 <c>Start()</c>，所有入口都汇到那里，所以只有一处）；
    /// ② <b>九条快捷键不注册</b>——不替一个关掉的功能继续占着系统的组合键；
    /// ③ 托盘整条消失（发起人裁决："关掉就别留入口"）。
    /// <para>
    /// 但 <c>canvas.toggle</c> 那条<b>必须继续注册</b>：按自己习惯那颗键的人要听见一句"要先在设置里打开"，
    /// 而不是从此多了一条哑键——三条注册入口（启动 / 托盘与开关联动 / 设置页保存与重试）都得走
    /// <c>GetRegisterableHotkeyBindings</c>，漏一条就等于"关掉之后快捷键还占着"。
    /// </para>
    /// </summary>
    [Fact]
    public void CanvasSwitchGatesStartRegistrationAndTray_AllThreeRegistrationSitesUseTheFilteredTable()
    {
        var store = SourceGate.ReadRepoFile(Store);
        Assert.Contains("public bool LoadCanvasEnabled() => Load() is not { } d || d.CanvasEnabled != false;", store);
        var reg = SourceGate.MethodBody(store,
            "public IReadOnlyDictionary<string, HotkeyGesture> GetRegisterableHotkeyBindings()");
        // 批次 S1：投影是 f(总开关, 会话态)，判据在 Core 的 HotkeyGate，接线层只问它要结论
        Assert.Contains("var canvasEnabled = LoadCanvasEnabled();", reg);
        Assert.Contains("AnnotationHub.IsSheetActive", reg);
        Assert.Contains("HotkeyGate.ShouldRegister(action, canvasEnabled, stage)", reg);
        Assert.DoesNotContain("action != HotkeyActions.CanvasToggle", reg);   // 豁免名单不在接线层自己列
        // 会话迁移点上必须真的重投影一次，否则摘/还键这件事只存在于文档里
        Assert.Contains("ReapplyHotkeys();",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Hub), "private static bool Apply"));
        Assert.Contains("hotkey.ApplyBindings(", SourceGate.MethodBody(SourceGate.ReadRepoFile(Hub), "public static void ReapplyHotkeys"));
        Assert.Contains("settings.GetRegisterableHotkeyBindings()",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Hub), "public static void ReapplyHotkeys"));

        Assert.Contains("settings.GetRegisterableHotkeyBindings()", SourceGate.ReadRepoFile(App));
        var main = SourceGate.ReadRepoFile(MainWindow);
        Assert.Contains("_settings.GetRegisterableHotkeyBindings()", main);
        Assert.DoesNotContain("_settings.GetHotkeyBindings()", main);
        var page = SourceGate.ReadRepoFile(SettingsPageCode);
        Assert.Equal(2, SourceGate.Count(page, "GetRegisterableHotkeyBindings()"));   // 保存 + 重试注册

        var service = SourceGate.ReadRepoFile(Service);
        var start = SourceGate.MethodBody(service, "public static bool OpenBoardHost()");
        Assert.Contains("if (!EnabledBySetting)", start);
        Assert.Contains("屏幕画布已关闭", start);
        Assert.Contains("list.Where(item => item.Tag != TrayCanvas)", main);
        // 关掉时正在画：立刻收玻璃（"我已经关了屏幕上还压着一层吃鼠标的东西"是最糟的收尾）
        var vm = SourceGate.ReadRepoFile("src/StarMark.UI/ViewModels/SettingsPageViewModel.cs");
        var changed = SourceGate.MethodBody(vm, "partial void OnCanvasEnabledChanged(bool value)");
        Assert.Contains("if (!value) StarMark.UI.Services.CanvasService.Stop();", changed);
        Assert.Contains("App.MainWindow?.ApplyTraySettings();", changed);   // 注册表当场跟着改，不重启、不再点保存
    }

    /// <summary>
    /// 设置页那一览与画布面板<b>同源</b>：都从 <see cref="HotkeyActions.Canvas"/> + 当前绑定生成。
    /// 两处各写一份键位，改天一定分岔（分岔的样子就是"照着说明按，没反应"）。
    /// </summary>
    [Fact]
    public void SettingsSheetAndCanvasPanelReadTheSameTable()
    {
        var vm = SourceGate.ReadRepoFile("src/StarMark.UI/ViewModels/SettingsPageViewModel.cs");
        Assert.Contains("HotkeyActions.Canvas.Select", vm);
        Assert.Contains("HotkeyDisplay.Display(bound)", vm);
        Assert.DoesNotContain("Ctrl+Alt+R", vm);                      // 一份字面键位都不许有
        // 改完键要重算，否则那一览会一直显示旧键位
        var page = SourceGate.ReadRepoFile(SettingsPageCode);
        Assert.Equal(2, SourceGate.Count(page, "ViewModel.RefreshCanvasHotkeySheet();"));
    }
}
