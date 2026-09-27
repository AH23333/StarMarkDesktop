#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Abstractions.Capture;
using StarMark.Integrations.Canvas;

namespace StarMark.Core.Canvas;

/// <summary>
/// 把笔迹合成分层窗的后备缓冲：<b>预乘 BGRA（uint，<c>B | G&lt;&lt;8 | R&lt;&lt;16 | A&lt;&lt;24</c>）</b>。
/// <para>
/// 这一层与截图标注那套 <c>AnnotationPainter</c> <b>刻意分开写</b>：那边永远画在不透明的底图上，
/// <c>Blend</c> 出口处把 alpha 钉成 255（交出去的 PNG 不能留透明洞）；而这块板子要的恰恰相反——
/// <b>没画到的地方必须是"空白"（<see cref="LayeredCanvasWindow.BlankPixel"/>：alpha=1，看不见但点得着）</b>，
/// 既不能是实色（那是一面墙），也不能是 0（分层窗的命中测试会跳过 alpha=0，那块地方就画不上）。
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
    private static IntRect Whole(int width, int height) => new(0, 0, width, height);

    public static readonly IntRect Nothing = new(0, 0, 0, 0);

    /// <summary>
    /// 整块板子擦干净。<b>填的是"空白"（alpha=1）而不是 0</b>：0 会让分层窗在这一块上直接漏掉鼠标，
    /// 擦过的地方就再也画不上（<see cref="LayeredCanvasWindow.BlankPixel"/> 记着为什么）。
    /// </summary>
    public static void Clear(uint[] buffer) => Array.Fill(buffer, LayeredCanvasWindow.BlankPixel);

    /// <summary>
    /// 只擦一块区域。<b>撤销与"擦掉一段荧光"靠的是这个而不是整块擦</b>：
    /// 4K 上一次 <c>Array.Clear</c> 是 33MB，每收一笔都整块擦就等于自己把自己卡住。
    /// </summary>
    public static void ClearRect(uint[] buffer, int width, int height, IntRect rect)
    {
        var r = Clamp(rect, width, height);
        for (var y = r.Y; y < r.Bottom; y++)
            Array.Fill(buffer, LayeredCanvasWindow.BlankPixel, y * width + r.X, r.Width);
    }

    /// <summary>把一块区域从源缓冲拷进目标缓冲（合成时"先铺持久层，再叠荧光段与光晕"就靠它）。</summary>
    public static void CopyRect(uint[] source, uint[] target, int width, int height, IntRect rect)
    {
        var r = Clamp(rect, width, height);
        for (var y = r.Y; y < r.Bottom; y++)
        {
            var from = y * width + r.X;
            Array.Copy(source, from, target, from, r.Width);
        }
    }

    /// <summary>
    /// 画<b>整条</b>笔迹（清屏/撤销后的全量重画走这条）。返回真碰到的区域（已夹进画布），
    /// 调用方据此决定往屏幕上提交哪一块——<b>4K 全屏每帧整张提交是 33MB，规格 §16.7 明令禁止</b>。
    /// </summary>
    public static IntRect Paint(uint[] buffer, int width, int height, CanvasStroke stroke, double fade = 1d)
        => PaintClipped(buffer, width, height, stroke, Whole(width, height), fade);

    /// <summary>
    /// 只往 <paramref name="clip"/> 里画（<b>结果与不裁剪时在那一块上逐像素相同</b>：取大规则下
    /// 每一笔对每个像素的结论只看它自己，笔迹之间不互相削弱）。
    /// <para>
    /// 为什么必须有它：荧光笔每帧要重画的是"这次真正脏的那一小块"，而 <see cref="Paint"/> 会连着
    /// 已经对的那些像素一起重算——一条划了两千像素的粗荧光笔，每帧几百万次逐像素运算，
    /// 实测 4K 粗档 <b>643 ms/帧</b>（真机症状："荧光笔绘制过程非常卡"）。
    /// </para>
    /// </summary>
    public static IntRect PaintClipped(uint[] buffer, int width, int height, CanvasStroke stroke,
        IntRect clip, double fade = 1d)
    {
        var points = stroke.Points;
        if (points.Count == 0) return Nothing;
        var area = Clamp(clip, width, height);
        var brush = new Brush(stroke, fade);

        Stamp(buffer, width, height, area, brush, points[0]);
        for (var i = 1; i < points.Count; i++)
            Walk(buffer, width, height, area, brush, points[i - 1], points[i]);
        return Clamp(stroke.Bounds, width, height);
    }

    /// <summary>
    /// 只画<b>最后一段</b>（拖动中的增量）。这是画布能跟手的关键：每来一个点就全量重画整层，
    /// 笔迹一多就会从"跟手"掉到"一帧一顿"。
    /// <para>增量为什么安全：这里的合成是<b>取大</b>（见 <see cref="PaintDisc"/> 里那句"比这一层更浓的像素保持原样"），
    /// 同一条笔迹重复涂同一像素不会变浓，所以"补画新的一段"与"整条重画"的结果一致。</para>
    /// </summary>
    public static IntRect PaintTail(uint[] buffer, int width, int height, CanvasStroke stroke, double fade = 1d)
    {
        var points = stroke.Points;
        if (points.Count == 0) return Nothing;
        var brush = new Brush(stroke, fade);
        if (points.Count == 1) return Stamp(buffer, width, height, Whole(width, height), brush, points[0]);
        return Walk(buffer, width, height, Whole(width, height), brush, points[^2], points[^1]);
    }

    /// <summary>按顺序全量重画一层（撤销、清屏之后的重算）。</summary>
    public static void PaintAll(uint[] buffer, int width, int height, IReadOnlyList<CanvasStroke> strokes)
    {
        Clear(buffer);
        foreach (var stroke in strokes) Paint(buffer, width, height, stroke);
    }

    /// <summary>
    /// 从"现在还剩的笔迹"<b>整块重烤</b>持久层：先擦掉 <paramref name="toErase"/>，再把剩下的每条按顺序画回去。
    /// <para>擦哪一块由调用方说清楚，而不是这里"按剩下的笔迹算"——<b>撤销一条之后，它占过的地方
    /// 往往不在剩余笔迹的包围盒里</b>，只擦剩下的就等于什么都不擦：屏幕上留下半只椭圆，
    /// 而撤销栈里已经没有东西能把它退掉（真机反馈"只能撤销绘制图形的部分"）。
    /// 调用方要把"上一次烤过的那一片"与"这次被丢掉的那一条"并进来传进来。</para>
    /// <para>也不整块擦：4K 一帧是 33MB。橡皮那条不幂等（按比例减 alpha，走两次擦过头），
    /// 所以必须一次画成，不能增量补。</para>
    /// </summary>
    /// <param name="toErase">要清回空白的区域（剩余笔迹之外的部分也必须包含在内）。</param>
    /// <param name="remaining">重烤之后<b>还剩的笔迹</b>占多大一片——调用方把它记下来，
    /// 下一次撤销时就是"上一次烤过的那一片"。</param>
    /// <returns>这次真的擦／画过的那一片（调用方拿去弄脏屏幕）；什么都不用做时返回空。</returns>
    public static IntRect Rebake(uint[] buffer, int width, int height, IReadOnlyList<CanvasStroke> strokes,
        IntRect toErase, out IntRect remaining)
    {
        var bounds = new IntRect[strokes.Count];
        for (var i = 0; i < strokes.Count; i++) bounds[i] = strokes[i].Bounds;
        remaining = Union(bounds, width, height);
        var erase = Union(new[] { remaining, toErase }, width, height);
        if (erase.IsEmpty) return default;
        ClearRect(buffer, width, height, erase);
        foreach (var stroke in strokes) Paint(buffer, width, height, stroke);
        return erase;
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

    /// <summary>
    /// 在光标处盖一团光晕（荧光笔态下"我现在拿的是荧光笔"的常驻提示）。
    /// <para>
    /// 与笔迹同一套取大规则，但它是<b>每帧重画</b>的：光晕跟着鼠标走，旧位置靠"那一块从持久层重铺"复原
    /// （调用方把新旧两块都算进脏区）。因此这里不需要缓存一张光晕贴图——一次 128×128 的圆盘
    /// 就是一帧的全部开销，而缓存位图会多一处"颜色改了没重建"的失效点。
    /// </para>
    /// </summary>
    public static IntRect PaintGlow(uint[] buffer, int width, int height, PixelPoint center, int radius, int colorBgra)
    {
        if (radius <= 0) return Nothing;
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
                // 中心最亮、外缘淡到 0：与荧光笔的"三层"不同，这里要的是连续的一团光
                var density = 1f - distance / reach;
                var index = y * width + x;
                var alpha = (int)Math.Round((colorBgra >>> 24) * density);
                if (alpha <= (int)(buffer[index] >>> 24)) continue;
                buffer[index] = Premultiply(colorBgra, alpha);
            }
        }
        return Clamp(new IntRect(center.X - reach, center.Y - reach, reach * 2 + 1, reach * 2 + 1), width, height);
    }

    /// <summary>
    /// 把一块预乘的画布墨叠到一份<b>不透明</b>的 BGRA 帧上（<see cref="Paint"/> 的目标是透明玻璃，
    /// 这里的目标是截屏／存图／贴图那一类实底画面——两种 alpha 语义不能共用一个函数）。
    /// <param name="bgra">帧缓冲（BGRA 字节序）。就地改：调用方交给它的本来就是这一帧的副本。</param>
    /// <param name="frameWidth">整帧宽度（行跨距按它算）。</param>
    /// <param name="at">这块墨贴到帧上的位置（帧坐标，可为负——画布上"拖出屏幕外"的那部分会被夹掉）。</param>
    /// </summary>
    public static void OverlayOntoFrame(byte[] bgra, int frameWidth, int frameHeight,
        IntRect at, uint[] ink, int inkWidth, int inkHeight)
    {
        for (var y = 0; y < inkHeight; y++)
        {
            var frameY = at.Y + y;
            if (frameY < 0 || frameY >= frameHeight) continue;
            for (var x = 0; x < inkWidth; x++)
            {
                var frameX = at.X + x;
                if (frameX < 0 || frameX >= frameWidth) continue;
                var pixel = ink[y * inkWidth + x];
                var alpha = (int)(pixel >>> 24);
                // "空白"那一档（alpha=1）也跳过：它是给分层窗命中测试留的，不是墨。
                // 不跳的话整张快照会被蒙上一层 1/255 的黑——差得看不见，但它确实存在。
                if (alpha <= (int)(LayeredCanvasWindow.BlankPixel >>> 24)) continue;
                var index = (frameY * frameWidth + frameX) * 4;
                var keep = 255 - alpha;                    // 帧是实底：源 over 目标，且墨本身已预乘，直接相加
                bgra[index] = (byte)((pixel & 0xFF) + (bgra[index] & 0xFF) * keep / 255);
                bgra[index + 1] = (byte)((pixel >> 8 & 0xFF) + (bgra[index + 1] & 0xFF) * keep / 255);
                bgra[index + 2] = (byte)((pixel >> 16 & 0xFF) + (bgra[index + 2] & 0xFF) * keep / 255);
                bgra[index + 3] = 255;                     // 交出去的图不能留透明洞
            }
        }
    }

    // ────────── 逐像素 ──────────

    /// <summary>
    /// 荧光笔三层的浓度（规格 §16.4 给定的值：内芯 90% / 中圈 45% / 外圈 25%）。
    /// <para>
    /// 一层线性衰减长得像雾而不像荧光笔，而"内芯最亮 + 外圈最淡"正是用户分辨"我在荧光还是在画"的视觉线索。
    /// <b>这三个数只在这里出现一次</b>：圆盘的"覆盖度=1"那一档直接写预乘好的像素，
    /// 边缘斜坡要按同一张表现算浓度，两处若各写一份就会长成"内芯和斜坡对不上"。
    /// </para>
    /// </summary>
    private const float CoreDensity = 0.90f, MidDensity = 0.45f, OuterDensity = 0.25f;

    /// <summary>
    /// 一支笔在某一浓度（<c>fade</c>）下的全部预设值。
    /// <para>
    /// <b>为什么要有它</b>：一帧要判几百万个像素，而"这笔是什么色、多粗、几成浓"在整条笔迹里一个字都不变。
    /// 原先这些算式（读笔色、算 alpha、预乘三个通道、开平方）直接写在像素循环里，Debug 构建下每像素
    /// 几十纳秒，一条 4K 上的粗荧光笔就把 UI 线程钉死在几百毫秒一帧。
    /// </para>
    /// <para>
    /// <b>几何判据一律用 d² 而不是 d</b>：d² 是整数，而 (r±0.5)²、core²、(2·core)² 都落在 float 能精确
    /// 表示的 x.0／x.25 上，开平方又是单调且正确舍入的，所以"d² 与它比大小"与"开完平方再比"同结论。
    /// 于是圆盘内部那一大片一个平方根都不用算，只有外缘那条约 1 像素宽的斜坡需要。
    /// </para>
    /// </summary>
    private sealed class Brush
    {
        public readonly int Radius;
        public readonly int Scan;          // radius + 1：外缘斜坡要多吃一像素，脏区同理
        public readonly int FullCoverMax;  // d² 不超过它 ⇒ 覆盖度必为 1（斜坡内侧）
        public readonly int SkipFrom;      // d² 不小于它 ⇒ 这个像素一点都碰不到（斜坡外侧）
        public readonly float RimStart;    // radius + 0.5f：斜坡的起点
        public readonly bool Highlighter;
        public readonly bool Eraser;
        public readonly int CoreMax;       // 内芯的 d² 上界
        public readonly int MidMax;        // 中圈的 d² 上界
        public readonly uint SolidPixel;   // 覆盖度=1 时直接写入的预乘像素（画笔／橡皮之外那一档）
        public readonly uint CorePixel;
        public readonly uint MidPixel;
        public readonly uint OuterPixel;
        public readonly int MaxAlpha;      // 这一笔最多浓到什么程度：已经比它浓的像素一步跳过
        public readonly int ColorBgra;     // 非预乘笔色（斜坡那圈按覆盖度现算）
        public readonly int BaseAlpha;     // 笔色的 alpha 字节
        public readonly float Factor;      // 整笔的淡出乘数（0–1）

        public Brush(CanvasStroke stroke, double fade)
        {
            var tool = stroke.Tool;
            Highlighter = tool == CanvasTool.Highlighter;
            Eraser = tool == CanvasTool.Eraser;
            var core = Highlighter ? Math.Max(1f, stroke.Width / 4f) : 0f;      // 内芯半径＝笔宽的 1/4
            var coreOuter = core * 2f;
            var colour = stroke.EffectiveColorBgra;
            Radius = CanvasWidths.RadiusFor(tool, stroke.Width);
            Scan = Radius + 1;
            RimStart = Radius + 0.5f;
            FullCoverMax = FloorOf((Radius - 0.5f) * (Radius - 0.5f));
            SkipFrom = FloorOf((Radius + 0.5f) * (Radius + 0.5f)) + 1;
            CoreMax = FloorOf(core * core);
            MidMax = FloorOf(coreOuter * coreOuter);
            Factor = (float)Math.Clamp(fade, 0d, 1d);
            ColorBgra = colour;
            BaseAlpha = colour >>> 24;
            SolidPixel = PixelFor(BaseAlpha, 1f * Factor, colour);
            CorePixel = PixelFor(BaseAlpha, CoreDensity * Factor, colour);
            MidPixel = PixelFor(BaseAlpha, MidDensity * Factor, colour);
            OuterPixel = PixelFor(BaseAlpha, OuterDensity * Factor, colour);
            MaxAlpha = Highlighter ? (int)(CorePixel >>> 24) : (int)(SolidPixel >>> 24);
        }

        /// <summary>覆盖度=1 的那个像素上，这一笔给多浓（三层由 d² 决定）。</summary>
        public uint LayerPixel(int d2) => Highlighter
            ? d2 <= CoreMax ? CorePixel : d2 <= MidMax ? MidPixel : OuterPixel
            : SolidPixel;

        /// <summary>边缘斜坡那一圈：浓度还要再乘一条从 1 到 0 的覆盖度。</summary>
        public float Density(int d2) => Highlighter
            ? d2 <= CoreMax ? CoreDensity : d2 <= MidMax ? MidDensity : OuterDensity
            : 1f;

        /// <summary>
        /// 边缘斜坡上的像素（<paramref name="ramp"/> 是那条从 1 到 0 的覆盖度）。
        /// <b>乘法次序与重写前那句逐像素算式一致</b>：<c>(ramp × 层浓度) × 整笔浓度</c>——
        /// float 乘法不满足结合律，换了次序就会在边界上差出 1/255。
        /// </summary>
        public uint RimPixel(int d2, float ramp) => PixelFor(BaseAlpha, (ramp * Density(d2)) * Factor, ColorBgra);

        /// <summary>
        /// 覆盖度 → 预乘像素。<b>alpha 必须先按 <c>Math.Round</c> 取整再预乘</b>（与原来写在
        /// 像素循环里的那句同式：<c>int × float</c> 先算 float，再 <c>Math.Round</c>）。
        /// </summary>
        private static uint PixelFor(int baseAlpha, float coverage, int colour)
        {
            var alpha = (int)Math.Round(baseAlpha * coverage);
            return alpha <= 0 ? 0u : Premultiply(colour, alpha);
        }

        private static int FloorOf(float square) => (int)Math.Floor((double)square);
    }

    private static IntRect Stamp(uint[] buffer, int width, int height, IntRect clip, Brush brush, PixelPoint p)
    {
        PaintDisc(buffer, width, height, clip, brush, p.X, p.Y);
        var radius = brush.Radius;
        return Clamp(new IntRect(p.X - radius - 1, p.Y - radius - 1, radius * 2 + 3, radius * 2 + 3), width, height);
    }

    private static IntRect Walk(uint[] buffer, int width, int height, IntRect clip, Brush brush,
        PixelPoint a, PixelPoint b)
    {
        var radius = brush.Radius;
        var scan = brush.Scan;
        var steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        // 步长 1 像素：更密只是在同一像素上多叠几次（取大规则下无事发生），更疏会断线
        for (var i = 0; i <= steps; i++)
        {
            var x = a.X;
            var y = a.Y;
            if (steps > 0)
            {
                var t = (double)i / steps;
                x = (int)Math.Round(a.X + (b.X - a.X) * t, MidpointRounding.AwayFromZero);
                y = (int)Math.Round(a.Y + (b.Y - a.Y) * t, MidpointRounding.AwayFromZero);
            }
            // 整个圆盘都在裁剪区外就一个像素都不判——这是 PaintClipped 能省掉那几百毫秒的全部依据
            if (x + scan < clip.X || x - scan >= clip.Right || y + scan < clip.Y || y - scan >= clip.Bottom) continue;
            PaintDisc(buffer, width, height, clip, brush, x, y);
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
    private static void PaintDisc(uint[] buffer, int width, int height, IntRect clip, Brush brush, int cx, int cy)
    {
        var left = Math.Max(cx - brush.Scan, clip.X);
        var right = Math.Min(cx + brush.Scan, clip.Right - 1);
        var top = Math.Max(cy - brush.Scan, clip.Y);
        var bottom = Math.Min(cy + brush.Scan, clip.Bottom - 1);
        if (right < left || bottom < top) return;
        if (brush.Eraser)
        {
            EraseDisc(buffer, width, top, bottom, left, right, brush, cx, cy);
            return;
        }
        for (var y = top; y <= bottom; y++)
        {
            var dy = y - cy;
            var row = y * width;
            for (var x = left; x <= right; x++)
            {
                var dx = x - cx;
                var d2 = dx * dx + dy * dy;
                if (d2 >= brush.SkipFrom) continue;                       // 斜坡之外：一点不染
                var index = row + x;
                var pixel = buffer[index];
                var destinationAlpha = (int)(pixel >>> 24);
                if (destinationAlpha >= brush.MaxAlpha) continue;         // 取大规则：再算也不会更浓
                var value = d2 <= brush.FullCoverMax
                    ? brush.LayerPixel(d2)                                // 覆盖度=1：像素早就算好了
                    : brush.RimPixel(d2, brush.RimStart - MathF.Sqrt(d2));   // 只有这一圈开平方
                if ((int)(value >>> 24) <= destinationAlpha) continue;    // 比这一层更浓的像素保持原样
                buffer[index] = value;
            }
        }
    }

    /// <summary>
    /// 橡皮那一段：<b>按比例减 alpha</b>，所以它没有"取大"可借用（同一条橡皮走两次会擦过头——
    /// 这就是松手必须整层重算的原因，见 <c>CanvasService.Recomposite</c>）。
    /// <para>
    /// 已经是"空白"的像素也照原样写一次 <see cref="LayeredCanvasWindow.BlankPixel"/>，与重写前的
    /// <c>BlendInk</c> 逐像素同结果；省下来的是那三次乘除。
    /// </para>
    /// </summary>
    private static void EraseDisc(uint[] buffer, int width, int top, int bottom, int left, int right,
        Brush brush, int cx, int cy)
    {
        var blank = (int)(LayeredCanvasWindow.BlankPixel >>> 24);
        for (var y = top; y <= bottom; y++)
        {
            var dy = y - cy;
            var row = y * width;
            for (var x = left; x <= right; x++)
            {
                var dx = x - cx;
                var d2 = dx * dx + dy * dy;
                if (d2 >= brush.SkipFrom) continue;
                var index = row + x;
                var pixel = buffer[index];
                var destinationAlpha = (int)(pixel >>> 24);
                var coverage = d2 <= brush.FullCoverMax
                    ? brush.Factor                                        // ramp=1、浓度=1，剩下的就是整笔浓度
                    : (brush.RimStart - MathF.Sqrt(d2)) * brush.Factor;
                var keep = (int)Math.Round(destinationAlpha * (1d - coverage));
                if (keep <= blank)
                {
                    buffer[index] = LayeredCanvasWindow.BlankPixel;       // 擦到底＝回到"空白"，不是 0（0 会漏鼠标）
                    continue;
                }
                var blue = (int)(pixel & 0xFF) * keep / destinationAlpha;
                var green = (int)(pixel >> 8 & 0xFF) * keep / destinationAlpha;
                var red = (int)(pixel >> 16 & 0xFF) * keep / destinationAlpha;
                buffer[index] = (uint)(blue | green << 8 | red << 16 | keep << 24);
            }
        }
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
