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
// "按下这一想改什么"的枚举归模型（Core.Capture）所有：判定与取值同源，界面不再自己列一份
// （原来那份私有 enum 就是让"拖动一行字变成放大字号"测不到的一半原因——政策在界面，测试引不到）。
using Grab = StarMark.Core.Capture.AnnotationGrab;

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
    private const double Bar_fallback_width = 460;
    private const double Bar_fallback_height = 34;

    private readonly ScreenFrame _frame;
    private readonly IntRect _monitor;          // 本屏在虚拟桌面里的物理矩形
    private readonly double _scale;             // 本屏 DPI 缩放（1.0 / 1.25 / 1.5 …）
    private readonly Action<CaptureOverlayWindow, IntRect?> _finish;
    private readonly CaptureMode _mode;         // 放开选区后做什么（F1 给条 / F3 贴 / 识字直接复制）

    private readonly AnnotationHistory _history = new();

    private PointInt32 _startPhysical;
    private IntRect? _selection;                // 虚拟桌面物理像素
    private bool _awaitingRelease;
    private bool _settled;

    private byte[]? _base;                      // 选区那块底图（物理像素，合成时的固定起点）
    private WriteableBitmap? _preview;
    private byte[]? _composed;                  // 底图 + 全部已提交标注（每次重烤后留下，给拖动做起点）
    private byte[]? _scratch;                   // 正在拖的这条打码的画布：_composed 的副本 + 已走过的段
    private PixelPoint _scratchTail;            // _scratch 已经画到哪个点（下一段从这里接）
    private List<PixelPoint>? _stroke;          // 正在拖、还没合成进去的那一条
    /// <summary>上一次把整块缓冲推给屏幕的时刻（毫秒）。见 <c>PaintPreview</c>。</summary>
    private long _lastMosaicPreview;
    // ── 选中与拖动（批次 RE-3：画完还能挪位置、改大小、转方向）──
    private int? _selected;                   // 选中的那一条（画着选择框与把手）
    private Annotation? _dragOriginal;        // 按下时的原样：拖动只改副本，松手才落进历史
    private PixelPoint _dragAnchor;           // 按下那一点（选区内物理像素）
    private PixelPoint _dragLast;             // 最近一次光标位置（松手按它落定，与预览同一套判据）
    private byte[]? _underDrag;               // 拖动期间的底：底图 + 除被拖那条之外的全部标注（一次算好）
    private byte[]? _dragCanvas;              // 每帧复用：_underDrag 的副本 + 预览那一条
    private Grab _grab;                        // Grab ＝ 别名的 AnnotationGrab（判定与取值都在模型一处）

    /// <summary>"点一下"要不要算选中脚下那条的容差（物理像素）。比把手小：点是打在形状上，不是打在小方块上。
    /// <para>角点/把手的容差不在这里——那一条跟着形状尺寸收缩，住在模型里（<c>Annotation.HandleSlopFor</c>），
    /// 因为它能被断言。</para></summary>
    private const int SelectionSlop = 8;

    /// <summary>框内（＝移动）的容差（物理像素）：贴着形状边缘那几像素也算"点在它身上"，不然细线永远拖不动。</summary>
    private const int MoveSlop = 6;

    /// <summary>旋转把手离框顶多远（物理像素）。</summary>
    private const int RotateHandleLift = 26;
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
        BuildToolBar();
        // 点进输入框（放光标、选中一段）不算"在选区里起一笔"：不拦下这一冒泡，第二次点击进来
        // 就会走 BeginStroke → BeginTextEdit → 把刚打的一行清空（真机反馈"文字编辑无效"的路径之一）。
        // 光标定位由 TextBox 自己的处理负责，我们只在它之后把事件吃掉。
        TextEditor.PointerPressed += (_, e) => e.Handled = true;
        Root.DoubleTapped += (_, e) =>
        {
            if (_polyLine is null) return;
            e.Handled = true;
            FinishPolyLine(commit: true);
        };
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

    // ────────── 工具条（按钮、图标、颜色、粗细一律按 Core 的模型生成）──────────────────

    /// <summary>图标边长与按钮尺寸（DIP）。整条上每个按钮都由这几个数决定：
    /// 加一种画法就多一颗点或浮层里多一项，而不是把条撑成第二行（浮层多长一行就盖住用户正要标的东西）。</summary>
    private const double IconSide = 16;
    private const double ButtonWidth = 27;
    private const double ButtonHeight = 23;

    /// <summary>图标用的笔色。<b>写死在这里而不是交给主题</b>：条底是一条固定的暗色，
    /// 跟着系统主题走会在浅色模式下把按钮刷成白底，图标就糊在底上了。</summary>
    private static readonly Brush Ink = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private static readonly Brush InkDim = new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF));
    private static readonly Brush BarNormal = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    private static readonly Brush BarChecked = new SolidColorBrush(Color.FromArgb(0x66, 0x4C, 0xA0, 0xFF));

    /// <summary>浮层里圆点用的实心白（<see cref="Ink"/> 是画笔，带浓度，点出来会像"没选中"）。</summary>
    private static readonly int SolidWhite = Annotation.Opaque(0xFF, 0xFF, 0xFF);

    private Button _shapeButton = null!;
    private readonly List<Button> _brushButtons = new();
    private Button _brushButton = null!;
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private Button _clearButton = null!;
    private Button _copyButton = null!;
    private Flyout? _pickerFlyout;

    /// <summary>正在点的折线（顶点＝选区内物理像素）；null＝没有正在画的折线。</summary>
    private List<PixelPoint>? _polyLine;
    private PixelPoint _hoverLocal;                 // 折线的"橡皮筋"拖到哪儿

    /// <summary>
    /// 生成整条工具条：一颗「图形」（矩形/椭圆/直线/折线/箭头收在同一层里，见
    /// <see cref="AnnotationTools.Shapes"/>）+ 四颗各占一位的笔（画笔/荧光/打码/文字）
    /// +「当前这支笔」+ 撤销/重做/清空 + 四个动作。文字全部收进 ToolTip，条上只有图标。
    /// <para>真机反馈两轮把它推到了这个形状：先嫌"带文字的下拉太大"（于是全上图标），
    /// 再嫌"图形一种占一颗太铺开"（于是图形收进一个选择栏，新增折线也不再撑长条）。</para>
    /// <para>颜色与粗细收在「笔」那颗点开的浮层里（点当前工具图标也开同一层）：
    /// 藏起来的是选择，不是状态——条上那颗点始终看得出当前颜色与粗细。</para>
    /// </summary>
    private void BuildToolBar()
    {
        _shapeButton = IconButton(ToolIcon(_tool), ShapeButtonText());
        _shapeButton.Click += (_, _) => ShowShapePicker(_shapeButton);
        BarRow.Children.Add(_shapeButton);

        foreach (AnnotationTool tool in AnnotationTools.Brushes)
        {
            var button = IconButton(ToolIcon(tool),
                $"{Annotation.ToolName(tool)}：{Annotation.ToolHint(tool)}（再点一次换颜色和粗细）");
            button.Tag = tool;
            button.Click += BrushTool_Click;
            _brushButtons.Add(button);
            BarRow.Children.Add(button);
        }

        _brushButton = IconButton(BrushIcon(), "当前这支笔：点开换颜色和粗细");
        _brushButton.Click += (_, _) => ToggleBrushPicker(_brushButton);
        BarRow.Children.Add(_brushButton);

        BarRow.Children.Add(Separator());
        _undoButton = IconButton(ArrowIcon(left: true), "撤销上一条（Ctrl+Z）");
        _undoButton.Click += Undo_Click;
        _redoButton = IconButton(ArrowIcon(left: false), "重做上一条（Ctrl+Y）");
        _redoButton.Click += Redo_Click;
        _clearButton = IconButton(EraserIcon(), "清空全部标注（还能撤销回来）");
        _clearButton.Click += Clear_Click;
        BarRow.Children.Add(_undoButton);
        BarRow.Children.Add(_redoButton);
        BarRow.Children.Add(_clearButton);

        BarRow.Children.Add(Separator());
        _copyButton = IconButton(CopyIcon(), "把带标注的画面复制进剪贴板（Enter）");
        _copyButton.Click += Copy_Click;
        var save = IconButton(SaveIcon(), "存成 PNG（Ctrl+S）");
        save.Click += Save_Click;
        var pin = IconButton(PinIcon(), "钉在桌面上（同 F3），带上刚画的标注");
        pin.Click += Pin_Click;
        var ocr = IconButton(OcrIcon(), "认出这块画面的文字并复制");
        ocr.Click += Ocr_Click;
        var cancel = IconButton(CrossIcon(), "结束这一屏（Esc）");
        cancel.Click += Cancel_Click;
        foreach (var action in new[] { _copyButton, save, pin, ocr, cancel }) BarRow.Children.Add(action);

        _undoButton.IsEnabled = _redoButton.IsEnabled = _clearButton.IsEnabled = false;
        SyncTools();
    }

    private static Border Separator() => new()
    {
        Width = 1,
        Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
        Margin = new Thickness(3, 2, 3, 2),
    };

    private static Button IconButton(UIElement icon, string tip)
    {
        var button = new Button
        {
            Content = icon,
            Width = ButtonWidth,
            Height = ButtonHeight,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = BarNormal,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(button, tip);
        return button;
    }

    private void BrushTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AnnotationTool tool }) return;
        if (tool == _tool)
        {
            // 再点一次当前工具＝换这支笔：条上已经排满图标，颜色和粗细若再各摆一个下拉就又回到"太大"
            ToggleBrushPicker((FrameworkElement)sender);
            return;
        }
        SetTool(tool);
    }

    /// <summary>换工具。<b>所有换工具的入口都必须走这里</b>：先落笔（正在打的那行字、正在点的折线），
    /// 否则"换了个工具，刚画的东西凭空消失"。</summary>
    private void SetTool(AnnotationTool tool)
    {
        EndTextEditing(commit: true);
        FinishPolyLine(commit: true);
        _tool = tool;
        SyncTools();
    }

    private string ShapeButtonText()
        => $"图形：{Annotation.ToolName(_tool)}（点开换矩形 / 椭圆 / 直线 / 折线 / 箭头）";

    /// <summary>把"当前用的是哪种图形、哪一支笔"画出来（图标上没有文字，只能靠底色与那颗点说）。</summary>
    private void SyncTools()
    {
        _shapeButton.Content = ToolIcon(AnnotationTools.IsShapeTool(_tool) ? _tool : AnnotationTool.Rectangle);
        _shapeButton.Background = AnnotationTools.IsShapeTool(_tool) ? BarChecked : BarNormal;
        ToolTipService.SetToolTip(_shapeButton, ShapeButtonText());
        foreach (var button in _brushButtons)
            button.Background = (AnnotationTool)button.Tag! == _tool ? BarChecked : BarNormal;
        _brushButton.Content = BrushIcon();
        var sizes = string.Join(" / ", Enumerable.Range(0, Annotation.ThicknessSteps.Length)
            .Select(index => Annotation.ThicknessFor(_tool, index)));
        ToolTipService.SetToolTip(_brushButton,
            $"当前：{Annotation.Palette[Math.Clamp(_colourIndex, 0, Annotation.Palette.Count - 1)].Name}"
            + $" · {Annotation.ThicknessNames[Math.Clamp(_weightIndex, 0, Annotation.ThicknessNames.Count - 1)]}"
            + $"（{Annotation.ToolName(_tool)} 的细 / 中 / 粗 ≈ {sizes} 像素）。点开换");
    }

    // ────────── 选择浮层：图形 / 颜色 / 粗细（项同样按模型生成）──────────────────

    private void ShowShapePicker(FrameworkElement anchor)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Padding = new Thickness(4) };
        foreach (AnnotationTool shape in AnnotationTools.Shapes)
        {
            var wanted = shape;
            var button = IconButton(ToolIcon(shape),
                $"{Annotation.ToolName(shape)}：{Annotation.ToolHint(shape)}");
            button.Background = shape == _tool ? BarChecked : BarNormal;
            button.Click += (_, _) =>
            {
                SetTool(wanted);
                HidePicker();
            };
            row.Children.Add(button);
        }
        ShowPicker(anchor, row);
    }

    private void ToggleBrushPicker(FrameworkElement anchor)
    {
        if (_pickerFlyout is not null) HidePicker();
        else ShowBrushPicker(anchor);
    }

    private void ShowBrushPicker(FrameworkElement anchor)
    {
        var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Padding = new Thickness(4) };

        var colours = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        for (var index = 0; index < Annotation.Palette.Count; index++)
        {
            var colour = Annotation.Palette[index];
            var wanted = index;
            var dot = IconButton(DotIcon(colour.Bgra, selected: index == _colourIndex), colour.Name);
            dot.Click += (_, _) =>
            {
                _colourIndex = wanted;
                // 正在输入的那行字跟着换色：否则"选了颜色，字却没变"要用户自己去猜为什么
                if (_editingText) ApplyEditorAccent();
                HidePicker();
            };
            colours.Children.Add(dot);
        }

        var weights = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        for (var index = 0; index < Annotation.ThicknessSteps.Length; index++)
        {
            var wanted = index;
            var dot = IconButton(DotIcon(SolidWhite, selected: index == _weightIndex, diameter: 3 + index * 3),
                $"{Annotation.ThicknessNames[index]}（{Annotation.ToolName(_tool)} 上约 {Annotation.ThicknessFor(_tool, index)} 像素）");
            dot.Click += (_, _) =>
            {
                _weightIndex = wanted;
                HidePicker();
            };
            weights.Children.Add(dot);
        }

        rows.Children.Add(colours);
        rows.Children.Add(weights);
        ShowPicker(anchor, rows);
    }

    private void ShowPicker(FrameworkElement anchor, UIElement content)
    {
        _pickerFlyout = new Flyout { Content = content };
        _pickerFlyout.ShowAt(anchor);
    }

    private void HidePicker()
    {
        _pickerFlyout?.Hide();
        _pickerFlyout = null;
        SyncTools();
    }

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

    // ────────── 图标：矢量图元画的，不用字体字形 ──────────
    // 为什么不用图标字体：缺字会显示成方块，而遮罩窗一按 Esc 就没了，"这九个字形到底长什么样"
    // 没人能在真机上一眼逐个确认。画出来的图元至少几何是自己算出来的，撞了车也能在断言里看出来。

    private static Canvas Icon(params UIElement[] parts)
    {
        var canvas = new Canvas { Width = IconSide, Height = IconSide };
        foreach (var part in parts) canvas.Children.Add(part);
        return canvas;
    }

    private static Line Seg(double x1, double y1, double x2, double y2, double thickness = 1.6, Brush? brush = null)
        => new()
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = brush ?? Ink,
            StrokeThickness = thickness,
        };

    private static Polyline Curve(Brush? brush, params (double X, double Y)[] pts)
    {
        var line = new Polyline { Stroke = brush ?? Ink, StrokeThickness = 1.5 };
        foreach (var p in pts) line.Points.Add(new Point(p.X, p.Y));
        return line;
    }

    private static T Placed<T>(T part, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(part, x);
        Canvas.SetTop(part, y);
        return part;
    }

    private static Rectangle Out(double x, double y, double w, double h, double thickness = 1.5, Brush? brush = null)
        => Placed(new Rectangle { Width = w, Height = h, Stroke = brush ?? Ink, StrokeThickness = thickness }, x, y);

    private static Rectangle Fill(double x, double y, double w, double h, Brush brush)
        => Placed(new Rectangle { Width = w, Height = h, Fill = brush }, x, y);

    private static Ellipse Ring(double x, double y, double w, double h, double thickness = 1.5)
        => Placed(new Ellipse { Width = w, Height = h, Stroke = Ink, StrokeThickness = thickness }, x, y);

    /// <summary>浮层里的一颗点：选中＝白圈加粗，未选中＝细灰圈（两种都要在暗底上看得出来）。</summary>
    private static UIElement DotIcon(int bgra, bool selected, double? diameter = null)
    {
        var side = diameter ?? 11d;
        return Icon(Placed(new Ellipse
        {
            Width = side,
            Height = side,
            Fill = new SolidColorBrush(ToColor(bgra)),
            Stroke = new SolidColorBrush(selected ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = selected ? 2 : 1,
        }, (IconSide - side) / 2, (IconSide - side) / 2));
    }

    /// <summary>某个工具长什么样。<b>每种工具的差异必须只看图形就分得开</b>：条上没有文字，
    /// 图标撞车就等于把两个功能摆成同一个按钮（直线与折线的差别刻意做成"一段 / 两段带顶点"）。</summary>
    private static UIElement ToolIcon(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Rectangle => Icon(Out(2.5, 4, 11, 8)),                        // 空心方框
        AnnotationTool.Ellipse => Icon(Ring(2.5, 4, 11, 8)),                         // 空心椭圆
        AnnotationTool.Line => Icon(Seg(3, 13, 13, 3)),                              // 就是一条斜线，没头没尾
        // 两段折 + 顶点小方块：一眼看得出"这是点出来的多段线"，不是一条直线
        AnnotationTool.PolyLine => Icon(Curve(null, (2.5, 13), (7, 5.5), (13.5, 9.5)),
            Fill(5.6, 4.1, 2.8, 2.8, Ink), Fill(12.1, 8.1, 2.8, 2.8, Ink)),
        AnnotationTool.Arrow => Icon(Seg(3, 13, 12, 4),                              // 斜线 + 终点一个开口头
            Seg(12, 4, 7.6, 4.4), Seg(12, 4, 11.6, 8.4)),
        AnnotationTool.Pen => Icon(Curve(null, (2.5, 13), (5.5, 6.5), (8.5, 10.5), (13.5, 2.5))),
        AnnotationTool.Highlighter => Icon(Fill(2.5, 6.5, 11, 5.5,                   // 粗而半透明的一横
            new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF))),
            Seg(2.5, 13.5, 13.5, 13.5, 1.2, InkDim)),
        AnnotationTool.Mosaic => Icon(Fill(2.5, 2.5, 5, 5, Ink), Fill(8, 2.5, 5, 5, InkDim),   // 2×2 格子
            Fill(2.5, 8, 5, 5, InkDim), Fill(8, 8, 5, 5, Ink)),
        // 用线段拼出的 "A"：文字工具的通用记号，且不依赖任何字体（字形缺了就是个方块）
        AnnotationTool.Text => Icon(Curve(null, (2.5, 13), (8, 2.5), (13.5, 13)), Seg(5, 9.5, 11, 9.5)),
        _ => Icon(Out(2.5, 4, 11, 8)),
    };

    /// <summary>「当前这支笔」：实心点的颜色＝正在用的颜色，点的直径＝正在用的粗细。</summary>
    private UIElement BrushIcon() => DotIcon(ColourBgra, selected: false,
        diameter: 3 + Math.Clamp(_weightIndex, 0, Annotation.ThicknessSteps.Length - 1) * 3);

    /// <summary>撤销 / 重做：同一支箭头只差方向（形状不一样就会被看成两个不同的动作）。</summary>
    private static UIElement ArrowIcon(bool left)
    {
        double X(double v) => left ? v : IconSide - v;
        return Icon(Seg(X(13), 8, X(3.5), 8), Seg(X(3.5), 8, X(7.5), 4.2), Seg(X(3.5), 8, X(7.5), 11.8));
    }

    private static UIElement EraserIcon()
        => Icon(Placed(new Polygon
        {
            Fill = InkDim,
            Points = new PointCollection { new(3, 12), new(9, 12), new(13.5, 4), new(7.5, 3) },
        }, 0, 0), Seg(3, 13.5, 13.5, 13.5, 1.2, InkDim));

    private static UIElement CrossIcon() => Icon(Seg(3.5, 3.5, 12.5, 12.5), Seg(12.5, 3.5, 3.5, 12.5));

    private static UIElement CopyIcon()
        => Icon(Out(2, 2.5, 8, 9, 1.3, InkDim), Out(6, 5, 8, 9, 1.3));

    private static UIElement SaveIcon()
        => Icon(Seg(8, 1.5, 8, 9.5), Seg(8, 9.5, 4.8, 6.3), Seg(8, 9.5, 11.2, 6.3), Seg(2.5, 13, 13.5, 13));

    private static UIElement PinIcon()
        => Icon(Ring(4.5, 2, 7, 5.5), Seg(8, 7.5, 8, 13.5), Seg(5, 13.5, 11, 13.5));

    private static UIElement OcrIcon() => Icon(Ring(2.5, 2.5, 8, 8, 1.6), Seg(9.8, 9.8, 14, 14, 1.8));

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
            // 先问"这一按是不是在改已有的那一条"（选中框/把手就在那儿）；不是才轮到画新的
            if (TryBeginGrab(ToLocal(physical), e.Pointer)) return;
            // 折线是"点出来的"，没有按下-拖动-放开这一说：每一按钉一个顶点，收口用 Enter 或双击
            if (_tool == AnnotationTool.PolyLine) PlaceVertex(ToLocal(physical));
            else BeginStroke(ToLocal(physical), e.Pointer);
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
        if (_dragOriginal is not null)
        {
            DragTo(ToLocal(ToPhysical(position.X, position.Y)));
            return;
        }
        if (_stroke is not null)
        {
            ExtendStroke(ToLocal(ToPhysical(position.X, position.Y)));
            return;
        }
        if (_polyLine is not null)
        {
            // 看不见"下一段会落在哪儿"，点出来的顶点就不是想要的形状 ⇒ 最后顶点到光标挂一段橡皮筋
            _hoverLocal = ToLocal(ToPhysical(position.X, position.Y));
            DrawLive();
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
        if (_dragOriginal is not null)
        {
            // 拖动改的是已有的那一条：这条链与"新画一笔"的收口（EndStroke）是两件事，别混在一起
            EndDrag();
            Root.ReleasePointerCapture(e.Pointer);
            return;
        }
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
        // 正在打字时键盘归那一行字：Enter＝落笔，Esc＝只丢掉这一行。
        // 这一道是兜底——焦点真落进输入框时上面那条链会先把键处理掉（Handled 不再冒到这一层），
        // 而焦点没进去时（表现就是"文字编辑无效"）至少不会把整张截图复制走或整场取消。
        if (_editingText && e.Key is VirtualKey.Enter or VirtualKey.Escape)
        {
            e.Handled = true;
            EndTextEditing(commit: e.Key == VirtualKey.Enter);
            return;
        }
        if (_polyLine is not null && e.Key is VirtualKey.Enter or VirtualKey.Escape)
        {
            // Enter＝收口这条折线，Esc＝只丢掉这一条；两者都不该动到整场截图
            e.Handled = true;
            FinishPolyLine(commit: e.Key == VirtualKey.Enter);
            return;
        }
        switch (e.Key)
        {
            case VirtualKey.Delete when _selected is not null:
            case VirtualKey.Back when _selected is not null:
                // 选中之后总得能删掉：只有"撤销"的话，删中间那条要把后面几条一起退掉
                e.Handled = true;
                DeleteSelected();
                break;
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
            case VirtualKey.Z when IsControlDown() && IsShiftDown():
                e.Handled = true;
                Redo();                       // Ctrl+Shift+Z：另一派习惯，两个都给
                break;
            case VirtualKey.Z when IsControlDown():
                e.Handled = true;
                Undo();
                break;
            case VirtualKey.Y when IsControlDown():
                e.Handled = true;
                Redo();
                break;
        }
    }

    // Windows.UI.Core 里也有 Point/Size，与上面 using Windows.Foundation 撞名 ⇒ 这里只能全限定
    private static bool IsShiftDown()
        => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

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
        _history.Reset();
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
        _copyButton.Focus(FocusState.Programmatic);
    }

    /// <summary>换选区或取消：标注与底图一起丢掉（留着旧的会和新的选区对不上）。</summary>
    private void ResetAnnotations()
    {
        EndTextEditing(commit: false);
        FinishPolyLine(commit: false);
        _stroke = null;
        _history.Reset();
        _base = null;
        _preview = null;
        AnnotateLayer.Visibility = Visibility.Collapsed;
        DropSelection();
        _composed = null;
        _scratch = null;
        _undoButton.IsEnabled = _clearButton.IsEnabled = false;
    }

    private void BeginStroke(PixelPoint local, Pointer pointer)
    {
        ErrorChip.Visibility = Visibility.Collapsed;
        // 上一行字先落笔再动手：在画布上点第二下不该把刚打的字凭空清掉（真机反馈"文字编辑无效"的路径之一）。
        EndTextEditing(commit: true);
        if (_tool == AnnotationTool.Text)
        {
            BeginTextEdit(local);
            return;
        }
        // 起新的一笔就不再指着上一条了：选择框留在原地会挡住看新画的形状，下标也会变成误导
        DropSelection();
        // 打码的"一笔"从按下那一下就该看见：同一格糊掉与"还没糊"对用户是两个完全不同的结果，
        // 所以起点先按"一个点画两遍"存（MosaicBrush 走的是段，两个重合的点正好糊掉笔尖那一格）。
        _stroke = _tool == AnnotationTool.Mosaic
            ? new List<PixelPoint> { local, local }
            : new List<PixelPoint> { local };
        if (_tool == AnnotationTool.Mosaic) StartMosaicScratch(local);
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
        if (_tool != AnnotationTool.Mosaic)
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
            AnnotationPainter.Paint(scratch, selection.Width, selection.Height,
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
        if (points.Count > 1 && Annotation.IsTwoPointTool(_tool)) points[^1] = local;
        else points.Add(local);
        PaintPreview();
    }

    private void EndStroke()
    {
        var points = _stroke;
        _stroke = null;
        LiveLayer.Children.Clear();
        if (points is null) return;
        if (points.Count < Annotation.MinPoints(_tool))
        {
            // 点了一下没拖：以前是静默丢掉（真机反馈"点了没反应"），现在改成"选中脚下那一条"。
            // 打码（点一下糊一格）与文字（点一下出输入框）走不到这里——它们的那一下本来就够点数。
            SelectAtTap(points[0]);
            return;
        }
        var mark = new Annotation(_tool, points, ColourBgra, ThicknessForTool);
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
        // 抢走就等于把刚打的一行字丢在半路——那是批次 RD-1 刚堵掉的那一类丢字路径。
        if (_editingText || _polyLine is not null) return false;
        if (_selected is not { } index) return false;
        if (Selected is not { } mark) { DropSelection(); return false; }

        _grab = mark.GrabAt(local, RotateHandle(mark), MoveSlop);
        if (_grab == Grab.None) return false;

        // 按的是某一头的把手 ⇒ 钉住的那一点改到<b>对面</b>那头（模型算，界面不猜）：
        // 否则绕字块中心缩放会把左上角一起推出去，真机反馈就是"一缩放整行字和它的框都跑了"。
        _dragOriginal = _grab == Grab.Scale ? mark.WithScalePivotTowards(local) : mark;
        _dragAnchor = local;
        _dragLast = local;
        _underDrag = UnderDragBuffer(index);
        _dragCanvas = _underDrag is { } under ? (byte[])under.Clone() : null;
        Root.CapturePointer(pointer);
        DrawSelectionHandles();
        return true;
    }

    /// <summary>
    /// 旋转把手落在哪儿。<b>顶边贴到选区上沿时把它挪进框内</b>：画在选区外面的那一按不属于本窗的
    /// "在选区里"那条链（会被当成重新框选），等于这颗把手永远点不到——而它在屏幕上明明看得见。
    /// </summary>
    private PixelPoint RotateHandle(Annotation mark)
    {
        var box = mark.Bounds();
        var lift = RotateHandleLift;
        if (_selection is { } selection && box.Y - lift < selection.Y)
            lift = Math.Min(box.Height / 2, RotateHandleLift);
        return new PixelPoint(box.X + box.Width / 2, box.Y - lift);
    }

    /// <summary>
    /// "除了被拖这条、其余都在原位"的那张底，拖动开始时算一次；之后每帧只做
    /// <b>一次整块复制 + 一条重画</b>。每帧从底图重烤全部标注的话，标注一多就又变成"跟不上手"
    /// 那一类（正是打码那条反馈的同一个成因）。
    /// </summary>
    private byte[]? UnderDragBuffer(int excluding)
    {
        if (_base is not { } basePixels || _selection is not { } selection) return null;
        var rest = new List<Annotation>(_history.Marks);
        rest.RemoveAt(excluding);
        try { return AnnotationPainter.Render(basePixels, selection.Width, selection.Height, rest); }
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
        var preview = Preview(original, local);
        if (_dragCanvas is { } canvas && _underDrag is { } under
            && _preview is { } previewBitmap && _selection is { } selection)
        {
            Buffer.BlockCopy(under, 0, canvas, 0, under.Length);
            try
            {
                AnnotationPainter.Paint(canvas, selection.Width, selection.Height, preview);
            }
            catch (Exception ex)
            {
                // 预览画不出来不当场说，用户就要到松手才发现这一改是坏的
                ShowError("这一改画不出来：" + ex.Message);
                return;
            }
            using (var stream = previewBitmap.PixelBuffer.AsStream())
                stream.Write(canvas, 0, canvas.Length);
            previewBitmap.Invalidate();
        }
        DrawSelectionHandles(preview);
    }

    /// <summary>拖动后的那一条：几何一律由模型侧算（MovedBy / ScaledBy / RotatedBy），界面只递光标位置。</summary>
    private Annotation Preview(Annotation original, PixelPoint local)
    {
        switch (_grab)
        {
            case Grab.Move:
                return original.MovedBy(local.X - _dragAnchor.X, local.Y - _dragAnchor.Y);
            case Grab.Rotate:
            {
                var pivot = original.Origin;
                var from = Math.Atan2(_dragAnchor.Y - pivot.Y, _dragAnchor.X - pivot.X);
                var to = Math.Atan2(local.Y - pivot.Y, local.X - pivot.X);
                return original.RotatedBy((to - from) * 180d / Math.PI);
            }
            default:
            {
                // 缩放走 ScalePivot（按下的是哪一头，轴就在对面那一头）；旋转仍走 Origin（字块中心）。
                var pivot = original.ScalePivot;
                var start = Distance(_dragAnchor, pivot);
                // 把手正好按在轴点上时比值没有意义（分母为 0）：保持原样，别让形状瞬间炸开
                return start < 1 ? original : original.ScaledBy(Distance(local, pivot) / start);
            }
        }
    }

    private static double Distance(PixelPoint a, PixelPoint b)
        => Math.Sqrt((double)(a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

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
        if (original is null || index is not { } i || grab == Grab.None) return;
        var result = Preview(original, _dragLast);
        if (result == original) { DrawSelectionHandles(); return; }
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
        SelectionTip.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 丢掉选中（连带把手与那句说明）。
    /// <para><b>历史一变就必须调</b>：撤销/重做/清空/删除都会移动下标，留着旧下标就成了
    /// "框指着矩形、下一个拖动改的是椭圆"。拖动状态也一起收：那些缓冲是按旧下标算的。</para>
    /// </summary>
    private void DropSelection()
    {
        _selected = null;
        _grab = Grab.None;
        _dragOriginal = null;
        _dragLast = default;
        _underDrag = null;
        _dragCanvas = null;
        LiveLayer.Children.Clear();
        SelectionTip.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// "这一按没拖出形状"（点了一下就走）原来会被静默丢掉——真机反馈的"点了没反应"就是它。
    /// 现在用它去选中脚下那条：已有的标注于是有一个不需要额外模式、也不会挡住画新东西的出口。
    /// <para>只在 <see cref="Annotation.MinPoints"/> 判定为"画不成"的那条链上触发，所以打码（点一下
    /// 就该糊掉一格）与文字（点一下就该出输入框）的语义完全没动。</para>
    /// </summary>
    private void SelectAtTap(PixelPoint at)
    {
        var index = AnnotationPainter.HitTest(_history.Marks, at, SelectionSlop);
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
        if (_base is not { } basePixels || _preview is not { } preview || _selection is not { } selection) return;
        var marks = _history.Marks;
        try
        {
            var composed = AnnotationPainter.Render(basePixels, selection.Width, selection.Height, marks);
            using (var stream = preview.PixelBuffer.AsStream())
                stream.Write(composed, 0, composed.Length);
            preview.Invalidate();
            _composed = composed;
            _scratch = null;
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

        switch (_tool)
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

    // ────────── 文字标注：就地输入 ──────────

    private void BeginTextEdit(PixelPoint local)
    {
        var (x, y) = LocalToDip(local);
        _editingText = true;
        // 靠右边/下边点击时把输入框拉回屏内：默认宽度 180 DIP，越界就等于"输入框跑屏外了，打不了字"
        TextEditorHost.Margin = new Thickness(
            Math.Clamp(x, 0, Math.Max(0, _monitor.Width / _scale - 190)),
            Math.Clamp(y, 0, Math.Max(0, _monitor.Height / _scale - 40)), 0, 0);
        TextEditorHost.Visibility = Visibility.Visible;
        TextEditor.FontSize = Annotation.DefaultFontHeight / _scale;
        ApplyEditorAccent();
        TextEditor.Text = string.Empty;
        _textAnchor = local;
        // 焦点没落进输入框必须当场说出来：那之后敲的键会落到遮罩那一层，Enter 变成"复制整张截图"，
        // 用户看到的就是"打了字什么都没发生"（真机反馈的原话）。静默失效比报错难查得多。
        if (!TextEditor.Focus(FocusState.Programmatic))
            ShowError("这一行字还没拿到键盘焦点：点一下那个描边的输入框再打字（Enter 落笔，Esc 只丢掉这一行）");
    }

    /// <summary>
    /// 就地输入那一框的字色与描边：<b>只有这一处</b>在说"用哪个颜色"。
    /// 底板是近乎透明的（真机反馈："点击后不应出现黄色矩形，最好是透明但描边的边框"——
    /// 实色黄底会把正要看的画面盖掉），所以边界全靠这条描边认出来，描边跟着当前字色走，
    /// 在深色截图与浅色截图上都看得出来。
    /// </summary>
    private void ApplyEditorAccent()
    {
        var brush = new SolidColorBrush(ToColor(ColourBgra));
        TextEditor.Foreground = brush;
        TextEditorHost.BorderBrush = brush;
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
        _history.Add(new Annotation(AnnotationTool.Text, new[] { _textAnchor }, ColourBgra, ThicknessForTool)
        {
            Text = text,
            FontHeight = Annotation.DefaultFontHeight,
        });   // 轴由模型按字块中心现算（Annotation.Origin），界面不自己钉变换轴
        _selected = _history.Count - 1;   // 打完字紧接着就是"挪个位置/改个字号"：那一条直接在手边
        Rebake();
        DrawSelectionHandles();
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

    // ────────── 编辑历史：撤销 / 重做 / 清空 ──────────
    // 三个动作都只是移动历史指针（状态快照在 AnnotationHistory 里），
    // 所以"清空了又撤销回来"和"撤销两步再重做"不需要任何额外代码，也不会残留半条。

    private void Undo()
    {
        if (!_history.Undo()) return;
        DropSelection();                      // 下标随历史移动：旧框指着的是完全另一条标注
        Rebake();
    }

    private void Redo()
    {
        if (!_history.Redo()) return;
        DropSelection();
        Rebake();
    }

    /// <summary>
    /// 输入框<b>不</b>因失去焦点而结束——这一条就是真机反馈"必须按住鼠标才在输入、松手就算编辑完"的成因：
    /// 按下那一下把焦点给了输入框，松开时焦点回到遮罩那一层，原先挂在 LostFocus 上的落笔于是把这一行当场结掉。
    /// 落笔的时机改由用户看得见的那几个动作明确决定：Enter、点画布别处、切工具、点动作按钮（各自都会调
    /// <see cref="EndTextEditing"/>），Esc 只丢掉这一行。
    /// </summary>
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        EndTextEditing(commit: true);
        Undo();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        EndTextEditing(commit: true);
        Redo();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        EndTextEditing(commit: true);
        _history.Clear();
        DropSelection();
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
        // 没按 Enter 就点动作按钮：刚打的那行字要跟着图一起走，而不是被丢掉（四条落点同一条出口）。
        EndTextEditing(commit: true);
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
                return (AnnotationPainter.Render(basePixels, selection.Width, selection.Height, _history.Marks),
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
