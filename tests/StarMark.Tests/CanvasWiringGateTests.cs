#nullable enable
using System;
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
    private const string Service = "src/StarMark.UI/Services/CanvasService.cs";
    private const string Toolbar = "src/StarMark.UI/Views/CanvasToolbarWindow.xaml.cs";
    private const string ToolbarXaml = "src/StarMark.UI/Views/CanvasToolbarWindow.xaml";
    private const string App = "src/StarMark.UI/App.xaml.cs";
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

    /// <summary>每帧的"叠盖记号"必须从空算起。拿上一帧的当起点＝它只增不减＝脏区最终铺满全屏。</summary>
    [Fact]
    public void OverlayMarkingIsRebuiltEachFrame_InsteadOfAccumulating()
    {
        var tick = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "private static void OnFrameTick");
        Assert.Contains("screen.Dirty.Add(screen.LastOverlay);", tick);      // 上一帧叠过的地方要复原
        Assert.Contains("var overlay = CanvasCompositor.Nothing;", tick);    // 本帧的记号从零开始并
        Assert.DoesNotContain("var overlay = screen.LastOverlay;", tick);
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
        Assert.Contains("Paint(screen.Window.Pixels", flush);
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
        Assert.Contains("DragThrottleMs", moved);
        Assert.Contains("Flush(screen);", released);
        // 松手必须立刻定形（增量提交只保证"看着跟手"，最终形态由全量重算保证）
        Assert.Contains("Recomposite(screen);", released);
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
}
