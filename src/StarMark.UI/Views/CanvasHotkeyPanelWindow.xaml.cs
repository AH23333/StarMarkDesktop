#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using StarMark.Abstractions.Capture;
using StarMark.Core.Hotkeys;
using StarMark.UI.Helpers;
using StarMark.UI.Services;

namespace StarMark.UI.Views;

/// <summary>
/// 画布的快捷键面板（发起人 2026-09-26 点名的兜底："防止用户忘记快捷键，可以随时打开查看"）。
/// <para>
/// 三件事决定它长得这样：① <b>每屏一份键位表</b>不存在——它只有一块，钉在工具条下面；
/// ② <b>它自己不穿透，且永远在画布之上</b>，否则就成了又一件"看得见点不到"的东西（工具条那条教训）；
/// ③ 行是<b>从动作目录 + 真实绑定生成</b>的，不在 XAML 里手写一行字面键位——写死的那份迟早和注册表
/// 分岔，而分岔的症状恰恰是"面板上印着一个键，按了没反应"，正是这块面板要防的事。
/// </para>
/// </summary>
public sealed partial class CanvasHotkeyPanelWindow : Window
{
    /// <summary>与工具条之间的缝隙（DIP）。</summary>
    private const double GapDip = 6;

    private readonly List<FrameworkElement> _rows = new();

    private IntRect _screen;
    private double _scale = 1d;
    private bool _dragging;
    private int _grabOffsetX;
    private int _grabOffsetY;

    public CanvasHotkeyPanelWindow()
    {
        InitializeComponent();
        WindowInterop.RemoveDefaultWindowFrame(this);
        Grip.PointerPressed += GripPressed;
        Grip.PointerMoved += GripMoved;
        Grip.PointerReleased += GripReleased;
        Grip.PointerCaptureLost += (_, _) => _dragging = false;
        // 窗可能从别处被关掉（退出画布、系统收尾）：让编排那边把它忘掉，
        // 否则"再点 ⌨ 就打不开"——那条链上挂着一个已经没了的窗。
        Closed += (_, _) => CanvasService.PanelClosed(this);
    }

    public IntPtr Hwnd => WindowInterop.GetHwnd(this);

    /// <summary>
    /// 把自己插到 <paramref name="insertAbove"/> <b>之下</b>（同一 topmost 带内）。
    /// Win32 的 <c>hwndInsertAfter</c> 原话是"the window to <b>precede</b> the positioned window
    /// in the Z order"，Z 序自上而下数 ⇒ 传进来的那个在上面。
    /// </summary>
    public void PlaceUnder(IntPtr insertAbove)
    {
        if (insertAbove == IntPtr.Zero) return;
        WindowInterop.SetWindowPos(Hwnd, insertAbove, 0, 0, 0, 0,
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
    }

    /// <summary>
    /// 出现在工具条下面。顺序照批次 WC-4 那条教训：<b>先亮窗 → 排版 → 才量尺寸</b>
    /// （窗没亮过时量到的是没套模板的空壳尺寸，面板会被截成一条）。
    /// </summary>
    public void ShowAt(IntRect anchorBelow, IntRect screen, double scale)
    {
        _screen = screen;
        _scale = scale <= 0 ? 1 : scale;
        RebuildRows();

        AppWindow.Show();
        Root.UpdateLayout();
        Fit(anchorBelow);
    }

    /// <summary>关掉面板（画布继续开着——这块面板只是"看一眼"）。</summary>
    public void ClosePanel()
    {
        Close();
    }

    /// <summary>键位表：动作名取自 <see cref="HotkeyActions.DisplayName"/>，键位取自当前绑定。</summary>
    private void RebuildRows()
    {
        foreach (var row in _rows) Rows.Children.Remove(row);
        _rows.Clear();
        foreach (var action in HotkeyActions.Canvas)
        {
            var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };
            grid.Children.Add(new TextBlock
            {
                Text = HotkeyActions.DisplayName(action),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 10, 1),
            });
            grid.Children.Add(new TextBlock
            {
                Text = CanvasService.BindingText(action),
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = KeyBrush,
            });
            _rows.Add(grid);
            Rows.Children.Add(grid);
        }
    }

    private static readonly SolidColorBrush KeyBrush = new(Windows.UI.Color.FromArgb(0xFF, 0x9C, 0xD4, 0xFF));

    private void Fit(IntRect anchorBelow)
    {
        var screen = _screen;
        var scale = _scale;
        if (screen.Width <= 0 || screen.Height <= 0) return;

        var availableDip = Math.Max(280, screen.Width / scale - 24);
        Root.Measure(new Windows.Foundation.Size(availableDip, double.PositiveInfinity));
        var wanted = Root.DesiredSize;
        var width = Math.Min((int)Math.Round(wanted.Width * scale), Math.Max(1, screen.Width));
        var height = (int)Math.Round(wanted.Height * scale);

        var x = anchorBelow.X;
        var y = anchorBelow.Bottom + (int)Math.Round(GapDip * scale);
        // 夹回屏内：面板下面通常就是任务栏那一头，宁可上移也不要出现"半截在屏外的键位表"
        x = Math.Clamp(x, screen.X, Math.Max(screen.X, screen.Right - width));
        y = Math.Clamp(y, screen.Y, Math.Max(screen.Y, screen.Bottom - height));

        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), WindowInterop.HWND_TOP, x, y, width, height,
            WindowInterop.SWP_SHOWWINDOW | WindowInterop.SWP_NOACTIVATE);
        WindowInterop.ShowWindow(WindowInterop.GetHwnd(this), WindowInterop.SW_SHOWNOACTIVATE);
    }

    // ────────── 拖动（面板会挡住要讲的画面）──────────

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
        var x = Math.Clamp(cursor.X - _grabOffsetX, _screen.X, Math.Max(_screen.X, _screen.Right - 40));
        var y = Math.Clamp(cursor.Y - _grabOffsetY, _screen.Y, Math.Max(_screen.Y, _screen.Bottom - 40));
        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), WindowInterop.HWND_TOP, x, y, 0, 0,
            WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
    }

    private void GripReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        Grip.ReleasePointerCapture(e.Pointer);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CanvasService.HideHotkeyPanel();
}
