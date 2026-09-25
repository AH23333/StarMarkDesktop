#nullable enable
using System;

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
}
