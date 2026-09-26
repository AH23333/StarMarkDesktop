#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using StarMark.Integrations.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
// "按下这一想改什么"的枚举归模型（Core.Capture）所有：判定与取值同源，界面不再自己列一份
// （原来那份私有 enum 就是让"拖动一行字变成放大字号"测不到的一半原因——政策在界面，测试引不到）。
using Grab = StarMark.Core.Capture.AnnotationGrab;

namespace StarMark.UI.Views;

/// <summary>
/// 选中已画的标注：点住拖动改位置，四角拖改大小，顶上那颗转方向。
/// <b>与同目录其余 CaptureOverlayWindow.*.cs 是同一个类</b>（partial，按「一件一个文件」拆开，不是新抽象层）。
/// </summary>
public sealed partial class CaptureOverlayWindow
{
    // ────────── 选中：点住拖动改位置，四角拖改大小，顶上那颗转方向 ──────────

    /// <summary>当前选中的那一条（下标会因撤销/删除而失效，一律现取，不缓存引用）。</summary>
    private Annotation? Selected =>
        _selected is { } i && i < _history.Count ? _history.Marks[i] : null;

    /// <summary>
    /// 这一按是不是"抓住已有的那一条"。顺序＝<b>旋转把手 → 四角（缩放）→ 框内（移动）</b>：
    /// 把手就画在框的边角上，反过来先判框内，四角会被"移动"整锅吃掉。
    /// <para>命中框内时连选中一起改。Snipaste 那套"先点一下选中、再点一下才拖"在这里会变成
    /// 用户点了两下没反应（第一下被吃掉了看不见），"点住就拖"一次就成。</para>
    /// </summary>
    private bool TryBeginGrab(PixelPoint local, Pointer pointer)
    {
        // 正在打字、或正在钉折线顶点时，这一按有它自己的含义（落笔/钉点），不能被"抓住上一条"抢走：
        // 抢走就等于把刚打的一行字丢在半路——那是批次 RD-1 刚堵掉的那类丢字路径。
        if (_editingText || _polyLine is not null) return false;
        // 批次 PV（真机反馈"点击已编辑内容要能直接选中"）：没有选中也先做命中测试——
        // 点在已画的那条上＝选中并抓住它；点在空白处才放行给"挪框"。
        var fresh = !(_selected is { } i && i < _history.Count);
        var index = fresh
            ? AnnotationPainter.HitTest(_history.Marks, local, SlopInSource(SelectionSlop))
            : _selected;
        if (index is not { } idx) return false;
        var mark = _history.Marks[idx];

        _grab = mark.GrabAt(local, RotateHandle(mark), SlopInSource(MoveSlop));
        if (_grab == Grab.None) return false;

        // 按的是某一头的把手 ⇒ 钉住的那一点改到<b>对面</b>那头（模型算，界面不猜）：
        // 否则绕字块中心缩放会把左上角一起推出去，真机反馈就是"一缩放整行字和它的框都跑了"。
        _grabFresh = fresh;
        _selected = idx;
        _dragOriginal = _grab == Grab.Scale ? mark.WithScalePivotTowards(local) : mark;
        _dragAnchor = local;
        _dragLast = local;
        _underDrag = UnderDragBuffer(idx);
        _dragCanvas = _underDrag is { } under ? (byte[])under.Clone() : null;
        Root.CapturePointer(pointer);
        DrawSelectionHandles();
        return true;
    }

    /// <summary>
    /// 旋转把手落在哪儿。<b>顶边贴到画面上沿时把它挪进框内</b>：画在画面外面的那一按不属于本窗的
    /// "在画面里"那条链（截图态会被当成重新框选，贴图态干脆落在窗外），等于这颗永远点不到。
    /// <para><b>比较要在底图像素这一层做</b>：`box` 是选区内坐标（原点＝画面左上角），而 `_selection.Y`
    /// 是虚拟桌面坐标——拿桌面坐标当边界，副屏在主屏下方时那个不等式对每条标注都成立（把手永远被
    /// 塞进框内压住内容），副屏在主屏上方时又永不成立（贴顶的那颗画到窗外、点不到）。
    /// 原点在 0 时两种写法恰好同值，所以这条错只有多屏才露出来。</para>
    /// </summary>
    private PixelPoint RotateHandle(Annotation mark)
    {
        var box = mark.Bounds();
        var lift = SlopInSource(RotateHandleLift);
        if (box.Y - lift < 0) lift = Math.Min(box.Height / 2, lift);
        return new PixelPoint(box.X + box.Width / 2, box.Y - lift);
    }

    /// <summary>
    /// "除了被拖这条、其余都在原位"的那张底，拖动开始时算一次；之后每帧只做
    /// <b>一次整块复制 + 一条重画</b>。每帧从底图重烤全部标注的话，标注一多就又变成"跟不上手"
    /// 那一类（正是打码那条反馈的同一个成因）。
    /// </summary>
    private byte[]? UnderDragBuffer(int excluding)
    {
        if (_base is not { } basePixels || _selection is not { }) return null;
        var rest = new List<Annotation>(_history.Marks);
        rest.RemoveAt(excluding);
        // 与 Rebake 同一口径：尺寸跟着底图走，不跟选区（贴图态那是显示尺寸）走。
        try { return AnnotationPainter.Render(basePixels, _contentWidth, _contentHeight, rest); }
        catch (Exception ex)
        {
            StarLog.Error("[CaptureOverlay] 拖动底图准备失败", ex);
            return null;
        }
    }

    /// <summary>拖动过程中：算出这一帧该长什么样，画进预览缓冲，再跟着更新选择框。</summary>
    private void DragTo(PixelPoint local)
    {
        _dragLast = local;
        if (_dragOriginal is not { } original || _grab == Grab.None) return;
        var preview = original.DraggedBy(_dragAnchor, local, _grab);
        if (_dragCanvas is { } canvas && _underDrag is { } under
            && _preview is { } previewBitmap && _selection is { } selection)
        {
            Buffer.BlockCopy(under, 0, canvas, 0, under.Length);
            try
            {
                AnnotationPainter.Paint(canvas, _contentWidth, _contentHeight, preview);
            }
            catch (Exception ex)
            {
                // 预览画不出来不当场说，用户就要到松手才发现这一改是坏的
                ShowError("这一改画不出来：" + ex.Message);
                return;
            }
            // 截图态的拖动底是整帧缓冲，逐事件上传会跟不上手：节流到 ~60fps，
            // 松手时 EndDrag 的 Rebake 会整帧收准，这里漏掉的那几帧不会丢东西。
            if (Environment.TickCount64 - _lastDragPaint >= 16)
            {
                _lastDragPaint = Environment.TickCount64;
                using (var stream = previewBitmap.PixelBuffer.AsStream())
                    stream.Write(canvas, 0, canvas.Length);
                previewBitmap.Invalidate();
            }
        }
        DrawSelectionHandles(preview);
    }

    // 拖动的算式本身在模型里（Annotation.DraggedBy），界面不再自己 switch：
    // 那一版读的是可变字段 _grab，而松手那一步会先把它清零 ⇒ "拖位置"在松手瞬间被算成"拖倍数"
    // （真机反馈"拖动文字会变大"，日志原件：[AnnoGrab] Move 跟着 [AnnoDrag] Move scale 1.000→6.000）。

    /// <summary>松手：把这一改落进历史（算一步，撤销能回去）。没真的动过就不制造一步空历史。</summary>
    private void EndDrag()
    {
        var original = _dragOriginal;
        var index = _selected;
        var grab = _grab;
        _dragOriginal = null;
        _underDrag = null;
        _dragCanvas = null;
        _grab = Grab.None;
        var fresh = _grabFresh;
        _grabFresh = false;
        if (original is null || index is not { } i || grab == Grab.None) return;
        var result = original.DraggedBy(_dragAnchor, _dragLast, grab);
        if (result == original)
        {
            DrawSelectionHandles();
            // 按住的是已经写好的那行字、按下到松手几乎没有移动 ⇒ 这是"点回去改它"（真机期望：
            // 随时可以点击之前编辑的文字，在编辑框里继续删减修改）。真拖过了就还是上一条语义＝移动位置，
            // 不该在这种时候弹框。<b>首次选中的那一按不算</b>（批次 PV，用户口径"文字框需要继续点击才可
            // 继续编辑"）：点一下＝选中，再点一下才进编辑。
            if (!fresh && grab == Grab.Move && original.Tool == AnnotationTool.Text &&
                Annotation.Near(_dragAnchor, _dragLast, SlopInSource(SelectionSlop))) BeginTextEdit(_dragAnchor, original, i);
            return;
        }

        _history.ReplaceAt(i, result);
        Rebake();
        DrawSelectionHandles();
    }

    /// <summary>
    /// 选择框 + 把手。传 <paramref name="mark"/> 时画的是"正在拖的那一条"的新位置。
    /// <para>名字刻意与 <see cref="DrawSelection(IntRect)"/> 分开：那个画的是"框选出来的哪一块"，
    /// 这个画的是"选中的哪一条标注"，两件事共用一个名字迟早会有人调错。</para>
    /// </summary>
    private void DrawSelectionHandles(Annotation? mark = null)
    {
        LiveLayer.Children.Clear();           // 先清再画：把手只有几颗，叠两层就会糊成一团黑方块
        mark ??= Selected;
        if (mark is null)
        {
            SelectionTip.Visibility = Visibility.Collapsed;
            TextDeleteButton.Visibility = Visibility.Collapsed;
            return;
        }
        var box = mark.Bounds();
        var (left, top) = LocalToDip(new PixelPoint(box.X, box.Y));
        var (right, bottom) = LocalToDip(new PixelPoint(box.Right, box.Bottom));
        LiveLayer.Children.Add(Out(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)));
        foreach (var corner in mark.Corners())
        {
            var (x, y) = LocalToDip(corner);
            LiveLayer.Children.Add(Fill(x - 3, y - 3, 6, 6, Ink));
        }
        // 每一类标注都给旋转把手：文字的旋转走"字模覆盖率再铺回去"那条路（见 GdiTextDrawer），
        // 与几何类一样能被像素断言钉住，所以不必再对文字单独关一档。
        var (hx, hy) = LocalToDip(RotateHandle(mark));
        LiveLayer.Children.Add(new Line { X1 = hx, Y1 = hy, X2 = hx, Y2 = top, Stroke = Ink, StrokeThickness = 1 });
        LiveLayer.Children.Add(Fill(hx - 3, hy - 3, 6, 6, Ink));
        // 文字标注的 ✕（用户口径："编辑框右上角为X号，可以点击删除该文字编辑框"）。
        // 只有文字给：形状类删起来没有"框住的是哪行字"的歧义，Delete 键与橡皮都够用。
        if (mark.Tool == AnnotationTool.Text)
        {
            TextDeleteButton.Margin = new Thickness(Math.Max(0, right - 6), Math.Max(0, top - 6), 0, 0);
            TextDeleteButton.Visibility = Visibility.Visible;
        }
        else TextDeleteButton.Visibility = Visibility.Collapsed;
        SelectionTip.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 丢掉选中（连带把手与那句说明）。
    /// <para><b>历史一变就必须调</b>：撤销/重做/清空/删除都会移动下标，留着旧下标就成了
    /// "框指着矩形、下一个拖动改的是椭圆"。拖动状态也一起收：那些缓冲是按旧下标算的。</para>
    /// </summary>
    private void DropSelection()
    {
        // 正在改的那一条被历史移动带走了：留住下标就等于把这一笔字写进"另一条"里（最坏的一种静默改错）
        if (_editingText && _editingIndex is not null) EndTextEditing(commit: false);
        _editingIndex = null;
        _selected = null;
        _grab = Grab.None;
        _grabFresh = false;
        _dragOriginal = null;
        _dragLast = default;
        _underDrag = null;
        _dragCanvas = null;
        LiveLayer.Children.Clear();
        SelectionTip.Visibility = Visibility.Collapsed;
        TextDeleteButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// "这一按没拖出形状"（点了一下就走）原来会被静默丢掉——真机反馈的"点了没反应"就是它。
    /// 现在用它去选中脚下那条：已有的标注于是有一个不需要额外模式、也不会挡住画新东西的出口。
    /// <para>只在 <see cref="Annotation.MinPoints"/> 判定为"画不成"的那条链上触发，所以打码（点一下
    /// 就该糊掉一格）与文字（点一下就该出输入框）的语义完全没动。</para>
    /// </summary>
    private void SelectAtTap(PixelPoint at)
    {
        var index = AnnotationPainter.HitTest(_history.Marks, at, SlopInSource(SelectionSlop));
        _selected = index;
        DrawSelectionHandles();               // 没点中时它也负责把手与说明一起收掉
    }

    /// <summary>删掉选中的那一条（Delete / Backspace）。算一步，撤销能拿回来。</summary>
    private void DeleteSelected()
    {
        if (_selected is not { } index) return;
        _history.RemoveAt(index);
        DropSelection();
        Rebake();
    }

    /// <summary>
    /// 拿底图把所有标注重烤一遍，并把结果摆成选区那块画面。
    /// <para>重烤而不是增量叠画：撤销、清空、改顺序这些操作就都不需要反向运算，
    /// 而"部分成功"（撤销了一条却残留半条）这类缺陷也结构上不可能出现。</para>
    /// <para>结果同时留一份在 <c>_composed</c>：下一条打码要以"已经画成的这张"为起点做增量预览
    /// （见 <see cref="StartMosaicScratch"/>），而<b>提交仍以这一次全烤为准</b>——
    /// 拖动中的增量只负责跟手，绝不会变成最终输出的第二条口径。</para>
    /// </summary>
    private void Rebake()
    {
        if (_base is not { } basePixels || _preview is not { }) return;
        // 正在改的那一条先不烤进画面：输入框就压在它原来的位置上，两份同时画出来
        // 就是真机反馈的"编辑中文字和已编辑文字重叠，红白两层"。落笔/取消后它自然回来。
        var marks = _editingText && _editingIndex is { } hidden && hidden < _history.Count
            ? _history.Marks.Where((_, i) => i != hidden).ToList()
            : _history.Marks;
        try
        {
            // 渲染尺寸取底图自己的尺寸，<b>不取选区</b>：贴图态"选区"＝窗口的显示尺寸（＝底图 × 倍率），
            // 按它渲染就是"缓冲比声明的尺寸短，画上去会越界"——真机反馈"标注没能画上去"的那条报信。
            var composed = AnnotationPainter.Render(basePixels, _contentWidth, _contentHeight, marks);
            // 压暗烤之前先把"帧＋标注、无压暗"的一份推给平面缓冲：改框/重拖期间显示它＋XAML 压暗跟随，
            // 拖动零重烤（见 ShowFlatWhileDragging）；松手 Rebake 烤准后由 RestoreComposedVisual 切回。
            if (!_pinned && _flatPreview is { } flat)
            {
                using (var flatStream = flat.PixelBuffer.AsStream())
                    flatStream.Write(composed, 0, composed.Length);
                flat.Invalidate();
            }
            // 截图态把选区外的压暗烤进合成图（批次 PU）：标注要能越出选区显示，
            // XAML 压暗层会把它盖掉；提交时裁的是选区内那一块，天然不含压暗。
            if (!_pinned && _selection is { } hole)
                BitmapTransform.DimOutside(composed, _contentWidth, _contentHeight, hole, 0x66);
            using (var stream = _preview.PixelBuffer.AsStream())
                stream.Write(composed, 0, composed.Length);
            _preview.Invalidate();
            _composed = composed;
            _scratch = null;
            RestoreComposedVisual();
        }
        catch (Exception ex)
        {
            // 画不上去必须看得见：交出一张"少了刚画的那条"的图，用户完全没有办法发现
            StarLog.Error("[CaptureOverlay] 合成标注失败", ex);
            ShowError("标注没能画上去：" + ex.Message);
            return;
        }
        ErrorChip.Visibility = Visibility.Collapsed;
        _undoButton.IsEnabled = _history.CanUndo;
        _redoButton.IsEnabled = _history.CanRedo;
        _clearButton.IsEnabled = _history.Count > 0;
    }

    /// <summary>拖动中的那一条用简单图元近似显示；松手立刻换成真像素。</summary>
    private void DrawLive()
    {
        LiveLayer.Children.Clear();
        if (_polyLine is { Count: > 0 } vertices)
        {
            DrawPolyLinePreview(vertices);
            return;
        }
        if (_stroke is not { Count: > 0 } points) return;
        var brush = new SolidColorBrush(ToColor(ColourBgra));
        var thickness = Math.Max(1.0, ThicknessForTool / _scale);
        var first = LocalToDip(points[0]);

        switch (_strokeTool)
        {
            case AnnotationTool.Rectangle:
            {
                var last = LocalToDip(points[^1]);
                AddShape(new Rectangle { Stroke = brush, StrokeThickness = thickness },
                    Math.Min(first.X, last.X), Math.Min(first.Y, last.Y),
                    Math.Abs(last.X - first.X), Math.Abs(last.Y - first.Y));
                return;
            }

            case AnnotationTool.Ellipse:
            {
                var last = LocalToDip(points[^1]);
                AddShape(new Ellipse { Stroke = brush, StrokeThickness = thickness },
                    Math.Min(first.X, last.X), Math.Min(first.Y, last.Y),
                    Math.Abs(last.X - first.X), Math.Abs(last.Y - first.Y));
                return;
            }

            case AnnotationTool.Arrow:
            {
                var head = Annotation.ArrowBarbs(
                    points[0], points[^1], ThicknessForTool)
                    .Select(barb => LocalToDip(barb)).ToList();
                AddLine(brush, thickness, first, LocalToDip(points[^1]));
                if (head.Count == 3)
                {
                    AddLine(brush, thickness, head[0], head[1]);
                    AddLine(brush, thickness, head[1], head[2]);
                }
                return;
            }
        }

        var polyline = new Polyline { Stroke = brush, StrokeThickness = thickness };
        foreach (var point in points)
        {
            var (x, y) = LocalToDip(point);
            polyline.Points.Add(new Point(x, y));
        }
        LiveLayer.Children.Add(polyline);
    }

    /// <summary>正在点的折线：已定的段实线、最后一顶点到光标那段半透明，顶点各摆一颗白点
    /// （不画顶点就分不清"这里断了一段"与"这里只是一笔经过"）。</summary>
    private void DrawPolyLinePreview(IReadOnlyList<PixelPoint> vertices)
    {
        var thickness = Math.Max(1.0, ThicknessForTool / _scale);
        var corners = vertices.Select(LocalToDip).ToList();
        if (corners.Count >= 2) LiveLayer.Children.Add(Band(corners, Ink, thickness));
        var trailing = new List<(double X, double Y)>(corners) { LocalToDip(_hoverLocal) };
        if (trailing.Count >= 2) LiveLayer.Children.Add(Band(trailing, InkDim, thickness));
        foreach (var corner in corners)
            LiveLayer.Children.Add(Placed(Fill(corner.X - 2, corner.Y - 2, 4, 4, Ink), corner.X - 2, corner.Y - 2));
    }

    private static Polyline Band(IReadOnlyList<(double X, double Y)> pts, Brush brush, double thickness)
    {
        var line = new Polyline { Stroke = brush, StrokeThickness = thickness };
        foreach (var p in pts) line.Points.Add(new Point(p.X, p.Y));
        return line;
    }

    private void AddShape(Shape shape, double x, double y, double w, double h)
    {
        Canvas.SetLeft(shape, x);
        Canvas.SetTop(shape, y);
        shape.Width = Math.Max(1, w);
        shape.Height = Math.Max(1, h);
        LiveLayer.Children.Add(shape);
    }

    private void AddLine(Brush brush, double thickness, (double X, double Y) from, (double X, double Y) to)
    {
        var line = new Line { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = brush, StrokeThickness = thickness };
        LiveLayer.Children.Add(line);
    }

}
