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

    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_BOTTOM = new(1);
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    /// <summary>设置窗口所有者（DeskBox 用它把组件挂到桌面图标层）。</summary>
    public const int GWLP_HWNDPARENT = -8;

    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public const int SW_SHOW = 5;
    public const int SW_RESTORE = 9;

    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_DONOTROUND = 1;   // 关掉 DWM 自带圆角：改用 SetWindowRgn 自定义半径时避免双重圆角

    /// <summary>DWM 系统背景类型（DeskBox 用它在自管控制器生效时关掉 DWM 自带的背景）。</summary>
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMSBT_NONE = 1;   // 不用 DWM 自带背景（由 DesktopAcrylicController/MicaController 接管）

    /// <summary>沉浸式深色模式（DeskBox 的 Win32Helper.SetWindowTheme 用同一个属性）。</summary>
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

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

    /// <summary>DeskBox 的 <c>MARGINS</c>（DwmExtendFrameIntoClientArea 用，全 -1 = 整窗玻璃）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int wEllipse, int hEllipse);

    [DllImport("user32.dll")]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    public static extern bool GetMonitorInfoExW(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowExW(
        IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    /// <summary>窗口是否处于 topmost 层（用于决定回落时插入到哪一层）。</summary>
    public static bool IsWindowTopMost(IntPtr hWnd) =>
        ((uint)GetWindowLong(hWnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) != 0;

    /// <summary>取窗口类名，用于查找 SHELLDLL_DefView / WorkerW。</summary>
    public static string GetClassName(IntPtr hWnd)
    {
        var sb = new System.Text.StringBuilder(256);
        return GetClassNameW(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

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

    /// <summary>设置 DWM 窗口圆角偏好（用 SetWindowRgn 自定义半径时关掉 DWM 自带圆角，避免双重圆角）。</summary>
    public static void SetDwmCornerPreference(Microsoft.UI.Xaml.Window window, int preference)
    {
        try { DwmSetWindowAttribute(GetHwnd(window), DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int)); }
        catch { /* 旧系统忽略 */ }
    }

    /// <summary>
    /// 关掉 DWM 自带的系统背景（DWMSBT_NONE）。
    /// 当用 <see cref="Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController"/> /
    /// <see cref="Microsoft.UI.Composition.SystemBackdrops.MicaController"/> 直接接管背景时，
    /// 必须这么做，否则 DWM 会在我们的控制器之上再叠一层默认亚克力/云母（DeskBox 同款调用）。
    /// </summary>
    public static void SetDwmSystemBackdropNone(Microsoft.UI.Xaml.Window window)
    {
        try
        {
            var type = DWMSBT_NONE;
            DwmSetWindowAttribute(GetHwnd(window), DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
        }
        catch { /* 旧系统忽略 */ }
    }

    /// <summary>
    /// 整窗玻璃化（DeskBox 的 <c>Win32Helper.ApplyFullWindowFrame</c>：四个边距全 -1）。
    /// <para>
    /// 这是原生材质能<b>透出来</b>的前提：不扩展边框时，窗口客户区由系统按不透明底色绘制，
    /// 内容背景设为透明也只是透出那层底色 —— 于是 <c>TintOpacity</c> / <c>LuminosityOpacity</c>
    /// 怎么调都看不出变化（「两个滑块对组件失效」的真因）。
    /// </para>
    /// </summary>
    public static void ApplyFullWindowFrame(Microsoft.UI.Xaml.Window window)
    {
        try
        {
            var margins = new MARGINS
            {
                cxLeftWidth = -1,
                cxRightWidth = -1,
                cyTopHeight = -1,
                cyBottomHeight = -1,
            };
            DwmExtendFrameIntoClientArea(GetHwnd(window), ref margins);
        }
        catch { /* 旧系统忽略 */ }
    }

    /// <summary>收回玻璃化（实色 / 纯色材质不需要，避免 DWM 无谓参与合成）。</summary>
    public static void ClearFullWindowFrame(Microsoft.UI.Xaml.Window window)
    {
        try
        {
            var margins = new MARGINS();
            DwmExtendFrameIntoClientArea(GetHwnd(window), ref margins);
        }
        catch { /* 旧系统忽略 */ }
    }

    /// <summary>让无边框窗口的非客户区（玻璃边缘）跟随深色模式，对齐 DeskBox 的 SetWindowTheme。</summary>
    public static void SetImmersiveDarkMode(Microsoft.UI.Xaml.Window window, bool dark)
    {
        try
        {
            var value = dark ? 1 : 0;
            DwmSetWindowAttribute(GetHwnd(window), DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch { /* 旧系统忽略 */ }
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

    // ── 每显示器拓扑布局（Phase B）：用 Win32 枚举显示器，按稳定设备名（szDevice）記忆，
    //    位置以 DIP 存为「所在显示器工作区左上角」的偏移，恢复时按该显示器当前 DPI 重新换算物理像素。
    //    （本机 WinAppSDK 2.3.6 的 DisplayArea 无 GetFromHwnd/GetFromId，故走 Win32，更贴近 DeskBox 做法。） ──

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int CbSize;
        public RECT RcMonitor;
        public RECT RcWork;
        public uint DwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string SzDevice;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsProc lpfnEnum, IntPtr dwData);

    public delegate bool EnumMonitorsProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private const int MONITORINFOF_PRIMARY = 0x1;
    private const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>窗口所在显示器的稳定设备名（如 \\.\DISPLAY1）、工作区（物理像素）与 DPI 缩放比。</summary>
    public static (string Device, RectInt32 WorkArea, double Scale) GetMonitorForWindow(IntPtr hwnd)
    {
        var hmonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFOEX { CbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (GetMonitorInfoExW(hmonitor, ref mi))
        {
            var work = new RectInt32(mi.RcWork.Left, mi.RcWork.Top,
                mi.RcWork.Right - mi.RcWork.Left, mi.RcWork.Bottom - mi.RcWork.Top);
            return (mi.SzDevice ?? string.Empty, work, GetMonitorScale(hmonitor));
        }
        return (string.Empty, new RectInt32(0, 0, 1920, 1040), 1.0);
    }

    private static double GetMonitorScale(IntPtr hmonitor)
    {
        try
        {
            if (GetDpiForMonitor(hmonitor, MDT_EFFECTIVE_DPI, out var x, out var y) == 0)
                return (x == 0 ? 96 : x) / 96.0;
        }
        catch { /* 拿不到则按 100% */ }
        return 1.0;
    }

    /// <summary>按设备名找显示器工作区；找不到（已断开）返回 null。</summary>
    public static RectInt32? FindMonitorWorkAreaByDevice(string device)
    {
        if (string.IsNullOrEmpty(device)) return null;
        RectInt32? found = null;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hmon, _, _, _) =>
        {
            var mi = new MONITORINFOEX { CbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfoExW(hmon, ref mi) && mi.SzDevice == device)
            {
                found = new RectInt32(mi.RcWork.Left, mi.RcWork.Top,
                    mi.RcWork.Right - mi.RcWork.Left, mi.RcWork.Bottom - mi.RcWork.Top);
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>按设备名找显示器 DPI 缩放比；找不到返回 1.0。</summary>
    public static double GetMonitorScaleByDevice(string device)
    {
        if (string.IsNullOrEmpty(device)) return 1.0;
        var scale = 1.0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hmon, _, _, _) =>
        {
            var mi = new MONITORINFOEX { CbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfoExW(hmon, ref mi) && mi.SzDevice == device)
            {
                scale = GetMonitorScale(hmon);
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return scale;
    }

    /// <summary>主显示器工作区（物理像素）。</summary>
    public static RectInt32 PrimaryWorkArea()
    {
        var result = new RectInt32(0, 0, 1920, 1040);
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hmon, _, _, _) =>
        {
            var mi = new MONITORINFOEX { CbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfoExW(hmon, ref mi) && (mi.DwFlags & MONITORINFOF_PRIMARY) != 0)
            {
                result = new RectInt32(mi.RcWork.Left, mi.RcWork.Top,
                    mi.RcWork.Right - mi.RcWork.Left, mi.RcWork.Bottom - mi.RcWork.Top);
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>矩形中心是否不在任何显示器工作区内（越界 / 显示器已断开）。</summary>
    public static bool IsOffScreen(RectInt32 r)
    {
        var cx = r.X + r.Width / 2;
        var cy = r.Y + r.Height / 2;
        var onAny = false;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hmon, _, _, _) =>
        {
            var mi = new MONITORINFOEX { CbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfoExW(hmon, ref mi))
            {
                var wa = mi.RcWork;
                if (cx >= wa.Left && cx <= wa.Right && cy >= wa.Top && cy <= wa.Bottom)
                    onAny = true;
            }
            return true;
        }, IntPtr.Zero);
        return !onAny;
    }

    /// <summary>返回包含矩形中心的显示器工作区；没有则 null。</summary>
    public static RectInt32? MonitorWorkAreaContaining(int x, int y, int w, int h)
    {
        var cx = x + w / 2;
        var cy = y + h / 2;
        RectInt32? found = null;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hmon, _, _, _) =>
        {
            var mi = new MONITORINFOEX { CbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfoExW(hmon, ref mi))
            {
                var wa = mi.RcWork;
                if (cx >= wa.Left && cx <= wa.Right && cy >= wa.Top && cy <= wa.Bottom)
                {
                    found = new RectInt32(wa.Left, wa.Top, wa.Right - wa.Left, wa.Bottom - wa.Top);
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
