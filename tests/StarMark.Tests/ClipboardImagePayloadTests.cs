#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using StarMark.Abstractions.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 剪贴板图片历史的<b>字节层</b>（批次 ClipIMG-1a）：DIB/DIBV5 头部解析、PNG 魔数与 IHDR、
/// 像素身份哈希、文件命名字典、去重的字节重载。
/// <para>
/// 这一层的价值全在"字节进、字节出"可以逐值断言：DIB 的每个字段读错都不会崩、也不会抛，
/// 只会得到一张<b>上下颠倒／通道串位／整幅偏暗</b>的图存进用户目录——那种缺陷到真机上才看得见，
/// 而且一看见就是"你们把我最爱的截图毁了"。所以每个字段都要有用例正面钉一次。
/// </para>
/// </summary>
public sealed class ClipboardImagePayloadTests
{
    // ────────── 造 DIB 的小工具（刻意把字段写全，不让测试自己变成"猜头布局"）──────────

    private static void PutU32(List<byte> b, int at, uint v)
    {
        b[at] = (byte)(v & 0xFF); b[at + 1] = (byte)(v >> 8 & 0xFF);
        b[at + 2] = (byte)(v >> 16 & 0xFF); b[at + 3] = (byte)(v >> 24 & 0xFF);
    }

    private static void AppendU32(List<byte> b, uint v)
        => b.AddRange(new[] { (byte)(v & 0xFF), (byte)(v >> 8 & 0xFF), (byte)(v >> 16 & 0xFF), (byte)(v >> 24 & 0xFF) });

    private static void AppendU16(List<byte> b, ushort v) => b.AddRange(new[] { (byte)(v & 0xFF), (byte)(v >> 8 & 0xFF) });

    /// <summary>写 BITMAPINFOHEADER（40 字节）；<paramref name="biSize"/> 传 124 即 V5。</summary>
    private static List<byte> Header(uint biSize, int width, int height, ushort bitCount,
                                     uint compression, uint clrUsed = 0)
    {
        var b = new List<byte>();
        AppendU32(b, biSize);
        AppendU32(b, (uint)width);
        AppendU32(b, (uint)height);            // 负数＝自上而下，这里按原样写
        AppendU16(b, 1);                       // biPlanes
        AppendU16(b, bitCount);
        AppendU32(b, compression);
        AppendU32(b, 0);                       // biSizeImage（许多应用给 0，不作判据）
        AppendU32(b, 0); AppendU32(b, 0);      // 分辨率
        AppendU32(b, clrUsed);
        AppendU32(b, 0);                       // biClrImportant
        for (var i = b.Count; i < biSize; i++) b.Add(0);   // V4/V5 的其余字段留给调用方覆盖
        return b;
    }

    private static void SetAt(List<byte> b, int at, uint v) => PutU32(b, at, v);

    private static byte[] Palette(params (byte B, byte G, byte R)[] colors)
    {
        var b = new List<byte>();
        foreach (var c in colors) b.AddRange(new[] { c.B, c.G, c.R, (byte)0 });
        return b.ToArray();
    }

    private static byte[] Pad(List<byte> head, byte[] tail)
    {
        head.AddRange(tail);
        return head.ToArray();
    }

    private static bool DibOk(byte[] data, out ClipboardPayload.ImageFrame frame, out string? reason)
        => ClipboardPayload.TryDecodeDib(data, out frame, out reason);

    // ────────── 32 位：行序与 alpha ──────────

    [Fact]
    public void Dib32BottomUpReversesRowsAndForcesOpaqueAlpha()
    {
        // DIB 自下而上：源里的第一行是画面的<b>最下面</b>一行。alpha 一律丢（§2 裁决）。
        var head = Header(40, 2, 2, 32, 0);
        var pixels = new List<byte>();
        pixels.AddRange(new byte[] { 10, 20, 30, 0 });         // 源行 0 ＝ 底行
        pixels.AddRange(new byte[] { 40, 50, 60, 255 });
        pixels.AddRange(new byte[] { 70, 80, 90, 0 });         // 源行 1 ＝ 顶行
        pixels.AddRange(new byte[] { 100, 110, 120, 0 });
        var ok = DibOk(Pad(head, pixels.ToArray()), out var f, out var why);

        Assert.True(ok, why);
        Assert.Equal((2, 2), (f.Width, f.Height));
        Assert.Equal(new byte[] { 70, 80, 90, 255, 100, 110, 120, 255,
                                  10, 20, 30, 255, 40, 50, 60, 255 }, f.Bgra);
    }

    [Fact]
    public void Dib32TopDownKeepsRowOrder()
    {
        // biHeight 为负＝自上而下：源行序就是目标行序。取绝对值后忘了符号＝整张图上下颠倒。
        var head = Header(40, 2, -2, 32, 0);
        var pixels = new List<byte>();
        pixels.AddRange(new byte[] { 1, 2, 3, 9 });
        pixels.AddRange(new byte[] { 4, 5, 6, 9 });
        pixels.AddRange(new byte[] { 7, 8, 9, 9 });
        pixels.AddRange(new byte[] { 10, 11, 12, 9 });
        Assert.True(DibOk(Pad(head, pixels.ToArray()), out var f, out _));
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255 }, f.Bgra);
    }

    [Fact]
    public void Dib24SkipsRowPadding()
    {
        // 24bpp × 宽 2 ⇒ 每行 6 字节有效、补齐到 8。填充位若被当成像素读，右边那格会串位。
        var head = Header(40, 2, 1, 24, 0);
        var pixels = new byte[] { 1, 2, 3, 4, 5, 6, 0xEE, 0xEE };
        Assert.True(DibOk(Pad(head, pixels), out var f, out _));
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 }, f.Bgra);
    }

    // ────────── 调色板系：张数与位序 ──────────

    [Fact]
    public void Dib8UsesFullPaletteWhenClrUsedIsZero()
    {
        var pal = new List<byte>(Palette((0, 0, 0), (11, 22, 33), (44, 55, 66)));
        for (var i = 3; i < 256; i++) pal.AddRange(new byte[] { 0, 0, 0, 0 });
        var head = Header(40, 2, 1, 8, 0);
        head.AddRange(pal);
        head.AddRange(new byte[] { 1, 2, 0, 0 });                   // stride=4
        Assert.True(DibOk(head.ToArray(), out var f, out _));
        Assert.Equal(new byte[] { 11, 22, 33, 255, 44, 55, 66, 255 }, f.Bgra);
    }

    [Fact]
    public void Dib8HonoursClrUsedInsteadOfAssumingTwoFiftySix()
    {
        // biClrUsed=2 ⇒ 调色板只有 2 项。写死 256 项会把像素起点推错 1016 字节 ⇒ 判"数据不完整"。
        var head = Header(40, 2, 1, 8, 0, clrUsed: 2);
        head.AddRange(Palette((0, 0, 0), (7, 8, 9)));
        head.AddRange(new byte[] { 1, 1, 0, 0 });
        Assert.True(DibOk(head.ToArray(), out var f, out _));
        Assert.Equal(new byte[] { 7, 8, 9, 255, 7, 8, 9, 255 }, f.Bgra);
    }

    [Fact]
    public void DibRejectsPaletteIndexBeyondTheDeclaredTableWithoutReadingPastIt()
    {
        // 声明只有 2 项却出现索引 9：越界索引会把头后面的字节当颜色读，甚至越过数组末尾。宁可整帧拒收。
        var head = Header(40, 1, 1, 8, 0, clrUsed: 2);
        head.AddRange(Palette((0, 0, 0), (7, 8, 9)));
        head.AddRange(new byte[] { 9, 0, 0, 0 });
        AssertRejects(head.ToArray());
    }

    [Fact]
    public void Dib4ReadsHighNibbleFirst()
    {
        var pal = new List<byte>(Palette((0, 0, 0), (1, 2, 3), (4, 5, 6)));
        for (var i = 3; i < 16; i++) pal.AddRange(new byte[] { 0, 0, 0, 0 });
        var head = Header(40, 2, 1, 4, 0);
        head.AddRange(pal);
        head.AddRange(new byte[] { 0x12, 0, 0, 0 });                  // 高位半字节＝第一个像素
        Assert.True(DibOk(head.ToArray(), out var f, out _));
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 }, f.Bgra);
    }

    [Fact]
    public void Dib1ReadsMostSignificantBitFirst()
    {
        var head = Header(40, 3, 1, 1, 0, clrUsed: 2);
        head.AddRange(Palette((0, 0, 0), (200, 100, 50)));
        head.AddRange(new byte[] { 0b1010_0000, 0, 0, 0 });
        Assert.True(DibOk(head.ToArray(), out var f, out _));
        Assert.Equal(new byte[] { 200, 100, 50, 255, 0, 0, 0, 255, 200, 100, 50, 255 }, f.Bgra);
    }

    // ────────── 位掩码：CF_DIBV5 的经典陷阱 ──────────

    [Fact]
    public void Bitfields16ExpandsFiveAndSixBitChannels()
    {
        // 期望值一律写成 <b>B,G,R,A</b> 的字节序（满红＝0,0,255,255）——写反一次就是一条假缺陷报告。
        var head = Header(40, 2, 1, 16, 3);                     // BI_BITFIELDS
        AppendU32(head, 0xF800);   // 40 字节头时，掩码就占在"调色板位置"
        AppendU32(head, 0x07E0);
        AppendU32(head, 0x001F);
        AppendU16(head, 0x07E0);   // 只满绿
        AppendU16(head, 0xF800);   // 只满红
        AppendU16(head, 0);         // stride=4 的补齐
        AppendU16(head, 0);
        Assert.True(DibOk(head.ToArray(), out var f, out _));
        Assert.Equal(new byte[] { 0, 255, 0, 255, 0, 0, 255, 255 }, f.Bgra);
    }

    [Fact]
    public void Rgb16DefaultsToFiveFiveFive()
    {
        var head = Header(40, 1, 1, 16, 0);                     // BI_RGB ⇒ 规定为 X1R5G5B5
        AppendU16(head, 0x7FFF);
        AppendU16(head, 0);
        Assert.True(DibOk(head.ToArray(), out var f, out _));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, f.Bgra);
    }

    [Fact]
    public void Bitfields32ShiftsPixelStartByTheTwelveMaskBytes()
    {
        // 掩码三张 DWORD 在 40 字节头后面<b>真的占了位置</b>：像素起点不后移 12 字节，第一格就读到掩码本身。
        var head = Header(40, 1, 1, 32, 3);
        AppendU32(head, 0x00FF0000); AppendU32(head, 0x0000FF00); AppendU32(head, 0x000000FF);
        AppendU32(head, 0x00_07_08_09);                          // 内存里 B=9 G=8 R=7
        Assert.True(DibOk(head.ToArray(), out var f, out _));
        Assert.Equal(new byte[] { 9, 8, 7, 255 }, f.Bgra);
    }

    [Fact]
    public void DibV5TakesMasksFromTheHeaderNotThePaletteSlot()
    {
        // biSize=124 ⇒ 掩码在头内偏移 40/44/48；像素从 124 起。当成 40 字节头处理会多让 12 字节像素起点。
        var head = Header(124, 1, 1, 32, 3);
        SetAt(head, 40, 0x00FF0000); SetAt(head, 44, 0x0000FF00); SetAt(head, 48, 0x000000FF);
        head.AddRange(new byte[] { 11, 22, 33, 0 });
        Assert.True(DibOk(head.ToArray(), out var f, out _));
        Assert.Equal(new byte[] { 11, 22, 33, 255 }, f.Bgra);
    }

    // ────────── 畸形输入：一律拒收并给得出原因 ──────────

    [Fact]
    public void RejectsTruncatedHeader()
    {
        AssertRejects(new byte[39]);      // 差一字节就不是一个完整的位图头
        AssertRejects(Array.Empty<byte>());
        AssertRejects(null!);
    }

    [Fact]
    public void RejectsCoreHeaderAndTinyHeaders()
    {
        var core = new List<byte>();
        AppendU32(core, 12);                                     // BITMAPCOREHEADER：16 位时代
        AssertRejects(core.ToArray());
        var tiny = Header(16, 1, 1, 32, 0);
        AssertRejects(tiny.ToArray());
    }

    [Fact]
    public void RejectsUnknownBitDepthAndCompressions()
    {
        AssertRejects(Header(40, 1, 1, 5, 0).ToArray());         // 位深 5 不是任何真实布局
        AssertRejects(Header(40, 1, 1, 8, 1).ToArray());         // BI_RLE8
        AssertRejects(Header(40, 1, 1, 8, 2).ToArray());         // BI_RLE4
        AssertRejects(Header(40, 1, 1, 32, 4).ToArray());        // BI_JPEG
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-4, 4)]
    public void RejectsImpossibleDimensions(int width, int height)
        => AssertRejects(Header(40, width, height, 32, 0).ToArray());

    [Fact]
    public void RejectsOverlargeDimensions()
        => AssertRejects(Header(40, ClipboardPayload.MaxImageDimension + 1, 1, 32, 0).ToArray());

    [Fact]
    public void RejectsPixelDataShorterThanTheGeometry()
    {
        // 头说 4×4 的 32 位（=64 字节像素），实际只给了 8 字节。不校验就会在拷贝循环里越界。
        var head = Header(40, 4, 4, 32, 0);
        head.AddRange(new byte[8]);
        AssertRejects(head.ToArray());
    }

    [Fact]
    public void RejectsAllZeroBitfields()
    {
        var head = Header(40, 1, 1, 32, 3);
        AssertRejects(Pad(head, new byte[12 + 4]));               // 三张掩码全零
    }

    [Fact]
    public void RejectReasonIsNeverEmptyWhenDecodingFails()
    {
        Assert.False(ClipboardPayload.TryDecodeDib(null, out _, out var r1));
        Assert.False(string.IsNullOrWhiteSpace(r1));
        Assert.False(ClipboardPayload.TryDecodeDib(Header(40, 1, 1, 8, 2).ToArray(), out _, out var r2));
        Assert.False(string.IsNullOrWhiteSpace(r2));
    }

    private static void AssertRejects(byte[] data)
        => Assert.False(ClipboardPayload.TryDecodeDib(data, out var f, out var reason),
            $"这份畸形输入本该被拒，却解出了 {f.Width}×{f.Height}（原因：{reason ?? "无"}）");

    // ────────── PNG：魔数与大端 IHDR ──────────

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static byte[] PngHeader(int width, int height, uint chunkLength = 13, string chunkType = "IHDR")
    {
        var b = new List<byte>();
        b.AddRange(PngSignature);
        AppendU32Big(b, chunkLength);
        b.AddRange(System.Text.Encoding.ASCII.GetBytes(chunkType));
        AppendU32Big(b, (uint)width);
        AppendU32Big(b, (uint)height);
        b.AddRange(new byte[] { 8, 6, 0, 0, 0, 0, 0, 0, 0 });     // 位深/色彩类型等 + 一段假 CRC
        return b.ToArray();
    }

    private static void AppendU32Big(List<byte> b, uint v)
        => b.AddRange(new[] { (byte)(v >> 24 & 0xFF), (byte)(v >> 16 & 0xFF), (byte)(v >> 8 & 0xFF), (byte)(v & 0xFF) });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PngMagicIsCheckedByteByByte(bool intact)
    {
        var data = PngHeader(4, 4);
        if (!intact) data[1] = 0x00;
        Assert.Equal(intact, ClipboardPayload.IsPng(data));
    }

    [Fact]
    public void PngSizeComesFromBigEndianIhdr()
    {
        Assert.True(ClipboardPayload.TryReadPngSize(PngHeader(1920, 1080), out var w, out var h));
        Assert.Equal((1920, 1080), (w, h));
    }

    [Fact]
    public void PngSizeRejectsLittleEndianAndWrongFirstChunk()
    {
        // 用 BitConverter 读 IHDR 的"顺手写法"会得到一个巨大尺寸——这里反向钉住它是大端。
        var le = new List<byte>();
        le.AddRange(PngSignature);
        AppendU32(le, 13); le.AddRange(new byte[] { 0x49, 0x48, 0x44, 0x52 });
        AppendU32(le, 1920); AppendU32(le, 1080);
        le.AddRange(new byte[9]);
        Assert.False(ClipboardPayload.TryReadPngSize(le.ToArray(), out _, out _));

        Assert.False(ClipboardPayload.TryReadPngSize(PngHeader(4, 4, chunkType: "IEXI"), out _, out _));
        Assert.False(ClipboardPayload.TryReadPngSize(PngHeader(4, 4, chunkLength: 12), out _, out _));
        Assert.False(ClipboardPayload.TryReadPngSize(PngSignature, out _, out _));     // 签名之后什么都没有
        Assert.False(ClipboardPayload.TryReadPngSize(
            PngHeader(ClipboardPayload.MaxImageDimension + 1, 2), out _, out _));
    }

    // ────────── 像素身份：哈希与去重 ──────────

    private static readonly byte[] Red2x2 = { 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255 };

    [Fact]
    public void ImageKeyHasTheSameShapeAsTheTextKey()
    {
        var id = ClipboardPolicy.BuildImageSourceId(Red2x2);
        Assert.StartsWith(ClipboardPolicy.SourceIdPrefix, id);
        Assert.Equal(ClipboardPolicy.SourceIdPrefix.Length + 32, id.Length);       // 与文本键同一长度、同一小写 hex
        Assert.Equal(id, id.ToLowerInvariant());
        Assert.Equal(id, ClipboardPolicy.BuildImageSourceId(Red2x2));              // 同像素 ⇒ 同键（回放命中靠这个）
    }

    [Fact]
    public void ImageKeyChangesWhenOnePixelChanges()
    {
        var other = (byte[])Red2x2.Clone();
        other[0] = 254;
        Assert.NotEqual(ClipboardPolicy.BuildImageSourceId(Red2x2), ClipboardPolicy.BuildImageSourceId(other));
    }

    [Fact]
    public void ImageEchoIsConsumedOnceLikeTheTextOne()
    {
        var d = new ClipboardDedupe();
        d.NoteOwnWrite(Red2x2, 1_000);
        Assert.True(d.ShouldSkip(Red2x2, 1_001));         // 自己写的那一次：挡掉
        Assert.False(d.ShouldSkip(Red2x2, 1_002));        // 登记已被消费，用户真复制不该再挡
    }

    [Fact]
    public void ImageEchoExpiresInsteadOfMaskingForever()
    {
        var d = new ClipboardDedupe();
        d.NoteOwnWrite(Red2x2, 0);
        Assert.True(d.ShouldSkip(Red2x2, 1));
        var d2 = new ClipboardDedupe();
        d2.NoteOwnWrite(Red2x2, 0);
        Assert.False(d2.ShouldSkip(Red2x2, ClipboardDedupe.OwnWriteTtlMs + 1));   // 一次失败的写入不许永久屏蔽同图
    }

    [Fact]
    public void OfficeBurstOfTwoIdenticalFramesCountsOnce()
    {
        // §2 说"突发去抖天然成立"：这里钉住它——两帧字节相同 ⇒ 第二帧被 700ms 窗口挡掉。
        var d = new ClipboardDedupe();
        Assert.False(d.ShouldSkip(Red2x2, 10_000));
        Assert.True(d.ShouldSkip(Red2x2, 10_100));
        Assert.False(d.ShouldSkip(Red2x2, 10_000 + ClipboardDedupe.DefaultWindowMs + 1));
    }

    [Fact]
    public void TextAndImageIdentitiesDoNotShareAGate()
    {
        var d = new ClipboardDedupe();
        d.NoteOwnWrite("同一段文字", 0);
        Assert.True(d.ShouldSkip("同一段文字", 1));
        Assert.False(d.ShouldSkip(Red2x2, 2));            // 图片身份不能被文本登记挡掉，反之亦然
        var d2 = new ClipboardDedupe();
        d2.NoteOwnWrite(Red2x2, 0);
        Assert.False(d2.ShouldSkip("同一段文字", 1));
    }

    // ────────── 文件命名与占用口径（§3-Q6"对用户可见"）──────────

    private static readonly DateTimeOffset Noon = new(2026, 9, 27, 14, 32, 0, TimeSpan.FromHours(8));

    [Fact]
    public void FileNamesAreHumanReadableAndSharedByBothFiles()
    {
        const string id = "c-9f2a3b8c1d2e3f405162738495a6b7c8";
        Assert.Equal("2026-09-27_1432_9f2a3b8c.png", ClipAssets.MainNameOf(id, Noon));
        Assert.Equal("2026-09-27_1432_9f2a3b8c_thumb.jpg", ClipAssets.ThumbNameOf(id, Noon));
        Assert.Equal("2026-09-27_1432_9f2a3b8c", ClipAssets.BaseNameOf(id, Noon));
    }

    [Fact]
    public void HashSegmentIsEightCharsOfTheRealHash()
    {
        // §3-Q6 原话是"时间+8 位哈希前缀"（同段的示例名只有 6 位，这里以正文为准并钉住位数）。
        Assert.Equal(8, ClipAssets.HashPrefixChars);
        Assert.Equal("abcd1234", ClipAssets.HashPartOf("c-abcd1234deadbeef"));
    }

    [Theory]
    [InlineData("c-9f2a3b8c1d2e3f405162738495a6b7c8", true)]
    [InlineData("c-9F2A3B8C1D2E3F405162738495A6B7C8", true)]     // 大写 hex 也合法（键是自己写的小写，但恢复时不该因大小写丢图）
    [InlineData("c-9f2a", false)]                                  // 太短
    [InlineData("9f2a3b8c1d2e3f405162738495a6b7c8", false)]        // 没有前缀
    [InlineData("c-../../windows", false)]                          // 手改过的备份字段不许变成路径片段
    [InlineData("c-9f2a3b8c1d2e3f405162738495a6b7c8/../x", false)]
    public void SourceIdIsCheckedBeforeItBecomesAFileName(string id, bool safe)
        => Assert.Equal(safe, ClipAssets.IsSafeSourceId(id));

    [Fact]
    public void UnsafeSourceIdStillYieldsAInertName()
    {
        // 不合法时既不抛也不给空串（空串＝两张图抢同一个文件名），落一坨零，且绝不含分隔符。
        var hash = ClipAssets.HashPartOf("c-../../windows");
        Assert.Equal("00000000", hash);
        Assert.DoesNotContain("..", ClipAssets.MainNameOf("c-../../windows", Noon));
        Assert.DoesNotContain('\\', ClipAssets.MainNameOf("c-../../windows", Noon));
    }

    [Fact]
    public void TempNamesAreIdentifiableSoConvergenceOnlyDeletesItsOwn()
    {
        var main = ClipAssets.MainNameOf("c-9f2a3b8c1d2e3f405162738495a6b7c8", Noon);
        Assert.True(ClipAssets.IsTempName(ClipAssets.TempNameOf(main)));
        Assert.False(ClipAssets.IsTempName(main));
        Assert.False(ClipAssets.IsTempName("用户拷进来的图.png"));
    }

    [Fact]
    public void FootprintCountsMainAndThumbSeparately()
    {
        var files = new[]
        {
            ("2026-09-27_1432_9f2a3b8c.png", 100_000L),
            ("2026-09-27_1432_9f2a3b8c_thumb.jpg", 8_000L),
            ("2026-09-27_1435_aabbccdd.png", 50_000L),
            ("2026-09-27_1435_aabbccdd.png.tmp", 777L),
        };
        var f = ClipAssets.Summarize(files, out var temp);
        Assert.Equal(150_000, f.MainBytes);
        Assert.Equal(8_000, f.ThumbBytes);
        Assert.Equal(2, f.MainCount);
        Assert.Equal(1, f.ThumbCount);
        Assert.Equal(777, temp);                       // 临时件只进"待清"这一类，不许混进占用
        Assert.Equal(158_000, f.TotalBytes);
        Assert.Equal(3, f.TotalFiles);
    }

    [Fact]
    public void StorageRootFollowsTheExistingAppDataLayout()
        => Assert.Equal(Path.Combine(@"C:\Users\x\AppData\Local\StarMark", "clip"),
            ClipAssets.DirectoryFor(@"C:\Users\x\AppData\Local\StarMark"));

    [Theory]
    [InlineData(1920, 1080, 1920)]
    [InlineData(1080, 1920, 1920)]
    [InlineData(1, 1, 1)]
    public void ThumbnailEdgeIsTheLongSide(int w, int h, int expected)
        => Assert.Equal(expected, ClipAssets.ThumbnailEdge(w, h));

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    public void OneSizeVocabularyForEverySurface(long bytes, string expected)
        => Assert.Equal(expected, ClipAssets.DescribeBytes(bytes));

    [Fact]
    public void ImageTitleIsTheGeometry()
    {
        Assert.Equal("1920×1080", ClipAssets.DescribeTitle(1920, 1080));
        Assert.Equal("图片", ClipAssets.DescribeTitle(0, 0));        // 尺寸读不出来时也不许空着
    }

    [Fact]
    public void PerImageByteCeilingIsAnInternalConstant()
    {
        // §4：单张 20MB「不可调」——它挡的是异常帧灌入，与偏好无关；有测钉着，免得下次"顺手"挪进设置页。
        Assert.Equal(20 * 1024 * 1024, ClipboardPolicy.MaxImageBytes);
    }

    // ────────── 设置页那两句话（占用 / 预估）：判据住 Core，XAML 只显示 ──────────

    [Fact]
    public void UsageSentenceSplitsMainFromThumb()
    {
        // 主图与缩略图分开报是这个功能对"存储可见"的一部分兑现：合起来报一个总数，
        // 用户删完图看见数字几乎没动，结论就是"删了也没用"——那比不报更糟。
        // 字节数刻意挑能整除的：这条测的是"分不分开报"，不是四舍五入。
        var fp = ClipAssets.Summarize(new[]
        {
            ("2026-09-28_0915_aaaa1111.png", 700_000L), ("2026-09-28_0915_aaaa1111_thumb.jpg", 51_200L),
            ("2026-09-28_0916_bbbb2222.png", 348_576L), ("2026-09-28_0916_bbbb2222_thumb.jpg", 10_240L),
        }, out var temp);

        var s = ClipAssets.DescribeUsage(fp, temp);
        Assert.Contains("图片 2 张", s);
        Assert.Contains("共 1 MB", s);                   // 700000 + 348576 = 1 MiB
        Assert.Contains("缩略图 2 张", s);
        Assert.Contains("60 KB", s);                     // 51200 + 10240 = 60 KiB
        Assert.DoesNotContain("临时件", s);             // 没有临时件就别提，别叫用户去找不存在的东西
    }

    [Fact]
    public void EmptyFolderSaysSo_AndTempFilesGetNamedWithAnExit()
    {
        Assert.Equal("图片目录还是空的。", ClipAssets.DescribeUsage(
            ClipAssets.Summarize(Array.Empty<(string, long)>(), out var none), none));

        var fp = ClipAssets.Summarize(new[] { ("2026-09-28_0915_cccc3333.png.tmp", 4096L) }, out var temp);
        Assert.Equal(4096, temp);
        Assert.Equal(0, fp.TotalFiles);                // 临时件既不计入主图也不计入缩略图
        var s = ClipAssets.DescribeUsage(fp, temp);
        Assert.Contains("未写完的临时件", s);
        Assert.Contains("可清理", s);                   // 报坏消息的那句要同时给出口（P-54 同口径）
    }

    [Theory]
    [InlineData(0L, 200, "估不出来")]                            // 一张都没有：不许编一个平均体积出来
    [InlineData(1_048_576L, 200, "200 MB")]                       // 平均 1 MB × 200 张（默认上限）
    [InlineData(2_097_152L, 2000, "3.91 GB")]                     // 平均 2 MB 拉到最高上限：句子要自己跳到 GB 档
    public void ProjectionUsesTheSameSizeVocabularyAsTheOccupancyLine(long mainBytes, int cap, string fragment)
    {
        var fp = new ClipAssets.Footprint(mainBytes, mainBytes / 40, mainBytes > 0 ? 1 : 0, mainBytes > 0 ? 1 : 0);
        var s = ClipAssets.DescribeProjection(fp, cap);
        Assert.Contains(fragment, s, StringComparison.Ordinal);
        Assert.Contains(cap.ToString(System.Globalization.CultureInfo.InvariantCulture), s);
    }
}
