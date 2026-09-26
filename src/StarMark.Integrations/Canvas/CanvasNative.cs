#nullable enable
using System;
using System.Runtime.InteropServices;
using StarMark.Abstractions.Capture;

namespace StarMark.Integrations.Canvas;

/// <summary>
/// 分层画布窗需要的 Win32 声明（<see cref="LayeredCanvasWindow"/> 的细节都收在这里）。
/// <para>
/// 窗口类注册、<c>CreateWindowExW</c>、<c>Get/SetWindowLongPtr</c>、<c>CreateDIBSection</c>、
/// <c>GetCursorPos</c> 这些已经在 <c>SystemTray.NativeMethods</c> 里有过一份，这里<b>只补画布缺的那几件</b>
/// ——同一件事在两处各声明一遍是这个仓库反复出坑的地方（结构体对齐/字段顺序写错不会编译失败，
/// 只会让 WndProc 收不到东西）。
/// </para>
/// </summary>
internal static class CanvasNative
{
    // ===== 扩展样式 =====
    public const uint WS_EX_LAYERED = 0x0008_0000;
    public const uint WS_EX_TRANSPARENT = 0x0000_0020;
    public const uint WS_EX_TOOLWINDOW = 0x0000_0080;
    public const uint WS_EX_TOPMOST = 0x0000_0008;
    public const uint WS_EX_NOACTIVATE = 0x0800_0000;
    public const int GWL_EXSTYLE = -20;

    // ===== 消息 =====
    public const uint WM_PAINT = 0x000F;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_NCHITTEST = 0x0084;

    public const int MK_LBUTTON = 0x0001;
    public const int MK_RBUTTON = 0x0002;

    // ===== 分层窗提交标志（UpdateLayeredWindow / …Indirect 的 dwFlags） =====

    /// <summary>
    /// 按 <c>pblend</c> 逐像素合成。<b>0 不是"什么都不做"，而是"不要用混合函数"</b>：
    /// 于是这块玻璃被按不透明贴图整块刷上去，笔迹一点没露出来，先露出来的是一面黑墙——
    /// 全屏 topmost 的黑墙还吃掉所有鼠标，真机上报的就是"一按画笔整块屏幕像卡死了"。
    /// </summary>
    public const uint ULW_ALPHA = 0x0000_0002;

    // ===== 光标 =====
    public static readonly IntPtr IDC_ARROW = new(32512);
    public static readonly IntPtr IDC_CROSS = new(32515);

    // ===== 显示 =====
    public const int SW_SHOWNOACTIVATE = 4;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const uint SWP_FRAMECHANGED = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    /// <summary>BLENDFUNCTION 四个字段都是 BYTE，<b>顺序写错不会报错、只会让整块板子变成不透明的灰</b>。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;               // AC_SRC_OVER = 0
        public byte BlendFlags;            // 0
        public byte SourceConstantAlpha;   // 255：整体不额外衰减，逐像素走 alpha
        public byte AlphaFormat;           // AC_SRC_ALPHA = 1：源是<b>预乘</b> BGRA
    }

    /// <summary>
    /// <c>UpdateLayeredWindowIndirect</c> 的参数块。<b>字段顺序与 winuser.h 逐字对齐</b>
    /// （cbSize / hdcDst / pptDst / psize / hdcSrc / pptSrc / crKey / pblend / dwFlags / prcDirty）；
    /// 指针成员指向栈上（stackalloc）的结构，调用返回后即失效，不留悬挂。
    /// <para>
    /// <b>名字要按槽位起，不能按"我以为它是干什么的"起</b>：这一版原先第 3 个槽写作 <c>pptSrc</c>、
    /// 第 6 个写作 <c>pptDst</c>，按名字赋值看着完全对称、编译通过、<c>cbSize</c> 也对，
    /// 于是系统把那块全屏玻璃"搬"到了 (0,0)，而源原点反而是 NULL。这类错位不会有任何报错，
    /// 只会让用户看见"一按下笔整块屏幕变成一面墙"。
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UPDATELAYEREDWINDOWINFO
    {
        public uint cbSize;
        public IntPtr hdcDst;              // 文档：Indirect 里不使用，置 NULL
        public IntPtr pptDst;              // POINT*：窗口的<b>新屏幕位置</b>，NULL＝不改位置
        public IntPtr psize;               // SIZE*
        public IntPtr hdcSrc;
        public IntPtr pptSrc;              // POINT*：源 DC 里这块图的取样原点
        public uint crKey;                 // COLORREF（是值，不是指针——文档的槽位 7）
        public IntPtr pbcf;                // BLENDFUNCTION*
        public uint dwFlags;               // 必须带 ULW_ALPHA，否则 pbcf 整个被忽略
        public IntPtr prcDirty;            // RECT*：<b>脏区就在这儿</b>，4K 全屏每帧整张提交是 33MB
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        public unsafe fixed byte rgbReserved[32];
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindowIndirect(IntPtr hwnd, ref UPDATELAYEREDWINDOWINFO pblit);

    /// <summary>老接口，没有脏区参数——只作为 <see cref="UpdateLayeredWindowIndirect"/> 失手时的兜底（整帧交一次）。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst,
        ref SIZE psize, IntPtr hdcSrc, ref SystemTray.NativeMethods.POINT pptSrc,
        uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    /// <summary>按下时必须抓Mouse：不抓到，笔迹拖到屏幕边缘就再也收不到抬起消息＝笔永远"没松"。</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr GetCapture();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>
    /// <c>SetCursor</c> 只有一个参数（光标句柄）。<b>传 NULL 就是"把光标藏起来"</b>——
    /// 之前按 <c>SetCursor(hWnd, hCursor)</c> 两个参数声明过，x64 下第一个实参落到句柄位上，
    /// 于是整块画布上鼠标直接消失（真机反馈："看不见鼠标、点不到任何东西"）。
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    /// <summary>把 lParam 拆成客户区坐标（低 16 位 x、高 16 位 y，<b>都是 signed</b>：多屏负原点时会读出大正数）。</summary>
    public static PixelPoint LoWordPoint(IntPtr lParam)
    {
        var raw = lParam.ToInt64();
        return new PixelPoint((short)(raw & 0xFFFF), (short)(raw >> 16 & 0xFFFF));
    }

    /// <summary>把脏区转成 Win32 的 RECT（left/top/right/bottom，不是 x/y/w/h）。</summary>
    public static RECT ToWin32Rect(IntRect rect) => new()
    {
        left = rect.X,
        top = rect.Y,
        right = rect.Right,
        bottom = rect.Bottom,
    };
}
