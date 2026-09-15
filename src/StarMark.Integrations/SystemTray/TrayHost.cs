#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StarMark.Integrations.SystemTray;

/// <summary>
/// 托盘常驻 + 全局热键宿主。
///
/// 原理：在当前（UI）线程创建一个不可见的 Win32 消息窗口（按 HWND_MESSAGE 挂到消息树上，
/// 不占任务栏、不进 Alt-Tab），由它接收 Shell_NotifyIcon 回调消息（WM_TRAY_CALLBACK）与
/// RegisterHotKey 投递的 WM_HOTKEY。不依赖 WinAppSDK 中不公开的消息挂钩 API。
///
/// ShowRequested / ExitRequested 事件均运行在创建线程（UI 线程）上。
/// </summary>
public sealed class TrayHost : IDisposable
{
    /// <summary>用户要求显示主窗口（托盘单击/双击或全局热键）。</summary>
    public event Action? ShowRequested;

    /// <summary>用户要求退出应用（托盘菜单“退出”）。</summary>
    public event Action? ExitRequested;

    private const int HotKeyId = 0x4D53; // 'MS'
    private const uint TrayId = 0x0001;

    private readonly NativeMethods.WndProcDelegate _wndProc;
    private IntPtr _hwnd;
    private IntPtr _icon;
    private GCHandle _gcHandle;
    private bool _hotKeyRegistered;
    private bool _disposed;

    /// <summary>全局热键是否注册成功。</summary>
    public bool HotKeyRegistered => _hotKeyRegistered;

    /// <summary>宿主窗口是否创建成功（托盘是否可用）。</summary>
    public bool IsAvailable => _hwnd != IntPtr.Zero;

    public TrayHost()
    {
        _wndProc = WndProc;
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            var hInstance = NativeMethods.GetModuleHandleW(null);

            var wc = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = _wndProc,
                cbWndExtra = IntPtr.Size,
                hInstance = hInstance,
                hCursor = NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_ARROW),
                lpszClassName = "StarMarkMessengerHost",
            };
            NativeMethods.RegisterClassExW(ref wc);

            _hwnd = NativeMethods.CreateWindowExW(
                0, wc.lpszClassName, wc.lpszClassName, NativeMethods.WS_POPUP,
                0, 0, 0, 0, NativeMethods.HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                Trace.WriteLine($"[TrayHost] 消息窗口创建失败, error={Marshal.GetLastWin32Error()}");
                return;
            }

            _gcHandle = GCHandle.Alloc(this);
            NativeMethods.SetWindowLongPtrW(_hwnd, NativeMethods.GWL_USERDATA, GCHandle.ToIntPtr(_gcHandle));

            _icon = CreateStarIcon();
            AddTrayIcon();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[TrayHost] 初始化失败: {ex.Message}");
        }
    }

    /// <summary>注册全局热键（默认 Ctrl+Alt+Space）。</summary>
    public bool TryRegisterHotKey(uint modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
                                   uint vk = NativeMethods.VK_SPACE)
    {
        if (_hwnd == IntPtr.Zero) return false;
        if (_hotKeyRegistered) return true;
        _hotKeyRegistered = NativeMethods.RegisterHotKey(_hwnd, HotKeyId, modifiers, vk);
        if (!_hotKeyRegistered)
            Trace.WriteLine("[TrayHost] RegisterHotKey 失败（可能被其他程序占用）");
        return _hotKeyRegistered;
    }

    private void AddTrayIcon()
    {
        if (_hwnd == IntPtr.Zero) return;

        var nid = NewNid();
        nid.hWnd = _hwnd;
        nid.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
        nid.uCallbackMessage = NativeMethods.TRAY_CALLBACK;
        nid.hIcon = _icon;
        nid.szTip = "StarMark — Ctrl+Alt+Space 呼出";
        if (NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, ref nid))
        {
            // 启用新版托盘行为（区分单击/双击/右键）
            nid.uFlags = NativeMethods.NIF_MESSAGE;
            nid.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
            NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_SETVERSION, ref nid);
        }
        else
        {
            Trace.WriteLine("[TrayHost] NIM_ADD 失败");
        }
    }

    /// <summary>在系统托盘弹出提示气泡。</summary>
    public void ShowBalloon(string title, string message)
    {
        if (_hwnd == IntPtr.Zero) return;
        var nid = NewNid();
        nid.hWnd = _hwnd;
        nid.uID = TrayId;
        nid.uFlags = NativeMethods.NIF_INFO | NativeMethods.NIF_MESSAGE;
        nid.uCallbackMessage = NativeMethods.TRAY_CALLBACK;
        nid.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
        nid.hIcon = _icon;
        nid.szInfo = message;
        nid.szInfoTitle = title;
        nid.dwInfoFlags = NativeMethods.NIIF_INFO;
        NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_MODIFY, ref nid);
    }

    private static NativeMethods.NOTIFYICONDATA NewNid()
    {
        return new NativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = IntPtr.Zero,
            uID = TrayId,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
            guidItem = Guid.Empty,
        };
    }

    // ===== WndProc =====

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_HOTKEY:
                if (wParam == HotKeyId)
                    ShowRequested?.Invoke();
                return IntPtr.Zero;

            case NativeMethods.TRAY_CALLBACK:
                HandleTrayMessage(lParam);
                return IntPtr.Zero;
        }
        return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void HandleTrayMessage(IntPtr lParam)
    {
        var msg = unchecked((uint)lParam.ToInt64());
        switch (msg)
        {
            case NativeMethods.WM_LBUTTONUP:
            case NativeMethods.WM_LBUTTONDBLCLK:
                ShowRequested?.Invoke();
                break;

            case NativeMethods.WM_RBUTTONUP:
                ShowContextMenu();
                break;
        }
    }

    private void ShowContextMenu()
    {
        if (_hwnd == IntPtr.Zero) return;

        var (x, y) = GetCursorPos();
        var menu = NativeMethods.CreatePopupMenu();
        NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING, new IntPtr(NativeMethods.IDM_SHOW), "显示 StarMark");
        NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING, new IntPtr(NativeMethods.IDM_EXIT), "退出");

        NativeMethods.SetForegroundWindow(_hwnd);
        var cmd = (int)NativeMethods.TrackPopupMenuEx(
            menu,
            NativeMethods.TPM_LEFTALIGN | NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD,
            x, y, _hwnd, IntPtr.Zero);
        NativeMethods.DestroyMenu(menu);

        switch (cmd)
        {
            case NativeMethods.IDM_SHOW:
                ShowRequested?.Invoke();
                break;
            case NativeMethods.IDM_EXIT:
                ExitRequested?.Invoke();
                break;
        }
    }

    private static (int X, int Y) GetCursorPos()
    {
        var pt = new NativeMethods.POINT();
        NativeMethods.GetCursorPos(ref pt);
        return (pt.X, pt.Y);
    }

    // ===== 图标 =====

    /// <summary>生成一个 32x32 的五角星 HICON（Windows 提示黄）。失败返回 Zero。</summary>
    private IntPtr CreateStarIcon()
    {
        const int size = 32;
        try
        {
            var hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero) return IntPtr.Zero;

            var bi = new NativeMethods.BITMAPINFO
            {
                biSize = Marshal.SizeOf<NativeMethods.BITMAPINFO>(),
                biWidth = size,
                biHeight = -size, // 自上而下
                biPlanes = 1,
                biBitCount = 32,
            };

            IntPtr bits;
            var hBmpColor = NativeMethods.CreateDIBSection(hdcScreen, ref bi, 0, out bits, IntPtr.Zero, 0);
            NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
            if (hBmpColor == IntPtr.Zero) return IntPtr.Zero;

            unsafe
            {
                var p = (uint*)bits;
                var star = BuildStarPolygon(size);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        // 0xAABBGGRR：五角星内为不透明亮黄，其余完全透明
                        p[y * size + x] = PointInPolygon(x, y, star)
                            ? 0xFFC8BE00u
                            : 0x00000000u;
                    }
                }
                // Alpha 通道哨兵：置为与背景色不同的值，Shell 按像素 alpha 混合
                p[0] = 0x000000FFu;
            }

            byte maskPixel = 0xFF;
            var hBmpMask = NativeMethods.CreateBitmap(1, 1, 1, 1, ref maskPixel);

            var ii = new NativeMethods.ICONINFO { fIcon = true, hbmMask = hBmpMask, hbmColor = hBmpColor };
            var hIcon = NativeMethods.CreateIconIndirect(ref ii);

            NativeMethods.DeleteObject(hBmpColor);
            NativeMethods.DeleteObject(hBmpMask);
            return hIcon;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>构建五角星顶点（10 个顶点）。</summary>
    private static (float X, float Y)[] BuildStarPolygon(int size)
    {
        const int points = 10;
        double cx = size / 2.0, cy = size / 2.0;
        double outer = size * 0.42, inner = outer * 0.42;
        var pts = new (float, float)[points];
        for (int i = 0; i < points; i++)
        {
            double angle = -Math.PI / 2 + i * Math.PI / points;
            double r = i % 2 == 0 ? outer : inner;
            pts[i] = ((float)(cx + r * Math.Cos(angle)), (float)(cy + r * Math.Sin(angle)));
        }
        return pts;
    }

    /// <summary>射线法（even-odd）判断点是否在多边形内。</summary>
    private static bool PointInPolygon(int x, int y, (float X, float Y)[] poly)
    {
        bool inside = false;
        int n = poly.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if ((a.Y > y) != (b.Y > y) &&
                x < (b.X - a.X) * (y - a.Y) / (double)(b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    // ===== 主窗口显示/隐藏辅助 =====

    public static void ShowAndFocus(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
        NativeMethods.SetForegroundWindow(hwnd);
    }

    public static void HideToTray(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            if (_hotKeyRegistered)
            {
                NativeMethods.UnregisterHotKey(_hwnd, HotKeyId);
                _hotKeyRegistered = false;
            }

            var nid = NewNid();
            nid.hWnd = _hwnd;
            nid.uID = TrayId;
            NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_DELETE, ref nid);

            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_gcHandle.IsAllocated)
            _gcHandle.Free();

        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }
}