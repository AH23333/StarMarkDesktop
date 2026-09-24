#nullable enable
using System;
using System.Runtime.InteropServices;

namespace StarMark.Integrations.Capture;

/// <summary>
/// 用 GDI 把文字画进一块 BGRA 缓冲——截图标注里的"文字"这一路。
/// <para>
/// 为什么用 GDI 而不是 XAML 渲染：<b>输出要能被实测</b>。走 <c>RenderTargetBitmap</c> 的话，
/// "渲染出来的那张图对不对"只有界面真的跑起来才知道，判据全在 WinRT 内部；
/// GDI 这条路是同一份像素、同一套字距，探针与单测都能画完直接断言像素。
/// </para>
/// <para>
/// 画之前先把<b>目标那一块的原像素</b>搬进临时 DIB，再在上面写字（<c>SetBkMode(TRANSPARENT)</c>），
/// 写完搬回去。这样"字底下的背景"天然就是对的——底下是截图、是马赛克、还是刚画的红色矩形都一样，
/// 不需要任何 alpha 混合，也不用猜 GDI 会不会顺手把背景刷白。
/// </para>
/// </summary>
public static class GdiTextDrawer
{
    /// <summary>
    /// 在 <paramref name="atX"/>/<paramref name="atY"/>（文字框左上角，缓冲内坐标）画一行文字。
    /// <para>字高按<b>物理像素</b>给：这块缓冲就是物理像素，传 DIP 进来等于让调用方去猜 DPI。</para>
    /// <para>
    /// 失败一律抛 <see cref="InvalidOperationException"/> 并带上原因。宁可让用户看到"字没写出去"，
    /// 也不能静默交出一张少了字的图——那种图会被当成"我刚才明明写了字"，事后无从分辨。
    /// </para>
    /// </summary>
    public static void Draw(byte[] bgra, int width, int height, int atX, int atY, string text, int fontHeight, int colorBgra)
    {
        if (bgra is null || width <= 0 || height <= 0 || bgra.Length < (long)width * height * 4)
            throw new ArgumentException("像素缓冲比声明的尺寸短，画上去会越界", nameof(bgra));
        if (string.IsNullOrEmpty(text)) return;
        if (fontHeight < 1) throw new InvalidOperationException("文字高度至少 1 像素");

        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new InvalidOperationException("创建内存画布失败（GDI 句柄用尽）");
        var font = CreateFont(fontHeight);
        IntPtr oldFont = IntPtr.Zero, dib = IntPtr.Zero, oldObject = IntPtr.Zero, header = IntPtr.Zero;
        var bits = IntPtr.Zero;
        var tile = new byte[0];
        try
        {
            if (font == IntPtr.Zero) throw new InvalidOperationException("系统没能创建标注用的字体（雅黑与 Segoe UI 都试过）");
            oldFont = SelectObject(dc, font);
            if (!GetTextExtentPoint32W(dc, text, text.Length, out var extent) || extent.cx <= 0 || extent.cy <= 0)
                throw new InvalidOperationException("量不出这行文字要占多大（GDI 拒绝了这个字体或这段文字）");

            // 只处理与画布相交的那一块：用户可以把字写到选区外面，那部分应当被切掉而不是越界写
            var left = Math.Max(0, atX);
            var top = Math.Max(0, atY);
            var right = Math.Min(width, atX + extent.cx);
            var bottom = Math.Min(height, atY + extent.cy);
            var tileWidth = right - left;
            var tileHeight = bottom - top;
            if (tileWidth <= 0 || tileHeight <= 0) return;        // 整行都在画布外：没东西可画，也不算错

            header = AllocTopDown32Header(tileWidth, tileHeight);
            dib = CreateDIBSection(dc, header, DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                throw new InvalidOperationException("创建文字画布失败（内存不足）");
            oldObject = SelectObject(dc, dib);

            // 逐行在"GDI 的位图内存"与托管数组之间搬：Marshal.Copy 内部自己 pin，
            // 一次一行（行数就是字高，几十行封顶），比逐像素快一个数量级也不用自己管固定地址
            var rowBytes = tileWidth * 4;
            tile = new byte[rowBytes];
            for (var y = 0; y < tileHeight; y++)
            {
                Buffer.BlockCopy(bgra, ((top + y) * width + left) * 4, tile, 0, rowBytes);
                Marshal.Copy(tile, 0, bits + y * rowBytes, rowBytes);
            }

            SetTextColor(dc, unchecked((uint)ColorRefOf(colorBgra)));
            SetBkMode(dc, TRANSPARENT);
            // 文字相对 tile 左上角的位置 = 目标位置 − tile 左上角（tile 可能被画布边界切过，所以不能填 0）
            if (!TextOutW(dc, left - atX, top - atY, text, text.Length))
                throw new InvalidOperationException($"文字没能写出去（Win32 {Marshal.GetLastWin32Error()}）");
            // GDI 的绘制可以被打批：不刷的话读回来可能是旧的位图内存
            GdiFlush();

            for (var y = 0; y < tileHeight; y++)
            {
                Marshal.Copy(bits + y * rowBytes, tile, 0, rowBytes);
                // GDI 写字时把它碰过的像素的 alpha 清成 0（32 位 DIB 里那个字节对 GDI 没有意义）。
                // 不补回 255 的话，"字"在 PNG 里就是一堆全透明像素——看着是画上了，存出去是个洞。
                for (var x = 3; x < rowBytes; x += 4) tile[x] = 255;
                Buffer.BlockCopy(tile, 0, bgra, ((top + y) * width + left) * 4, rowBytes);
            }
        }
        finally
        {
            if (oldObject != IntPtr.Zero) SelectObject(dc, oldObject);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (header != IntPtr.Zero) Marshal.FreeHGlobal(header);
            if (oldFont != IntPtr.Zero) SelectObject(dc, oldFont);
            if (font != IntPtr.Zero) DeleteObject(font);
            DeleteDC(dc);
        }
    }

    /// <summary>
    /// 这行字实际要占多大（物理像素）。<b>选择框与命中测试只能量，不能估</b>：
    /// 按"字高 × 字数"估宽，中文与英文差着两三倍，框会框到一个不存在的位置，
    /// 用户就会看到"我点那行字，选中的却是旁边那条线"。
    /// 与 <see cref="Draw"/> 用同一个字体选择顺序，量出来的才是真正会被画出去的那一块。
    /// </summary>
    public static (int Width, int Height) Measure(string text, int fontHeight)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0);
        if (fontHeight < 1) throw new InvalidOperationException("文字高度至少 1 像素");
        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new InvalidOperationException("创建内存画布失败（GDI 句柄用尽）");
        var font = CreateFont(fontHeight);
        try
        {
            if (font == IntPtr.Zero) throw new InvalidOperationException("系统没能创建标注用的字体（雅黑与 Segoe UI 都试过）");
            var oldFont = SelectObject(dc, font);
            if (!GetTextExtentPoint32W(dc, text, text.Length, out var extent) || extent.cx <= 0 || extent.cy <= 0)
                throw new InvalidOperationException("量不出这行文字要占多大（GDI 拒绝了这个字体或这段文字）");
            SelectObject(dc, oldFont);
            return (extent.cx, extent.cy);
        }
        finally
        {
            if (font != IntPtr.Zero) DeleteObject(font);
            DeleteDC(dc);
        }
    }

    /// <summary>
    /// 依次试 微软雅黑 UI → 微软雅黑 → Segoe UI（截图上中英混排是常态，只会一种的就别当默认）。
    /// <para>负 <c>lfHeight</c> ＝按 em 高给，与用户看到的字号最接近；本进程声明了 PerMonitorV2，
    /// 所以这里的数值就是<b>设备像素</b>，与那块 BGRA 缓冲同一个单位。</para>
    /// </summary>
    private static IntPtr CreateFont(int fontHeight)
    {
        foreach (var face in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" })
        {
            var logFont = new LogFont
            {
                Height = -fontHeight,
                Weight = 400,
                CharSet = 1,                 // DEFAULT_CHARSET：中文靠它挑到支持 CJK 的字面
                Quality = 5,                 // CLEARTYPE_QUALITY，与真实桌面一致
                FaceName = face,
            };
            var handle = CreateFontIndirectW(ref logFont);
            if (handle != IntPtr.Zero) return handle;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// 手工写 BITMAPINFOHEADER（自上而下 32 位）。<b>biSize 恒为 40</b>，但缓冲区按"头 + 1KB 余量"分配：
    /// 系统会校验整段长度并可能写那块联合体区（与 <c>GdiScreenCapture</c> 同一课）。
    /// </summary>
    private static IntPtr AllocTopDown32Header(int width, int height)
    {
        const int headerSize = 40;
        var ptr = Marshal.AllocHGlobal(headerSize + 1024);
        for (var i = 0; i < headerSize; i++) Marshal.WriteByte(ptr, i, 0);
        Marshal.WriteInt32(ptr, 0, headerSize);
        Marshal.WriteInt32(ptr, 4, width);
        Marshal.WriteInt32(ptr, 8, -height);        // 负＝自上而下行序，与我们的缓冲一致
        Marshal.WriteInt16(ptr, 12, 1);
        Marshal.WriteInt16(ptr, 14, 32);
        return ptr;
    }

    /// <summary>
    /// BGRA 整数 → GDI 的 <c>COLORREF</c>。<b>两者字节顺序相反</b>：COLORREF 是 <c>0x00BBGGRR</c>
    /// （低位是红），我们整条像素链是 <c>B | G&lt;&lt;8 | R&lt;&lt;16</c>（低位是蓝）。
    /// 直接把整数递过去＝红蓝互换，而"红蓝互换"这种事只有眼睛看得出来，所以这里有单测钉着。
    /// </summary>
    private static int ColorRefOf(int bgra) =>
        (bgra & 0xFF) << 16 | bgra & 0xFF00 | (bgra >>> 16) & 0xFF;

    private const uint DIB_RGB_COLORS = 0;
    private const int TRANSPARENT = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct LogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, CharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextExtent
    {
        public int cx;
        public int cy;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, IntPtr pbmi, uint usage,
        out IntPtr ppvBits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, uint colorRef);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool GdiFlush();

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontIndirectW(ref LogFont logFont);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetTextExtentPoint32W(IntPtr hdc, string lpString, int count, out TextExtent size);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool TextOutW(IntPtr hdc, int x, int y, string lpString, int count);
}
