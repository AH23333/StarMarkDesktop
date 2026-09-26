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
/// 贴图态：移动、缩放、穿透、角标、悬停出条，以及旋转 / 翻转 / 透明度 / 右键菜单。
/// <b>与同目录其余 CaptureOverlayWindow.*.cs 是同一个类</b>（partial，按「一件一个文件」拆开，不是新抽象层）。
/// </summary>
public sealed partial class CaptureOverlayWindow
{
    // ────────── 贴图态：移动、缩放、穿透、角标、悬停出条 ──────────

    /// <summary>
    /// 没选笔时按下拖动＝移动整张图。存"按下时的窗口矩形 + 光标物理坐标"两份快照，
    /// 每帧从快照重算绝对位置——用增量累加会抖（与 MZ 那条抖动教训同一口径）。
    /// </summary>
    private void BeginPinDrag(Pointer pointer)
    {
        WindowInterop.GetCursorPos(out _gestureStartCursor);
        _gestureStartRect = WindowInterop.GetWindowRect(this);
        _draggingPin = true;
        Root.CapturePointer(pointer);
    }

    private void PinDragTo()
    {
        WindowInterop.GetCursorPos(out var cursor);
        // 全程物理像素：光标坐标与窗口矩形同一单位，不需要 DPI 因子，
        // 于是"拖到另一块缩放不同的屏上就越来越偏"这一类错误结构上不存在。
        var x = _gestureStartRect.X + cursor.X - _gestureStartCursor.X;
        var y = _gestureStartRect.Y + cursor.Y - _gestureStartCursor.Y;
        // 可以拖出屏幕去看想看的部分，但不许整块丢光（与缩放共用同一条收边判据）
        var (cx, cy) = CaptureGeometry.PinOrigin(
            x, y, _gestureStartRect.Width, _gestureStartRect.Height, WorkArea());
        if (cx == _lastAppliedX && cy == _lastAppliedY) return;
        _lastAppliedX = cx;
        _lastAppliedY = cy;
        SetMonitor(new IntRect(cx, cy, _monitor.Width, _monitor.Height));
        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), IntPtr.Zero, cx, cy, 0, 0,
            WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE);
        // 换到一块缩放不同的屏：物理尺寸不用动（我们本来就按物理像素摆窗），但 DIP↔物理的除数变了，
        // 不重算就是"拖到另一台显示器上画面突然比窗口大/小一圈，笔也落在偏的地方"。
        if (RefreshScaleIfChanged()) RelayoutContent();
    }

    /// <summary>
    /// 摆这张贴图的新位置/新尺寸。<b>贴图态的"选区"恒等于本窗矩形</b>，所以改 <see cref="_monitor"/>
    /// 必须连它一起改：`ToLocal`／`LocalToDip`／`InsideSelection`／`PositionBar` 全以选区为原点，
    /// 留着旧矩形就等于把笔迹按"拖过的那段距离"整体平移一遍——拖完一次就再也画不到点上，
    /// 而且 `InsideSelection` 会一路报 false，连"这一按是画"都判不出来。
    /// </summary>
    private void SetMonitor(IntRect next)
    {
        _monitor = next;
        if (_pinned) _selection = next;
    }

    /// <summary>
    /// 重量一次本窗所在屏的缩放，变了就更新 <see cref="_scale"/> 并回报 true。
    /// <para>截图态不需要（每屏一窗，窗不会跨屏走）；贴图态会被用户拖到别的屏上，而那条链上
    /// 每一处 DIP 都除以这个数——`SetWindowPos` 之后 WinUI 会按新屏的缩放重新排布局，
    /// 除数留在旧屏的值，画面与窗口就对不上了。</para>
    /// </summary>
    private bool RefreshScaleIfChanged()
    {
        var next = WindowInterop.GetScale(this);
        if (next <= 0 || Math.Abs(next - _scale) < 0.001) return false;
        _scale = next;
        return true;
    }

    /// <summary>滚轮＝缩放这张图（贴图态专属；截图态滚轮没有意义）。</summary>
    private void Root_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!_pinned) return;
        // 手上有未完成的一笔时不改倍率/透明度：那些点是按旧倍率换算的，中途改等于让正在画的那一笔跑偏，
        // 而"跑偏"只在松手合成之后才看得见（那时已经退不掉）。
        if (_stroke is not null || _polyLine is not null || _dragOriginal is not null
            || _draggingPin || _editingText) return;
        var delta = e.GetCurrentPoint(Root).Properties.MouseWheelDelta;
        // Shift+滚轮＝整窗透明度（Snipaste 同款）
        if (IsShiftDown())
        {
            SetOpacity(Math.Clamp(_opacity + (delta > 0 ? 0.1 : -0.1), 0.1, 1d));
            e.Handled = true;
            return;
        }
        var next = CaptureGeometry.NextZoom(_zoom, delta);
        if (Math.Abs(next - _zoom) < 0.0001) { SyncBadge(); return; }   // 已在端点：窗不动，角标仍要说清现在几倍
        _zoom = next;
        _sourceScale = next;
        ResizePinAnchoringTopLeft();
        e.Handled = true;
    }

    /// <summary>
    /// 改尺寸时<b>钉住左上角</b>再按 <see cref="CaptureGeometry.PinOrigin"/> 收边（PJ 口径）：
    /// 绕中心缩放会让整块图跑出屏幕再也回不来。
    /// </summary>
    private void ResizePinAnchoringTopLeft()
    {
        var current = WindowInterop.GetWindowRect(this);
        var (w, h) = CaptureGeometry.PinPixelSize(_contentWidth, _contentHeight, _zoom);
        // 钉住的是<b>图</b>的左上角；窗口＝图 + 下面那一条（条子在画面外），收边按整窗尺寸算。
        SetMonitor(new IntRect(current.X, current.Y, w, h));
        ApplyPinWindowRect();         // 只算物理像素：尺寸、收边、落位、_lastApplied 一起
        RefreshScaleIfChanged();      // 收边可能把这张图整个推到另一块屏上
        RelayoutContent();            // DIP 那一层（内容摆位 + 条子）按新缩放重摆，里面会再校一次窗口
    }

    /// <summary>窗口矩形变了（缩放）之后重摆内容与工具条：底图与预览的像素没动，动的只是显示尺寸。</summary>
    private void RelayoutContent()
    {
        if (_selection is not { } s) return;
        var (x, y, w, h) = ToDip(s);
        Canvas.SetLeft(AnnotateShot, x);
        Canvas.SetTop(AnnotateShot, y);
        AnnotateShot.Width = w;
        AnnotateShot.Height = h;
        PositionBar(s);
        DrawSelectionHandles();
        SyncBadge();
    }

    /// <summary>
    /// 贴图态的角标：倍率，以及"已穿透"这件事必须留在图上（这张窗收不到鼠标时，
    /// 用户若不知道 F5 就只剩"托盘关掉全部"这一条粗路）。复用截图时那颗尺寸提示 <c>SizeChip</c>——
    /// 它本来就是"这块画面现在是什么样"的说明位，另起一块只会多一处要维护的浮层。
    /// <para>摆在<b>右下</b>：这条工具条在贴图态只能压在画面顶部，左上一个角标会把最右那几颗挡住。</para>
    /// </summary>
    private void SyncBadge()
    {
        if (!_pinned) return;
        SizeChip.HorizontalAlignment = HorizontalAlignment.Right;
        SizeChip.VerticalAlignment = VerticalAlignment.Bottom;
        SizeChip.Margin = new Thickness(0, 0, 1, 1);
        SizeText.Text = CaptureGeometry.FormatZoom(_zoom) + (_clickThrough ? " · 已穿透，按 F5 恢复" : "");
        // 100% 且没穿透＝刚贴上的原样，不必顶一个角标挡画面
        SizeChip.Visibility = Math.Abs(_zoom - 1.0) < 0.0001 && !_clickThrough
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>当前是否鼠标穿透（名册用它决定托盘勾选项的勾选态）。</summary>
    public bool IsClickThrough => _clickThrough;

    /// <summary>
    /// 套用穿透状态并回报有没有真的套上。<b>失败不能静默</b>：那时用户看到的是"点了没反应"。
    /// <para>开成穿透时把工具条收掉：这张窗此后收不到鼠标，那条悬停才收起的条子会一直压在画面上，
    /// 而用户已经没有第二颗按钮能把它点掉（F5 恢复后再移进来就会重新出现）。</para>
    /// </summary>
    public bool ApplyClickThrough(bool on)
    {
        var ok = WindowInterop.SetClickThrough(this, on);
        _clickThrough = ok ? on : _clickThrough;
        if (_clickThrough)
        {
            ActionBar.Visibility = Visibility.Collapsed;
            PinBorder.Visibility = Visibility.Collapsed;    // 穿透中的窗收不到鼠标，悬停高亮也永远等不来退出
        }
        SyncBadge();
        return ok;
    }

    /// <summary>
    /// 放回屏幕（F4 显示全部）。显示走"原生 ShowWindow 兜一遍"：批次 D4 量过 WinUI 的显示调用
    /// 在桌面窗 owned 的那层关系上并不可靠，贴图窗同样挂在桌面上，不该指望另一条路径。
    /// </summary>
    public void Present()
    {
        try
        {
            var hwnd = WindowInterop.GetHwnd(this);
            WindowInterop.ShowWindow(hwnd, WindowInterop.SW_SHOWNOACTIVATE);
            Activate();
        }
        catch (Exception ex) { StarLog.Warn($"[Pin] 唤回贴图失败：{ex.Message}"); }
    }

    public void HidePin()
    {
        try { WindowInterop.ShowWindow(WindowInterop.GetHwnd(this), WindowInterop.SW_HIDE); }
        catch (Exception ex) { StarLog.Warn($"[Pin] 隐藏贴图失败：{ex.Message}"); }
    }

    /// <summary>工具条平时收起（一屏十几张贴图就十几条横杠，会盖住画面），鼠标进窗即现。</summary>
    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!_pinned) return;
        ActionBar.Visibility = Visibility.Visible;
        PinBorder.Visibility = Visibility.Visible;      // 高亮边框：标出"这块画面是一张贴图"（真机点名缺它）
    }

    /// <summary>
    /// 鼠标离开就收起——但<b>正在用笔的时候不许收</b>：选了笔、正在打字、折线还没收口、已选中某条要拖，
    /// 这些时候条子半路消失比看不见更烦（真机反馈里"工具条自己没了"就是这类收起时机错的形状）。
    /// </summary>
    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_pinned || Armed || _editingText || _polyLine is not null || _selected is not null) return;
        // 条子<b>常驻</b>：批次 WD-6 起窗口本身为它留了下面那一条，鼠标离开再把条子收走
        // 就只剩一条"看不见、但会吃掉鼠标"的空带——比原先压在画上更糟。高亮边框仍按悬停给，
        // 它标的是"这块画面是一张贴图"，收走不损失任何出口。
        PinBorder.Visibility = Visibility.Collapsed;
    }

    /// <summary>本屏工作区（物理像素，已扣任务栏）：选区挪动与改大小都只能在它里面。</summary>
    private IntRect WorkArea()
    {
        var a = WindowInterop.GetWorkArea(this);
        return new IntRect(a.X, a.Y, a.Width, a.Height);
    }

    /// <summary>开始改框。按下时的选区与按下点都是快照，之后每一帧从这两份快照<b>重算</b>绝对矩形，
    /// 不累加增量（贴图那条抖动教训同一口径）。只在边/角与"未拿笔的框内"两条路上被调（见 Root_PointerPressed）。</summary>
    private void BeginAdjust(CaptureGeometry.SelectionEdge edge, PointInt32 physical, Pointer pointer)
    {
        if (_selection is not { } s) return;
        // 改框是一次模式切换：打了一半的字先落笔保住（输入框开着挪框，字悬在半路最难收拾）
        EndTextEditing(commit: true);
        _adjust = edge;
        _adjustStart = s;
        _adjustPending = s;
        _adjustPress = physical;
        Root.CapturePointer(pointer);
        ApplyCursor();
    }

    private void AdjustTo(PointInt32 physical)
    {
        var work = WorkArea();
        var (dx, dy) = (physical.X - _adjustPress.X, physical.Y - _adjustPress.Y);
        _adjustPending = _adjust == CaptureGeometry.SelectionEdge.Move
            ? CaptureGeometry.MoveSelection(_adjustStart, dx, dy, work)
            : CaptureGeometry.ResizeSelection(_adjustStart, _adjust, dx, dy, work, CaptureGeometry.MinSelectionSide);
        // 选区活更新（批次 PU）：标注跟屏走、不再随选区平移。压暗此时交给 XAML 实时跟随
        //（内容源切到无压暗的平面缓冲）——拖动全程零整帧重烤，松手再烤准（真机反馈的拖框卡顿）。
        _selection = _adjustPending;
        DrawSelection(_adjustPending);
        ShowFlatWhileDragging();
    }

    private void EndAdjust()
    {
        _adjust = CaptureGeometry.SelectionEdge.None;
        _selection = _adjustPending;
        DrawSelection(_selection.Value);
        PositionBar(_selection.Value);
        // 节流可能吞掉拖动中的最后一帧：收尾整帧重烤一次，压暗与选区严格对齐
        Rebake();
        ApplyCursor();
    }

    /// <summary>
    /// 框选确认（批次 PU 两阶段模型的分界线）：候选点一下、拖框松手、双击取整屏都汇到这一处。
    /// 确认之后工具条才出现；已画的标注<b>不清</b>——它们跟屏走，新的选区只是重新画了个"裁剪记号"。
    /// </summary>
    private void ConfirmSelection()
    {
        if (_frame is null || _base is null || _selection is not { } selection) return;
        _annotating = true;
        EnterEditing(_base, _contentWidth, _contentHeight, selection);
    }

    private void BeginStroke(PixelPoint local, Pointer pointer, AnnotationTool tool)
    {
        ErrorChip.Visibility = Visibility.Collapsed;
        // 上一行字先落笔再动手：在画布上点第二下不该把刚打的字凭空清掉（真机反馈"文字编辑无效"的路径之一）。
        if (tool == AnnotationTool.Text)
        {
            // 落笔必须排在命中<b>之前</b>：真机反馈"第二次编辑时已经输入了文字，点旧字之后刚打的字直接没了"，
            // 原因是这里原先按"先命中、再丢弃正在打的那一行"走（commit: false＝丢），而丢弃并不等于"那条不存在"。
            // 命中也不能先算：落笔那一步可能删掉一条（把一条改成空字＝删那条），先算好的下标就会指着隔壁那条，
            // 于是"改这一行"变成"把字写进另一行"——最坏的一种静默改错。
            EndTextEditing(commit: true);
            // 点在已经写好的那行字上＝回去改它（真机期望"随时可以点击之前编辑的文字继续删减修改"），
            // 点在空白处＝新写一行。命中判据用模型里那一条（与"点一下就选中"同一个式子），不另算一套。
            var hit = AnnotationPainter.HitTest(_history.Marks, local, SlopInSource(SelectionSlop));
            if (hit is { } index && _history.Marks[index].Tool == AnnotationTool.Text)
            {
                _selected = index;
                DrawSelectionHandles();
                BeginTextEdit(local, _history.Marks[index], index);
                return;
            }
            BeginTextEdit(local);
            return;
        }
        // 非文字工具：上一行字先落笔再动手（真机反馈"文字编辑无效"的路径之一）。
        EndTextEditing(commit: true);
        // 起新的一笔就不再指着上一条了：选择框留在原地会挡住看新画的形状，下标也会变成误导
        DropSelection();
        // 打码的"一笔"从按下那一下就该看见：同一格糊掉与"还没糊"对用户是两个完全不同的结果，
        // 所以起点先按"一个点画两遍"存（MosaicBrush 走的是段，两个重合的点正好糊掉笔尖那一格）。
        _stroke = tool == AnnotationTool.Mosaic
            ? new List<PixelPoint> { local, local }
            : new List<PixelPoint> { local };
        if (tool == AnnotationTool.Mosaic) StartMosaicScratch(local);
        Root.CapturePointer(pointer);
        PaintPreview();
    }

    /// <summary>
    /// 开一条打码：以"已提交的那张合成图"为起点，之后每帧只往上补新走过的那一段。
    /// <b>不再每帧从底图重烤整张</b>——那正是真机反馈"打码速度远落后于鼠标移动速度"的成因：
    /// 一次重烤的代价 ∝ 选区面积 × 已有标注条数，手一快就落在后面。
    /// </summary>
    private void StartMosaicScratch(PixelPoint local)
    {
        _scratch = _composed is { } composed ? (byte[])composed.Clone() : null;
        _scratchTail = local;
    }

    /// <summary>把 _scratch 推到屏幕。整块缓冲上传是 memcpy，节流只为挡住每秒上百次的重复上传。</summary>
    private void FlushMosaicScratch()
    {
        if (_scratch is not { } scratch || _preview is not { } preview) return;
        try
        {
            using var stream = preview.PixelBuffer.AsStream();
            stream.Write(scratch, 0, scratch.Length);
            preview.Invalidate();
        }
        catch (Exception ex)
        {
            StarLog.Error("[CaptureOverlay] 打码预览上传失败", ex);
            ShowError("打码预览没能刷到屏幕上：" + ex.Message);
        }
    }

    /// <summary>拖动中的预览：画笔/荧光/形状走近似图元，打码走真像素（增量补段）。</summary>
    private void PaintPreview()
    {
        if (_strokeTool != AnnotationTool.Mosaic)
        {
            DrawLive();
            return;
        }
        if (_stroke is not { Count: > 1 } points || _scratch is not { } scratch
            || _base is not { } || _preview is null || _selection is not { } selection) return;
        // 先补段（每帧都做，代价只与"这一帧走了多远"成正比），再按节奏上传整块缓冲：
        // 少上传一次不会丢东西——下一帧会把之前画的段一起带上去。
        var tail = points[^1];
        if (tail != _scratchTail)
        {
            AnnotationPainter.Paint(scratch, _contentWidth, _contentHeight,
                new Annotation(AnnotationTool.Mosaic, new[] { _scratchTail, tail }, ColourBgra, ThicknessForTool));
            _scratchTail = tail;
        }
        if (Environment.TickCount64 - _lastMosaicPreview < 16) return;
        _lastMosaicPreview = Environment.TickCount64;
        FlushMosaicScratch();
    }

    private void ExtendStroke(PixelPoint local)
    {
        if (_stroke is not { Count: > 0 } points) return;
        if (points[^1] == local) return;                 // 鼠标不动也会反复回调：重复点会让折线的点数爆掉
        // 两点工具（矩形/椭圆/直线/箭头）的形状只由"按下那点"与"放开那点"决定：中途的采样是走过的痕迹，
        // 覆盖掉而不是追加。留着它们，一条矩形会在历史里带着几十个点，而任何按"第二个点"取另一端的写法
        // 都会画出针尖大的框——预览取最后一点、落笔取第二点，就是"松手后图形变得非常小"的成因。
        if (points.Count > 1 && Annotation.IsTwoPointTool(_strokeTool)) points[^1] = local;
        else points.Add(local);
        PaintPreview();
    }

    private void EndStroke()
    {
        var points = _stroke;
        _stroke = null;
        LiveLayer.Children.Clear();
        if (points is null) return;
        if (points.Count < Annotation.MinPoints(_strokeTool))
        {
            // 点了一下没拖：以前是静默丢掉（真机反馈"点了没反应"），现在改成"选中脚下那一条"。
            // 打码（点一下糊一格）与文字（点一下出输入框）走不到这里——它们的那一下本来就够点数。
            SelectAtTap(points[0]);
            return;
        }
        var mark = new Annotation(_strokeTool, points, ColourBgra, ThicknessForTool);
        if (mark.Problem() is { } problem)
        {
            ShowError(problem);
            return;
        }
        _history.Add(mark);
        // 刚画完的那条就是接下来最想改的：不用再点一次就把它选中。
        // 打码**不**自动选中——它是"同一块区域反复补几笔"的面积笔刷，
        // 选中后"点住就拖"会变成移动整条，正对着 RE-2 修好的"点一下就糊一格"。
        _selected = mark.Tool == AnnotationTool.Mosaic ? null : _history.Count - 1;
        Rebake();
        DrawSelectionHandles();
    }

    // ────────── 贴图旋转 / 翻转 / 透明度 / 右键菜单 ──────────

    /// <summary>
    /// 90° 离散旋转（左/右）：源尺寸换轴的<b>精确像素重排</b>——没有插值糊化、没有填黑四角、
    /// 不需要任何窗口区域裁切。自由角度旋转（右键拖动）在 WinUI 上必然带着"四角填黑 +
    /// 区域裁切"两件套，真机反馈就是大面积黑背景；且外接矩形随角度变大，
    /// 过度旋转会把贴图顶出屏幕。按用户裁决只留四种姿态：左转 90° / 右转 90° / 水平翻转 / 垂直翻转。
    /// <para>先把当前标注合成进像素再转（旋转带着标注一起走，之后它们就是像素的一部分）；
    /// 显示尺寸＝新源尺寸 × 当前倍率，位置按 <see cref="CaptureGeometry.PinOrigin"/> 收边——
    /// 转完仍要整块可见，不许跑出屏幕。</para>
    /// </summary>
    private void BakeQuarterTurn(bool clockwise)
    {
        Rebake();
        if (_composed is not { } composed) return;
        var rotated = BitmapTransform.Rotate90(composed, _contentWidth, _contentHeight, clockwise);
        var (w, h) = CaptureGeometry.PinPixelSize(rotated.Width, rotated.Height, _zoom);
        var current = WindowInterop.GetWindowRect(this);
        // 转完仍要整块可见（PJ）：窗口＝图 + 下面那一条，收边按整窗算，图自己仍钉在窗口左上角
        SetMonitor(new IntRect(current.X, current.Y, w, h));
        ApplyPinWindowRect();
        BeginEditingExisting(rotated.Pixels, rotated.Width, rotated.Height);
        RefreshScaleIfChanged();
        SyncBadge();
    }

    private void BakeFlip(bool horizontal)
    {
        Rebake();
        if (_composed is not { } composed) return;
        var flipped = BitmapTransform.Flip(composed, _contentWidth, _contentHeight, horizontal);
        BeginEditingExisting(flipped, _contentWidth, _contentHeight);
        SyncBadge();
    }

    private void SetOpacity(double opacity)
    {
        _opacity = opacity;
        WindowInterop.SetWindowOpacity(this, opacity);
        SizeChip.Visibility = Visibility.Visible;
        SizeText.Text = $"不透明度 {opacity * 100:0}%";
    }

    private void ShowPinMenu(Point localDip)
    {
        var menu = new MenuFlyout();
        AddMenuItem(menu, "复制（Ctrl+C）", () => Commit(CommitAction.Copy));
        AddMenuItem(menu, "保存（Ctrl+S）", () => Commit(CommitAction.Save));
        AddMenuItem(menu, "识字", () => Commit(CommitAction.Ocr));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "缩放重置为 100%", ResetZoomTo1);
        var opacityMenu = new MenuFlyoutSubItem { Text = "不透明度" };
        foreach (var v in new[] { 1.0, 0.75, 0.5, 0.25 })
        {
            var value = v;
            opacityMenu.Items.Add(NewMenuItem($"{v * 100:0}%", () => SetOpacity(value)));
        }
        menu.Items.Add(opacityMenu);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "向左旋转 90°", () => BakeQuarterTurn(clockwise: false));
        AddMenuItem(menu, "向右旋转 90°", () => BakeQuarterTurn(clockwise: true));
        AddMenuItem(menu, "水平翻转", () => BakeFlip(horizontal: true));
        AddMenuItem(menu, "垂直翻转", () => BakeFlip(horizontal: false));
        AddMenuItem(menu, _clickThrough ? "取消鼠标穿透" : "鼠标穿透",
            () => ApplyClickThrough(!_clickThrough));
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuItem(menu, "关闭（Esc）", Close);
        menu.ShowAt(Root, localDip);
    }

    private static MenuFlyoutItem NewMenuItem(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private static void AddMenuItem(MenuFlyout menu, string text, Action action)
        => menu.Items.Add(NewMenuItem(text, action));

    private void ResetZoomTo1()
    {
        if (Math.Abs(_zoom - 1d) < 0.0001) { SyncBadge(); return; }
        _zoom = 1d;
        _sourceScale = 1d;
        ResizePinAnchoringTopLeft();
        SyncBadge();
    }
}
