#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

namespace StarMark.UI.Views;

/// <summary>
/// 截图遮罩窗：显示"按下热键那一刻"的整屏帧，让用户拖动框选，然后<b>就地标注</b>，
/// 最后复制或存图或贴图或识字。
/// <para>
/// 生命周期是<b>一次性</b>的：每次截图新建每屏一个窗、结束即关，不留常驻窗池（D3 裁决）。
/// 常驻隐藏窗池能省几十毫秒首帧，代价是永远有一批顶层窗口挂在每台显示器上。
/// </para>
/// <para>
/// <b>每条退出路径都必须把会话收干净</b>：漏一条就等于把用户摁在一层吃满屏幕的顶层窗里，
/// 只能去任务管理器杀进程。因此 Esc / 右键 / 点取消 / 窗口被外部关闭 都走同一个
/// <see cref="_finish"/> 回调（<see cref="Settle"/> 保证只回调一次），由服务统一关窗。
/// </para>
/// <para>
/// 标注这一层的设计要点：<b>预览显示的就是真的会被交出去的那份像素</b>。
/// 松手即由 <see cref="AnnotationPainter"/> 把标注合成进选区底图，再把合成结果摆回原位；
/// 只有"正在拖、还没松手"的那一条用简单图元近似显示。这样"预览看着对、存出来不对"这一整类缺陷
/// 结构上就不成立，而撤销/清空也只是拿底图重烤一次，不需要任何反向操作。
/// </para>
/// </summary>
public sealed partial class CaptureOverlayWindow : Window
{
    // 工具条兜底尺寸：真实尺寸由 UpdateLayout 之后量到的 ActualWidth/Height 决定，
    // 这两个数只在"还没量出来"的那一帧用来避免把条摆到屏外。
    private const double Bar_fallback_width = 340;
    private const double Bar_fallback_height = 116;

    private readonly ScreenFrame _frame;
    private readonly IntRect _monitor;          // 本屏在虚拟桌面里的物理矩形
    private readonly double _scale;             // 本屏 DPI 缩放（1.0 / 1.25 / 1.5 …）
    private readonly Action<CaptureOverlayWindow, IntRect?> _finish;
    private readonly CaptureMode _mode;         // 放开选区后做什么（F1 给条 / F3 贴 / 识字直接复制）

    private readonly List<Annotation> _marks = new();

    private PointInt32 _startPhysical;
    private IntRect? _selection;                // 虚拟桌面物理像素
    private bool _awaitingRelease;
    private bool _settled;

    private byte[]? _base;                      // 选区那块底图（物理像素，合成时的固定起点）
    private WriteableBitmap? _preview;
    private List<PixelPoint>? _stroke;          // 正在拖、还没合成进去的那一条
    private bool _editingText;
    private PixelPoint _textAnchor;
    private AnnotationTool _tool = AnnotationTool.Rectangle;
    private int _colourIndex;
    private int _weightIndex = 1;

    /// <param name="monitorDevice">本窗负责的显示器设备名（失活时只认自己这屏的失活）。</param>
    /// <param name="mode">见 <see cref="CaptureMode"/>：<see cref="CaptureMode.Toolbar"/> 之外都放开即执行。</param>
    public CaptureOverlayWindow(
        ScreenFrame frame,
        (string Device, RectInt32 Bounds, double Scale) monitor,
        Action<CaptureOverlayWindow, IntRect?> finish,
        CaptureMode mode)
    {
        _frame = frame;
        _monitor = new IntRect(monitor.Bounds.X, monitor.Bounds.Y, monitor.Bounds.Width, monitor.Bounds.Height);
        _scale = monitor.Scale <= 0 ? 1.0 : monitor.Scale;
        _finish = finish;
        _mode = mode;
        DeviceName = monitor.Device;

        InitializeComponent();
        BuildToolStrip();
        HintText.Text = mode switch
        {
            CaptureMode.Pin => "按住拖动框选区域 · 放开即钉到桌面 · Enter 立即贴当前选区 · Esc 取消 · 右键不截",
            CaptureMode.Ocr => "按住拖动框选要认字的区域 · 放开即识别并把文字复制走 · Esc 取消 · 右键不截",
            _ => HintText.Text,
        };

        // 遮罩不需要主题：画面是抓来的桌面，文字全画在暗底上并用硬编码白色 —— 这里刻意不调
        // ThemeManager。套主题反而会把窗口的 ActualTheme 拉去影响按钮默认前景，出现"暗底灰字"。
        WindowInterop.RemoveDefaultWindowFrame(this);

        // 位置与尺寸走 Win32 物理像素：AppWindow 那套按 DIP 算，多屏混合 DPI 时每屏都会算偏。
        // SWP_NOACTIVATE：先就位再 Activate，避免用户看到窗口从别处滑过来。
        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), WindowInterop.HWND_TOPMOST,
            _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height,
            WindowInterop.SWP_SHOWWINDOW | WindowInterop.SWP_NOACTIVATE);

        if (BuildMonitorBitmap() is not { } bitmap)
        {
            ShowError("这一帧没能铺到屏幕上（内存不足或像素缓冲尺寸不符）——按 Esc 结束");
            return;
        }
        Shot.Source = bitmap;

        // 被外部关掉（Alt+F4、任务管理器"切到"、系统注销）也要按取消回报，否则会话永远挂着。
        Closed += (_, _) => Settle(null);
    }

    public string DeviceName { get; }

    /// <summary>当前选区（虚拟桌面物理像素）；还没框选时为 null。</summary>
    public IntRect? Selection => _selection;

    /// <summary>本屏的矩形（虚拟桌面物理像素）。</summary>
    public IntRect MonitorBounds => _monitor;

    // ────────── 会话收尾 ──────────

    /// <summary>把结果（null＝取消）交给服务；一个会话只交一次。</summary>
    private void Settle(IntRect? selection)
    {
        if (_settled) return;
        _settled = true;
        _finish(this, selection);
    }

    /// <summary>服务在统一收尾时调用：本窗已把结果交出去了，直接关。</summary>
    public void CloseWindow() => Close();

    /// <summary>用户按了 Esc / 右键 / 取消：服务据此撤销整场会话。</summary>
    public void CancelFromService() => Settle(null);

    private WriteableBitmap? BuildMonitorBitmap()
    {
        try
        {
            var (ox, oy) = CaptureGeometry.CropOffset(_monitor, _frame.Bounds);
            var pixels = GdiScreenCapture.Crop(new FrameCopyRequest(
                _frame, ox, oy, _monitor.Width, _monitor.Height));
            var bitmap = new WriteableBitmap(_monitor.Width, _monitor.Height);
            using (var stream = bitmap.PixelBuffer.AsStream())
                stream.Write(pixels, 0, pixels.Length);
            bitmap.Invalidate();
            return bitmap;
        }
        catch (Exception ex)
        {
            StarLog.Error($"[CaptureOverlay] 铺帧失败（{DeviceName}）", ex);
            return null;
        }
    }

    // ────────── 工具条（全部按 Core 的模型生成）──────────────────

    /// <summary>
    /// 按 <see cref="AnnotationTool"/> / <see cref="Annotation.Palette"/> /
    /// <see cref="Annotation.ThicknessSteps"/> 生成三行里的前两行。
    /// <para>
    /// 刻意不在 XAML 里手写这些按钮：手写就等于"有哪些工具"这件事存在两份事实，
    /// 于是"模型里加了个工具、工具条上没有"只能靠眼睛发现。现在它不可能发生。
    /// </para>
    /// </summary>
    private void BuildToolStrip()
    {
        foreach (AnnotationTool tool in Enum.GetValues<AnnotationTool>())
        {
            var button = new RadioButton
            {
                GroupName = "Tool",
                Content = Annotation.ToolName(tool),
                FontSize = 11,
                Tag = tool,
                IsChecked = tool == _tool,
            };
            ToolTipService.SetToolTip(button, Annotation.ToolHint(tool));
            button.Checked += Tool_Checked;
            ToolPanel.Children.Add(button);
        }

        for (var index = 0; index < Annotation.Palette.Count; index++)
        {
            var colour = Annotation.Palette[index];
            var swatch = new Rectangle
            {
                Width = 18,
                Height = 12,
                Fill = new SolidColorBrush(ToColor(colour.Bgra)),
                Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80)),
                StrokeThickness = 1,
            };
            var button = new RadioButton { GroupName = "Colour", Tag = index, Content = swatch, IsChecked = index == _colourIndex };
            ToolTipService.SetToolTip(button, colour.Name);
            button.Checked += Colour_Checked;
            ColourPanel.Children.Add(button);
        }

        for (var index = 0; index < Annotation.ThicknessSteps.Length; index++)
        {
            var button = new RadioButton
            {
                GroupName = "Weight",
                Tag = index,
                Content = Annotation.ThicknessNames[index],
                FontSize = 11,
                IsChecked = index == _weightIndex,
            };
            ToolTipService.SetToolTip(button, $"{Annotation.ThicknessNames[index]}（当前工具约 " +
                $"{Annotation.ThicknessFor(_tool, index)} 像素）");
            button.Checked += Weight_Checked;
            WeightPanel.Children.Add(button);
        }
    }

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AnnotationTool tool }) return;
        EndTextEditing(commit: true);   // 先落笔再换工具：用户打了一半的字不该凭空消失
        _tool = tool;
    }

    private void Colour_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index }) _colourIndex = index;
    }

    private void Weight_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index }) _weightIndex = index;
    }

    private int ColourBgra => Annotation.Palette[Math.Clamp(_colourIndex, 0, Annotation.Palette.Count - 1)].Bgra;

    private int ThicknessForTool => Annotation.ThicknessFor(_tool, _weightIndex);

    private static Color ToColor(int bgra) => Color.FromArgb(
        (byte)(bgra >>> 24), (byte)(bgra >> 16 & 0xFF), (byte)(bgra >> 8 & 0xFF), (byte)(bgra & 0xFF));

    // ────────── 鼠标：按下 → 拖动 → 放开 ──────────

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_settled) return;
        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed) return;

        var physical = ToPhysical(point.Position.X, point.Position.Y);
        // 已经有选区、这次按在选区里面、而且是"给动作条"的那条链 ⇒ 这一按是**画**，不是重新框选。
        // 选区外面照旧起新框：用户想换个范围就换个范围，标注跟着作废（底图都换了，留着旧的只会对不上）。
        if (_mode == CaptureMode.Toolbar && _base is not null && InsideSelection(physical))
        {
            BeginStroke(ToLocal(physical), e.Pointer);
            return;
        }

        _startPhysical = physical;
        _awaitingRelease = true;
        ResetAnnotations();
        _selection = null;
        HintChip.Visibility = Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Collapsed;
        ErrorChip.Visibility = Visibility.Collapsed;
        Root.CapturePointer(e.Pointer);      // 拖出窗口边界也要继续收到 Moved/Released
        DrawSelection(new IntRect(_startPhysical.X, _startPhysical.Y, 0, 0));
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_settled) return;
        var position = e.GetCurrentPoint(Root).Position;
        if (_stroke is not null)
        {
            ExtendStroke(ToLocal(ToPhysical(position.X, position.Y)));
            return;
        }
        if (!_awaitingRelease) return;
        var current = ToPhysical(position.X, position.Y);
        var selection = CaptureGeometry.Normalize(_startPhysical.X, _startPhysical.Y, current.X, current.Y);
        // 夹回本屏：L1 按"每屏各截各的"处理跨屏拖拽（每屏一个遮罩窗，各拿各的选区，互不合并）
        _selection = CaptureGeometry.Intersect(selection, _monitor) ?? selection;
        if (_selection is { } box) DrawSelection(box);
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_settled) return;
        if (_stroke is not null)
        {
            EndStroke();
            Root.ReleasePointerCapture(e.Pointer);
            return;
        }
        if (!_awaitingRelease) return;
        _awaitingRelease = false;
        Root.ReleasePointerCapture(e.Pointer);
        if (_selection is not { } selection) return;

        // 只点了一下没拖动：不是错误，静默清掉让用户再拖一次（"选区太小"的措辞留给真拖了但太窄的情况）
        if (selection.Width < CaptureGeometry.MinSelectionSide && selection.Height < CaptureGeometry.MinSelectionSide)
        {
            _selection = null;
            ClearSelection();
            return;
        }
        if (CaptureGeometry.SelectionProblem(selection, _monitor) is { } reason)
        {
            ShowError(reason);
            return;
        }
        // 贴图与识字：放开的这个动作本身就是答案，不必再让用户多点一次按钮（Snipaste 的 F3 同理）
        if (_mode != CaptureMode.Toolbar) Commit(_mode == CaptureMode.Pin ? CommitAction.Pin : CommitAction.Ocr);
        else EnterAnnotationMode(selection);
    }

    private void Root_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        Settle(null);                        // 右键＝这一屏不截（与 Snipaste 一致）
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;
                Settle(null);
                break;
            case VirtualKey.Enter:
                e.Handled = true;
                Commit(_mode switch
                {
                    CaptureMode.Pin => CommitAction.Pin,
                    CaptureMode.Ocr => CommitAction.Ocr,
                    _ => CommitAction.Copy,
                });
                break;
            case VirtualKey.S when IsControlDown():
                e.Handled = true;
                Commit(CommitAction.Save);
                break;
            case VirtualKey.Z when IsControlDown():
                e.Handled = true;
                Undo();
                break;
        }
    }

    // Windows.UI.Core 里也有 Point/Size，与上面 using Windows.Foundation 撞名 ⇒ 这里只能全限定
    private static bool IsControlDown()
        => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // ────────── 坐标换算 ──────────

    /// <summary>窗口内 DIP → 虚拟桌面物理像素（先乘本屏缩放，再加本屏原点）。</summary>
    private PointInt32 ToPhysical(double dipX, double dipY)
        => new(
            _monitor.X + (int)Math.Round(dipX * _scale, MidpointRounding.AwayFromZero),
            _monitor.Y + (int)Math.Round(dipY * _scale, MidpointRounding.AwayFromZero));

    /// <summary>虚拟桌面物理像素 → 窗口内 DIP（画选区与提示用）。</summary>
    private (double X, double Y, double W, double H) ToDip(IntRect selection)
        => ((selection.X - _monitor.X) / _scale,
            (selection.Y - _monitor.Y) / _scale,
            selection.Width / _scale,
            selection.Height / _scale);

    private bool InsideSelection(PointInt32 physical) => _selection is { } s
        && physical.X >= s.X && physical.X < s.Right && physical.Y >= s.Y && physical.Y < s.Bottom;

    /// <summary>虚拟桌面物理像素 → 选区内物理像素（标注模型用的坐标系，原点在选区左上角）。</summary>
    private PixelPoint ToLocal(PointInt32 physical)
        => new(physical.X - (_selection?.X ?? physical.X), physical.Y - (_selection?.Y ?? physical.Y));

    /// <summary>选区内物理像素 → 窗口内 DIP（摆预览图元与文字输入框用）。</summary>
    private (double X, double Y) LocalToDip(PixelPoint local)
    {
        var s = _selection ?? default;
        return ((s.X + local.X - _monitor.X) / _scale, (s.Y + local.Y - _monitor.Y) / _scale);
    }

    // ────────── 标注：进入、拖动一条、合成 ──────────

    /// <summary>框选放开后进入标注态：把这一块底图拿在手里，之后每改一条就在它上面重烤一次。</summary>
    private void EnterAnnotationMode(IntRect selection)
    {
        var (ox, oy) = CaptureGeometry.CropOffset(selection, _frame.Bounds);
        try
        {
            _base = GdiScreenCapture.Crop(new FrameCopyRequest(_frame, ox, oy, selection.Width, selection.Height));
        }
        catch (Exception ex)
        {
            StarLog.Error("[CaptureOverlay] 取选区底图失败", ex);
            ShowError("这一块画面的像素没能取到：" + ex.Message);
            return;
        }
        _marks.Clear();
        _preview = new WriteableBitmap(selection.Width, selection.Height);
        AnnotateShot.Source = _preview;
        var (x, y, w, h) = ToDip(selection);
        Canvas.SetLeft(AnnotateShot, x);
        Canvas.SetTop(AnnotateShot, y);
        AnnotateShot.Width = w;
        AnnotateShot.Height = h;
        AnnotateLayer.Visibility = Visibility.Visible;

        Rebake();
        ActionBar.Visibility = Visibility.Visible;
        PositionBar(selection);
        CopyButton.Focus(FocusState.Programmatic);
    }

    /// <summary>换选区或取消：标注与底图一起丢掉（留着旧的会和新的选区对不上）。</summary>
    private void ResetAnnotations()
    {
        EndTextEditing(commit: false);
        _stroke = null;
        _marks.Clear();
        _base = null;
        _preview = null;
        AnnotateLayer.Visibility = Visibility.Collapsed;
        LiveLayer.Children.Clear();
        UndoButton.IsEnabled = ClearButton.IsEnabled = false;
    }

    private void BeginStroke(PixelPoint local, Pointer pointer)
    {
        ErrorChip.Visibility = Visibility.Collapsed;
        if (_tool == AnnotationTool.Text)
        {
            BeginTextEdit(local);
            return;
        }
        _stroke = new List<PixelPoint> { local };
        Root.CapturePointer(pointer);
        DrawLive();
    }

    private void ExtendStroke(PixelPoint local)
    {
        if (_stroke is null || _stroke.Count == 0) return;
        if (_stroke[^1] == local) return;                 // 鼠标不动也会反复回调：重复点会让折线的点数爆掉
        _stroke.Add(local);
        DrawLive();
    }

    private void EndStroke()
    {
        var points = _stroke;
        _stroke = null;
        LiveLayer.Children.Clear();
        if (points is null) return;
        if (points.Count < Annotation.MinPoints(_tool)) return;   // 点了一下没拖：不是错误，静默丢掉
        var mark = new Annotation(_tool, points, ColourBgra, ThicknessForTool);
        if (mark.Problem() is { } problem)
        {
            ShowError(problem);
            return;
        }
        _marks.Add(mark);
        Rebake();
    }

    /// <summary>
    /// 拿底图把所有标注重烤一遍，并把结果摆成选区那块画面。
    /// <para>重烤而不是增量叠画：撤销、清空、改顺序这些操作就都不需要反向运算，
    /// 而"部分成功"（撤销了一条却残留半条）这类缺陷也结构上不可能出现。</para>
    /// </summary>
    private void Rebake()
    {
        if (_base is not { } basePixels || _preview is not { } preview || _selection is not { } selection) return;
        try
        {
            var composed = AnnotationPainter.Render(basePixels, selection.Width, selection.Height, _marks);
            using (var stream = preview.PixelBuffer.AsStream())
                stream.Write(composed, 0, composed.Length);
            preview.Invalidate();
        }
        catch (Exception ex)
        {
            // 画不上去必须看得见：交出一张"少了刚画的那条"的图，用户完全没有办法发现
            StarLog.Error("[CaptureOverlay] 合成标注失败", ex);
            ShowError("标注没能画上去：" + ex.Message);
            return;
        }
        ErrorChip.Visibility = Visibility.Collapsed;
        UndoButton.IsEnabled = ClearButton.IsEnabled = _marks.Count > 0;
    }

    /// <summary>拖动中的那一条用简单图元近似显示；松手立刻换成真像素。</summary>
    private void DrawLive()
    {
        LiveLayer.Children.Clear();
        if (_stroke is not { Count: > 0 } points) return;
        var brush = new SolidColorBrush(ToColor(ColourBgra));
        var thickness = Math.Max(1.0, ThicknessForTool / _scale);
        var first = LocalToDip(points[0]);
        var mosaicDiameter = Annotation.ThicknessFor(AnnotationTool.Mosaic, _weightIndex) / _scale;

        switch (_tool)
        {
            case AnnotationTool.Mosaic:
                // 打码的效果是"整块糊掉"，画一条线没有意义：只把笔刷大小显示出来就够了
                AddShape(new Ellipse
                {
                    Stroke = brush,
                    StrokeThickness = thickness,
                    Fill = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)),
                }, first.X - mosaicDiameter / 2, first.Y - mosaicDiameter / 2, mosaicDiameter, mosaicDiameter);
                return;

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

    // ────────── 文字标注：就地输入 ──────────

    private void BeginTextEdit(PixelPoint local)
    {
        var (x, y) = LocalToDip(local);
        _editingText = true;
        TextEditorHost.Margin = new Thickness(Math.Max(0, x), Math.Max(0, y), 0, 0);
        TextEditorHost.Visibility = Visibility.Visible;
        TextEditor.FontSize = Annotation.DefaultFontHeight / _scale;
        TextEditor.Foreground = new SolidColorBrush(ToColor(ColourBgra));
        TextEditor.Text = string.Empty;
        TextEditor.Focus(FocusState.Programmatic);
        _textAnchor = local;
    }

    /// <summary>
    /// 结束就地输入。<paramref name="commit"/> 为 false 只用于 Esc 与"换选区"——
    /// 点工具条别的按钮时**要落笔**，否则用户打了一半的字凭空消失，比多一条标注糟得多。
    /// </summary>
    private void EndTextEditing(bool commit)
    {
        if (!_editingText) return;
        _editingText = false;
        TextEditorHost.Visibility = Visibility.Collapsed;
        var text = TextEditor.Text.Trim();
        if (!commit || text.Length == 0) return;
        _marks.Add(new Annotation(AnnotationTool.Text, new[] { _textAnchor }, ColourBgra, ThicknessForTool)
        {
            Text = text,
            FontHeight = Annotation.DefaultFontHeight,
        });
        Rebake();
    }

    private void TextEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;             // 不能漏给 Root：那条链会变成"复制整张截图"
                EndTextEditing(commit: true);
                break;
            case VirtualKey.Escape:
                e.Handled = true;             // Esc 在输入框里＝丢掉这一行字，不是取消整场截图
                EndTextEditing(commit: false);
                break;
        }
    }

    private void TextEditor_LostFocus(object sender, RoutedEventArgs e) => EndTextEditing(commit: true);

    // ────────── 撤销 / 清空 ──────────

    private void Undo()
    {
        if (_marks.Count == 0) return;
        _marks.RemoveAt(_marks.Count - 1);
        Rebake();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _marks.Clear();
        Rebake();
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

        PlaceOnCanvas(DimTop, 0, 0, screenWidth, y);
        PlaceOnCanvas(DimBottom, 0, y + h, screenWidth, Math.Max(0, screenHeight - y - h));
        PlaceOnCanvas(DimLeft, 0, y, x, h);
        PlaceOnCanvas(DimRight, x + w, y, Math.Max(0, screenWidth - x - w), h);

        SizeChip.Visibility = Visibility.Visible;
        SizeText.Text = CaptureGeometry.FormatSize(selection.Width, selection.Height);
        PlaceByMargin(SizeChip, Math.Max(0, x), Math.Max(0, y - 22));
    }

    /// <summary>
    /// 把工具条摆到选区下方（放不下就摆到上方），左右都夹进本屏。
    /// <para>尺寸按<b>量出来的</b> ActualWidth/Height 算：三行都是代码生成的，按写死的数字摆
    /// 一旦加个工具就会压住选区或掉到屏外。</para>
    /// </summary>
    private void PositionBar(IntRect selection)
    {
        ActionBar.UpdateLayout();
        var barWidth = ActionBar.ActualWidth > 0 ? ActionBar.ActualWidth : Bar_fallback_width;
        var barHeight = ActionBar.ActualHeight > 0 ? ActionBar.ActualHeight : Bar_fallback_height;
        var (x, y, w, h) = ToDip(selection);
        var screenWidth = _monitor.Width / _scale;
        var screenHeight = _monitor.Height / _scale;
        var left = Math.Clamp(x + w - barWidth, 4, Math.Max(4, screenWidth - barWidth - 4));
        var below = y + h + 6 + barHeight <= screenHeight;
        PlaceByMargin(ActionBar, left, below ? y + h + 6 : Math.Max(4, y - barHeight - 6));
    }

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

    // ────────── 动作 ──────────

    private void Copy_Click(object sender, RoutedEventArgs e) => Commit(CommitAction.Copy);

    private void Save_Click(object sender, RoutedEventArgs e) => Commit(CommitAction.Save);

    private void Pin_Click(object sender, RoutedEventArgs e) => Commit(CommitAction.Pin);

    private void Ocr_Click(object sender, RoutedEventArgs e) => Commit(CommitAction.Ocr);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Settle(null);

    private enum CommitAction { Copy, Save, Pin, Ocr }

    /// <summary>
    /// 提交这一屏的选区：复制、存图、钉住，或认字并复制文字。四个落点交的都是<b>带上标注的那一份画面</b>。
    /// 先算好像素再 Settle：服务收到结果就会关掉所有遮罩窗（包括本窗），
    /// 反过来先干活会让用户在裁图期间还被困在暗幕里。
    /// </summary>
    private void Commit(CommitAction action)
    {
        if (_selection is not { } selection)
        {
            Settle(null);
            return;
        }
        if (CaptureGeometry.SelectionProblem(selection, _monitor) is { } reason)
        {
            ShowError(reason);
            return;
        }
        if (FinalPixels() is not { } final) return;       // 原因已经在里面报过了，这里只负责不再往下走
        Settle(selection);
        switch (action)
        {
            case CommitAction.Copy: _ = ScreenshotService.CopyPixelsAsync(final.Pixels, final.Width, final.Height); break;
            case CommitAction.Save: _ = ScreenshotService.SavePixelsAsync(final.Pixels, final.Width, final.Height); break;
            case CommitAction.Ocr: _ = OcrService.CopyTextFromPixelsAsync(final.Pixels, final.Width, final.Height); break;
            default: ScreenshotService.PinPixels(final.Pixels, final.Width, final.Height, selection); break;
        }
    }

    /// <summary>
    /// 要交出去的那份画面：有底图就在它上面重烤一次标注（连"一条都没画"也走这条路，结果就是原样），
    /// 拿不到底图时退回"直接从这一帧里裁"。两条路都报同样的失败原因，不静默少一张图。
    /// </summary>
    private (byte[] Pixels, int Width, int Height)? FinalPixels()
    {
        if (_selection is not { } selection) return null;
        if (_base is { } basePixels)
        {
            try
            {
                return (AnnotationPainter.Render(basePixels, selection.Width, selection.Height, _marks),
                    selection.Width, selection.Height);
            }
            catch (Exception ex)
            {
                StarLog.Error("[CaptureOverlay] 交图前合成标注失败", ex);
                ShowError("标注没能合成：" + ex.Message);
                return null;
            }
        }
        return ScreenshotService.TryCrop(_frame, selection);
    }
}
