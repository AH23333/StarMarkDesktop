#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <b>截图标注合成核的对差分测试</b>：改动前后的逐像素结果必须完全相同。
/// <para>
/// 画布那条链早就有同级证据（<see cref="CanvasKernelIdentityTests"/>，234 例），而截图这条一直只有
/// "某个像素是不是红的"这类点断言——它能钉住单点行为，钉不住<b>整张图的形状</b>。
/// 批次 S2-d 第二刀要把两条链的"怎么画"收进同一处（`PixelInkRenderer`，合成规则按 surface 分臂），
/// 那是一次"看起来什么都没改"的搬迁，而这一类改动坏起来正好是"眼睛才看得见"：
/// 半透明笔中段浓淡、马赛克边界、椭圆采样密度、箭头翼长，任何一处差 1/255 都不会让既有测变红。
/// 所以先在这里把<b>今天的算式原样留一份</b>当参照，搬迁之后仍然逐像素相等才算没改坏。
/// </para>
/// <para>
/// <b>参照实现不许"顺手优化"</b>：它存在的唯一理由就是复现今天的行为，包括那句
/// "每个像素只混合一次"的掩码、硬边圆判据 <c>d² ≤ r²</c>、以及椭圆按周长估的采样数。
/// 哪天它被改了，本文件就失去意义。
/// </para>
/// <para>
/// <b>不覆盖 Text / Number 两臂</b>：它们交给 GDI 量字与描字，字形栅格化随系统字体版本变，
/// 逐像素相等在这里不是可守的契约（旋转/中心／底色圆盘的几何另有测：批次 RF-2／WT）。
/// </para>
/// </summary>
public sealed class CaptureKernelIdentityTests
{
    private const int BgB = 10, BgG = 20, BgR = 30;

    private static byte[] Backdrop(int width, int height)
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

    /// <summary>隔列竖条纹：马赛克的平均色必须真的"混过"，纯平底色上打码等于什么都没改（锚点会假绿）。</summary>
    private static byte[] StripedBackdrop(int width, int height)
    {
        var pixels = Backdrop(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x += 2)
                pixels[(y * width + x) * 4 + 2] = 255;
        return pixels;
    }

    private static readonly int OpaqueRed = Annotation.Opaque(0x11, 0x23, 0xFF);
    private static readonly int HalfYellow = unchecked((int)0x80F0E020);   // 半透明：走"每像素只混合一次"那条臂
    private static readonly int BarelyVisible = unchecked((int)0x01F0E020); // alpha=1：混合后差 1/255，最容易暴露次序差别

    /// <summary>模型侧的构造与生产完全同源（点位/颜色/档位），参照只复刻"怎么画到像素上"。</summary>
    private static Annotation Mark(AnnotationTool tool, int color, int thickness, params PixelPoint[] points)
        => new(tool, points, color, thickness);

    private static Annotation[][] Shapes(int width, int height)
    {
        var a = new PixelPoint(6, 7);
        var b = new PixelPoint(width - 8, height - 5);
        var c = new PixelPoint(3, height - 11);
        var zig = new[] { a, new PixelPoint(20, 4), new PixelPoint(9, 22), b, c, new PixelPoint(width - 2, 2) };
        return new[]
        {
            new[] { Mark(AnnotationTool.Rectangle, OpaqueRed, 4, a, b) },
            new[] { Mark(AnnotationTool.Ellipse, OpaqueRed, 4, a, b) },
            new[] { Mark(AnnotationTool.Line, OpaqueRed, 4, a, b) },
            new[] { Mark(AnnotationTool.Arrow, OpaqueRed, 4, a, b) },
            new[] { Mark(AnnotationTool.Pen, OpaqueRed, 4, zig) },
            new[] { Mark(AnnotationTool.PolyLine, OpaqueRed, 4, zig) },
            new[] { Mark(AnnotationTool.Highlighter, OpaqueRed, 4, zig) },
            new[] { Mark(AnnotationTool.Mosaic, OpaqueRed, 4, zig) },
            // 退化形状：原地按一下、两点重合、以及完全在画面外的——这些最容易在搬迁时被"顺手简化"
            new[] { Mark(AnnotationTool.Line, OpaqueRed, 4, a, a) },
            new[] { Mark(AnnotationTool.Ellipse, OpaqueRed, 1, a, a) },
            new[] { Mark(AnnotationTool.Pen, OpaqueRed, 15, a, new PixelPoint(a.X + 1, a.Y)) },
            new[] { Mark(AnnotationTool.Arrow, OpaqueRed, 8, a, new PixelPoint(a.X, a.Y + 1)) },
            new[] { Mark(AnnotationTool.Rectangle, OpaqueRed, 2, new PixelPoint(-40, -40), new PixelPoint(-6, -6)) },
            new[] { Mark(AnnotationTool.Pen, OpaqueRed, 4, new PixelPoint(width - 3, 1), new PixelPoint(width + 30, 20)) },
            // 同一条路径上叠两笔：第二笔要能盖住第一笔（取大会擦不掉，这里验的是"over"那一条律没被抹平）
            new[] { Mark(AnnotationTool.Highlighter, HalfYellow, 8, a, b), Mark(AnnotationTool.Pen, OpaqueRed, 8, a, b) },
        };
    }

    private static readonly int[] Thicknesses = { 1, 2, 4, 8, 15 };
    private static readonly int[] Colors = { OpaqueRed, HalfYellow, BarelyVisible };

    [Theory]
    [MemberData(nameof(CaseMatrix))]
    public void ProductionMatchesTheFrozenKernel(int width, int height, int thickness, int color, int shapeIndex)
    {
        var shapes = Shapes(width, height);
        var mark = shapes[shapeIndex][0];
        var withColor = mark with { ColorBgra = color, Thickness = thickness };
        // 每一例都必须是"画得出去"的标注：否则两边比的只是"谁先抛"，逐像素相等就成了假证据。
        Assert.Null(withColor.Problem());
        var backdrop = shapeIndex is 7 ? StripedBackdrop(width, height) : Backdrop(width, height);

        var actual = AnnotationPainter.Render(backdrop, width, height, new[] { withColor });
        var expected = (byte[])backdrop.Clone();
        PaintRef(expected, width, height, withColor);

        Assert.Equal(expected, actual);
    }

    public static IEnumerable<object[]> CaseMatrix()
    {
        const int w = 48, h = 40;
        var shapes = Shapes(w, h);
        for (var shape = 0; shape < shapes.Length; shape++)
            foreach (var thickness in Thicknesses)
                foreach (var color in Colors)
                    yield return new object[] { w, h, thickness, color, shape };
    }

    /// <summary>
    /// 逐笔叠加也要相等（多条标注共用一张缓冲时的次序效果＝"后画的盖前面的"能不能被搬迁改坏）。
    /// </summary>
    [Fact]
    public void StackedMarksMatchTheFrozenKernel()
    {
        const int w = 64, h = 48;
        var backdrop = StripedBackdrop(w, h);
        var marks = new[]
        {
            Mark(AnnotationTool.Mosaic, OpaqueRed, 4, new PixelPoint(4, 6), new PixelPoint(30, 6), new PixelPoint(30, 30)),
            Mark(AnnotationTool.Highlighter, HalfYellow, 8, new PixelPoint(8, 12), new PixelPoint(52, 40)),
            Mark(AnnotationTool.Ellipse, OpaqueRed, 2, new PixelPoint(2, 2), new PixelPoint(26, 22)),
            Mark(AnnotationTool.Arrow, BarelyVisible, 15, new PixelPoint(40, 4), new PixelPoint(12, 44)),
            Mark(AnnotationTool.Pen, OpaqueRed, 4, new PixelPoint(1, 1), new PixelPoint(63, 1), new PixelPoint(63, 47), new PixelPoint(1, 47)),
        };

        var actual = AnnotationPainter.Render(backdrop, w, h, marks);
        var expected = (byte[])backdrop.Clone();
        foreach (var mark in marks) PaintRef(expected, w, h, mark);

        Assert.Equal(expected, actual);
    }

    // ────────── 参照实现（＝批次 S2-d 第二刀之前 <see cref="AnnotationPainter"/> 的那几段，逐字搬来） ──────────

    private static void PaintRef(byte[] bgra, int width, int height, Annotation mark)
    {
        var color = mark.EffectiveColorBgra;
        var radius = Annotation.InkRadius(mark.Thickness);
        var points = mark.TransformedPoints();
        var far = points[^1];

        switch (mark.Tool)
        {
            case AnnotationTool.Text:
            case AnnotationTool.Number:
                throw new InvalidOperationException("参照实现不覆盖 GDI 那两臂（字形栅格化随系统变，不是可逐像素守的契约）");

            case AnnotationTool.Rectangle:
                RectangleOutlineRef(bgra, width, height, points[0], far, radius, color);
                break;

            case AnnotationTool.Ellipse:
                EllipseOutlineRef(bgra, width, height, points[0], far, radius, color);
                break;

            case AnnotationTool.Line:
                SegmentRef(bgra, width, height, points[0], far, radius, color);
                break;

            case AnnotationTool.Arrow:
                ArrowRef(bgra, width, height, points[0], far, radius, color);
                break;

            case AnnotationTool.Pen:
            case AnnotationTool.Highlighter:
            case AnnotationTool.PolyLine:
                byte[]? painted = color >>> 24 < 255 ? new byte[width * height] : null;
                for (var i = 1; i < points.Count; i++)
                    SegmentRef(bgra, width, height, points[i - 1], points[i], radius, color, painted);
                break;

            case AnnotationTool.Mosaic:
                MosaicBrushRef(bgra, width, height, points, radius);
                break;
        }
    }

    private static void RectangleOutlineRef(byte[] bgra, int width, int height,
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
        SegmentRef(bgra, width, height, tl, tr, radius, color);
        SegmentRef(bgra, width, height, tr, br, radius, color);
        SegmentRef(bgra, width, height, br, bl, radius, color);
        SegmentRef(bgra, width, height, bl, tl, radius, color);
    }

    private static void EllipseOutlineRef(byte[] bgra, int width, int height,
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
            SegmentRef(bgra, width, height, previous, current, radius, color);
            previous = current;
        }
    }

    private static void ArrowRef(byte[] bgra, int width, int height,
        PixelPoint from, PixelPoint to, int radius, int color)
    {
        SegmentRef(bgra, width, height, from, to, radius, color);
        var barbs = Annotation.ArrowBarbs(from, to, radius * 2 + 1);
        SegmentRef(bgra, width, height, barbs[0], barbs[1], radius, color);
        SegmentRef(bgra, width, height, barbs[1], barbs[2], radius, color);
    }

    private static void SegmentRef(byte[] bgra, int width, int height,
        PixelPoint a, PixelPoint b, int radius, int color, byte[]? painted = null)
        => InkPath.Each(a, b, (x, y) => DiscRef(bgra, width, height, x, y, radius, color, painted));

    private static void DiscRef(byte[] bgra, int width, int height, int cx, int cy, int radius, int color,
        byte[]? painted = null)
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
                BlendRef(bgra, width, height, x, y, color);
            }
        }
    }

    private static void MosaicBrushRef(byte[] bgra, int width, int height,
        IReadOnlyList<PixelPoint> points, int radius)
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
                if (touched[by * columns + bx]) PixelateRef(bgra, width, height, bx * block, by * block, block);
    }

    private static void PixelateRef(byte[] bgra, int width, int height, int left, int top, int block)
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
                WriteRef(bgra, width, height, x, y, average);
    }

    private static void WriteRef(byte[] bgra, int width, int height, int x, int y, int color)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;
        var p = (y * width + x) * 4;
        bgra[p] = (byte)color;
        bgra[p + 1] = (byte)(color >> 8);
        bgra[p + 2] = (byte)(color >> 16);
        bgra[p + 3] = 255;
    }

    private static void BlendRef(byte[] bgra, int width, int height, int x, int y, int color)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;
        var alpha = color >>> 24;
        if (alpha >= 255) { WriteRef(bgra, width, height, x, y, color); return; }
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
