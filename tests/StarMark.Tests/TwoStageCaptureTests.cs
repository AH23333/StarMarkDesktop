#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Integrations.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 PU：截图交互改成 Snipaste 两阶段模型（用户口径逐句钉住）——
/// "在识别阶段不出现菜单栏，点击推荐窗口后依旧处于框选阶段且可调节框选区域大小…
/// 在框选阶段即可进行有效的绘制和文字编辑，且可超出框选区域，取消选择编辑工具后仍可继续调整框选区域，
/// 在点击'贴到桌面上'功能按钮后才截取框选区域图片并钉在桌面上"。
/// <para>
/// 落到结构上是四条不变量：①标注跟屏走（重拖/改框不清笔迹、不平移标注）；
/// ②框选阶段就能落笔（Move 判定不许劫持框内那一按，框外拿笔也是落笔）；
/// ③压暗烤进合成图（XAML 压暗层会盖住越界的标注）；④提交＝从合成图裁选区。
/// 界面侧引用不到，只能扫源码；几何与像素的两条（Crop / DimOutside）直接跑真数据。
/// </para>
/// </summary>
public sealed class TwoStageCaptureTests
{
    private const string Overlay = "src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs";
    private const string ToolBar = "src/StarMark.UI/Views/CaptureOverlayWindow.ToolBar.cs";
    private const string CanvasToolbar = "src/StarMark.UI/Views/CanvasToolbarWindow.xaml.cs";
    private const string OverlayXaml = "src/StarMark.UI/Views/CaptureOverlayWindow.xaml";

    // ────────── 结构闸门（UI 源码扫描） ──────────

    /// <summary>框外那一按，拿着笔＝落笔（可越出选区）。框内/框外两条路共用一个分发，
    /// 所以"BeginToolStroke(physical, …)"必须恰好出现两次——少了哪一处，哪一侧就还在走老路。</summary>
    [Fact]
    public void DrawingMayLeaveTheSelectionBounds()
    {
        var cs = SourceGate.ReadRepoPartials(Overlay);
        var press = SourceGate.MethodBody(cs, "private void Root_PointerPressed");
        Assert.Equal(2, SourceGate.Count(press, "BeginToolStroke(physical, e.Pointer);"));
        // 框外那条路必须先问过 Armed：没拿笔的框外按下仍是"重新框一块"
        Assert.Contains("if (_mode == CaptureMode.Toolbar && Armed)", press);
    }

    /// <summary>重拖一块新选区<b>不清标注</b>（标注跟屏走）。原来那条 ResetAnnotations
    /// （历史清空＋底图作废＋标注层收起）整个删掉，谁也不许再叫回去。</summary>
    [Fact]
    public void RedrawingTheRegionKeepsTheMarks()
    {
        var cs = SourceGate.ReadRepoPartials(Overlay);
        Assert.DoesNotContain("ResetAnnotations", cs);
        var drag = SourceGate.MethodBody(cs, "private void BeginRegionDrag");
        Assert.DoesNotContain("_history.Reset()", drag);
        Assert.DoesNotContain("_base = null", drag);
        Assert.DoesNotContain("AnnotateLayer.Visibility = Visibility.Collapsed", drag);
        Assert.Contains("DropSelection();", drag);
    }

    /// <summary>提交＝从"整帧＋标注"的合成图里裁出选区（截图态）。没有这一步，
    /// 越界的标注要么整个进图（尺寸对不上）要么全部丢失。</summary>
    [Fact]
    public void CommitCropsTheComposedFrame()
    {
        var final = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Overlay),
            "private (byte[] Pixels, int Width, int Height)? FinalPixels()");
        Assert.Contains("BitmapTransform.Crop(composed, _contentWidth, _contentHeight,", final);
        Assert.Contains("_annotating", final);           // 只在确认过选区后走裁剪路
        Assert.Contains("ScreenshotService.TryCrop(frame", final);   // 未确认（Enter 直提交）的兜底仍在
    }

    /// <summary>压暗烤进合成图：确认且不在拖框时 XAML 那四块必须收起（否则叠暗一遍、盖掉越界标注）；
    /// 改框/重拖进行中内容源切到无压暗平面缓冲，四块重新出来实时跟随（零重烤，批次 PV 的卡顿修复）。</summary>
    [Fact]
    public void DimIsBakedIntoTheComposedFrame()
    {
        var cs = SourceGate.ReadRepoPartials(Overlay);
        Assert.Contains("BitmapTransform.DimOutside(composed, _contentWidth, _contentHeight, hole, 0x66);",
            SourceGate.MethodBody(cs, "private void Rebake()"));
        Assert.Contains("_flatPreview", SourceGate.MethodBody(cs, "private void Rebake()"));
        var draw = SourceGate.MethodBody(cs, "private void DrawSelection(IntRect selection)");
        Assert.Contains("!_annotating || IsFrameDragging ? Visibility.Visible : Visibility.Collapsed", draw);
        // 拖动期间不许有任何整帧重烤：ShowFlatWhileDragging 只切 Source，不碰像素
        Assert.Contains("private void ShowFlatWhileDragging()", cs);
        Assert.DoesNotContain("ScheduleRebake", cs);
    }

    /// <summary>识别（悬停建议）阶段不出现菜单栏：自动检测与放大镜只在"未确认选区"时活动，
    /// 工具条只在 EnterEditing（确认）里亮出来。批次 WI 起亮/收一律走那唯一的出口
    /// （贴图态它住独立窗，直接写 Visibility 会留下"看不见但吃鼠标的空窗"）。</summary>
    [Fact]
    public void TheToolbarAppearsOnlyAfterConfirmation()
    {
        var cs = SourceGate.ReadRepoPartials(Overlay);
        Assert.Contains("if (!_pinned && !_annotating)",
            SourceGate.MethodBody(cs, "private void Root_PointerMoved"));
        var enter = SourceGate.MethodBody(cs, "private void EnterEditing(");
        Assert.Contains("SetBarVisible(true);", enter);
        Assert.DoesNotContain("ActionBar.Visibility", enter);
        Assert.Contains("ToDip(_monitor)", enter);       // 内容层铺满整扇窗，不按选区摆
    }

    [Fact]
    public void RegionReleaseAppliesItsOwnPositionBeforeConfirmation()
    {
        var released = SourceGate.Code(SourceGate.MethodBody(
            SourceGate.ReadRepoPartials(Overlay), "private void Root_PointerReleased"));
        var ordered = new[]
        {
            "if (!_awaitingRelease) return;",
            "var point = e.GetCurrentPoint(Root);",
            "UpdateRegionDrag(ToPhysical(point.Position.X, point.Position.Y));",
            "_awaitingRelease = false;",
            "if (_selection is not { } selection) return;",
        };
        var previous = -1;
        foreach (var statement in ordered)
        {
            var index = released.IndexOf(statement, StringComparison.Ordinal);
            Assert.True(index >= 0, statement);
            Assert.True(index > previous, statement);
            previous = index;
        }
    }

    [Fact]
    public void RegionMoveAndReleaseShareClippingAndCandidateJitterRules()
    {
        var cs = SourceGate.ReadRepoPartials(Overlay);
        var moved = SourceGate.Code(SourceGate.MethodBody(cs, "private void Root_PointerMoved"));
        Assert.Contains("if (!_awaitingRelease) return;", moved);
        Assert.Contains("UpdateRegionDrag(physical);", moved);
        var update = SourceGate.Code(SourceGate.MethodBody(cs, "private void UpdateRegionDrag"));
        Assert.Contains("CaptureGeometry.Normalize(_startPhysical.X, _startPhysical.Y, physical.X, physical.Y)", update);
        Assert.Contains("CaptureGeometry.Intersect(selection, _monitor) ?? selection", update);
        Assert.Matches(@"if\s*\(_candidate is not null && normalized.Width < CaptureGeometry.MinSelectionSide\s*&& normalized.Height < CaptureGeometry.MinSelectionSide\) return;", update);
        Assert.Contains("_selection = normalized;", update);
        Assert.Contains("DrawSelection(box);", update);
    }

    /// <summary>截图态 Esc 两级：拿着笔先收笔（回到可改框的那一态），再按才取消整场。</summary>
    [Fact]
    public void EscDisarmsThePenBeforeCancelingTheShot()
    {
        var keys = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Overlay), "private void Root_KeyDown");
        Assert.Contains("else if (Armed) SetTool(null);", keys);
        // 方向键的分态也必须跟着两阶段走：确认前＝挪/缩选区，确认后＝挪选中的标注
        Assert.Contains("if (_pinned || _annotating) NudgeSelectedMark(e.Key, IsShiftDown());", keys);
    }

    /// <summary>文字标注的 ✕（用户口径："编辑框右上角为X号，可以点击删除该文字编辑框"）：
    /// 独立顶层元素（标注层整层不吃命中）、按在文字标注上才现身、按下就删并把事件吃掉。</summary>
    [Fact]
    public void TextGetsAnXToDeleteIt()
    {
        var xaml = SourceGate.ReadRepoFile(OverlayXaml);
        Assert.Contains("x:Name=\"TextDeleteButton\"", xaml);
        var cs = SourceGate.ReadRepoPartials(Overlay);
        var handles = SourceGate.MethodBody(cs, "private void DrawSelectionHandles(Annotation? mark = null)");
        Assert.Contains("mark.Tool == AnnotationTool.Text", handles);
        Assert.Contains("TextDeleteButton.Visibility = Visibility.Visible;", handles);
        var init = SourceGate.MethodBody(cs, "private void InitWindow()");
        Assert.Contains("TextDeleteButton.PointerPressed", init);
        Assert.Contains("DeleteSelected();", init);
        Assert.Contains("e.Handled = true;", init);      // 这一按不许再冒成落笔/挪框
    }

    /// <summary>旋转过的文字回编辑时输入框跟着转（用户口径："旋转文字时文字编辑框也一同旋转"）。</summary>
    [Fact]
    public void TheTextEditBoxFollowsTheRotation()
    {
        var edit = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Overlay), "private void BeginTextEdit");
        Assert.Contains("rotated.Rotation != 0", edit);
        Assert.Contains("Angle = rotated.Rotation", edit);
    }

    /// <summary>悬停光标按选区部位给形状（用户口径："光标位于区域内时为十字箭头，位于边缘时为拉伸的双向箭头"）。</summary>
    [Fact]
    public void HoverCursorFollowsTheSelectionEdge()
    {
        var hover = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Overlay), "private void UpdateHoverCursor");
        Assert.Contains("CaptureGeometry.SelectionEdgeAt(box, AsPixel(physical), SelectionSlop)", hover);
        Assert.Contains("InputSystemCursorShape.SizeAll", hover);            // 框内＝十字箭头
        Assert.Contains("InputSystemCursorShape.SizeNorthwestSoutheast", hover);   // 边/角＝双向箭头
        Assert.Contains("InputSystemCursorShape.Cross", hover);              // 拿笔/框外＝十字准线
    }

    /// <summary>整帧才是底图（截图态）：构造时就按整屏定内容尺寸，选区只是"裁到哪里"的记号。</summary>
    [Fact]
    public void TheWholeFrameIsTheBase_NotTheSelection()
    {
        var ctor = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Overlay), "ScreenFrame frame,");
        Assert.Contains("_base = shot.Pixels;", ctor);
        Assert.Contains("_contentWidth = _monitor.Width;", ctor);
        Assert.Contains("_contentHeight = _monitor.Height;", ctor);
    }

    /// <summary>拖动已有标注的预览要节流：截图态的拖动底是整帧缓冲，逐鼠标事件上传会跟不上手。</summary>
    [Fact]
    public void TheDragPreviewUploadIsThrottled()
    {
        var drag = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Overlay), "private void DragTo");
        Assert.Contains("_lastDragPaint", drag);
        Assert.Contains(">= 16", drag);
    }

    // ────────── 批次 PV（PU 复验六条）的结构闸门 ──────────

    /// <summary>确认选区后选区框必须<b>常驻</b>（真机：确认路径路过 ClearDetected→ClearSelection
    /// 把框收掉＝"无框"）；提示条也要压回去。</summary>
    [Fact]
    public void TheSelectionBorderSurvivesConfirmation()
    {
        var enter = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Overlay), "private void EnterEditing(");
        Assert.Contains("DrawSelection(selection);", enter);
        Assert.Contains("HintChip.Visibility = Visibility.Collapsed;", enter);
    }

    /// <summary>框选模式下点击已画内容＝直接选中（不需要先拿笔）：
    /// 抓取的入口在"没有选中"时先做命中测试，未命中才放行给挪框。</summary>
    [Fact]
    public void TappingDrawnContentSelectsItWithoutATool()
    {
        var grab = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Overlay), "private bool TryBeginGrab");
        Assert.Contains("AnnotationPainter.HitTest(_history.Marks, local, SlopInSource(SelectionSlop))", grab);
        Assert.Contains("fresh", grab);      // 首次选中与"点回去改字"是两回事
        // 文字"再点一次才进编辑"：首次选中的那一按松手不许直接弹输入框
        var end = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Overlay), "private void EndDrag(");
        Assert.Contains("!fresh && grab == Grab.Move && original.Tool == AnnotationTool.Text", end);
    }

    /// <summary>
    /// 笔类工具：<b>点一次＝选中＋弹出颜色/粗细浮层</b>（换颜色永远一次点击就到），
    /// <b>再点当前这支＝收笔</b>回"改框 / 改已画内容位置大小"那一态。
    /// <para>
    /// 后者是批次 WD-6 的发起人裁决，<b>推翻批次 PV 那版"收笔只交给 Esc"</b>：画完想立刻挪动它的人
    /// 手在鼠标上，逼他去找键盘就是多一步。浮层只在"选中那一下"弹——再点这一下是收笔，
    /// 把颜色/粗细又摆回来等于否认他刚做的选择。Esc 仍然是收笔的另一条出口（两级 Esc 不动）。
    /// </para>
    /// </summary>
    [Fact]
    public void RepeatingTheSelectedPenHolstersIt_AndThePickerOnlyOpensOnSelection()
    {
        var cs = SourceGate.ReadRepoPartials(Overlay);
        var click = SourceGate.MethodBody(cs, "private void BrushTool_Click");
        Assert.Contains("if (tool == _tool)", click);
        Assert.Contains("SetTool(null);", click);
        Assert.True(click.IndexOf("HidePicker();") < click.IndexOf("SetTool(tool);"),
            "收笔那一支不能顺手把浮层再摆回来");
        Assert.Contains("SetTool(tool);", click);
        Assert.Contains("ShowBrushPicker(button);", click);
        Assert.Contains("SetTool(wanted);", cs);                 // 形状浮层同样只选中、不 toggle
        var keys = SourceGate.MethodBody(cs, "private void Root_KeyDown");
        Assert.Contains("else if (Armed) SetTool(null);", keys); // Esc 仍是收笔出口，两条并存
    }

    /// <summary>文字的"细/中/粗"＝字号档（16/22/32）：输入框开框与落笔烤出去用的是同一个数——
    /// 输入时的文字大小就是编辑后的文字大小（真机反馈的 WYSIWYG 缺口）。</summary>
    [Fact]
    public void TextWeightStepsAreFontSizes()
    {
        // 注意传的是"档位下标"（ThicknessSteps = {2,4,8} 的 0/1/2），不是粗细值本身
        Assert.Equal(new[] { 16, 22, 32 },
            new[] { 0, 1, 2 }.Select(step => StarMark.Core.Capture.Annotation.ThicknessFor(
                StarMark.Core.Capture.AnnotationTool.Text, step)));
        var cs = SourceGate.ReadRepoPartials(Overlay);
        Assert.Contains("Annotation.ThicknessFor(AnnotationTool.Text, _weightIndex)",
            SourceGate.MethodBody(cs, "private void BeginTextEdit"));
        Assert.Contains("FontHeight = Annotation.ThicknessFor(AnnotationTool.Text, _weightIndex),",
            SourceGate.MethodBody(cs, "private void EndTextEditing"));
    }

    // ────────── 像素单测（Integrations，直接跑真数据） ──────────

    /// <summary>Crop：裁出来的必须是源图上那块的逐行拷贝——跨行错位是这类实现最典型的错。</summary>
    [Fact]
    public void CropExtractsExactlyTheRequestedRect()
    {
        const int w = 6, h = 5;
        var src = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            src[i * 4] = (byte)(i & 0xFF);          // B = 下标低字节：唯一可认
            src[i * 4 + 3] = 255;
        }
        var (pixels, cw, ch) = BitmapTransform.Crop(src, w, h, 2, 1, 3, 2);
        Assert.Equal((3, 2), (cw, ch));
        // 第一行＝源图 y=1 的 x=2..4（下标 8,9,10），第二行＝源图 y=2 的 x=2..4（下标 14,15,16）
        Assert.Equal(new byte[] { 8, 9, 10 }, new[] { pixels[0], pixels[4], pixels[8] });
        Assert.Equal(new byte[] { 14, 15, 16 }, new[] { pixels[12], pixels[16], pixels[20] });
        // 源图不许被改动
        Assert.Equal(0, src[0]);
        Assert.Equal(255, src[3]);
    }

    [Fact]
    public void CropClampsAndRejectsEmptyResults()
    {
        var src = new byte[4 * 4 * 4];
        // 越界请求被夹回源图（右侧越界 → 只剩夹完那块）
        var (pixels, cw, ch) = BitmapTransform.Crop(src, 4, 4, 2, 2, 99, 99);
        Assert.Equal((2, 2), (cw, ch));
        // 夹完全空的请求必须抛，不许交一张 0×0 的图
        Assert.Throws<CaptureFailureException>(() => BitmapTransform.Crop(src, 4, 4, 4, 4, 2, 2));
        Assert.Throws<CaptureFailureException>(() => BitmapTransform.Crop(new byte[3], 4, 4, 0, 0, 1, 1));
    }

    /// <summary>DimOutside：洞内一格不动，洞外按 keep 因子变暗，alpha 不许被动过（提交还要靠它不透明）。</summary>
    [Fact]
    public void DimOutsideDarkensOnlyOutsideTheHole()
    {
        const int w = 4, h = 4;
        var bgra = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            bgra[i * 4] = 200;      // B
            bgra[i * 4 + 1] = 150;  // G
            bgra[i * 4 + 2] = 100;  // R
            bgra[i * 4 + 3] = 255;
        }
        BitmapTransform.DimOutside(bgra, w, h, new IntRect(1, 1, 2, 2), 0x66);

        int At(int x, int y) => (y * w + x) * 4;
        // 洞内（(1,1)..(2,2)）原样
        Assert.Equal((200, 150, 100, 255), (bgra[At(1, 1)], bgra[At(1, 1) + 1], bgra[At(1, 1) + 2], bgra[At(1, 1) + 3]));
        Assert.Equal(200, bgra[At(2, 2)]);
        // 洞外（(0,0)）按 255-0x66=153 压暗：200*153/255=120
        Assert.Equal(120, bgra[At(0, 0)]);
        Assert.Equal((byte)(150 * 153 / 255), bgra[At(0, 0) + 1]);
        Assert.Equal((byte)(100 * 153 / 255), bgra[At(0, 0) + 2]);
        Assert.Equal(255, bgra[At(0, 0) + 3]);      // alpha 不变
        // 洞正右侧那格也在洞外
        Assert.Equal(120, bgra[At(3, 1)]);
    }

    [Fact]
    public void DimOutsideHandlesHolesThatLeaveTheBuffer()
    {
        var bgra = new byte[4 * 4 * 4];
        // 洞比画面大/负原点都不许越界写
        BitmapTransform.DimOutside(bgra, 4, 4, new IntRect(-3, -3, 20, 20), 0x66);
        BitmapTransform.DimOutside(bgra, 4, 4, new IntRect(4, 4, 2, 2), 0x66);
        Assert.Throws<CaptureFailureException>(() =>
            BitmapTransform.DimOutside(new byte[3], 4, 4, new IntRect(0, 0, 1, 1), 0x66));
    }

    /// <summary>
    /// 那两句（「太小」与「屏外」）在界面上到底问的是哪一块。真机 2026-10-29 13:20 量到：屏幕正中横向拖一条
    /// 100×0 的细条一路走到裁图才炸（角标念的是「切り出せません…100×0 を要求」），因为两颗读者把 <c>_monitor</c>
    /// 递给了旧判据里"已相交的那块"那一颗参数——量的是整块屏，永远不"太小"。
    /// 现在判据自己答那两问（尺寸看选区、屏外才看相交），所以界面<b>照旧交整块屏</b>才是对的形状：钉住这两行，
    /// 也钉住另一种改法（在调用点先相交＝那颗"屏外"的说法会被顶掉）。
    /// </summary>
    [Fact]
    public void BothSelectionGatesHandTheMonitorAndTheGuardMeasuresTheSelection()
    {
        var cs = SourceGate.ReadRepoPartials(Overlay);
        Assert.Equal(2, SourceGate.Count(cs, "CaptureGeometry.SelectionProblem(selection, _monitor)"));
        Assert.DoesNotContain("SelectionProblem(selection, CaptureGeometry.Intersect", cs);
    }

    /// <summary>
    /// 真机现读：截图工具条那 16 颗按钮在 UIA 树里全是裸「按钮」——图标做面而 <c>Content</c> ⛔ 文本，
    /// 只挂了 <c>ToolTip</c>（悬停说明⛔ 是可访问名）⇒ 辅助技术指不到任何东西，与 ✅P-260 那句
    /// 「屏幕上没有任何字念得出这个名字＝指不到东西」同一族。名字必须复用悬停那一句（⛔ 第二份文案），
    /// 且走同一颗 <c>LocText</c> 名单，换语言才跟着重贴。
    /// </summary>
    [Fact]
    public void BarButtonsExposeTheirTipAsAnAccessibleName()
    {
        var cs = SourceGate.Code(SourceGate.ReadRepoFile(ToolBar));
        Assert.Contains("LocText.SetMessage(button, ToolTipService.ToolTipProperty, tip);", cs);
        Assert.Contains("LocText.SetMessage(button, AutomationProperties.NameProperty, tip);", cs);
        Assert.Contains("using Microsoft.UI.Xaml.Automation;", cs);
    }

    /// <summary>
    /// 普查（任务 #14）交回的另一宿主：画板工具条那两排纯图标按钮（五颗图形＋那一排色点）同属
    /// ✅P-265 那一族——<c>Content</c> 是一颗图标或一颗 <c>Border</c>，只挂了 ToolTip ⇒ UIA 里念不出。
    /// 那颗档自己的注释写着"名字与手势只能靠状态行说"，那说的是<b>肉眼</b>那一半（条子两行高、
    /// ToolTip 被宿主窗边界钉住）；辅助技术那一半得有自己的名字，否则 Tab 到这儿只念得出"按钮"。
    /// 名字复用同一颗键（图形那一颗还带嵌套实参），⛔ 立第二份文案。
    /// <para>P-288 图标化把 XAML 里那排动作钮也换成了记号：它们走同一条登记路——<c>Iconize</c>
    /// 一处实现（贴面＋登记名字），每颗只交键；嵌参那四颗与「⌨」照旧各自登记，
    /// 整档取词仍排在 <c>Refresh</c> 体内那一发。</para>
    /// <para><b>这颗窗口给的机制⛔ 是 <c>LocText</c></b>：<c>CanvasBarWordGateTests</c> 按整档禁
    /// <c>LocText.SetMessage</c>（那条线另有判据），而重贴节拍已经由 <c>Locale.Changed += Refresh</c> 送到位
    /// ⇒ 建条时只登记没渲染的那句，取词排在 <c>Refresh</c> 体内那一发。
    /// 钉子落在<b>体内</b>而不是全档在场：写在构造里＝把字符串提前拼死，换语言它一个字都不动。</para>
    /// </summary>
    [Fact]
    public void CanvasToolbarIconButtonsExposeAnAccessibleNameToo()
    {
        var cs = SourceGate.Code(SourceGate.ReadRepoFile(CanvasToolbar));
        Assert.Equal(2, SourceGate.Count(cs, "_names[button] = LocMessage.Of("));
        Assert.Contains("_names[button] = LocMessage.Of(colour.NameKey);", cs);
        Assert.Contains("_names[button] = LocMessage.Of(key, LocMessage.Of(tool.NameKey()));", cs);
        var refresh = SourceGate.MethodBody(cs, "private void Refresh()");
        Assert.Equal(1, SourceGate.Count(cs, "AutomationProperties.SetName("));
        Assert.Contains("AutomationProperties.SetName(button, message.Render());", refresh);
        // P-288：整排动作钮一条窄路——贴面＋登记名字一处实现，每颗只交键（嵌参那四颗与「⌨」不在这一排里）
        var build = SourceGate.MethodBody(cs, "private void BuildActionIcons()");
        Assert.Equal(12, SourceGate.Count(build, "Iconize("));
        var iconize = SourceGate.MethodBody(cs, "private void Iconize(Button target, UIElement icon, string nameKey)");
        Assert.Contains("target.Content = icon;", iconize);
        Assert.Contains("_names[target] = LocMessage.Of(nameKey);", iconize);
        // 「⌨」是记号不是词，名字登记的还是那颗说明键（⛔ 第二份文案）
        Assert.Contains("_names[KeysButton] = LocMessage.Of(\"canvas.bar.tip.keys\");", cs);
    }
}
