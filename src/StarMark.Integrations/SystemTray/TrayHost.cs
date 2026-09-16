#nullable enable
using System.Runtime.InteropServices;
using StarMark.Abstractions;
using static StarMark.Integrations.SystemTray.NativeMethods;

namespace StarMark.Integrations.SystemTray;

/// <summary>
/// 系统托盘宿主（对标 DeskBox 的 TrayIconService）：
/// 运行时创建一个仅消息窗口承载 NotifyIcon，无需 XAML 窗口或 ApplicationIcon 资源。
/// 左键 / 双击 → 显示主窗口；右键 → 菜单（主窗口、桌面组件增减、设置、退出）。
/// 同时承载全局热键（Ctrl+Alt+Space 唤起搜索）。
/// 必须在 UI 线程创建（消息泵分发回调）。
/// </summary>
public sealed class TrayHost : IDisposable
{
    /// <summary>组件类型显示名（索引与 StarMark.Core.Widgets.WidgetKind 枚举值一致）。</summary>
    public static readonly string[] WidgetTitles =
    {
        "★ 快捷启动", "✅ 待办", "📝 随记", "🕒 时钟", "🔍 快捷搜索",
    };

    public event Action? ShowRequested;
    public event Action? ExitRequested;
    /// <summary>托盘“显示/隐藏全部组件”总开关。</summary>
    public event Action? WidgetsToggleRequested;
    /// <summary>勾选/取消某组件（参数为 WidgetKind 枚举值 0..4）。</summary>
    public event Action<int>? WidgetToggleRequested;
    public event Action? ShowAllWidgetsRequested;
    public event Action? HideAllWidgetsRequested;
    public event Action? SettingsRequested;

    /// <summary>菜单打开时查询某组件是否已启用（勾选态）。</summary>
    public Func<int, bool>? IsWidgetEnabled { get; set; }

    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _added;
    private bool _hotkeyRegistered;
    private WndProcDelegate? _wndProc;
    private GCHandle _selfHandle;

    public bool Initialize(bool registerHotkey = true)
    {
        try
        {
            _wndProc = WndProc;
            var className = "StarMarkTrayMessageWindow";
            var hInstance = GetModuleHandleW(null);

            var wndClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _wndProc,
                hInstance = hInstance,
                lpszClassName = className,
                hCursor = LoadCursorW(IntPtr.Zero, IDC_ARROW),
            };
            RegisterClassExW(ref wndClass);

            _hwnd = CreateWindowExW(
                0, className, "StarMark Tray Host", 0,
                0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);

            _selfHandle = GCHandle.Alloc(this);
            SetWindowLongPtrW(_hwnd, GWL_USERDATA, GCHandle.ToIntPtr(_selfHandle));

            _hIcon = CreateStarIcon(16);
            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = TRAY_CALLBACK,
                hIcon = _hIcon,
                szTip = "StarMark — 本地索引",
            };
            _added = Shell_NotifyIconW(NIM_ADD, ref data);
            if (!_added)
            {
                StarLog.Error("[TrayHost] Shell_NotifyIcon NIM_ADD 失败");
                return false;
            }
            data.uVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIconW(NIM_SETVERSION, ref data);

            if (registerHotkey) RegisterGlobalHotKey();
            return true;
        }
        catch (Exception ex)
        {
            StarLog.Error("[TrayHost] 初始化失败", ex);
            return false;
        }
    }

    public void RegisterGlobalHotKey()
    {
        if (_hotkeyRegistered || _hwnd == IntPtr.Zero) return;
        const uint modifiers = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;
        if (NativeMethods.RegisterHotKey(_hwnd, 1, modifiers, VK_SPACE))
        {
            _hotkeyRegistered = true;
            StarLog.Info("[TrayHost] 全局热键已注册 Ctrl+Alt+Space");
        }
        else
        {
            StarLog.Warn("[TrayHost] RegisterHotKey 失败（可能被其他程序占用）");
        }
    }

    public void UnregisterGlobalHotKey()
    {
        if (!_hotkeyRegistered || _hwnd == IntPtr.Zero) return;
        NativeMethods.UnregisterHotKey(_hwnd, 1);
        _hotkeyRegistered = false;
    }

    public void ShowNotification(string title, string message)
    {
        if (!_added) return;
        try
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_INFO,
                szInfoTitle = title.Length > 63 ? title[..63] : title,
                szInfo = message.Length > 255 ? message[..255] : message,
                dwInfoFlags = NIIF_INFO,
            };
            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
        catch (Exception ex) { StarLog.Warn($"[TrayHost] 通知失败: {ex.Message}"); }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_DESTROY)
        {
            if (_selfHandle.IsAllocated) _selfHandle.Free();
            return IntPtr.Zero;
        }

        if (msg == WM_HOTKEY)
        {
            ShowRequested?.Invoke();
            return IntPtr.Zero;
        }

        if (msg == TRAY_CALLBACK)
        {
            var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            switch (mouseMsg)
            {
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                    ShowRequested?.Invoke();
                    break;
                case WM_RBUTTONUP:
                    ShowContextMenu();
                    break;
            }
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();

        AppendMenuW(menu, MF_STRING, (IntPtr)IDM_SHOW, "显示 StarMark");
        AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null!);
        AppendMenuW(menu, MF_STRING, (IntPtr)IDM_WIDGETS, "显示/隐藏全部组件");

        // 桌面组件子菜单：逐项勾选 + 全部显示/隐藏
        var sub = CreatePopupMenu();
        for (var i = 0; i < WidgetTitles.Length; i++)
        {
            var enabled = IsWidgetEnabled?.Invoke(i) ?? false;
            var flags = MF_STRING | (enabled ? MF_CHECKED : 0);
            AppendMenuW(sub, (uint)flags, (IntPtr)(IDM_WIDGET_BASE + i), WidgetTitles[i]);
        }
        AppendMenuW(sub, MF_SEPARATOR, IntPtr.Zero, null!);
        AppendMenuW(sub, MF_STRING, (IntPtr)IDM_WIDGET_SHOWALL, "全部显示");
        AppendMenuW(sub, MF_STRING, (IntPtr)IDM_WIDGET_HIDEALL, "全部隐藏");
        AppendMenuW(menu, MF_POPUP, sub, "桌面组件");

        AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null!);
        AppendMenuW(menu, MF_STRING, (IntPtr)IDM_SETTINGS, "设置");
        AppendMenuW(menu, MF_STRING, (IntPtr)IDM_EXIT, "退出");

        GetCursorPos(out var pt);

        // 必须前台窗口一次，否则部分系统上菜单点击后不消失
        SetForegroundWindow(_hwnd);
        var cmd = TrackPopupMenuEx(
            menu,
            TPM_LEFTALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD,
            pt.X, pt.Y, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);

        if (cmd == 0) return;
        switch (cmd)
        {
            case IDM_SHOW: ShowRequested?.Invoke(); break;
            case IDM_EXIT: ExitRequested?.Invoke(); break;
            case IDM_WIDGETS: WidgetsToggleRequested?.Invoke(); break;
            case IDM_WIDGET_SHOWALL: ShowAllWidgetsRequested?.Invoke(); break;
            case IDM_WIDGET_HIDEALL: HideAllWidgetsRequested?.Invoke(); break;
            case IDM_SETTINGS: SettingsRequested?.Invoke(); break;
            default:
                if (cmd >= IDM_WIDGET_BASE && cmd < IDM_WIDGET_BASE + WidgetTitles.Length)
                    WidgetToggleRequested?.Invoke(cmd - IDM_WIDGET_BASE);
                break;
        }
    }

    public void Dispose()
    {
        try
        {
            UnregisterGlobalHotKey();
            if (_added)
            {
                var data = new NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = 1,
                };
                Shell_NotifyIconW(NIM_DELETE, ref data);
                _added = false;
            }
            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
        catch (Exception ex) { StarLog.Warn($"[TrayHost] Dispose 异常: {ex.Message}"); }
    }

    /// <summary>
    /// 运行时绘制一颗黄色五角星托盘图标（不依赖 ApplicationIcon/嵌入资源）。
    /// </summary>
    private static IntPtr CreateStarIcon(int size)
    {
        var hdc = GetDC(IntPtr.Zero);
        var bmi = new BITMAPINFO
        {
            biSize = Marshal.SizeOf<BITMAPINFO>(),
            biWidth = size,
            biHeight = -size, // 自上而下
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
        };
        var bits = IntPtr.Zero;
        var hBitmap = CreateDIBSection(hdc, ref bmi, 0, out bits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, hdc);

        var cx = (size - 1) / 2.0;
        var cy = (size - 1) / 2.0;
        var outer = size * 0.46;
        var inner = outer * 0.42;
        var raw = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var angle = Math.Atan2(dy, dx) + Math.PI / 2;
                var seg = (angle + Math.PI * 2) % (Math.PI / 5);
                var rr = outer - (outer - inner) * Math.Abs(seg - Math.PI / 10) / (Math.PI / 10);
                var idx = (y * size + x) * 4;
                if (dist <= rr)
                {
                    raw[idx + 0] = 0x30; // B
                    raw[idx + 1] = 0xB6; // G
                    raw[idx + 2] = 0xFC; // R
                    raw[idx + 3] = 0xFF; // A
                }
            }
        }
        if (bits != IntPtr.Zero) Marshal.Copy(raw, 0, bits, raw.Length);

        // 单色 mask（全 0：全部不透明）
        var mask = CreateBitmap(size, size, 1, 1, new byte[(size * size + 7) / 8]);
        var iconInfo = new ICONINFO
        {
            fIcon = true,
            xHotspot = size / 2,
            yHotspot = size / 2,
            hbmMask = mask,
            hbmColor = hBitmap,
        };
        var hIcon = CreateIconIndirect(ref iconInfo);
        DeleteObject(mask);
        DeleteObject(hBitmap);
        return hIcon;
    }
}