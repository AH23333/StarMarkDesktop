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
    /// <summary>条子的尺寸（DIP）。物理尺寸按本屏缩放换算，混合 DPI 下才不会出现"半截在屏外"。</summary>
    private const double WidthDip = 720;
    private const double HeightDip = 88;
    private const double TopMarginDip = 20;

    private static readonly SolidColorBrush ActiveBrush = new(Color.FromArgb(0xFF, 0x2D, 0x6E, 0xC4));
    private static readonly SolidColorBrush IdleBrush = new(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush HaloBrush = new(Color.FromArgb(0x55, 0x2D, 0x6E, 0xC4));

    private readonly List<Button> _colourButtons = new();

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

    /// <summary>摆在所在屏的顶部居中。<paramref name="scale"/> 必须是那块屏自己的缩放。</summary>
    public void ShowAt(IntRect screen, double scale)
    {
        scale = scale <= 0 ? 1 : scale;
        var width = (int)Math.Round(WidthDip * scale);
        var height = (int)Math.Round(HeightDip * scale);
        var x = screen.X + (int)Math.Round((screen.Width - width) / 2d);
        var y = screen.Y + (int)Math.Round(TopMarginDip * scale);

        AppWindow.Show();
        var hwnd = WindowInterop.GetHwnd(this);
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOPMOST, x, y, width, height,
            WindowInterop.SWP_SHOWWINDOW | WindowInterop.SWP_NOACTIVATE);
        WindowInterop.ShowWindow(hwnd, WindowInterop.SW_SHOWNOACTIVATE);
        Refresh();
    }

    public void CloseToolbar()
    {
        CanvasService.StateChanged -= Refresh;
        Close();
    }

    // ────────── 动作 ──────────

    private void Pen_Click(object sender, RoutedEventArgs e) => CanvasService.SelectTool(CanvasTool.Pen);

    private void Marker_Click(object sender, RoutedEventArgs e) => CanvasService.SelectTool(CanvasTool.Highlighter);

    private void Eraser_Click(object sender, RoutedEventArgs e) => CanvasService.SelectTool(CanvasTool.Eraser);

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

        Status.Text = CanvasService.IsClickThrough
            ? "穿透中：鼠标归下面的应用。再点「穿透」、按热键或托盘都能回到绘制"
            : $"{ToolName(CanvasService.Tool)} · {CanvasService.WidthStep + 1} 档 · {CanvasService.Palette[CanvasService.ColorIndex].Name}";
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
        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), WindowInterop.HWND_TOPMOST,
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
