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

    /// <summary>鼠标穿透（须与 LAYERED 同设）与分层窗口位。贴图窗"让它不挡鼠标"用这一对。</summary>
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_LAYERED = 0x00080000;

    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_BOTTOM = new(1);
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    /// <summary>设置窗口所有者（DeskBox 用它把组件挂到桌面图标层）。</summary>
    public const int GWLP_HWNDPARENT = -8;

    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
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

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    public static extern bool GetMonitorInfoExW(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    /// <summary>取窗口属主的进程 id（返回值为线程 id）。用于"按标题找窗口"时确认它真是我们的窗口。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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

    /// <summary>
    /// 贴图窗的鼠标穿透：加上 <c>WS_EX_TRANSPARENT</c> 后鼠标消息会越过这扇窗落到下面。
    /// <para>
    /// Win32 要求它与 <c>WS_EX_LAYERED</c> 同设才生效，所以第一次开启时一并加上 LAYERED；
    /// <b>关闭时只摘 TRANSPARENT、保留 LAYERED</b>——来回改 LAYERED 会让 DWM 重建呈现路径，
    /// 是"贴图一关穿透就变黑框"那类现象的来源。
    /// </para>
    /// </summary>
    /// <returns>改完并**读回确认**过才返回 true。扩展样式被系统拒绝时不能假装成功：
    /// 用户下一步就是"点这张贴图怎么没反应"，那时"改了没改成"只有窗口自己知道。</returns>
    public static bool SetClickThrough(Microsoft.UI.Xaml.Window window, bool on)
    {
        var hwnd = GetHwnd(window);
        var current = GetWindowLong(hwnd, GWL_EXSTYLE).ToInt64();
        var wanted = on ? current | WS_EX_TRANSPARENT | WS_EX_LAYERED : current & ~((long)WS_EX_TRANSPARENT);
        SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(wanted));
        // 改扩展样式后要一次带 SWP_FRAMECHANGED 的位置调用才会被重算
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        var nowTransparent = (GetWindowLong(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TRANSPARENT) != 0;
        return nowTransparent == on;
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
    /// 把「窗口本身」裁成圆角矩形（<see cref="SetWindowRgn"/>，真正物理裁切，无黑角）。
    /// <para>
    /// 圆角半径用<b>逻辑像素</b>传入，内部按 DPI 换算成物理像素的椭圆直径；尺寸取自
    /// <see cref="GetWindowRect"/>（物理像素），而非 <c>AppWindow.Size</c>（逻辑像素）——
    /// 在缩放屏上后者会让区域与窗口实际尺寸不符，表现为「修改前/后两种圆角叠加」或裁切错位。
    /// </para>
    /// <para>
    /// 半径 ≤ 0 时移除区域并恢复 DWM 自带圆角（直角 / 系统圆角）。
    /// <see cref="SetWindowRgn"/> 会接管传入的新区域、并在下次替换时<b>自动释放</b>旧区域，
    /// 故<b>成功</b>时调用方无需手动 <c>DeleteObject</c>；但<b>失败</b>（返回 0）时窗口并未接管，
    /// 必须由调用方 <c>DeleteObject</c> 释放，否则每套用一次外观就泄漏一个 GDI 区域句柄。
    /// </para>
    /// </summary>
    /// <param name="window">目标窗口。</param>
    /// <param name="cornerRadiusLogical">圆角半径（逻辑像素，即与 XAML CornerRadius 同单位）。</param>
    public static void SetRoundedWindowRegion(Microsoft.UI.Xaml.Window window, double cornerRadiusLogical)
    {
        try
        {
            var hwnd = GetHwnd(window);
            var rect = GetWindowRect(window);   // 物理像素：始终是当前窗口的真实尺寸
            var scale = GetScale(window);
            var w = rect.Width;
            var h = rect.Height;
            if (w <= 0 || h <= 0) return;
            if (cornerRadiusLogical <= 0)
            {
                // 直角 / 恢复 DWM 自带圆角：移除自定义区域
                SetWindowRgn(hwnd, IntPtr.Zero, true);
                SetDwmCornerPreference(window, DWMWCP_ROUND);
                return;
            }
            // 关掉 DWM 自带圆角，改用 SetWindowRgn 自定义半径，避免双重圆角
            SetDwmCornerPreference(window, DWMWCP_DONOTROUND);
            var d = (int)(cornerRadiusLogical * 2 * scale);   // 椭圆直径（物理像素）
            var hrgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, d, d);
            if (hrgn != IntPtr.Zero && SetWindowRgn(hwnd, hrgn, true) == 0)
                DeleteObject(hrgn);   // 失败时窗口未接管，须自行释放，避免 GDI 区域句柄泄漏
        }
        catch { /* 取不到窗口句柄/尺寸时跳过，下次套用外观或尺寸变化会再算 */ }
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

    // ───────────────────────── XamlRoot → 发起窗口映射 ─────────────────────────
    // 弹窗（CenteredDialog）以「发起窗口所在显示器」为基准居中，需要把内容里的 XamlRoot
    // 反查回它的宿主 Window——组件与主窗口是各自独立的顶层窗口，若恒按主窗口居中，
    // 从桌面组件弹出的编辑框就会跑到主窗口那块屏幕上。弱引用登记，窗口关闭即摘除。

    private static readonly List<WeakReference<Microsoft.UI.Xaml.Window>> _liveWindows = new();

    /// <summary>登记一个存活的顶层窗口（在其宿主 XamlRoot 就绪前调用也安全，解析时才惰性比对）。
    /// 会在窗口 Closed 时自动摘除，重复登记同一窗口会先清旧项。</summary>
    public static void TrackWindow(Microsoft.UI.Xaml.Window window)
    {
        void Prune()
        {
            lock (_liveWindows)
                _liveWindows.RemoveAll(wr => !wr.TryGetTarget(out var t) || ReferenceEquals(t, window));
        }
        Prune();
        lock (_liveWindows) _liveWindows.Add(new WeakReference<Microsoft.UI.Xaml.Window>(window));
        window.Closed += (_, _) => Prune();
    }

    /// <summary>把 <paramref name="xamlRoot"/> 解析回其宿主 Window；未命中时回退 <paramref name="fallback"/>。</summary>
    public static Microsoft.UI.Xaml.Window? ResolveWindow(Microsoft.UI.Xaml.XamlRoot? xamlRoot,
        Microsoft.UI.Xaml.Window? fallback)
    {
        if (xamlRoot is null) return fallback;
        lock (_liveWindows)
        {
            foreach (var wr in _liveWindows)
            {
                if (wr.TryGetTarget(out var w)
                    && ReferenceEquals((w.Content as Microsoft.UI.Xaml.FrameworkElement)?.XamlRoot, xamlRoot))
                    return w;
            }
        }
        return fallback;
    }

    /// <summary>
    /// 去掉窗口默认标题栏/边框。
    /// <para>
    /// <b>为什么整段走 Win32、不调 <c>OverlappedPresenter.SetBorderAndTitleBar</c></b>：真机分段计量到
    /// 那一句框架调用单次就要 <b>≈68 ms</b>（同一函数里其余五步合计 &lt;3 ms），而无边框窗不止组件——
    /// 截图遮罩、贴图、各类弹窗每次都要过一遍；22 颗组件在启动那一段里就因此吃掉 ≈1.5 s。
    /// 下面第 2 步清的是同一批样式位（WS_CAPTION / WS_BORDER / WS_DLGFRAME / WS_THICKFRAME），
    /// 第 3 步一次 <c>SWP_FRAMECHANGED</c> 让非客户区重算，观感与那句框架调用一致。
    /// </para>
    /// </summary>
    public static void RemoveDefaultWindowFrame(Microsoft.UI.Xaml.Window window)
    {
        var hwnd = GetHwnd(window);

        // 1) WinAppSDK 层：只留"不进任务栏 / Alt+Tab"这件只有框架能替我们说的事。
        //    （早先这里还设过 IsResizable / IsMaximizable / IsMinimizable 三个 bool——第 2 步清的
        //     WS_THICKFRAME / WS_MAXIMIZEBOX / WS_MINIMIZEBOX 正是同一件事，属于对着系统白说一遍。）
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

    /// <summary>
    /// 所有显示器的<b>整屏</b>矩形（物理像素，含任务栏——截图遮罩必须盖住整块屏，
    /// 用工作区会留下一条任务栏没被压暗，用户会以为遮罩没铺满）+ 各自的有效缩放。
    /// 截图/贴图按"每屏一个窗口"铺，是因为每块的 DPI 可能不同：单窗跨屏时
    /// 只有一屏的 DIP→物理换算是对的，另一屏上的选区会整体偏移。
    /// </summary>
    public static IReadOnlyList<(string Device, RectInt32 Bounds, double Scale)> ListMonitors()
    {
        var list = new List<(string, RectInt32, double)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hmon, _, _, _) =>
        {
            var mi = new MONITORINFOEX { CbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfoExW(hmon, ref mi))
                list.Add((mi.SzDevice ?? string.Empty,
                    new RectInt32(mi.RcMonitor.Left, mi.RcMonitor.Top,
                        mi.RcMonitor.Right - mi.RcMonitor.Left,
                        mi.RcMonitor.Bottom - mi.RcMonitor.Top),
                    GetMonitorScale(hmon)));
            return true;      // 继续枚举下一块
        }, IntPtr.Zero);
        return list;
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
