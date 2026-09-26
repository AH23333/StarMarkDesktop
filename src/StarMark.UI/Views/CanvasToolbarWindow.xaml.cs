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
    /// 重新提到最上层。<b>必须用 HWND_TOP 而不是 HWND_TOPMOST</b>：这条窗本来就在 topmost 带里，
    /// 对这样的窗口再传 HWND_TOPMOST 只"换带"、不在带内重排＝什么都没做
    /// （真机症状：画布压在工具条上面，条上每颗按钮都点不动，而它是唯一看得见的出口）。
    /// </summary>
    public void RaiseAboveCanvas()
    {
        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), WindowInterop.HWND_TOP,
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

    private void Refresh()
    {
        // 按钮高亮只反映"现在是哪一个"，不反映悬停：两套状态画在一起就分不清了
        Highlight(PenButton, CanvasService.Tool == CanvasTool.Pen);
        Highlight(MarkerButton, CanvasService.Tool == CanvasTool.Highlighter);
        Highlight(EraserButton, CanvasService.Tool == CanvasTool.Eraser);
        Highlight(ThinButton, CanvasService.WidthStep == 0);
        Highlight(MediumButton, CanvasService.WidthStep == 1);
        Highlight(ThickButton, CanvasService.WidthStep == 2);
        Highlight(ThroughButton, CanvasService.IsClickThrough, strong: false);
        Highlight(HaloButton, CanvasService.HaloEnabled, strong: false);
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
        var tool = CanvasService.Tool;
        var ink = $"{ToolName(tool)} · {CanvasService.WidthStep + 1} 档 · {CanvasService.Palette[CanvasService.ColorIndex].Name}";
        if (!CanvasService.IsClickThrough) return $"绘制中（鼠标归画布）：{ink}。点「穿透」或右键交出鼠标";
        if (tool == CanvasTool.Highlighter) return $"穿透中 + 荧光笔已选：按住左键即画、松开自动穿透；{ink}";
        return $"穿透中：下层应用照常操作。{ink}；按住 Ctrl+Alt 可直接圈画，点「画笔」进入留痕模式";
    }

    private static string ToolName(CanvasTool tool) => tool switch
    {
        CanvasTool.Highlighter => "荧光笔",
        CanvasTool.Eraser => "橡皮",
        _ => "画笔",
    };

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
        // Esc 退出画布模式（不是"收笔"——画布上没有需要逐级撤回的编辑态）
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            CanvasService.Stop();
        }
    }
}
