#nullable enable
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using StarMark.Integrations.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace StarMark.UI.Views;

/// <summary>
/// 截图遮罩窗：显示"按下热键那一刻"的整屏帧，让用户拖动框选，放开后复制或存图。
/// <para>
/// 生命周期是<b>一次性</b>的：每次截图新建每屏一个窗、结束即关，不留常驻窗池（D3 裁决）。
/// 常驻隐藏窗池能省几十毫秒首帧，代价是永远有一批顶层窗口挂在每台显示器上。
/// </para>
/// <para>
/// <b>每条退出路径都必须把会话收干净</b>：漏一条就等于把用户摁在一层吃满屏幕的顶层窗里，
/// 只能去任务管理器杀进程。因此 Esc / 右键 / 点取消 / 窗口被外部关闭 都走同一个
/// <see cref="_finish"/> 回调（<see cref="Settle"/> 保证只回调一次），由服务统一关窗。
/// </para>
/// </summary>
public sealed partial class CaptureOverlayWindow : Window
{
    private const double Action_bar_width = 210;
    private const double Action_bar_height = 44;

    private readonly ScreenFrame _frame;
    private readonly IntRect _monitor;          // 本屏在虚拟桌面里的物理矩形
    private readonly double _scale;             // 本屏 DPI 缩放（1.0 / 1.25 / 1.5 …）
    private readonly Action<CaptureOverlayWindow, IntRect?> _finish;

    private PointInt32 _startPhysical;
    private IntRect? _selection;                // 虚拟桌面物理像素
    private bool _awaitingRelease;
    private bool _settled;

    /// <param name="monitorDevice">本窗负责的显示器设备名（失活时只认自己这屏的失活）。</param>
    public CaptureOverlayWindow(
        ScreenFrame frame,
        (string Device, RectInt32 Bounds, double Scale) monitor,
        Action<CaptureOverlayWindow, IntRect?> finish)
    {
        _frame = frame;
        _monitor = new IntRect(monitor.Bounds.X, monitor.Bounds.Y, monitor.Bounds.Width, monitor.Bounds.Height);
        _scale = monitor.Scale <= 0 ? 1.0 : monitor.Scale;
        _finish = finish;
        DeviceName = monitor.Device;

        InitializeComponent();

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

    // ────────── 鼠标：按下 → 拖动 → 放开 ──────────

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_settled) return;
        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed) return;

        _startPhysical = ToPhysical(point.Position.X, point.Position.Y);
        _awaitingRelease = true;
        _selection = null;
        HintChip.Visibility = Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Collapsed;
        ErrorChip.Visibility = Visibility.Collapsed;
        Root.CapturePointer(e.Pointer);      // 拖出窗口边界也要继续收到 Moved/Released
        DrawSelection(new IntRect(_startPhysical.X, _startPhysical.Y, 0, 0));
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_awaitingRelease || _settled) return;
        var position = e.GetCurrentPoint(Root).Position;
        var current = ToPhysical(position.X, position.Y);
        var selection = CaptureGeometry.Normalize(_startPhysical.X, _startPhysical.Y, current.X, current.Y);
        // 夹回本屏：L1 按"每屏各截各的"处理跨屏拖拽（每屏一个遮罩窗，各拿各的选区，互不合并）
        _selection = CaptureGeometry.Intersect(selection, _monitor) ?? selection;
        if (_selection is { } box) DrawSelection(box);
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
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
        ActionBar.Visibility = Visibility.Visible;
        PositionActionBar(selection);
        CopyButton.Focus(FocusState.Programmatic);
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
                Commit(Copy: true);
                break;
            case VirtualKey.S when IsControlDown():
                e.Handled = true;
                Commit(Copy: false);
                break;
        }
    }

    private static bool IsControlDown()
        => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

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

    private void PositionActionBar(IntRect selection)
    {
        var (x, y, w, h) = ToDip(selection);
        var screenWidth = _monitor.Width / _scale;
        var screenHeight = _monitor.Height / _scale;
        var left = Math.Clamp(x + w - Action_bar_width, 4, Math.Max(4, screenWidth - Action_bar_width - 4));
        var below = y + h + 6 + Action_bar_height <= screenHeight;
        PlaceByMargin(ActionBar, left, below ? y + h + 6 : Math.Max(4, y - Action_bar_height - 6));
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
        ActionBar.Visibility = Visibility.Collapsed;
    }

    // ────────── 动作 ──────────

    private void Copy_Click(object sender, RoutedEventArgs e) => Commit(Copy: true);

    private void Save_Click(object sender, RoutedEventArgs e) => Commit(Copy: false);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Settle(null);

    /// <summary>
    /// 提交这一屏的选区。<paramref name="copy"/> 为真走剪贴板，否则存成文件。
    /// 先 Settle 再干活：服务收到结果就会关掉所有遮罩窗（包括本窗），
    /// 反过来先干活会让用户在裁图期间还被困在暗幕里。
    /// </summary>
    private void Commit(bool Copy)
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
        Settle(selection);
        if (Copy) _ = ScreenshotService.CopySelectionAsync(_frame, selection);
        else _ = ScreenshotService.SaveSelectionAsync(_frame, selection);
    }
}
