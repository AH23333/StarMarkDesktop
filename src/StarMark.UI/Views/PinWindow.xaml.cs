#nullable enable
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using Windows.System;

namespace StarMark.UI.Views;

/// <summary>
/// 一张贴图：把截下来的那块画面常驻在桌面上，可以拖、可以滚轮缩放、可以点穿过去。
/// <para>
/// <b>几何全部走物理像素</b>（<see cref="WindowInterop.SetWindowPos"/>）：源图像素与屏幕上那块区域
/// 是 1:1 的，所以 1× 时窗口的物理尺寸就等于选区的物理尺寸，<b>不该再乘一次 DPI 缩放</b>。
/// 用 AppWindow 的 DIP 接口在 150% 屏上会让贴图比原物大一半。
/// </para>
/// <para>
/// 生命周期由 <see cref="PinManager"/> 管：本窗自己不知道上限、隐藏与穿透的全局状态，
/// 关闭时只负责把自己从名册里摘掉（<see cref="Window.Closed"/>）。
/// </para>
/// </summary>
public sealed partial class PinWindow : Window
{
    private readonly byte[] _bgra;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;

    private double _zoom = 1.0;
    private double _scale = 1.0;
    private bool _dragging;
    private bool _clickThrough;
    private (double X, double Y) _grabDip;

    /// <param name="placement">贴在虚拟桌面里的物理矩形（就是刚框选的那块区域）。</param>
    public PinWindow(byte[] bgra, int width, int height, IntRect placement)
    {
        _bgra = bgra;
        _sourceWidth = Math.Max(1, width);
        _sourceHeight = Math.Max(1, height);

        InitializeComponent();
        WindowInterop.RemoveDefaultWindowFrame(this);

        // 贴图不进任务栏、不进 Alt+Tab：一屏贴十几张时那两处会被占满，而它们是一次性的工具窗，
        // 用户找回它们靠的是"看得见的那张图"本身，不是任务栏按钮。
        var hwnd = WindowInterop.GetHwnd(this);
        WindowInterop.SetWindowLong(hwnd, WindowInterop.GWL_EXSTYLE,
            new IntPtr(WindowInterop.GetWindowLong(hwnd, WindowInterop.GWL_EXSTYLE).ToInt64()
                | WindowInterop.WS_EX_TOOLWINDOW));

        if (!TryPaint())
        {
            // 画不出来就别留一扇黑窗：贴图窗没有别的可看内容，留着只会挡地方
            Close();
            throw new InvalidOperationException("贴图没能显示出来（像素缓冲与位图尺寸不符或内存不足）");
        }

        var (w, h) = CaptureGeometry.PinPixelSize(_sourceWidth, _sourceHeight, _zoom);
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOPMOST,
            placement.X, placement.Y, w, h,
            WindowInterop.SWP_SHOWWINDOW | WindowInterop.SWP_NOACTIVATE);
        _scale = WindowInterop.GetScale(this);

        // 抢一次焦点：贴图窗要能直接按 Esc 关掉（Snipaste 同做法）。不激活的话 Esc 永远收不到，
        // 而"只能右键才能关"在贴图铺满屏幕时是最难受的那种死法。
        Activate();

        Closed += (_, _) => PinManager.Unregister(this);
    }

    /// <summary>当前是否处于鼠标穿透（名册要用它决定托盘勾选项的勾选态）。</summary>
    public bool IsClickThrough => _clickThrough;

    private bool TryPaint()
    {
        try
        {
            var bitmap = new WriteableBitmap(_sourceWidth, _sourceHeight);
            using (var stream = bitmap.PixelBuffer.AsStream())
                stream.Write(_bgra, 0, _bgra.Length);
            bitmap.Invalidate();
            Shot.Source = bitmap;
            return true;
        }
        catch (Exception ex)
        {
            StarLog.Error("[Pin] 位图未能显示", ex);
            return false;
        }
    }

    // ────────── 缩放 ──────────

    private void Root_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var next = CaptureGeometry.NextZoom(_zoom, e.GetCurrentPoint(Root).Properties.MouseWheelDelta);
        if (Math.Abs(next - _zoom) < 0.0001) { SyncBadge(); return; }   // 已在端点：窗不动，但角标要说清现在几倍
        _zoom = next;
        ResizeKeepingCenter();
        SyncBadge();
        e.Handled = true;
    }

    /// <summary>改尺寸时按住中心：否则每滚一档窗就往右下跑，图很快"看不见了"。</summary>
    private void ResizeKeepingCenter()
    {
        var current = WindowInterop.GetWindowRect(this);
        var (w, h) = CaptureGeometry.PinPixelSize(_sourceWidth, _sourceHeight, _zoom);
        WindowInterop.SetWindowPos(
            WindowInterop.GetHwnd(this), IntPtr.Zero,
            current.X + (current.Width - w) / 2, current.Y + (current.Height - h) / 2, w, h,
            WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE);
    }

    // ────────── 拖动 ──────────

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed) return;
        // 每开始一次拖动都重取缩放：把贴图拖到另一块 DPI 不同的屏上时，
        // "DIP 位移 × 缩放"要用**当下这块屏**的缩放，用开局那块会越拖越偏。
        _scale = WindowInterop.GetScale(this);
        _dragging = true;
        _grabDip = (point.Position.X, point.Position.Y);
        Root.CapturePointer(e.Pointer);
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        var position = e.GetCurrentPoint(Root).Position;
        var current = WindowInterop.GetWindowRect(this);
        var dx = (int)Math.Round((position.X - _grabDip.X) * _scale, MidpointRounding.AwayFromZero);
        var dy = (int)Math.Round((position.Y - _grabDip.Y) * _scale, MidpointRounding.AwayFromZero);
        if (dx == 0 && dy == 0) return;
        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), IntPtr.Zero,
            current.X + dx, current.Y + dy, 0, 0,
            WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE);
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Root.ReleasePointerCapture(e.Pointer);
    }

    // ────────── 右键菜单 / 键盘 ──────────

    private void Root_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        FlyoutBase.ShowAttachedFlyout(Root);
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        Close();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
        => _ = ScreenshotService.CopyPixelsAsync(_bgra, _sourceWidth, _sourceHeight);

    private void Save_Click(object sender, RoutedEventArgs e)
        => _ = ScreenshotService.SavePixelsAsync(_bgra, _sourceWidth, _sourceHeight);

    private void Ocr_Click(object sender, RoutedEventArgs e)
        => _ = OcrService.CopyTextFromPixelsAsync(_bgra, _sourceWidth, _sourceHeight, "贴图识字");

    private void ClickThrough_Click(object sender, RoutedEventArgs e) => PinManager.ToggleClickThrough();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ────────── 由名册调用的状态同步 ──────────

    /// <summary>
    /// 把这一张放回屏幕。显示走"原生 ShowWindow 兜一遍"：批次 D4 量过 WinUI 的显示调用
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

    /// <summary>套用穿透状态并回报有没有真的套上。<b>失败不能静默</b>：那时用户看到的是"点了没反应"。</summary>
    public bool ApplyClickThrough(bool on)
    {
        var ok = WindowInterop.SetClickThrough(this, on);
        _clickThrough = ok ? on : _clickThrough;
        SyncBadge();
        return ok;
    }

    private void SyncBadge()
    {
        ThroughItem.Text = _clickThrough ? "取消鼠标穿透（所有贴图）" : "鼠标穿透（所有贴图）";
        var zoomText = CaptureGeometry.FormatZoom(_zoom);
        var text = _clickThrough ? zoomText + " · 已穿透" : zoomText;
        // 100% 且没穿透＝刚贴上的原样，不必顶一个角标挡画面
        var isDefault = Math.Abs(_zoom - 1.0) < 0.0001 && !_clickThrough;
        BadgeText.Text = text;
        Badge.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
    }
}
