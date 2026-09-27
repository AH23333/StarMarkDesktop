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

    /// <summary>
    /// "这里什么都没画"的那个像素值：<b>alpha=1 而不是 0</b>。
    /// <para>
    /// 分层窗的鼠标命中测试是逐像素看的——<b>alpha=0 的像素永远不算这个窗的</b>，消息直接给下层窗口。
    /// 于是"擦干净"写成 0 的症状是：在擦过的那块地方点不动、也画不上（真机反馈的"选了中国画笔还是画不了"）。
    /// alpha=1 肉眼不可见（1/255 ≈ 0.4%），但命中测试认它；至于到底拦不拦鼠标，由
    /// <c>WS_EX_TRANSPARENT</c> 单独说话（<see cref="SetClickThrough"/>），两者各管一头。
    /// </para>
    /// <para>
    /// <b>所有写"空白"的地方都必须用这个值</b>：构造时的初值、<c>Clear</c>/<c>ClearRect</c>、
    /// 橡皮擦到见底。漏一处就有一块区域重新变成"点不动"，而且那块正好是用户刚刚画过的地方——最难复现的那种。
    /// </para>
    /// </summary>
    public const uint BlankPixel = 0x01000000u;   // B=0 G=0 R=0 A=1

    /// <summary>静态持有：WndProc 的委托与"句柄→实例"的表。表要锁——消息可能来自同一线程的重入。</summary>
    private static NativeMethods.WndProcDelegate? _sharedProc;
    private static readonly Dictionary<IntPtr, LayeredCanvasWindow> Instances = new();
    private static readonly object Gate = new();
    private static ushort _classAtom;

    private readonly IntPtr _hwnd;

    /// <summary>这块玻璃自己的顶层句柄。<b>只用来与别的窗的"顶层祖先"比</b>（见 CanvasService 的验层判据），
    /// 不要拿它去等值比较 <c>WindowFromPoint</c> 的返回值——那比的是子窗，永远不相等。</summary>
    public IntPtr Hwnd => _hwnd;
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

    /// <summary>
    /// 右键按下。<b>画布上右键没有别的用途</b>，所以它是"把鼠标交还给下面的应用"的唯一纯鼠标出口——
    /// 光标看不见、工具条找不到的时候，人只剩下点鼠标这一件事可做（真机反馈定出来的设计）。
    /// </summary>
    public event Action? RightPressed;

    /// <summary>分辨率／显示器拓扑变了：这块缓冲的尺寸已经是错的，调用方要重建画布（不能凑合画）。</summary>
    public event Action? DisplayChanged;

    public LayeredCanvasWindow(IntRect boundsPhys)
    {
        if (boundsPhys.Width <= 0 || boundsPhys.Height <= 0)
            throw new ArgumentException($"画布尺寸不合法（{boundsPhys.Width} × {boundsPhys.Height}）");
        Bounds = boundsPhys;
        Width = boundsPhys.Width;
        Height = boundsPhys.Height;
        // 整块板子以"空白"起步（alpha=1，见 <see cref="BlankPixel"/>）：0 会让这块玻璃在鼠标眼里不存在
        Pixels = new uint[Width * Height];
        Array.Fill(Pixels, BlankPixel);

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
        // <b>开局就把整块"空白"表面交给系统</b>。托管侧那份 <c>BlankPixel</c> 只是我们自己的缓冲：
        // 分层窗在第一次 <c>UpdateLayeredWindow</c> 之前，系统那边那层是<b>全 alpha=0</b>，
        // 而 alpha=0 的像素对鼠标命中测试等于"这里没有窗"——<c>WindowFromPoint</c> 会一路穿到下层应用。
        // 真机症状（批次 WO）：绘制态下光标只要停在<b>还没画过的地方</b>，自校验就误判成
        // "别人占了那一层"并把鼠标交回去，于是"刚点画笔就画不上"；而有墨的地方一切正常，
        // 看起来就像"有时能画有时不能"。空白像素必须是 alpha=1 并且<b>真的交上去</b>，两条缺一不可。
        PresentAll();
    }

    /// <summary>
    /// 左键现在按着吗（全局状态，不是本窗收到的消息）。
    /// <b>穿透态下本窗收不到任何鼠标消息</b>，而规格 §16.5.2 要"按住即画、松开即透"——
    /// 那一次按下归了下层应用，我们只能自己看按键状态。原生细节留在 <c>CanvasNative</c> 里，
    /// 这里只露两个布尔出去（那层是 internal，UI 侧不该看见 P/Invoke）。
    /// </summary>
    public static bool LeftButtonDown => CanvasNative.IsDown(CanvasNative.VK_LBUTTON);

    /// <summary>Ctrl+Alt 是否同时按着（穿透态下的"快速圈画"修饰键，§16.5.3 第一行）。</summary>
    public static bool CtrlAltDown
        => CanvasNative.IsDown(CanvasNative.VK_CONTROL) && CanvasNative.IsDown(CanvasNative.VK_MENU);

    /// <summary>
    /// 屏幕坐标上这一点<b>当前归哪个窗口</b>（绘制态自校验用）。<b>返回的是这一点上最深的那个 HWND</b>：
    /// WinUI 窗（工具条、快捷键面板）的内容住在它自己的子窗里，所以别拿它跟 <c>GetHwnd()</c> 比相等，
    /// 要比就比进程归属（判据在 <c>LayerDirector.Classify</c>，动作在 <c>AnnotationHub.AuditFrame</c>）。
    /// 穿透态下本窗被 <c>WS_EX_TRANSPARENT</c> 跳过，结果必然是别人，所以这条探测只在绘制态说话。
    /// </summary>
    public static IntPtr WindowAt(int screenX, int screenY)
        => CanvasNative.WindowFromPoint(new NativeMethods.POINT { X = screenX, Y = screenY });

    /// <summary>
    /// 主动抓鼠标。<b>穿透态下"按住即画"是轮询发现的</b>：那一次 WM_LBUTTONDOWN 已经发给下层应用了，
    /// 我们不会收到，所以摘掉穿透之后要自己补一次 SetCapture，否则抬起永远收不到＝笔"没松"。
    /// </summary>
    public void Capture() => CanvasNative.SetCapture(_hwnd);

    /// <summary>
    /// 把鼠标捕获交还系统。<b>这是整条线程级的动作，不是"这一扇窗的"</b>，所以做成静态的。
    /// <para>玻璃被<b>藏起来</b>的那一刻必须叫一次：捕获不会因为窗被隐藏而失效，下一次"抬起"照样寄给
    /// 那扇已经看不见的窗，而后来上屏的那扇（截图遮罩）永远等不到松手＝整场截图卡在暗幕上。
    /// 窗被销毁时系统自己会放手，所以只有"收起但没拆"这条路需要手动还。</para>
    /// </summary>
    public static void ReleasePointerCapture() => CanvasNative.ReleaseCapture();

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
        // 改完扩展样式补一发 SWP_FRAMECHANGED：穿透能不能立刻生效在部分系统/远程会话上不只看样式位，
        // "点了没关掉、也没画上"这种半生效状态最难查，宁可多发一条消息。
        // <b>必须带 SWP_NOZORDER</b>：顺手把自己提到 topmost 链顶端的话，画布会盖到工具条之上，
        // 而工具条是穿透态下唯一还能用鼠标点到的出口。
        CanvasNative.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            CanvasNative.SWP_NOMOVE | CanvasNative.SWP_NOSIZE | CanvasNative.SWP_NOACTIVATE
                | CanvasNative.SWP_NOZORDER | CanvasNative.SWP_FRAMECHANGED);
        IsClickThrough = on;
    }

    /// <summary>
    /// <b>窗口此刻实际带着 <c>WS_EX_TRANSPARENT</c> 吗</b>——读的是样式位本身，不是 <see cref="IsClickThrough"/>
    /// 那个我们自己记的旗标。两者可以分岔（某条路只改了一边、或系统/远程会话把那位改回去），
    /// 而分岔的样子是"工具条说绘制中、点下去却画不上"：界面读旗标，鼠标归谁读的是那一位。
    /// 编排层（CanvasService）每帧拿它对一次账，并在不一致时按状态改回来。
    /// </summary>
    public bool StyleClickThrough
        => ((ulong)NativeMethods.GetWindowLongPtrW(_hwnd, CanvasNative.GWL_EXSTYLE).ToInt64()
            & CanvasNative.WS_EX_TRANSPARENT) != 0;

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

            var srcOrigin = new NativeMethods.POINT { X = 0, Y = 0 };   // 整块缓冲就是这块玻璃，取样原点当然在左上角
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
                pptDst = IntPtr.Zero,              // NULL＝不动窗口位置（画布铺满这块屏，改位置只会闪）
                psize = (IntPtr)(&size),
                hdcSrc = _memDc,
                pptSrc = (IntPtr)(&srcOrigin),
                crKey = 0,                         // 不用颜色键：透明度全在 alpha 通道里
                pbcf = (IntPtr)(&blend),
                dwFlags = CanvasNative.ULW_ALPHA,  // <b>少了这一位，上面整个 blend 结构就被系统忽略</b>
                prcDirty = (IntPtr)(&dirty),
            };
            if (CanvasNative.UpdateLayeredWindowIndirect(_hwnd, ref info)) return;

            // 兜底：Indirect 失手（驱动/远程会话上见过）就整帧走老接口——它没有脏区参数，整张交一次。
            // 这条路的代价是一次全屏提交，但"画布完全不出现"比慢一下严重得多。
            var error = Marshal.GetLastWin32Error();
            var plainSize = size;
            var plainSrc = srcOrigin;
            var plainBlend = blend;
            if (!CanvasNative.UpdateLayeredWindow(_hwnd, IntPtr.Zero, IntPtr.Zero, ref plainSize,
                    _memDc, ref plainSrc, 0, ref plainBlend, CanvasNative.ULW_ALPHA))
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

            case CanvasNative.WM_RBUTTONDOWN:
                // 右键在画布上没有别的用途，就把它当"把鼠标交还给下面"的鼠标出口：
                // 光标看不见、工具条找不到的时候，热键与托盘是键盘式出口，这一条不需要键盘。
                CanvasNative.ReleaseCapture();
                window.RightPressed?.Invoke();
                return IntPtr.Zero;

            case CanvasNative.WM_SETCURSOR:
                // 必须先 SetCursor 再回 TRUE：回 FALSE 等于告诉系统"我没处理"，
                // 而这条链上一次的真实事故是把参数写错成两个（NULL 句柄＝光标直接消失）。
                CanvasNative.SetCursor(window._crossCursor && !window.IsClickThrough
                    ? CanvasNative.IDC_CROSS
                    : CanvasNative.IDC_ARROW);
                return new IntPtr(1);

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
