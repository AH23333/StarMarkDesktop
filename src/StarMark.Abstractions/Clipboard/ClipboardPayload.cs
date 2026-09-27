#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace StarMark.Abstractions.Clipboard;

/// <summary>
/// 剪贴板原始负载（<c>CF_UNICODETEXT</c> / <c>CF_TEXT</c> / <c>CF_HDROP</c> / <c>CF_DIB(V5)</c> / <c>CF_PNG</c>）
/// 的字节解码。
/// <para>
/// 这里只放"字节 → 字符串 / 字节 → BGRA"的纯函数，不放任何 Win32 调用。同一份解码既要服务
/// 系统剪贴板的实时采集（<c>GetClipboardData</c> 拷出来的字节），也要服务 Ditto 库里
/// <c>Data.ooData</c> 的 BLOB —— 之前只有 Ditto 侧一份私有实现，抽出来共用，
/// 免得两条路径对"UTF-16 尾零怎么剥"各写一遍、各错一遍。
/// </para>
/// <para>
/// <b>图片为什么也放这一层</b>：DIB 头部的每个字段都有一次写错的机会（行序看 <c>biHeight</c> 的符号、
/// 通道顺序看 <c>biCompression</c> 与位掩码、调色板张数看 <c>biClrUsed</c> 是否为 0、行跨距要 4 字节对齐），
/// 而这些错的代价是"屏幕上一张颜色不对的图"——只有把它做成能被字节级断言钉住的纯函数，才谈得上验过。
/// </para>
/// <para>
/// <b>PNG 的像素解码不在这份文件里</b>：它另起一节 <c>ClipboardPayload.Png.cs</c>（同一个类的 partial 分段），
/// 因为那是"自研 inflate + defilter"一整件事，与这里的"按字节搬移"是两种复杂度。
/// </para>
/// </summary>
public static partial class ClipboardPayload
{
    /// <summary>文本格式：宽字符（Windows 上最常见的 CF_UNICODETEXT）。</summary>
    public const string FormatUnicodeText = "CF_UNICODETEXT";

    /// <summary>文本格式：单字节（旧程序写入的 CF_TEXT）。</summary>
    public const string FormatAnsiText = "CF_TEXT";

    /// <summary>文件列表格式。</summary>
    public const string FormatHDrop = "CF_HDROP";

    // ===== 图片格式名（采集优先级见 ClipboardNative）=====

    /// <summary>位图（设备无关）：<c>BITMAPV5HEADER</c> + 像素，带位掩码，最完整的一种。</summary>
    public const string FormatDibV5 = "CF_DIBV5";

    /// <summary>位图：<c>BITMAPINFOHEADER</c> + 像素。绝大多数应用复制图片给的就是这一份。</summary>
    public const string FormatDib = "CF_DIB";

    /// <summary>PNG 字节流（部分应用与我们的写回会给出）。校验魔数后才按字节存。</summary>
    public const string FormatPng = "CF_PNG";

    /// <summary>
    /// 把文本类剪贴板负载解成字符串。
    /// <paramref name="ansi"/> 为真按单字节（UTF-8 容错读法）处理，否则按 UTF-16LE。
    /// </summary>
    public static string DecodeText(byte[]? data, bool ansi)
    {
        if (data is null || data.Length == 0) return string.Empty;
        try
        {
            if (ansi)
            {
                // ANSI/UTF-8：NUL 终止符是单字节，按字节剥离安全。
                var end = data.Length;
                while (end > 0 && data[end - 1] == 0) end--;
                return Encoding.UTF8.GetString(data, 0, end);
            }

            // UTF-16LE：NUL 终止符是「码元」= 2 字节 0x0000，必须按整个码元剥离。
            // 逐「字节」剥尾零再 `end & ~1` 会把以 ASCII 结尾的文本（'A' = 0x41,0x00）的合法高位
            // 字节 0x00 误当终止符吃掉，再 &~1 丢弃配对的 0x41 → 末字符被静默截断。
            // 先对齐到偶数字节，再仅剥离完整的 0x0000 对，杜绝奇数剥离与错切。
            var len = data.Length & ~1;
            while (len >= 2 && data[len - 2] == 0 && data[len - 1] == 0) len -= 2;
            return Encoding.Unicode.GetString(data, 0, len);
        }
        catch
        {
            return string.Empty;   // 调用方（后台采集 / 只读第三方库）都按"拿不到就当没有"处理
        }
    }

    /// <summary>
    /// 解析 <c>CF_HDROP</c> 的 DROPFILES 结构：头部（pFiles 指向列表起点）+ 双 NUL 结束的路径列表。
    /// <para>刻意不假设头部固定 20 字节：不同发送方给的 <c>pFiles</c> 不一样，越界一律按数据末尾夹住
    /// （<c>(int)pFiles</c> 直接强转一个 uint 会得到负数偏移，故必须先判上界）。</para>
    /// </summary>
    public static List<string> ParseDropFiles(byte[]? data)
    {
        var files = new List<string>();
        if (data is null || data.Length < 20) return files;

        var pFiles = BitConverter.ToUInt32(data, 0);
        var wide = BitConverter.ToUInt32(data, 16) != 0;
        var offset = (int)Math.Min(pFiles, (uint)data.Length);

        if (wide)
        {
            var sb = new StringBuilder();
            var i = offset;
            while (i + 1 < data.Length)
            {
                var ch = (char)BitConverter.ToUInt16(data, i);
                i += 2;
                if (ch == 0)
                {
                    if (sb.Length > 0)
                    {
                        files.Add(sb.ToString());
                        sb.Clear();
                    }
                    // 连续两个 NUL 表示列表结束
                    if (i + 1 < data.Length && BitConverter.ToUInt16(data, i) == 0) break;
                    continue;
                }
                sb.Append(ch);
            }
        }
        else
        {
            var sb = new StringBuilder();
            var i = offset;
            while (i < data.Length)
            {
                var ch = (char)data[i++];
                if (ch == 0)
                {
                    if (sb.Length > 0)
                    {
                        files.Add(sb.ToString());
                        sb.Clear();
                    }
                    if (i < data.Length && data[i] == 0) break;
                    continue;
                }
                sb.Append(ch);
            }
        }
        return files;
    }

    // ==================== 图片：CF_DIB / CF_DIBV5 / CF_PNG ====================

    /// <summary>DIB 与 PNG 都允许的单一最大边长：再大的数不是"一张截图"，而是把长度校验撑坏的畸形头。</summary>
    public const int MaxImageDimension = 16384;

    private const int InfoHeaderSize = 40;      // BITMAPINFOHEADER（V4=108 / V5=124 都从这一份字段起手）
    private const int BiRgb = 0;
    private const int BiBitFields = 3;

    /// <summary>
    /// 一帧解到底的图像：<b>每像素 4 字节 BGRA、行序自上而下、每行宽 = width*4（无填充）</b>。
    /// <para>刻意不给"原格式什么样就怎么交出去"的选项：往下 PNG 编码、缩略图、写回、未来的贴图
    /// 要的都是这一种布局。把它定成唯一的中间形状，同一帧才只可能错一处。</para>
    /// </summary>
    public readonly record struct ImageFrame(int Width, int Height, byte[] Bgra);

    /// <summary>PNG 魔数（8 字节）。<b>校验它才允许"按字节存 PNG"</b>：不看魔数，一份改了文件头的 BMP 会被当 PNG 存进库，症状是"这张历史图永远打不开"。</summary>
    public static bool IsPng(byte[]? data)
        => data is { Length: >= 8 }
           && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
           && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

    /// <summary>
    /// 只读 PNG 的 <c>IHDR</c> 宽高（<b>不解码像素</b>）。标题要说"1920×1080"、门禁要按像素数估体积，
    /// 两个都只要这两个数；为它付一次整图解码不值。
    /// </summary>
    public static bool TryReadPngSize(byte[]? data, out int width, out int height)
    {
        width = height = 0;
        // 签名(8) + 块长度(4) + 块类型(4) + 宽(4) + 高(4) ⇒ 28 字节才够读到高。
        if (!IsPng(data) || data!.Length < 28) return false;
        if (Be32(data, 8) != 13) return false;                        // IHDR 的数据段固定 13 字节
        if (data[12] != 'I' || data[13] != 'H' || data[14] != 'D' || data[15] != 'R') return false;
        var w = (int)Be32(data, 16);
        var h = (int)Be32(data, 20);
        if (w <= 0 || h <= 0 || w > MaxImageDimension || h > MaxImageDimension) return false;
        width = w; height = h;
        return true;
    }

    /// <summary>
    /// <c>CF_DIB</c> / <c>CF_DIBV5</c> 的字节 → <see cref="ImageFrame"/>；失败时 <paramref name="reason"/>
    /// 是给状态行与日志用的原话（"拒收必须出声"沿用 F10 那条口径）。
    /// <para>
    /// 头部每个字段的读法都有"错了不崩但画面错"的余地，逐条列在这里：
    /// ① <c>biHeight</c> 的<b>符号</b>才是行序（负＝自上而下，截屏类给的多半是负的；取绝对值之后必须记住原始符号）；
    /// ② 行跨距按 32 位对齐（<c>(bitCount*width+31)&amp;~31</c> 再除 8），不是 <c>width*bitCount/8</c>；
    /// ③ 调色板张数：只有 <c>biClrUsed==0</c> 时才按 <c>1&lt;&lt;biBitCount</c>；
    /// ④ <c>BI_BITFIELDS</c> 的三张位掩码<b>放哪儿</b>取决于头长度——40 字节头时它们占在"调色板位置"
    ///    （像素起点要再往后让 12 字节），V4(108)/V5(124) 头时在头内偏移 40/44/48（像素起点不动）。
    ///    这一条是 CF_DIBV5 最经典的坑。
    /// </para>
    /// <para><b>alpha 一律丢掉</b>（§2 裁决）：剪贴板里的图没有"半透明"语义，留着它只会让
    /// PNG 编码与写回两条路对同一帧给出不同结果。</para>
    /// </summary>
    public static bool TryDecodeDib(byte[]? data, out ImageFrame image, out string? reason)
    {
        image = default;
        reason = null;
        if (data is null || data.Length < InfoHeaderSize) { reason = "字节不够一个位图头"; return false; }

        const int coreHeader = 12;          // BITMAPCOREHEADER：16 位时代的头（宽高各 16 位有符号、调色板每项 3 字节）
        var header = (int)U32(data, 0);
        if (header == coreHeader) { reason = "16 位时代的 BITMAPCOREHEADER（不支持）"; return false; }
        if (header < InfoHeaderSize) { reason = $"位图头长度异常（{header}）"; return false; }

        var width = (int)U32(data, 4);
        var rawHeight = (int)U32(data, 8);              // 符号就是行序，先留着再取绝对值
        var bitCount = (int)U16(data, 14);
        var compression = (int)U32(data, 16);
        var clrUsed = (int)U32(data, 32);

        if (width <= 0 || rawHeight == 0) { reason = $"尺寸不合法（{width}×{rawHeight}）"; return false; }
        var topDown = rawHeight < 0;
        var height = Math.Abs(rawHeight);
        if (width > MaxImageDimension || height > MaxImageDimension)
        {
            reason = $"尺寸超出上限（{width}×{height}）";
            return false;
        }
        if (compression is not (BiRgb or BiBitFields))
        {
            // BI_RLE8/BI_RLE4 是老式压缩：真实截图链上见不到，为它写一遍解码器只会多一处可写错的地方。
            reason = $"不支持的压缩方式（{compression}）";
            return false;
        }
        if (bitCount is not (1 or 4 or 8 or 16 or 24 or 32)) { reason = $"不支持的位深（{bitCount}）"; return false; }

        var masksFromHeader = header >= 108;            // BITMAPV4HEADER 起，掩码在头内（偏移 48/52/56）
        var maxPalette = bitCount <= 8 ? 1 << bitCount : 0;
        // 只有 biClrUsed==0 才按位深推张数；给了值就照它（越界视为畸形，按位深推更安全）。
        var paletteEntries = maxPalette;
        if (maxPalette > 0 && clrUsed > 0 && clrUsed <= maxPalette) paletteEntries = clrUsed;
        var stride = (((bitCount * width) + 31) & ~31) / 8;
        var pixelOffset = header + paletteEntries * 4;

        uint redMask, greenMask, blueMask;
        if (compression == BiBitFields)
        {
            var at = masksFromHeader ? 40 : header;      // V4/V5：头内 40/44/48；40 字节头：占在"调色板位置"
            if (data.Length < at + 12) { reason = "位掩码越界"; return false; }
            redMask = U32(data, at); greenMask = U32(data, at + 4); blueMask = U32(data, at + 8);
            // 只有"掩码挤在调色板位置"那种（40 字节头）才把像素起点往后推 12 字节；
            // V4/V5 的掩码本来就在头里，再推一次就是把头当成像素读，整帧尺寸看着对、内容全是垃圾。
            if (!masksFromHeader) pixelOffset += 12;
        }
        else
        {
            // BI_RGB 的默认布局：32/24 位就是 B,G,R(,[A])；16 位规定为 X1R5G5B5。
            (redMask, greenMask, blueMask) = bitCount switch
            {
                16 => (0x7C00u, 0x03E0u, 0x001Fu),
                32 => (0x00FF0000u, 0x0000FF00u, 0x000000FFu),
                _ => (0x00FF0000u, 0x0000FF00u, 0x000000FFu),
            };
        }
        if (bitCount is 16 or 24 or 32 && (redMask | greenMask | blueMask) == 0)
        { reason = "位掩码全零（读不出通道）"; return false; }

        var need = (long)pixelOffset + (long)stride * height;
        if (data.Length < need) { reason = $"像素数据不完整（需 {need}，实得 {data.Length}）"; return false; }

        var bgra = new byte[width * height * 4];
        var badIndex = false;
        for (var y = 0; y < height && !badIndex; y++)
        {
            var src = pixelOffset + y * stride;
            // 自上而下：源第 y 行就是目标第 y 行；自下而上：源第 0 行是画面的最下面一行。
            var dst = (topDown ? y : height - 1 - y) * width * 4;
            for (var x = 0; x < width; x++)
            {
                var o = dst + x * 4;
                switch (bitCount)
                {
                    case 32:
                        Unpack(U32(data, src + x * 4), redMask, greenMask, blueMask, bgra, o);
                        break;
                    case 24:
                        bgra[o] = data[src + x * 3];
                        bgra[o + 1] = data[src + x * 3 + 1];
                        bgra[o + 2] = data[src + x * 3 + 2];
                        break;
                    case 16:
                        Unpack((uint)U16(data, src + x * 2), redMask, greenMask, blueMask, bgra, o);
                        break;
                    default:                              // 8/4/1 位：都是"调色板索引"，只是每像素占几位
                        var bit = x * bitCount;
                        // 高位在前（DIB 的位序），且<b>8 位时位移必须是 0</b>：写成 `7-(bit&7)` 会把整字节
                        // 右移 7 位 ⇒ 每个像素都读到调色板第 0 项，症状是"图全是黑的但没报错"。
                        var shift = 8 - bitCount - (bit & 7);
                        var index = (data[src + (bit >> 3)] >> shift) & ((1 << bitCount) - 1);
                        // 索引超出<b>声明</b>的张数就不是颜色数据（手改过的头能把读指针带到缓冲区外）：整帧拒收。
                        if (index >= paletteEntries) { badIndex = true; break; }
                        var entry = header + index * 4;              // RGBQUAD：B,G,R,保留
                        bgra[o] = data[entry];
                        bgra[o + 1] = data[entry + 1];
                        bgra[o + 2] = data[entry + 2];
                        break;
                }
                bgra[o + 3] = 255;                                  // alpha 丢弃＝一律不透明
            }
        }
        if (badIndex) { reason = "调色板索引越界"; return false; }

        image = new ImageFrame(width, height, bgra);
        return true;
    }

    /// <summary>按位掩码取出一通道并摊到 8 位（5/6 位用四舍五入展开，避免"整幅偏暗一档"）。</summary>
    private static void Unpack(uint value, uint red, uint green, uint blue, byte[] bgra, int o)
    {
        // 位掩码五花八门（BGR 顺序、5-6-5、5-5-5），但"从右数第几位起、占几位"是唯一需要的信息。
        bgra[o] = Channel(value, blue);
        bgra[o + 1] = Channel(value, green);
        bgra[o + 2] = Channel(value, red);
    }

    private static byte Channel(uint value, uint mask)
    {
        if (mask == 0) return 0;
        var shift = 0;
        while (((mask >> shift) & 1) == 0) shift++;
        var bits = 0;
        for (var m = mask >> shift; m != 0; m >>= 1) bits++;
        var raw = (value >> shift) & ((1u << bits) - 1);
        return bits switch
        {
            >= 8 => (byte)(raw & 0xFF),
            7 => (byte)((raw << 1) | (raw >> 5)),
            6 => (byte)((raw * 255 + 31) / 63),
            5 => (byte)((raw * 255 + 15) / 31),
            4 => (byte)((raw * 255 + 7) / 15),
            1 => (byte)(raw != 0 ? 255 : 0),
            _ => (byte)(raw << (8 - bits)),
        };
    }

    private static uint U32(byte[] b, int i) => (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24);

    private static uint Be32(byte[] b, int i) => (uint)(b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3]);

    private static int U16(byte[] b, int i) => b[i] | b[i + 1] << 8;
}
