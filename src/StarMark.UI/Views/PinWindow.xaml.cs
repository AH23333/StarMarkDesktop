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
    private byte[] _bgra;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;

    private double _zoom = 1.0;
    private bool _dragging;
    private bool _clickThrough;

    /// <summary>
    /// 正在被就地编辑：这期间这扇窗收起，由编辑窗盖在它原来的位置上。
    /// 这个旗标不是为了好看——不挡住重复进入的话，同一张贴图会被开两次编辑，
    /// 后写回的那份把先写的悄悄覆盖掉（两次写回＝丢一次改动）。
    /// </summary>
    public bool IsEditing { get; private set; }

    /// <summary>贴图的源尺寸（＝标注编辑器交回来必须是这个尺寸）。</summary>
    public int SourceWidth => _sourceWidth;
    public int SourceHeight => _sourceHeight;

    /// <summary>当前显示倍率。编辑时按它换算"鼠标在哪、笔落在哪个像素上"。</summary>
    public double Zoom => _zoom;

    /// <summary>当前显示在屏幕上的物理矩形（已含缩放）——编辑窗要一模一样地盖在它上面。</summary>
    public IntRect DisplayRect
    {
        get
        {
            var r = WindowInterop.GetWindowRect(this);
            return new IntRect(r.X, r.Y, r.Width, r.Height);
        }
    }

    /// <summary>所在显示器的 DPI 缩放（编辑窗把 DIP 换算成物理像素用）。</summary>
    public double DpiScale => WindowInterop.GetScale(this);

    /// <summary>交给编辑器的底图：<b>一份副本</b>。编辑器会拿它当"重烤标注的起点"，
    /// 把原件交出去就等于允许就地改写一张已经钉在桌面上的图——写回只应发生在用户点「完成」那一刻。</summary>
    public byte[] PixelsForEdit() => (byte[])_bgra.Clone();

    /// <summary>按下瞬间的光标物理坐标与窗口矩形：拖动全程按这两份快照重算绝对位置。</summary>
    private WindowInterop.POINT _gestureStartCursor;
    private Windows.Graphics.RectInt32 _gestureStartRect;
    private int _lastAppliedX = int.MinValue;
    private int _lastAppliedY = int.MinValue;

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

    // ────────── 就地编辑（复用截图那条标注链）──────────

    /// <summary>
    /// 进入编辑：本窗收起，由 <see cref="ScreenshotService.EditPin"/> 在同一块矩形上开一扇编辑窗。
    /// <para>收起而不是"留在底下"：编辑窗是不透明的同一份画面，留一张在底下只是多占一份像素，
    /// 而且万一两者差一像素，用户会看到一条重影边。</para>
    /// </summary>
    public void EnterEditing()
    {
        if (IsEditing) return;
        IsEditing = true;
        HidePin();
    }

    /// <summary>
    /// 编辑结束。<paramref name="pixels"/> 非空＝把这一份写回这张图（尺寸必须与源一致，
    /// 不一致就是哪一步把尺寸改了，那种图不能往桌面上贴——它会与窗口的尺寸对不上，显示成拉伸过的样子）。
    /// </summary>
    public void EndEditing(byte[]? pixels, bool closePin)
    {
        if (!IsEditing) return;
        IsEditing = false;
        if (closePin)
        {
            try { Close(); }
            catch (Exception ex) { StarLog.Error("[Pin] 关闭贴图失败", ex); }
            return;
        }
        if (pixels is { } edited)
        {
            // 尺寸判据在 Core（CaptureGeometry.PinEditProblem）：那是"能不能写回"的唯一说法，
            // 贴图与将来的其它回写点都该问它，而不是各写一份字节数比较。
            if (CaptureGeometry.PinEditProblem(_sourceWidth, _sourceHeight, edited) is { } problem)
                TrayReporter.Report("贴图编辑", "没能写回", problem + "（已保留原来那张）");
            else
            {
                _bgra = edited;
                if (!TryPaint())
                    TrayReporter.Report("贴图编辑", "已写回但没能刷新显示", "按 Esc 关掉这张，再重截一张");
            }
        }
        Present();
    }

    /// <summary>右键菜单 / 双击 / 按 E：进入就地编辑。</summary>
    private void Edit_Click(object sender, RoutedEventArgs e) => ScreenshotService.EditPin(this);

    private void Root_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // 双击＝进入编辑（Snipaste 同口径）。拖动那条链用的是按下-移动-放开，
        // 一次双击会被它当成"原地没动的拖动"，两者不冲突。
        e.Handled = true;
        ScreenshotService.EditPin(this);
    }

    // ────────── 缩放 ──────────

    private void Root_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var next = CaptureGeometry.NextZoom(_zoom, e.GetCurrentPoint(Root).Properties.MouseWheelDelta);
        if (Math.Abs(next - _zoom) < 0.0001) { SyncBadge(); return; }   // 已在端点：窗不动，但角标要说清现在几倍
        _zoom = next;
        ResizeAnchoringTopLeft();
        SyncBadge();
        e.Handled = true;
    }

    /// <summary>
    /// 改尺寸时<b>钉住左上角</b>（Snipaste 口径），再按 <see cref="CaptureGeometry.PinOrigin"/> 收边。
    /// <para>原来是绕窗口中心缩放，那正是真机反馈"过度放大后贴图跑出屏幕外、再也看不见"的成因：
    /// 贴图允许拖出屏，中心一旦在屏外，往下缩只是围着那个屏外的中心收拢，整块永远回不来。
    /// 钉住左上角之后，"缩到能塞进屏幕"的那一刻，收边规则会把左上角自动带回屏幕边缘。</para>
    /// </summary>
    private void ResizeAnchoringTopLeft()
    {
        var current = WindowInterop.GetWindowRect(this);
        var (w, h) = CaptureGeometry.PinPixelSize(_sourceWidth, _sourceHeight, _zoom);
        var (x, y) = CaptureGeometry.PinOrigin(current.X, current.Y, w, h, WorkArea());
        WindowInterop.SetWindowPos(
            WindowInterop.GetHwnd(this), IntPtr.Zero,
            x, y, w, h,
            WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE);
    }

    /// <summary>所在显示器的工作区（物理像素，已扣掉任务栏）。多屏按"最近那块"取，与拖动的快照同一口径。</summary>
    private IntRect WorkArea()
    {
        var area = WindowInterop.GetWorkArea(this);
        return new IntRect(area.X, area.Y, area.Width, area.Height);
    }

    // ────────── 拖动 ──────────

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) return;
        // 按下瞬间把"光标物理坐标"和"窗口矩形"各存一份，之后每帧都从这两份快照重算绝对位置。
        // 用坐标增量累加会抖：窗一移动，指针相对窗的位置就变了，
        // 下一次再叠上去等于把同一段位移重复应用——越拖越 runaway。
        WindowInterop.GetCursorPos(out _gestureStartCursor);
        _gestureStartRect = WindowInterop.GetWindowRect(this);
        _dragging = true;
        Root.CapturePointer(e.Pointer);
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        WindowInterop.GetCursorPos(out var cursor);
        // 全程物理像素：光标坐标与窗口矩形同一单位，不需要 DPI 缩放因子，
        // 于是"拖到另一块缩放不同的屏上就越来越偏"这一类错误结构上不存在。
        var x = _gestureStartRect.X + cursor.X - _gestureStartCursor.X;
        var y = _gestureStartRect.Y + cursor.Y - _gestureStartCursor.Y;
        // 同一条收边判据（与缩放共用）：贴图可以拖出屏去看想看的部分，但不许整块丢光——
        // "整块在屏外"就是这次反馈里"再也看不见"的另一条来路，只修缩放等于留一半。
        var (cx, cy) = CaptureGeometry.PinOrigin(x, y, _gestureStartRect.Width, _gestureStartRect.Height, WorkArea());
        if (cx == _lastAppliedX && cy == _lastAppliedY) return;
        _lastAppliedX = cx;
        _lastAppliedY = cy;
        WindowInterop.SetWindowPos(WindowInterop.GetHwnd(this), IntPtr.Zero, cx, cy, 0, 0,
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
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }
        // E＝进入就地编辑。贴图窗收得到键盘（建窗时抢过一次焦点），
        // 所以"想改这张图"不必先把鼠标挪到右键菜单上。
        if (e.Key == VirtualKey.E)
        {
            e.Handled = true;
            ScreenshotService.EditPin(this);
        }
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
        ThroughItem.Text = _clickThrough ? "取消鼠标穿透（F5）" : "鼠标穿透（开启后用 F5 恢复）";
        var zoomText = CaptureGeometry.FormatZoom(_zoom);
        // 穿透时必须把出口写在图上：这张窗收不到鼠标，用户若不知道 F5 就只剩"托盘关掉全部"这一条粗路
        var text = _clickThrough ? zoomText + " · 已穿透，按 F5 恢复" : zoomText;
        // 100% 且没穿透＝刚贴上的原样，不必顶一个角标挡画面
        var isDefault = Math.Abs(_zoom - 1.0) < 0.0001 && !_clickThrough;
        BadgeText.Text = text;
        Badge.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
    }
}
