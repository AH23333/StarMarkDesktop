#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Clipboard;
using StarMark.Integrations.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// ClipIMG-2b：<b>PNG → BGRA 的纯托管解码</b>（§2「解码」那条终态：inflate + defilter 自研、不引 NuGet）。
/// <para>
/// 验收口径刻意做成两头夹：每一条都<b>同时</b>问"托管解出来是不是那批已知像素"与"WinRT 那份是不是
/// 同一批"。只断言两者相等不够——测试里那个 PNG 封装器若写坏了，两份实现会一起解出同一堆垃圾并
/// 心满意足地相等。与已知像素比才叫字节级。
/// </para>
/// <para>
/// 封装器为什么要在测试里现写：要造的正是产品编码器造不出的形状（16 位、索引图、灰度+alpha、
/// 五种行滤波器、多块 IDAT）。这些不是假想敌，而是"别人拷进剪贴板的一张 PNG"本来就长这样。
/// </para>
/// </summary>
public sealed class ClipboardPngCodecTests
{
    // ────────── 测试侧最小 PNG 封装器（造形状用，不是产品实现） ──────────

    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static byte[] Chunk(string type, byte[] payload)
    {
        var body = new byte[type.Length + payload.Length];
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(body, 0);
        payload.CopyTo(body, type.Length);
        var crc = Crc32(body);
        return [
            (byte)(payload.Length >> 24), (byte)(payload.Length >> 16), (byte)(payload.Length >> 8), (byte)payload.Length,
            .. body,
            (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc,
        ];
    }

    /// <summary>块 CRC（WIC 会验它，封装器算错就等于对照测试全体失效）。</summary>
    private static uint Crc32(byte[] b)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var x in b)
        {
            crc ^= (uint)x << 24;
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7u : crc << 1;
        }
        return crc ^ 0xFFFFFFFF;
    }

    private static byte[] IHdr(int w, int h, int depth, int colorType, int interlace = 0)
    {
        var d = new byte[13];
        d[0] = (byte)(w >> 24); d[1] = (byte)(w >> 16); d[2] = (byte)(w >> 8); d[3] = (byte)w;
        d[4] = (byte)(h >> 24); d[5] = (byte)(h >> 16); d[6] = (byte)(h >> 8); d[7] = (byte)h;
        d[8] = (byte)depth; d[9] = (byte)colorType; d[12] = (byte)interlace;
        return d;
    }

    private static byte[] Deflate(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw);
        return ms.ToArray();
    }

    private static int Channels(int colorType) => colorType switch { 0 or 3 => 1, 4 => 2, 2 => 3, 6 => 4, _ => 0 };

    /// <summary>加滤镜（编码方向）。行滤波器号按 <paramref name="perRowFilter"/> 逐行轮转，一个数组就能混用五种。</summary>
    private static byte[] Filtered(int w, int h, int channels, int sampleBytes, byte[] rows, int[] perRowFilter)
    {
        var rowBytes = w * channels * sampleBytes;
        var bpp = Math.Max(1, channels * sampleBytes);
        var raw = new byte[h * (rowBytes + 1)];
        var cur = new byte[rowBytes];
        var prev = new byte[rowBytes];
        for (var y = 0; y < h; y++)
        {
            Array.Copy(rows, y * rowBytes, cur, 0, rowBytes);
            var f = perRowFilter[y % perRowFilter.Length];
            for (var i = 0; i < rowBytes; i++)
            {
                var left = i >= bpp ? cur[i - bpp] : (byte)0;
                var upLeft = y > 0 && i >= bpp ? prev[i - bpp] : (byte)0;
                var pred = f switch
                {
                    1 => left,
                    2 => prev[i],
                    3 => (byte)((left + prev[i]) / 2),
                    4 => Paeth(left, prev[i], upLeft),
                    _ => (byte)0,
                };
                raw[y * (rowBytes + 1) + 1 + i] = (byte)(cur[i] - pred);
            }
            raw[y * (rowBytes + 1)] = (byte)f;
            Array.Copy(cur, prev, rowBytes);
        }
        return raw;
    }

    private static byte Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a); var pb = Math.Abs(p - b); var pc = Math.Abs(p - c);
        return (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
    }

    private static byte[] Png(int w, int h, int depth, int colorType, byte[] rows, byte[]? plte = null,
        int[]? filters = null, int? idatSplit = null)
        => PngRaw(w, h, depth, colorType,
            Filtered(w, h, Channels(colorType), depth / 8, rows, filters ?? new[] { 0 }), plte, idatSplit);

    /// <summary>拼一份完整 PNG。多块 IDAT 是真实形式（编码器按缓冲切块），不是怪癖。</summary>
    private static byte[] PngRaw(int w, int h, int depth, int colorType, byte[] rawIdat, byte[]? plte, int? idatSplit)
    {
        var outBytes = new List<byte>();
        outBytes.AddRange(Signature);
        outBytes.AddRange(Chunk("IHDR", IHdr(w, h, depth, colorType)));
        if (plte is not null) outBytes.AddRange(Chunk("PLTE", plte));
        // 切块切的是<b>压缩后</b>的字节流（真实编码器就是这么切的：整条 zlib 流跨多块 IDAT）。
        var deflated = Deflate(rawIdat);
        if (idatSplit is int cut && cut > 0 && cut < deflated.Length)
        {
            outBytes.AddRange(Chunk("IDAT", deflated[..cut]));
            outBytes.AddRange(Chunk("IDAT", deflated[cut..]));
        }
        else outBytes.AddRange(Chunk("IDAT", deflated));
        outBytes.AddRange(Chunk("IEND", Array.Empty<byte>()));
        return outBytes.ToArray();
    }

    /// <summary>
    /// 图案只有一个来源：像素值由这里给，样本行与期望 BGRA 都从它推。
    /// <b>四个通道都必须同时随 x 与 y 变</b>——原先 G 只随 y、A 只按 x 奇偶，那种图案会让"整行错位"
    /// 一类的 bug 装成正确（同一行里那几个字节本来就是同一个数）。
    /// </summary>
    private static (byte R, byte G, byte B, byte A) Pixel(int x, int y)
        => ((byte)(x * 31 + y * 5), (byte)(255 - y * 17 - x * 3), (byte)(x * 7 + y * 13),
            (byte)((x + 2 * y) % 2 == 0 ? (byte)255 : (byte)90));

    private static byte[] Samples(int w, int h, int depth, int colorType)
    {
        var sb = depth / 8;
        var ch = Channels(colorType);
        var buf = new byte[w * h * ch * sb];
        var i = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (r, g, b, a) = Pixel(x, y);
                // 16 位样本是大端：高字节在前、低字节留 0 ⇒ 取高字节就是那个 8 位值。
                void Put(byte v) { buf[i++] = v; if (sb == 2) buf[i++] = 0; }
                switch (colorType)
                {
                    case 0: Put(r); break;
                    case 4: Put(r); Put(a); break;
                    case 2: Put(r); Put(g); Put(b); break;
                    case 6: Put(r); Put(g); Put(b); Put(a); break;
                    case 3: buf[i++] = (byte)((x + y) % 3); break;
                }
            }
        return buf;
    }

    /// <summary>托管解码应当交出的那份 BGRA（alpha 一律 255＝§2 裁决；灰度填三色；PLTE 是 RGB 序）。</summary>
    private static byte[] Expected(int w, int h, int colorType, byte[]? plte = null)
    {
        var buf = new byte[w * h * 4];
        var i = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (r, g, b, _) = Pixel(x, y);
                if (colorType == 3)
                {
                    var p = (x + y) % 3;
                    (r, g, b) = (plte![p * 3], plte[p * 3 + 1], plte[p * 3 + 2]);
                }
                else if (colorType is 0 or 4) { b = r; g = r; }
                buf[i++] = b; buf[i++] = g; buf[i++] = r; buf[i++] = 255;
            }
        return buf;
    }

    private static byte[] Palette() => [10, 20, 30, 200, 0, 0, 0, 0, 200];      // 三张：RGB 各一

    private static ClipboardPayload.ImageFrame MustDecode(byte[] png)
    {
        Assert.True(ClipboardPayload.TryDecodePng(png, out var frame, out var reason), reason ?? "解开了却没给帧");
        return frame;
    }

    // ────────── 逐形状：与已知像素比，并顺手与 WinRT 那份比 ──────────

    [Theory]
    [InlineData(8, 0)]        // 灰度
    [InlineData(8, 2)]        // 真彩色
    [InlineData(8, 3)]        // 索引
    [InlineData(8, 4)]        // 灰度 + alpha
    [InlineData(8, 6)]        // 真彩色 + alpha
    [InlineData(16, 0)]
    [InlineData(16, 2)]
    [InlineData(16, 4)]
    [InlineData(16, 6)]
    public void EveryShapeDecodesToTheKnownPixels(int depth, int colorType)
    {
        const int w = 7, h = 5;                       // 奇数宽高：行跨距一算错就露（偶数尺寸会假装对）
        var png = Png(w, h, depth, colorType, Samples(w, h, depth, colorType),
            colorType == 3 ? Palette() : null);
        var frame = MustDecode(png);
        Assert.Equal((w, h), (frame.Width, frame.Height));
        // 与"我知道像素是什么"比，而不是与另一份实现比：两份实现一起错是可能的（它们会共用同一个
        // 写坏的封装器），与已知像素比才是字节级验收。
        Assert.Equal(Expected(w, h, colorType, colorType == 3 ? Palette() : null), frame.Bgra);
    }

    [Fact]
    public void BothReadPathsAskThisOneDecoder()
    {
        // 采集与读档必须共用同一份解码：各一份的话"同一帧两个哈希"＝用户每点一次重复制，历史多一条。
        // （换成这一份之前，16 位的 PNG 在 WinRT 那条路上根本交不出可用的像素块——那种图进不了历史。）
        var watcher = SourceGate.ReadRepoFile("src/StarMark.Integrations/Clipboard/ClipboardWatcher.cs");
        var store = SourceGate.ReadRepoFile("src/StarMark.Integrations/Clipboard/ClipboardImageStore.cs");
        Assert.Contains("ClipboardPayload.TryDecodePng(png", watcher);
        Assert.Contains("ClipboardPayload.TryDecodePng(File.ReadAllBytes(path)", store);
        // 解码不留第二份：BitmapDecoder 只许在"曾经解码"的地方消失，编码器（BitmapEncoder）照旧。
        Assert.DoesNotContain("BitmapDecoder", store);
        Assert.Contains("BitmapEncoder", store);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(0, 1, 2, 3, 4)]                       // 同一帧里五种滤波器逐行混用
    public void EveryRowFilterDefiltersToTheKnownPixels(params int[] filters)
    {
        const int w = 6, h = 6;
        var png = Png(w, h, 8, 6, Samples(w, h, 8, 6), filters: filters);
        Assert.Equal(Expected(w, h, 6), MustDecode(png).Bgra);
    }

    [Fact]
    public void PaethRowsMatchARealEncoderByteForByte()
    {
        // <b>故意不用上面那个 Filtered()</b>：封装器与产品共用一颗 helper 时，两边"一致地写错"是测不出来的
        // ——第一版正是这样：Paeth 的三个距离整体转了一格（|p−b|/|p−c|/|p−a|），
        // 自己的往返测全绿，而 GDI+ 编码的真实 PNG 从每行第二个像素起就串色。
        // 这里的两行字节取自 GDI+ 那份 8×6 文件的开头（同一 (a,b,c)＝(232,29,0) 的判例：答案必须是 a）。
        var raw = new byte[]
        {
            0, 0, 0, 0, 255, 29, 13, 7, 255,          // 行 0：滤波器 None，两个像素的 R,G,B,A
            4, 232, 104, 56, 0, 29, 13, 7, 0,         // 行 1：滤波器 Paeth，交出的就是编码后的样子
        };
        var png = PngRaw(2, 2, 8, 6, raw, null, null);
        Assert.Equal(
            new byte[] { 0, 0, 0, 255, 7, 13, 29, 255, 56, 104, 232, 255, 63, 117, 5, 255 },
            MustDecode(png).Bgra);
    }

    [Fact]
    public void MultiChunkIdatIsReassembled()
    {
        // 只读第一块的实现会"解出半张图"而不是报错——那比失败更坏：屏幕上是一张正常图，下半截是垃圾。
        const int w = 9, h = 9;
        var raw = Filtered(w, h, 3, 1, Samples(w, h, 8, 2), new[] { 0 });
        var png = PngRaw(w, h, 8, 2, raw, null, idatSplit: 12);
        Assert.Equal(Expected(w, h, 2), MustDecode(png).Bgra);
    }

    [Fact]
    public async Task OurOwnEncoderRoundTripsThroughTheManagedDecoder()
    {
        // §5 验收点名的"往返测"：全仓唯一那份编码器（GDI+）写出来的 PNG，必须能被托管解码器逐字节还原。
        // 2a 已用 WinRT 证过一次同一件事；这条补的是"读路换成自研之后仍然成立"。
        var bgra = new byte[16 * 9 * 4];
        for (var i = 0; i < 16 * 9; i++)
        {
            bgra[i * 4] = (byte)(i * 11); bgra[i * 4 + 1] = (byte)(i * 37); bgra[i * 4 + 2] = (byte)(i * 53);
            bgra[i * 4 + 3] = 255;
        }
        var png = await ClipboardImageStore.EncodePngAsync(bgra, 16, 9, CancellationToken.None);
        Assert.NotNull(png);
        var frame = MustDecode(png!);
        Assert.Equal((16, 9), (frame.Width, frame.Height));
        Assert.Equal(bgra, frame.Bgra);
    }

    [Fact]
    public void AlphaInTheFileIsDroppedOnTheWayOut()
    {
        // §2「alpha 丢弃并写明」：文件里的真 alpha 与 tRNS 都是死信息。留着它的坏法不是"贴图半透明"，
        // 而是两条读路（采集走 WinRT、历史走自研）对同一帧给出不同像素 ⇒ 不同哈希 ⇒ 回声挡不住。
        const int w = 4, h = 4;
        var frame = MustDecode(Png(w, h, 8, 6, Samples(w, h, 8, 6)));
        var alphas = new List<byte>();
        for (var i = 3; i < frame.Bgra.Length; i += 4) alphas.Add(frame.Bgra[i]);
        Assert.All(alphas, a => Assert.Equal((byte)255, a));
        Assert.Contains((byte)90, Samples(w, h, 8, 6));     // 图案里确有 90 这一档 alpha（源数据带得进来）
        Assert.DoesNotContain((byte)90, alphas);             // 但它没漏到出口
    }

    [Fact]
    public void HeaderSizeReadAndPixelDecodeAgree()
    {
        // 标题那一句"1920×1080"与门禁估体积都只读 IHDR（1a 那份）。它与整图解码给的尺寸不一致时，
        // 症状是"条目写着 800×600、贴出来是别的大小"。
        var png = Png(11, 3, 8, 2, Samples(11, 3, 8, 2));
        Assert.True(ClipboardPayload.TryReadPngSize(png, out var w, out var h));
        var frame = MustDecode(png);
        Assert.Equal((w, h), (frame.Width, frame.Height));
    }

    // ────────── 拒收：每一种都要出声，且不许抛 ──────────

    [Theory]
    [InlineData("bad-signature", "不是 PNG")]
    [InlineData("no-ihdr", "缺 IHDR")]
    [InlineData("interlaced", "隔行")]
    [InlineData("depth-4", "不支持的位深")]
    [InlineData("color-type-5", "不支持的颜色类型")]
    [InlineData("palette-missing", "没有调色板")]
    [InlineData("palette-index", "调色板索引越界")]
    [InlineData("truncated", "不完整")]
    [InlineData("size-bomb", "过大")]
    [InlineData("row-length", "像素长度不符")]
    [InlineData("unknown-filter", "未知的行滤波器")]
    [InlineData("empty-idat", "空的 IDAT")]
    [InlineData("not-deflate", "像素流解不开")]
    public void MalformedFramesAreRefusedWithTheirOwnReason(string kind, string fragment)
    {
        // 抛出来＝整条读档链断掉；静默交出半张图＝屏幕上有一张没人知道是坏的图。两种都不许。
        Assert.False(ClipboardPayload.TryDecodePng(Bad(kind), out _, out var reason));
        Assert.Contains(fragment, reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatingAGoodFileAnywhereNeverThrows()
    {
        // "从旧机器/备份里拷回来半张图"是真实场景。每一个前缀都必须走拒收那条路（并给一句原话）。
        var good = Png(5, 5, 8, 6, Samples(5, 5, 8, 6));
        for (var cut = 0; cut < good.Length; cut++)     // 不含整份：那一份本来就该解得开
        {
            var ok = ClipboardPayload.TryDecodePng(good[..cut], out _, out var reason);
            Assert.False(ok, $"截到 {cut} 字节居然解开了：长度判据没生效");
            Assert.NotEmpty(reason!);
        }
    }

    /// <summary>造一份"看着像 PNG 但各坏一处"的输入。每种坏法对应上面一条 reason。</summary>
    private static byte[] Bad(string kind)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Signature);
        switch (kind)
        {
            case "bad-signature": return [.. Signature[..4], .. new byte[4]];
            case "no-ihdr":
                bytes.AddRange(Chunk("IDAT", Deflate(new byte[9])));
                bytes.AddRange(Chunk("IEND", Array.Empty<byte>()));
                return bytes.ToArray();
            case "interlaced":
                bytes.AddRange(Chunk("IHDR", IHdr(4, 4, 8, 2, interlace: 1)));
                bytes.AddRange(Chunk("IDAT", Deflate(Filtered(4, 4, 3, 1, Samples(4, 4, 8, 2), new[] { 0 }))));
                break;
            case "depth-4":
                bytes.AddRange(Chunk("IHDR", IHdr(4, 4, 4, 3)));
                bytes.AddRange(Chunk("PLTE", Palette()));
                bytes.AddRange(Chunk("IDAT", Deflate(new byte[4 * (4 + 1)])));
                break;
            case "color-type-5":
                bytes.AddRange(Chunk("IHDR", IHdr(4, 4, 8, 5)));
                bytes.AddRange(Chunk("IDAT", Deflate(new byte[4 * (12 + 1)])));
                break;
            case "palette-missing":
                bytes.AddRange(Chunk("IHDR", IHdr(4, 4, 8, 3)));
                bytes.AddRange(Chunk("IDAT", Deflate(Filtered(4, 4, 1, 1, new byte[16], new[] { 0 }))));
                break;
            case "palette-index":
                // 索引 200 而调色板只有 3 张：不许"当作黑色"糊过去（那会在图上留一行来路不明的颜色）。
                bytes.AddRange(Chunk("IHDR", IHdr(2, 1, 8, 3)));
                bytes.AddRange(Chunk("PLTE", Palette()));
                bytes.AddRange(Chunk("IDAT", Deflate(new byte[] { 0, 200, 0 })));
                break;
            case "truncated": return Png(5, 5, 8, 2, Samples(5, 5, 8, 2))[..33];   // 恰好一个完整 IHDR 块，后面什么都没有
            case "size-bomb":
                // 两边都 ≤MaxImageDimension，所以尺寸判据放过它；这一条只能由"解出来的帧"上限挡：
                // 几十 KB 的输入撑出 GB 级缓冲是解压炸弹，与"边长离谱"是两个不同的判据。
                bytes.AddRange(Chunk("IHDR", IHdr(16384, 16384, 8, 2)));
                bytes.AddRange(Chunk("IDAT", Deflate(new byte[1])));
                break;
            case "row-length":
                // 声明 4×4 只喂 3 行像素：inflate 出来短一截 ⇒ 必须拒，不许画半张。
                bytes.AddRange(Chunk("IHDR", IHdr(4, 4, 8, 2)));
                bytes.AddRange(Chunk("IDAT", Deflate(Filtered(4, 3, 3, 1, Samples(4, 3, 8, 2), new[] { 0 }))));
                break;
            case "unknown-filter":
                bytes.AddRange(Chunk("IHDR", IHdr(4, 1, 8, 2)));
                bytes.AddRange(Chunk("IDAT", Deflate([9, .. new byte[12]])));
                break;
            case "empty-idat":
                bytes.AddRange(Chunk("IHDR", IHdr(4, 4, 8, 2)));
                bytes.AddRange(Chunk("IDAT", Array.Empty<byte>()));
                break;
            case "not-deflate":
                bytes.AddRange(Chunk("IHDR", IHdr(4, 4, 8, 2)));
                bytes.AddRange(Chunk("IDAT", new byte[64]));                       // 不是 zlib 流
                break;
        }
        bytes.AddRange(Chunk("IEND", Array.Empty<byte>()));
        return bytes.ToArray();
    }
}
