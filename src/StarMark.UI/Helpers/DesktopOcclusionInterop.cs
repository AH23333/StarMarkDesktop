#nullable enable
using System;
using System.Runtime.InteropServices;
using StarMark.Core.Widgets;

namespace StarMark.UI.Helpers;

/// <summary>
/// 整屏遮挡感知（片281）缺的那几件 Win32 互操作。
/// <para><see cref="WindowInterop"/> 已有的（EnumWindows/GetClassName/GetAncestor/GetWindowLong/
/// MonitorFromWindow/GetMonitorInfoW/IsWindowVisible…）这里不重复。本档只补遮挡评估缺的：
/// IsZoomed/IsIconic、DWM CLOAKED、近透明分层窗的 alpha、独占全屏口径
/// （<c>SHQueryUserNotificationState</c> 只认 3|4，同 DeskBox，不含 BUSY），以及把屏几何
/// 打包成 Core 的 <see cref="MonitorGeometry"/>。</para>
/// </summary>
internal static class DesktopOcclusionInterop
{
    /// <summary>MonitorFromWindow 的"探不到给 0"口径——探不到屏的窗不参与遮挡评估（最近屏口径会把离屏窗硬算进来）。</summary>
    public const uint MONITOR_DEFAULTTONULL = 0x00000000;

    /// <summary>DWM：窗口是否被更高层机制藏起（UWP 挂起、虚拟桌面切走）——cloaked 窗 IsWindowVisible 仍真但不参与遮挡。</summary>
    public const int DWMWA_CLOAKED = 14;

    /// <summary><see cref="WindowInterop"/> 没有的那一位扩展样式：有它才进任务栏/Alt+Tab。
    /// 工具窗排除里"TOOLWINDOW 但没有 APPWINDOW"用它当另一半（DeskBox 同款组合判定）。</summary>
    public const long WS_EX_APPWINDOW = 0x00040000;

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeInt(IntPtr hwnd, int attr, out int value, int cbAttribute);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int attr, out WindowInterop.RECT rect, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint crKey, out byte bAlpha, out uint dwFlags);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    /// <summary>这扇窗是不是我们自家进程的：前台判定（自家窗在前台＝谈不上被盖）与候选排除共用。</summary>
    public static bool IsOwnProcessWindow(IntPtr hwnd)
    {
        _ = WindowInterop.GetWindowThreadProcessId(hwnd, out var pid);
        return pid != 0 && pid == (uint)Environment.ProcessId;
    }

    /// <summary>窗口是否被 cloak（DWM 层面被藏起来，IsWindowVisible 仍为 true 的那种）。</summary>
    public static bool IsWindowCloaked(IntPtr hwnd)
    {
        try
        {
            return DwmGetWindowAttributeInt(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0
                   && cloaked != 0;
        }
        catch { return false; }   // DWM 不可用（极旧系统）按未 cloak 算
    }

    /// <summary>近透明分层窗的 alpha（0..255）；不是按 alpha 调的（或读不到）给 255＝永不因它跳过候选。
    /// DeskBox 口径：alpha ≤ 16 的分层浮层近似全透明，盖住了也等于没盖。</summary>
    public static byte GetLayeredAlpha(IntPtr hwnd)
    {
        try
        {
            if (GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var flags)
                && (flags & WindowInterop.LWA_ALPHA) != 0)
                return alpha;
        }
        catch { }
        return 255;
    }

    /// <summary>
    /// 独占全屏/演示模式是否在跑。只认 <c>SHQueryUserNotificationState</c> 的 3|4
    /// （RUNNING_D3D_FULL_SCREEN 与 PRESENTATION_MODE）——<b>不含 BUSY</b>，与 DeskBox 同口径：
    /// BUSY 只说明"有个应用宣布很忙"，独占/演示才是"桌面真的看不见了"。
    /// </summary>
    public static bool IsExclusiveFullscreenOrPresentationActive()
    {
        try
        {
            return SHQueryUserNotificationState(out var state) == 0 && state is 3 or 4;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>窗口可见边界：DWM 扩展边界优先（不带隐形边框），失败/退化回退 GetWindowRect。</summary>
    public static bool TryGetWindowBounds(IntPtr hwnd, out WindowInterop.RECT rect)
    {
        try
        {
            if (DwmGetWindowAttributeRect(hwnd, WindowInterop.DWMWA_EXTENDED_FRAME_BOUNDS, out rect,
                    Marshal.SizeOf<WindowInterop.RECT>()) == 0
                && rect.Right > rect.Left && rect.Bottom > rect.Top)
                return true;
        }
        catch { }
        return WindowInterop.GetWindowRect(hwnd, out rect);
    }

    /// <summary>把一块屏的几何打包成 Core 的 <see cref="MonitorGeometry"/>（key＝hMonitor.ToInt64()）。</summary>
    public static bool TryGetMonitorGeometry(IntPtr hMonitor, out MonitorGeometry geometry)
    {
        var info = new WindowInterop.MONITORINFO { CbSize = Marshal.SizeOf<WindowInterop.MONITORINFO>() };
        if (hMonitor != IntPtr.Zero && WindowInterop.GetMonitorInfoW(hMonitor, ref info))
        {
            geometry = new MonitorGeometry(
                hMonitor.ToInt64(),
                info.RcMonitor.Left, info.RcMonitor.Top, info.RcMonitor.Right, info.RcMonitor.Bottom,
                info.RcWork.Left, info.RcWork.Top, info.RcWork.Right, info.RcWork.Bottom);
            return true;
        }
        geometry = new MonitorGeometry(0, 0, 0, 0, 0, 0, 0, 0, 0);
        return false;
    }
}
