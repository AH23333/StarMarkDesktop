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
    /// 在 <paramref name="atX"/>/<paramref name="atY"/>（文字框左上角，缓冲内坐标）画一段文字。
    /// <para>字高按<b>物理像素</b>给：这块缓冲就是物理像素，传 DIP 进来等于让调用方去猜 DPI。</para>
    /// <para>
    /// <b>段里可以有几行</b>（编辑框里按 Enter 换的行）。一行的高度按量出来的单行高算，逐行往下排；
    /// 这样"编辑框里看到的换行"与"松手后画出去的成品"是同一件事——反过来（把整段当一行画）
    /// 会让换行只在编辑时存在、落笔就并成一行，那是"我明明分了行"这类反馈的源头。
    /// </para>
    /// <para>
    /// 失败一律抛 <see cref="InvalidOperationException"/> 并带上原因。宁可让用户看到"字没写出去"，
    /// 也不能静默交出一张少了字的图——那种图会被当成"我刚才明明写了字"，事后无从分辨。
    /// </para>
    /// <para>
    /// <paramref name="rotation"/> 是绕<b>字块中心</b>顺时针转的度数。转的时候走另一条路：
    /// 先把字用灰度抗锯齿写进一块黑底字模（拿到的是一张覆盖率图，不是"字 + 背景"），
    /// 再按覆盖率逐像素混合到目标上。<b>覆盖率 0 的像素一个字节都不改</b>——
    /// 要是把那块方方正正的字模整个转过去，字没盖到的地方也会被打磨成背景色，
    /// 用户看到的就是"字转走了，还留下一块比字大得多的脏斑"。
    /// </para>
    /// </summary>
    public static void Draw(byte[] bgra, int width, int height, int atX, int atY,
        string text, int fontHeight, int colorBgra, double rotation = 0d)
    {
        if (bgra is null || width <= 0 || height <= 0 || bgra.Length < (long)width * height * 4)
            throw new ArgumentException("像素缓冲比声明的尺寸短，画上去会越界", nameof(bgra));
        if (string.IsNullOrEmpty(text)) return;
        if (fontHeight < 1) throw new InvalidOperationException("文字高度至少 1 像素");
        var lines = LinesOf(text);
        if (TextGeometry.Normalise(rotation) != 0d)
        {
            DrawRotated(bgra, width, height, atX, atY, lines, fontHeight, colorBgra, rotation);
            return;
        }

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
            var lineHeight = LineHeight(dc);
            var (textWidth, textHeight) = ExtentOf(dc, lines, lineHeight);
            if (textWidth <= 0 || textHeight <= 0)
                throw new InvalidOperationException("量不出这段文字要占多大（GDI 拒绝了这个字体或这段文字）");

            // 只处理与画布相交的那一块：用户可以把字写到选区外面，那部分应当被切掉而不是越界写
            var left = Math.Max(0, atX);
            var top = Math.Max(0, atY);
            var right = Math.Min(width, atX + textWidth);
            var bottom = Math.Min(height, atY + textHeight);
            var tileWidth = right - left;
            var tileHeight = bottom - top;
            if (tileWidth <= 0 || tileHeight <= 0) return;        // 整段都在画布外：没东西可画，也不算错

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
            for (var line = 0; line < lines.Length; line++)
                if (lines[line].Length > 0 &&
                    !TextOutW(dc, left - atX, top - atY + line * lineHeight, lines[line], lines[line].Length))
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
    /// 这段字实际要占多大（物理像素）。<b>选择框与命中测试只能量，不能估</b>：
    /// 按"字高 × 字数"估宽，中文与英文差着两三倍，框会框到一个不存在的位置，
    /// 用户就会看到"我点那行字，选中的却是旁边那条线"。
    /// 与 <see cref="Draw"/> 用同一个字体选择顺序，量出来的才是真正会被画出去的那一块。
    /// <para>多行时宽取<b>最宽那一行</b>、高取<code>行数 × 单行高</code>——与 <see cref="Draw"/> 逐行往下排的
    /// 那套完全同一个式子；两边各算一遍就会出现"框比字矮一行"。</para>
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
            var box = ExtentOf(dc, LinesOf(text), LineHeight(dc));
            SelectObject(dc, oldFont);
            return box;
        }
        finally
        {
            if (font != IntPtr.Zero) DeleteObject(font);
            DeleteDC(dc);
        }
    }

    /// <summary>换行符切行（编辑框给的是 \r\n 与 \n 两种都可能）；空行留着——它要占一行的高。</summary>
    private static string[] LinesOf(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>
    /// 单行高。用一段"没有任何降部/上部首字符"的探针量，而不是拿整段文字去量：
    /// 多行文字里某一行的字距可能被调高，用它当行距会让后面每一行错位累积。
    /// </summary>
    private static int LineHeight(IntPtr dc)
    {
        const string probe = "Hxg";               // 大写 + 小写 x + 带降部的 g：CellHeight 本来就含这两者
        if (!GetTextExtentPoint32W(dc, probe, probe.Length, out var extent) || extent.cy <= 0)
            throw new InvalidOperationException("量不出这个字体的行高（GDI 拒绝了探针字符串）");
        return extent.cy;
    }

    private static (int Width, int Height) ExtentOf(IntPtr dc, string[] lines, int lineHeight)
    {
        var widest = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            if (!GetTextExtentPoint32W(dc, line, line.Length, out var extent) || extent.cx <= 0)
                throw new InvalidOperationException("量不出这段文字要占多大（GDI 拒绝了这个字体或这段文字）");
            widest = Math.Max(widest, extent.cx);
        }
        if (widest <= 0) throw new InvalidOperationException("量不出这段文字要占多大（每一行都是空的）");
        return (widest, lineHeight * lines.Length);
    }

    /// <summary>
    /// 旋转那条路：字模 → 覆盖率图 → 逐像素混合。
    /// <para>与不旋转那条的关键差别是<b>不能"搬背景"</b>：不旋转时把目标那块的原像素先搬进字模、
    /// 在上面写字、再搬回去，天然对齐；一旦转起来，字模是个方块而字是斜的，整个方块搬过去
    /// 就会在字周围留下一片脏斑。所以这里只取"这一像素有多少墨"，再按角度铺到目标上。</para>
    /// </summary>
    private static void DrawRotated(byte[] bgra, int width, int height,
        int atX, int atY, string[] lines, int fontHeight, int colorBgra, double rotation)
    {
        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new InvalidOperationException("创建内存画布失败（GDI 句柄用尽）");
        var font = CreateFont(fontHeight, grayscale: true);
        IntPtr dib = IntPtr.Zero, oldObject = IntPtr.Zero, oldFont = IntPtr.Zero, header = IntPtr.Zero;
        var bits = IntPtr.Zero;
        byte[] coverage;
        int textWidth, textHeight;
        try
        {
            if (font == IntPtr.Zero) throw new InvalidOperationException("系统没能创建标注用的字体（雅黑与 Segoe UI 都试过）");
            oldFont = SelectObject(dc, font);
            var lineHeight = LineHeight(dc);
            (textWidth, textHeight) = ExtentOf(dc, lines, lineHeight);
            var rowBytes = textWidth * 4;

            header = AllocTopDown32Header(textWidth, textHeight);
            dib = CreateDIBSection(dc, header, DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                throw new InvalidOperationException("创建文字画布失败（内存不足）");
            oldObject = SelectObject(dc, dib);

            // 黑底自己清：CreateDIBSection 不承诺位图内存的初值，而"黑＝没墨"是这张覆盖率图的前提
            var zero = new byte[textWidth * 4];
            for (var y = 0; y < textHeight; y++) Marshal.Copy(zero, 0, bits + y * rowBytes, textWidth * 4);

            SetTextColor(dc, 0x00FFFFFF);                // 白＝满墨；COLORREF 是 0x00BBGGRR
            SetBkMode(dc, TRANSPARENT);
            for (var line = 0; line < lines.Length; line++)
                if (lines[line].Length > 0 &&
                    !TextOutW(dc, 0, line * lineHeight, lines[line], lines[line].Length))
                    throw new InvalidOperationException($"文字没能写出去（Win32 {Marshal.GetLastWin32Error()}）");
            GdiFlush();

            coverage = new byte[textWidth * textHeight];
            var row = new byte[textWidth * 4];
            for (var y = 0; y < textHeight; y++)
            {
                Marshal.Copy(bits + y * rowBytes, row, 0, textWidth * 4);
                for (var x = 0; x < textWidth; x++)
                    // 灰度抗锯齿下三个通道相等；取最大是防某个通道被系统的字距调整单独动过
                    coverage[y * textWidth + x] = Math.Max(row[x * 4], Math.Max(row[x * 4 + 1], row[x * 4 + 2]));
            }
        }
        finally
        {
            if (oldObject != IntPtr.Zero) SelectObject(dc, oldObject);
            if (oldFont != IntPtr.Zero) SelectObject(dc, oldFont);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (header != IntPtr.Zero) Marshal.FreeHGlobal(header);
            if (font != IntPtr.Zero) DeleteObject(font);
            DeleteDC(dc);
        }

        var box = TextGeometry.RotatedBox(atX, atY, textWidth, textHeight, rotation);
        var left = Math.Max(0, box.Left);
        var top = Math.Max(0, box.Top);
        var right = Math.Min(width, box.Right);
        var bottom = Math.Min(height, box.Bottom);
        var inkBlue = colorBgra & 0xFF;
        var inkGreen = (colorBgra >>> 8) & 0xFF;
        var inkRed = (colorBgra >>> 16) & 0xFF;
        var inkAlpha = (colorBgra >>> 24) & 0xFF;

        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var (sourceX, sourceY) = TextGeometry.SourceOf(
                    atX, atY, textWidth, textHeight, rotation, x + 0.5, y + 0.5);
                var ink = Sample(coverage, textWidth, textHeight, sourceX - atX, sourceY - atY);
                if (ink == 0) continue;                 // 没墨的像素一个字节都不动：这就是"不糊出脏斑"
                var alpha = ink * inkAlpha / 255;
                if (alpha == 0) continue;
                var at = (y * width + x) * 4;
                bgra[at] = (byte)((bgra[at] * (255 - alpha) + inkBlue * alpha + 127) / 255);
                bgra[at + 1] = (byte)((bgra[at + 1] * (255 - alpha) + inkGreen * alpha + 127) / 255);
                bgra[at + 2] = (byte)((bgra[at + 2] * (255 - alpha) + inkRed * alpha + 127) / 255);
                // 与不旋转那条同一口径：碰过的像素 alpha 必须是 255，否则存出的 PNG 里字是透明洞
                bgra[at + 3] = 255;
            }
        }
    }

    /// <summary>
    /// 双线性取墨量。<paramref name="u"/>/<paramref name="v"/> 按"字模内第 0 个像素的中心是 0.5"计，
    /// 与 <see cref="TextGeometry.SourceOf"/> 同一套坐标；超出字模一律算没墨。
    /// </summary>
    private static int Sample(byte[] coverage, int width, int height, double u, double v)
    {
        var fx = u - 0.5;
        var fy = v - 0.5;
        var x0 = (int)Math.Floor(fx);
        var y0 = (int)Math.Floor(fy);
        var tx = fx - x0;
        var ty = fy - y0;
        var sum = 0d;
        for (var dy = 0; dy <= 1; dy++)
        {
            var sy = y0 + dy;
            if (sy < 0 || sy >= height) continue;                    // 越界的角＝没墨（权重不重新归一，正是"边缘淡出"）
            for (var dx = 0; dx <= 1; dx++)
            {
                var sx = x0 + dx;
                if (sx < 0 || sx >= width) continue;
                var weight = (dx == 0 ? 1 - tx : tx) * (dy == 0 ? 1 - ty : ty);
                sum += coverage[sy * width + sx] * weight;
            }
        }
        return (int)Math.Round(sum);
    }

    /// <summary>
    /// 依次试 微软雅黑 UI → 微软雅黑 → Segoe UI（截图上中英混排是常态，只会一种的就别当默认）。
    /// <para>负 <c>lfHeight</c> ＝按 em 高给，与用户看到的字号最接近；本进程声明了 PerMonitorV2，
    /// 所以这里的数值就是<b>设备像素</b>，与那块 BGRA 缓冲同一个单位。</para>
    /// </summary>
    private static IntPtr CreateFont(int fontHeight) => CreateFont(fontHeight, grayscale: false);

    /// <summary>
    /// <paramref name="grayscale"/> 为真时用 <c>ANTIALIASED_QUALITY</c>（灰度抗锯齿）而不是 ClearType：
    /// 旋转那条路要把字模当<b>覆盖率图</b>来采样，而 ClearType 是亚像素渲染——同一个笔画在
    /// R/G/B 三个通道上给的是三个不同的值，拿它当 alpha 会转出一排彩边。
    /// </summary>
    private static IntPtr CreateFont(int fontHeight, bool grayscale)
    {
        foreach (var face in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" })
        {
            var logFont = new LogFont
            {
                Height = -fontHeight,
                Weight = 400,
                CharSet = 1,                 // DEFAULT_CHARSET：中文靠它挑到支持 CJK 的字面
                Quality = (byte)(grayscale ? 4 : 5),   // 4＝ANTIALIASED（灰度），5＝CLEARTYPE，与真实桌面一致
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
