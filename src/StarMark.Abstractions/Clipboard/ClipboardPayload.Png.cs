#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace StarMark.Abstractions.Clipboard;

/// <summary>
/// <see cref="ClipboardPayload"/> 的 PNG 分段：<b>PNG → 归一 BGRA 的纯托管解码</b>。
/// <para>
/// 为什么自研（§2「解码」的终态裁决）：<b>存储只有 PNG 一种格式，于是"历史图片→贴到桌面"这条能力的
/// 地基必须不依赖编码器</b>。原先那份 WinRT 解码（<c>BitmapDecoder</c>）已在对照测试里露出两处底细
/// （实测，见 <c>ClipboardPngCodecTests</c>）：<b>16 位的 PNG 它交不出"每像素 3 或 4 字节"的像素块
/// ⇒ 整帧被拒</b>（那样的图根本进不了历史），而<b>带 alpha 的帧它交出的像素与"alpha 一律 255"
/// 这个归一口径不同</b>。采集与读档因此共用这一份：<b>两处各一份解码，最坏的就是对同一帧算出两个哈希</b>
/// ——那等于用户每点一次"再复制"，历史多一条自回声。
/// </para>
/// <para>
/// <b>形状与 <see cref="TryDecodeDib"/> 完全一致</b>：每像素 4 字节 BGRA、自上而下、行内无填充、
/// <b>alpha 一律 255</b>（§2「alpha 丢弃并写明」）。最后这条决定了 <c>tRNS</c> 在这里是死信息——
/// 整块跳过，不解析，也不许"顺手支持一下颜色键透明"：采集与读档若对同一帧给出不同 alpha，
/// 代价是每次重复制都多一条历史，而那正是 §3-Q4 点名不许发生的。
/// </para>
/// <para>
/// <b>不校验块 CRC 与 zlib 校验和</b>：这两样防的是"传输途中坏了"，而我们防的是"用户改了备份里的字节"——
/// 后者由调用方的尺寸/长度判据兜住（长度不符一律拒），前者不是这一层的职责。
/// </para>
/// </summary>
public static partial class ClipboardPayload
{
    /// <summary>
    /// 一帧解出来的像素字节数上限。<b>这条必须有，而且与 <see cref="MaxImageDimension"/> 不重复</b>：
    /// 合法尺寸（两边都 ≤16384）配上几十 KB 的 IDAT 就能撑出 GB 级的缓冲——那是解压炸弹，
    /// 而 DIB 那一路没有这个风险（输入字节本身就那么大）。256 MB 覆盖 8K 截屏（≈134 MB）还有余量，
    /// 再大的"历史图片"不是截图，拒收并给原话。
    /// </summary>
    public const long MaxDecodedBytes = 256L * 1024 * 1024;

    /// <summary>
    /// PNG 字节 → <see cref="ImageFrame"/>。失败时 <paramref name="reason"/> 是给状态行与日志的原话
    /// （"拒收必须出声"沿用 F10 那条口径），且<b>每一种"看着能解其实错位"的形状都在这里被挡</b>：
    /// 隔行（Adam7）、非 8/16 位深、索引图没有调色板、调色板索引越界、IDAT 解出来长度不符
    /// （行数/行跨距算错时最常见的症状）。
    /// </summary>
    public static bool TryDecodePng(byte[]? data, out ImageFrame image, out string? reason)
    {
        image = default;
        reason = null;
        if (!IsPng(data)) { reason = "不是 PNG（魔数不对）"; return false; }
        var bytes = data!;

        var idatParts = new List<byte[]>();
        var idatBytes = 0L;
        byte[]? palette = null;
        int width = 0, height = 0, depth = 0, colorType = 0;
        var seenIhdr = false;

        var pos = 8;
        while (true)
        {
            // 一个块至少 12 字节（长度 4 + 类型 4 + 数据 0 + CRC 4）：连这个都不够就是文件被截断。
            if (pos + 12 > bytes.Length) { reason = "块不完整（文件被截断？）"; return false; }
            var len = (long)Be32(bytes, pos);
            if (len > bytes.Length - pos - 12) { reason = "块的长度字段与实际字节不符"; return false; }
            // 块布局＝[长度 4][类型 4][数据 len][CRC 4]：类型在 pos+4、数据在 pos+8，
            // 两者混用会把"IHDR"当成数据比、永远比不中（第一版就这么错过了整份 IHDR）。
            var ty = pos + 4;
            var at = pos + 8;

            if (!seenIhdr)
            {
                // IHDR 必须是第一块：没有它，"每行几字节、每像素几通道"全都没有依据。
                if (len != 13 || !Four(bytes, ty, 'I', 'H', 'D', 'R')) { reason = "缺 IHDR 或它不在最前面"; return false; }
                width = (int)Be32(bytes, at);
                height = (int)Be32(bytes, at + 4);
                depth = bytes[at + 8];
                colorType = bytes[at + 9];
                if (bytes[at + 10] != 0 || bytes[at + 11] != 0) { reason = "不支持的压缩/滤波方法"; return false; }
                if (bytes[at + 12] != 0) { reason = "隔行（Adam7）PNG 不解"; return false; }
                seenIhdr = true;
            }
            else if (Four(bytes, ty, 'P', 'L', 'T', 'E')) palette = Slice(bytes, at, (int)len);
            else if (Four(bytes, ty, 'I', 'D', 'A', 'T'))
            {
                if (len == 0) { reason = "空的 IDAT"; return false; }
                idatBytes += len;
                idatParts.Add(Slice(bytes, at, (int)len));
            }
            else if (Four(bytes, ty, 'I', 'E', 'N', 'D')) break;
            // 其余块（gAMA / pHYs / sRGB / tEXt / tRNS …）与像素无关：整块跳过。
            pos = at + (int)len + 4;
        }

        if (width <= 0 || height <= 0 || width > MaxImageDimension || height > MaxImageDimension)
        { reason = $"尺寸不合法（{width}×{height}）"; return false; }
        var channels = colorType switch
        {
            0 => 1,     // 灰度
            2 => 3,     // 真彩色
            3 => 1,     // 索引
            4 => 2,     // 灰度 + alpha
            6 => 4,     // 真彩色 + alpha
            _ => 0,
        };
        if (channels == 0) { reason = $"不支持的颜色类型（{colorType}）"; return false; }
        // 1/2/4 位深只存在于索引/灰度小图：从剪贴板进来的那一路系统早已把它展成 DIB，
        // CF_PNG 单发的小图 v1 明确不接——为它写一遍"半个字节"的拆装，多的是可以写错的地方。
        if (depth is not (8 or 16)) { reason = $"不支持的位深（{depth}）"; return false; }
        var paletteEntries = palette?.Length / 3 ?? 0;             // PLTE 每项 3 字节；不足一项的按项数截断
        if (colorType == 3 && paletteEntries == 0) { reason = "索引图没有调色板"; return false; }

        var sampleBytes = depth / 8;                              // 每样本几字节（8 位＝1，16 位＝2）
        var rowBytes = width * channels * sampleBytes;            // PNG 行内没有 4 字节填充（与 DIB 不同！）
        var rawLen = (long)height * (rowBytes + 1);               // 每行前面还有一字节滤波器号
        if (rawLen > MaxDecodedBytes) { reason = $"这一帧过大（{width}×{height}），不解"; return false; }
        if (idatParts.Count == 0) { reason = "没有像素数据（IDAT）"; return false; }
        if (idatBytes > MaxDecodedBytes) { reason = "像素流过大（压缩态就超了上限）"; return false; }

        var packed = new byte[(int)idatBytes];
        var copy = 0;
        foreach (var part in idatParts) { Buffer.BlockCopy(part, 0, packed, copy, part.Length); copy += part.Length; }

        var raw = new byte[(int)rawLen];
        if (!Inflate(packed, raw, out var detail)) { reason = detail; return false; }

        var bgra = new byte[width * height * 4];
        var line = new byte[rowBytes];
        var prev = new byte[rowBytes];
        // 滤波器按"整像素"回看左邻，不足 1 字节的一律按 1 算（规范原话）。
        var bpp = Math.Max(1, channels * sampleBytes);
        // 通道在样本里的位置：PNG 交出来是 R,G,B(,A)，我们要的是 B,G,R —— 换的是读取下标，
        // 不是再搬一遍内存。（灰度/灰度+alpha 三个下标同为 0：一份灰度填三色。）
        var (offR, offG, offB) = channels >= 3 ? (0, 1, 2) : (0, 0, 0);

        byte Sample(byte[] buf, int sampleIndex) => sampleBytes == 1 ? buf[sampleIndex] : buf[sampleIndex * 2];

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * (rowBytes + 1);
            var filter = raw[rowStart];
            Buffer.BlockCopy(raw, rowStart + 1, line, 0, rowBytes);
            switch (filter)
            {
                case 0: break;                                       // None
                case 1: for (var i = bpp; i < rowBytes; i++) line[i] = (byte)(line[i] + line[i - bpp]); break;
                case 2: for (var i = 0; i < rowBytes; i++) line[i] = (byte)(line[i] + prev[i]); break;
                case 3:
                    for (var i = 0; i < rowBytes; i++)
                        line[i] = (byte)(line[i] + ((i >= bpp ? line[i - bpp] : 0) + prev[i]) / 2);
                    break;
                case 4:
                    for (var i = 0; i < rowBytes; i++)
                        line[i] = (byte)(line[i] + Paeth(
                            i >= bpp ? line[i - bpp] : 0, prev[i], y > 0 && i >= bpp ? prev[i - bpp] : 0));
                    break;
                default: reason = $"未知的行滤波器（{filter}）"; return false;
            }

            for (var x = 0; x < width; x++)
            {
                byte b, g, r;
                if (colorType == 3)
                {
                    // 越界索引不能"当作黑"糊过去：那张图上会有一行颜色来路不明，而条目看着完全正常。
                    var p = Sample(line, x);
                    if (p >= paletteEntries) { reason = "调色板索引越界"; return false; }
                    // PLTE 每项是 R,G,B 三个字节——与像素型的通道顺序同一头，别在这里手滑反成 BGR。
                    r = palette![p * 3]; g = palette[p * 3 + 1]; b = palette[p * 3 + 2];
                }
                else
                {
                    var at = x * channels;
                    r = Sample(line, at + offR); g = Sample(line, at + offG); b = Sample(line, at + offB);
                }
                var o = (y * width + x) * 4;
                bgra[o] = b; bgra[o + 1] = g; bgra[o + 2] = r;
                bgra[o + 3] = 255;                                  // alpha 一律丢掉（见类注释）
            }

            (prev, line) = (line, prev);                             // 上一行换过来；line 下一轮整行覆盖
        }

        image = new ImageFrame(width, height, bgra);
        reason = null;
        return true;
    }

    /// <summary>
    /// Paeth 预测子：取 <b>a/b/c 之中最接近 p = a + b − c 的那一个</b>。
    /// <para>三个距离必须按 <c>|p−a| / |p−b| / |p−c|</c> 各自对号——写成 <c>|p−b| / |p−c| / |p−a|</c>
    /// 那种"整体转一格"的顺手写法编译得过、也过得了"测试与实现共用同一个 helper"的往返测，
    /// 但解出来的每一行从第二个像素起就串到别的通道上（GDI+ 编码的真实 PNG 一把就抓出来了：
    /// (a,b,c)＝(232,29,0) 时正确答案是 a，转过的写法给出 c）。</para>
    /// </summary>
    private static byte Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
    }

    /// <summary>
    /// IDAT（zlib 流）→ <paramref name="into"/>，<b>长度必须正好</b>：
    /// 少了是截断/行跨距算错，多了是塞了第二帧。两种都拒，绝不"截一段能用"地画半张图。
    /// </summary>
    private static bool Inflate(byte[] packed, byte[] into, out string? detail)
    {
        detail = null;
        var tail = new byte[1];
        try
        {
            using var ms = new MemoryStream(packed, writable: false);
            using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
            var total = 0;
            while (total < into.Length)
            {
                var n = zlib.Read(into, total, into.Length - total);
                if (n <= 0) break;                                  // 流提前结束：交给下面的长度判据
                total += n;
            }
            if (total != into.Length) { detail = $"像素长度不符（解出 {total}，应有 {into.Length}）"; return false; }
            if (zlib.Read(tail, 0, 1) > 0) { detail = "像素数据比声明的尺寸更长"; return false; }
            return true;
        }
        catch (Exception ex)
        {
            detail = $"像素流解不开（{ex.GetType().Name}）";
            return false;
        }
    }

    private static bool Four(byte[] b, int i, char a, char c, char d, char e)
        => b[i] == a && b[i + 1] == c && b[i + 2] == d && b[i + 3] == e;

    private static byte[] Slice(byte[] b, int from, int len)
    {
        var copy = new byte[len];
        Buffer.BlockCopy(b, from, copy, 0, len);
        return copy;
    }
}
