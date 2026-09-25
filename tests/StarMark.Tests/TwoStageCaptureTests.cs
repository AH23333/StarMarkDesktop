#nullable enable
using System;
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
    private const string OverlayXaml = "src/StarMark.UI/Views/CaptureOverlayWindow.xaml";

    // ────────── 结构闸门（UI 源码扫描） ──────────

    /// <summary>框外那一按，拿着笔＝落笔（可越出选区）。框内/框外两条路共用一个分发，
    /// 所以"BeginToolStroke(physical, …)"必须恰好出现两次——少了哪一处，哪一侧就还在走老路。</summary>
    [Fact]
    public void DrawingMayLeaveTheSelectionBounds()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);
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
        var cs = SourceGate.ReadRepoFile(Overlay);
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
        var final = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay),
            "private (byte[] Pixels, int Width, int Height)? FinalPixels()");
        Assert.Contains("BitmapTransform.Crop(composed, _contentWidth, _contentHeight,", final);
        Assert.Contains("_annotating", final);           // 只在确认过选区后走裁剪路
        Assert.Contains("ScreenshotService.TryCrop(frame", final);   // 未确认（Enter 直提交）的兜底仍在
    }

    /// <summary>压暗烤进合成图：XAML 那四块压暗在确认选区后必须收起（否则叠暗一遍、盖掉越界标注）。</summary>
    [Fact]
    public void DimIsBakedIntoTheComposedFrame()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);
        Assert.Contains("BitmapTransform.DimOutside(composed, _contentWidth, _contentHeight, hole, 0x66);",
            SourceGate.MethodBody(cs, "private void Rebake()"));
        var draw = SourceGate.MethodBody(cs, "private void DrawSelection(IntRect selection)");
        Assert.Contains("_annotating ? Visibility.Collapsed : Visibility.Visible", draw);
    }

    /// <summary>识别（悬停建议）阶段不出现菜单栏：自动检测与放大镜只在"未确认选区"时活动，
    /// 工具条只在 EnterEditing（确认）里亮出来。</summary>
    [Fact]
    public void TheToolbarAppearsOnlyAfterConfirmation()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);
        Assert.Contains("if (!_pinned && !_annotating)",
            SourceGate.MethodBody(cs, "private void Root_PointerMoved"));
        var enter = SourceGate.MethodBody(cs, "private void EnterEditing(");
        Assert.Contains("ActionBar.Visibility = Visibility.Visible;", enter);
        Assert.Contains("ToDip(_monitor)", enter);       // 内容层铺满整扇窗，不按选区摆
    }

    /// <summary>截图态 Esc 两级：拿着笔先收笔（回到可改框的那一态），再按才取消整场。</summary>
    [Fact]
    public void EscDisarmsThePenBeforeCancelingTheShot()
    {
        var keys = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void Root_KeyDown");
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
        var cs = SourceGate.ReadRepoFile(Overlay);
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
        var edit = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void BeginTextEdit");
        Assert.Contains("rotated.Rotation != 0", edit);
        Assert.Contains("Angle = rotated.Rotation", edit);
    }

    /// <summary>悬停光标按选区部位给形状（用户口径："光标位于区域内时为十字箭头，位于边缘时为拉伸的双向箭头"）。</summary>
    [Fact]
    public void HoverCursorFollowsTheSelectionEdge()
    {
        var hover = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void UpdateHoverCursor");
        Assert.Contains("CaptureGeometry.SelectionEdgeAt(box, AsPixel(physical), SelectionSlop)", hover);
        Assert.Contains("InputSystemCursorShape.SizeAll", hover);            // 框内＝十字箭头
        Assert.Contains("InputSystemCursorShape.SizeNorthwestSoutheast", hover);   // 边/角＝双向箭头
        Assert.Contains("InputSystemCursorShape.Cross", hover);              // 拿笔/框外＝十字准线
    }

    /// <summary>整帧才是底图（截图态）：构造时就按整屏定内容尺寸，选区只是"裁到哪里"的记号。</summary>
    [Fact]
    public void TheWholeFrameIsTheBase_NotTheSelection()
    {
        var ctor = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "ScreenFrame frame,");
        Assert.Contains("_base = shot.Pixels;", ctor);
        Assert.Contains("_contentWidth = _monitor.Width;", ctor);
        Assert.Contains("_contentHeight = _monitor.Height;", ctor);
    }

    /// <summary>拖动已有标注的预览要节流：截图态的拖动底是整帧缓冲，逐鼠标事件上传会跟不上手。</summary>
    [Fact]
    public void TheDragPreviewUploadIsThrottled()
    {
        var drag = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void DragTo");
        Assert.Contains("_lastDragPaint", drag);
        Assert.Contains(">= 16", drag);
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
        Assert.Throws<ArgumentException>(() => BitmapTransform.Crop(src, 4, 4, 4, 4, 2, 2));
        Assert.Throws<ArgumentException>(() => BitmapTransform.Crop(new byte[3], 4, 4, 0, 0, 1, 1));
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
        Assert.Throws<ArgumentException>(() =>
            BitmapTransform.DimOutside(new byte[3], 4, 4, new IntRect(0, 0, 1, 1), 0x66));
    }
}
