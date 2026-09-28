#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Abstractions.Capture;
using StarMark.Integrations.Capture;

namespace StarMark.Core.Capture;

/// <summary>
/// 把标注画进 BGRA 缓冲：<b>纯托管像素运算</b>（只有文字那一臂交给 GDI）。
/// <para>
/// 为什么自己在像素上画而不是让界面画完再"拍照"：截图标注的输出要交给四个落点
/// （复制／存图／贴图／识字），每一条都吃同一份 BGRA 缓冲。渲染路径必须<b>可断言</b>——
/// "红色矩形在 (12,12) 那个像素上确实是红的"这种事单测能钉住，而 WinRT 的离屏渲染做不到。
/// </para>
/// <para>
/// <b>输入缓冲一律不改</b>：预览要拿同一份底图反复重烤（撤销一条就整批重画一次），
/// 底图一旦被就地改过就再也回不到"什么都没画"的状态。所以 <see cref="Render"/> 先复制。
/// </para>
/// </summary>
public static class AnnotationPainter
{
    /// <summary>画完的那份缓冲是新数组；<paramref name="source"/> 保持原样。</summary>
    public static byte[] Render(byte[] source, int width, int height, IReadOnlyList<Annotation> marks)
    {
        if (width <= 0 || height <= 0) throw new InvalidOperationException($"画面尺寸不合法（{width} × {height}）");
        if (source is null || source.Length < (long)width * height * 4)
            throw new InvalidOperationException("像素缓冲比声明的尺寸短，画上去会越界");
        var output = (byte[])source.Clone();
        foreach (var mark in marks) Paint(output, width, height, mark);
        return output;
    }

    /// <summary>
    /// 就地把一条标注画进 <paramref name="bgra"/>（<see cref="Render"/> 负责"先复制再逐条画"）。
    /// 画不出去时抛原因：<see cref="Annotation.Problem"/> 里每一条都对应一种真实输入，
    /// 静默跳过等于让用户对着一张少了字的图猜"我刚才写的那行字呢"。
    /// </summary>
    public static void Paint(byte[] bgra, int width, int height, Annotation mark)
    {
        if (mark.Problem() is { } problem) throw new InvalidOperationException(problem);
        var color = mark.EffectiveColorBgra;
        var radius = Radius(mark);
        // 变换（旋转/缩放）在这里落地一次：绘制、选择框、命中测试读的都是同一组点，
        // 分三处各算一遍就会出现"框框住原位置、字已经转走"。
        var points = mark.TransformedPoints();
        // 两点点工具的"另一端"取末尾采到的那一点，不取第二个元素：调用方在拖动过程中会一路追加采样点，
        // 而第二个元素只是按下后的第一次移动（离起点一两个像素）——拿它当另一端就画出一个针尖大的框。
        var far = points[^1];

        switch (mark.Tool)
        {
            case AnnotationTool.Text:
                // 位置已由 TransformedPoints() 转过，这里再给角度：字块绕自己的中心转（同一套数学在 TextGeometry）
                GdiTextDrawer.Draw(bgra, width, height, points[0].X, points[0].Y,
                    mark.Text!, mark.DrawFontHeight, color, mark.Rotation);
                break;

            case AnnotationTool.Rectangle:
                RectangleOutline(bgra, width, height, points[0], far, radius, color);
                break;

            case AnnotationTool.Ellipse:
                EllipseOutline(bgra, width, height, points[0], far, radius, color);
                break;

            case AnnotationTool.Line:
                Segment(bgra, width, height, points[0], far, radius, color);
                break;

            case AnnotationTool.Arrow:
                Arrow(bgra, width, height, points[0], far, radius, color);
                break;

            case AnnotationTool.Pen:
            case AnnotationTool.Highlighter:
            // 折线＝"顶点是点出来的，不是拖出来的"画笔：逐段连起来，走的还是同一条臂。
            // 顶点处不做斜接（miter）而是让圆头叠过去——截图上的折角看不出差别，而少一处几何就少一处会算错。
            case AnnotationTool.PolyLine:
                // 半透明笔必须"每个像素只混合一次"：圆点是连续盖着的，同一个像素会被七八个圆点扫到，
                // 逐次混合会让笔画中段明显比两端浓——荧光笔涂出来一条深浅不匀的带子，等于没做对。
                byte[]? painted = color >>> 24 < 255 ? new byte[width * height] : null;
                for (var i = 1; i < points.Count; i++)
                    Segment(bgra, width, height, points[i - 1], points[i], radius, color, painted);
                break;

            case AnnotationTool.Mosaic:
                MosaicBrush(bgra, width, height, points, radius);
                break;

            case AnnotationTool.Number:
                DrawNumber(bgra, width, height, mark);
                break;
        }
    }

    // ────────── 选中：命中测试 ──────────

    /// <summary>
    /// 这一点落在哪条标注上，返回<b>最上面</b>那条的下标（后画的盖在上面，命中的也该是它），没有则 null。
    /// <para>包围盒一律取 <see cref="Annotation.Bounds"/>——那是"画出去占哪一块"的唯一说法；
    /// 这里再算一遍就会长成"框框住原位置、字已经转走"。</para>
    /// </summary>
    public static int? HitTest(IReadOnlyList<Annotation> marks, PixelPoint at, int slop)
    {
        for (var i = marks.Count - 1; i >= 0; i--)
            if (marks[i].Contains(at, slop)) return i;
        return null;
    }

    // ────────── 图元 ──────────

    /// <summary>矩形轮廓：四条边各一条线段（用线段而不是"填充再挖洞"，线宽与端点形状自动跟着走）。</summary>
    private static void DrawNumber(byte[] bgra, int width, int height, Annotation mark)
    {
        var box = mark.Bounds();
        var cx = box.X + box.Width / 2;
        var cy = box.Y + box.Height / 2;
        // 半径与命中框必须出自模型那一处（NumberRadius）：从前这里还按半径大小决定"画不画"，
        // 序号缩到 0.2× 就长成"图上没有、列表里还在"（批次 WT）。
        var radius = mark.NumberRadius;
        var color = mark.EffectiveColorBgra;

        // 实心圆（逐扫描线，圆内直接写色，不走混合：序号底色本就该不透明）
        for (var dy = -radius; dy <= radius; dy++)
        {
            var y = cy + dy;
            if (y < 0 || y >= height) continue;
            var span = (int)Math.Sqrt(Math.Max(0, radius * radius - dy * dy));
            for (var dx = -span; dx <= span; dx++)
            {
                var x = cx + dx;
                if (x < 0 || x >= width) continue;
                var p = (y * width + x) * 4;
                bgra[p] = (byte)(color & 0xFF);
                bgra[p + 1] = (byte)((color >> 8) & 0xFF);
                bgra[p + 2] = (byte)((color >> 16) & 0xFF);
                bgra[p + 3] = 255;
            }
        }

        // 白色编号居中
        var label = mark.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var fontHeight = Math.Max(8, (int)(radius * 1.3));
        var (tw, th) = GdiTextDrawer.Measure(label, fontHeight);
        GdiTextDrawer.Draw(bgra, width, height, cx - tw / 2, cy - th / 2,
            label, fontHeight, unchecked((int)0xFFFFFFFFu));
    }

    private static void RectangleOutline(byte[] bgra, int width, int height,
        PixelPoint a, PixelPoint b, int radius, int color)
    {
        var left = Math.Min(a.X, b.X);
        var right = Math.Max(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var bottom = Math.Max(a.Y, b.Y);
        var tl = new PixelPoint(left, top);
        var tr = new PixelPoint(right, top);
        var br = new PixelPoint(right, bottom);
        var bl = new PixelPoint(left, bottom);
        Segment(bgra, width, height, tl, tr, radius, color);
        Segment(bgra, width, height, tr, br, radius, color);
        Segment(bgra, width, height, br, bl, radius, color);
        Segment(bgra, width, height, bl, tl, radius, color);
    }

    /// <summary>椭圆轮廓：按角度采样后连成折线。采样数按周长估，少采会在大椭圆上看见"多边形"。</summary>
    private static void EllipseOutline(byte[] bgra, int width, int height,
        PixelPoint a, PixelPoint b, int radius, int color)
    {
        var cx = (a.X + b.X) / 2.0;
        var cy = (a.Y + b.Y) / 2.0;
        var rx = Math.Max(0.5, Math.Abs(b.X - a.X) / 2.0);
        var ry = Math.Max(0.5, Math.Abs(b.Y - a.Y) / 2.0);
        var steps = Math.Clamp((int)(Math.PI * Math.Sqrt(2 * (rx * rx + ry * ry))), 64, 4000);
        PixelPoint At(double angle) => new(
            (int)Math.Round(cx + rx * Math.Cos(angle), MidpointRounding.AwayFromZero),
            (int)Math.Round(cy + ry * Math.Sin(angle), MidpointRounding.AwayFromZero));
        var previous = At(0);
        for (var i = 1; i <= steps; i++)
        {
            var current = At(i * 2 * Math.PI / steps);
            Segment(bgra, width, height, previous, current, radius, color);
            previous = current;
        }
    }

    /// <summary>箭头：一杆 + 两翼。翼长随线宽走，翼太短在小尺寸截图上根本看不见。</summary>
    /// <summary>箭头：一杆 + 两翼。两翼几何在 <see cref="Annotation.ArrowBarbs"/>，与拖动中的预览共用同一份。</summary>
    private static void Arrow(byte[] bgra, int width, int height, PixelPoint from, PixelPoint to, int radius, int color)
    {
        Segment(bgra, width, height, from, to, radius, color);
        var barbs = Annotation.ArrowBarbs(from, to, radius * 2 + 1);
        Segment(bgra, width, height, barbs[0], barbs[1], radius, color);
        Segment(bgra, width, height, barbs[1], barbs[2], radius, color);
    }

    /// <summary>一段粗线：沿路径每约一个像素盖一个圆点。圆点保证端点是圆的，折线相接处不会出现缺口。</summary>
    private static void Segment(byte[] bgra, int width, int height,
        PixelPoint a, PixelPoint b, int radius, int color, byte[]? painted = null)
        => InkPath.Each(a, b, (x, y) => Disc(bgra, width, height, x, y, radius, color, painted));

    private static void Disc(byte[] bgra, int width, int height, int cx, int cy, int radius, int color, byte[]? painted = null)
    {
        var r2 = radius * radius;
        for (var y = cy - radius; y <= cy + radius; y++)
        {
            var ddy = y - cy;
            if (ddy * ddy > r2) continue;
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                var ddx = x - cx;
                if (ddx * ddx + ddy * ddy > r2) continue;
                if (painted is not null)
                {
                    if ((uint)x >= width || (uint)y >= height) continue;
                    var cell = y * width + x;
                    if (painted[cell] != 0) continue;
                    painted[cell] = 1;
                }
                Blend(bgra, width, height, x, y, color);
            }
        }
    }

    // ────────── 马赛克 ──────────

    /// <summary>
    /// 马赛克笔：<b>只打散被涂到的那些格子</b>，格子边界对齐到整块画面的固定网格。
    /// <para>
    /// 对齐固定网格而不是"以笔尖为中心分格"有两个理由：来回涂同一条不会把格子错开成噪点；
    /// 而且同一处涂两次的结果完全一致（可重复操作是这类工具最基本的正确性）。
    /// </para>
    /// </summary>
    private static void MosaicBrush(byte[] bgra, int width, int height, IReadOnlyList<PixelPoint> points, int radius)
    {
        var block = Annotation.MosaicBlockSize;
        var columns = (width + block - 1) / block;
        var rows = (height + block - 1) / block;
        var touched = new bool[columns * rows];
        var any = false;
        for (var i = 1; i < points.Count; i++)
        {
            InkPath.Each(points[i - 1], points[i], (x, y) =>
            {
                for (var by = (y - radius) / block; by <= (y + radius) / block; by++)
                {
                    for (var bx = (x - radius) / block; bx <= (x + radius) / block; bx++)
                    {
                        if ((uint)bx >= columns || (uint)by >= rows) continue;
                        // 只碰"中心落在笔刷圆里"的格子：格子要么整块被打散、要么原样，
                        // 于是来回涂同一处不会涂出半明半暗的花脸。
                        var ddx = bx * block + block / 2 - x;
                        var ddy = by * block + block / 2 - y;
                        if (ddx * ddx + ddy * ddy > radius * radius) continue;
                        if (!touched[by * columns + bx]) { touched[by * columns + bx] = true; any = true; }
                    }
                }
            });
        }
        if (!any) return;
        for (var by = 0; by < rows; by++)
            for (var bx = 0; bx < columns; bx++)
                if (touched[by * columns + bx]) Pixelate(bgra, width, height, bx * block, by * block, block);
    }

    /// <summary>把一块格子压成它自己的平均色（真"打散"，不是高斯模糊：马赛克要的是"知道这里有字但读不出"）。</summary>
    private static void Pixelate(byte[] bgra, int width, int height, int left, int top, int block)
    {
        var right = Math.Min(width, left + block);
        var bottom = Math.Min(height, top + block);
        long sumB = 0, sumG = 0, sumR = 0;
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            var row = y * width * 4;
            for (var x = left; x < right; x++)
            {
                var p = row + x * 4;
                sumB += bgra[p];
                sumG += bgra[p + 1];
                sumR += bgra[p + 2];
                count++;
            }
        }
        if (count == 0) return;
        var average = Annotation.Opaque((byte)(sumB / count), (byte)(sumG / count), (byte)(sumR / count));
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
                Write(bgra, width, height, x, y, average);
    }

    // ────────── 像素与工具 ──────────

    /// <summary>
    /// 画这条标注用的圆点半径。<b>公式在 <see cref="Annotation.InkRadius"/>（那一档"真正画多宽"的唯一出处）</b>：
    /// 拖动中的预览要拿同一个数（<see cref="Annotation.InkWidth"/>），这里再算一遍就会出现
    /// "屏幕上比贴出来的粗一点"（批次 XU）。
    /// </summary>
    private static int Radius(Annotation mark) => Annotation.InkRadius(mark.Thickness);

    private static void Write(byte[] bgra, int width, int height, int x, int y, int color)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;      // 一次比较挡住负数与越界两个方向
        var p = (y * width + x) * 4;
        bgra[p] = (byte)color;
        bgra[p + 1] = (byte)(color >> 8);
        bgra[p + 2] = (byte)(color >> 16);
        bgra[p + 3] = 255;      // 交出去必须仍是不透明，否则 PNG 上会多出一个透明洞
    }

    /// <summary>按颜色的 alpha 混合后写一个像素（alpha=255 时退化成直接写）。</summary>
    private static void Blend(byte[] bgra, int width, int height, int x, int y, int color)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;
        var alpha = color >>> 24;
        if (alpha >= 255) { Write(bgra, width, height, x, y, color); return; }
        if (alpha == 0) return;
        var p = (y * width + x) * 4;
        var keep = 255 - alpha;
        for (var channel = 0; channel < 3; channel++)
        {
            var src = color >>> (channel * 8) & 0xFF;
            bgra[p + channel] = (byte)((bgra[p + channel] * keep + src * alpha) / 255);
        }
        bgra[p + 3] = 255;
    }
}
