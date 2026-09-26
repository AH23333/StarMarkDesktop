#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Abstractions.Capture;

namespace StarMark.Core.Canvas;

/// <summary>
/// 一次拖拽（起点 → 终点）展成一条笔迹的点列。
/// <para>
/// <b>为什么不另做一套"图形渲染"</b>：这块板子的合成是"沿点列走、每步盖一枚圆帽"（
/// <see cref="CanvasCompositor"/>），而取大合成下重复覆盖不变浓——把图形展成点列，
/// 就自动继承了笔迹的一切：脏区、撤销、清空、按比例减 alpha 的橡皮、存图/复制/贴图，
/// 一行特判都不需要。反过来若给图形单开一条绘制路径，这些每条都要重做一遍，
/// 而漏掉的那一条一定是"橡皮擦不掉矩形"这类只有真机才看得见的事。
/// </para>
/// <para>
/// 坐标与笔迹同一套：<b>本屏物理像素，原点在那块屏的左上角</b>（踩坑 #55 那一族：中途换 DIP 必画偏）。
/// 这里只算几何，<b>不碰穿透态、不碰 TTL、不碰缓冲</b>——所以逐点可断言。
/// </para>
/// </summary>
public static class CanvasShapes
{
    /// <summary>
    /// 图形的轮廓点。<paramref name="width"/> 是笔宽（直径）：它决定椭圆要采多少点、箭头头颈多长，
    /// 所以<b>必须传进来</b>——按固定点数采样，细笔画出来是多边形、粗笔又白扔一堆点。
    /// </summary>
    public static List<PixelPoint> Outline(CanvasTool shape, PixelPoint from, PixelPoint to, int width)
        => shape switch
        {
            CanvasTool.Rectangle => RectanglePoints(from, to),
            CanvasTool.Ellipse => EllipsePoints(from, to, Math.Max(1, width / 2)),
            CanvasTool.Line => new List<PixelPoint> { from, to },
            CanvasTool.Arrow => ArrowPoints(from, to, Math.Max(1, width / 2)),
            // 不是图形（笔/橡皮）却走到这里＝接线错了。给它一条直线比静默画个圆更好查：
            // 症状会是"选了画笔却只能画出直线"，一眼能归到这条链上。
            _ => new List<PixelPoint> { from, to },
        };

    /// <summary>
    /// 矩形：四个角走一圈并<b>回到起点闭口</b>。
    /// <para>拖拽的两个对角可以是任意方向（从右下往左上拖也一样），所以取 min/max 而不是假定 from 在左上。</para>
    /// </summary>
    private static List<PixelPoint> RectanglePoints(PixelPoint from, PixelPoint to)
    {
        var left = Math.Min(from.X, to.X);
        var right = Math.Max(from.X, to.X);
        var top = Math.Min(from.Y, to.Y);
        var bottom = Math.Max(from.Y, to.Y);
        return new List<PixelPoint>
        {
            new(left, top), new(right, top), new(right, bottom), new(left, bottom), new(left, top),
        };
    }

    /// <summary>
    /// 椭圆：拖拽矩形是它的外切框，按笔宽决定采样数后<b>首尾再补一次</b>（闭口）。
    /// <para>
    /// 采样间隔受两个约束：① 相邻两点的弦高（sagitta）要 ≤1 像素，否则圆周上看得出自折线；
    /// ② 不必小于笔宽——走笔是连续的胶囊体，点密过头只会白算。取两者里更严的那个。
    /// </para>
    /// </summary>
    private static List<PixelPoint> EllipsePoints(PixelPoint from, PixelPoint to, int radius)
    {
        var cx = (from.X + to.X) / 2.0;
        var cy = (from.Y + to.Y) / 2.0;
        // 半轴至少 1：原地按一下（from==to）也要落下一个小圈，而不是"什么都没画"
        var rx = Math.Max(1.0, Math.Abs(to.X - from.X) / 2.0);
        var ry = Math.Max(1.0, Math.Abs(to.Y - from.Y) / 2.0);
        var step = Math.Max(1.0, Math.Min(radius * 1.2, Math.Sqrt(8.0 * Math.Max(rx, ry))));
        var perimeter = Math.PI * (rx + ry);                       // 拉马努二阶近似够用，这里只要个数量级
        var count = (int)Math.Ceiling(perimeter / step);
        count = Math.Clamp(count, 16, 512);

        var points = new List<PixelPoint>(count + 1);
        for (var i = 0; i < count; i++)
        {
            var angle = 2 * Math.PI * i / count;
            points.Add(new PixelPoint(
                (int)Math.Round(cx + rx * Math.Cos(angle)),
                (int)Math.Round(cy + ry * Math.Sin(angle))));
        }
        points.Add(points[0]);
        return points;
    }

    /// <summary>
    /// 箭头：一段箭杆 + 终点两撇。
    /// <para>
    /// 点列是 <c>[起点, 终点, 撇一, 终点, 撇二]</c>——走笔会画 终点→撇一→终点→撇二，
    /// 回头重涂那一段在取大合成下不变深，所以不需要为"头"单开一条绘制路径。
    /// </para>
    /// <para>原地按一下（长度为零）方向无从谈起：退化成一段（就是一个圆帽点），不猜方向。</para>
    /// </summary>
    private static List<PixelPoint> ArrowPoints(PixelPoint from, PixelPoint to, int radius)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + (double)dy * dy);
        if (length < 1) return new List<PixelPoint> { from, to };

        var ux = dx / length;
        var uy = dy / length;
        var head = Math.Clamp(radius * 4, 10, 60);
        const double cos = 0.8660254;      // cos30°：张角 60° 是"隔着两排座位也看得出箭头朝哪"的那个宽度
        const double sin = 0.5;
        var backX = to.X - head * cos * ux;
        var backY = to.Y - head * cos * uy;
        var spreadX = head * sin * uy;
        var spreadY = head * sin * ux;
        return new List<PixelPoint>
        {
            from, to,
            new((int)Math.Round(backX + spreadX), (int)Math.Round(backY - spreadY)),
            to,
            new((int)Math.Round(backX - spreadX), (int)Math.Round(backY + spreadY)),
        };
    }
}
