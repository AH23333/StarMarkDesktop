#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Abstractions.Capture;

namespace StarMark.Core.Canvas;

/// <summary>
/// 把笔迹合成分层窗的后备缓冲：<b>预乘 BGRA（uint，<c>B | G&lt;&lt;8 | R&lt;&lt;16 | A&lt;&lt;24</c>）</b>。
/// <para>
/// 这一层与截图标注那套 <c>AnnotationPainter</c> <b>刻意分开写</b>：那边永远画在不透明的底图上，
/// <c>Blend</c> 出口处把 alpha 钉成 255（交出去的 PNG 不能留透明洞）；而这块板子要的恰恰相反——
/// <b>没画到的地方必须是 alpha=0</b>，否则屏幕上就是一块涂满了东西的玻璃，穿透态下用户会看见一面墙。
/// 把两种 alpha 语义塞进同一个函数，改的那一次一定会撞坏另一条链。
/// </para>
/// <para>
/// <b>为什么是预乘</b>：<c>UpdateLayeredWindow</c> 的 DIB 就是 <c>AC_SRC_ALPHA</c> 预乘格式；
/// 存非预乘的话每次合成都要多一次除法，而"边缘发白"这类伪影正是预乘没做对的标准症状。
/// </para>
/// </summary>
public static class CanvasCompositor
{
    /// <summary>没有脏区时的返回值（<c>IsEmpty</c> 为真）。</summary>
    public static readonly IntRect Nothing = new(0, 0, 0, 0);

    /// <summary>整块板子擦干净（全 alpha=0，等于这块玻璃不存在）。</summary>
    public static void Clear(uint[] buffer) => Array.Clear(buffer, 0, buffer.Length);

    /// <summary>
    /// 画<b>整条</b>笔迹（清屏/撤销后的全量重画走这条）。返回真碰到的区域（已夹进画布），
    /// 调用方据此决定往屏幕上提交哪一块——<b>4K 全屏每帧整张提交是 33MB，规格 §16.7 明令禁止</b>。
    /// </summary>
    public static IntRect Paint(uint[] buffer, int width, int height, CanvasStroke stroke, double fade = 1d)
    {
        var points = stroke.Points;
        if (points.Count == 0) return Nothing;

        Stamp(buffer, width, height, stroke, points[0], fade);
        for (var i = 1; i < points.Count; i++)
            Walk(buffer, width, height, stroke, points[i - 1], points[i], fade);
        return Clamp(stroke.Bounds, width, height);
    }

    /// <summary>
    /// 只画<b>最后一段</b>（拖动中的增量）。这是画布能跟手的关键：每来一个点就全量重画整层，
    /// 笔迹一多就会从"跟手"掉到"一帧一顿"。
    /// <para>增量为什么安全：这里的合成是<b>取大</b>（见 <see cref="BlendInk"/>），
    /// 同一条笔迹重复涂同一像素不会变浓，所以"补画新的一段"与"整条重画"的结果一致。</para>
    /// </summary>
    public static IntRect PaintTail(uint[] buffer, int width, int height, CanvasStroke stroke, double fade = 1d)
    {
        var points = stroke.Points;
        if (points.Count == 0) return Nothing;
        if (points.Count == 1) return Stamp(buffer, width, height, stroke, points[0], fade);
        return Walk(buffer, width, height, stroke, points[^2], points[^1], fade);
    }

    /// <summary>按顺序全量重画一层（撤销、清屏之后的重算）。</summary>
    public static void PaintAll(uint[] buffer, int width, int height, IReadOnlyList<CanvasStroke> strokes)
    {
        Clear(buffer);
        foreach (var stroke in strokes) Paint(buffer, width, height, stroke);
    }

    /// <summary>把若干脏区合成一块（分层窗一次只吃一个矩形源）。</summary>
    public static IntRect Union(IReadOnlyCollection<IntRect> rects, int width, int height)
    {
        var x1 = int.MaxValue;
        var y1 = int.MaxValue;
        var x2 = int.MinValue;
        var y2 = int.MinValue;
        var any = false;
        foreach (var rect in rects)
        {
            if (rect.IsEmpty) continue;
            var clamped = Clamp(rect, width, height);
            if (clamped.IsEmpty) continue;                       // 整块都在画布外：不脏
            any = true;
            x1 = Math.Min(x1, clamped.X);
            y1 = Math.Min(y1, clamped.Y);
            x2 = Math.Max(x2, clamped.Right);
            y2 = Math.Max(y2, clamped.Bottom);
        }
        return any ? new IntRect(x1, y1, x2 - x1, y2 - y1) : Nothing;
    }

    /// <summary>夹进画布。<b>允许原点为负</b>（笔迹拖到屏幕外面），但宽高超不出边界。</summary>
    public static IntRect Clamp(IntRect rect, int width, int height)
    {
        var x1 = Math.Max(0, rect.X);
        var y1 = Math.Max(0, rect.Y);
        var x2 = Math.Min(width, rect.Right);
        var y2 = Math.Min(height, rect.Bottom);
        return x2 <= x1 || y2 <= y1 ? Nothing : new IntRect(x1, y1, x2 - x1, y2 - y1);
    }

    // ────────── 逐像素 ──────────

    private static IntRect Stamp(uint[] buffer, int width, int height, CanvasStroke stroke, PixelPoint p, double fade)
    {
        var radius = CanvasWidths.RadiusFor(stroke.Tool, stroke.Width);
        PaintDisc(buffer, width, height, stroke, p.X, p.Y, radius, fade);
        return Clamp(new IntRect(p.X - radius - 1, p.Y - radius - 1, radius * 2 + 3, radius * 2 + 3), width, height);
    }

    private static IntRect Walk(uint[] buffer, int width, int height, CanvasStroke stroke,
        PixelPoint a, PixelPoint b, double fade)
    {
        var radius = CanvasWidths.RadiusFor(stroke.Tool, stroke.Width);
        var steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        // 步长 1 像素：更密只是在同一像素上多叠几次（取大规则下无事发生），更疏会断线
        for (var i = 0; i <= steps; i++)
        {
            if (steps == 0)
            {
                PaintDisc(buffer, width, height, stroke, a.X, a.Y, radius, fade);
                break;
            }
            var t = (double)i / steps;
            PaintDisc(buffer, width, height, stroke,
                (int)Math.Round(a.X + (b.X - a.X) * t, MidpointRounding.AwayFromZero),
                (int)Math.Round(a.Y + (b.Y - a.Y) * t, MidpointRounding.AwayFromZero),
                radius, fade);
        }
        return Clamp(new IntRect(
            Math.Min(a.X, b.X) - radius - 1,
            Math.Min(a.Y, b.Y) - radius - 1,
            Math.Abs(b.X - a.X) + radius * 2 + 3,
            Math.Abs(b.Y - a.Y) + radius * 2 + 3), width, height);
    }

    /// <summary>
    /// 盖一个圆。<b>最外圈给一条 1 像素的覆盖度斜坡</b>（不是硬边）：分层窗没有抗锯齿可借用——
    /// <c>UpdateLayeredWindow</c> 是逐像素 alpha 合成，硬边圆直接画出来就是锯齿台阶；
    /// 而这条斜坡同时解释了"脏区为什么必须比半径多 1 像素"（少一像素就留一圈残影）。
    /// </summary>
    private static void PaintDisc(uint[] buffer, int width, int height, CanvasStroke stroke,
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
                var density = DensityFor(stroke.Tool, distance, core);
                if (density <= 0f) continue;
                BlendInk(buffer, y * width + x, stroke, ramp * density * factor);
            }
        }
    }

    /// <summary>
    /// 这支笔在这个距离上有多浓（0–1）。
    /// <para>
    /// <b>荧光笔的三层是规格给定的值</b>（内芯 90% / 中圈 45% / 外圈 25%，§16.4）：一层线性衰减
    /// 长得像雾而不像荧光笔，而"内芯最亮 + 外圈最淡"正是用户分辨"我在荧光还是在画"的视觉线索。
    /// </para>
    /// </summary>
    private static float DensityFor(CanvasTool tool, float distance, float coreRadius)
        => tool switch
        {
            CanvasTool.Highlighter => distance <= coreRadius ? 0.90f
                : distance <= coreRadius * 2f ? 0.45f
                : 0.25f,
            _ => 1f,
        };

    /// <summary>
    /// 把一支笔的圆点合进一个像素（<paramref name="index"/> 已是缓冲下标）。
    /// <para>
    /// <b>墨水取大、橡皮按比例减</b>：取大＝同一支笔来回涂不会越涂越浓（荧光笔"盖在字上还能看见字"
    /// 靠它成立），且后画的淡色不会把先画的浓色洗掉（荧光笔盖在黑笔上，黑笔仍透得出来——
    /// 真实荧光笔就是这样）。橡皮把整像素按 <c>1-覆盖度</c> 缩小，预乘关系因此继续成立。
    /// </para>
    /// </summary>
    private static void BlendInk(uint[] buffer, int index, CanvasStroke stroke, float coverage)
    {
        var destination = buffer[index];
        var destinationAlpha = (int)(destination >>> 24);

        if (stroke.Tool == CanvasTool.Eraser)
        {
            var keep = (int)Math.Round(destinationAlpha * (1d - coverage));
            if (keep <= 0) { buffer[index] = 0; return; }
            var blue = (int)(destination & 0xFF) * keep / destinationAlpha;
            var green = (int)(destination >> 8 & 0xFF) * keep / destinationAlpha;
            var red = (int)(destination >> 16 & 0xFF) * keep / destinationAlpha;
            buffer[index] = (uint)(blue | green << 8 | red << 16 | keep << 24);
            return;
        }

        var sourceAlpha = (int)Math.Round((stroke.EffectiveColorBgra >>> 24) * coverage);
        if (sourceAlpha <= destinationAlpha) return;             // 更淡的一笔不改已经更浓的像素
        buffer[index] = Premultiply(stroke.EffectiveColorBgra, sourceAlpha);
    }

    /// <summary>非预乘色 → 预乘到指定 alpha。<b>三个通道乘同一个数</b>：漏一个就是彩色描边。</summary>
    private static uint Premultiply(int colorBgra, int alpha)
    {
        var blue = (colorBgra & 0xFF) * alpha / 255;
        var green = (colorBgra >> 8 & 0xFF) * alpha / 255;
        var red = (colorBgra >> 16 & 0xFF) * alpha / 255;
        return (uint)(blue | green << 8 | red << 16 | alpha << 24);
    }
}
