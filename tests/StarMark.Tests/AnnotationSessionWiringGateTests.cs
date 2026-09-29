#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 S1 的<b>收官闸门</b>：架构方案 §13 那些"只有真机才看得见"的用例，能在源码层面钉死的部分逐条钉住。
/// <para>分工：状态机与规则表本体在 <see cref="AnnotationSessionTests"/>（纯函数、拿得进用例号），
/// 画布那半边的接线在 <see cref="CanvasWiringGateTests"/>，截图与贴图那条链的窗口约束在
/// <see cref="CaptureOverlayGateTests"/>/<see cref="PinEditTests"/>。这里只补 S1 新立的那几条
/// <b>"同一件事全机只有一处书写"</b>——这类约束的失效方式不是编译不过，而是半年后有人在别处
/// 顺手多写一行，两个真值开始分岔（C1/C4/C8 的成因）。</para>
/// </summary>
public sealed class AnnotationSessionWiringGateTests
{
    private const string Screenshot = "src/StarMark.UI/Services/ScreenshotService.cs";
    private const string Hub = "src/StarMark.UI/Services/AnnotationHub.cs";
    private const string Director = "src/StarMark.UI/Services/LayerDirector.cs";
    private const string Service = "src/StarMark.UI/Services/CanvasService.cs";
    private const string Toolbar = "src/StarMark.UI/Views/CanvasToolbarWindow.xaml.cs";
    private const string Panel = "src/StarMark.UI/Views/CanvasHotkeyPanelWindow.xaml.cs";
    private const string Bar = "src/StarMark.UI/Views/CaptureBarWindow.cs";
    private const string Overlay = "src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs";
    private const string Layer = "src/StarMark.Integrations/Canvas/LayeredCanvasWindow.cs";

    /// <summary>
    /// §13 用例 1：<b>一次截图只有一份会话真值</b>，而且冻帧要在那份真值改口之前抓。
    /// <para>「在不在截图」以前这里记一份、遮罩窗里再记一份：两边一分岔就是"界面说在截、屏幕上没有遮罩"
    /// 或反过来。顺序这条更阴——迁移排在抓帧之前，画布玻璃已经被收掉了，「截图带画布」那条设置就永远
    /// 截不到笔迹；排在建窗之后，用户会看见一帧"玻璃还亮着、遮罩已经上来"的重影。</para>
    /// </summary>
    [Fact]
    public void AScreenshotIsOneStageAndTheFrameIsGrabbedBeforeIt()
    {
        var service = SourceGate.ReadRepoFile(Screenshot);
        Assert.DoesNotContain("_busy", service);                                  // 第二份旗标＝分岔的源头
        Assert.Contains("public static bool IsCapturing => AnnotationHub.IsSheetActive;", service);
        var start = SourceGate.MethodBody(service, "public static void Start(CaptureMode");
        // 重入问的是同一个真值，而且<b>与总开关合在同一道闸里判</b>（批次 RP）：
        // 接线层自己 `if (IsSheetActive)` 再加一句 `if (!enabled)` 是两个条件各判一次——
        // 将来加第三个条件时漏掉哪一个都不会报错，只会漏掉一条出口。
        Assert.Contains("CaptureGate.BlockOf(EnabledBySetting, AnnotationHub.IsSheetActive)", start);
        Assert.DoesNotContain("if (AnnotationHub.IsSheetActive)", start);
        Assert.True(start.IndexOf("Grab();", StringComparison.Ordinal)
                    < start.IndexOf("AnnotationHub.Raise(SessionEvent.BeginSheet);", StringComparison.Ordinal),
            "会话迁移既要在抓帧之后、又要在建窗之前——排错了就是上面那两个症状之一");
        var close = SourceGate.MethodBody(service, "private static void CloseSession()");
        // 少这一句＝建窗半路抛异常时（Session 可能是空的，但态已经是 Sheet）画布那九条键再也回不来
        Assert.Contains("AnnotationHub.Raise(SessionEvent.EndSheet);", close);
        Assert.DoesNotContain("return;", close);
    }

    /// <summary>
    /// §13 用例 1 后半：<b>冻帧期间不许留下任何悬浮幽灵层</b>。玻璃与画布条的显隐只按状态表那一行翻，
    /// 接线层再判一次"这是不是 Sheet"＝下一批加一种状态时必然漏判一处（WF-1 的同一课）。
    /// </summary>
    [Fact]
    public void TheBoardHidesItselfByReadingTheStageRow_NotByReDerivingIt()
    {
        var apply = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Service),
            "public static void ApplyStage(AnnotationStage stage)");
        Assert.Contains("var visible = stage.GlassVisible();", apply);
        Assert.Contains("screen.Window.SetVisible(visible);", apply);
        Assert.Contains("_toolbar.SetStripVisible(stage.BoardStripVisible());", apply);
        Assert.DoesNotContain("AnnotationStage.Sheet", apply);
        Assert.DoesNotContain("AnnotationHub.Stage", apply);        // 只吃传进来的那一个态，不回头自己读
    }

    /// <summary>
    /// 方案 §3.5 的完成定义：<b>整条标注链只有一处写 Z 序</b>。
    /// 各家自己提层＝三家各自抱着"我以为该谁在上"的小算盘（C1），而多一处"记得叫上定序"就多一处会被忘掉的地方。
    /// <para>分层窗自己建窗那一次置顶（<c>CanvasNative.SetWindowPos</c>，在 Integrations）是"机制"不是"决定"，
    /// 不在这条禁令之内；改样式位后补的那一发带 <c>SWP_NOZORDER</c>，本来也不动层序。</para>
    /// </summary>
    [Fact]
    public void TheAnnotationChainHasExactlyOneZOrderWriter()
    {
        foreach (var file in new[] { Service, Hub, Screenshot, Toolbar, Panel, Bar, Overlay,
                     "src/StarMark.UI/Views/CaptureOverlayWindow.Draw.cs",
                     "src/StarMark.UI/Views/CaptureOverlayWindow.Pin.cs" })
            Assert.DoesNotContain("WindowInterop.SetWindowPos", SourceGate.ReadRepoFile(file));
        Assert.Contains("WindowInterop.SetWindowPos", SourceGate.ReadRepoFile(Director));
    }

    /// <summary>
    /// 会话态除了 <see cref="AnnotationHub.Raise"/> 这一处，<b>没有任何别的书写点</b>。
    /// 这条是 S1 的全部理由：状态分岔（"工具条说绘制中、点下去画不上"）只能靠"只有一个地方能改它"根除。
    /// </summary>
    [Fact]
    public void OnlyTheHubMovesTheStage()
    {
        var hub = SourceGate.ReadRepoFile(Hub);
        Assert.DoesNotContain("Stage = ", SourceGate.WithoutMethod(hub, "public static void Raise(SessionEvent what)"));
        Assert.DoesNotContain("Stage = ", SourceGate.ReadRepoPartials(Service));
        Assert.DoesNotContain("Stage = ", SourceGate.ReadRepoFile(Screenshot));
    }

    /// <summary>
    /// §13 用例 6：截图进行中按画布那颗开关<b>要给得出原因</b>（另外九条这时是故意不注册的）。
    /// <para>顺序是关键：<c>Move</c> 对"Sheet 期间 ToggleBoard"给的就是 Sheet（会话不被快捷键打断），
    /// 于是 <c>to == from</c> 那条短路先跑的话，回执永远打不出来——变成最坏的一种：哑键。</para>
    /// <para>这时画布工具条按 §5 是收起的，所以回执走提示卡/主窗提示条那条 <c>Report</c>，
    /// 不是往条子状态行上写字。</para>
    /// </summary>
    [Fact]
    public void TheToggleKeyAnswersWhileASheetIsUp_BeforeTheNoOpShortCircuit()
    {
        var raise = SourceGate.MethodBody(SourceGate.ReadRepoFile(Hub),
            "public static void Raise(SessionEvent what)");
        Assert.True(raise.IndexOf("if (what == SessionEvent.ToggleBoard && from.IsSheet())", StringComparison.Ordinal)
                    < raise.IndexOf("if (to == from) return;", StringComparison.Ordinal),
            "回执排在短路之后＝Sheet 期间那颗键什么都不会说");
        Assert.Contains("HotkeyGate.ReasonNotRunning(", raise);
        Assert.Contains("Report(\"画布现在按不动\"", raise);
        Assert.DoesNotContain("SetHint", raise);
    }

    /// <summary>
    /// 收会话那一步要<b>重新看一眼状态再决定下一发</b>：取消截图自己会走一次 EndSheet——
    /// 板子本来开着时落回冻结前那个子态（还得再 toggle 才到 Idle），板子没开时直接落 Idle。
    /// 少这道守卫就是"用户在设置里关掉屏幕画布，反手给他开了一块板子"。
    /// </summary>
    [Fact]
    public void ClosingTheSessionReReadsTheStageBeforeTheLastStep()
    {
        var idle = SourceGate.MethodBody(SourceGate.ReadRepoFile(Hub), "public static void EnsureIdle()");
        Assert.True(idle.IndexOf("CancelActiveSession();", StringComparison.Ordinal)
                    < idle.IndexOf("if (Stage.IsBoard())", StringComparison.Ordinal),
            "toggle 排在取消截图之前＝拿进门时那份状态做决定");
        Assert.Contains("if (Stage.IsBoard()) Raise(SessionEvent.ToggleBoard);", idle);
    }

    /// <summary>
    /// 进名册与退名册成对。留着已销毁的句柄不会崩，但会让"锚点"永远不可用——
    /// 玻璃此后每一次定序都静默什么都不做，症状要等到下一次"条子点不动"才看得见。
    /// </summary>
    [Fact]
    public void EveryRegisteredWindowLeavesTheRosterWhenItDies()
    {
        Assert.Contains("LayerDirector.Unregister(WindowInterop.GetHwnd(this));",
            SourceGate.ReadRepoPartials(Overlay));
        Assert.Contains("LayerDirector.Unregister(",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Bar), "public void Shutdown()"));
    }

    /// <summary>
    /// §10 的 S1 交付项「画布绘制态开截图不卡死」那一手：<b>玻璃收起那一刻必须把鼠标捕获交还系统</b>。
    /// <para><c>SetCapture</c> 抓的是鼠标，<b>窗被隐藏并不会让它失效</b>：抬起照样寄给那扇已经看不见的窗，
    /// 后来上屏的截图遮罩永远等不到"松手"，整场截图就卡在暗幕上（用户只能去托盘收掉全部）。
    /// 窗销毁时系统自己会放手，所以要手动还的只有"收起但没拆"这一条路——而 Sheet 态走的正是它。</para>
    /// </summary>
    [Fact]
    public void HidingTheGlassGivesTheMouseCaptureBack()
    {
        var apply = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Service),
            "public static void ApplyStage(AnnotationStage stage)");
        Assert.Contains("if (!visible) LayeredCanvasWindow.ReleasePointerCapture();", apply);
        Assert.True(apply.IndexOf("CommitOpenStroke();", StringComparison.Ordinal)
                    < apply.IndexOf("ReleasePointerCapture();", StringComparison.Ordinal),
            "手上那一笔要先收进持久层，再谈交还鼠标——反过来的话那一按的落点找不回来");
        Assert.Contains("public static void ReleasePointerCapture() => CanvasNative.ReleaseCapture();",
            SourceGate.ReadRepoFile(Layer));
    }
}
