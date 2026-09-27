#nullable enable
using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Integrations.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// ClipIMG-2d：<b>右键「复制图片」</b>——剪贴板图片行与本机图片文件都要能交出<b>位图数据</b>。
/// <para>
/// 这一批的起因是用户原话："剪贴板和主窗口的文件夹树中只能复制图片的路径，若需要复制图片，
/// 需要打开图片路径或直接打开图片"。所以这里钉的正是那两件事：
/// <b>①哪些行该有这一项</b>（判据只有一份，且不按条目类型列白名单）、
/// <b>②交出去的是 PNG 数据而不是路径</b>（PNG 逐字节透传；别的格式转成 PNG 后与"登记回声的像素"必须同源）。
/// </para>
/// <para>真机那一半（粘到画图/Word 看得见图、历史不许多出条目）在实施方案 §6，不在这里冒充已验。</para>
/// </summary>
public sealed class ClipboardCopyImageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"starmark_copyimg_{Guid.NewGuid():N}");

    public ClipboardCopyImageTests()
    {
        Directory.CreateDirectory(_dir);
        // 写侧/删侧/读侧共用的那一个改道（见 ClipAssets 注释）：不改道的话历史行那两条会
        // 直接往用户真正的 clip 目录里写测试图，而"删掉这一条"也照删不误。
        ClipAssets.FolderOverride = _dir;
    }

    public void Dispose()
    {
        ClipAssets.FolderOverride = null;
        try { Directory.Delete(_dir, true); } catch { /* 临时目录留给系统清 */ }
    }

    // ────────── ① 哪些行该有这一项 ──────────

    [Theory]
    // 文件夹树 / 磁盘搜索的结果行（两斜杠拼法）与图片行（三斜杠拼法）都要有
    [InlineData("file://C:/pics/a.png", true)]
    [InlineData("file:///C:/pics/a.png", true)]
    [InlineData("FILE://C:/pics/A.JPG", true)]                 // scheme 与扩展名都不分大小写
    [InlineData("file://C:/My Docs/a.png", true)]              // 路径里有空格照样是图
    [InlineData("file://C:/pics/a.jpeg", true)]
    [InlineData("file://C:/pics/a.bmp", true)]
    [InlineData("file://C:/pics/a.gif", true)]
    [InlineData("file://C:/pics/a.tif", true)]
    [InlineData("file://C:/pics/a.tiff", true)]
    [InlineData("file://C:/pics/a.jfif", true)]
    // 不是图片 / 不是本机文件 / 什么都没有
    [InlineData("file://C:/pics/a.txt", false)]
    [InlineData("file://C:/pics/a.pdf", false)]
    [InlineData("file://C:/pics/a", false)]                     // 没有扩展名
    [InlineData("file://C:/pics/", false)]
    [InlineData("https://x.com/a.png", false)]                  // 网上的图：本机没有那个文件
    [InlineData("C:\\pics\\a.png", false)]                      // 裸路径不是 URI
    [InlineData("", false)]
    [InlineData(null, false)]
    // 有意不给的两类（理由写在 ItemCardPolicy 的注释里，别"顺手补上"）：
    [InlineData("file://C:/pics/a.webp", false)]                // 系统不保证有 WebP 解码器＝点了只会报错
    [InlineData("file://C:/pics/a.heic", false)]
    [InlineData("file://C:/pics/a.ico", false)]
    [InlineData("file://server/share/a.png", false)]            // UNC：TryPathFromUri 认盘符，宁可少给
    public void TheActionAppearsForLocalImageFiles(string? uri, bool expected)
        => Assert.Equal(expected, ItemCardPolicy.CanCopyAsImage(uri));

    [Theory]
    [InlineData(".PNG")] [InlineData(".Bmp")] [InlineData(".tiff")]     // 大小写不敏感
    [InlineData("png")]                                                 // 少了点就不是 Path.GetExtension 的形状
    [InlineData(".pngx")] [InlineData(".")] [InlineData("")] [InlineData(null)]
    public void ExtensionJudgementTakesOnlyDotPrefixedForms(string? ext)
        => Assert.Equal(ext is { Length: > 2 } && ext.StartsWith('.')
            && ext.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".jfif" or ".bmp" or ".gif" or ".tif" or ".tiff",
            ItemCardPolicy.IsBitmapDecodableExtension(ext));

    [Fact]
    public void UriIsTheOnlyInputSoNoRowTypeNeedsASecondRule()
    {
        // 判据不吃 ItemType：书签里一条指向本机图片的 file:// 也该有的这一项。
        // 写成"按类型列白名单"就会长出第二种"什么算图片行"（漏掉一类＝用户只能打开图片再另存）。
        var policy = SourceGate.ReadRepoFile("src/StarMark.Abstractions/ItemCardPolicy.cs");
        var body = SourceGate.MethodBody(policy, "public static bool CanCopyAsImage(string? uri)");
        Assert.DoesNotContain("ItemType", body);
        Assert.Contains("LocalFileIdentity.TryPathFromUri", body);   // 与动作那一侧同一句路径解析
    }

    // ────────── ② 交出去的是数据：PNG 逐字节透传 ──────────

    [Fact]
    public async Task PngFilesAreHandedOverByteForByte()
    {
        // 重新编码一次就会换掉字节。"粘出去的字节"与"登记回声的像素"必须出自同一张图，
        // 否则采集侧算出的身份哈希与登记的那一个不是一回事——症状是"每右键一次，历史多一条"。
        var bgra = Screenshot(9, 5);                     // 奇数宽高：跨距算错就露
        var png = await ClipboardImageStore.EncodePngAsync(bgra, 9, 5, CancellationToken.None);
        Assert.NotNull(png);
        var path = Write("a.png", png!);

        var (got, frame, why) = await ClipboardImageStore.TryReadFileAsPngAsync(path, CancellationToken.None);
        Assert.True(ClipboardPayload.IsPng(got), why ?? "交出来的不是 PNG");
        Assert.Equal(png, got);
        Assert.Equal((9, 5), (frame.Width, frame.Height));
        Assert.Equal(bgra, frame.Bgra);
    }

    [Fact]
    public async Task BmpIsTranscodedToTheExactSamePixels()
    {
        // BMP 是这里唯一能"逐像素验收"的非 PNG 格式（24 位、无压缩、我知道每个字节写的是什么）。
        // JPEG/GIF/TIFF 只能验形状与同源，因为它们要么有损要么需要真编码器——那一条留给 §6 真机。
        var (w, h) = (7, 5);
        var bytes = Bmp24(w, h, (x, y) => ((byte)(x * 11), (byte)(y * 31), (byte)(x * 5 + y * 7)));
        var path = Write("a.bmp", bytes);

        var (png, frame, why) = await ClipboardImageStore.TryReadFileAsPngAsync(path, CancellationToken.None);
        Assert.True(ClipboardPayload.IsPng(png), why ?? "BMP 没能转成 PNG");
        Assert.Equal((w, h), (frame.Width, frame.Height));
        Assert.Equal(ExpectedBgra(w, h, (x, y) => ((byte)(x * 11), (byte)(y * 31), (byte)(x * 5 + y * 7))), frame.Bgra);

        // 通道顺序在这条路上错过两次（PNG 的 RGB 与 DIB/BMP 的 BGR）：上面那条逐字节相等就是它的墓碑。
        Assert.True(ClipboardPayload.TryDecodePng(png!, out var again, out _));
        Assert.Equal(frame.Bgra, again.Bgra);
    }

    [Fact]
    public async Task JpegIsTranscodedAndIdentityComesFromThePngWeHandOver()
    {
        // 这一条钉的是"身份只有一种算法"：WinRT 解码器当场交出的那块像素不参与哈希，
        // 登记回声用的必须是<b>交出去的那份 PNG</b> 解出来的。两边各解一次＝两种哈希＝回声失效。
        var bgra = Solid(6, 4, r: 220, g: 30, b: 40);
        var jpeg = await ClipboardImageStore.EncodeThumbnailAsync(bgra, 6, 4, CancellationToken.None);
        Assert.NotNull(jpeg);
        var path = Write("a.jpg", jpeg!);

        var (png, frame, why) = await ClipboardImageStore.TryReadFileAsPngAsync(path, CancellationToken.None);
        Assert.True(ClipboardPayload.IsPng(png), why ?? "JPEG 没能转成 PNG");
        Assert.Equal((6, 4), (frame.Width, frame.Height));
        Assert.True(ClipboardPayload.TryDecodePng(png!, out var again, out _));
        Assert.Equal(frame.Bgra, again.Bgra);
        Assert.Equal(ClipboardPolicy.BuildImageSourceId(again.Bgra), ClipboardPolicy.BuildImageSourceId(frame.Bgra));

        // 有损，所以不比字节，比"它确实是那张偏红的图"：全给成灰/蓝（通道走反）时这一句会红。
        var at = (2 * 4);
        Assert.True(frame.Bgra[at + 2] > 120, $"R 通道不对：{frame.Bgra[at + 2]}");
        Assert.True(frame.Bgra[at] < 120, $"B 通道不对：{frame.Bgra[at]}");
    }

    [Fact]
    public async Task TheTranscodedPixelsActuallyBlockTheEcho()
    {
        // 与 BMP 那条同源，但走到"采集侧问 ShouldSkip"那一步为止：
        // 登记与判重用的是同一份像素，中间任何一处换了键的算法，症状都是"右键复制一次图片，历史多一条"。
        var (w, h) = (5, 5);
        var bytes = Bmp24(w, h, (x, y) => ((byte)(x * 17), (byte)(y * 23), (byte)(x + y)));
        var (_, frame, why) = await ClipboardImageStore.TryReadFileAsPngAsync(Write("echo.bmp", bytes), CancellationToken.None);
        Assert.True(frame.Width == w && frame.Height == h, why);

        var dedupe = new ClipboardDedupe();
        dedupe.NoteOwnWrite(frame.Bgra, 1_000);
        Assert.True(dedupe.ShouldSkip(ExpectedBgra(w, h, (x, y) => ((byte)(x * 17), (byte)(y * 23), (byte)(x + y))), 1_001));
    }

    // ────────── 坏消息：每种坏法都说得出为什么 ──────────

    [Fact]
    public async Task MissingFileIsRefusedInItsOwnWords()
    {
        var (png, _, why) = await ClipboardImageStore.TryReadFileAsPngAsync(
            Path.Combine(_dir, "gone.png"), CancellationToken.None);
        Assert.Null(png);
        Assert.Contains("不在本机", why, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", why, StringComparison.Ordinal);   // 不是系统原话
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public async Task NoPathIsRefusedWithoutAnException(string? path)
    {
        var (png, _, why) = await ClipboardImageStore.TryReadFileAsPngAsync(path, CancellationToken.None);
        Assert.Null(png);
        Assert.Contains("没有给出文件路径", why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALyingExtensionIsRefusedNotSilentlyCopied()
    {
        // 名字是 .png 内容是一段文本：这一条防的是"把不是位图的东西当位图交出去"——
        // 症状是粘到画图里什么都没有，而右键那一下看起来成功了。
        var path = Write("lie.png", System.Text.Encoding.UTF8.GetBytes("这不是一张图"));
        var (png, _, why) = await ClipboardImageStore.TryReadFileAsPngAsync(path, CancellationToken.None);
        Assert.Null(png);
        Assert.Contains("解不开", why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATruncatedPngIsRefusedEvenThoughTheSignatureIsRight()
    {
        // 有 PNG 头但 IDAT 断了：IsPng 过、TryDecodePng 不过。这一条钉住"半张图不许交出去"，
        // 也顺手钉住转码那一支不会把它当成"别的格式"重新编出一张错图。
        var bgra = Screenshot(8, 6);
        var png = await ClipboardImageStore.EncodePngAsync(bgra, 8, 6, CancellationToken.None);
        Assert.NotNull(png);
        // 只留开头 40 字节：签名与 IHDR 在，IDAT 一个字节都没有——两条读路都必须拒它。
        var cut = png!.AsSpan(0, System.Math.Min(40, png!.Length)).ToArray();
        var (got, _, why) = await ClipboardImageStore.TryReadFileAsPngAsync(Write("cut.png", cut), CancellationToken.None);
        Assert.Null(got);
        Assert.False(string.IsNullOrEmpty(why));
    }

    [Fact]
    public async Task OversizedFilesAreRefusedBeforeTheyAreRead()
    {
        // 与采集侧同一道上限（20MB）：位图负载吃的是内存，不是磁盘。
        // 数字守不住（机器不同），但"必须有人挡"这件事守得住。
        var bytes = new byte[ClipboardPolicy.MaxImageBytes + 1];
        var (png, _, why) = await ClipboardImageStore.TryReadFileAsPngAsync(Write("big.png", bytes), CancellationToken.None);
        Assert.Null(png);
        Assert.Contains("超过", why, StringComparison.Ordinal);
        Assert.Contains("MB", why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryRowsUseTheSameHandOutAsLocalFiles()
    {
        // 两处必须给出同一份字节：历史行走名字名册（备份里回来的 Uri 可以是任意字符串），
        // 本机文件走路径。分岔的那一半是"卡片上点能复制、右键复制出来的却不是同一张"。
        var bgra = Screenshot(6, 4);
        var png = await ClipboardImageStore.EncodePngAsync(bgra, 6, 4, CancellationToken.None);
        var name = "2026-09-28_0915_deadbeef.png";
        var fullPath = ClipAssets.FullPathOf(name);
        Assert.NotNull(fullPath);
        File.WriteAllBytes(fullPath!, png!);
        try
        {
            var row = ImageRowNamed(name);
            Assert.True(ClipboardImageStore.TryReadEntryImage(row, out var fromEntry, out var f1, out var w1), w1 ?? "");
            var (fromFile, f2, w2) = await ClipboardImageStore.TryReadFileAsPngAsync(fullPath, CancellationToken.None);
            Assert.Equal(fromEntry, fromFile);
            Assert.Equal(f1.Bgra, f2.Bgra);
            Assert.Null(w2);
        }
        finally { ClipboardImageStore.TryDelete(name); }
    }

    // ────────── 造材料的小工具 ──────────

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static Item ImageRowNamed(string mainName) => ClipboardEntry.BuildImage(
        "clip-image:deadbeefdeadbeef",
        new ClipboardEntry.ImageMeta(mainName, "2026-09-28_0915_deadbeef_thumb.jpg", 6, 4, 1234),
        "Code",
        new DateTimeOffset(2026, 9, 28, 9, 15, 0, TimeSpan.FromHours(8)));

    /// <summary>一块"像截图"的像素：逐像素变化的 BGR，alpha 恒 255。</summary>
    private static byte[] Screenshot(int w, int h)
    {
        var p = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            p[i * 4] = (byte)(i * 7);
            p[i * 4 + 1] = (byte)(i * 13);
            p[i * 4 + 2] = (byte)(i * 29);
            p[i * 4 + 3] = 255;
        }
        return p;
    }

    private static byte[] Solid(int w, int h, byte r, byte g, byte b)
    {
        var p = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            p[i * 4] = b; p[i * 4 + 1] = g; p[i * 4 + 2] = r; p[i * 4 + 3] = 255;
        }
        return p;
    }

    private static byte[] ExpectedBgra(int w, int h, Func<int, int, (byte B, byte G, byte R)> px)
    {
        var p = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (b, g, r) = px(x, y);
                var at = (y * w + x) * 4;
                p[at] = b; p[at + 1] = g; p[at + 2] = r; p[at + 3] = 255;
            }
        return p;
    }

    /// <summary>
    /// 手写一个 24 位 BMP（<b>BGR、底朝上、每行补到 4 字节</b>）。
    /// <para>为什么在测试里现写而不用产品的编码器：要的正是一个"产品没参与生成"的真实文件——
    /// 用同一颗 helper 造材料再验自己，两边一起写错也照样全绿（#127 的那一课）。</para>
    /// </summary>
    private static byte[] Bmp24(int w, int h, Func<int, int, (byte B, byte G, byte R)> px)
    {
        var rowBytes = (w * 3 + 3) / 4 * 4;
        var data = rowBytes * h;
        var all = new byte[54 + data];
        all[0] = (byte)'B'; all[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(all.AsSpan(2), all.Length);
        BinaryPrimitives.WriteInt32LittleEndian(all.AsSpan(10), 54);          // 像素起点
        BinaryPrimitives.WriteInt32LittleEndian(all.AsSpan(14), 40);          // BITMAPINFOHEADER
        BinaryPrimitives.WriteInt32LittleEndian(all.AsSpan(18), w);
        BinaryPrimitives.WriteInt32LittleEndian(all.AsSpan(22), h);           // 正数＝底朝上
        BinaryPrimitives.WriteInt16LittleEndian(all.AsSpan(26), 1);           // planes
        BinaryPrimitives.WriteInt16LittleEndian(all.AsSpan(28), 24);          // bitcount
        BinaryPrimitives.WriteInt32LittleEndian(all.AsSpan(34), data);
        BinaryPrimitives.WriteInt32LittleEndian(all.AsSpan(38), 2835);        // ~72 DPI
        BinaryPrimitives.WriteInt32LittleEndian(all.AsSpan(42), 2835);
        for (var y = 0; y < h; y++)
        {
            var rowStart = 54 + (h - 1 - y) * rowBytes;                       // 第 0 行写在最后
            for (var x = 0; x < w; x++)
            {
                var (b, g, r) = px(x, y);
                var at = rowStart + x * 3;
                all[at] = b; all[at + 1] = g; all[at + 2] = r;
            }
        }
        return all;
    }
}
