#nullable enable
using System;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using StarMark.Integrations.Canvas;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <b>合成核的对差分测试</b>：性能改写后的逐像素循环必须与改写前<b>逐像素同结果</b>。
/// <para>
/// 为什么值得在这里留一份"上一版算法"：<c>CanvasCompositor</c> 的热循环被重写了一次
/// （把笔色/浓度/预乘从像素循环里提出来、几何判据从 d 改成 d²、覆盖度=1 的那一档直接写预乘好的像素、
/// 取大规则下"不可能更浓"的像素一步跳过）。这类改动的症状只在屏幕上看得见——
/// 而真机验收时"荧光笔不卡了"和"荧光笔颜色不对"是同一次修改带来的，一旦只报前者就漏了后者。
/// 所以这里把<b>旧算式原样留一份</b>当参照，逐像素比对，而不是靠人眼看。
/// </para>
/// <para>
/// 参照实现<b>不许"顺手优化"</b>：它存在的唯一理由就是复现旧行为，包括那两次 <c>Math.Round</c>
/// 与每像素一次 <c>MathF.Sqrt</c>。哪天它被改了，本文件就失去了意义。
/// </para>
/// <para>
/// 批次 S4-⑥ 又添了第二份参照（<c>PaintGlowRef</c>）：光晕那一团改成"逐行只算一次开方"之后，
/// 少算一像素没人报错、多算一像素没人看得见，只有对差分能说话。
/// </para>
/// </summary>
public sealed class CanvasKernelIdentityTests
{
    private const uint Blank = LayeredCanvasWindow.BlankPixel;
    private const int Yellow = 0x20A0F023;         // 任选一种不透明 BGRA（B=0x23 G=A0 R=F0）
    private const int Black = 0x00000000;

    /// <summary>不透明的出厂红（与 <c>CanvasInkTests</c> 同一支笔色）：光晕按 alpha 取大，浓度上限要一起被测到。</summary>
    private const int Opaque = unchecked((int)0xFFE81123);

    private static uint[] Blank_(int width, int height)
    {
        var buffer = new uint[width * height];
        Array.Fill(buffer, Blank);
        return buffer;
    }

    private static CanvasStroke Stroke(CanvasTool tool, int colour, int width, PixelPoint first)
        => new(tool, colour, width, first);

    private static CanvasStroke Zig(CanvasTool tool, int colour, int width, int points, int dx, int dy)
    {
        var stroke = Stroke(tool, colour, width, new PixelPoint(12, 14));
        for (var i = 0; i < points; i++)
            stroke.AddPoint(new PixelPoint(12 + (i + 1) * dx, 14 + (i % 3 - 1) * dy));
        return stroke;
    }

    // ────────── 参照实现（＝批次 WG 之前的那段代码，逐字搬来） ──────────

    /// <summary>
    /// 改动前的 <c>PaintGlow</c>（批次 S4-⑥ 之前那一版：整块方框逐像素，圆外那句 <c>distance &gt; reach</c> 当场跳过）。
    /// <b>同样不许"顺手优化"</b>——它存在的唯一理由就是复现旧行为，好让"逐行算跨度"这一刀被证伪或证实。
    /// </summary>
    private static IntRect PaintGlowRef(uint[] buffer, int width, int height, PixelPoint center, int radius, int colorBgra)
    {
        if (radius <= 0) return CanvasCompositor.Nothing;
        var reach = radius;
        var top = Math.Max(0, center.Y - reach);
        var bottom = Math.Min(height - 1, center.Y + reach);
        var left = Math.Max(0, center.X - reach);
        var right = Math.Min(width - 1, center.X + reach);
        for (var y = top; y <= bottom; y++)
        {
            var dy = y - center.Y;
            for (var x = left; x <= right; x++)
            {
                var dx = x - center.X;
                var distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance > reach) continue;
                var density = 1f - distance / reach;
                var index = y * width + x;
                var alpha = (int)Math.Round((colorBgra >>> 24) * density);
                if (alpha <= (int)(buffer[index] >>> 24)) continue;
                buffer[index] = PremultiplyRef(colorBgra, alpha);
            }
        }
        return CanvasCompositor.Clamp(new IntRect(center.X - reach, center.Y - reach, reach * 2 + 1, reach * 2 + 1), width, height);
    }

    private static void PaintRef(uint[] buffer, int width, int height, CanvasStroke stroke, double fade)
    {
        var points = stroke.Points;
        if (points.Count == 0) return;
        StampRef(buffer, width, height, stroke, points[0], fade);
        for (var i = 1; i < points.Count; i++)
            WalkRef(buffer, width, height, stroke, points[i - 1], points[i], fade);
    }

    private static void StampRef(uint[] buffer, int width, int height, CanvasStroke stroke, PixelPoint p, double fade)
        => PaintDiscRef(buffer, width, height, stroke, p.X, p.Y, CanvasWidths.RadiusFor(stroke.Tool, stroke.Width), fade);

    private static void WalkRef(uint[] buffer, int width, int height, CanvasStroke stroke,
        PixelPoint a, PixelPoint b, double fade)
    {
        var radius = CanvasWidths.RadiusFor(stroke.Tool, stroke.Width);
        var steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        for (var i = 0; i <= steps; i++)
        {
            if (steps == 0)
            {
                PaintDiscRef(buffer, width, height, stroke, a.X, a.Y, radius, fade);
                return;
            }
            var t = (double)i / steps;
            PaintDiscRef(buffer, width, height, stroke,
                (int)Math.Round(a.X + (b.X - a.X) * t, MidpointRounding.AwayFromZero),
                (int)Math.Round(a.Y + (b.Y - a.Y) * t, MidpointRounding.AwayFromZero),
                radius, fade);
        }
    }

    private static void PaintDiscRef(uint[] buffer, int width, int height, CanvasStroke stroke,
        int cx, int cy, int radius, double fade)
    {
        var reach = radius + 1;
        var top = Math.Max(0, cy - reach);
        var bottom = Math.Min(height - 1, cy + reach);
        var left = Math.Max(0, cx - reach);
        var right = Math.Min(width - 1, cx + reach);
        var factor = (float)Math.Clamp(fade, 0d, 1d);
        var core = stroke.Tool == CanvasTool.Highlighter ? Math.Max(1f, stroke.Width / 4f) : 0f;
        for (var y = top; y <= bottom; y++)
        {
            var dy = y - cy;
            for (var x = left; x <= right; x++)
            {
                var dx = x - cx;
                var distance = MathF.Sqrt(dx * dx + dy * dy);
                var ramp = Math.Clamp(radius + 0.5f - distance, 0f, 1f);
                if (ramp <= 0f) continue;
                var density = DensityRef(stroke.Tool, distance, core);
                if (density <= 0f) continue;
                BlendRef(buffer, y * width + x, stroke, ramp * density * factor);
            }
        }
    }

    private static float DensityRef(CanvasTool tool, float distance, float coreRadius)
        => tool switch
        {
            CanvasTool.Highlighter => distance <= coreRadius ? 0.90f
                : distance <= coreRadius * 2f ? 0.45f
                : 0.25f,
            _ => 1f,
        };

    private static void BlendRef(uint[] buffer, int index, CanvasStroke stroke, float coverage)
    {
        var destination = buffer[index];
        var destinationAlpha = (int)(destination >>> 24);
        if (stroke.Tool == CanvasTool.Eraser)
        {
            var keep = (int)Math.Round(destinationAlpha * (1d - coverage));
            if (keep <= (int)(Blank >>> 24))
            {
                buffer[index] = Blank;
                return;
            }
            var blue = (int)(destination & 0xFF) * keep / destinationAlpha;
            var green = (int)(destination >> 8 & 0xFF) * keep / destinationAlpha;
            var red = (int)(destination >> 16 & 0xFF) * keep / destinationAlpha;
            buffer[index] = (uint)(blue | green << 8 | red << 16 | keep << 24);
            return;
        }
        var sourceAlpha = (int)Math.Round((stroke.EffectiveColorBgra >>> 24) * coverage);
        if (sourceAlpha <= destinationAlpha) return;
        buffer[index] = PremultiplyRef(stroke.EffectiveColorBgra, sourceAlpha);
    }

    private static uint PremultiplyRef(int colorBgra, int alpha)
    {
        var blue = (colorBgra & 0xFF) * alpha / 255;
        var green = (colorBgra >> 8 & 0xFF) * alpha / 255;
        var red = (colorBgra >> 16 & 0xFF) * alpha / 255;
        return (uint)(blue | green << 8 | red << 16 | alpha << 24);
    }

    // ────────── 逐像素比对 ──────────

    private static int FirstDifference(uint[] want, uint[] got)
    {
        for (var i = 0; i < want.Length; i++)
            if (want[i] != got[i]) return i;
        return -1;
    }

    private static void AssertSame(uint[] want, uint[] got, string which)
    {
        var at = FirstDifference(want, got);
        Assert.True(at < 0, at < 0 ? string.Empty :
            $"{which}：第 {at / 120} 行第 {at % 120} 列不同（参照 {want[at]:X8} vs 现在 {got[at]:X8}）");
    }

    public static TheoryData<CanvasTool, int, double> Cases()
    {
        var data = new TheoryData<CanvasTool, int, double>();
        foreach (var tool in new[] { CanvasTool.Pen, CanvasTool.Highlighter, CanvasTool.Eraser })
            foreach (var width in new[] { 1, 2, 3, 4, 5, 7, 8, 9, 11, 13, 16, 18, 21 })
                foreach (var fade in new[] { 1d, 0.997d, 0.5d, 0.25d, 0.03d, 0d })
                    data.Add(tool, width, fade);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RewrittenKernelIsPixelIdenticalToTheOldOne(CanvasTool tool, int width, double fade)
    {
        const int w = 120;
        const int h = 90;
        var stroke = Zig(tool, tool == CanvasTool.Highlighter ? Yellow : Black, width, 14, 7, 5);
        var want = Blank_(w, h);
        var got = Blank_(w, h);
        PaintRef(want, w, h, stroke, fade);
        CanvasCompositor.Paint(got, w, h, stroke, fade);
        AssertSame(want, got, $"{tool} 宽{width} 浓度{fade:0.###}");
    }

    [Fact]
    public void HighlighterOverExistingInkIsStillIdentical()
    {
        // 叠在别的墨上才检验得到"取大"那条跳过判据：参照实现里它靠 sourceAlpha <= destinationAlpha
        const int w = 120;
        const int h = 90;
        var pen = Zig(CanvasTool.Pen, Black, 9, 12, 6, 4);
        var glow = Zig(CanvasTool.Highlighter, Yellow, 18, 16, 5, 6);
        var want = Blank_(w, h);
        var got = Blank_(w, h);
        PaintRef(want, w, h, pen, 1d);
        PaintRef(want, w, h, glow, 0.4d);
        CanvasCompositor.Paint(got, w, h, pen, 1d);
        CanvasCompositor.Paint(got, w, h, glow, 0.4d);
        AssertSame(want, got, "荧光压黑字");
    }

    [Fact]
    public void EraserOverTwoLayersIsStillIdentical()
    {
        const int w = 120;
        const int h = 90;
        var pen = Zig(CanvasTool.Pen, Black, 18, 12, 6, 4);
        var eraser = Zig(CanvasTool.Eraser, Black, 4, 10, 9, 3);
        var want = Blank_(w, h);
        var got = Blank_(w, h);
        PaintRef(want, w, h, pen, 1d);
        PaintRef(want, w, h, eraser, 1d);
        PaintRef(want, w, h, eraser, 0.5d);            // 同一条橡皮走两次：正是"擦过头"那一条
        CanvasCompositor.Paint(got, w, h, pen, 1d);
        CanvasCompositor.Paint(got, w, h, eraser, 1d);
        CanvasCompositor.Paint(got, w, h, eraser, 0.5d);
        AssertSame(want, got, "橡皮叠两遍");
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(1d)]
    public void ClippingToTheDirtyRectChangesNoSubmittedPixel(double fade)
    {
        // 裁剪后的帧必须与"整条重画"在提交区里逐像素相同——否则症状是"画布上有一块永远淡不下去"
        const int w = 120;
        const int h = 90;
        var stroke = Zig(CanvasTool.Highlighter, Yellow, 9, 18, 5, 4);
        var clip = new IntRect(30, 20, 46, 33);
        var want = Blank_(w, h);
        var got = Blank_(w, h);
        PaintRef(want, w, h, stroke, fade);
        CanvasCompositor.PaintClipped(got, w, h, stroke, clip, fade);
        for (var y = clip.Y; y < clip.Bottom; y++)
            for (var x = clip.X; x < clip.Right; x++)
                Assert.True(want[y * w + x] == got[y * w + x],
                    $"裁剪区内在 ({x},{y}) 不同：参照 {want[y * w + x]:X8} vs {got[y * w + x]:X8}");
    }

    [Fact]
    public void EverythingOutsideTheClipStaysExactlyAsItWas()
    {
        // 裁剪区外一个像素都不许动：那是上一帧已经提交对的内容，动了就是要复原却没复原的地方
        const int w = 120;
        const int h = 90;
        var earlier = Zig(CanvasTool.Pen, Black, 18, 10, 8, 5);
        var stroke = Zig(CanvasTool.Highlighter, Yellow, 9, 18, 5, 4);
        var clip = new IntRect(30, 20, 46, 33);
        var buffer = Blank_(w, h);
        CanvasCompositor.Paint(buffer, w, h, earlier, 1d);
        var before = (uint[])buffer.Clone();
        CanvasCompositor.PaintClipped(buffer, w, h, stroke, clip, 1d);
        for (var i = 0; i < before.Length; i++)
        {
            var x = i % w;
            var y = i / w;
            if (x >= clip.X && x < clip.Right && y >= clip.Y && y < clip.Bottom) continue;
            Assert.True(before[i] == buffer[i], $"裁剪区外第 {x},{y} 被改了");
        }
    }

    [Fact]
    public void TailPaintStillEqualsTheFullRepaint()
    {
        // 取大规则那条老不变量：增量补尾与整条重画逐像素同结果（拖动帧与最终帧必须一样）
        const int w = 120;
        const int h = 90;
        var want = Blank_(w, h);
        var got = Blank_(w, h);
        var full = Zig(CanvasTool.Highlighter, Yellow, 9, 16, 6, 5);
        CanvasCompositor.Paint(want, w, h, full, 1d);
        var points = full.Points;
        var tail = new CanvasStroke(CanvasTool.Highlighter, Yellow, 9, points[0]);
        CanvasCompositor.Paint(got, w, h, tail, 1d);
        for (var i = 1; i < points.Count; i++)
        {
            tail.AddPoint(points[i]);
            CanvasCompositor.PaintTail(got, w, h, tail, 1d);
        }
        AssertSame(want, got, "增量补尾");
    }

    // ────────── 批次 S4-⑥：光晕那一团改成"逐行只算一次开方" ──────────

    /// <summary>半径 160 DIP 换算到 150%/250% 屏上的物理像素，加上从前那档小光晕，一起进用例。</summary>
    public static TheoryData<int, PixelPoint> GlowCases()
    {
        var data = new TheoryData<int, PixelPoint>();
        foreach (var radius in new[] { 1, 3, 16, 60, 240, 400 })
            foreach (var center in new[]
            {
                new PixelPoint(100, 75),      // 居中：整块圆都落在画布内
                new PixelPoint(0, 0),         // 左上角：三条夹边同时生效
                new PixelPoint(199, 149),     // 右下角
                new PixelPoint(10, 140),      // 只夹左与下
                new PixelPoint(199, 10),      // 只夹右与上
            })
                data.Add(radius, center);
        return data;
    }

    /// <summary>
    /// <b>逐像素与逐脏区都同结果</b>：新写法每行按圆算左右端（少算约 21% 的圆角像素），
    /// 只要那一行的跨度少算 1 像素，症状就是"光晕的外缘缺一条"；多算则没人看得见——所以比对必须整幅做，
    /// 而<b>脏区也要比</b>：脏区是"这一帧哪一块交给系统"，画到了却没进脏区＝那块像素永远不上屏
    /// （症状＝光晕换位置时后面拖一条旧光）。
    /// </summary>
    [Theory]
    [MemberData(nameof(GlowCases))]
    public void GlowRowPruningIsIdenticalToTheSquareLoop(int radius, PixelPoint center)
    {
        const int w = 200;
        const int h = 150;
        foreach (var colour in new[] { Yellow, Opaque })
            foreach (var surface in new[] { "空白", "全零", "已有浓墨" })
            {
                var want = Surface(surface, w, h);
                var got = Surface(surface, w, h);
                var wantRect = PaintGlowRef(want, w, h, center, radius, colour);
                var gotRect = CanvasCompositor.PaintGlow(got, w, h, center, radius, colour);
                var at = FirstDifference(want, got);
                Assert.True(at < 0, at < 0 ? string.Empty :
                    $"{surface}面 r={radius} 圆心({center.X},{center.Y}) 色 {colour:X8}：" +
                    $"第 {at / w} 行第 {at % w} 列不同（参照 {want[at]:X8} vs 现在 {got[at]:X8}）");
                Assert.True(wantRect == gotRect,
                    $"{surface}面 r={radius} 圆心({center.X},{center.Y})：脏区不同（参照 {wantRect} vs 现在 {gotRect}）");
            }
    }

    /// <summary>三种"叠光之前那块玻璃长什么样"：空白（alpha=1）、全零（鼠标眼里没有这块玻璃）、已有一笔浓墨（取大要生效）。</summary>
    private static uint[] Surface(string kind, int width, int height)
    {
        var buffer = new uint[width * height];
        Array.Fill(buffer, kind == "空白" ? Blank : 0u);
        if (kind != "已有浓墨") return buffer;
        var ink = new CanvasStroke(CanvasTool.Pen, Opaque, 9, new PixelPoint(width / 2, height / 2));
        ink.AddPoint(new PixelPoint(width / 2 + 30, height / 2 + 20));
        CanvasCompositor.Paint(buffer, width, height, ink, 1d);
        return buffer;
    }

    /// <summary>
    /// 半径 0／负数：既不许画东西，也不许交出一块脏区。
    /// <para>合并半径之后 <see cref="CursorCircle"/> 会把读不到的缩放兜到 1×，所以这一臂今天不可达；
    /// 但它钉的是"<b>算不出圆</b>"这件事的兜底形状——少了那句守卫，0 会走到 <c>1 - d/0</c> 上，
    /// 而负半径会算出一块原点为负的矩形，两者都表现为"提交了一次什么都没改的脏区"。</para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-240)]
    public void AGlowWithNoRadiusPaintsNothingAndReportsNothing(int radius)
    {
        const int w = 60;
        const int h = 40;
        var buffer = Blank_(w, h);
        var dirty = CanvasCompositor.PaintGlow(buffer, w, h, new PixelPoint(30, 20), radius, Opaque);
        Assert.Equal(CanvasCompositor.Nothing, dirty);
        Assert.All(buffer, pixel => Assert.Equal(Blank, pixel));
    }

    /// <summary>
    /// <b>交出去的脏区必须盖住它真的画到的每一颗像素</b>——这是光晕唯一"看不见"的失败方式：
    /// 缓冲改了、脏区没带上那一块，系统就按旧内容贴，屏幕上留一条旧光晕。
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(199, 149)]
    [InlineData(100, 75)]
    [InlineData(5, 143)]
    public void GlowDirtyRectCoversEveryPixelItPainted(int cx, int cy)
    {
        const int w = 200;
        const int h = 150;
        var before = Surface("空白", w, h);
        var buffer = (uint[])before.Clone();
        var dirty = CanvasCompositor.PaintGlow(buffer, w, h, new PixelPoint(cx, cy), 240, Opaque);
        var painted = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (buffer[y * w + x] == before[y * w + x]) continue;
                painted++;
                Assert.True(x >= dirty.X && x < dirty.Right && y >= dirty.Y && y < dirty.Bottom,
                    $"圆心({cx},{cy}) 画到 ({x},{y}) 却没进脏区 {dirty}");
            }
        Assert.True(painted > 1000, $"半径 240 的圆不可能只画 {painted} 颗像素——这条判据在空转");
    }
}
