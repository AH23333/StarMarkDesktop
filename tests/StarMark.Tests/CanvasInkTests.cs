#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using StarMark.Integrations.Canvas;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 屏幕画布的纯逻辑层（规格 §16.3 / §16.4）。这一层钉的都是"只有画出来才看得见"的事，
/// 但它们全能在像素数组上断言——这正是把合成写成托管像素运算（而不是让界面画完再拍照）的回报：
/// ① <b>没画到的地方必须是"空白"（alpha=1 那一档）</b>：实色＝一面墙，0＝分层窗在这一块上直接漏掉鼠标，
///    症状是"刚擦过的地方画不上"（见 <see cref="LayeredCanvasWindow.BlankPixel"/>）；
/// ② 荧光笔盖在字上还能看见字（浓度上限 + 取大规则）；
/// ③ 同一条笔迹重复涂抹不变浓（增量绘制与全量重画必须同结果，否则跟手帧与最终帧不一样）；
/// ④ 预乘不变量（通道 ≤ alpha），否则分层窗合成出白边；
/// ⑤ 脏区夹进画布（负原点是常态：笔迹拖到屏幕外）。
/// </summary>
public sealed class CanvasInkTests
{
    private static readonly int Red = Annotation.Opaque(0x23, 0x11, 0xE8);      // 出厂红：B=23 G=11 R=E8
    private const uint Blank = LayeredCanvasWindow.BlankPixel;
    private const int BlankAlpha = (int)(Blank >>> 24);                 // 1：看不见，但命中测试认它
    private const int Width = 4;                                        // 半径 2

    /// <summary>新板子的初值——<b>不是全 0</b>，全 0 那块玻璃在鼠标眼里根本不存在。</summary>
    private static uint[] Buffer(int width = 100, int height = 100)
    {
        var buffer = new uint[width * height];
        Array.Fill(buffer, Blank);
        return buffer;
    }

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
    public void OnlyAPressCanOpenAStrokeSoAMoveCannotExtendNothing()
    {
        // 从前这条由 CanvasInk.Extend 抛异常守着（"没在下笔却收到移动＝丢字，不能静默"）。
        // 拆成两层之后（手上那条住在 Screen.Drawing，落定的住在 InkDoc），
        // 这道防线改成"没有开口的笔迹就根本没有可拖的对象"——接线那里必须有 `Drawing is { }` 这一手，
        // 由 CanvasWiringGateTests 钉住；这里钉模型侧的那半：一条笔迹从构造起就是完整的。
        var stroke = Pen(10, 10);
        Assert.True(stroke.AddPoint(new PixelPoint(30, 10)));
        Assert.Equal(2, stroke.Points.Count);
        Assert.False(stroke.AddPoint(new PixelPoint(30, 11)));      // 距离 1 < 2：这一帧不值得重画
        Assert.Equal(2, stroke.Points.Count);
    }

    [Fact]
    public void FinishedStrokesEnterTheLayerAndUndoPopsThemBackOut()
    {
        var doc = new InkDoc(new InkSurface(SurfaceRole.Board, 0));
        var stroke = Pen(10, 10);
        stroke.AddPoint(new PixelPoint(30, 10));
        Assert.True(stroke.WorthKeeping);
        doc.Add(stroke.ToAnnotation());
        Assert.Equal(1, doc.Count);
        Assert.True(doc.Undo());
        Assert.False(doc.Undo());
        Assert.Equal(0, doc.Count);
    }

    [Fact]
    public void ClearingTheBoardAlsoForgetsTheStrokeStillBeingDrawn()
    {
        // 幽灵笔迹那一条：清完屏之后鼠标还动着的话，手上那条会被补成一截没人画过的墨。
        // 模型侧现在只保证"清空是整叠丢掉、并且能撤销回来"，而"手上那条必须一起丢"是接线的责任
        // （CanvasWiringGateTests 钉 ClearAll 里那一句）。
        var doc = new InkDoc(new InkSurface(SurfaceRole.Board, 0));
        doc.Add(Pen(5, 5).ToAnnotation());
        doc.Clear();
        Assert.Equal(0, doc.Count);
        Assert.True(doc.Undo());
        Assert.Equal(1, doc.Count);
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
    public void UntouchedPixelsStayAtTheBlankLevel_NotSolidNotZero()
    {
        var buffer = Buffer();
        CanvasCompositor.Paint(buffer, 100, 100, StrokeFrom(10, 10, to: 40));
        Assert.Equal(Blank, Pixel(buffer, 90, 90));   // 没画到的地方：看不见（alpha=1），但点得着（≠0）
    }

    [Fact]
    public void EveryBlankPathUsesTheSamePixel()
    {
        // "擦干净"有三条路（整块擦、只擦一块、橡皮擦到底），三条都必须落在同一个"空白"值上。
        // 漏一条的症状都是同一件事：那块地方鼠标点不动、笔也画不上，而且只在刚擦过的地方发作。
        var whole = Buffer();
        CanvasCompositor.Clear(whole);
        var rect = Buffer();
        CanvasCompositor.ClearRect(rect, 100, 100, new IntRect(10, 10, 25, 25));
        var erased = Buffer();
        CanvasCompositor.Paint(erased, 100, 100, Pen(50, 50));
        CanvasCompositor.Paint(erased, 100, 100,
            new CanvasStroke(CanvasTool.Eraser, Red, CanvasWidths.EraserDiameter, new PixelPoint(50, 50)));
        Assert.Equal(Blank, Pixel(whole, 3, 7));
        Assert.Equal(Blank, Pixel(rect, 20, 20));
        Assert.Equal(Blank, Pixel(erased, 50, 50));
        // 空白必须是"几乎不可见"：这一档一旦被人调成看得见的浓度，整块玻璃就变成一面墙
        Assert.InRange(BlankAlpha, 1, 2);
        Assert.Equal(0u, Blank & 0x00FF_FFFFu);
    }

    [Fact]
    public void BlankPixelsAreLeftOutOfTheSnapshotOverlay()
    {
        // 快照/存图吃的是同一份缓冲。空白那 1/255 要是被当成墨叠上去，整张图每个像素都会被蒙一层黑
        const int Side = 20;
        var frame = new byte[Side * Side * 4];
        Array.Fill(frame, (byte)200);
        var ink = Buffer(Side, Side);
        CanvasCompositor.Paint(ink, Side, Side, new CanvasStroke(CanvasTool.Pen, Red, Width, new PixelPoint(2, 2)));
        CanvasCompositor.OverlayOntoFrame(frame, Side, Side, new IntRect(0, 0, 0, 0), ink, Side, Side);
        var far = ((Side - 1) * Side + (Side - 1)) * 4;      // 离那一笔最远的一角：只有"空白"
        Assert.Equal(200, frame[far]);
        Assert.Equal(200, frame[far + 1]);
        Assert.Equal(200, frame[far + 2]);
        Assert.NotEqual(200, frame[(2 * Side + 2) * 4]);     // 笔芯那里必须真的改变了画面（否则这条测试是空的）
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
        Assert.Equal(BlankAlpha, AlphaOf(Pixel(buffer, 53, 50)));   // 距离 3 ⇒ 斜坡之外，回到空白那一档
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
    public void EraserTakesTheInkBackToTheBlankLevel()
    {
        var buffer = Buffer();
        CanvasCompositor.Paint(buffer, 100, 100, Pen(50, 50));
        var dirty = CanvasCompositor.Paint(buffer, 100, 100,
            new CanvasStroke(CanvasTool.Eraser, Red, CanvasWidths.EraserDiameter, new PixelPoint(50, 50)));
        Assert.Equal(Blank, Pixel(buffer, 50, 50));
        Assert.False(dirty.IsEmpty);
    }

    [Fact]
    public void ErasingNothingDoesNotManufacturePixels()
    {
        var buffer = Buffer();
        CanvasCompositor.Paint(buffer, 100, 100,
            new CanvasStroke(CanvasTool.Eraser, Red, CanvasWidths.EraserDiameter, new PixelPoint(50, 50)));
        Assert.All(buffer, pixel => Assert.Equal(Blank, pixel));  // 空板子上擦：整块仍然是空白（也不该除零）
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
        Assert.Equal(Blank, Pixel(buffer, 20, 20));                // 撤销走的就是这条路：只擦那一条的包围盒
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

    // ────────── 批次 WG：谁该进脏区 ──────────

    [Fact]
    public void TickHandsBackTheBoundsOfWhatItDropped()
    {
        // 段一掉出去就再没人画它，而它贴过的那层像素还在缓冲里——Tick 必须把这块交回调用方，
        // 否则症状是"荧光淡到一半就永远停在那儿"
        var ink = Trail();
        ink.Begin(new PixelPoint(10, 10), Red, 9, T0);
        ink.Extend(new PixelPoint(90, 70), T0 + 10);
        var covered = ink.Segments[0].Stroke.Bounds;
        Assert.True(ink.Tick(T0 + 100).IsEmpty, "没删段就不该报范围：报了每帧都白重算一大片");
        Assert.Equal(covered, ink.Tick(T0 + 1_600));                   // 过了 TTL：整块都要复原
        Assert.True(ink.IsEmpty);
    }

    [Fact]
    public void DroppedBoundsMergeWhenSeveralSegmentsExpireTogether()
    {
        var ink = Trail();
        ink.Begin(new PixelPoint(10, 10), Red, 9, T0);
        ink.Begin(new PixelPoint(400, 300), Red, 9, T0);
        var dropped = ink.Tick(T0 + 1500);
        Assert.False(dropped.IsEmpty);
        Assert.True(dropped.Width > 390 && dropped.Height > 290, "两段离得远时并成的那块要盖住两边");
    }

    [Fact]
    public void LiveBoundsIsWhatClearingMustDirty()
    {
        var ink = Trail();
        var only = ink.Begin(new PixelPoint(10, 10), Red, 9, T0);
        Assert.Equal(only.Stroke.Bounds, ink.LiveBounds);
        ink.Begin(new PixelPoint(400, 300), Red, 9, T0 + 10);
        // 两段各自是 (-5,-5,31,31) 与 (385,285,31,31)：并起来必须严丝合缝盖住两边
        Assert.Equal(new IntRect(-5, -5, 421, 321), ink.LiveBounds);
        ink.Clear();
        Assert.True(ink.LiveBounds.IsEmpty, "已经清干净了还报一块范围＝每帧白重算");
    }

    [Fact]
    public void ExtendReportsWhetherTheSegmentActuallyGrew()
    {
        // 太近的点被 CanvasStroke 丢掉时"段没变长"：调用方据此决定要不要重算——
        // 若这里一律返回真，鼠标原地抖一下也要付一次合成
        var ink = Trail();
        ink.Begin(new PixelPoint(10, 10), Red, 9, T0);
        Assert.False(ink.Extend(new PixelPoint(11, 10), T0 + 50));
        Assert.Equal(T0 + 50, ink.Segments[0].LastPointTick);    // 点丢了但 TTL 要重新起算（还在比划＝没停）
        Assert.True(ink.Extend(new PixelPoint(60, 10), T0 + 60));
    }

    [Fact]
    public void TailBoundsCoversExactlyTheStepPaintTailDraws()
    {
        // 拖动时的脏区记号与真正画到的那一片必须是同一条式子：脏区小了就会在笔迹边缘留一圈残影
        var stroke = new CanvasStroke(CanvasTool.Highlighter, Red, 9, new PixelPoint(20, 20));
        Assert.Equal(stroke.Bounds, stroke.TailBounds);                  // 只有一个点时同一条
        stroke.AddPoint(new PixelPoint(60, 20));
        Assert.Equal(new IntRect(5, 5, 71, 31), stroke.TailBounds);      // 半径 14 + 那条 1 像素斜坡
        stroke.AddPoint(new PixelPoint(60, 120));
        stroke.AddPoint(new PixelPoint(200, 60));
        Assert.True(stroke.TailBounds.Width < stroke.Bounds.Width
            && stroke.TailBounds.Height < stroke.Bounds.Height,
            "整条笔迹的包围盒当脏区＝每帧又回到整段重画，那正是这批要消掉的开销");
        var buffer = Buffer(300, 300);
        Assert.Equal(CanvasCompositor.Clamp(stroke.TailBounds, 300, 300),
            CanvasCompositor.PaintTail(buffer, 300, 300, stroke));
    }

    [Fact]
    public void TrailDefaultsAreTheSpecValues()
    {
        var ink = Trail();
        Assert.Equal(TimeSpan.FromMilliseconds(1500), ink.Ttl);
        // 批次 S4-⑥：这里<b>不再存"光晕开不开"</b>——那一档要看档位、手上的笔与背景态三件事，
        // 判据在 <c>CursorCircle.ShowsHalo</c>（默认档＝只荧光笔，见 <c>CursorCircleTests</c>）。
        // 宿主再留一个 bool 就是第二份真值，症状是"条上常开着、屏幕上却什么都不跟"。
        Assert.Equal(Annotation.Opaque(0x30, 0x30, 0xFF), ink.HaloColorBgra);
        Assert.Equal(0xFF, ink.HaloColorBgra >>> 24);                  // 光晕自己不半透明
    }

    // ────────── 小工具 ──────────

    /// <summary>
    /// 穿透态抢按的<b>方向</b>（批次 WF-1 同一条纪律：这种布尔写反时代码全绿而功能坏着）。
    /// <para>2026-09-27 用户改判：<b>只认 Ctrl+Alt，与选了哪支笔完全无关</b>——穿透态就是"鼠标归下层"，
    /// 要画荧光笔得先关掉穿透。"不许看工具"这半条钉在调用点（CanvasWiringGateTests：抢按那几句里
    /// 不许出现 <c>_tool</c>），这里只管两臂的方向。</para>
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyCtrlAltClaimsAPressInPenetrating(bool ctrlAltDown)
        => Assert.Equal(ctrlAltDown, CanvasModes.ClaimsPressInPenetrating(ctrlAltDown));

    /// <summary>
    /// 批次 WO：<b>重烤必须擦掉"上一次烤过的那一片"，不是只擦"现在还剩的"</b>。
    /// <para>真机反馈"撤销只能撤销绘制图形的部分（比如只撤销一个完整椭圆的一半）"——椭圆是一条笔迹，
    /// <c>Undo</c> 已经把它从层里拿掉了，屏幕上却还留半只：因为重烤时清的区域按<b>剩余</b>笔迹的包围盒算，
    /// 被撤那条没被盖到的地方从头到尾没有任何一步去擦它。而撤销栈里已经没有东西能再退一次，
    /// 用户唯一的出路是清屏重画。</para>
    /// <para>这里逐像素钉两件事：撤掉不相邻的那一条之后它<b>一个像素都不许留</b>，
    /// 而另一条<b>一个像素都不许少</b>。</para>
    /// </summary>
    [Fact]
    public void RebakeErasesWhatWasPreviouslyBaked_NotJustWhatRemains()
    {
        var buffer = Buffer();
        var left = StrokeFrom(10, 10, 30, 30);          // 两块互不相交的位置：一半被留在屏幕上才看得出来
        var right = StrokeFrom(60, 60, 90, 90);

        var erase = CanvasCompositor.Rebake(buffer, 100, 100, new[] { left, right },
            default, out var baked);
        Assert.False(erase.IsEmpty);
        Assert.NotEqual(Blank, Pixel(buffer, 20, 20));
        Assert.NotEqual(Blank, Pixel(buffer, 75, 75));

        // 撤掉右边那条：把"上一次烤过的那一片"交回去，它占过的地方必须整块回到空白
        erase = CanvasCompositor.Rebake(buffer, 100, 100, new[] { left }, baked, out var remaining);
        Assert.False(erase.IsEmpty);
        Assert.Equal(remaining, CanvasCompositor.Union(new[] { left.Bounds }, 100, 100));
        for (var y = 55; y <= 95; y++)
            for (var x = 55; x <= 95; x++)
                Assert.Equal(Blank, Pixel(buffer, x, y));       // 半只椭圆＝这一句会红
        Assert.NotEqual(Blank, Pixel(buffer, 20, 20));           // 另一条一格不少
        Assert.Equal(Blank, Pixel(buffer, 3, 97));               // 两块之外的地方一步都不许动
    }

    /// <summary>
    /// 一笔落下去却<b>不够格留下来</b>（橡皮点一下）时，它按下那刻已经烤进持久层的那一小片要还回去擦：
    /// 否则屏幕上留下一个"没有任何笔迹对应、撤销里也没有"的洞——比多一条笔迹更难解释。
    /// </summary>
    [Fact]
    public void ADroppedStrokeFootprintIsErasedToo()
    {
        var buffer = Buffer();
        var kept = StrokeFrom(10, 10, 40, 10);
        CanvasCompositor.Rebake(buffer, 100, 100, new[] { kept }, default, out var baked);
        Assert.NotEqual(Blank, Pixel(buffer, 25, 10));

        var dropped = StrokeFrom(25, 10, 25, 10, CanvasTool.Eraser);   // 橡皮原地按一下：不够格进层
        CanvasCompositor.Rebake(buffer, 100, 100, new[] { kept },
            CanvasCompositor.Union(new[] { baked, dropped.Bounds }, 100, 100), out _);
        Assert.NotEqual(Blank, Pixel(buffer, 25, 10));                 // 那一按不该把已画好的笔迹擦出一个洞
    }

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
