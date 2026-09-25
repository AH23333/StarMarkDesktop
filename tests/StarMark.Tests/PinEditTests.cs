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
        // 批次 PU：改框的边/角判定自己算（SelectionEdgeAt），Move 那条留给"抓标注/挪框"的后面分支
        Assert.Contains("!_pinned && _selection is { } box", pressed);
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
        Assert.Contains("ActionBar.Visibility = Visibility.Collapsed;", through);
        Assert.Contains("PinBorder.Visibility = Visibility.Collapsed;", through);
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

    /// <summary>批次 PR/PU：确认选区之后双击不承担"提交复制"——那是放序号、回编辑文字时的高频手势。</summary>
    [Fact]
    public void DoubleTapCommitsOnlyInTheSelectionStage()
    {
        var tapped = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void OnDoubleTapped");
        Assert.Contains("if (_annotating) return;", tapped);         // 确认过选区：交给落笔/编辑那一层
        Assert.Contains("Commit(CommitAction.Copy);", tapped);       // 选区阶段才提交
        Assert.Contains("if (_pinned) { e.Handled = true; HidePin(); return; }", tapped);
    }

    /// <summary>方向键步长按屏幕定：0.2× 贴图上 1 个底图像素＝0.2 个屏幕像素，按一下几乎不动。</summary>
    [Fact]
    public void ArrowNudgeStepsAreScreenSized()
    {
        var nudge = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void NudgeSelectedMark");
        Assert.Contains("SlopInSource(big ? 10 : 1)", nudge);
    }

    /// <summary>序号接着现存最大号走：撤销/删掉最大那颗之后再点，不该跳号（Snipaste 手感）。</summary>
    [Fact]
    public void NumberToolContinuesFromExistingMarks()
    {
        var place = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void PlaceNumber");
        Assert.Contains("existing.Tool == AnnotationTool.Number) next = Math.Max(next, existing.Number + 1);", place);
        Assert.DoesNotContain("_numberCounter", SourceGate.ReadRepoFile(Overlay));
    }

    /// <summary>旋转保持当前倍率；四边形窗口区域随缩放按新倍率重套（否则放大后画面被裁得只剩一角）。</summary>
    /// <summary>
    /// 旋转只提供 90° 离散四种（用户裁决：左转/右转/水平/垂直）。自由角度旋转在 WinUI 上必然
    /// "四角填黑 + SetWindowRgn 裁切"两件套——真机就是大面积黑背景；外接矩形随角度变大还会把
    /// 贴图顶出屏幕。90° 是精确像素重排：保持倍率、窗口按 PinOrigin 收边、窗口恒为矩形。
    /// </summary>
    [Fact]
    public void RotationIsQuarterTurnOnly_KeepsZoomAndStaysOnScreen()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);
        var bake = SourceGate.MethodBody(cs, "private void BakeQuarterTurn");
        Assert.Contains("BitmapTransform.Rotate90(composed, _contentWidth, _contentHeight, clockwise)", bake);
        Assert.Contains("PinPixelSize(rotated.Width, rotated.Height, _zoom)", bake);
        Assert.Contains("PinOrigin(current.X, current.Y, w, h, WorkArea())", bake);   // 转完仍整块在屏内
        Assert.DoesNotContain("_zoom = 1d", bake);                                    // 旋转不重置倍率
        // 自由角度旋转的全套机器必须清干净：填黑四角 / 区域裁切 / 角度累计
        Assert.DoesNotContain("SetWindowRgn", cs);
        Assert.DoesNotContain("_bakedRotation", cs);
        Assert.DoesNotContain("RotatedImage", cs);
        Assert.Contains("Rotate90(", SourceGate.ReadRepoFile("src/StarMark.Integrations/Capture/BitmapTransform.cs"));
        Assert.DoesNotContain("RotatedImage", SourceGate.ReadRepoFile("src/StarMark.Integrations/Capture/BitmapTransform.cs"));
    }

    /// <summary>
    /// 候选窗口点击<b>不得开出一个独立的"标注态"</b>（批次 PU，用户口径：点击推荐窗口后
    /// 依旧处于框选阶段、可继续调框、可画可写）。两条路共用 <c>EnterEditing</c>：
    /// <c>EnterAnnotationMode</c> 那个"点了候选就锁进编辑"的独立入口必须不存在；
    /// <c>_selection</c> 在摆底图的那一处落进字段——漏了它，候选点击后每一按都判不出"在选区里"。
    /// </summary>
    [Fact]
    public void CandidateClickStaysInTheSelectionStage()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);
        Assert.Contains("_selection = selection;", SourceGate.MethodBody(cs, "private void EnterEditing"));
        Assert.Contains("BeginCandidatePress(physical, e.Pointer);",
            SourceGate.MethodBody(cs, "private void Root_PointerPressed"));
        Assert.DoesNotContain("EnterAnnotationMode", cs);
        // 点按（没拖动）松手＝确认候选；确认的入口只有 ConfirmSelection 这一处
        Assert.Contains("if (candidate is { } cand)",
            SourceGate.MethodBody(cs, "private void Root_PointerReleased"));
        Assert.Contains("private void ConfirmSelection()", cs);
    }

    /// <summary>真机反馈"贴图边缘无高亮显示"：悬停时要有高亮边框，且与工具条同节奏出现/收起。</summary>
    [Fact]
    public void PinShowsAHighlightBorderWhileTheMouseIsOverIt()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);
        Assert.Contains("PinBorder", SourceGate.ReadRepoFile(OverlayXaml));
        Assert.Contains("PinBorder.Visibility = Visibility.Visible;",
            SourceGate.MethodBody(cs, "private void Root_PointerEntered"));
        Assert.Contains("PinBorder.Visibility = Visibility.Collapsed;",
            SourceGate.MethodBody(cs, "private void Root_PointerExited"));
        // 穿透中的窗收不到鼠标，高亮若不主动收起会永远留在画面上
        Assert.Contains("PinBorder.Visibility = Visibility.Collapsed;",
            SourceGate.MethodBody(cs, "public bool ApplyClickThrough"));
    }

    /// <summary>
    /// 窗口检测是尽力而为：<b>任何失败都只能降级成"没有候选"</b>，绝不能拖死截图会话。
    /// <para>SP 曾把 DWM 导出名写成不存在的 DwmGetWindowAttributeRect，而候选收集在遮罩窗
    /// 构造函数里跑——真机每次 F1 都死在"截图失败"。</para>
    /// </summary>
    [Fact]
    public void WindowDetectionCanNeverKillTheCapture()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);
        var collect = SourceGate.MethodBody(cs, "private List<IntRect> CollectWindowCandidates");
        Assert.Contains("catch (Exception ex)", collect);
        Assert.Contains("return new List<IntRect>();", collect);
        // 候选收集必须发生在窗口上屏之前：上屏之后抛异常＝一块看得见、键盘焦点也没挂上的僵尸遮罩
        var ctor = SourceGate.MethodBody(cs, "ScreenFrame frame,");
        Assert.True(
            ctor.IndexOf("CollectWindowCandidates();", StringComparison.Ordinal)
            < ctor.IndexOf("SWP_SHOWWINDOW", StringComparison.Ordinal),
            "候选收集排在窗口上屏之后＝失败时留下看得见却关不掉的僵尸遮罩");
        // P/Invoke 必须用真实存在的导出名
        var interop = SourceGate.ReadRepoFile("src/StarMark.UI/Helpers/WindowInterop.cs");
        Assert.Contains("DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size)", interop);
        Assert.DoesNotContain("DwmGetWindowAttributeRect", interop);
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
        // 批次 PU 起 FinalPixels 分两条路：贴图＝按底图尺寸重烤，截图＝从合成图裁选区（裁剪用选区尺寸是对的）。
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay),
            "private (byte[] Pixels, int Width, int Height)? FinalPixels()");

        Assert.Contains("AnnotationPainter.Render(basePixels, _contentWidth, _contentHeight, _history.Marks)", body);
        Assert.DoesNotContain("Render(basePixels, selection.Width", body);
        Assert.Contains("BitmapTransform.Crop(composed", body);
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
        // 贴图 Esc 是两级：有选中的标注先丢选中，再按才关这张（关闭会连没烤出去的标注一起丢）
        Assert.Contains("if (_selected is not null) DropSelection();", keys);
        Assert.Contains("else Close();", keys);
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
