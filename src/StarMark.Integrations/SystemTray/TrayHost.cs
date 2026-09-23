#nullable enable
using System.Runtime.InteropServices;
using StarMark.Abstractions;
using static StarMark.Integrations.SystemTray.NativeMethods;

namespace StarMark.Integrations.SystemTray;

/// <summary>
/// 系统托盘宿主（对标 DeskBox 的 TrayIconService）：
/// 运行时创建一个仅消息窗口承载 NotifyIcon，无需 XAML 窗口或 ApplicationIcon 资源。
/// 左键 / 双击 → 显示主窗口；右键 → 菜单（主窗口、桌面组件增减、设置、退出）。
/// 全局快捷键已迁移到 HotkeyService（注册在 MainWindow 句柄上，支持多动作与冲突允许），此处不再注册。
/// 必须在 UI 线程创建（消息泵分发回调）。
/// </summary>
public sealed class TrayHost : IDisposable
{
    /// <summary>
    /// 托盘「桌面组件」子菜单要列出的行，<b>由宿主从组件注册表注入</b>。
    /// <para>
    /// 这里原先是一份手写的 5 条标题数组，而注册表已长出到 12 种组件 ⇒ 7 种在托盘里既看不到也关不掉
    /// （P-62b）。第二份清单必然漂移，所以直接删掉它、只接受注入。没注入时子菜单只剩"全部显示/隐藏"。
    /// </para>
    /// </summary>
    public IReadOnlyList<TrayWidgetItem> WidgetMenuItems { get; set; } = Array.Empty<TrayWidgetItem>();

    public event Action? ShowRequested;
    public event Action? ExitRequested;
    /// <summary>托盘“显示/隐藏全部组件”总开关。</summary>
    public event Action? WidgetsToggleRequested;
    /// <summary>勾选/取消某组件（参数为注入行携带的 WidgetKind 整数值，<b>不是</b>菜单序号）。</summary>
    public event Action<int>? WidgetToggleRequested;
    public event Action? ShowAllWidgetsRequested;
    public event Action? HideAllWidgetsRequested;
    public event Action? SettingsRequested;

    /// <summary>
    /// 宿主在右键那一刻提供的附加命令（渲染在「桌面组件」之后、设置之前）。
    /// <b>用回调而不是属性快照</b>：勾选态（主题、性能模式、开机自启、热键开关）随时在变，
    /// 存成属性就等于"托盘显示的是上一次右键时的状态"——点错了还看不出为什么。
    /// </summary>
    public Func<IReadOnlyList<TrayCommandItem>>? CommandProvider { get; set; }

    /// <summary>附加命令被点击，参数是该条目自带的 Tag（宿主自己的语义编号）。</summary>
    public event Action<int>? CommandInvoked;

    /// <summary>宿主附加命令的号段起点。与 TrayHost 自带的 1001–1004、组件子菜单号段都不重叠。</summary>
    public const int HostCommandBase = 5000;

    /// <summary>
    /// 一条附加命令。<see cref="Tag"/> 交给宿主解释；<b>TrayHost 只按"渲染顺序 + 起始号"发号</b>，
    /// 不让宿主直接给命令号——那样两个来源可能撞号（组件子菜单那次"序号≠kind"的教训同一族）。
    /// </summary>
    public sealed record TrayCommandItem(
        string Label,
        int Tag,
        bool Checked = false,
        bool SeparatorBefore = false,
        bool Enabled = true);

    /// <summary>菜单打开时查询某组件是否已启用（勾选态）；参数同样是 WidgetKind 整数值。</summary>
    public Func<int, bool>? IsWidgetEnabled { get; set; }

    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _added;
    private WndProcDelegate? _wndProc;
    private GCHandle _selfHandle;

    public bool Initialize()
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

            // 全局快捷键不在这里注册：它统一由 HotkeyService 挂在 MainWindow 句柄上
            // （支持多动作 / 允许冲突），TrayHost 曾经也有一个 RegisterHotKey，两边会抢同一手势。
            return true;
        }
        catch (Exception ex)
        {
            StarLog.Error("[TrayHost] 初始化失败", ex);
            return false;
        }
    }

    /// <summary>
    /// 托盘气泡。<b>返回是否真的发出去了</b>：托盘没启用（未 Initialize 成功 / 用户关了托盘 / 图标被系统
    /// 隐藏）时这里发不出去，而调用方（倒计时到点提醒）必须知道这一点才能留下别的证据——
    /// 只有 void 返回值时，"提醒了"与"什么都没发生"在日志里长得一模一样。
    /// </summary>
    public bool ShowNotification(string title, string message)
    {
        if (!_added) return false;
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
            return Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[TrayHost] 通知失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>本次右键实际渲染出来的宿主命令（点击时用行号回查其 Tag）。</summary>
    private readonly List<TrayCommandItem> _hostCommands = new();

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // 原生 WNDPROC 反调托管代码：异常从这儿穿出去既不经 UI 调度器、也不经 DispatcherQueue，
        // Application.UnhandledException 结构上够不到，只能落到 AppDomain（IsTerminating=true）
        // 或直接 fail-fast ⇒ 托盘右键一次就能把常驻应用带走。同类钩子站点都做了包裹，这里补齐。
        try
        {
            if (msg == WM_DESTROY)
            {
                if (_selfHandle.IsAllocated) _selfHandle.Free();
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
        }
        catch (Exception ex) { StarLog.Error($"[TrayHost] 窗口过程异常（已吞，避免穿越原生边界杀进程）：msg=0x{msg:x}", ex); }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();

        AppendMenuW(menu, MF_STRING, (IntPtr)IDM_SHOW, "显示 StarMark");
        AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null!);
        AppendMenuW(menu, MF_STRING, (IntPtr)IDM_WIDGETS, "显示/隐藏全部组件");

        // 桌面组件子菜单：逐项勾选 + 全部显示/隐藏。行由宿主注入（注册表为唯一事实来源）。
        var items = WidgetMenuItems;
        var sub = CreatePopupMenu();
        for (var i = 0; i < items.Count; i++)
        {
            var enabled = IsWidgetEnabled?.Invoke(items[i].Kind) ?? false;
            var flags = MF_STRING | (enabled ? MF_CHECKED : 0);
            AppendMenuW(sub, (uint)flags, (IntPtr)(TrayWidgetMenu.Base + i), items[i].Title);
        }
        AppendMenuW(sub, MF_SEPARATOR, IntPtr.Zero, null!);
        AppendMenuW(sub, MF_STRING, (IntPtr)TrayWidgetMenu.ShowAll, "全部显示");
        AppendMenuW(sub, MF_STRING, (IntPtr)TrayWidgetMenu.HideAll, "全部隐藏");
        AppendMenuW(menu, MF_POPUP, sub, "桌面组件");

        // 宿主附加命令：按渲染顺序发号（HostCommandBase + 行号），点击后回传该行的 Tag。
        _hostCommands.Clear();
        var provided = CommandProvider?.Invoke();
        if (provided is { Count: > 0 })
        {
            foreach (var item in provided)
            {
                if (item.SeparatorBefore) AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null!);
                var flags = MF_STRING | (item.Enabled ? 0 : MF_GRAYED) | (item.Checked ? MF_CHECKED : 0);
                AppendMenuW(menu, (uint)flags, (IntPtr)(HostCommandBase + _hostCommands.Count), item.Label);
                _hostCommands.Add(item);
            }
        }

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
            case TrayWidgetMenu.ShowAll: ShowAllWidgetsRequested?.Invoke(); break;
            case TrayWidgetMenu.HideAll: HideAllWidgetsRequested?.Invoke(); break;
            case IDM_SETTINGS: SettingsRequested?.Invoke(); break;
            default:
                if (cmd >= HostCommandBase)
                {
                    var hostIndex = cmd - HostCommandBase;
                    if (hostIndex >= 0 && hostIndex < _hostCommands.Count)
                        CommandInvoked?.Invoke(_hostCommands[hostIndex].Tag);
                    break;
                }
                // 逐组件项：把命令号换算回菜单序号后，交回该行携带的 kind 值（不是序号）——
                // 序号与枚举值相等只是"注册表按枚举顺序声明"的巧合，拿它当 kind 用会在两者分叉时静默点错组件。
                var offset = cmd - TrayWidgetMenu.Base;
                if (offset >= 0 && offset < WidgetMenuItems.Count)
                    WidgetToggleRequested?.Invoke(WidgetMenuItems[offset].Kind);
                break;
        }
    }

    public void Dispose()
    {
        // 全局热键由 HotkeyService.Dispose 统一注销：TrayHost 从不注册热键，所以这里也没有对应操作。
        try
        {
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