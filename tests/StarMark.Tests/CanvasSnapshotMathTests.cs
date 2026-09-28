#nullable enable
using System;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using StarMark.Integrations.Canvas;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 S4-⑤「Board 快照成贴」的三条几何判据（<see cref="CanvasSnapshotMath"/>）。
/// <para>
/// 这一格的用户可见结论只有一句：<b>贴出来的是我写的那一块，不是整块屏幕</b>。
/// 而"哪一块"这件事在像素上才算得准——笔迹的包围盒记账（<c>Composited</c>、<c>Trail.LiveBounds</c>）
/// 少算一条就少一块（批次 WO/WK 两次的同族症状），所以这里读的是**合成完的那块墨缓冲本身**。
/// 于是三条判据都能机检：什么算有墨、留边之后怎么夹、怎么从帧里取子矩形。
/// </para>
/// </summary>
public sealed class CanvasSnapshotMathTests
{
    /// <summary>一个"看得见"的墨点（预乘：通道 ≤ alpha，这里取纯白不透明）。</summary>
    private const uint Ink = 0xFFFFFFFFu;

    private static uint[] Blank(int width, int height)
    {
        var buffer = new uint[width * height];
        Array.Fill(buffer, LayeredCanvasWindow.BlankPixel);
        return buffer;
    }

    // ────────── InkRegionOf：什么算"有墨" ──────────

    [Fact]
    public void ABoardWithNothingOnItHasNoRegion()
    {
        var region = CanvasSnapshotMath.InkRegionOf(Blank(8, 6), 8, 6);
        Assert.True(region.IsEmpty);
    }

    [Fact]
    public void ASingleDotIsItsOwnRegion()
    {
        var ink = Blank(8, 6);
        ink[2 * 8 + 5] = Ink;                                     // x=5, y=2
        Assert.Equal(new IntRect(5, 2, 1, 1), CanvasSnapshotMath.InkRegionOf(ink, 8, 6));
    }

    [Fact]
    public void TheRegionIsTightAroundTheTwoFarthestDots()
    {
        var ink = Blank(10, 8);
        ink[1 * 10 + 2] = Ink;                                    // x=2, y=1
        ink[6 * 10 + 9] = Ink;                                    // x=9, y=6（右下角那个像素）
        Assert.Equal(new IntRect(2, 1, 8, 6), CanvasSnapshotMath.InkRegionOf(ink, 10, 8));
    }

    /// <summary>
    /// 判据是"alpha 高过空白位"，不是"与空白位不相等"。
    /// <para>橡皮按比例减 alpha，擦到底会留下 <b>alpha=0</b>（Array.Clear 的 0 也是这一档）：它看不见，
    /// 按"不等"判就算有墨，裁出来的块白长一圈；alpha=1 是空白位本身；alpha=2 才第一次真正盖上一层。</para>
    /// </summary>
    [Theory]
    [InlineData(0u, false)]                                       // 被擦到见底／整块清零：看不见
    [InlineData(LayeredCanvasWindow.BlankPixel, false)]            // 空白位本身：看不见但点得着
    [InlineData(0x02000000u, true)]                                // 刚刚盖上一层：算有墨
    public void OnlyPixelsThatCoverSomethingCountAsInk(uint pixel, bool isInk)
    {
        var ink = Blank(4, 4);
        ink[1 * 4 + 1] = pixel;
        var region = CanvasSnapshotMath.InkRegionOf(ink, 4, 4);
        Assert.Equal(isInk, !region.IsEmpty);
        if (isInk) Assert.Equal(new IntRect(1, 1, 1, 1), region);
    }

    /// <summary>颜色通道为 0 但 alpha 高过空白位（一层极淡的墨）仍要算数：预乘缓冲里 alpha 才是"盖了多少"。</summary>
    [Fact]
    public void AlphaIsTheQuestionNotTheColour()
    {
        var ink = Blank(4, 4);
        ink[3 * 4 + 0] = 0x40000000u;                             // BGR 全 0、alpha=64
        Assert.Equal(new IntRect(0, 3, 1, 1), CanvasSnapshotMath.InkRegionOf(ink, 4, 4));
    }

    // ────────── FrameOf：留边与夹取 ──────────

    [Fact]
    public void PaddingGrowsEverySideByTheSameAmount()
        => Assert.Equal(new IntRect(46, 46, 18, 18),
            CanvasSnapshotMath.FrameOf(new IntRect(50, 50, 10, 10), 200, 200, 4));

    /// <summary>墨贴到画面边缘时，边不能把裁块推出画面——否则 <see cref="CropBgra"/> 会当场拒绝，一句"贴图失败"。</summary>
    [Theory]
    [InlineData(0, 0)]                                                 // 左上角
    [InlineData(190, 0)]                                               // 右上角（10 宽正好贴边）
    [InlineData(0, 190)]                                               // 左下角
    [InlineData(190, 190)]                                             // 右下角
    public void PaddingAtTheEdgeIsClampedInsideTheFrame(int x, int y)
    {
        var region = CanvasSnapshotMath.FrameOf(new IntRect(x, y, 10, 10), 200, 200, 24);
        Assert.True(region.X >= 0 && region.Y >= 0);
        Assert.True(region.Right <= 200 && region.Bottom <= 200);
    }

    /// <summary>
    /// 边比整幅画面还大时不许抛，也不许返回越界的矩形。<c>Math.Clamp</c> 在这种输入下界限会颠倒并当场抛
    /// （批次 WQ 那条同一个形状），所以这里走 <see cref="CanvasCompositor.Clamp"/>。
    /// </summary>
    [Fact]
    public void AWholeFramePadIsTheWholeFrameNotAnException()
        => Assert.Equal(new IntRect(0, 0, 30, 20),
            CanvasSnapshotMath.FrameOf(new IntRect(5, 5, 4, 4), 30, 20, 10_000));

    [Fact]
    public void AnEmptyRegionStaysEmpty()
        => Assert.True(CanvasSnapshotMath.FrameOf(CanvasCompositor.Nothing, 100, 100, 24).IsEmpty);

    /// <summary>已经占满整幅的墨（比如贴到四角都画过）加边之后还是整幅——不许多一圈越界的像素。</summary>
    [Fact]
    public void AWholeFrameRegionStaysTheWholeFrame()
        => Assert.Equal(new IntRect(0, 0, 100, 100),
            CanvasSnapshotMath.FrameOf(new IntRect(0, 0, 100, 100), 100, 100, 24));

    // ────────── CropBgra：从帧里取子矩形 ──────────

    /// <summary>每像素一个可辨认的值（B=R=序号，G=行号），这样"步长用错"必然表现成错位的字节。</summary>
    private static byte[] Frame(int width, int height)
    {
        var frame = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                frame[i] = (byte)(y * width + x);
                frame[i + 1] = (byte)y;
                frame[i + 2] = (byte)x;
                frame[i + 3] = 255;
            }
        return frame;
    }

    /// <summary>
    /// <b>源步长是帧宽、目标步长是裁块宽</b>。把源步长写成裁块宽的话，
    /// 只有 x≠0 的裁块才会错位——而贴图的裁块几乎永远 x≠0，1× 时看起来"差不多对"，
    /// 这正是"逐像素比对源子矩形"而不是"看个形状"的理由。
    /// </summary>
    [Fact]
    public void TheCropUsesTheFrameStrideForTheSourceRow()
    {
        const int width = 6, height = 4;
        var frame = Frame(width, height);
        var rect = new IntRect(2, 1, 3, 2);
        var crop = CanvasSnapshotMath.CropBgra(frame, width, height, rect);

        Assert.Equal(rect.Width * rect.Height * 4, crop.Length);
        for (var y = 0; y < rect.Height; y++)
            for (var x = 0; x < rect.Width; x++)
                for (var channel = 0; channel < 4; channel++)
                    Assert.Equal(
                        frame[((rect.Y + y) * width + rect.X + x) * 4 + channel],
                        crop[(y * rect.Width + x) * 4 + channel]);
    }

    [Fact]
    public void CroppingTheWholeFrameChangesNothing()
        => Assert.Equal(Frame(5, 3), CanvasSnapshotMath.CropBgra(Frame(5, 3), 5, 3, new IntRect(0, 0, 5, 3)));

    [Fact]
    public void ARectangleOutsideTheFrameIsRefusedInsteadOfQuietlyShrunk()
    {
        var frame = Frame(4, 4);
        Assert.Throws<ArgumentException>(() => CanvasSnapshotMath.CropBgra(frame, 4, 4, new IntRect(2, 2, 4, 1)));
        Assert.Throws<ArgumentException>(() => CanvasSnapshotMath.CropBgra(frame, 4, 4, new IntRect(-1, 0, 2, 2)));
    }

    [Fact]
    public void AnEmptyRectangleYieldsNoBytes()
        => Assert.Empty(CanvasSnapshotMath.CropBgra(Frame(4, 4), 4, 4, new IntRect(1, 1, 0, 5)));

    /// <summary>
    /// 留边的常数住在 Core、按 <b>DIP</b> 说话：UI 侧要乘这块屏的缩放。
    /// 这里钉住"它是 DIP 量级的一个数"（写死物理像素＝150% 屏上边距缩水一半，与幕布亮区同一条纪律）。
    /// </summary>
    [Fact]
    public void ThePaddingIsStatedInDipAndIsAPositiveMargin()
        => Assert.InRange(CanvasSnapshotMath.PaddingDip, 8d, 64d);
}
