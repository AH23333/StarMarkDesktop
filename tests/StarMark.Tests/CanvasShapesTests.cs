#nullable enable
using System;
using StarMark.Core.Capture;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WK：画布上的四种图形（矩形/椭圆/直线/箭头）。
/// <para>
/// 钉的是<b>几何与生命周期</b>两件事：① 一次拖拽展成的点列必须就是用户拖出来的那个形状
/// （四个方向、退化情况、闭口、采样密度）；② 预览只活在临时层——它不能提前进持久层，
/// 也不能在松手后留下一份影子。这两件事都是"只有真机才看得见"的形状，所以在这里逐点断言。
/// </para>
/// </summary>
public sealed class CanvasShapesTests
{
    private static List<PixelPoint> Outline(CanvasTool tool, (int, int) from, (int, int) to, int width = 9)
        => CanvasShapes.Outline(tool, new PixelPoint(from.Item1, from.Item2), new PixelPoint(to.Item1, to.Item2), width);

    // ────────── 矩形 ──────────

    /// <summary>矩形闭口（首尾同点）：不闭口就会在起点处留一个缺口，而缺口在投影距离上比笔宽还显眼。</summary>
    [Theory]
    [InlineData(100, 100, 400, 300)]
    [InlineData(400, 300, 100, 100)]      // 从右下往左上拖
    [InlineData(100, 300, 400, 100)]      // 从左下往右上拖
    [InlineData(400, 100, 100, 300)]      // 从右上往左下拖
    public void RectangleIsClosedAndIndependentOfDragDirection(int x1, int y1, int x2, int y2)
    {
        var points = Outline(CanvasTool.Rectangle, (x1, y1), (x2, y2));
        var left = Math.Min(x1, x2);
        var right = Math.Max(x1, x2);
        var top = Math.Min(y1, y2);
        var bottom = Math.Max(y1, y2);

        Assert.Equal(5, points.Count);
        Assert.Equal(points[0], points[^1]);                              // 闭口
        Assert.Equal(new[] { left, right, right, left, left }, points.Select(p => p.X).ToArray());
        Assert.Equal(new[] { top, top, bottom, bottom, top }, points.Select(p => p.Y).ToArray());
    }

    /// <summary>原地按一下也要画得出东西（一个圆帽），而不是"点了没反应"。</summary>
    [Fact]
    public void DegenerateDragStillProducesAPaintableStroke()
    {
        foreach (var tool in CanvasTools.Shapes)
        {
            var points = Outline(tool, (200, 200), (200, 200));
            Assert.NotEmpty(points);
            // 椭圆的半轴至少 1（否则除零/退化），所以允许落在按下点周围一圈里
            Assert.All(points, p =>
            {
                Assert.InRange(p.X, 198, 202);
                Assert.InRange(p.Y, 198, 202);
            });
        }
    }

    // ────────── 直线与箭头 ──────────

    [Fact]
    public void LineIsExactlyTheTwoEnds()
    {
        var points = Outline(CanvasTool.Line, (30, 40), (500, 260));
        Assert.Equal(new[] { new PixelPoint(30, 40), new PixelPoint(500, 260) }, points);
    }

    /// <summary>
    /// 箭头＝箭杆 + 两撇，点列是 <c>[起点, 终点, 撇一, 终点, 撇二]</c>。
    /// <para>回头重涂那一段靠的是"取大合成不变浓"（批次 WB 定的口径），所以这里必须钉住<b>终点出现两次</b>：
    /// 少了那一次，走笔会从撇尖直接连到另一撇，画出来的是一个三角形而不是箭头。</para>
    /// </summary>
    [Fact]
    public void ArrowHeadIsAtTheEndYouReleasedAndTheShaftIsRedrawnNotBridged()
    {
        var points = Outline(CanvasTool.Arrow, (100, 100), (500, 100));
        Assert.Equal(5, points.Count);
        Assert.Equal(new PixelPoint(100, 100), points[0]);
        Assert.Equal(new PixelPoint(500, 100), points[1]);
        Assert.Equal(new PixelPoint(500, 100), points[3]);        // 回到终点再出第二撇
        Assert.Equal(points[2].X, points[4].X);                   // 两撇在同一深度
        Assert.Equal(2 * 100, points[2].Y + points[4].Y);         // 关于箭杆对称
        Assert.True(points[2].X < 500 && points[4].X < 500);      // 撇往回走，不是往前伸
        Assert.NotEqual(points[2], points[4]);
    }

    /// <summary>箭头方向跟着拖拽走：反过来拖，头就在另一端（钉住"头在终点"而不是"头在起点"）。</summary>
    [Fact]
    public void ArrowHeadFollowsTheReleaseEnd()
    {
        var points = Outline(CanvasTool.Arrow, (500, 100), (100, 100));
        Assert.Equal(new PixelPoint(100, 100), points[1]);        // 箭杆的终点＝抬手那一端＝头所在
        Assert.All(new[] { points[2], points[4] }, p => Assert.InRange(p.X, 101, 140));
    }

    /// <summary>笔宽越粗头越大，但封顶：细笔时一个 200 像素的头会把整支箭吃掉。</summary>
    [Theory]
    [InlineData(4, 9)]        // 半径 2 → 头长 10（下限），水平回退 10·cos30°
    [InlineData(18, 31)]      // 半径 9 → 头长 36
    [InlineData(80, 52)]      // 半径 40 → 头长本该 160，被封顶到 60
    public void ArrowHeadScalesWithTheBrushButIsClamped(int width, double expected)
    {
        var points = Outline(CanvasTool.Arrow, (100, 500), (900, 500), width);
        var back = 900 - points[2].X;
        Assert.Equal(expected, back, 0);
    }

    // ────────── 椭圆 ──────────

    /// <summary>椭圆闭口，且所有采样点都落在拖拽矩形上（±1 是取整）。</summary>
    [Fact]
    public void EllipseIsClosedAndStaysInsideItsBoundingBox()
    {
        var points = Outline(CanvasTool.Ellipse, (100, 200), (700, 500));
        Assert.Equal(points[0], points[^1]);
        Assert.All(points, p =>
        {
            Assert.InRange(p.X, 99, 701);
            Assert.InRange(p.Y, 199, 501);
        });
        // 四个极值点必须真的够到框（够不到＝椭圆画小了，用户圈不住要圈的东西）；±1 是取整
        Assert.InRange(points.Min(p => p.X), 99, 101);
        Assert.InRange(points.Max(p => p.X), 699, 701);
        Assert.InRange(points.Min(p => p.Y), 199, 201);
        Assert.InRange(points.Max(p => p.Y), 499, 501);
    }

    /// <summary>采样点要在椭圆那条线上：偏差大就是"画出来的是多边形"。</summary>
    [Fact]
    public void EllipseSamplesLieOnTheCurve()
    {
        var points = Outline(CanvasTool.Ellipse, (0, 0), (600, 400), 9);
        double cx = 300, cy = 200, rx = 300, ry = 200;
        Assert.All(points, p =>
        {
            var v = Math.Pow((p.X - cx) / rx, 2) + Math.Pow((p.Y - cy) / ry, 2);
            Assert.InRange(v, 0.98, 1.02);
        });
    }

    /// <summary>
    /// 采样密度同时跟<b>尺寸</b>与<b>笔宽</b>走：大椭圆不采密就会露棱角，
    /// 而小椭圆配粗笔若还按固定步长采，就是在同一个点上白算几百次。
    /// </summary>
    [Fact]
    public void EllipseSampleCountFollowsSizeAndBrushWidth()
    {
        var small = Outline(CanvasTool.Ellipse, (0, 0), (60, 40), 9).Count;
        var big = Outline(CanvasTool.Ellipse, (0, 0), (600, 400), 9).Count;
        var thin = Outline(CanvasTool.Ellipse, (0, 0), (600, 400), 4).Count;
        Assert.True(big > small, $"大椭圆采样点应更密：{big} vs {small}");
        Assert.True(thin > big, $"细笔应比粗笔采得更密：{thin} vs {big}");
        Assert.InRange(big, 16, 512);
    }

    /// <summary>不是图形却走进来＝接线错了：退化成一段直线（看得见、能归因），而不是静默画个圆。</summary>
    [Theory]
    [InlineData(CanvasTool.Pen)]
    [InlineData(CanvasTool.Highlighter)]
    [InlineData(CanvasTool.Eraser)]
    public void ABrushFallsBackToAStraightSegment(CanvasTool tool)
        => Assert.Equal(2, Outline(tool, (0, 0), (10, 10)).Count);

    // ────────── 点列 → 笔迹 ──────────

    /// <summary>
    /// <see cref="CanvasStroke.FromPoints"/> <b>不走"相邻点 &lt;2px 合并"</b>：
    /// 那道过滤是给自由手笔迹去噪的，套到椭圆上就是把圆削成八边形。
    /// </summary>
    [Fact]
    public void FromPointsKeepsEverySampledPointWhileAddPointDeduplicates()
    {
        var near = new[] { new PixelPoint(100, 100), new PixelPoint(101, 100), new PixelPoint(102, 100) };
        var stroke = CanvasStroke.FromPoints(CanvasTool.Ellipse, unchecked((int)0xFF00FF00), 9, near);
        Assert.Equal(3, stroke.Points.Count);

        var byHand = new CanvasStroke(CanvasTool.Ellipse, unchecked((int)0xFF00FF00), 9, near[0]);
        byHand.AddPoint(near[1]);                       // 1 像素远：该被丢掉
        Assert.Single(byHand.Points);
    }

    /// <summary>包围盒要含住<b>所有</b>点外扩一个笔半径：脏区漏一角＝那一角擦不干净。</summary>
    [Fact]
    public void FromPointsBoundsCoverEveryPointPlusTheBrushRadius()
    {
        var points = Outline(CanvasTool.Rectangle, (200, 200), (400, 350));
        var stroke = CanvasStroke.FromPoints(CanvasTool.Rectangle, unchecked((int)0xFF0000FF), 18, points);
        var radius = CanvasWidths.RadiusFor(CanvasTool.Rectangle, 18);
        Assert.Equal(200 - radius - 1, stroke.Bounds.X);
        Assert.Equal(200 - radius - 1, stroke.Bounds.Y);
        Assert.True(stroke.Bounds.Right >= 400 + radius);
        Assert.True(stroke.Bounds.Bottom >= 350 + radius);
    }

    [Fact]
    public void FromPointsRejectsAnEmptyPointList()
        => Assert.Throws<ArgumentException>(() =>
            CanvasStroke.FromPoints(CanvasTool.Line, unchecked((int)0xFF000000), 9, Array.Empty<PixelPoint>()));

    // ────────── 预览的生命周期 ──────────

    /// <summary>
    /// 换预览必须把<b>旧的那份</b>一起交回脏区：旧的不再有人画它，而它贴过的那一层像素还留在屏幕上
    /// ——症状就是"拖一个矩形，屏幕上一条更宽的矩形影子"。
    /// </summary>
    [Fact]
    public void SettingThePreviewHandsBackBothTheOldAndTheNewArea()
    {
        var ink = new EphemeralInk();
        var first = CanvasStroke.FromPoints(CanvasTool.Rectangle, unchecked((int)0xFF00FF00), 9,
            new[] { new PixelPoint(10, 10), new PixelPoint(20, 20), new PixelPoint(10, 10) });
        var second = CanvasStroke.FromPoints(CanvasTool.Rectangle, unchecked((int)0xFF00FF00), 9,
            new[] { new PixelPoint(500, 500), new PixelPoint(600, 600), new PixelPoint(500, 500) });

        var dirty = ink.SetPreview(first);
        Assert.Equal(first.Bounds, dirty);

        dirty = ink.SetPreview(second);
        Assert.Equal(10 - CanvasWidths.RadiusFor(CanvasTool.Rectangle, 9) - 1, dirty.X);
        Assert.True(dirty.Right >= second.Bounds.Right);
        Assert.True(dirty.Bottom >= second.Bounds.Bottom);
        Assert.Same(second, ink.Preview);
    }

    /// <summary>预览<b>不按 TTL 淡出</b>：它该在松手那一刻整份换成持久笔迹，而不是慢慢变透明。</summary>
    [Fact]
    public void ThePreviewNeverFades()
    {
        var ink = new EphemeralInk { Ttl = TimeSpan.FromMilliseconds(1500) };
        ink.Begin(new PixelPoint(0, 0), unchecked((int)0xFF00FF00), 9, 1_000);
        var preview = CanvasStroke.FromPoints(CanvasTool.Line, unchecked((int)0xFF00FF00), 9,
            new[] { new PixelPoint(30, 30), new PixelPoint(90, 90) });
        ink.SetPreview(preview);

        ink.Tick(99_000);                                   // 荧光段早该没了，预览不该动
        Assert.Empty(ink.Segments);
        Assert.Same(preview, ink.Preview);
        Assert.False(ink.IsEmpty);
    }

    /// <summary>丢掉预览要还回它占过的地方（调用方据此擦屏幕）；没有预览时是空区域。</summary>
    [Fact]
    public void DroppingThePreviewReturnsItsAreaOnce()
    {
        var ink = new EphemeralInk();
        Assert.True(ink.DropPreview().IsEmpty);
        var preview = CanvasStroke.FromPoints(CanvasTool.Ellipse, unchecked((int)0xFF00FF00), 9,
            CanvasShapes.Outline(CanvasTool.Ellipse, new PixelPoint(0, 0), new PixelPoint(80, 40), 9));
        ink.SetPreview(preview);
        Assert.Equal(preview.Bounds, ink.DropPreview());
        Assert.Null(ink.Preview);
        Assert.True(ink.DropPreview().IsEmpty);
        Assert.True(ink.IsEmpty);
    }

    /// <summary>清空（含"清屏"那颗）必须连预览一起丢，且把它占过的地方交回脏区。</summary>
    [Fact]
    public void ClearTakesThePreviewWithIt()
    {
        var ink = new EphemeralInk();
        var preview = CanvasStroke.FromPoints(CanvasTool.Arrow, unchecked((int)0xFF00FF00), 9,
            CanvasShapes.Outline(CanvasTool.Arrow, new PixelPoint(40, 40), new PixelPoint(140, 90), 9));
        ink.SetPreview(preview);
        var live = ink.LiveBounds;
        ink.Clear();
        Assert.True(ink.IsEmpty);
        Assert.Null(ink.Preview);
        // 交回的那片必须含住预览占过的地方——少一角就是屏幕上擦不掉的一角
        Assert.True(live.X <= preview.Bounds.X && live.Y <= preview.Bounds.Y
            && live.Right >= preview.Bounds.Right && live.Bottom >= preview.Bounds.Bottom);
    }

    /// <summary>预览与荧光段同层，但<b>不进 <see cref="EphemeralInk.Segments"/></b>：那条列表按 TTL 淡出，套上去就成了"图形会自己变淡"。</summary>
    [Fact]
    public void ThePreviewIsNotAFadingSegment()
    {
        var ink = new EphemeralInk();
        ink.SetPreview(CanvasStroke.FromPoints(CanvasTool.Line, unchecked((int)0xFF00FF00), 9,
            new[] { new PixelPoint(5, 5), new PixelPoint(50, 50) }));
        Assert.Empty(ink.Segments);
        Assert.NotNull(ink.Preview);
    }

    // ────────── 定形之后一切照旧 ──────────

    /// <summary>图形进的是<b>模型那一份持久层</b>（InkDoc）：撤销、清空、橡皮、存图都按笔迹那一套走，不需要任何特判。</summary>
    [Fact]
    public void ACommittedShapeIsUndoableLikeAnyStroke()
    {
        var doc = new InkDoc(new InkSurface(SurfaceRole.Board, 0));
        Assert.Equal(0, doc.Count);
        doc.Add(CanvasStroke.FromPoints(CanvasTool.Rectangle, unchecked((int)0xFF00FF00), 9,
            CanvasShapes.Outline(CanvasTool.Rectangle, new PixelPoint(0, 0), new PixelPoint(60, 40), 9)).ToAnnotation());
        Assert.NotEqual(0, doc.Count);
        Assert.Equal(AnnotationTool.Rectangle, doc.Marks[0].Tool);
        Assert.True(doc.Undo());
        Assert.Equal(0, doc.Count);
    }

    /// <summary>图形不是荧光笔，颜色必须原样落下去（<see cref="CanvasStroke.EffectiveColorBgra"/> 只对荧光笔强制半透明）。</summary>
    [Fact]
    public void ShapesKeepTheChosenColourOpaque()
    {
        const int opaque = unchecked((int)0xFF203040);
        foreach (var shape in CanvasTools.Shapes)
            Assert.Equal(opaque, CanvasStroke.FromPoints(shape, opaque, 9,
                new[] { new PixelPoint(0, 0), new PixelPoint(9, 9) }).EffectiveColorBgra);
        Assert.NotEqual(opaque, CanvasStroke.FromPoints(CanvasTool.Highlighter, opaque, 9,
            new[] { new PixelPoint(0, 0), new PixelPoint(9, 9) }).EffectiveColorBgra);
    }

    /// <summary>五种图形都归"图形"那一组（工具条按这张表生成按钮、编排按它决定"拖形还是走笔"）。</summary>
    [Theory]
    [InlineData(CanvasTool.Rectangle)]
    [InlineData(CanvasTool.Ellipse)]
    [InlineData(CanvasTool.Line)]
    [InlineData(CanvasTool.PolyLine)]
    [InlineData(CanvasTool.Arrow)]
    public void ShapesAreGroupedAsShapes(CanvasTool shape)
    {
        Assert.True(shape.IsShape());
        // 从前这里还断言"选了图形就该收回鼠标"（IsClickThroughAfter）。2026-09-27 用户改判：
        // 换工具不再改穿透态，所以那条判据连同事件一起删了——留着一条没人读的判据正是 WR 那批的病因。
    }

    [Fact]
    public void BrushesAreNotShapesAndNamesAreUnique()
    {
        Assert.All(CanvasTools.Brushes, brush => Assert.False(brush.IsShape()));
        var names = CanvasTools.Brushes.Concat(CanvasTools.Shapes).Select(tool => tool.Name()).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());          // 撞名＝条上两颗看起来是同一件事
        // 钉的是"每个工具都归了组"，不是"现在有几个"：加一种工具而没进 Brushes/Shapes，
        // 条上就不会有它、闸门也不会红——只有这一条会在建表当天就拦住
        Assert.Equal(Enum.GetValues<CanvasTool>().Length, names.Count);
    }

    /// <summary>
    /// 折线的橡皮筋：<b>预览点列＝已定形顶点 + 光标那一段</b>，且少一种边界处理就会出现
    /// "刚选折线，板上凭空多个圆点"（单点也画）或"顶点处叠一个重点，收口后笔迹自己拐了一下"。
    /// </summary>
    [Fact]
    public void PolyLinePreviewAppendsTheRubberBand_WithoutDoublingTheLastVertex()
    {
        var a = new PixelPoint(10, 10);
        var b = new PixelPoint(40, 25);
        // 一个顶点都没有（刚切到工具）：什么都不画
        Assert.Empty(CanvasShapes.PolyLinePreview(Array.Empty<PixelPoint>(), new PixelPoint(1, 1)));
        Assert.Empty(CanvasShapes.PolyLinePreview(new[] { a }, null));
        // 一个顶点 + 光标：那一段就是它两个端点
        Assert.Equal(new[] { a, b }, CanvasShapes.PolyLinePreview(new[] { a }, b));
        // 光标还停在最后那个顶点上（抬手之后还没动）：不重复添点，否则那一段长度为零
        Assert.Equal(new[] { a, b }, CanvasShapes.PolyLinePreview(new[] { a, b }, b));
        // 已定形的那几段原样在前，橡皮筋只追加在最后
        Assert.Equal(new[] { a, b, new PixelPoint(20, 60) },
            CanvasShapes.PolyLinePreview(new[] { a, b }, new PixelPoint(20, 60)));
    }


    // ────────── 真的画得出来（逐像素，不是"看起来一样"） ──────────
    //
    // 上面钉的是几何点列，这里钉的是"点列交给那支笔之后，屏幕上出现的确实是那个形状"：
    // 展成点列这条路只要有一环接错（半径算错、闭口漏一段、颜色被当成荧光笔），
    // 症状就是真机上"画出来是实心的／缺一条边／擦不掉"，而那些都不会被几何断言抓到。

    private static uint[] Paint(CanvasTool tool, PixelPoint from, PixelPoint to, int width = 5)
    {
        const int w = 120, h = 100;
        var buffer = new uint[w * h];
        CanvasCompositor.Clear(buffer);
        CanvasCompositor.Paint(buffer, w, h, CanvasStroke.FromPoints(tool,
            unchecked((int)0xFF2030FF), width, CanvasShapes.Outline(tool, from, to, width)));
        return buffer;
    }

    private static bool Inked(uint[] buffer, int x, int y) => (buffer[y * 120 + x] >>> 24) > 1;

    [Fact]
    public void RectanglePaintsFourEdgesAndKeepsTheInsideBlank()
    {
        var buffer = Paint(CanvasTool.Rectangle, new PixelPoint(10, 10), new PixelPoint(70, 50));
        Assert.True(Inked(buffer, 10, 10));       // 左上角
        Assert.True(Inked(buffer, 70, 30));       // 右边中段
        Assert.True(Inked(buffer, 40, 50));       // 下边
        Assert.True(Inked(buffer, 10, 30));       // 左边（闭口漏了这一段就是这里空）
        Assert.False(Inked(buffer, 40, 30));      // 中间必须是空的：它是"圈重点"，不是"涂一块"
        Assert.False(Inked(buffer, 5, 5));        // 框外一像素都不许染到
    }

    [Fact]
    public void EllipsePaintsTheCurveAndKeepsTheInsideBlank()
    {
        var buffer = Paint(CanvasTool.Ellipse, new PixelPoint(20, 20), new PixelPoint(80, 60));
        Assert.True(Inked(buffer, 50, 20));       // 上极
        Assert.True(Inked(buffer, 50, 60));       // 下极
        Assert.True(Inked(buffer, 20, 40));       // 左极
        Assert.True(Inked(buffer, 80, 40));       // 右极
        Assert.False(Inked(buffer, 50, 40));      // 中心
        Assert.False(Inked(buffer, 10, 10));      // 框外
    }

    [Fact]
    public void LineAndArrowPaintTheShaftAndNothingBeyondTheTip()
    {
        var line = Paint(CanvasTool.Line, new PixelPoint(20, 20), new PixelPoint(80, 20));
        Assert.True(Inked(line, 50, 20));
        Assert.False(Inked(line, 50, 40));

        var arrow = Paint(CanvasTool.Arrow, new PixelPoint(20, 50), new PixelPoint(90, 50));
        Assert.True(Inked(arrow, 50, 50));        // 杆
        Assert.True(Inked(arrow, 81, 45) && Inked(arrow, 81, 55));  // 两撇的落点：终点往回、上下对称
        Assert.False(Inked(arrow, 96, 50));       // 笔宽只有 5：尖端之外不该染到
    }
}
