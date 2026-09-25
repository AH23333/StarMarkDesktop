#nullable enable
using System;

namespace StarMark.Integrations.Capture;

/// <summary>
/// BGRA 画面的纯托管几何变换（贴图窗的旋转 / 镜像烘焙用），零新增依赖。
/// <para>
/// 旋转后窗口仍是矩形，四角没有对应源像素：这些角在像素里填黑，同时交回旋转四边形
/// （<see cref="RotatedImage.Quad"/>），由调用方用 <c>SetWindowRgn</c> 裁掉，观感与
/// Snipaste 的透明角一致。
/// </para>
/// </summary>
public static class BitmapTransform
{
    /// <summary>把画面绕中心旋转任意角度（双线性采样）。</summary>
    public static RotatedImage Rotate(byte[] src, int width, int height, double angleDegrees)
    {
        if (width <= 0 || height <= 0 || src.Length < (long)width * height * 4)
            throw new ArgumentException("像素缓冲与声明尺寸不符");

        // 角度归一到 (-180,180]；0 度原样返回
        var angle = Normalize(angleDegrees);
        if (Math.Abs(angle) < 0.01)
        {
            var copy = (byte[])src.Clone();
            return new RotatedImage(copy, width, height,
                new(0, 0), new(width, 0), new(width, height), new(0, height));
        }

        var radians = angle * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        // 旋转后的外接尺寸
        var newWidth = (int)Math.Ceiling(Math.Abs(width * cos) + Math.Abs(height * sin));
        var newHeight = (int)Math.Ceiling(Math.Abs(width * sin) + Math.Abs(height * cos));
        newWidth = Math.Max(1, newWidth);
        newHeight = Math.Max(1, newHeight);

        var dst = new byte[newWidth * newHeight * 4];
        // 源中心 → 目标中心的映射：对每个目标像素反推源坐标
        var cxSrc = (width - 1) / 2.0;
        var cySrc = (height - 1) / 2.0;
        var cxDst = (newWidth - 1) / 2.0;
        var cyDst = (newHeight - 1) / 2.0;

        for (var y = 0; y < newHeight; y++)
        {
            var dx = y - cyDst;
            for (var x = 0; x < newWidth; x++)
            {
                var ddx = x - cxDst;
                // 逆旋转
                var sx = cos * ddx + sin * dx + cxSrc;
                var sy = -sin * ddx + cos * dx + cySrc;
                var p = (y * newWidth + x) * 4;
                if (sx < 0 || sy < 0 || sx > width - 1 || sy > height - 1)
                {
                    dst[p] = dst[p + 1] = dst[p + 2] = 0;
                    dst[p + 3] = 255;
                    continue;
                }
                SampleBilinear(src, width, height, sx, sy, dst, p);
            }
        }

        // 源画面四角旋转到目标坐标（用于窗口区域裁切）
        (double X, double Y) Map(double px, double py)
        {
            var dx = px - cxSrc;
            var dy = py - cySrc;
            return (cos * dx - sin * dy + cxDst, sin * dx + cos * dy + cyDst);
        }
        var tl = Map(0, 0);
        var tr = Map(width - 1, 0);
        var br = Map(width - 1, height - 1);
        var bl = Map(0, height - 1);

        return new RotatedImage(dst, newWidth, newHeight,
            ToPoint(tl), ToPoint(tr), ToPoint(br), ToPoint(bl));
    }

    /// <summary>水平（horizontal=true）或垂直翻转画面。</summary>
    public static byte[] Flip(byte[] src, int width, int height, bool horizontal)
    {
        if (width <= 0 || height <= 0 || src.Length < (long)width * height * 4)
            throw new ArgumentException("像素缓冲与声明尺寸不符");
        var dst = new byte[src.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sx = horizontal ? width - 1 - x : x;
                var sy = horizontal ? y : height - 1 - y;
                var from = (sy * width + sx) * 4;
                var to = (y * width + x) * 4;
                dst[to] = src[from];
                dst[to + 1] = src[from + 1];
                dst[to + 2] = src[from + 2];
                dst[to + 3] = src[from + 3];
            }
        }
        return dst;
    }

    private static void SampleBilinear(byte[] src, int width, int height,
        double sx, double sy, byte[] dst, int dstOffset)
    {
        var x0 = (int)Math.Floor(sx);
        var y0 = (int)Math.Floor(sy);
        var x1 = Math.Min(x0 + 1, width - 1);
        var y1 = Math.Min(y0 + 1, height - 1);
        var fx = sx - x0;
        var fy = sy - y0;

        for (var channel = 0; channel < 4; channel++)
        {
            var p00 = src[(y0 * width + x0) * 4 + channel];
            var p10 = src[(y0 * width + x1) * 4 + channel];
            var p01 = src[(y1 * width + x0) * 4 + channel];
            var p11 = src[(y1 * width + x1) * 4 + channel];
            var top = p00 + (p10 - p00) * fx;
            var bottom = p01 + (p11 - p01) * fx;
            var value = top + (bottom - top) * fy;
            dst[dstOffset + channel] = (byte)Math.Clamp(Math.Round(value), 0, 255);
        }
    }

    private static double Normalize(double degrees)
    {
        var a = degrees % 360d;
        if (a > 180d) a -= 360d;
        if (a <= -180d) a += 360d;
        return a;
    }

    private static (int X, int Y) ToPoint((double X, double Y) p)
        => ((int)Math.Round(p.X, MidpointRounding.AwayFromZero),
            (int)Math.Round(p.Y, MidpointRounding.AwayFromZero));
}

/// <summary>一次旋转烘焙的结果。</summary>
public sealed record RotatedImage(
    byte[] Pixels, int Width, int Height,
    (int X, int Y) TopLeft,
    (int X, int Y) TopRight,
    (int X, int Y) BottomRight,
    (int X, int Y) BottomLeft)
{
    /// <summary>旋转四边形（窗口区域用），顺序＝左上、右上、右下、左下。</summary>
    public (int X, int Y)[] Quad => new[] { TopLeft, TopRight, BottomRight, BottomLeft };
}
