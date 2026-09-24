#nullable enable
using System;

namespace StarMark.Integrations.Capture;

/// <summary>
/// 一行字在像素上占的那一块，以及它<b>转出去之后</b>占的那一块。
/// <para>为什么单独立一个地方：绘制端要按它落笔（绕字块中心转），Core 的选择框要按它画
/// （<c>Annotation.Bounds</c>）。两处各写一遍三角函数，就会长成"字转走了、框还横在原地"——
/// 那正是真机反馈的"文字显著脱离文字框范围内"。</para>
/// <para>角度按<b>顺时针</b>算（屏幕坐标系 y 向下，用户把把手向右拖就是正角）。</para>
/// </summary>
public static class TextGeometry
{
    /// <summary>字块中心的横坐标（未旋转时的左上角 + 半个宽）。</summary>
    public static double CentreX(int atX, int width) => atX + width / 2.0;

    /// <summary>字块中心的纵坐标。</summary>
    public static double CentreY(int atY, int height) => atY + height / 2.0;

    /// <summary>角度归一：0 ≤ a &lt; 360。</summary>
    public static double Normalise(double angle)
    {
        var wrapped = angle % 360d;
        return wrapped < 0 ? wrapped + 360d : wrapped;
    }

    /// <summary>
    /// 旋转用到的 (cos, sin)。<b>90° 的整数倍要钉成精确值</b>：
    /// <c>Math.Cos(90°)</c> 给的是 6.1e-17 而不是 0，逐像素采样时整幅字会偏移一列，
    /// 而且"转 90° 再转回 0°"拿不回同一张图（可重复性是断言要抓的东西）。
    /// </summary>
    public static (double Cos, double Sin) RotationOf(double angle)
    {
        var turned = Normalise(angle);
        if (turned % 90d == 0d)
        {
            var quarter = (int)(turned / 90d) % 4;
            return quarter switch
            {
                0 => (1d, 0d),
                1 => (0d, 1d),
                2 => (-1d, 0d),
                _ => (0d, -1d),
            };
        }
        var radians = turned * Math.PI / 180d;
        return (Math.Cos(radians), Math.Sin(radians));
    }

    /// <summary>
    /// 字块绕自身中心转过 <paramref name="angle"/> 之后，<b>轴对齐</b>那一份外接框（整像素，向外取整）。
    /// <para>0° 走原样返回，不做浮点往返——绝大多数标注一辈子都不会被转。</para>
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) RotatedBox(int atX, int atY, int width, int height, double angle)
    {
        if (width <= 0 || height <= 0) return (atX, atY, atX + Math.Max(0, width), atY + Math.Max(0, height));
        if (Normalise(angle) == 0d) return (atX, atY, atX + width, atY + height);

        var (cos, sin) = RotationOf(angle);
        var centreX = CentreX(atX, width);
        var centreY = CentreY(atY, height);
        var halfW = width / 2.0;
        var halfH = height / 2.0;
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        for (var corner = 0; corner < 4; corner++)
        {
            var dx = (corner & 1) == 0 ? -halfW : halfW;
            var dy = (corner & 2) == 0 ? -halfH : halfH;
            var x = centreX + dx * cos - dy * sin;
            var y = centreY + dx * sin + dy * cos;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
        return ((int)Math.Floor(minX), (int)Math.Floor(minY), (int)Math.Ceiling(maxX), (int)Math.Ceiling(maxY));
    }

    /// <summary>
    /// 目标像素 → 字块内的源坐标（<b>反向</b>映射，即转 −θ）。像素按中心取样，所以要各减去 0.5。
    /// <para>这一条必须由绘制端与判据端共用：反过来在绘制里用一套、断言里另推一套，
    /// 测出来的就不是真正会画出去的那张图。</para>
    /// </summary>
    public static (double SourceX, double SourceY) SourceOf(
        int atX, int atY, int width, int height, double angle, double targetX, double targetY)
    {
        var (cos, sin) = RotationOf(angle);
        var centreX = CentreX(atX, width);
        var centreY = CentreY(atY, height);
        var dx = targetX - centreX;
        var dy = targetY - centreY;
        // R(−θ) = [ cos  sin ; −sin  cos ]
        return (centreX + dx * cos + dy * sin, centreY - dx * sin + dy * cos);
    }
}
