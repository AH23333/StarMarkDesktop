#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using Windows.UI;

namespace StarMark.UI.Views;

/// <summary>
/// 画布的工具条。<b>它自己不参与鼠标穿透</b>：§16.6 那条"穿透之后找不回"的防线之一
/// （另两条是全局热键与托盘菜单）。位置只由拖动决定，不"智能跟随"——用户需要知道它在哪。
/// </summary>
public sealed partial class CanvasToolbarWindow : Window
{
    /// <summary>
    /// 兜底尺寸（DIP）：只在内容还没量出来时用。正常路径按 <c>Root.DesiredSize</c> 实测——
    /// 写死的数字在"按钮多一点/字体大一号"之后就是把 ✕ 挤出客户区，而条子被画布盖住时那是唯一的鼠标出口。
    /// </summary>
    private const double FallbackWidthDip = 960;
    private const double FallbackHeightDip = 100;
    private const double TopMarginDip = 20;

    private static readonly SolidColorBrush ActiveBrush = new(Color.FromArgb(0xFF, 0x2D, 0x6E, 0xC4));
    private static readonly SolidColorBrush IdleBrush = new(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush HaloBrush = new(Color.FromArgb(0x55, 0x2D, 0x6E, 0xC4));

    private readonly List<Button> _colourButtons = new();

    /// <summary>图形那几颗：按 <see cref="CanvasTools.Shapes"/> 生成。换工具时亮哪一颗只认这张表，
    /// 不在界面里另列一份工具清单（否则"条上有这颗、点下去走的却是另一支"只有真机才看得见）。</summary>
    private readonly Dictionary<CanvasTool, Button> _shapeButtons = new();

    private IntRect _screen;
    private double _scale = 1d;

    private bool _dragging;
    private int _grabOffsetX;
    private int _grabOffsetY;

    public CanvasToolbarWindow()
    {
        InitializeComponent();
        WindowInterop.RemoveDefaultWindowFrame(this);
        BuildPalette();
        BuildShapeButtons();
        // 状态回报是"按钮哪个亮着"这件事的唯一出口：画布那边改了，条子必须跟着改
        CanvasService.StateChanged += Refresh;
        Closed += (_, _) => CanvasService.StateChanged -= Refresh;

        Grip.PointerPressed += GripPressed;
        Grip.PointerMoved += GripMoved;
        Grip.PointerReleased += GripReleased;
        Grip.PointerCaptureLost += (_, _) => _dragging = false;
        Root.KeyDown += Root_KeyDown;
        Refresh();
    }

    public void ShowAt(IntRect screen, double scale)
    {
        _screen = screen;
        _scale = scale <= 0 ? 1 : scale;

        // 先亮出来再量：窗没亮过时按钮的控件模板尚未应用，那一次 Measure 量到的是十几颗空按钮的
        // MinWidth 之和（真机症状：条子只有约 360 宽，右边「穿透／贴图／存图／复制／✕退出」整段在窗外，
        // 而 ✕ 是唯一的鼠标出口）。
        AppWindow.Show();
        Root.UpdateLayout();
        Fit(centerOnScreen: true);
        Refresh();
    }

    /// <summary>
    /// 按内容重算窗尺寸。<b>每次状态刷新都要走一遍</b>：切到穿透态时那句说明长得多，
    /// 只在 ShowAt 量一次的话，变高的那一行会被截在窗外（"按钮还在但看不见"最难查）。
    /// <paramref name="centerOnScreen"/> 只在第一次出现——用户拖动过之后就留在原地。
    /// </summary>
    private void Fit(bool centerOnScreen)
    {
        var screen = _screen;
        var scale = _scale;
        if (screen.Width <= 0 || screen.Height <= 0) return;      // 还没 ShowAt 过（构造期那次 Refresh）

        // 量内容时要按这块屏的宽度去约束它：无限大下状态文本会报出一行长文的自然宽，
        // 整条窗因此比屏还宽，居中摆放就变成"左右各被截掉一截"。
        var availableDip = Math.Max(360, screen.Width / scale - TopMarginDip * 2);
        Root.Measure(new Windows.Foundation.Size(availableDip, double.PositiveInfinity));
        var wanted = Root.DesiredSize;
        var width = Math.Min((int)Math.Round((wanted.Width > 1 ? wanted.Width : FallbackWidthDip) * scale),
            Math.Max(1, screen.Width));
        var height = (int)Math.Round((wanted.Height > 1 ? wanted.Height : FallbackHeightDip) * scale);

        var hwnd = WindowInterop.GetHwnd(this);
        int x, y;
        if (centerOnScreen)
        {
            x = screen.X + (screen.Width - width) / 2;
            y = screen.Y + (int)Math.Round(TopMarginDip * scale);
        }
        else
        {
            // 重算尺寸不该把条子挪回中间：用户刚拖到哪儿它就还该在哪儿
            var now = WindowInterop.GetWindowRect(this);
            x = now.X;
            y = now.Y;
        }
        // 左右都夹回屏内：宁可贴边，也不要出现"看得见一半、点不到另一半"
        x = Math.Clamp(x, screen.X, Math.Max(screen.X, screen.Right - width));
        y = Math.Clamp(y, screen.Y, Math.Max(screen.Y, screen.Bottom - height));

        // 定位顺带提层：同 RaiseAboveCanvas，这里也不能传 HWND_TOPMOST——它已经在带里，
        // 再传一次只"换带"不重排＝画布仍然压在条子上面（每屏一块 TOPMOST 的玻璃，谁最后被提谁在上）
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOP, x, y, width, height,
            WindowInterop.SWP_SHOWWINDOW | WindowInterop.SWP_NOACTIVATE);
        WindowInterop.ShowWindow(hwnd, WindowInterop.SW_SHOWNOACTIVATE);
    }

    /// <summary>
    /// 压在画布之上。<b>两发 SetWindowPos，各管一件事</b>（批次 WD-7 的真机教训）：
    /// ① <c>HWND_TOPMOST</c> 只负责<b>进带</b>——WinUI 窗生下来不在 topmost 带里，只传 HWND_TOP 的话
    ///    它永远留在普通层，于是任何应用一激活就把条子盖住（症状："选了画笔后菜单点不动"），
    ///    而且把这样的窗当 <c>hwndInsertAfter</c> 递给画布，还会<b>把画布一起拽出 topmost 带</b>
    ///    （Win32 文档："If a topmost window is repositioned … after any non-topmost window, it is no longer topmost"）
    ///    ——那正是"绘制态下鼠标还在动桌面应用"的成因；
    /// ② <c>HWND_TOP</c> 才负责<b>带内重排</b>：对已经在带里的窗再传 TOPMOST 只换带不重排＝什么都没做。
    /// </summary>
    public void RaiseAboveCanvas()
    {
        var hwnd = WindowInterop.GetHwnd(this);
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOPMOST, 0, 0, 0, 0,
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOP,
            0, 0, 0, 0,
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
    }

    /// <summary>这块条子的句柄：画布层要靠它把自己插到条子之下（定序不能只靠提自己）。</summary>
    public IntPtr Hwnd => WindowInterop.GetHwnd(this);

    public void CloseToolbar()
    {
        CanvasService.StateChanged -= Refresh;
        Close();
    }

    // ────────── 动作 ──────────

    // 三支笔都走 ToggleTool：再点当前这支＝收笔回穿透态（与截图/贴图那条"再点取消选择"同一交互语言）
    private void Pen_Click(object sender, RoutedEventArgs e) => CanvasService.ToggleTool(CanvasTool.Pen);

    private void Marker_Click(object sender, RoutedEventArgs e) => CanvasService.ToggleTool(CanvasTool.Highlighter);

    private void Eraser_Click(object sender, RoutedEventArgs e) => CanvasService.ToggleTool(CanvasTool.Eraser);

    // 图形与三支笔同一套语义：点一下选上、再点当前这颗＝收笔回穿透态。
    // 一颗都不在 XAML 里写（见 BuildShapeButtons），落点交给 CanvasService（它认 Press.Shape / Press.PolyLine）。
    private void Shape_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CanvasTool tool }) CanvasService.ToggleTool(tool);
    }

    private void Thin_Click(object sender, RoutedEventArgs e) => CanvasService.SelectWidth(0);

    private void Medium_Click(object sender, RoutedEventArgs e) => CanvasService.SelectWidth(1);

    private void Thick_Click(object sender, RoutedEventArgs e) => CanvasService.SelectWidth(2);

    private void Undo_Click(object sender, RoutedEventArgs e) => CanvasService.Undo();

    private void Clear_Click(object sender, RoutedEventArgs e) => CanvasService.ClearAll();

    private void Through_Click(object sender, RoutedEventArgs e)
        => CanvasService.SetClickThrough(!CanvasService.IsClickThrough);

    private void Halo_Click(object sender, RoutedEventArgs e) => CanvasService.SetHalo(!CanvasService.HaloEnabled);

    private void Pin_Click(object sender, RoutedEventArgs e) => CanvasService.SnapshotToPin();

    private void Save_Click(object sender, RoutedEventArgs e) => CanvasService.SavePng();

    private void Copy_Click(object sender, RoutedEventArgs e) => CanvasService.SnapshotToClipboard();

    /// <summary>「⌨」：开／收快捷键面板（发起人点名的兜底——"防止忘记快捷键，随时能打开查看"）。</summary>
    private void Keys_Click(object sender, RoutedEventArgs e) => CanvasService.ToggleHotkeyPanel();

    private void Close_Click(object sender, RoutedEventArgs e) => CanvasService.Stop();

    private void Colour_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index }) CanvasService.SelectColor(index);
    }

    /// <summary>
    /// 颜色格子按 <see cref="CanvasService.Palette"/> 生成，不在 XAML 里手写：
    /// 手写就变成"哪里有哪些颜色"的第二份事实，加一个色而条上没有——那种错只有跑起来才看得见。
    /// </summary>
    private void BuildPalette()
    {
        for (var index = 0; index < CanvasService.Palette.Count; index++)
        {
            var colour = CanvasService.Palette[index];
            var swatch = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(
                    (byte)(colour.Bgra >> 24 & 0xFF),
                    (byte)(colour.Bgra >> 16 & 0xFF),
                    (byte)(colour.Bgra >> 8 & 0xFF),
                    (byte)(colour.Bgra & 0xFF))),
            };
            var button = new Button
            {
                Content = swatch,
                Tag = index,
                Padding = new Thickness(4),
                MinWidth = 0,
                MinHeight = 0,
                Background = IdleBrush,
            };
            ToolTipService.SetToolTip(button, colour.Name);
            button.Click += Colour_Click;
            _colourButtons.Add(button);
            ColourPalette.Children.Add(button);
        }
    }

    /// <summary>
    /// 图形那几颗按钮：整排由 <see cref="CanvasTools.Shapes"/> 生成，一颗都不在 XAML 里写。
    /// <para>
    /// <b>为什么只有这一段是图标、三支笔仍旧是汉字</b>：发起人点名的就是"图形编辑功能使用图标"，
    /// 而图形这一段确实该换——五种图形若各占两个汉字，条子要长出十个字的宽度，
    /// 而 <see cref="FallbackWidthDip"/> 之外的宽度是从 ✕ 那颗出口身上挤的。
    /// 画笔／荧光笔／橡皮那三颗一字不差、且是全条最常被点的，留着字比留着一个需要认的记号好认。
    /// </para>
    /// <para>
    /// <b>图标上没有文字，所以名字与手势只能靠状态行说</b>：条子只有两行高，而 WinUI 3 把 ToolTip
    /// 那种弹出层钉在宿主窗边界内（批次 WI 的同一条真机结论）——悬停时把说明写进自己那一行才看得见。
    /// ToolTip 仍然挂着：它才是这条文案的唯一出处，悬停处理只是把它显示到看得见的位置。
    /// </para>
    /// </summary>
    private void BuildShapeButtons()
    {
        foreach (var tool in CanvasTools.Shapes)
        {
            var button = new Button
            {
                Content = ShapeIcon(tool),
                Tag = tool,
                Width = ShapeButtonWidth,
                Height = ShapeButtonHeight,
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = 0,
                Background = IdleBrush,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var hint = ShapeHint(tool);
            ToolTipService.SetToolTip(button, hint);
            button.PointerEntered += (_, _) => ShowHint(hint);
            button.PointerExited += (_, _) => HideHint();
            button.Click += Shape_Click;
            _shapeButtons[tool] = button;
            ShapeRow.Children.Add(button);
        }
    }

    private const double ShapeButtonWidth = 27;
    private const double ShapeButtonHeight = 23;

    /// <summary>悬停说明直接写进状态行（弹出层在这扇小窗里放不下，见 <see cref="BuildShapeButtons"/>）。</summary>
    private void ShowHint(string hint)
    {
        Status.Text = hint;
        Fit(centerOnScreen: false);       // 说明比常态那行长时，不重算窗高就会被截在窗外
    }

    private void HideHint()
    {
        Status.Text = StatusText();
        Fit(centerOnScreen: false);
    }

    /// <summary>这颗按钮在说什么。名字一律取 <see cref="CanvasTools.Name"/>，界面不另写一份；
    /// 手势说明只有这里知道（"按下与抬起是两个对角"和"抬手定一个顶点"不是一回事）。</summary>
    private static string ShapeHint(CanvasTool tool) => tool switch
    {
        CanvasTool.Rectangle => $"{tool.Name()}：按住拖出，按下处与抬起处是两个对角。再点一次收笔",
        CanvasTool.Ellipse => $"{tool.Name()}：按住拖出，拖拽的矩形就是它的外切框。再点一次收笔",
        CanvasTool.Line => $"{tool.Name()}：按住拖出一条，起点到抬手处。再点一次收笔",
        // 折线是唯一"跨按"的，所以它的说明必须把收口方式一并说清，否则用户会以为软件卡住了
        CanvasTool.PolyLine => $"{tool.Name()}：按住拖出一段、抬手定一个顶点，可接着拖下一段。" +
                               "Esc／再点一次／换工具收口（整条算一笔，撤销一次退整条）",
        CanvasTool.Arrow => $"{tool.Name()}：按住拖出，箭头指向抬手那一端。再点一次收笔",
        _ => $"{tool.Name()}：按住拖出。再点一次收笔",
    };

    // ────────── 图标：矢量图元画的，不用字体字形 ──────────
    //
    // 与截图/贴图那条工具条（CaptureOverlayWindow 的图标库）同一套几何与同样的 16×16 边长：
    // 两边是同一族能力，用户在一边认得的记号到另一边不该换个画法。
    // 不用图标字形的理由也同一条：缺字会显示成方块，而"这五种图形到底长什么样"得能当场逐个核对。

    private static readonly SolidColorBrush IconInk = new(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));

    private static UIElement ShapeIcon(CanvasTool tool) => tool switch
    {
        CanvasTool.Rectangle => Icon(Out(2.5, 4, 11, 8)),                        // 空心方框
        CanvasTool.Ellipse => Icon(Ring(2.5, 4, 11, 8)),                         // 空心椭圆
        CanvasTool.Line => Icon(Seg(3, 13, 13, 3)),                              // 就是一条斜线，没头没尾
        // 两段折 + 顶点小方块：一眼看得出"这是点出来的多段线"，不是一条直线
        CanvasTool.PolyLine => Icon(Curve((2.5, 13), (7, 5.5), (13.5, 9.5)),
            Dot(5.6, 4.1), Dot(12.1, 8.1)),
        CanvasTool.Arrow => Icon(Seg(3, 13, 12, 4),                              // 斜线 + 终点一个开口头
            Seg(12, 4, 7.6, 4.4), Seg(12, 4, 11.6, 8.4)),
        _ => Icon(Out(2.5, 4, 11, 8)),
    };

    private const double IconSide = 16;

    private static Canvas Icon(params UIElement[] parts)
    {
        var canvas = new Canvas { Width = IconSide, Height = IconSide };
        foreach (var part in parts) canvas.Children.Add(part);
        return canvas;
    }

    private static Line Seg(double x1, double y1, double x2, double y2)
        => new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = IconInk, StrokeThickness = 1.6 };

    private static Polyline Curve(params (double X, double Y)[] pts)
    {
        var line = new Polyline { Stroke = IconInk, StrokeThickness = 1.5 };
        foreach (var p in pts) line.Points.Add(new Windows.Foundation.Point(p.X, p.Y));
        return line;
    }

    /// <summary>折线的顶点记号（2.8 见方的小实心块）。它才是"直线"与"折线"分得开的那一笔。</summary>
    private static Rectangle Dot(double x, double y)
    {
        var square = new Rectangle { Width = 2.8, Height = 2.8, Fill = IconInk };
        Canvas.SetLeft(square, x);
        Canvas.SetTop(square, y);
        return square;
    }

    private static Rectangle Out(double x, double y, double w, double h)
    {
        var box = new Rectangle { Width = w, Height = h, Stroke = IconInk, StrokeThickness = 1.5 };
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);
        return box;
    }

    private static Ellipse Ring(double x, double y, double w, double h)
    {
        var ring = new Ellipse { Width = w, Height = h, Stroke = IconInk, StrokeThickness = 1.5 };
        Canvas.SetLeft(ring, x);
        Canvas.SetTop(ring, y);
        return ring;
    }

    private void Refresh()
    {
        // 按钮高亮只反映"现在是哪一个"，不反映悬停：两套状态画在一起就分不清了
        Highlight(PenButton, CanvasService.Tool == CanvasTool.Pen);
        Highlight(MarkerButton, CanvasService.Tool == CanvasTool.Highlighter);
        Highlight(EraserButton, CanvasService.Tool == CanvasTool.Eraser);
        // 图形那颗不在这张硬编码清单里：按模型逐颗比，加一种图形不会漏亮
        foreach (var (tool, button) in _shapeButtons) Highlight(button, CanvasService.Tool == tool);
        Highlight(ThinButton, CanvasService.WidthStep == 0);
        Highlight(MediumButton, CanvasService.WidthStep == 1);
        Highlight(ThickButton, CanvasService.WidthStep == 2);
        Highlight(ThroughButton, CanvasService.IsClickThrough, strong: false);
        Highlight(HaloButton, CanvasService.HaloEnabled, strong: false);
        Highlight(KeysButton, CanvasService.IsHotkeyPanelOpen, strong: false);
        for (var index = 0; index < _colourButtons.Count; index++)
            _colourButtons[index].Background = index == CanvasService.ColorIndex ? ActiveBrush : IdleBrush;

        // 状态行要说清"现在这一按会发生什么"——三态最容易混的就是这里
        Status.Text = StatusText();
        // 文本一变，行高就可能变（穿透那句会折成两行）→ 窗尺寸跟着重算，位置保持不动
        Fit(centerOnScreen: false);
        // 画布每屏一块也是 TOPMOST，topmost 链里谁最后被提谁在上。每次刷新都补一发：
        // 条子被画布盖住＝唯一的鼠标出口消失（真机"看不见也点不到"就是这么来的）
        RaiseAboveCanvas();
    }

    /// <summary>
    /// 状态行要说清"<b>现在这一按会发生什么</b>"——三态里最容易混的就是"穿透态下选了荧光笔"
    /// （看着什么都没开，其实按住就能画）。只写"穿透中"会让人以为功能坏了。
    /// </summary>
    private string StatusText()
    {
        // 刚发生过"让位"就先说这一句：用户此刻最需要知道的是"为什么刚才还能画"，
        // 而不是那行常态说明（下一次自己动工具/穿透时这句话就翻篇）
        if (CanvasService.Notice is { } note) return note;
        var tool = CanvasService.Tool;
        var ink = $"{tool.Name()} · {CanvasService.WidthStep + 1} 档 · {CanvasService.Palette[CanvasService.ColorIndex].Name}";
        if (!CanvasService.IsClickThrough)
            // 折线是唯一"跨按还开着"的：不在这行说怎么收，用户就只能靠试——而试出来的那一下是 Esc＝退出画布
            return $"绘制中（鼠标归画布）：{ink}。点「穿透」或右键交出鼠标"
                   + (tool == CanvasTool.PolyLine ? "；勾折线时 Esc／再点「折线」＝收口这一条" : string.Empty);
        if (tool == CanvasTool.Highlighter) return $"穿透中 + 荧光笔已选：按住左键即画、松开自动穿透；{ink}";
        // 图形和画笔一样要真握住鼠标才画得出来。穿透态下选了它却不说明，就是"点了矩形、拖了半天什么都没画、
        // 还以为软件坏了"——那句"这一按仍归下层应用"是这条链上唯一能挡住这种误会的出口。
        if (tool.IsShape()) return $"穿透中 + {tool.Name()}已选：现在这一按仍归下层应用；点「穿透」收回鼠标再画{tool.Name()}";
        return $"穿透中：下层应用照常操作。{ink}；按住 Ctrl+Alt 可直接圈画，点「画笔」进入留痕模式";
    }

    private static void Highlight(Button button, bool on, bool strong = true)
        => button.Background = on ? (strong ? ActiveBrush : HaloBrush) : IdleBrush;

    // ────────── 拖动 ──────────
    //
    // 全程用物理像素：XAML 给的是 DIP，而这条窗可能出现在任何一块缩放的屏上。
    // 按下时记下"光标与窗左上角的差"，之后每一步都用 GetCursorPos 减掉它——
    // 一次 DIP→物理的来回换算就是"拖到副屏上越拖越偏"的那个偏。

    private void GripPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = true;
        Grip.CapturePointer(e.Pointer);
        if (!WindowInterop.GetCursorPos(out var cursor)) { _dragging = false; return; }
        var rect = WindowInterop.GetWindowRect(this);
        _grabOffsetX = cursor.X - rect.X;
        _grabOffsetY = cursor.Y - rect.Y;
    }

    private void GripMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        if (!WindowInterop.GetCursorPos(out var cursor)) return;
        // 拖动途中也要压住画布：绘制态下画布是整块能吃到鼠标的玻璃，条子一旦被它盖住就"拖着拖着点不到了"
        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), WindowInterop.HWND_TOP,
            cursor.X - _grabOffsetX, cursor.Y - _grabOffsetY, 0, 0,
            WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
    }

    private void GripReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        Grip.ReleasePointerCapture(e.Pointer);
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Esc 是两级的：正在勾折线时先收口那一条（板子留着），没有手上一半的东西才退出画布。
        // 只有一级会让"想停下这条折线"变成"整块板子连笔画一起没了"——退出即丢弃，那是最贵的一次误按。
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            CanvasService.Escape();
        }
    }
}
