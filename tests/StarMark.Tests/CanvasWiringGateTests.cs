#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
        // 整帧那条路只允许首帧与系统要求重绘时走（4K 整帧 = 33MB，§16.7 明令禁止每帧这么干）
        Assert.Equal(2, SourceGate.Count(code, "PresentAll()"));
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
        Assert.Contains("Restart()", service);
        Assert.Contains("Stop();", SourceGate.MethodBody(service, "private static void Restart()"));
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
    }

    [Fact]
    public void ChangingToolOrWidthCommitsTheOpenStrokeFirst()
    {
        var service = SourceGate.ReadRepoFile(Service);
        // 不先收笔的话，症状是"画着画着笔自己变粗/换了颜色还接到上一条上"
        Assert.Contains("CommitOpenStroke();", SourceGate.MethodBody(service, "public static void SelectTool(CanvasTool tool)"));
        Assert.Contains("CommitOpenStroke();", SourceGate.MethodBody(service, "public static void SelectWidth(int step)"));
        Assert.Contains("CommitOpenStroke();", SourceGate.MethodBody(service, "public static void SetClickThrough(bool on)"));
    }

    [Fact]
    public void DragIsThrottledButReleaseAlwaysFlushes()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var moved = SourceGate.MethodBody(service, "private static void OnMoved(Screen screen, CanvasPointer pointer)");
        var released = SourceGate.MethodBody(service, "private static void OnReleased(Screen screen, CanvasPointer pointer)");
        var finish = SourceGate.MethodBody(service, "private static void FinishPress(Screen? screen)");
        Assert.Contains("DragThrottleMs", moved);
        // 收口集中在 FinishPress：轮询抢来的那一按（荧光笔 / Ctrl+Alt 圈画）与真实按下走同一条尾
        Assert.Contains("FinishPress(screen);", released);
        Assert.Contains("Flush(screen);", finish);
        // 松手必须立刻定形（增量提交只保证"看着跟手"，最终形态由全量重算保证）
        Assert.Contains("Recomposite(screen);", finish);
    }

    [Fact]
    public void EveryLayerIsPerMonitor_InPhysicalPixels()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var start = SourceGate.MethodBody(service, "public static void Start()");
        Assert.Contains("foreach (var monitor in monitors)", start);
        Assert.Contains("monitor.Bounds.Width", start);                     // 物理像素，不做 DIP 换算
        Assert.Contains("catch (Exception ex)", start);                     // 某一屏建不起来不牵连别的屏
        Assert.Contains("每一屏的透明层都没能建起来", start);                // 一块都没成时要回报
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
        var service = SourceGate.ReadRepoFile(Service);
        Assert.Contains("private static bool _clickThrough = true;", service);
        var stop = SourceGate.MethodBody(service, "public static void Stop()");
        Assert.Contains("_clickThrough = true;", stop);
        Assert.Contains("_press = Press.None;", stop);     // 手上那条"临时抢来的按"不能留到下一次进入
        // 只有"分辨率变了要重建"这一条路允许把当前态带过去（那不是用户重新进入画布）
        Assert.Contains("_clickThrough = wasClickThrough;",
            SourceGate.MethodBody(service, "private static void Restart()"));
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
        Assert.Contains("SetClickThrough(CanvasModes.IsClickThroughAfter(tool));", select);
        Assert.DoesNotContain("tool != CanvasTool.Highlighter", select);
        var toggle = SourceGate.MethodBody(service, "public static void ToggleTool(CanvasTool tool)");
        Assert.Contains("if (_tool == tool && !_clickThrough) SetClickThrough(true);", toggle);
        Assert.Contains("else SelectTool(tool);", toggle);
        // 工具条上三支笔都走 ToggleTool——直接绑 SelectTool 就没有"再点取消"了
        var toolbar = SourceGate.ReadRepoFile(Toolbar);
        Assert.Equal(3, SourceGate.Count(toolbar, "CanvasService.ToggleTool("));
        Assert.DoesNotContain("CanvasService.SelectTool(", toolbar);
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
        Assert.Contains("if (_press != Press.None || !_clickThrough || !down) return;", poll);
        Assert.Contains("s.Window.SetClickThrough(false);", poll);
        Assert.Contains("screen.Window.Capture();", poll);
        Assert.Contains("_wasTemporary = true;", poll);
        // 顺序：先摘穿透 → 再抓鼠标 → 才落笔
        Assert.True(poll.IndexOf("SetClickThrough(false)") < poll.IndexOf(".Capture();"), "没摘穿透就抓不到这一次");
        Assert.True(poll.IndexOf(".Capture();") < poll.IndexOf("BeginPress("), "抓取要在落笔之前补上");
        // 短按（抬起发生在两帧之间）也要收尾，否则笔停在"还在画"
        Assert.Contains("FinishPress(_pressScreen);", poll);
        Assert.Contains("EndTemporaryPress();", poll);
        var tick = SourceGate.MethodBody(service, "private static void OnFrameTick");
        Assert.Contains("PollPress(cursor.X, cursor.Y);", tick);
        var end = SourceGate.MethodBody(service, "private static void EndTemporaryPress()");
        Assert.Contains("if (!_clickThrough) return;", end);              // 本来就在绘制态就别乱恢复
        Assert.Contains("s.Window.SetClickThrough(true);", end);
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

    // ────────── 批次 WD-2：工具条与面板永远压在画布之上 ──────────

    /// <summary>
    /// <b>空白像素不能是 0</b>：分层窗的命中测试跳过 alpha=0，那块地方直接漏给下层窗口，
    /// 于是"刚擦过的地方点不动、画不上"。擦干净有三条路（整块／区域／橡皮到底），加上持久层初值一共四处，
    /// 漏一处就留下一片"画不了的区域"，而且它正好在用户刚刚操作过的地方——最难复现的那种。
    /// </summary>
    [Fact]
    public void BlankIsTheOneHitTestablePixel_EveryClearPathUsesIt()
    {
        var layer = SourceGate.ReadRepoFile(Layer);
        Assert.Contains("public const uint BlankPixel = 0x01000000u;", layer);
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
        var start = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "public static void Start()");
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
        var native = SourceGate.ReadRepoFile(Native);
        Assert.Contains("public static readonly IntPtr HWND_TOP = IntPtr.Zero;", native);
        var toolbar = SourceGate.ReadRepoFile(Toolbar);
        var raise = SourceGate.MethodBody(toolbar, "public void RaiseAboveCanvas()");
        Assert.Contains("WindowInterop.HWND_TOPMOST", raise);       // 先进带
        Assert.Contains("WindowInterop.HWND_TOP,", raise);           // 再带内重排
        Assert.True(raise.IndexOf("HWND_TOPMOST") < raise.IndexOf("WindowInterop.HWND_TOP,"),
            "顺序反了等于没提层");
        var under = SourceGate.MethodBody(SourceGate.ReadRepoFile(Panel), "public void PlaceUnder(IntPtr insertAbove)");
        Assert.Contains("WindowInterop.HWND_TOPMOST", under);
        Assert.Contains("WindowInterop.SetWindowPos(hwnd, insertAbove", under);

        var service = SourceGate.ReadRepoFile(Service);
        var place = SourceGate.MethodBody(service, "private static void PlaceLayersBelowChrome()");
        Assert.Contains("_toolbar?.Hwnd ?? IntPtr.Zero", place);
        Assert.Contains("screen.Window.PlaceBelow(above);", place);
        // 画布这一侧也得自保：递进来一个非 topmost 的窗就拒绝，绝不跟着掉出 topmost 带
        var placeBody = SourceGate.MethodBody(SourceGate.ReadRepoFile(Layer), "public void PlaceBelow(IntPtr insertAfter)");
        Assert.Contains("CanvasNative.SWP_NOACTIVATE", placeBody);
        Assert.Contains("if ((ex & CanvasNative.WS_EX_TOPMOST) == 0) return;", placeBody);
        // 三个时刻定序：工具条出现、穿透态切换、以及工具条每次刷新自己提一次
        Assert.Contains("PlaceLayersBelowChrome();", SourceGate.MethodBody(service, "public static void SetClickThrough(bool on)"));
        Assert.Contains("PlaceLayersBelowChrome();", SourceGate.MethodBody(service, "private static void ShowToolbar()"));
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
        var service = SourceGate.ReadRepoFile(Service);
        var yield = SourceGate.MethodBody(service, "private static void YieldIfNotOurLayer(int cursorX, int cursorY)");
        Assert.Contains("if (_clickThrough || _press != Press.None || !_running) return;", yield);
        Assert.Contains("LayeredCanvasWindow.WindowAt(cursorX, cursorY)", yield);   // 逐像素问，不猜前台窗口
        Assert.Contains("WindowInterop.GetWindowThreadProcessId(hit, out var pid) == 0", yield);
        Assert.Contains("pid == (uint)Environment.ProcessId) return;", yield);      // 自己人不让位
        Assert.DoesNotContain(".Hwnd", yield);                                      // 命中句柄不参与"是谁"的判断
        Assert.DoesNotContain(".Handle", yield);
        Assert.DoesNotContain("本程序的另一个窗口", yield);                             // 自家窗口不再触发让位
        Assert.Contains("hit == IntPtr.Zero || hit == _yieldedTo", yield);           // 同一个窗只说一次，不每帧刷
        Assert.Contains("SetClickThrough(true);", yield);
        Assert.Contains("另一个程序的窗口", yield);                                    // 让位必须给得出原因
        var tick = SourceGate.MethodBody(service, "private static void OnFrameTick");
        Assert.True(tick.IndexOf("YieldIfNotOurLayer(") < tick.IndexOf("PollPress("),
            "先验层再决定抢不抢：反过来就是应用与画布同时接手同一次按下");
        // 这条探测只在绘制态有意义（穿透态本窗被 WS_EX_TRANSPARENT 跳过，答案必然是别人）
        Assert.Contains("public static IntPtr WindowAt(int screenX, int screenY)",
            SourceGate.ReadRepoFile(Layer));
        Assert.Contains("public static extern IntPtr WindowFromPoint(NativeMethods.POINT pt);",
            SourceGate.ReadRepoFile(Native));
        // 让位的话要说给用户听，且用户自己动手后翻篇
        Assert.Contains("if (CanvasService.Notice is { } note) return note;",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Toolbar), "private string StatusText()"));
        Assert.Contains("Notice = null;", SourceGate.MethodBody(service, "public static void SetClickThrough(bool on)"));
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
        Assert.Contains("if (!_running) Start();", tool);
        Assert.Contains("ToggleTool(tool);", tool);
        Assert.DoesNotContain("Report(", tool);                       // 开不起来时 Start() 已经报过，不再补一条
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
        var place = SourceGate.MethodBody(service, "private static void PlaceLayersBelowChrome()");
        Assert.Contains("_panel.PlaceUnder(chrome);", place);
        Assert.Contains("above = _panel.Hwnd;", place);
        Assert.Contains("screen.Window.PlaceBelow(above);", place);
        Assert.True(place.IndexOf("_panel.PlaceUnder") < place.IndexOf("PlaceBelow(above)"),
            "面板先归位，画布再插到面板之下");
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
        Assert.Contains("if (LoadCanvasEnabled()) return all;", reg);
        Assert.Contains("action != HotkeyActions.CanvasToggle", reg);

        Assert.Contains("settings.GetRegisterableHotkeyBindings()", SourceGate.ReadRepoFile(App));
        var main = SourceGate.ReadRepoFile(MainWindow);
        Assert.Contains("_settings.GetRegisterableHotkeyBindings()", main);
        Assert.DoesNotContain("_settings.GetHotkeyBindings()", main);
        var page = SourceGate.ReadRepoFile(SettingsPageCode);
        Assert.Equal(2, SourceGate.Count(page, "GetRegisterableHotkeyBindings()"));   // 保存 + 重试注册

        var service = SourceGate.ReadRepoFile(Service);
        var start = SourceGate.MethodBody(service, "public static void Start()");
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
