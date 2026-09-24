#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 标注画到像素上之后的<b>逐像素</b>契约。
/// <para>
/// 为什么值得这样测：截图标注是"眼睛看才知道对不对"的东西，而这类需求最容易长成
/// "只有跑起来才发现问题"。把渲染做成可断言的纯像素运算之后，
/// 红蓝位序、轮廓还是实心、马赛克来回涂会不会花、荧光笔中段比两端浓——这些都能在这里红。
/// </para>
/// </summary>
public sealed class AnnotationPainterTests
{
    private const int BgB = 10, BgG = 20, BgR = 30;

    private static byte[] Canvas(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            pixels[i * 4] = BgB;
            pixels[i * 4 + 1] = BgG;
            pixels[i * 4 + 2] = BgR;
            pixels[i * 4 + 3] = 255;
        }
        return pixels;
    }

    private static (int B, int G, int R, int A) At(byte[] pixels, int width, int x, int y)
    {
        var p = (y * width + x) * 4;
        return (pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3]);
    }

    private static Annotation Shape(AnnotationTool tool, int colorBgra, params PixelPoint[] points)
        => new(tool, points, colorBgra, 4);

    /// <summary>一个"三个通道各不相同"的红：位序写反时 B/G/R 对不上号，所以刻意不用纯红。</summary>
    private static readonly int Red = Annotation.Opaque(0x11, 0x23, 0xFF);
    private static readonly PixelPoint P00 = new(0, 0);

    private static bool IsRed((int B, int G, int R, int A) pixel) => pixel is (0x11, 0x23, 0xFF, 255);
    private static bool IsBackground((int B, int G, int R, int A) pixel) => pixel is (BgB, BgG, BgR, 255);

    // ────────── 输入缓冲不许动 ──────────

    [Fact]
    public void RenderReturnsANewBufferAndLeavesTheSourceAlone()
    {
        // 预览要拿同一份底图反复重烤（撤销一条就整批重画）。底图被就地改过 ⇒ 再也回不到"什么都没画"。
        var source = Canvas(16, 16);
        var before = (byte[])source.Clone();
        var output = AnnotationPainter.Render(source, 16, 16, new[]
        {
            Shape(AnnotationTool.Rectangle, Red, new PixelPoint(2, 2), new PixelPoint(12, 12)),
        });
        Assert.NotSame(source, output);
        Assert.Equal(before, source);
        Assert.NotEqual(before, output);
    }

    [Fact]
    public void NoAnnotationsStillHandsBackACopyNotTheSameArray()
    {
        var source = Canvas(8, 8);
        var output = AnnotationPainter.Render(source, 8, 8, Array.Empty<Annotation>());
        Assert.NotSame(source, output);
        Assert.Equal(source, output);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(8, 0)]
    [InlineData(-4, 4)]
    public void AbsurdCanvasSizesAreRefused(int width, int height)
        => Assert.Contains("尺寸不合法", Assert.Throws<InvalidOperationException>(
            () => AnnotationPainter.Render(Canvas(4, 4), width, height, Array.Empty<Annotation>())).Message);

    [Fact]
    public void ShortBufferIsRefusedInsteadOfReadingPastTheEnd()
        => Assert.Throws<InvalidOperationException>(() => AnnotationPainter.Render(
            new byte[4 * 4 * 4 - 1], 4, 4, Array.Empty<Annotation>()));

    // ────────── 形状 ──────────

    [Fact]
    public void RectanglePaintsTheOutlineNotTheInside()
    {
        var pixels = AnnotationPainter.Render(Canvas(20, 20), 20, 20, new[]
        {
            Shape(AnnotationTool.Rectangle, Red, new PixelPoint(4, 4), new PixelPoint(15, 15)),
        });
        Assert.True(IsRed(At(pixels, 20, 9, 4)), "上边中点应当被画到");
        Assert.True(IsRed(At(pixels, 20, 4, 10)), "左边中点应当被画到");
        Assert.True(IsBackground(At(pixels, 20, 10, 10)), "框内必须还是原图：截图标注是画个圈，不是挖个洞");
        Assert.True(IsBackground(At(pixels, 20, 0, 0)), "框外必须不受影响");
    }

    [Fact]
    public void ChannelOrderSurvivesTheWholePath()
    {
        // 红蓝写反只有眼睛看得出来，所以这里按字节钉死：请求的是"蓝 0x11、绿 0x23、红 0xFF"
        var pixels = AnnotationPainter.Render(Canvas(12, 12), 12, 12, new[]
        {
            Shape(AnnotationTool.Line, Red, new PixelPoint(6, 0), new PixelPoint(6, 11)),
        });
        var hit = At(pixels, 12, 6, 6);
        Assert.Equal((0x11, 0x23, 0xFF, 255), hit);
    }

    [Fact]
    public void LineCoversBothEndsAndEverythingBetween()
    {
        var pixels = AnnotationPainter.Render(Canvas(21, 21), 21, 21, new[]
        {
            new Annotation(AnnotationTool.Line, new[] { new PixelPoint(0, 10), new PixelPoint(20, 10) }, Red, 2),
        });
        Assert.True(IsRed(At(pixels, 21, 0, 10)));
        Assert.True(IsRed(At(pixels, 21, 10, 10)));
        Assert.True(IsRed(At(pixels, 21, 20, 10)));
        Assert.True(IsBackground(At(pixels, 21, 10, 9)), "线宽 2 的笔宽是 1 像素，不该糊到邻行");
    }

    [Fact]
    public void ArrowIsStrictlyMoreInkThanTheSameShaftAsALine()
    {
        var points = new[] { new PixelPoint(2, 10), new PixelPoint(20, 10) };
        var line = AnnotationPainter.Render(Canvas(24, 21), 24, 21, new[] { Shape(AnnotationTool.Line, Red, points) });
        var arrow = AnnotationPainter.Render(Canvas(24, 21), 24, 21, new[] { Shape(AnnotationTool.Arrow, Red, points) });
        Assert.True(Ink(arrow) > Ink(line), "两翼没画出去的话，箭头就只是一条直线");
        static int Ink(byte[] pixels) => pixels.Chunk(4).Count(px => px[2] == 0xFF);
    }

    [Fact]
    public void EllipseIsAnOutlineToo()
    {
        var pixels = AnnotationPainter.Render(Canvas(41, 41), 41, 41, new[]
        {
            Shape(AnnotationTool.Ellipse, Red, new PixelPoint(5, 5), new PixelPoint(35, 35)),
        });
        Assert.True(IsRed(At(pixels, 41, 5, 20)), "椭圆左端点附近应有笔画");
        Assert.True(IsBackground(At(pixels, 41, 20, 20)), "中心必须空着");
    }

    [Fact]
    public void DegenerateArrowDoesNotThrow()
    {
        // 起点终点重合：杆没有方向，两翼更无从算起（这时 atan2(0,0) 参与画图会写出乱七八糟的翼）
        var pixels = AnnotationPainter.Render(Canvas(12, 12), 12, 12, new[]
        {
            Shape(AnnotationTool.Arrow, Red, new PixelPoint(6, 6), new PixelPoint(6, 6)),
        });
        Assert.True(IsRed(At(pixels, 12, 6, 6)));
    }

    // ────────── 形状的尺寸：按下点 → 放开点 ──────────

    /// <summary>
    /// 真机反馈的成因：<b>拖动过程中每一帧的鼠标位置都会被采进来</b>，于是一条矩形在历史里带着几十个点。
    /// 另一端若取"第二个元素"，那是按下后的第一次移动（离起点一两个像素），松手就看见一个针尖大的框；
    /// 而拖动中的预览取的是最后一点 ⇒ "预览对、落笔错"，正是最难自己发现的那一类。
    /// </summary>
    [Theory]
    [InlineData(AnnotationTool.Rectangle)]
    [InlineData(AnnotationTool.Ellipse)]
    [InlineData(AnnotationTool.Line)]
    [InlineData(AnnotationTool.Arrow)]
    public void ShapeSpansThePressPointToTheReleasePointNotTheFirstSample(AnnotationTool tool)
    {
        // 首点＝按下，末点＝放开，中间两个是拖过的痕迹：形状只能由首末两点决定
        var stroke = new[]
        {
            new PixelPoint(3, 4), new PixelPoint(4, 5), new PixelPoint(11, 9), new PixelPoint(24, 18),
        };
        var pixels = AnnotationPainter.Render(Canvas(30, 24), 30, 24, new[] { Shape(tool, Red, stroke) });
        var ink = RedBox(pixels, 30, 24);

        Assert.True(ink.X <= 4 && ink.Y <= 5, $"{Annotation.ToolName(tool)}：笔画没到按下的那一点（{ink.X},{ink.Y}）");
        Assert.True(ink.Right - 1 >= 20, $"{Annotation.ToolName(tool)} 只画到 x={ink.Right - 1}：图形被画小了（末端应在 24）");
        Assert.True(ink.Bottom - 1 >= 14, $"{Annotation.ToolName(tool)} 只画到 y={ink.Bottom - 1}：图形被画小了（末端应在 18）");
    }

    private static IntRect RedBox(byte[] pixels, int width, int height)
    {
        var minX = width; var minY = height; var maxX = -1; var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!IsRed(At(pixels, width, x, y))) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        Assert.True(maxX >= minX, "一个红像素都没有：这条标注根本没画出去");
        return new IntRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // ────────── 荧光笔：每像素只混合一次 ──────────

    [Fact]
    public void HighlighterBlendsExactlyOncePerPixel()
    {
        // 期望值是"灰 200 被浓度 0x70 的黄混合一次"手算出来的：
        //   通道 = (200*(255-112) + 源*112) / 255
        //   B = 28600/255 = 112   G = (28600+185*112)/255 = 193   R = (28600+255*112)/255 = 224
        // 连续圆点若各自都混合一次，中段会明显比两端浓 ⇒ 下面三条断言会同时红。
        var gray = Enumerable.Range(0, 33 * 21).SelectMany(_ => new byte[] { 200, 200, 200, 255 }).ToArray();
        var yellow = Annotation.WithAlpha(Annotation.Opaque(0x00, 0xB9, 0xFF), Annotation.HighlighterAlpha);
        AnnotationPainter.Paint(gray, 33, 21, new Annotation(AnnotationTool.Highlighter,
            new[] { new PixelPoint(2, 10), new PixelPoint(30, 10) }, yellow, Annotation.DefaultThickness(AnnotationTool.Highlighter)));

        Assert.Equal((112, 193, 224, 255), At(gray, 33, 3, 10));        // 笔画开头附近
        Assert.Equal((112, 193, 224, 255), At(gray, 33, 16, 10));       // 中段：被几十个圆点反复扫过
        Assert.Equal((112, 193, 224, 255), At(gray, 33, 29, 10));       // 收尾附近
        Assert.Equal((200, 200, 200, 255), At(gray, 33, 16, 2));        // 笔刷半径之外不受影响
    }

    // ────────── 马赛克 ──────────

    [Fact]
    public void MosaicTurnsATouchedBlockIntoOneFlatColour()
    {
        var pixels = Canvas(24, 24);
        for (var y = 0; y < 24; y++)
            for (var x = 0; x < 24; x += 2)
                pixels[(y * 24 + x) * 4 + 2] = 255;                 // 隔列竖条纹：一个格子里必然同时有两种红，平均色才算得准
        AnnotationPainter.Paint(pixels, 24, 24, new Annotation(AnnotationTool.Mosaic,
            new[] { new PixelPoint(2, 2), new PixelPoint(8, 8) }, Red, Annotation.DefaultThickness(AnnotationTool.Mosaic)));

        var first = At(pixels, 24, 0, 0);
        for (var y = 0; y < Annotation.MosaicBlockSize; y++)
            for (var x = 0; x < Annotation.MosaicBlockSize; x++)
                Assert.Equal(first, At(pixels, 24, x, y));           // 整块同色才叫马赛克
        Assert.Equal((BgB, BgG, 142, 255), first);   // (72*255 + 72*30)/144 = 142.5 → 截断成 142
        Assert.Equal((BgB, BgG, BgR, 255), At(pixels, 24, 23, 23));  // 没涂到的格子必须原样
    }

    /// <summary>点一下就走完的那一笔（按下即松开，两个采样点重合）：必须糊掉笔尖所在的整格。
    /// 否则"选中打码、点一下、什么也没发生"就成了静默失效（真机反馈的那一类）。</summary>
    [Fact]
    public void ATapWithTwoIdenticalPointsStillPixelatesTheBlockUnderIt()
    {
        var pixels = Canvas(24, 24);
        for (var y = 0; y < 24; y++)
            for (var x = 0; x < 24; x += 2)
                pixels[(y * 24 + x) * 4 + 2] = 255;                     // 隔列竖条纹：没糊掉的格子一定看得见两种红
        AnnotationPainter.Paint(pixels, 24, 24, new Annotation(AnnotationTool.Mosaic,
            new[] { new PixelPoint(5, 5), new PixelPoint(5, 5) }, Red, Annotation.DefaultThickness(AnnotationTool.Mosaic)));

        var first = At(pixels, 24, 0, 0);
        for (var y = 0; y < Annotation.MosaicBlockSize; y++)
            for (var x = 0; x < Annotation.MosaicBlockSize; x++)
                Assert.Equal(first, At(pixels, 24, x, y));              // 第 0 格整块同色
        Assert.NotEqual(first, At(pixels, 24, 22, 22));                  // 没涂到的那一格必须还是条纹
    }

    [Fact]
    public void MosaicBrushedTwiceIsTheSameImage()
    {
        // 格子若跟着笔尖走，来回涂同一处会错开半格 → 花脸。对齐固定网格就是为了让这条成立。
        var stroke = new Annotation(AnnotationTool.Mosaic,
            new[] { new PixelPoint(3, 3), new PixelPoint(20, 14), new PixelPoint(6, 9) }, Red,
            Annotation.DefaultThickness(AnnotationTool.Mosaic));
        var once = AnnotationPainter.Render(Canvas(32, 24), 32, 24, new[] { stroke });
        var twice = AnnotationPainter.Render(Canvas(32, 24), 32, 24, new[] { stroke, stroke });
        Assert.Equal(once, twice);
    }

    // ────────── 越界 ──────────

    [Fact]
    public void ShapesEntirelyOutsideTheCanvasChangeNothing()
    {
        var source = Canvas(12, 12);
        var output = AnnotationPainter.Render(source, 12, 12, new[]
        {
            Shape(AnnotationTool.Rectangle, Red, new PixelPoint(-50, -50), new PixelPoint(-10, -10)),
            Shape(AnnotationTool.Line, Red, new PixelPoint(40, 40), new PixelPoint(60, 60)),
        });
        Assert.Equal(source, output);
    }

    [Fact]
    public void ShapesHalfOutsideAreClippedInsteadOfCrashing()
    {
        var pixels = AnnotationPainter.Render(Canvas(12, 12), 12, 12, new[]
        {
            Shape(AnnotationTool.Line, Red, new PixelPoint(-30, 6), new PixelPoint(30, 6)),
        });
        Assert.True(IsRed(At(pixels, 12, 0, 6)));
        Assert.True(IsRed(At(pixels, 12, 11, 6)));
        Assert.True(IsBackground(At(pixels, 12, 6, 0)));
    }

    // ────────── 文字（真的走 GDI） ──────────

    [Fact]
    public void TextPutsInkOnTheBufferWithoutFillingTheBox()
    {
        var pixels = Canvas(160, 48);
        AnnotationPainter.Paint(pixels, 160, 48, new Annotation(AnnotationTool.Text,
            new[] { new PixelPoint(6, 8) }, Red, 4) { Text = "中文字ABC", FontHeight = 26 });

        var ink = pixels.Chunk(4).Count(px => px[2] > 128 && px[0] < 128);
        Assert.True(ink > 200, "一行 26px 的字不可能只落不到 200 个像素——文字这条臂没跑通");
        Assert.True(ink < 160 * 48 * 0.4, "GDI 顺手把整块背景刷掉了（SetBkMode 没生效）");
        // GDI 写字会把它碰过的像素 alpha 清成 0：不补回来，存出去的 PNG 里"字"就是一串透明洞
        for (var p = 3; p < pixels.Length; p += 4) Assert.Equal(255, pixels[p]);
    }

    [Fact]
    public void TextBelowTheBottomEdgeIsClippedNotWrittenOutOfBounds()
    {
        var source = Canvas(64, 32);
        var pixels = AnnotationPainter.Render(source, 64, 32, new[]
        {
            new Annotation(AnnotationTool.Text, new[] { new PixelPoint(4, 300) }, Red, 4) { Text = "画不到的字", FontHeight = 24 },
        });
        Assert.Equal(source, pixels);
    }

    [Fact]
    public void PaintRefusesAnUndrawableAnnotationWithTheModelsReason()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AnnotationPainter.Paint(
            Canvas(8, 8), 8, 8, new Annotation(AnnotationTool.Rectangle, new[] { P00 }, Red, 4)));
        Assert.Contains("矩形", ex.Message);
        Assert.Contains("2 个点", ex.Message);
    }

    [Fact]
    public void AnnotationsAreAppliedInOrderSoLaterOnesWin()
    {
        // 撤销＝从列表末尾弹掉，靠的就是"后画的盖在先画的上面"这个顺序语义
        var pixels = AnnotationPainter.Render(Canvas(12, 12), 12, 12, new[]
        {
            Shape(AnnotationTool.Line, Annotation.Opaque(0x00, 0xFF, 0x00), new PixelPoint(0, 6), new PixelPoint(11, 6)),   // 绿
            Shape(AnnotationTool.Line, Red, new PixelPoint(6, 0), new PixelPoint(6, 11)),          // 红竖线压在中间
        });
        Assert.Equal((0x00, 0xFF, 0x00, 255), At(pixels, 12, 0, 6));
        Assert.True(IsRed(At(pixels, 12, 6, 6)));
    }
    /// <summary>折线＝顶点是点出来的多段线：每一段都得连上，中间断一段就是"圈了一半"，
    /// 而它看起来像画完了（撤销列表里也只有一条）。</summary>
    [Fact]
    public void PolyLineConnectsEverySegmentNotJustTheEnds()
    {
        var points = new[] { new PixelPoint(2, 18), new PixelPoint(9, 3), new PixelPoint(17, 15) };
        var pixels = AnnotationPainter.Render(Canvas(22, 22), 22, 22,
            new[] { Shape(AnnotationTool.PolyLine, Red, points) });

        Assert.True(IsRed(At(pixels, 22, 2, 18)), "起点必须有笔画");
        Assert.True(IsRed(At(pixels, 22, 17, 15)), "终点必须有笔画");
        Assert.True(IsRed(At(pixels, 22, 9, 3)), "中间顶点必须有笔画（只连首尾就会绕过它）");
        Assert.True(IsRed(At(pixels, 22, 5, 11)), "第一段中点必须有笔画");
        Assert.True(IsRed(At(pixels, 22, 13, 9)), "第二段中点必须有笔画");
        Assert.True(IsBackground(At(pixels, 22, 13, 19)), "折线不是填充：拐角外侧不该被糊上");
    }

    /// <summary>折线只点了一下（一个顶点）时不是一条线：判据必须说不画并给原因，
    /// 而不是画出一个孤点让用户以为"这工具坏了"。</summary>
    [Fact]
    public void ASingleVertexPolyLineIsRefusedWithTheModelsReason()
    {
        var one = new Annotation(AnnotationTool.PolyLine, new[] { new PixelPoint(6, 6) }, Red, 4);
        Assert.Contains("折线", one.Problem());
        Assert.Contains("2 个点", one.Problem());
    }
}
