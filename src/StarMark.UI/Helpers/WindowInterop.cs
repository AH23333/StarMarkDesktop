#nullable enable
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace StarMark.UI.Helpers;

/// <summary>
/// 桌面组件/无边框窗口所需的 Win32 互操作（对标 DeskBox Win32Helper 的常用子集）。
/// 仅 x64 Windows。所有坐标均为物理像素。
/// </summary>
internal static class WindowInterop
{
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public const uint WS_BORDER = 0x00800000;
    public const uint WS_DLGFRAME = 0x00400000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_MAXIMIZEBOX = 0x00010000;
    public const uint WS_SYSMENU = 0x00080000;

    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_TOPMOST = 0x00000008;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    public const int SW_SHOW = 5;
    public const int SW_RESTORE = 9;

    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;

    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int CbSize;
        public RECT RcMonitor;
        public RECT RcWork;
        public uint DwFlags;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static IntPtr GetWindowLong(IntPtr hWnd, int index) => GetWindowLongPtr64(hWnd, index);

    public static IntPtr SetWindowLong(IntPtr hWnd, int index, IntPtr value) => SetWindowLongPtr64(hWnd, index, value);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowW(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    public static IntPtr GetHwnd(Microsoft.UI.Xaml.Window window) => WindowNative.GetWindowHandle(window);

    public static RectInt32 GetWindowRect(Microsoft.UI.Xaml.Window window)
        => GetWindowRect(GetHwnd(window), out var r)
            ? new RectInt32(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)
            : new RectInt32();

    /// <summary>设置/取消窗口最顶层（DeskBox WidgetLayerService.SetTopmost 同款调用）。</summary>
    public static void SetTopmost(Microsoft.UI.Xaml.Window window, bool topmost)
    {
        SetWindowPos(
            GetHwnd(window),
            topmost ? HWND_TOPMOST : HWND_NOTOPMOST,
            0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Win11 圆角（无边框窗口默认是方角，需要显式设置）。</summary>
    public static void ApplyRoundedCorners(Microsoft.UI.Xaml.Window window)
    {
        try
        {
            var preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(GetHwnd(window), DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch { /* 旧系统无此属性，忽略 */ }
    }

    /// <summary>取窗口当前所在显示器的工作区（物理像素，多显示器正确）。</summary>
    public static RectInt32 GetWorkArea(Microsoft.UI.Xaml.Window window)
    {
        var hwnd = GetHwnd(window);
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { CbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfoW(monitor, ref info))
            return new RectInt32(info.RcWork.Left, info.RcWork.Top,
                info.RcWork.Right - info.RcWork.Left, info.RcWork.Bottom - info.RcWork.Top);
        return new RectInt32(0, 0, 1920, 1040);
    }

    /// <summary>窗口 DPI 缩放比（96 = 100%）。</summary>
    public static double GetScale(Microsoft.UI.Xaml.Window window)
    {
        try
        {
            var dpi = GetDpiForWindow(GetHwnd(window));
            return dpi == 0 ? 1.0 : dpi / 96.0;
        }
        catch { return 1.0; }
    }

    /// <summary>去掉窗口默认标题栏/边框（DeskBox WidgetWindowBase.ConfigureWindowCore 同款序列）。</summary>
    public static void RemoveDefaultWindowFrame(Microsoft.UI.Xaml.Window window)
    {
        var hwnd = GetHwnd(window);

        // 1) WinAppSDK 层：先关边框/标题栏与最大化/最小化
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        window.AppWindow.IsShownInSwitchers = false;

        // 2) Win32 层：清样式位 + 工具窗口（不进任务栏/Alt+Tab）
        var style = (uint)GetWindowLong(hwnd, GWL_STYLE).ToInt64();
        style &= ~(WS_BORDER | WS_DLGFRAME | WS_THICKFRAME | WS_CAPTION | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        SetWindowLong(hwnd, GWL_STYLE, new IntPtr((int)style));

        var exStyle = (uint)GetWindowLong(hwnd, GWL_EXSTYLE).ToInt64();
        exStyle |= WS_EX_TOOLWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr((int)exStyle));

        // 3) 通知框架重算非客户区，否则部分系统上仍残留标题栏
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }
}
