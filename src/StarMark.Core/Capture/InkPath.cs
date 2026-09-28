#nullable enable
using System;
using StarMark.Abstractions.Capture;

namespace StarMark.Core.Capture;

/// <summary>
/// 两点之间"每约一个像素一个落点"的<b>唯一步进表</b>（批次 S2-d 第一刀）。
/// <para>
/// 合并前这段算式在渲染链里有<b>三份</b>：截图那条的 <c>Segment</c>（圆点沿线铺开）、
/// 同文件里马赛克那条的 <c>Walk</c>（笔刷路径铺开格子）、画布那条的 <c>CanvasCompositor.Walk</c>
/// （圆盘沿线盖章）。三份写的都是同一条式子，但字形不一样（一份提前返回、一份在循环里判 <c>steps == 0</c>），
/// 于是谁改一份"步长取 max 还是取斜边"、谁换一种舍入，另外两条都不知道——
/// 症状会是"同一条线在截图上连着、在画布上断续"，而没有任何测会红（与 WS 那份图标几何同族）。
/// </para>
/// <para>
/// <b>为什么端点也算一个落点</b>：圆头笔刷在端点处要留下半圆，跳过 <c>i == 0</c> 会让线段起点缺一个帽。
/// <b>为什么步长是 1 像素而不是 0.5</b>：更密只是在同一像素上多叠几次（取大规则下无事发生），更疏会断线。
/// </para>
/// </summary>
public static class InkPath
{
    /// <summary>
    /// 沿 <paramref name="a"/>→<paramref name="b"/> 逐点回调（含两端）。
    /// <para>两点重合时只回调一次 <paramref name="a"/>——不是"回调两次同一个点"：
    /// 对橡皮那条链（按比例减 alpha）来说多走一次就是多擦一次。</para>
    /// </summary>
    public static void Each(PixelPoint a, PixelPoint b, Action<int, int> visit)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (steps == 0)
        {
            visit(a.X, a.Y);
            return;
        }
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            visit(
                (int)Math.Round(a.X + dx * t, MidpointRounding.AwayFromZero),
                (int)Math.Round(a.Y + dy * t, MidpointRounding.AwayFromZero));
        }
    }
}
