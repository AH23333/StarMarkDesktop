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
/// 落墨：折线、标注模型进出、逐像素绘制、序号标注与橡皮。
/// <b>与同目录其余 CaptureOverlayWindow.*.cs 是同一个类</b>（partial，按「一件一个文件」拆开，不是新抽象层）。
/// </summary>
public sealed partial class CaptureOverlayWindow
{
    // ────────── 折线：点几下钉几个顶点 ──────────

    /// <summary>点一下加一个顶点。<b>直线只能一段，而"沿一条边界描一圈"是截图标注最常见的指示</b>
    /// （真机反馈点名缺它）。顶点是点出来的，所以它不进拖动那套 _stroke 状态。</summary>
    private void PlaceVertex(PixelPoint local)
    {
        ErrorChip.Visibility = Visibility.Collapsed;
        if (_polyLine is null)
        {
            DropSelection();                  // 开始钉新的顶点，就不该再指着上一条（把手也会被 DrawLive 抹掉）
            _polyLine = new List<PixelPoint> { local };
        }
        else if (_polyLine[^1] != local) _polyLine.Add(local);
        _hoverLocal = local;
        DrawLive();
    }

    /// <summary>收口折线。<paramref name="commit"/> 为 false 只用于 Esc 与换选区——
    /// 顶点不足两个不算一条（那只是一个点，画出来什么也指不了）。</summary>
    private void FinishPolyLine(bool commit)
    {
        var points = _polyLine;
        _polyLine = null;
        LiveLayer.Children.Clear();
        if (points is null) return;
        if (!commit || points.Count < Annotation.MinPoints(AnnotationTool.PolyLine)) return;
        var mark = new Annotation(AnnotationTool.PolyLine, points, ColourBgra, ThicknessForTool);
        if (mark.Problem() is { } problem)
        {
            ShowError(problem);
            return;
        }
        _history.Add(mark);
        _selected = _history.Count - 1;       // 与 EndStroke 同一口径：刚画完的那条立刻可挪/可缩放
        Rebake();
        DrawSelectionHandles();
    }

    // ────────── 标注：进入、拖动一条、合成 ──────────

    /// <summary>
    /// 就地编辑贴图的第一帧：底图＝贴图像素，选区＝整个窗口（＝贴图当前显示的那块矩形），
    /// 之后所有编辑动作与截图时<b>走的是同一批代码</b>（八种笔、文字、撤销重做、马赛克增量、复制存图识字）。
    /// 历史在这里清空：贴图拿的是一份<b>新</b>底图（旋转/翻转烘焙后同理），旧标注已经烤在像素里；
    /// 截图态确认选区不清历史（标注跟屏走），所以清不清是调用方的事，不放进 <see cref="EnterEditing"/>。
    /// </summary>
    private void BeginEditingExisting(byte[] pixels, int width, int height)
    {
        _selection = _monitor;
        _history.Reset();
        EnterEditing(pixels, width, height, _monitor);
        HintText.Text = string.Empty;
    }

    /// <summary>
    /// 把一份底图摆上屏幕并进入可编辑态。截图与贴图编辑共用这一句：两者唯一的差别是底图从哪来，
    /// 摆法、重烤、工具条定位、光标态完全一致——分成两份写迟早会有一处不同步（本项目已栽过四次）。
    /// <para>内容层永远<b>铺满整扇窗</b>（截图＝整屏帧、贴图＝整张贴图）：标注跟屏走、可以越出选区，
    /// 不再按选区裁一小块摆——选区外的压暗改在合成图里烤（XAML 压暗层会盖住越界的标注）。</para>
    /// </summary>
    private void EnterEditing(byte[] basePixels, int contentWidth, int contentHeight, IntRect selection)
    {
        HideMagnifier();
        ClearDetected();
        // <b>selection 必须在这里落进 _selection</b>：手动拖框那条路在拖动时已赋过值，
        // 但候选窗口点击把框直接递进来——不落的话 _selection 保持 null，
        // 之后每一按都判不出"在选区里"（真机反馈"点击候选无反应、只能一直拖框"就是它）。
        _selection = selection;
        _base = basePixels;
        _contentWidth = Math.Max(1, contentWidth);
        _contentHeight = Math.Max(1, contentHeight);
        if (_preview is null || _preview.PixelWidth != _contentWidth || _preview.PixelHeight != _contentHeight)
        {
            _preview = new WriteableBitmap(_contentWidth, _contentHeight);
            // 截图态多一份"帧＋标注、无压暗"的显示位图：改框/重拖期间切到它＋XAML 压暗跟随（零重烤）
            _flatPreview = _pinned ? null : new WriteableBitmap(_contentWidth, _contentHeight);
        }
        AnnotateShot.Source = _preview;
        var (x, y, w, h) = ToDip(_monitor);
        Canvas.SetLeft(AnnotateShot, x);
        Canvas.SetTop(AnnotateShot, y);
        AnnotateShot.Width = w;
        AnnotateShot.Height = h;
        AnnotateLayer.Visibility = Visibility.Visible;

        Rebake();
        SetBarVisible(true);
        PositionBar(selection);
        SyncTools();            // 条上不该有任何一颗看起来是选中的：确认选区后是"改框"那一态
        ApplyCursor();
        // 选区框<b>常驻</b>（真机反馈"框选会出现无框的情况"）：确认路径会路过 ClearDetected →
        // ClearSelection 把框收掉，这里必须重新画上；提示条也一并压回去。
        if (!_pinned)
        {
            DrawSelection(selection);
            HintChip.Visibility = Visibility.Collapsed;
        }
        _copyButton.Focus(FocusState.Programmatic);
    }

    // ────────── 绘制 ──────────

    private void ClearSelection()
    {
        SelRect.Visibility = Visibility.Collapsed;
        foreach (var dim in new[] { DimTop, DimLeft, DimRight, DimBottom })
            dim.Visibility = Visibility.Collapsed;
        SizeChip.Visibility = Visibility.Collapsed;
        HintChip.Visibility = Visibility.Visible;
    }

    private void DrawSelection(IntRect selection)
    {
        if (selection.Width <= 0 || selection.Height <= 0)
        {
            ClearSelection();
            return;
        }
        var (x, y, w, h) = ToDip(selection);
        var screenWidth = _monitor.Width / _scale;
        var screenHeight = _monitor.Height / _scale;

        SelRect.Visibility = Visibility.Visible;
        PlaceOnCanvas(SelRect, x, y, w, h);

        // 压暗层的分工（批次 PU/PV）：确认选区且不在拖框时，压暗烤在合成图里，XAML 这四块必须收起
        //（否则叠在烤好的压暗上再暗一遍，还会把越出选区的标注一起盖掉）；
        // 改框/重拖进行中内容源切到了无压暗的平面缓冲，四块重新出来实时跟随（零重烤，见 ShowFlatWhileDragging）。
        var dim = !_annotating || IsFrameDragging ? Visibility.Visible : Visibility.Collapsed;
        PlaceOnCanvas(DimTop, 0, 0, screenWidth, y);
        PlaceOnCanvas(DimBottom, 0, y + h, screenWidth, Math.Max(0, screenHeight - y - h));
        PlaceOnCanvas(DimLeft, 0, y, x, h);
        PlaceOnCanvas(DimRight, x + w, y, Math.Max(0, screenWidth - x - w), h);
        DimTop.Visibility = dim;
        DimBottom.Visibility = dim;
        DimLeft.Visibility = dim;
        DimRight.Visibility = dim;

        SizeChip.Visibility = Visibility.Visible;
        SizeText.Text = CaptureGeometry.FormatSize(selection.Width, selection.Height);
        PlaceByMargin(SizeChip, Math.Max(0, x), Math.Max(0, y - 22));
    }

    /// <summary>
    /// 悬停光标（批次 PU，用户口径："光标位于区域内时为十字箭头，位于边缘时为拉伸的双向箭头"）：
    /// 没拿笔时按光标落在选区的哪个部位给形状（框内＝移动、边/角＝拉伸、框外＝可以重新框一块）；
    /// 拿着笔＝十字准线（要落笔的地方得看得清）。形状没变就不重建，PointerMoved 每帧都路过这里。
    /// </summary>
    private void UpdateHoverCursor(PointInt32 physical)
    {
        if (_adjust != CaptureGeometry.SelectionEdge.None) return;   // 拖动中由 ApplyCursor 定
        var local = ToLocal(physical);
        // 选中了某条标注（批次 PV，用户口径"文字编辑时光标位于框内为十字箭头、左上/左下/右下
        // 为斜方向拉伸"）：框内（含边）＝移动，角点＝对应对角拉伸；文字的右上角是 ✕ 删除位，
        // 给普通箭头（其余工具四角都是缩放把手）。没点中标注才落到选区的那套形状。
        InputSystemCursorShape? OverSelectedMark()
        {
            if (_pinned || Armed || Selected is not { } mark) return null;
            var slop = SlopInSource(SelectionSlop);
            var box = mark.Bounds();
            foreach (var corner in mark.Corners())
            {
                if (!Annotation.Near(corner, local, slop)) continue;
                var north = Math.Abs(corner.Y - box.Y) <= Math.Abs(corner.Y - box.Bottom);
                var west = Math.Abs(corner.X - box.X) <= Math.Abs(corner.X - box.Right);
                if (mark.Tool == AnnotationTool.Text && north && !west) return InputSystemCursorShape.Arrow;
                return north == west
                    ? InputSystemCursorShape.SizeNorthwestSoutheast
                    : InputSystemCursorShape.SizeNortheastSouthwest;
            }
            if (local.X >= box.X - slop && local.X < box.Right + slop
                && local.Y >= box.Y - slop && local.Y < box.Bottom + slop) return InputSystemCursorShape.SizeAll;
            return null;
        }
        var shape = OverSelectedMark();
        if (shape is null)
        {
            if (!_annotating || Armed) shape = InputSystemCursorShape.Cross;
            else if (_selection is { } box)
            {
                shape = CaptureGeometry.SelectionEdgeAt(box, AsPixel(physical), SelectionSlop) switch
                {
                    CaptureGeometry.SelectionEdge.Left or CaptureGeometry.SelectionEdge.Right => InputSystemCursorShape.SizeWestEast,
                    CaptureGeometry.SelectionEdge.Top or CaptureGeometry.SelectionEdge.Bottom => InputSystemCursorShape.SizeNorthSouth,
                    CaptureGeometry.SelectionEdge.TopLeft or CaptureGeometry.SelectionEdge.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
                    CaptureGeometry.SelectionEdge.TopRight or CaptureGeometry.SelectionEdge.BottomLeft => InputSystemCursorShape.SizeNortheastSouthwest,
                    CaptureGeometry.SelectionEdge.Move => InputSystemCursorShape.SizeAll,
                    _ => InputSystemCursorShape.Cross,
                };
            }
            else shape = InputSystemCursorShape.Cross;
        }
        if (_lastCursor == shape) return;
        _lastCursor = shape;
        Root.Cursor = Microsoft.UI.Input.InputSystemCursor.Create(shape.Value);
    }

    /// <summary>
    /// 改框/重拖期间的显示切换（批次 PV，治真机反馈的"拖框明显卡顿"）：内容源切到
    /// <b>无压暗</b>的平面缓冲（<see cref="_flatPreview"/>），压暗交给 XAML 四块实时跟随——
    /// 拖动全程<b>零整帧重烤</b>。原先"每 16ms 烤一次 4K 整帧＋上传"会阻塞 UI 线程，
    /// 连 SelRect 的 XAML 更新一起卡。代价：拖动期间越出选区的标注被 XAML 压暗暂时盖住（瞬态）。
    /// </summary>
    private void ShowFlatWhileDragging()
    {
        if (_pinned || !_annotating || _flatPreview is null) return;
        if (!ReferenceEquals(AnnotateShot.Source, _flatPreview)) AnnotateShot.Source = _flatPreview;
    }

    /// <summary>把内容源切回<b>带压暗</b>的合成图（每次 Rebake 成功后调用）。已在位时不动。</summary>
    private void RestoreComposedVisual()
    {
        if (_pinned || _preview is null) return;
        if (!ReferenceEquals(AnnotateShot.Source, _preview)) AnnotateShot.Source = _preview;
    }

    /// <summary>贴图态那一侧的工具条窗（只在第一次摆位时创建；选区阶段一直是 <c>null</c>）。</summary>
    private CaptureBarWindow? _barWindow;

    /// <summary>
    /// 条子收起／展开的<b>唯一出口</b>：贴图被设为鼠标穿透、正在输入文字时条子是收着的，
    /// 那扇独立窗必须跟着藏——一扇量出来是 0 的空窗停在原处，看不见却会把那一块的鼠标吃掉。
    /// （WinUI 3 的 Border 没有 IsVisibleChanged 可订阅，所以收与展一律走这里，不留第二条路。）
    /// </summary>
    private void SetBarVisible(bool visible)
    {
        ActionBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _barWindow?.Reposition();
    }

    /// <summary>
    /// 把工具条搬去它自己那扇置顶窗。<b>幂等</b>：贴图态每次摆位都会走进来，但一扇贴图只搬一次
    /// （第二次它已经没有父元素可摘，条子也就再没有主人）。
    /// <para>
    /// 摘下来而不是再做一条：条上每颗按钮的 Click 直接握着本窗的标注状态（工具、颜色、粗细、
    /// 撤销重做、复制／存图／识字／✕）。再做一条就要在两扇窗之间架一层回调，两层事实迟早分岔，
    /// 分岔的样子是"条上亮着矩形、画出去的是画笔"。
    /// </para>
    /// </summary>
    private void AttachBarWindow()
    {
        if (_barWindow is not null) return;
        if (ActionBar.Parent is Panel host) host.Children.Remove(ActionBar);
        ActionBar.Margin = new Thickness(0);
        // 铺满那扇小窗（窗比它大的只有防取整的那一圈 slack）：留左/上对齐会在右下多出一条窗底色，
        // 而这条暗色边的圆角就是贴图的观感边界，不能被拆成两层。
        ActionBar.HorizontalAlignment = HorizontalAlignment.Stretch;
        ActionBar.VerticalAlignment = VerticalAlignment.Stretch;
        ActionBar.RenderTransform = null;
        _barWindow = new CaptureBarWindow(ActionBar, _scale);
        Closed += (_, _) => _barWindow?.Shutdown();
    }

    /// <summary>
    /// 贴图窗的窗口矩形＝<b>画面的矩形，一格不加</b>（只做 PJ 那条收边：塞得下就整块留屏内）。
    /// <para>
    /// 批次 WI 之前这里是"窗口 = 图 + 下面那一条（宽再让条子塞得下）"，那是按构造不成立的方案：
    /// WinUI 3 的客户区不能整块透明，画面只填满"图那一段"，为条子加高的那一条没有像素可画，
    /// 屏幕上就是一块黑（真机反馈："菜单栏还是和贴图同框，会造成局部黑块"）。
    /// 条子现在住它自己那扇置顶窗（<see cref="CaptureBarWindow"/>），这里只剩下"图放在哪"这一件事。
    /// </para>
    /// </summary>
    private IntRect PinWindowRect(IntRect image)
    {
        if (!_pinned) return image;
        var w = Math.Max(1, image.Width);
        var h = Math.Max(1, image.Height);
        var (x, y) = CaptureGeometry.PinOrigin(image.X, image.Y, w, h, WorkArea());
        return new IntRect(x, y, w, h);
    }

    /// <summary>
    /// 把窗口摆成 <see cref="PinWindowRect"/> 算出来的那块画面矩形。<b>只在贴图态调用</b>，
    /// 且是这条链上唯一的窗口尺寸写点（尺寸与位置一起定，避免"先缩小再收边"两步各自收边）——
    /// 缩放、90° 旋转都从这里过，所以它顺带叫上条子（<see cref="FollowBarToImage"/>）。
    /// </summary>
    private void ApplyPinWindowRect()
    {
        var win = PinWindowRect(_monitor);
        var now = WindowInterop.GetWindowRect(this);
        if (now.X == win.X && now.Y == win.Y && now.Width == win.Width && now.Height == win.Height) return;
        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), IntPtr.Zero, win.X, win.Y, win.Width, win.Height,
            WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE);
        _lastAppliedX = win.X;
        _lastAppliedY = win.Y;
        FollowBarToImage();
    }

    /// <summary>
    /// 让条子跟上画面现在的位置/大小。<b>只问窗口实际矩形</b>——画面跟到哪儿，条子跟到哪儿。
    /// <para>真机反馈："菜单栏已和贴图分离，但无法随着贴图位置变化而改变位置"：平移贴图那条路
    /// （<c>PinDragTo</c>）自己 <c>SetWindowPos</c> 之后就结束了，只有"换了缩放不同的屏"才会顺带重摆条子，
    /// 于是拖完贴图，条子留在原地。条子与贴图是两扇窗，<b>谁移动谁得叫上它</b>，
    /// 所以贴图几何的每一个写点都必须过这里。</para>
    /// <para>这里刻意不走整条 <see cref="PositionBar"/>：拖动每一帧都进来，那条会连带
    /// <c>UpdateLayout</c> 与窗内那一层的重新摆位（拖动期禁整帧重排，PV/WG 的教训）。</para>
    /// </summary>
    private void FollowBarToImage()
    {
        if (!_pinned || _barWindow is null) return;
        var now = WindowInterop.GetWindowRect(this);
        _barWindow.Place(new IntRect(now.X, now.Y, now.Width, now.Height), WorkArea());
    }

    /// <summary>
    /// 条子的<b>内容</b>换了（选择栏开／收）之后重摆一次那扇窗。
    /// <para>真机反馈："截图贴图的所有菜单功能的二级菜单均会被菜单的高度限制遮挡，有时无法显示或只部分显示"。
    /// 根因不是算错高度，而是<b>没人来算</b>：搬进条子窗之后 <c>ActionBar</c> 是 <c>Stretch</c> 的
    /// （<see cref="AttachBarWindow"/>），父窗只给它窗内那 33 像素，于是它<b>永远量不出"我变高了"</b>——
    /// <c>SizeChanged</c> 不响，那扇窗就一直停在旧高度上把选择栏那一行截在窗外。
    /// 选区阶段没有这个问题（条子住在全屏遮罩窗里，长多少看得见），所以只有贴图态需要这一句。</para>
    /// <para>与 <see cref="FollowBarToImage"/> 同一类错，只是通知点从"几何写点"换成了"内容写点"：
    /// <b>两扇窗之间没有自动同步，谁改了谁得叫上它。</b></para>
    /// </summary>
    private void ReflowBar()
    {
        if (_pinned) FollowBarToImage();
    }

    /// <summary>
    /// 摆工具条。选区阶段：摆在这扇全屏窗里、选区下方（放不下就上方），左右都夹进本屏。
    /// 贴图阶段：搬进它自己那扇置顶窗（<see cref="CaptureBarWindow"/>，批次 WI），跟着画面走。
    /// <para>尺寸按<b>量出来的</b> ActualWidth/Height 算：三行都是代码生成的，按写死的数字摆
    /// 一旦加个工具就会压住选区或掉到屏外。</para>
    /// </summary>
    private void PositionBar(IntRect selection)
    {
        ActionBar.UpdateLayout();
        var barWidth = ActionBar.ActualWidth > 0 ? ActionBar.ActualWidth : Bar_fallback_width;
        var barHeight = ActionBar.ActualHeight > 0 ? ActionBar.ActualHeight : Bar_fallback_height;
        var (x, y, w, h) = ToDip(selection);
        if (_pinned)
        {
            // 贴图态：条子住它自己那扇置顶窗（批次 WI）。先把窗口按画面矩形校正一次（缩放／拖动／
            // 旋转之后都从这条走），再拿<b>窗口实际矩形</b>去摆条子——画面跟到哪儿，条子跟到哪儿。
            ApplyPinWindowRect();
            AttachBarWindow();
            var now = WindowInterop.GetWindowRect(this);
            _barWindow!.Place(new IntRect(now.X, now.Y, now.Width, now.Height), WorkArea());
            // 悬停高亮那一圈只描画面：窗口此刻就是画面，描边跟着画面走
            PinBorder.HorizontalAlignment = HorizontalAlignment.Left;
            PinBorder.VerticalAlignment = VerticalAlignment.Top;
            PinBorder.Width = w;
            PinBorder.Height = h;
            return;
        }
        var screenWidth = _monitor.Width / _scale;
        var screenHeight = _monitor.Height / _scale;
        // 摆位判据在模型里（批次 WQ）：贴图态那条工具条走的是同一个函数，两处不再各写一份。
        // 这一态的坐标是窗口内 DIP，条子长高是同一句 ActualHeight 量出来的，判上下与摆放用同一个数。
        var (barX, barY) = CaptureGeometry.BarOrigin(
            new IntRect(ToInt(x), ToInt(y), ToInt(w), ToInt(h)),
            new IntRect(0, 0, ToInt(screenWidth), ToInt(screenHeight)),
            ToInt(barWidth), ToInt(barHeight), ToInt(barHeight));
        PlaceByMargin(ActionBar, barX, barY);
    }

    /// <summary>几何判据一律吃整数（物理像素或整 DIP），界面这边只负责把量到的尺寸舍入过去。</summary>
    private static int ToInt(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static void PlaceOnCanvas(FrameworkElement element, double x, double y, double w, double h)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        element.Width = Math.Max(0, w);
        element.Height = Math.Max(0, h);
        element.Visibility = w <= 0 || h <= 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Grid 里的浮层元素：靠 Left/Top 对齐 + Margin 定位（Canvas 的附加属性在这里不起作用）。</summary>
    private static void PlaceByMargin(FrameworkElement element, double x, double y)
        => element.Margin = new Thickness(Math.Max(0, x), Math.Max(0, y), 0, 0);

    private void ShowError(string reason)
    {
        ErrorText.Text = reason;
        ErrorChip.Visibility = Visibility.Visible;
    }

    // ────────── 序号标注 ──────────

    private void PlaceNumber(PixelPoint local)
    {
        // 编号取"现存最大号 + 1"而不是自增计数器：撤销/删除最大那颗之后再点，Snipaste 的手感是
        // 接着已有的号往下走（画 1,2,3 → 撤销掉 3 → 再点仍是 3），全局计数器会让它跳成 4。
        var next = 1;
        foreach (var existing in _history.Marks)
            if (existing.Tool == AnnotationTool.Number) next = Math.Max(next, existing.Number + 1);
        var mark = new Annotation(AnnotationTool.Number, new[] { local }, ColourBgra, 2)
        {
            Number = next,
        };
        if (mark.Problem() is not null) { return; }
        EndTextEditing(commit: true);
        DropSelection();
        _history.Add(mark);
        Rebake();
    }

    // ────────── 橡皮擦 ──────────

    private void BeginEraseStroke(PixelPoint local, Pointer pointer)
    {
        EndTextEditing(commit: true);
        DropSelection();
        _erasing = true;
        _eraseRemoved.Clear();
        _history.BeginErase();
        Root.CapturePointer(pointer);
        EraseTo(local);
    }

    private void EraseTo(PixelPoint local)
    {
        var changed = false;
        while (true)
        {
            var hit = AnnotationPainter.HitTest(_history.Marks, local, SlopInSource(SelectionSlop));
            if (hit is not int index) break;
            _eraseRemoved.Add(_history.Marks[index]);
            _history.ApplyErase(_eraseRemoved);
            changed = true;
        }
        if (changed) Rebake();
    }

    private void EndEraseStroke()
    {
        _erasing = false;
        _history.EndErase();
        _eraseRemoved.Clear();
        Rebake();
    }

}
