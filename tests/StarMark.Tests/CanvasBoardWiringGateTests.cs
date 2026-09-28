#nullable enable
using StarMark.Core.Hotkeys;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 白板底（批次 S4-①）的<b>接线</b>闸门：底落在哪一步、谁改得动它、被拦住的出口给不给得出原因。
/// <para>
/// 这三件事都是"编译得过、旧测试一条不红、真机上却是整块板子看不见 / 点不动"的形状，而像素级部分只能靠真机眼睛看，
/// 所以这里钉<b>结构</b>：分臂的位置、写入点的个数、方向判据的出处。算术本身在
/// <see cref="CanvasBoardBackdropTests"/>。
/// </para>
/// </summary>
public sealed class CanvasBoardWiringGateTests
{
    private const string Layer = "src/StarMark.Integrations/Canvas/LayeredCanvasWindow.cs";
    private const string Blend = "src/StarMark.Integrations/Canvas/BackdropBlend.cs";
    private const string Compositor = "src/StarMark.Core/Canvas/CanvasCompositor.cs";
    private const string Ink = "src/StarMark.Core/Canvas/CanvasInk.cs";
    private const string Service = "src/StarMark.UI/Services/CanvasService.cs";
    private const string Hub = "src/StarMark.UI/Services/AnnotationHub.cs";
    private const string Toolbar = "src/StarMark.UI/Views/CanvasToolbarWindow.xaml.cs";
    private const string ToolbarXaml = "src/StarMark.UI/Views/CanvasToolbarWindow.xaml";
    private const string App = "src/StarMark.UI/App.xaml.cs";

    /// <summary>
    /// 叠底只发生在<b>提交那一步</b>，而且分臂在循环外：托管缓冲（<c>Pixels</c>）里永远是"预乘 + 透明"的墨，
    /// 取大混合、橡皮减 alpha、增量＝全量那套语义一行没动。
    /// </summary>
    [Fact]
    public void TheBackdropIsComposedAtSubmitTime_WithTheBranchOutsideTheLoop()
    {
        var push = SourceGate.MethodBody(SourceGate.ReadRepoFile(Layer), "private unsafe void Push");
        Assert.Equal(1, SourceGate.Count(push, "if (BackdropArgb"));             // 每像素一次判断＝每帧几百万次
        Assert.Equal(1, SourceGate.Count(push, "BackdropBlend.OverOpaque"));
        // 透明底那条快路（逐行整块搬）必须原样还在：白板不许把常态的提交拖下水位
        Assert.Contains("var bytes = source.Width * 4;", push);
        Assert.Contains("Buffer.MemoryCopy(from + offset, to + offset, bytes, bytes);", push);
        // 渲放管线里不许出现"背景态"这个自变量（分臂要加在每帧几百万次的热循环上，批次 WG 的纪律）
        Assert.DoesNotContain("Backdrop", SourceGate.ReadRepoFile(Compositor));
        Assert.DoesNotContain("Backdrop", SourceGate.ReadRepoFile(Ink));
    }

    /// <summary>换底要整块重交（旧底不留残行），并且<b>不写托管缓冲</b>：写了就是把"底"存进模型那一份真值里。</summary>
    [Fact]
    public void SetBackdropRepaintsTheWholeSurface_WithoutTouchingTheManagedBuffer()
    {
        var layer = SourceGate.ReadRepoFile(Layer);
        var set = SourceGate.MethodBody(layer, "public void SetBackdrop(uint argb)");
        Assert.Contains("if (BackdropArgb == argb) return;", set);               // 同值不重交：换底是离散动作，不是每帧
        Assert.Contains("BackdropArgb = argb;", set);
        Assert.Contains("PresentAll();", set);
        Assert.DoesNotContain("Pixels[", set);
        Assert.Contains("public uint BackdropArgb { get; private set; }", layer);
    }

    /// <summary>
    /// 背景态的<b>写入点只有会话裁判那一家</b>：宿主只是执行者。
    /// <para>§3.2 那句话对这个开关同样成立——多一个书写点的下场就是"按钮说白板开着、屏幕却还是桌面"，
    /// 而这一处分岔只有眼睛看得见，编译器与运行时都不报错。</para>
    /// </summary>
    [Fact]
    public void OnlyTheHubWritesTheBackdrop_TheHostOnlyAppliesIt()
    {
        var hub = SourceGate.ReadRepoFile(Hub);
        Assert.Equal(2, SourceGate.Count(hub, "Backdrop = "));                   // ① ToggleBackdrop ② 板子关掉时复位
        Assert.Contains("public static CanvasBackdrop Backdrop { get; private set; }", hub);
        var service = SourceGate.ReadRepoFile(Service);
        Assert.DoesNotContain("Backdrop = ", service);                           // 宿主一份都不许存
        Assert.Equal(1, SourceGate.Count(SourceGate.MethodBody(service, "public static void ApplyBackdrop"),
            "screen.Window.SetBackdrop(argb);"));
        // 方向判据不写在接线层：宿主只把事件递给 Hub（批次 WF-1 的口径）
        Assert.Contains("=> AnnotationHub.Raise(SessionEvent.ToggleBackdrop);",
            SourceGate.MethodBody(service, "public static void ToggleBackdrop()"));
        Assert.DoesNotContain("CanvasBackdropMath.Next", service);
        Assert.DoesNotContain("Backdrop =", SourceGate.ReadRepoFile(Toolbar));   // 工具条是投影，只读不写
    }

    /// <summary>
    /// 闸门与"上白板必收鼠标"两臂都要有人调：<b>只调一半</b>的样子是"在穿透态点白板 → 一整面点不动的白墙"。
    /// 被拦住时还必须发广播——工具条那行字是唯一读者，不发就是"点了没反应"。
    /// </summary>
    [Fact]
    public void TheHubAsksTheCoreRuleOnBothSides_AndAnnouncesTheRefusal()
    {
        var hub = SourceGate.ReadRepoFile(Hub);
        Assert.Contains("AnnotationSessions.IsBlockedByBackdrop(what, Backdrop)", hub);
        Assert.Contains("AnnotationSessions.PointerConsequenceOf(next)", hub);
        Assert.Contains("CanvasBackdropMath.Next(Backdrop)", hub);
        var raise = SourceGate.MethodBody(hub, "public static void Raise(SessionEvent what)");
        Assert.Contains("Notice = AnnotationSessions.ReasonBlockedByBackdrop(what, Backdrop);", raise);
        Assert.Contains("CanvasService.RaiseStateChanged();", raise);
        // 穿透那条旧出口不许自己判背景态（判据只有一份）
        Assert.DoesNotContain("Whiteboard", SourceGate.MethodBody(SourceGate.ReadRepoFile(Service),
            "public static void SetClickThrough(bool on)"));
    }

    /// <summary>换分辨率／拔屏之后的重建要把背景态原样还回来，否则"这块屏变桌面、那块还是白板"。</summary>
    [Fact]
    public void ARebuiltScreenIsSeededWithTheCurrentBackdrop()
    {
        var start = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "public static bool OpenBoardHost()");
        Assert.Contains("window.SetBackdrop(CanvasBackdropMath.ArgbOf(AnnotationHub.Backdrop));", start);
        // 建窗之后立刻交：次序是"后备位图→显形→定样式→交表面"（批次 WO），换底排在建窗与登记之间，不动那条序
        Assert.True(start.IndexOf("new LayeredCanvasWindow(bounds)", System.StringComparison.Ordinal)
            < start.IndexOf("window.SetBackdrop(CanvasBackdropMath.ArgbOf", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// 白板态下的"贴图／存图／复制"必须以<b>那块白底</b>为图底：屏幕上是一张白纸、图里却是别人的桌面＝
    /// 批次 WK 定性的同一类缺口（凡是"屏幕上有"的东西都得进这张图）。
    /// 常态那条"抓干净桌面"的路原样留着，两条底共用同一份叠墨代码。
    /// </summary>
    [Fact]
    public void TheSnapshotUsesTheBoardAsItsBaseWhileTheWhiteboardIsOn()
    {
        var service = SourceGate.ReadRepoFile(Service);
        var compose = SourceGate.MethodBody(service, "private static bool TryCompose");
        Assert.Contains("CanvasBackdropMath.IsOpaque(AnnotationHub.Backdrop)", compose);
        Assert.Contains("BoardFrame(boardWidth, boardHeight, CanvasBackdropMath.WhiteboardArgb)", compose);
        Assert.Equal(1, SourceGate.Count(compose, "CaptureWithoutCanvas()"));     // 抓屏只在那一条非白板臂里
        Assert.Equal(1, SourceGate.Count(compose, "OverlayOntoFrame("));          // 叠墨只有一份，两条底共用
        // 交出去的图不能留透明洞（与 OverlayOntoFrame 那一句同一条纪律）
        Assert.Contains("frame[i + 3] = 255;", SourceGate.MethodBody(service, "private static byte[] BoardFrame"));
    }

    /// <summary>
    /// 那颗按钮与那条热键都只能汇到<b>同一个事件</b>上，而且被拦时状态行要讲得出原因。
    /// <para>XAML 里那颗按钮写成汉字是刻意的：它不属于"图形那排图标"（那排必须按模型生成，见批次 WM）。
    /// 状态行里"要先点「白板」"那句是拦住它的闸门的可见交代——少了它，"点穿透没反应"只剩一种解释＝软件坏了。</para>
    /// </summary>
    [Fact]
    public void TheBoardButtonAndItsHotkeyBothFunnelIntoOneEvent_AndTheBarSaysWhyWhenBlocked()
    {
        var xaml = SourceGate.ReadRepoFile(ToolbarXaml);
        Assert.Contains("x:Name=\"BoardButton\"", xaml);
        Assert.Contains("Click=\"Board_Click\"", xaml);
        var code = SourceGate.ReadRepoFile(Toolbar);
        Assert.Contains("=> CanvasService.ToggleBackdrop();", SourceGate.MethodBody(code, "private void Board_Click"));
        Assert.Contains("Highlight(BoardButton, CanvasService.IsWhiteboard", SourceGate.MethodBody(code, "private void Refresh()"));
        Assert.Contains("CanvasService.IsWhiteboard", SourceGate.MethodBody(code, "private string StatusText()"));
        Assert.Contains("要先点「白板」", code);
        Assert.Contains("RegisterHandler(HotkeyActions.CanvasBoard", SourceGate.ReadRepoFile(App));
        Assert.Contains(HotkeyActions.CanvasBoard, HotkeyActions.Canvas);
    }

    /// <summary>叠底算术只许有一份，而且只有一个调用者（提交那一步）。</summary>
    [Fact]
    public void TheBlendHasExactlyOneDefinitionAndOneCallSite()
    {
        Assert.Contains("public static uint OverOpaque(uint ink, uint board)", SourceGate.ReadRepoFile(Blend));
        Assert.Equal(1, SourceGate.Count(SourceGate.ReadRepoFile(Layer), "BackdropBlend.OverOpaque"));
        foreach (var file in new[] { Service, Compositor, Ink })
            Assert.DoesNotContain("BackdropBlend", SourceGate.ReadRepoFile(file));
    }
}
