#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 屏幕画布的纯逻辑层（规格 §16.3 / §16.4）。这一层钉的都是"只有画出来才看得见"的事，
/// 但它们全能在像素数组上断言——这正是把合成写成托管像素运算（而不是让界面画完再拍照）的回报：
/// ① <b>没画到的地方必须 alpha=0</b>（否则那块"玻璃"会盖住整个桌面）；
/// ② 荧光笔盖在字上还能看见字（浓度上限 + 取大规则）；
/// ③ 同一条笔迹重复涂抹不变浓（增量绘制与全量重画必须同结果，否则跟手帧与最终帧不一样）；
/// ④ 预乘不变量（通道 ≤ alpha），否则分层窗合成出白边；
/// ⑤ 脏区夹进画布（负原点是常态：笔迹拖到屏幕外）。
/// </summary>
public sealed class CanvasInkTests
{
    private static readonly int Red = Annotation.Opaque(0x23, 0x11, 0xE8);      // 出厂红：B=23 G=11 R=E8
    private const int Width = 4;                                        // 半径 2

    private static uint[] Buffer(int width = 100, int height = 100) => new uint[width * height];

    private static uint Pixel(uint[] buffer, int x, int y, int width = 100) => buffer[y * width + x];

    private static int AlphaOf(uint pixel) => (int)(pixel >>> 24);

    private static CanvasStroke Pen(int x, int y) => new(CanvasTool.Pen, Red, Width, new PixelPoint(x, y));

    // ────────── 笔迹模型 ──────────

    [Fact]
    public void PointsCloserThanTwoPixelsAreMerged()
    {
        var stroke = Pen(0, 0);
        Assert.False(stroke.AddPoint(new PixelPoint(1, 1)));     // 距离 √2 < 2：丢掉
        Assert.True(stroke.AddPoint(new PixelPoint(2, 0)));        // 正好 2：收下
        Assert.Equal(2, stroke.Points.Count);
    }

    [Fact]
    public void BoundsPadByTheBrushRadiusPlusOne()
    {
        // 半径 2 + 那条 1 像素覆盖度斜坡＝脏区必须到 3；少留一像素就会在边缘留一圈擦不掉的残影
        var stroke = Pen(50, 50);
        Assert.Equal(new IntRect(47, 47, 7, 7), stroke.Bounds);
    }

    [Fact]
    public void HighlighterBoundsReachTheOuterGlow_NotTheCore()
    {
        var stroke = new CanvasStroke(CanvasTool.Highlighter, Red, 9, new PixelPoint(50, 50));
        var radius = CanvasWidths.RadiusFor(CanvasTool.Highlighter, 9);
        Assert.True(radius > CanvasWidths.RadiusFor(CanvasTool.Pen, 9),
            "荧光笔的光晕比笔芯宽，包围盒按笔芯算就会漏提交那一圈");
        Assert.Equal(50 - radius - 1, stroke.Bounds.X);
        Assert.Equal(radius * 2 + 3, stroke.Bounds.Width);
    }

    [Fact]
    public void ZeroWidthPenIsRejectedInsteadOfPaintingAnInvisibleStroke()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new CanvasStroke(CanvasTool.Pen, Red, 0, new PixelPoint(1, 1)));

    [Fact]
    public void ASingleTapWithTheEraserIsNotWorthAnUndoSlot()
    {
        Assert.False(new CanvasStroke(CanvasTool.Eraser, Red, CanvasWidths.EraserDiameter, new PixelPoint(9, 9)).WorthKeeping);
        Assert.True(Pen(9, 9).WorthKeeping);                       // 笔点一下是"点了一个圆点"，要留
    }

    [Fact]
    public void ExtendingWithoutAnOpenStrokeIsRejected()
    {
        // 静默吞掉的话，症状是" Sometimes 画不上"——那种 bug 在这台机器上永远复现不出来
        var ink = new CanvasInk();
        Assert.Throws<InvalidOperationException>(() => ink.Extend(new PixelPoint(1, 1)));
    }

    [Fact]
    public void FinishedStrokesEnterTheLayerAndUndoPopsThemBackOut()
    {
        var ink = new CanvasInk();
        ink.Begin(CanvasTool.Pen, Red, Width, new PixelPoint(10, 10));
        ink.Extend(new PixelPoint(30, 10));
        Assert.True(ink.End());
        Assert.True(ink.Drawing is null, "收笔之后必须没有\"正在画\"的残留，否则下一次移动会接到上一条上");
        Assert.True(ink.Undo());
        Assert.False(ink.Undo());
        Assert.True(ink.IsEmpty);
    }

    [Fact]
    public void ClearAlsoDropsTheStrokeStillBeingDrawn()
    {
        var ink = new CanvasInk();
        ink.Begin(CanvasTool.Pen, Red, Width, new PixelPoint(5, 5));
        ink.Clear();
        Assert.True(ink.Drawing is null);
        // 清屏之后不该还剩着"正在画"的那条：否则松手时会把清屏后又动了一下鼠标补成一条幽灵笔迹
        Assert.Throws<InvalidOperationException>(() => ink.Extend(new PixelPoint(9, 9)));
    }

    [Fact]
    public void TheEraserIsWiderThanTheThickestPen()
        => Assert.True(CanvasWidths.EraserDiameter > CanvasWidths.Steps.Max(),
            "橡皮比最粗的笔还窄的话，擦一条粗笔迹要来回拖好几下");

    [Theory]
    [InlineData(-5, 4)]
    [InlineData(0, 4)]
    [InlineData(1, 9)]
    [InlineData(2, 18)]
    [InlineData(99, 18)]
    public void WidthIndexesClampIntoTheSteps(int index, int want)
        => Assert.Equal(want, CanvasWidths.At(index));

    // ────────── 合成：透明底 + 取大 + 预乘 ──────────

    [Fact]
    public void UntouchedPixelsStayFullyTransparent()
    {
        var buffer = Buffer();
        CanvasCompositor.Paint(buffer, 100, 100, StrokeFrom(10, 10, to: 40));
        Assert.Equal(0u, Pixel(buffer, 90, 90));                   // 这块玻璃没画到的地方必须真的没有像素
    }

    [Fact]
    public void PenCoreIsOpaqueAndKeepsItsColour()
    {
        var buffer = Buffer();
        CanvasCompositor.Paint(buffer, 100, 100, Pen(50, 50));
        var pixel = Pixel(buffer, 50, 50);
        Assert.Equal(255, AlphaOf(pixel));
        Assert.Equal(Red & 0x00FFFFFF, (int)pixel & 0x00FFFFFF);   // 预乘 ×255/255＝原色
    }

    [Fact]
    public void TheOuterRingIsHalfCovered_AndBeyondItNothing()
    {
        var buffer = Buffer();
        CanvasCompositor.Paint(buffer, 100, 100, Pen(50, 50));     // 半径 2
        Assert.Equal(0, AlphaOf(Pixel(buffer, 53, 50)));            // 距离 3 ⇒ 斜坡之外
        var rim = AlphaOf(Pixel(buffer, 52, 50));                  // 距离 2 ⇒ 斜坡正中
        Assert.InRange(rim, 120, 135);
    }

    [Fact]
    public void RepaintingTheSameStrokeDoesNotDarkenIt()
    {
        // 这条就是"增量绘制与全量重画同结果"的根据：拖动时只补最后一段，松手后整层重算，
        // 两者不一致的话用户会看到"松手那一下笔迹变深/变粗"。
        var once = Buffer();
        var twice = Buffer();
        var stroke = StrokeFrom(20, 50, 60, 50);
        CanvasCompositor.Paint(once, 100, 100, stroke);
        CanvasCompositor.Paint(twice, 100, 100, stroke);
        CanvasCompositor.Paint(twice, 100, 100, stroke);
        Assert.True(once.AsSpan().SequenceEqual(twice));
    }

    [Fact]
    public void IncrementalTailPaintMatchesTheFullRepaint()
    {
        var whole = Buffer();
        var stepped = Buffer();
        var stroke = StrokeFrom(10, 20, 70, 20);
        CanvasCompositor.Paint(whole, 100, 100, stroke);
        // 逐段增量：先画第一条线段，再按"每加一个点只补最后一段"走完整条
        var partial = new CanvasStroke(CanvasTool.Pen, Red, Width, new PixelPoint(10, 20));
        CanvasCompositor.PaintTail(stepped, 100, 100, partial);
        foreach (var point in stroke.Points.Skip(1))
        {
            partial.AddPoint(point);
            CanvasCompositor.PaintTail(stepped, 100, 100, partial);
        }
        Assert.True(whole.AsSpan().SequenceEqual(stepped));
    }

    [Fact]
    public void HighlighterHasThreeRings_AndNeverGoesOpaque()
    {
        var buffer = Buffer();
        var stroke = new CanvasStroke(CanvasTool.Highlighter, Red, 9, new PixelPoint(50, 50));
        CanvasCompositor.Paint(buffer, 100, 100, stroke);
        var core = AlphaOf(Pixel(buffer, 50, 50));
        var middle = AlphaOf(Pixel(buffer, 53, 50));               // 内芯半径 2.25，中圈到 4.5
        var halo = AlphaOf(Pixel(buffer, 58, 50));
        Assert.True(core > middle && middle > halo,
            $"三层要一眼分得出：core={core} middle={middle} halo={halo}");
        Assert.True(core < 255, $"荧光笔内芯必须透得过去，实测 {core}");
        Assert.True(halo > 0, "外圈光晕是荧光笔的形状线索，不能整圈没有");
    }

    [Fact]
    public void HighlighterOverPenLeavesThePenVisible()
    {
        // 真实荧光笔盖过黑字，字还在——取大规则给的就是这个结果（淡的一笔不改已经更浓的像素）
        var buffer = Buffer();
        CanvasCompositor.Paint(buffer, 100, 100, Pen(50, 50));
        CanvasCompositor.Paint(buffer, 100, 100, new CanvasStroke(CanvasTool.Highlighter, Red, 9, new PixelPoint(50, 50)));
        Assert.Equal(255, AlphaOf(Pixel(buffer, 50, 50)));
    }

    [Fact]
    public void EraserTakesTheInkBackToNothing()
    {
        var buffer = Buffer();
        CanvasCompositor.Paint(buffer, 100, 100, Pen(50, 50));
        var dirty = CanvasCompositor.Paint(buffer, 100, 100,
            new CanvasStroke(CanvasTool.Eraser, Red, CanvasWidths.EraserDiameter, new PixelPoint(50, 50)));
        Assert.Equal(0u, Pixel(buffer, 50, 50));
        Assert.False(dirty.IsEmpty);
    }

    [Fact]
    public void ErasingNothingDoesNotManufacturePixels()
    {
        var buffer = Buffer();
        CanvasCompositor.Paint(buffer, 100, 100,
            new CanvasStroke(CanvasTool.Eraser, Red, CanvasWidths.EraserDiameter, new PixelPoint(50, 50)));
        Assert.All(buffer, pixel => Assert.Equal(0u, pixel));      // 空板子上擦：整块仍然是空的（也不该除零）
    }

    [Fact]
    public void EveryPixelKeepsThePremultipliedInvariant()
    {
        // UpdateLayeredWindow 吃的是预乘 BGRA：通道大于 alpha 就是"亮得没有依据"，合成出来是白边
        var buffer = Buffer();
        CanvasCompositor.PaintAll(buffer, 100, 100, new[]
        {
            StrokeFrom(10, 20, 80, 60),
            new CanvasStroke(CanvasTool.Highlighter, Red, 9, new PixelPoint(40, 40)),
            StrokeFrom(30, 30, 70, 70, CanvasTool.Eraser),
        });
        foreach (var pixel in buffer)
        {
            var alpha = AlphaOf(pixel);
            Assert.True((int)(pixel & 0xFF) <= alpha, $"蓝 {pixel & 0xFF} 超过了 alpha {alpha}");
            Assert.True((int)(pixel >> 8 & 0xFF) <= alpha, $"绿超过了 alpha {alpha}");
            Assert.True((int)(pixel >> 16 & 0xFF) <= alpha, $"红超过了 alpha {alpha}");
        }
    }

    [Fact]
    public void FadeScalesTheWholeStroke_WhichIsHowTheHighlighterTrailDies()
    {
        var full = Buffer();
        var half = Buffer();
        var stroke = StrokeFrom(20, 50, 60, 50, CanvasTool.Highlighter);
        CanvasCompositor.Paint(full, 100, 100, stroke, fade: 1d);
        CanvasCompositor.Paint(half, 100, 100, stroke, fade: 0.5d);
        var strong = AlphaOf(Pixel(full, 40, 50));
        var weak = AlphaOf(Pixel(half, 40, 50));
        Assert.InRange(weak, strong / 2 - 3, strong / 2 + 3);
    }

    [Fact]
    public void PaintReportsTheDirtyRectClampedIntoTheCanvas()
    {
        var buffer = Buffer(60, 60);
        var stroke = new CanvasStroke(CanvasTool.Pen, Red, Width, new PixelPoint(2, 2));      // 包围盒伸到负坐标
        stroke.AddPoint(new PixelPoint(58, 58));                                              // 也伸出右下一角
        var dirty = CanvasCompositor.Paint(buffer, 60, 60, stroke);
        Assert.Equal(0, dirty.X);
        Assert.Equal(0, dirty.Y);
        Assert.Equal(60, dirty.Right);
        Assert.Equal(60, dirty.Bottom);
        Assert.True(AlphaOf(Pixel(buffer, 2, 2, 60)) > 0, "屏幕角落上的那一笔也要真的画进缓冲");
    }

    [Fact]
    public void InkFromAStrokeIsOpaque_AndTheHighlighterIsForcedTranslucent()
    {
        var pen = new CanvasStroke(CanvasTool.Pen, Red, Width, new PixelPoint(1, 1));
        Assert.Equal(Red, pen.EffectiveColorBgra);                       // 用户挑的颜色就是画出去的颜色
        var marker = new CanvasStroke(CanvasTool.Highlighter, Red, 9, new PixelPoint(1, 1));
        Assert.Equal(Annotation.HighlighterAlpha, marker.EffectiveColorBgra >>> 24);
    }

    [Fact]
    public void ClearRectOnlyTouchesThatRect()
    {
        var buffer = Buffer();
        CanvasCompositor.Paint(buffer, 100, 100, Pen(20, 20));
        CanvasCompositor.Paint(buffer, 100, 100, Pen(70, 70));
        CanvasCompositor.ClearRect(buffer, 100, 100, new IntRect(10, 10, 25, 25));
        Assert.Equal(0u, Pixel(buffer, 20, 20));                     // 撤销走的就是这条路：只擦那一条的包围盒
        Assert.True(AlphaOf(Pixel(buffer, 70, 70)) > 0, "别的地方不能被牵连");
    }

    [Fact]
    public void CopyRectMovesPixelsWithoutTouchingTheRest()
    {
        var source = Buffer();
        var target = Buffer();
        CanvasCompositor.Paint(source, 100, 100, Pen(20, 20));
        CanvasCompositor.Paint(target, 100, 100, Pen(70, 70));       // 目标那块区域外本来有别的东西
        CanvasCompositor.CopyRect(source, target, 100, 100, new IntRect(0, 0, 40, 40));
        Assert.Equal(Pixel(source, 20, 20), Pixel(target, 20, 20));
        Assert.Equal(Pixel(target, 70, 70), Pixel(target, 70, 70));  // 区域外原样
        Assert.True(AlphaOf(Pixel(target, 70, 70)) > 0, "拷贝不能把区域外的笔迹带走");
    }

    [Fact]
    public void GlowIsBrightestAtTheCentreAndDiesAtTheEdge()
    {
        var buffer = Buffer();
        var dirty = CanvasCompositor.PaintGlow(buffer, 100, 100, new PixelPoint(50, 50), 16, Red);
        var centre = AlphaOf(Pixel(buffer, 50, 50));
        var middle = AlphaOf(Pixel(buffer, 58, 50));
        var edge = AlphaOf(Pixel(buffer, 66, 50));
        Assert.True(centre > middle && middle > edge, $"光晕必须是连续的一团：{centre}/{middle}/{edge}");
        Assert.Equal(new IntRect(34, 34, 33, 33), dirty);
    }

    [Fact]
    public void GlowNeverWashesOutInkThatIsAlreadyDenser()
    {
        var buffer = Buffer();
        CanvasCompositor.Paint(buffer, 100, 100, Pen(50, 50));              // 不透明的笔芯
        CanvasCompositor.PaintGlow(buffer, 100, 100, new PixelPoint(50, 50), 16, Red);
        Assert.Equal(255, AlphaOf(Pixel(buffer, 50, 50)));                  // 取大：光晕不改变已有浓墨的密度
    }

    // ────────── 脏区数学 ──────────

    [Fact]
    public void DirtyRectsMergeIntoOneBoundingRect()
    {
        var union = CanvasCompositor.Union(new[] { new IntRect(0, 0, 10, 10), new IntRect(20, 20, 10, 10) }, 1920, 1080);
        Assert.Equal(new IntRect(0, 0, 30, 30), union);
    }

    [Fact]
    public void RectsEntirelyOffTheCanvasAreNotDirty()
    {
        Assert.Equal(CanvasCompositor.Nothing, CanvasCompositor.Union(new[] { new IntRect(5000, 5000, 10, 10) }, 1920, 1080));
        Assert.Equal(CanvasCompositor.Nothing, CanvasCompositor.Union(Array.Empty<IntRect>(), 1920, 1080));
    }

    [Fact]
    public void NegativeOriginsAreClampedButStillCount()
    {
        // 笔迹拖到那块屏的左边外面：原点为负是常态，不是错误
        Assert.Equal(new IntRect(0, 0, 5, 5), CanvasCompositor.Clamp(new IntRect(-5, -5, 10, 10), 50, 50));
        Assert.Equal(new IntRect(0, 0, 50, 50), CanvasCompositor.Clamp(new IntRect(-10, -10, 100, 100), 50, 50));
    }

    // ────────── 荧光段的寿命（TTL）──────────

    private const long T0 = 100_000;                                  // 任意"现在"（TickCount64 的刻度）

    private static EphemeralInk Trail() => new();

    [Fact]
    public void ATrailIsFullStrengthWhileStillBeingDrawn()
    {
        var ink = Trail();
        ink.Begin(new PixelPoint(10, 10), Red, 9, T0);
        ink.Extend(new PixelPoint(40, 10), T0 + 500);
        ink.Tick(T0 + 500);
        Assert.Equal(1d, ink.Segments[0].AlphaScale);
        Assert.Equal(T0 + 500, ink.Segments[0].LastPointTick);
    }

    [Fact]
    public void ATrailFadesAcrossTheTtl_AndIsGoneAtTheEnd()
    {
        var ink = Trail();
        ink.Begin(new PixelPoint(10, 10), Red, 9, T0);
        ink.Tick(T0 + 750);                                            // 1.5s 的一半
        Assert.InRange(ink.Segments[0].AlphaScale, 0.45, 0.55);
        ink.Tick(T0 + 1500);
        Assert.True(ink.IsEmpty, "到点的那一帧必须自己掉出去，不然屏幕上赖着一道擦不掉的荧光");
    }

    [Fact]
    public void ExtendingARetrailRestartsWithNoFade()
    {
        var ink = Trail();
        ink.Begin(new PixelPoint(10, 10), Red, 9, T0);
        ink.Extend(new PixelPoint(30, 30), T0 + 900);                 // 快到期了又接着比划
        ink.Tick(T0 + 900);
        Assert.Equal(1d, ink.Segments[0].AlphaScale);
    }

    [Fact]
    public void ExtendWithNoOpenSegmentIsIgnored_NotThrown()
    {
        // 穿透态收不到事件，松手与移动的到达顺序不保证；这里抛异常＝"画布一动就崩"
        var ink = Trail();
        Assert.False(ink.Extend(new PixelPoint(1, 1), T0));
        Assert.True(ink.IsEmpty);
    }

    [Fact]
    public void ClockWraparoundDoesNotSwallowTheTrail()
    {
        var ink = Trail();
        ink.Begin(new PixelPoint(10, 10), Red, 9, T0);
        ink.Tick(T0 - 5_000);                                          // now < 添点时刻：当作刚画过
        Assert.Equal(1d, ink.Segments[0].AlphaScale);
    }

    [Fact]
    public void ClearDropsEveryTrailAtOnce()
    {
        var ink = Trail();
        ink.Begin(new PixelPoint(1, 1), Red, 9, T0);
        ink.Begin(new PixelPoint(2, 2), Red, 9, T0 + 10);
        ink.Clear();
        Assert.True(ink.IsEmpty);
    }

    [Fact]
    public void TrailDefaultsAreTheSpecValues()
    {
        var ink = Trail();
        Assert.Equal(TimeSpan.FromMilliseconds(1500), ink.Ttl);
        Assert.True(ink.CursorHaloEnabled, "光晕默认开：穿透态下它是\"模式还开着\"的唯一提示");
        Assert.Equal(Annotation.Opaque(0x30, 0x30, 0xFF), ink.HaloColorBgra);
        Assert.Equal(0xFF, ink.HaloColorBgra >>> 24);                  // 光晕自己不半透明
    }

    // ────────── 小工具 ──────────

    private static CanvasStroke StrokeFrom(int x, int y, int to, int? toY = null, CanvasTool tool = CanvasTool.Pen)
    {
        var width = tool == CanvasTool.Eraser ? CanvasWidths.EraserDiameter : tool == CanvasTool.Highlighter ? 9 : Width;
        var stroke = new CanvasStroke(tool, Red, width, new PixelPoint(x, y));
        var end = toY ?? y;
        for (var i = 1; i <= Math.Max(Math.Abs(to - x), Math.Abs(end - y)); i++)
        {
            stroke.AddPoint(new PixelPoint(x + (to - x) * i / Math.Max(1, Math.Abs(to - x)),
                y + (end - y) * i / Math.Max(1, Math.Abs(end - y))));
        }
        return stroke;
    }
}
