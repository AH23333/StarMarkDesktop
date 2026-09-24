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
        BuildToolBar();
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
    /// 加一个工具就多一颗点，而不是把条撑成第二行（浮层多长一行就盖住用户正要标的东西）。</summary>
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

    private readonly List<Button> _toolButtons = new();
    private Button _brushButton = null!;
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private Button _clearButton = null!;
    private Button _copyButton = null!;
    private Flyout? _brushFlyout;

    /// <summary>
    /// 生成整条工具条：八个工具图标（按 <see cref="AnnotationTool"/> 枚举）+「当前这支笔」
    /// + 撤销/重做/清空 + 四个动作。文字全部收进 ToolTip，条上只有图标。
    /// <para>颜色与粗细收在「笔」那颗点开的浮层里（点已选中的工具图标也开同一层，Snipaste 的用法）：
    /// 条上再摆两个下拉就是真机反馈的"选择器太大"，而把它们彻底藏起来又让"现在用什么颜色、多粗"看不见
    /// ⇒ 折中成<b>条上留一颗看得懂的点，点开才是选择</b>。</para>
    /// </summary>
    private void BuildToolBar()
    {
        foreach (AnnotationTool tool in Enum.GetValues<AnnotationTool>())
        {
            var button = IconButton(ToolIcon(tool),
                $"{Annotation.ToolName(tool)}：{Annotation.ToolHint(tool)}（再点一次换颜色和粗细）");
            button.Tag = tool;
            button.Click += ToolButton_Click;
            _toolButtons.Add(button);
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
        SyncToolButtons();
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

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AnnotationTool tool }) return;
        if (tool == _tool)
        {
            // 再点一次当前工具＝换这支笔：八个图标已经排满一行，颜色和粗细若再各摆一个下拉就又回到"太大"
            ToggleBrushPicker((FrameworkElement)sender);
            return;
        }
        EndTextEditing(commit: true);   // 先落笔再换工具：用户打了一半的字不该凭空消失
        _tool = tool;
        SyncToolButtons();
    }

    /// <summary>把"哪一个是当前工具"画出来（图标上没有文字，只能靠底色说），并让"笔"那颗点跟上颜色与粗细。</summary>
    private void SyncToolButtons()
    {
        foreach (var button in _toolButtons)
            button.Background = (AnnotationTool)button.Tag! == _tool ? BarChecked : BarNormal;
        _brushButton.Content = BrushIcon();
        var sizes = string.Join(" / ", Enumerable.Range(0, Annotation.ThicknessSteps.Length)
            .Select(index => Annotation.ThicknessFor(_tool, index)));
        ToolTipService.SetToolTip(_brushButton,
            $"当前：{Annotation.Palette[Math.Clamp(_colourIndex, 0, Annotation.Palette.Count - 1)].Name}"
            + $" · {Annotation.ThicknessNames[Math.Clamp(_weightIndex, 0, Annotation.ThicknessNames.Count - 1)]}"
            + $"（{Annotation.ToolName(_tool)} 的细 / 中 / 粗 ≈ {sizes} 像素）。点开换");
    }

    // ────────── 颜色和粗细：浮层里的项同样按模型生成 ──────────

    /// <summary>点开 / 收起那一层。已经开着时再点一次是"关掉"，不是"把浮层挪到另一颗点上"。</summary>
    private void ToggleBrushPicker(FrameworkElement anchor)
    {
        if (_brushFlyout is not null) HideBrushPicker();
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
                if (_editingText) TextEditor.Foreground = new SolidColorBrush(ToColor(colour.Bgra));
                HideBrushPicker();
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
                HideBrushPicker();
            };
            weights.Children.Add(dot);
        }

        rows.Children.Add(colours);
        rows.Children.Add(weights);
        _brushFlyout = new Flyout { Content = rows };
        _brushFlyout.ShowAt(anchor);
    }

    private void HideBrushPicker()
    {
        _brushFlyout?.Hide();
        _brushFlyout = null;
        SyncToolButtons();
    }

    // ────────── 图标：矢量图元画的，不用字体字形 ──────────
    // 为什么不用图标字体：缺字会显示成方块，而遮罩窗一按 Esc 就没了，"八个字形到底长什么样"
    // 没人能在真机上一眼逐个确认。画出来的图元至少几何是自己算出来的，撞不了车。

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
    /// 图标撞车就等于把两个功能摆成同一个按钮。</summary>
    private static UIElement ToolIcon(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Rectangle => Icon(Out(2.5, 4, 11, 8)),                        // 空心方框
        AnnotationTool.Ellipse => Icon(Ring(2.5, 4, 11, 8)),                         // 空心椭圆
        AnnotationTool.Line => Icon(Seg(3, 13, 13, 3)),                              // 就是一条斜线，没头没尾
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
        _stroke = null;
        _history.Reset();
        _base = null;
        _preview = null;
        AnnotateLayer.Visibility = Visibility.Collapsed;
        LiveLayer.Children.Clear();
        _undoButton.IsEnabled = _clearButton.IsEnabled = false;
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
        if (_stroke is not { Count: > 0 } points) return;
        if (points[^1] == local) return;                 // 鼠标不动也会反复回调：重复点会让折线的点数爆掉
        // 两点工具（矩形/椭圆/直线/箭头）的形状只由"按下那点"与"放开那点"决定：中途的采样是走过的痕迹，
        // 覆盖掉而不是追加。留着它们，一条矩形会在历史里带着几十个点，而任何按"第二个点"取另一端的写法
        // 都会画出针尖大的框——预览取最后一点、落笔取第二点，就是"松手后图形变得非常小"的成因。
        if (points.Count > 1 && Annotation.IsTwoPointTool(_tool)) points[^1] = local;
        else points.Add(local);
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
        _history.Add(mark);
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
            var composed = AnnotationPainter.Render(basePixels, selection.Width, selection.Height, _history.Marks);
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
        _undoButton.IsEnabled = _history.CanUndo;
        _redoButton.IsEnabled = _history.CanRedo;
        _clearButton.IsEnabled = _history.Count > 0;
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
        _history.Add(new Annotation(AnnotationTool.Text, new[] { _textAnchor }, ColourBgra, ThicknessForTool)
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

    // ────────── 编辑历史：撤销 / 重做 / 清空 ──────────
    // 三个动作都只是移动历史指针（状态快照在 AnnotationHistory 里），
    // 所以"清空了又撤销回来"和"撤销两步再重做"不需要任何额外代码，也不会残留半条。

    private void Undo()
    {
        if (_history.Undo()) Rebake();
    }

    private void Redo()
    {
        if (_history.Redo()) Rebake();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
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
