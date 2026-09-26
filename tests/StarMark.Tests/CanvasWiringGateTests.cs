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
    private const string Compositor = "src/StarMark.Core/Canvas/CanvasCompositor.cs";
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
    /// </summary>
    [Fact]
    public void ToolChoiceDecidesTheState_AndRepeatingItHandsTheMouseBack()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var select = SourceGate.MethodBody(service, "public static void SelectTool(CanvasTool tool)");
        Assert.Contains("SetClickThrough(tool != CanvasTool.Highlighter);", select);
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
    public void ChromeStaysAboveTheCanvas_ByPushingTheCanvasDown_NotByReTopmostingItself()
    {
        var native = SourceGate.ReadRepoFile(Native);
        Assert.Contains("public static readonly IntPtr HWND_TOP = IntPtr.Zero;", native);
        var toolbar = SourceGate.ReadRepoFile(Toolbar);
        var raise = SourceGate.MethodBody(toolbar, "public void RaiseAboveCanvas()");
        Assert.Contains("WindowInterop.HWND_TOP", raise);
        Assert.DoesNotContain("HWND_TOPMOST", raise);
        // 整份工具条都不该再出现 HWND_TOPMOST：那三处（提层／定位／拖动）要的都是"带内重排"
        Assert.DoesNotContain("WindowInterop.HWND_TOPMOST", toolbar);

        var service = SourceGate.ReadRepoFile(Service);
        var place = SourceGate.MethodBody(service, "private static void PlaceLayersBelowChrome()");
        Assert.Contains("_toolbar?.Hwnd ?? IntPtr.Zero", place);
        Assert.Contains("screen.Window.PlaceBelow(chrome);", place);
        var placeBody = SourceGate.MethodBody(SourceGate.ReadRepoFile(Layer), "public void PlaceBelow(IntPtr insertAfter)");
        Assert.Contains("CanvasNative.SWP_NOACTIVATE", placeBody);
        Assert.DoesNotContain("HWND_TOPMOST", placeBody);
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
}
