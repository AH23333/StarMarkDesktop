#nullable enable
using System.IO;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 PM/PN：贴图那扇窗<b>本身就是</b>截图时的编辑器（用户口径：
/// "可以始终使用截图时的菜单，唯一改变的只有…是框选区域还是钉在桌面上的贴图"）。
/// <para>
/// 这一批没有可断言的纯函数（改的是"哪扇窗持有这条链"），所以钉的是**结构**：
/// 只允许有一条标注链、贴图态必须收掉选区外壳、坐标必须除掉缩放、工具条必须承担原来那份右键菜单。
/// 结构一旦被改回去（例如又开一扇"编辑窗"、或再写一份 PinWindow），这些闸门会红。
/// </para>
/// </summary>
public sealed class PinEditTests
{
    private const string Overlay = "src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs";
    private const string OverlayXaml = "src/StarMark.UI/Views/CaptureOverlayWindow.xaml";

    [Fact]
    public void ThereIsOnlyOnePinWindowClass_TheCaptureEditorItself()
    {
        // 第二份"贴图窗"就是第二份标注链的开始——PM 那一版正是这样，被用户退回。
        Assert.False(File.Exists(Path.Combine(SourceGate.RepoRoot(), "src/StarMark.UI/Views/PinWindow.xaml.cs")));

        var manager = SourceGate.ReadRepoFile("src/StarMark.UI/Services/PinManager.cs");
        Assert.Contains("List<CaptureOverlayWindow> Pins", manager);
        Assert.Contains("new CaptureOverlayWindow(bgra, width, height, placement", manager);
        Assert.Equal(0, SourceGate.Count(manager, "PinWindow"));
    }

    [Fact]
    public void PinnedWindowTurnsOffTheSelectionChrome()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);
        var ctor = SourceGate.MethodBody(cs, "double zoom)");

        // 整块都是内容：压暗、框选说明这些"选区外壳"必须收起，否则用户会去拖一圈不存在的边界
        Assert.Contains("DimLayer.Visibility = Visibility.Collapsed", ctor);
        Assert.Contains("HintChip.Visibility = Visibility.Collapsed", ctor);
        Assert.Contains("BeginEditingExisting(pixels, width, height)", ctor);
        Assert.Contains("PinManager.Unregister(this)", ctor);

        // 而"重新框一块 / 改框"两条分支都要被挡掉：那一按会连底图带已画标注一起清空
        var pressed = SourceGate.MethodBody(cs, "private void Root_PointerPressed");
        Assert.Contains("if (_pinned)", pressed);
        Assert.Contains("if (!Armed) BeginPinDrag(e.Pointer);", pressed);   // 没选笔＝不动笔 ⇒ 这一按是移动这张图
        Assert.Contains("!_pinned && _base is not null && !Armed && TryBeginAdjust", pressed);
        // 贴图态的"选区"＝整块画面，所以"未选笔"那一按先在选区内被拦下、根本走不到选区外面。
        // 这条就是真机反馈"贴图拖不动"的位置：没在里面接上 BeginPinDrag，移动就永远触发不了。
        Assert.Contains("if (_tool is not { } tool)", pressed);
        Assert.Contains("if (_pinned) BeginPinDrag(e.Pointer);", pressed);
    }

    /// <summary>
    /// 贴图这条工具条<b>只能压在画面上</b>（窗口就是那张图），而"截一小块钉住"常常没有一条工具条宽：
    /// 不整条缩进画面的话，被裁掉的正好是右边那几颗（复制 / 存图 / 识字 / 穿透 / 关闭）。
    /// </summary>
    [Fact]
    public void BarIsScaledDownSoTheRightHandButtonsStayReachable()
    {
        var bar = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void PositionBar");
        // 小贴图整条缩进画面（下限 0.5），且缩的时候贴着右上角收——不然被裁掉的正是右边那几颗
        Assert.Contains("Math.Clamp((available - 8) / barWidth, 0.5, 1.0)", bar);
        // 贴图这条不许再自己算左边界：那是"整条被推到画面外、要靠放大才挪得出来"的成因
        Assert.DoesNotContain("screenWidth - scaledWidth", bar);
        Assert.Contains("return;", bar);
    }

    /// <summary>穿透中的贴图收不到鼠标，那条"悬停才收起"的工具条会一直留在画面上且没人点得动它。</summary>
    [Fact]
    public void ClickThroughPutsTheBarAway_BecauseTheWindowNoLongerSeesTheMouse()
    {
        var through = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "public bool ApplyClickThrough");
        Assert.Contains("if (_clickThrough) ActionBar.Visibility = Visibility.Collapsed;", through);
    }

    /// <summary>
    /// 贴图态"选区"恒等于本窗矩形 ⇒ 改窗口位置/尺寸必须把选区带着一起走。
    /// <para>`ToLocal`／`InsideSelection`／`PositionBar` 全以选区为原点：只改 _monitor 的话，
    /// 用户把这张图拖过一次，笔迹就按那段距离整体平移（并且 InsideSelection 一路报 false＝再也画不上）。</para>
    /// </summary>
    [Fact]
    public void MovingOrResizingThePinKeepsTheSelectionInStep()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);
        Assert.Contains("if (_pinned) _selection = next;",
            SourceGate.MethodBody(cs, "private void SetMonitor(IntRect next)"));
        var drag = SourceGate.MethodBody(cs, "private void PinDragTo");
        Assert.Contains("SetMonitor(", drag);
        Assert.DoesNotContain("_monitor = new IntRect(", drag);      // 不许绕过 SetMonitor 自己改
        Assert.Contains("SetMonitor(", SourceGate.MethodBody(cs, "private void ResizePinAnchoringTopLeft"));
    }

    /// <summary>
    /// 每一处"把标注烤进缓冲"的调用都必须按<b>底图尺寸</b>算，不能按选区尺寸。
    /// <para>贴图态选区＝窗口的显示尺寸（＝底图 × 倍率），一放大就比缓冲长：
    /// `AnnotationPainter` 的护栏直接抛"像素缓冲比声明的尺寸短，画上去会越界"，
    /// 界面把它显示成"标注没能画上去"——真机反馈就是这条，且倍率 1× 时看不出来。</para>
    /// </summary>
    [Fact]
    public void EveryPaintSizesOffTheBaseBuffer_NotTheSelection()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);
        foreach (var method in new[] { "private void Rebake()", "private byte[]? UnderDragBuffer", "private void DragTo" })
            Assert.Contains("_contentWidth, _contentHeight", SourceGate.MethodBody(cs, method));
        Assert.DoesNotContain("Render(basePixels, selection.Width", cs);
        Assert.DoesNotContain("(scratch, selection.Width", cs);
        Assert.DoesNotContain("(canvas, selection.Width", cs);
    }

    /// <summary>
    /// 贴图这条工具条只能压在画面上：摆它的那一处必须用<b>布局真值</b>量可用宽度，
    /// 而不是"物理宽 ÷ 缩放"自己算左边界（建窗那一刻 DPI 还没落到本窗时算出来的数会比窗口宽，
    /// 整条被推到画面外，只剩右上角露一点 —— 真机反馈"要不停放大才把菜单挪出来"）。
    /// </summary>
    [Fact]
    public void PinBarIsPlacedByLayoutTruthAndKeepsItsOwnSize()
    {
        var bar = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void PositionBar");
        Assert.Contains("Root.ActualWidth", bar);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Right", bar);
        Assert.Contains("RenderTransformOrigin = new Windows.Foundation.Point(1, 0)", bar);   // 贴着右上角往里收
        Assert.Contains("_pinned", bar);
        // 第一次布局才有真宽度：量到了要重摆一次
        Assert.Contains("ActionBar.SizeChanged += ",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void InitWindow"));
        // 输入框的字号也要过倍率这一层：只除 DPI 的话，放大过的贴图上"框里的字"比烤进去的那份小一个倍率
        Assert.Contains("_sourceScale / _scale",
            SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void BeginTextEdit"));
    }

    /// <summary>
    /// 换算除数必须有初始值。<b>截图那条链不经过贴图构造</b>，不写初始值就是 double 的默认 0，
    /// 而 <c>ToLocal</c> 拿它做除数、<c>SlopInSource</c> 与输入框字号也按它换算 ⇒ 起笔坐标变成
    /// Infinity/NaN 再截回 int，表现就是真机反馈的"截图时无法编辑"。
    /// <para>这条闸门存在的理由：PM/PN 两批的断言都在问"贴图要不要除掉这个数"，
    /// 没有一句问过"截图态它是几"——而 C# 里"忘了写初始值"既不报警也不报错。</para>
    /// </summary>
    [Fact]
    public void TheCapturePathNeverReliesOnAnUninitializedScale()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);
        Assert.Contains("private double _sourceScale = 1.0;", cs);
        Assert.DoesNotContain("private double _sourceScale;", cs);
        Assert.Contains("private double _zoom = 1.0;", cs);        // 同一族：贴图倍率也有明确初值
    }

    [Fact]
    public void PointerMappingDividesTheZoomOut_AndWheelKeepsItInStep()
    {
        // 不除掉缩放，2.5× 的贴图就会"鼠标明明在字上、笔落在字外"；
        // 滚轮改了倍率却忘了同步这个除数，是同一缺陷的另一半。
        var cs = SourceGate.ReadRepoFile(Overlay);

        Assert.Contains("/ _sourceScale", SourceGate.MethodBody(cs, "private PixelPoint ToLocal"));
        Assert.Contains("* _sourceScale", SourceGate.MethodBody(cs, "private (double X, double Y) LocalToDip"));
        var wheel = SourceGate.MethodBody(cs, "private void Root_PointerWheelChanged");
        Assert.Contains("_sourceScale = next;", wheel);
        Assert.Contains("ResizePinAnchoringTopLeft()", wheel);
        // 手上有未完成的一笔时倍率不许动：那些点是按旧除数换算的，中途改等于让这一笔跑偏，
        // 而跑偏只有松手合成之后才看得见（那时已经退不掉）。
        Assert.Contains("_stroke is not null || _polyLine is not null || _dragOriginal is not null", wheel);
        // 贴图会被拖到另一块缩放不同的屏上：那块屏的 DPI 变了就要重算 DIP 除数并重摆内容，
        // 否则画面比窗口大/小一圈，笔也整体偏（截图态每屏一窗，走不到这一步）。
        Assert.Contains("if (RefreshScaleIfChanged()) RelayoutContent();",
            SourceGate.MethodBody(cs, "private void PinDragTo"));
        Assert.Contains("WindowInterop.GetScale(this)",
            SourceGate.MethodBody(cs, "private bool RefreshScaleIfChanged"));
    }

    [Fact]
    public void FinalPixelsRenderAtSourceSize_NotDisplaySize()
    {
        // 贴图在 2.5× 时"窗口矩形"是显示尺寸；按它渲染会得到一张拉伸过的糊图。
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay),
            "private (byte[] Pixels, int Width, int Height)? FinalPixels()");

        Assert.Contains("_contentWidth, _contentHeight", body);
        Assert.DoesNotContain("selection.Width, selection.Height", body);
    }

    [Fact]
    public void ToolbarCarriesWhatThePinContextMenuUsedTo()
    {
        // 用户口径：贴图那份右键菜单"完全可以"由截图时这条小菜单取代。所以菜单删了，
        // 但它承担过的动作必须都在条上——少一项就是功能被删掉了。
        var cs = SourceGate.ReadRepoFile(Overlay);
        Assert.DoesNotContain("AttachedFlyout", SourceGate.ReadRepoFile(OverlayXaml));

        var bar = SourceGate.MethodBody(cs, "private void BuildToolBar()");
        Assert.Contains("if (_pinned)", bar);
        Assert.Contains("Through_Click", bar);                                  // 鼠标穿透
        Assert.Contains("_pinned ? \"关闭这张（Esc）\"", bar);                    // 关闭这张
        Assert.Contains("if (!_pinned)", bar);                                  // "再钉一张"只在截图态出现
        Assert.Contains("CopyIcon()", bar);
        Assert.Contains("SaveIcon()", bar);
        Assert.Contains("OcrIcon()", bar);
    }

    [Fact]
    public void BarHidesOnLeaveButNeverWhileTheUserIsUsingAPen()
    {
        // 一屏十几张贴图，条子常驻会盖住画面；但"画着画着条没了"比看不见更烦。
        var cs = SourceGate.ReadRepoFile(Overlay);
        var exited = SourceGate.MethodBody(cs, "private void Root_PointerExited");

        Assert.Contains("Armed || _editingText || _polyLine is not null || _selected is not null", exited);
        Assert.Contains("ActionBar.Visibility = Visibility.Collapsed", exited);
    }

    [Fact]
    public void EscapeAndCrossCloseThePin_WhileCaptureStillCancelsTheShot()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);

        Assert.Contains("if (_pinned) Close();", SourceGate.MethodBody(cs, "private void Cancel_Click"));
        var keys = SourceGate.MethodBody(cs, "private void Root_KeyDown");
        Assert.Contains("if (_pinned) Close();     // 贴图态：Esc＝关闭这张", keys);
        // 贴图态右键不能是"丢"：图已经画了一半，一次误触不该把它清空
        Assert.Contains("if (!_pinned) Settle(null);", SourceGate.MethodBody(cs, "private void Root_RightTapped"));
    }

    [Fact]
    public void PinnedWindowNeverDeliversASelectionToTheCaptureSession()
    {
        // 贴图不在截图会话里：交回选区会让服务去关"整场会话的遮罩窗"，那是一屏之外的另一批窗。
        var settle = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void Settle(IntRect? selection)");
        Assert.Contains("if (!_pinned) _finish(this, selection);", settle);
    }
}
