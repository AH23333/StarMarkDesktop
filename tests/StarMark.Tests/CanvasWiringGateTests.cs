#nullable enable
using System;
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
