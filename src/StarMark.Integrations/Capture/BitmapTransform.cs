#nullable enable
using System;
using StarMark.Abstractions.Capture;

namespace StarMark.Integrations.Capture;

/// <summary>
/// BGRA 画面的纯托管几何变换（贴图窗的 90° 旋转 / 镜像烘焙用），零新增依赖。
/// <para>
/// <b>只有 90° 离散旋转与镜像</b>：两者都是精确像素重排——没有插值糊化、没有填黑四角、
/// 不需要窗口区域裁切（自由角度旋转的"四角填黑 + SetWindowRgn"两件套在真机上就是
/// 大面积黑背景，且外接矩形越转越大，用户裁决只留左/右 90° 与水平/垂直翻转四种姿态）。
/// </para>
/// </summary>
public static class BitmapTransform
{
    /// <summary>把画面旋转 90°（顺时针或逆时针），宽高换轴。返回新像素与新的宽高。</summary>
    public static (byte[] Pixels, int Width, int Height) Rotate90(
        byte[] src, int width, int height, bool clockwise)
    {
        if (width <= 0 || height <= 0 || src.Length < (long)width * height * 4)
            throw new ArgumentException("像素缓冲与声明尺寸不符");

        var newWidth = height;
        var newHeight = width;
        var dst = new byte[src.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // 顺时针：src(x,y) → dst(H-1-y, x)；逆时针：src(x,y) → dst(y, W-1-x)
                int dx, dy;
                if (clockwise) { dx = height - 1 - y; dy = x; }
                else { dx = y; dy = width - 1 - x; }
                var from = (y * width + x) * 4;
                var to = (dy * newWidth + dx) * 4;
                dst[to] = src[from];
                dst[to + 1] = src[from + 1];
                dst[to + 2] = src[from + 2];
                dst[to + 3] = src[from + 3];
            }
        }
        return (dst, newWidth, newHeight);
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

    /// <summary>
    /// 从一幅画面里裁出一块矩形（批次 PU：截图提交＝从"整帧＋标注"的合成图里裁出选区）。
    /// 逐行 BlockCopy，不逐像素走；<paramref name="x"/>/<paramref name="y"/> 为负或越界都会被夹进源图，
    /// 夹完不足一个像素时抛出——交一张 0×0 的图等于静默失败。
    /// </summary>
    public static (byte[] Pixels, int Width, int Height) Crop(
        byte[] src, int srcWidth, int srcHeight, int x, int y, int width, int height)
    {
        if (srcWidth <= 0 || srcHeight <= 0 || src.Length < (long)srcWidth * srcHeight * 4)
            throw new ArgumentException("像素缓冲与声明尺寸不符");
        var left = Math.Clamp(x, 0, srcWidth);
        var top = Math.Clamp(y, 0, srcHeight);
        var w = Math.Clamp(width, 0, srcWidth - left);
        var h = Math.Clamp(height, 0, srcHeight - top);
        if (w <= 0 || h <= 0) throw new ArgumentException($"裁不出画面（请求 {width}×{height} @({x},{y})，源图 {srcWidth}×{srcHeight}）");
        var dst = new byte[(long)w * h * 4];
        for (var row = 0; row < h; row++)
        {
            var from = ((long)(top + row) * srcWidth + left) * 4;
            Buffer.BlockCopy(src, (int)from, dst, row * w * 4, w * 4);
        }
        return (dst, w, h);
    }

    /// <summary>
    /// 把 <paramref name="hole"/> 之外的画面压暗（批次 PU：选区外的压暗烤进合成图）。
    /// <para>标注要能越出选区显示，就不能再让 XAML 压暗层叠在上面——那会把越界的标注一起盖暗到看不见。
    /// 烤进画面还有第二个理由：提交时从合成图里裁选区，选区内本来就不含压暗，交出去的图天然干净。</para>
    /// </summary>
    public static void DimOutside(byte[] bgra, int width, int height, IntRect hole, byte alpha)
    {
        if (width <= 0 || height <= 0 || bgra.Length < (long)width * height * 4)
            throw new ArgumentException("像素缓冲与声明尺寸不符");
        var keep = 255 - alpha;
        if (keep <= 0) return;
        var x0 = Math.Clamp(hole.X, 0, width);
        var y0 = Math.Clamp(hole.Y, 0, height);
        var x1 = Math.Clamp(hole.Right, 0, width);
        var y1 = Math.Clamp(hole.Bottom, 0, height);

        void DimSpan(int y, int from, int to)
        {
            for (var x = from; x < to; x++)
            {
                var p = (y * width + x) * 4;
                bgra[p] = (byte)(bgra[p] * keep / 255);
                bgra[p + 1] = (byte)(bgra[p + 1] * keep / 255);
                bgra[p + 2] = (byte)(bgra[p + 2] * keep / 255);
            }
        }

        for (var y = 0; y < y0; y++) DimSpan(y, 0, width);
        for (var y = y0; y < y1; y++)
        {
            DimSpan(y, 0, x0);
            DimSpan(y, x1, width);
        }
        for (var y = y1; y < height; y++) DimSpan(y, 0, width);
    }
}
