#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Integrations.SystemTray;

namespace StarMark.Integrations.Canvas;

/// <summary>一次鼠标采样（客户区＝本屏的物理像素坐标，原点在这块屏的左上角）。</summary>
/// <param name="At">位置。</param>
/// <param name="LeftDown">左键是否按着（取自消息自带的 wParam 标志，不是我们自己记的状态）。</param>
public readonly record struct CanvasPointer(PixelPoint At, bool LeftDown);

/// <summary>
/// 一块屏幕上的透明画布：<b>纯 Win32 分层窗</b>（<c>WS_EX_LAYERED</c> + <c>UpdateLayeredWindowIndirect</c>），
/// 每屏一个。托管侧一份预乘 BGRA 缓冲，Core 的合成器往里写，脏区逐行拷进 DIB section 再交给系统。
/// <para>
/// <b>为什么不用 WinUI 3 的 <c>Window</c></b>：那套没有"整窗透明 + 只显示笔迹像素"的合成语义
/// （背景可以材质化，但不能做成"这块玻璃的其他地方根本不参与绘制"）。Snipaste / Epic Pen / gInk 全部走分层窗，
/// 这里不是另辟蹊径而是走那条被验证过的路。
/// </para>
/// <para>三条只有这类窗才会遇到的事，已经按注释钉在代码上：</para>
/// <para>① <b>按下必须 SetCapture</b>——不抓到鼠标，笔迹拖出屏幕边缘就再也收不到抬起，笔"永远没松"；</para>
/// <para>② <b>WM_PAINT 必须 BeginPaint/EndPaint</b>——不验证更新区域，系统会不停重发 WM_PAINT，界面卡死；</para>
/// <para>③ <b>WndProc 委托要有静态引用兜着</b>——它是非托管代码回调进来的唯一入口，被 GC 挪走/回收之后
/// 症状是"消息忽然全没了"，而且不抛托管异常（<c>ClipboardWatcher</c> 那次同一课）。</para>
/// </summary>
public sealed class LayeredCanvasWindow : IDisposable
{
    private const string ClassName = "StarMarkCanvasLayer";

    /// <summary>静态持有：WndProc 的委托与"句柄→实例"的表。表要锁——消息可能来自同一线程的重入。</summary>
    private static NativeMethods.WndProcDelegate? _sharedProc;
    private static readonly Dictionary<IntPtr, LayeredCanvasWindow> Instances = new();
    private static readonly object Gate = new();
    private static ushort _classAtom;

    private readonly IntPtr _hwnd;
    private readonly IntPtr _memDc;
    private readonly IntPtr _dib;
    private readonly IntPtr _bits;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>本块屏在虚拟桌面里的位置（物理像素）。跨屏分发笔迹、快照拼接都要用它。</summary>
    public IntRect Bounds { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// 托管绘制缓冲（预乘 BGRA，<c>B | G&lt;&lt;8 | R&lt;&lt;16 | A&lt;&lt;24</c>）。
    /// <b>Core 的合成器直接写这里</b>；Present 时只把脏区那些行拷进 DIB section。
    /// </summary>
    public uint[] Pixels { get; }

    /// <summary>鼠标穿透（点穿到下面的 PPT／桌面）。开启时本窗收不到任何鼠标消息。</summary>
    public bool IsClickThrough { get; private set; }

    public event Action<CanvasPointer>? PointerPressed;
    public event Action<CanvasPointer>? PointerMoved;
    public event Action<CanvasPointer>? PointerReleased;

    /// <summary>分辨率／显示器拓扑变了：这块缓冲的尺寸已经是错的，调用方要重建画布（不能凑合画）。</summary>
    public event Action? DisplayChanged;

    public LayeredCanvasWindow(IntRect boundsPhys)
    {
        if (boundsPhys.Width <= 0 || boundsPhys.Height <= 0)
            throw new ArgumentException($"画布尺寸不合法（{boundsPhys.Width} × {boundsPhys.Height}）");
        Bounds = boundsPhys;
        Width = boundsPhys.Width;
        Height = boundsPhys.Height;
        Pixels = new uint[Width * Height];

        EnsureClassRegistered();
        _hwnd = NativeMethods.CreateWindowExW(
            CanvasNative.WS_EX_LAYERED | CanvasNative.WS_EX_TRANSPARENT | CanvasNative.WS_EX_TOPMOST
                | CanvasNative.WS_EX_TOOLWINDOW | CanvasNative.WS_EX_NOACTIVATE,
            ClassName, "StarMark Canvas", NativeMethods.WS_POPUP,
            boundsPhys.X, boundsPhys.Y, Width, Height,
            IntPtr.Zero, IntPtr.Zero,
            NativeMethods.GetModuleHandleW(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"创建分层画布窗失败（Win32 {Marshal.GetLastWin32Error()}）");

        lock (Gate) Instances[_hwnd] = this;

        var screenDc = NativeMethods.GetDC(_hwnd);
        try
        {
            _memDc = CanvasNative.CreateCompatibleDC(screenDc);
            if (_memDc == IntPtr.Zero) throw new InvalidOperationException("创建内存 DC 失败");
            _dib = CreateArgbDib(_memDc, Width, Height, out _bits);
            if (_dib == IntPtr.Zero || _bits == IntPtr.Zero)
                throw new InvalidOperationException($"创建画布后备位图失败（{Width} × {Height}，Win32 {Marshal.GetLastWin32Error()}）");
            if (CanvasNative.SelectObject(_memDc, _dib) == IntPtr.Zero)
                throw new InvalidOperationException("把画布位图选进 DC 失败");
        }
        finally
        {
            NativeMethods.ReleaseDC(_hwnd, screenDc);
        }

        // 建窗就压在最上层（WS_EX_TOPMOST 只是样式位，位置要过一发 SetWindowPos 才落实）
        CanvasNative.SetWindowPos(_hwnd, CanvasNative.HWND_TOPMOST, boundsPhys.X, boundsPhys.Y, Width, Height,
            CanvasNative.SWP_NOACTIVATE);
        // 先以"穿透 + 隐藏"起步：进入绘制态由调用方显式打开（一开就挡住全屏鼠标是设计语义，不该是默认）
        NativeMethods.ShowWindow(_hwnd, CanvasNative.SW_SHOWNOACTIVATE);
        SetClickThrough(true);
    }

    /// <summary>整块板子交出去（首帧、以及 WM_PAINT 要求重绘时）。</summary>
    public void PresentAll()
    {
        if (_disposed) return;
        var whole = new IntRect(0, 0, Width, Height);
        Push(whole, whole);
    }

    /// <summary>
    /// 只提交脏区。<b>脏区为空就直接返回</b>——每帧整张 4K 提交是 33MB，规格 §16.7 明令禁止，
    /// 而这一句就是"允许调用方传空"的全部含义。
    /// </summary>
    public void Present(IntRect dirty)
    {
        if (_disposed) return;
        var clipped = Clip(dirty);
        if (clipped.IsEmpty) return;
        Push(clipped, clipped);
    }

    /// <summary>显示／隐藏这块板子（隐藏后笔迹仍在内存里，回来还在）。</summary>
    public void SetVisible(bool visible)
        => NativeMethods.ShowWindow(_hwnd, visible ? CanvasNative.SW_SHOWNOACTIVATE : NativeMethods.SW_HIDE);

    /// <summary>
    /// 开／关鼠标穿透。开启后本窗收不到鼠标消息，笔迹仍然显示——
    /// 这是"边讲边翻 PPT"那一态；关掉它才有绘制。
    /// </summary>
    public void SetClickThrough(bool on)
    {
        var ex = (ulong)NativeMethods.GetWindowLongPtrW(_hwnd, CanvasNative.GWL_EXSTYLE).ToInt64();
        if (on) ex |= CanvasNative.WS_EX_TRANSPARENT;
        else ex &= ~CanvasNative.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtrW(_hwnd, CanvasNative.GWL_EXSTYLE, new IntPtr((long)ex));
        IsClickThrough = on;
    }

    /// <summary>绘制态给十字光标（"现在按下去就会画东西"这件事要有视觉交代），穿透态恢复箭头。</summary>
    public void SetDrawCursor(bool cross) => _crossCursor = cross;

    private bool _crossCursor;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (Gate) Instances.Remove(_hwnd);
        if (_dib != IntPtr.Zero) NativeMethods.DeleteObject(_dib);
        if (_memDc != IntPtr.Zero) CanvasNative.DeleteDC(_memDc);
        NativeMethods.DestroyWindow(_hwnd);
    }

    // ────────── 内部 ──────────

    private IntRect Clip(IntRect rect)
    {
        var x1 = Math.Max(0, rect.X);
        var y1 = Math.Max(0, rect.Y);
        var x2 = Math.Min(Width, rect.Right);
        var y2 = Math.Min(Height, rect.Bottom);
        return x2 <= x1 || y2 <= y1 ? new IntRect(0, 0, 0, 0) : new IntRect(x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>
    /// 把脏区逐行拷进 DIB section，然后交给系统。<paramref name="updateRect"/> 是告诉系统的脏区
    /// （可以与 <paramref name="source"/> 相同：整帧那条路用于首帧与 WM_PAINT）。
    /// </summary>
    private unsafe void Push(IntRect source, IntRect updateRect)
    {
        lock (_gate)
        {
            fixed (uint* managed = Pixels)
            {
                var from = (byte*)managed;
                var to = (byte*)_bits;
                for (var y = source.Y; y < source.Bottom; y++)
                {
                    var offset = (y * Width + source.X) * 4;
                    var bytes = source.Width * 4;
                    Buffer.MemoryCopy(from + offset, to + offset, bytes, bytes);
                }
            }

            var srcPoint = new NativeMethods.POINT { X = 0, Y = 0 };
            var size = new CanvasNative.SIZE { cx = Width, cy = Height };
            var blend = new CanvasNative.BLENDFUNCTION
            {
                BlendOp = 0,                       // AC_SRC_OVER
                BlendFlags = 0,
                SourceConstantAlpha = 255,         // 不额外衰减整体：透明度全在逐像素 alpha 里
                AlphaFormat = 1,                   // AC_SRC_ALPHA：交上去的就是<b>预乘</b> BGRA
            };
            var dirty = CanvasNative.ToWin32Rect(updateRect);
            var info = new CanvasNative.UPDATELAYEREDWINDOWINFO
            {
                cbSize = (uint)sizeof(CanvasNative.UPDATELAYEREDWINDOWINFO),
                hdcDst = IntPtr.Zero,
                pptSrc = (IntPtr)(&srcPoint),
                psize = (IntPtr)(&size),
                hdcSrc = _memDc,
                pptDst = IntPtr.Zero,              // NULL＝不动窗口位置（画布是铺满屏的，改位置只会闪）
                pwcrKey = IntPtr.Zero,
                pbcf = (IntPtr)(&blend),
                dwFlags = 0,
                prcDirty = (IntPtr)(&dirty),
            };
            if (CanvasNative.UpdateLayeredWindowIndirect(_hwnd, ref info)) return;

            // 兜底：Indirect 失手（驱动/远程会话上见过）就整帧走老接口。
            // 这条路的代价是一次 33MB 提交，但"画布完全不出现"比慢一下严重得多。
            var error = Marshal.GetLastWin32Error();
            var plainSize = size;
            var plainSrc = srcPoint;
            var plainBlend = blend;
            if (!CanvasNative.UpdateLayeredWindow(_hwnd, IntPtr.Zero, IntPtr.Zero, ref plainSize,
                    _memDc, ref plainSrc, IntPtr.Zero, ref plainBlend, 0))
            {
                StarLog.WarnThrottled("canvas:present",
                    $"画布提交失败（Win32 {error}，兜底也失败 {Marshal.GetLastWin32Error()}）", windowMs: 30_000);
                return;
            }
            StarLog.WarnThrottled("canvas:present",
                $"画布脏区提交失手（Win32 {error}），已改整帧提交", windowMs: 30_000);
        }
    }

    private static IntPtr CreateArgbDib(IntPtr dc, int width, int height, out IntPtr bits)
    {
        var info = new NativeMethods.BITMAPINFO
        {
            biSize = Marshal.SizeOf<NativeMethods.BITMAPINFO>(),
            biWidth = width,
            biHeight = -height,          // 负＝自上而下的行序，与托管缓冲一致
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,           // BI_RGB
            biSizeImage = width * height * 4,
        };
        return NativeMethods.CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0);
    }

    private static void EnsureClassRegistered()
    {
        if (_classAtom != 0) return;
        _sharedProc ??= HandleMessage;        // 静态持有：这条委托是内核回调进来的唯一入口，不能被回收
        var instance = NativeMethods.GetModuleHandleW(null);
        var wcx = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = _sharedProc,
            hInstance = instance,
            hCursor = NativeMethods.LoadCursorW(instance, CanvasNative.IDC_ARROW),
            hbrBackground = IntPtr.Zero,      // 分层窗没有"背景刷"这件事：内容全靠 UpdateLayeredWindow 供
            lpszClassName = ClassName,
            hIcon = IntPtr.Zero,
            hIconSm = IntPtr.Zero,
        };
        _classAtom = NativeMethods.RegisterClassExW(ref wcx);
        if (_classAtom == 0)
        {
            var error = Marshal.GetLastWin32Error();
            // 已经注册过（同一进程内重复注册返回 0 且错误码 ERROR_CLASS_ALREADY_EXISTS=1414）：可以继续
            if (error != 1414) throw new InvalidOperationException($"注册画布窗口类失败（Win32 {error}）");
            _classAtom = 1;
        }
    }

    private static IntPtr HandleMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        LayeredCanvasWindow? window;
        lock (Gate) Instances.TryGetValue(hWnd, out window);
        if (window is null || window._disposed)
            return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);

        var buttons = wParam.ToInt64();
        var leftDown = (buttons & CanvasNative.MK_LBUTTON) != 0;
        var sample = new CanvasPointer(CanvasNative.LoWordPoint(lParam), leftDown);

        switch (msg)
        {
            case CanvasNative.WM_LBUTTONDOWN:
                // 不抓鼠标就会漏掉抬起：笔停在"还在画"的状态，用户只能重开画布
                CanvasNative.SetCapture(hWnd);
                window.PointerPressed?.Invoke(sample);
                return IntPtr.Zero;

            case CanvasNative.WM_MOUSEMOVE:
                window.PointerMoved?.Invoke(sample);
                return IntPtr.Zero;

            case CanvasNative.WM_LBUTTONUP:
                CanvasNative.ReleaseCapture();
                window.PointerReleased?.Invoke(sample with { LeftDown = false });
                return IntPtr.Zero;

            case CanvasNative.WM_SETCURSOR:
                CanvasNative.SetCursor(IntPtr.Zero, window._crossCursor && !window.IsClickThrough
                    ? CanvasNative.IDC_CROSS
                    : CanvasNative.IDC_ARROW);
                return IntPtr.Zero;

            case CanvasNative.WM_ERASEBKGND:
                return new IntPtr(1);   // 1＝背景已处理：这里没有背景可擦，真擦一下就是一闪

            case CanvasNative.WM_PAINT:
            {
                // 必须 Begin/EndPaint 把更新区域验证掉，否则系统按帧重发 WM_PAINT
                var paint = new CanvasNative.PAINTSTRUCT();
                CanvasNative.BeginPaint(hWnd, out paint);
                CanvasNative.EndPaint(hWnd, ref paint);
                window.PresentAll();          // 内容只在我们这份缓冲里，系统重建时只能由我们重交一遍
                return IntPtr.Zero;
            }

            case CanvasNative.WM_DISPLAYCHANGE:
                window.DisplayChanged?.Invoke();
                return IntPtr.Zero;

            case CanvasNative.WM_DESTROY:
                lock (Gate) Instances.Remove(hWnd);
                return IntPtr.Zero;

            default:
                return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }
}
