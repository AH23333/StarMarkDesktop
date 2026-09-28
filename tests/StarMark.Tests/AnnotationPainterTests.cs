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

    /// <summary>隔列竖条纹：一个格子里必然同时有两种红 ⇒ "糊过"与"没糊"用肉眼和断言都分得开。
    /// 纯平底色上打码等于什么都没改（平均色就是它自己），拿它做锚点会假绿。</summary>
    private static byte[] StripedCanvas(int width, int height)
    {
        var pixels = Canvas(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x += 2)
                pixels[(y * width + x) * 4 + 2] = 255;
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

    // ────────── 一档"粗细"到底画多宽：预览与提交共用一个出处（批次 XU） ──────────

    /// <summary>
    /// 三档"粗细"<b>画出去的宽度</b>必须等于 <see cref="Annotation.InkWidth"/>，一个像素都不许差。
    /// <para>起因是真机反馈"预览比真图粗"：拖动中的预览按档位值当宽度，而落笔按半径 <c>(T-1)/2</c>
    /// 盖圆点，两边各算一遍就一定有得差。修法不是把两边凑成同一个数，而是<b>让两边问同一个出处</b>——
    /// 这里就是把那个出处钉在像素上：整数除法让截图那三档 {2,4,8} 实际是 {1,3,7}，
    /// 那是这条链一直的观感（改它等于改所有已存贴图），不许被"顺手对齐成偶数"。</para>
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(15)]
    public void TheCommittedWidthIsTheInkWidth(int thickness)
    {
        const int side = 25;
        var centre = new PixelPoint(12, 12);
        // 两个重合的点＝"原地按一下"：圆盘只盖一次，外接框正好就是那一笔的宽与高
        var pixels = AnnotationPainter.Render(Canvas(side, side), side, side,
            new[] { new Annotation(AnnotationTool.Line, new[] { centre, centre }, Red, thickness) });
        var box = PaintedBox(pixels, side);
        Assert.True(box.Width > 0 && box.Height > 0, $"厚度 {thickness} 一个像素都没画出去");
        Assert.Equal(Annotation.InkWidth(thickness), box.Width);
        Assert.Equal(Annotation.InkWidth(thickness), box.Height);
    }

    /// <summary>已画区域的外接框（行、列各自取极值，不做"它一定对称"的假设）。</summary>
    private static StarMark.Abstractions.Capture.IntRect PaintedBox(byte[] pixels, int side)
    {
        var x1 = int.MaxValue;
        var y1 = int.MaxValue;
        var x2 = -1;
        var y2 = -1;
        for (var y = 0; y < side; y++)
            for (var x = 0; x < side; x++)
            {
                if (IsBackground(At(pixels, side, x, y))) continue;
                x1 = Math.Min(x1, x);
                x2 = Math.Max(x2, x);
                y1 = Math.Min(y1, y);
                y2 = Math.Max(y2, y);
            }
        return new StarMark.Abstractions.Capture.IntRect(x1, y1, x2 - x1 + 1, y2 - y1 + 1);
    }

    // ────────── 序号：那一档"粗细"必须落在像素上（批次 WR） ──────────

    private static int PaintedPixels(Annotation mark, int side)
    {
        var pixels = AnnotationPainter.Render(Canvas(side, side), side, side, new[] { mark });
        var painted = 0;
        for (var y = 0; y < side; y++)
            for (var x = 0; x < side; x++)
                if (!IsBackground(At(pixels, side, x, y))) painted++;
        return painted;
    }

    /// <summary>
    /// 三档必须画出<b>三种大小</b>。写死半径的那一版三档像素数完全相等——
    /// 那就是用户报的"序号功能更改粗细无效果"，而它在源码里长得和"正常"一模一样，只有数像素才分得开。
    /// </summary>
    [Fact]
    public void AHeavierNumberPaintsABiggerDot()
    {
        var counts = Annotation.NumberRadii
            .Select(radius => PaintedPixels(new Annotation(AnnotationTool.Number,
                new[] { new PixelPoint(30, 30) }, Red, radius) { Number = 7 }, 61))
            .ToList();

        Assert.True(counts[0] < counts[1] && counts[1] < counts[2],
            $"细／中／粗 = {counts[0]}／{counts[1]}／{counts[2]} 像素，必须一档比一档大");
        // 中档钉在这条链一直以来的那颗点：半径 15 的实心盘 ≈ π·15² ≈ 707（编号那几笔白像素也算进来）
        Assert.InRange(counts[1], 700, 800);
    }

    /// <summary>序号圆点是<b>实心</b>底色盘＋白字，圆外一格不许动（脏矩形与"擦干净"都靠这个前提）。</summary>
    [Fact]
    public void TheNumberDotIsSolidInsideAndUntouchedOutside()
    {
        var pixels = AnnotationPainter.Render(Canvas(61, 61), 61, 61, new[]
        {
            new Annotation(AnnotationTool.Number, new[] { new PixelPoint(30, 30) }, Red, 22) { Number = 1 },
        });

        Assert.True(IsRed(At(pixels, 61, 11, 30)), "圆内偏左（编号之外）必须是实心底色");
        Assert.True(IsRed(At(pixels, 61, 45, 45)), "对角 15+15≈21.2 还在半径内");
        Assert.True(IsBackground(At(pixels, 61, 46, 46)), "对角 16+16≈22.6 已在半径外：一像素都不许多涂");
        Assert.True(IsRed(At(pixels, 61, 52, 30)), "x=圆心+22 是扫描线的最后一格（含端点），该画到");
        Assert.True(IsBackground(At(pixels, 61, 53, 30)), "再外一像素就不许动了");
    }

    /// <summary>
    /// 缩到底的那一颗<b>必须真的有像素落出去</b>（批次 WT 的症状就是"画不出"：图上什么都没有，
    /// 而列表里那条还在）。这里数的是像素，不是源码形状——上一版"半径不足 4 就 return"在源码里
    /// 长得和正常一模一样，只有数像素才分得开（同 <see cref="AHeavierNumberPaintsABiggerDot"/>）。
    /// </summary>
    [Fact]
    public void ANumberShrunkToTheMinimumIsStillPainted()
    {
        const int side = 61;
        var tiny = new Annotation(AnnotationTool.Number, new[] { new PixelPoint(30, 30) }, Red, 11)
        { Number = 1, Scale = Annotation.MinScale };
        var pixels = AnnotationPainter.Render(Canvas(side, side), side, side, new[] { tiny });

        Assert.False(IsBackground(At(pixels, side, 30, 30)), "圆心要有东西");
        Assert.False(IsBackground(At(pixels, side, 30 + Annotation.MinNumberRadius - 1, 30)), "下限半径之内要有东西");
        Assert.True(IsBackground(At(pixels, side, 30 + Annotation.MinNumberRadius + 1, 30)), "半径外一像素都不许动");
        Assert.True(PaintedPixels(tiny, side) >= 100,
            $"半径 {Annotation.MinNumberRadius} 的实心盘≈113 像素，数出 0 就是那颗序号又消失了");
    }

    /// <summary>
    /// 一个点都没有的序号<b>走不到绘制端</b>：`Paint` 第一句就问 `Problem()`，那种输入直接抛原因
    /// （"画不出去时抛原因"那条契约在这里）。这条测钉的是"上游挡死了"，
    /// 所以 <c>DrawNumber</c> 里不需要再自带一道"框宽为 0 就不画"——WT 一度补了这么一句，
    /// 顺着调用链查实后被删（坑表 #145）。
    /// </summary>
    [Fact]
    public void APointlessNumberIsRefusedBeforeAnyInk()
    {
        const int side = 61;
        var canvas = Canvas(side, side);
        var empty = new Annotation(AnnotationTool.Number, Array.Empty<PixelPoint>(), Red, 15) { Number = 1 };

        var ex = Assert.Throws<InvalidOperationException>(() => AnnotationPainter.Paint(canvas, side, side, empty));
        Assert.Contains("至少需要 1 个点", ex.Message);
        Assert.True(IsBackground(At(canvas, side, 0, 0)), "原点不许落一颗没有归属的墨");
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

    /// <summary>
    /// <b>拖动中的"一段一段补"必须与松手后"整条一次烤"给出同一张图。</b>
    /// <para>这是增量预览唯一能成立的前提：预览为了跟手不再每帧从底图重烤整张，
    /// 而是拿上一张合成图当起点、每帧只补新走过的那一段。若格子会因"来过几次"而不同，
    /// 用户就会看到"松手的瞬间画面跳了一下"——预览与交付是两套口径（本仓库吃过这类亏）。</para>
    /// <para>刻意按真实输入的形状构造：段与段<b>共用端点</b>、并且有一段会回头穿过已涂过的格子，
    /// 这样"重复涂同一格"这条路径真的被跑到，而不是被一个不相交的样例绕开。</para>
    /// </summary>
    [Fact]
    public void MosaicPaintedSegmentBySegmentEqualsOneShotStroke()
    {
        const int width = 48, height = 32;
        var thickness = Annotation.DefaultThickness(AnnotationTool.Mosaic);
        var points = new[]
        {
            new PixelPoint(4, 5), new PixelPoint(30, 12), new PixelPoint(12, 26), new PixelPoint(34, 9),
        };
        var whole = AnnotationPainter.Render(StripedCanvas(width, height), width, height,
            new[] { new Annotation(AnnotationTool.Mosaic, points, Red, thickness) });

        // 模拟拖动：起点先按"一个点画两遍"涂一次（与 BeginStroke 同一口径），之后逐段接上
        var stepwise = StripedCanvas(width, height);
        for (var i = 0; i < points.Length - 1; i++)
            AnnotationPainter.Paint(stepwise, width, height,
                new Annotation(AnnotationTool.Mosaic, new[] { points[i], points[i + 1] }, Red, thickness));
        AnnotationPainter.Paint(stepwise, width, height,
            new Annotation(AnnotationTool.Mosaic, new[] { points[0], points[0] }, Red, thickness));

        Assert.Equal(whole, stepwise);
        // 锚点：这条线真的涂到了东西（纯平底色上"什么都没变"也会让相等断言假绿）
        Assert.NotEqual(At(stepwise, width, 30, 12), At(StripedCanvas(width, height), width, 30, 12));
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

    // ────────── 批次 RF-2：文字旋转 ──────────

    /// <summary>
    /// 转 90° 的那一行字要<b>站起来</b>，而且转出去之后<b>只有墨被搬过去</b>。
    /// <para>旋转那条路的实现是"字模→覆盖率图→逐像素混合"，最容易犯的错是把整块方方的字模
    /// 连着背景一起搬过去——那会在字周围留一片脏斑。这里两头都钉：字形方向要换，
    /// 改动的像素数要远小于整块字模的面积（真脏斑会接近填满）。</para>
    /// </summary>
    [Fact]
    public void ARotatedLineOfTextStandsUpAndCarriesNoBackgroundWithIt()
    {
        var text = new Annotation(AnnotationTool.Text, new[] { new PixelPoint(40, 70) }, Red, 4)
        { Text = "MMMMMMMMMM", FontHeight = 30 };
        var blank = Canvas(220, 220);

        var flat = (byte[])blank.Clone();
        AnnotationPainter.Paint(flat, 220, 220, text);
        var lying = InkBox(blank, flat, 220, 220);

        var turned = (byte[])blank.Clone();
        AnnotationPainter.Paint(turned, 220, 220, text.RotatedBy(90));
        var standing = InkBox(blank, turned, 220, 220);

        Assert.True(lying.Width > 60 && lying.Height is > 5 and < 45,
            $"不旋转时该是横着长长的一条：{lying}");
        Assert.True(standing.Height > 60 && standing.Width is > 5 and < 45,
            $"转 90° 之后该竖起来（旋转没生效／只搬了字模＝这一条会红）：{standing}");
        Assert.True(standing.Count < lying.Width * lying.Height,
            $"改动像素数（{standing.Count}）不该接近整块字模的面积：连背景一起搬＝脏斑");
    }

    /// <summary>
    /// 转出去的字，<b>每一滴墨都落在 Bounds 里</b>。
    /// <para>这条直接对着真机反馈"文字显著脱离文字框范围内"：选择框与命中测试都读 <c>Bounds()</c>，
    /// 而它在批次 RF-2 之前只报未旋转的宽高——字转出去了、框还横在原地，
    /// 用户点字点不中、拖框又拖到空处。</para>
    /// </summary>
    [Theory]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(150)]
    [InlineData(270)]
    public void RotatedTextBoundsCoverEveryPixelItPaints(double angle)
    {
        var text = new Annotation(AnnotationTool.Text, new[] { new PixelPoint(60, 90) }, Red, 4)
        { Text = "旋转我 turn me", FontHeight = 28 };
        var blank = Canvas(260, 260);
        var turned = text.RotatedBy(angle);
        var painted = (byte[])blank.Clone();
        AnnotationPainter.Paint(painted, 260, 260, turned);

        var ink = InkBox(blank, painted, 260, 260);
        Assert.True(ink.Count > 60, $"这一转总得画上字（改动 {ink.Count} 个像素＝多半什么都没画）");
        var box = turned.Bounds();
        Assert.True(ink.Left >= box.X && ink.Top >= box.Y
            && ink.Right <= box.Right - 1 && ink.Bottom <= box.Bottom - 1,
            $"墨跑出框了：ink {ink} vs bounds {box}（角度 {angle}）");
    }

    /// <summary>
    /// 抓着一头改大小，<b>另一头画出来的墨必须一格都不动</b>。
    /// <para>真机反馈"缩放文字后，位置与文字框都偏移了"修在模型的轴点上；但轴点这件事最后成不成立，
    /// 看的是字有没有真的挪。只量 <c>Bounds()</c> 是不够的——那个框和轴点共用一套式子，
    /// 轴点写错时它会跟着一起错（"框与字一起跑"就又是下一次反馈）。所以从像素那一头验：
    /// 钉住右下 ⇒ 墨的右边界与下边界不许动，左与上必须长出去。</para>
    /// </summary>
    [Fact]
    public void ScalingTextFromOneEndLeavesTheOtherEndsInkWhereItWas()
    {
        const string line = "拖着左上角放大这一行";
        var mark = new Annotation(AnnotationTool.Text, new[] { new PixelPoint(150, 140) }, Red, 4)
        { Text = line, FontHeight = 26 };
        var blank = Canvas(560, 320);

        var beforeBuffer = (byte[])blank.Clone();
        AnnotationPainter.Paint(beforeBuffer, 560, 320, mark);
        var before = InkBox(blank, beforeBuffer, 560, 320);

        var box = mark.Bounds();
        var grown = mark.WithScalePivotTowards(new PixelPoint(box.X, box.Y)).ScaledBy(1.5);   // 抓左上 ⇒ 钉右下
        var afterBuffer = (byte[])blank.Clone();
        AnnotationPainter.Paint(afterBuffer, 560, 320, grown);
        var after = InkBox(blank, afterBuffer, 560, 320);

        Assert.True(after.Width > before.Width && after.Height > before.Height,
            $"放大就该墨迹变大：{before.Width}x{before.Height} → {after.Width}x{after.Height}");
        Assert.True(Math.Abs(after.Right - before.Right) <= 1, $"右端自己动了：{before.Right} → {after.Right}");
        Assert.True(Math.Abs(after.Bottom - before.Bottom) <= 1, $"下端自己动了：{before.Bottom} → {after.Bottom}");
        Assert.True(after.Left < before.Left && after.Top < before.Top,
            $"被抓的那一头该往外长：({before.Left},{before.Top}) → ({after.Left},{after.Top})");
        // 字与它的框还得继续重合（这条挡住"墨钉住了、框却还按中心算"那种分岔）
        var bounds = grown.Bounds();
        Assert.True(after.Left >= bounds.X && after.Top >= bounds.Y
            && after.Right <= bounds.Right - 1 && after.Bottom <= bounds.Bottom - 1,
            $"缩放后墨跑出框了：ink {after} vs bounds {bounds}");
    }

    /// <summary>
    /// 编辑框里按 Enter 换的行，画出来必须<em>真的</em>占一行：量的行高与画的行距必须是同一个数。
    /// <para>这一条钉的是"框比字矮一行"那一类——行高用估的（字高、字号、行数各乘一下）就会偏，
    /// 而偏了之后第二行的字落在选择框外面：点它点不中，拖动它也拖不走。</para>
    /// </summary>
    [Fact]
    public void ATwoLineTextPaintsBothLinesInsideItsOwnBounds()
    {
        var mark = new Annotation(AnnotationTool.Text, new[] { new PixelPoint(20, 20) }, Red, 4)
        { Text = "上行 Aaaa\n下行 Bbbbbbb", FontHeight = 26 };
        var blank = Canvas(420, 220);
        var painted = (byte[])blank.Clone();
        AnnotationPainter.Paint(painted, 420, 220, mark);

        var ink = InkBox(blank, painted, 420, 220);
        var box = mark.Bounds();
        var line = StarMark.Integrations.Capture.GdiTextDrawer.Measure("上行 Aaaa", 26).Height;

        // 墨不会铺满两格（行高里含行距空白），但必须明显超过一格——那才说明第二行真的另起了一行
        Assert.True(ink.Height > line, $"第二行没另起一行：两行墨只有 {ink.Height}，单行高 {line}");
        Assert.True(ink.Left >= box.X && ink.Top >= box.Y
            && ink.Right <= box.Right - 1 && ink.Bottom <= box.Bottom - 1,
            $"两行的墨跑出框了：ink {ink} vs bounds {box}");
    }

    /// <summary>转过角度的多行：整块字模（含两行）按覆盖率铺回去，墨仍然一滴都不许出框。</summary>
    [Theory]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(200)]
    public void ATwoLineTextStillStaysInsideItsBoundsWhenTurned(double angle)
    {
        var mark = new Annotation(AnnotationTool.Text, new[] { new PixelPoint(60, 60) }, Red, 4)
        { Text = "上行 Aaaa" + '\n' + "下行 Bbbbbbb", FontHeight = 26 }.RotatedBy(angle);
        var blank = Canvas(420, 420);
        var painted = (byte[])blank.Clone();
        AnnotationPainter.Paint(painted, 420, 420, mark);

        var ink = InkBox(blank, painted, 420, 420);
        var box = mark.Bounds();
        // 转起来之后"两行"落在哪个方向都会变，所以这里数墨不数量边界：
        // 只画第一行的实现，墨量会明显少于一行半——那条正是这条断言要抓的。
        var oneLine = new Annotation(AnnotationTool.Text, new[] { new PixelPoint(60, 60) }, Red, 4)
        { Text = "上行 Aaaa", FontHeight = 26 }.RotatedBy(angle);
        var singleBuffer = (byte[])blank.Clone();
        AnnotationPainter.Paint(singleBuffer, 420, 420, oneLine);
        var single = InkBox(blank, singleBuffer, 420, 420);

        Assert.True(ink.Count > 120, $"这一转总得画上字（只画了 {ink.Count} 个像素）");
        Assert.True(ink.Count > single.Count * 3 / 2,
            $"转 {angle:0}° 时第二行没画出去：两行墨 {ink.Count}，一行墨 {single.Count}");
        Assert.True(ink.Left >= box.X && ink.Top >= box.Y
            && ink.Right <= box.Right - 1 && ink.Bottom <= box.Bottom - 1,
            $"转 {angle:0}° 后两行的墨跑出框了：ink {ink} vs bounds {box}");
    }

    /// <summary>改动过的像素的外接框与数量（"画了多少"与"画在哪儿"两件事一起看）。</summary>
    private static (int Left, int Top, int Right, int Bottom, int Width, int Height, int Count)
        InkBox(byte[] before, byte[] after, int width, int height)
    {
        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = -1;
        var bottom = -1;
        var count = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = ((long)y * width + x) * 4;
                var changed = false;
                for (var channel = 0; channel < 4; channel++)
                    if (before[at + channel] != after[at + channel]) { changed = true; break; }
                if (!changed) continue;
                count++;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }
        if (right < left) return (0, 0, -1, -1, 0, 0, 0);
        return (left, top, right, bottom, right - left + 1, bottom - top + 1, count);
    }
}
