#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// <b>落笔步进的唯一步长表</b>（批次 S2-d 第一刀：整合 §3.4"怎么画只有一处"里可验证的那半）。
/// <para>
/// 合并前"沿两点连线每约一个像素盖一个点"这条式子在渲染链里有<b>三份</b>：截图那条的 <c>Segment</c>、
/// 同文件里马赛克那条的 <c>Walk</c>、画布那条的 <c>CanvasCompositor.Walk</c>。三份写的是同一条式子，
/// 但<b>字形各不相同</b>（一份提前 return、一份把 <c>steps == 0</c> 判在循环里、一份把 x/y 先赋成端点再覆盖），
/// 于是"改一份、另两份不知道"这件事没有任何测会红——与 WS 那份图标几何同族。
/// </para>
/// <para>
/// 所以这里钉的是<b>等价</b>，不是"有个新类"：同一对端点，新的 <see cref="InkPath"/> 必须与
/// <b>三份旧写法逐点相等</b>。下面三段参照实现是<b>逐字搬来的改动前代码，不许"顺手优化"</b>——
/// 它们存在的唯一理由就是复现旧行为（含那个只在 <c>steps == 0</c> 才成立的早退分支）。
/// 逐点相等 ⇒ 输出缓冲相等，因为下游（盖章／混合／裁剪）一行都没动。
/// </para>
/// </summary>
public sealed class InkPathTests
{
    public static TheoryData<string, PixelPoint, PixelPoint> Shapes() => new()
    {
        { "同一点", new PixelPoint(7, 9), new PixelPoint(7, 9) },
        { "一像素·水平", new PixelPoint(3, 4), new PixelPoint(4, 4) },
        { "一像素·垂直", new PixelPoint(3, 4), new PixelPoint(3, 5) },
        { "半程落在 .5 上", new PixelPoint(0, 0), new PixelPoint(1, 2) },
        { "半程落在 .5 上·反向", new PixelPoint(1, 2), new PixelPoint(0, 0) },
        { "长水平", new PixelPoint(2, 6), new PixelPoint(31, 6) },
        { "长垂直", new PixelPoint(2, 6), new PixelPoint(2, 41) },
        { "正斜率", new PixelPoint(4, 4), new PixelPoint(23, 19) },
        { "反斜率", new PixelPoint(23, 4), new PixelPoint(4, 19) },
        { "反向拖·跨过原点", new PixelPoint(-3, -4), new PixelPoint(5, 2) },
        { "扁斜线（步长由 y 决定）", new PixelPoint(10, 10), new PixelPoint(11, 26) },
        { "长斜线", new PixelPoint(0, 0), new PixelPoint(100, -37) },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheSteppingIsTheOldFormulaOnEveryShape(string shape, PixelPoint a, PixelPoint b)
    {
        var merged = Stepped(a, b);
        Assert.True(merged.Count > 0, shape);
        Assert.Equal(AsSegmentDid(a, b), merged);          // 截图那条（圆点沿线铺开）
        Assert.Equal(AsMosaicWalkDid(a, b), merged);       // 同文件里马赛克那条（格子沿笔刷铺开）
        Assert.Equal(AsCanvasWalkDid(a, b), merged);       // 画布那条（圆盘沿笔迹盖章）
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void BothEndpointsAreStampedAndThePathHasNoGap(string shape, PixelPoint a, PixelPoint b)
    {
        var points = Stepped(a, b);
        // 端点各一次：跳过 i==0 会让线段起点缺一个圆帽（症状＝笔迹两头比中间细）
        Assert.Equal((a.X, a.Y), (points[0].X, points[0].Y));
        Assert.Equal((b.X, b.Y), (points[^1].X, points[^1].Y));
        Assert.Equal(Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)) + 1, points.Count);
        for (var i = 1; i < points.Count; i++)
            // 步长 1 像素：相邻两点的切比雪夫距离不许超过 1，否则线上有洞（"断续"是用户看得见的）
            Assert.True(Math.Max(Math.Abs(points[i].X - points[i - 1].X),
                                 Math.Abs(points[i].Y - points[i - 1].Y)) <= 1,
                $"{shape}：第 {i} 步跳了 {points[i - 1]} → {points[i]}，线上会有缺口");
    }

    /// <summary>
    /// 半程恰好落在 .5 时<b>必须远离零进位</b>。默认的银行家舍入会给出 0，
    /// 于是同一条线"正向拖"与"反向拖"落点不对称——圆点不连续，粗笔上看得到断口。
    /// </summary>
    [Fact]
    public void TheMidpointGoesAwayFromZero()
        => Assert.Equal(
            new[] { new PixelPoint(0, 0), new PixelPoint(1, 1), new PixelPoint(1, 2) },
            Stepped(new PixelPoint(0, 0), new PixelPoint(1, 2)));    // 换成银行家舍入，中间那颗会是 (0,1)

    /// <summary>
    /// 整条渲染链里"两点之间逐像素步进"只许有一处实现，且三个老调用点<b>真的</b>改调它。
    /// <para>闸门扫的是整个 <c>src</c>：将来任何一处再手写一份内联循环（画布、截图、或新加的表面），
    /// 这条立刻红——它守的是"下一次分岔"，不是这一次。</para>
    /// </summary>
    [Fact]
    public void TheStepInterpolationLivesInExactlyOnePlace()
    {
        var offenders = Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("Math.Max(Math.Abs"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Equal(new[] { "InkPath.cs" }, offenders);

        // 调用点计数：截图那条两处（线段＋马赛克笔刷）、画布那条一处。少一个＝那份循环还留在原地。
        Assert.Equal(2, Count(ReadRepoFile("src/StarMark.Core/Capture/AnnotationPainter.cs"), "InkPath.Each("));
        Assert.Equal(1, Count(ReadRepoFile("src/StarMark.Core/Canvas/CanvasCompositor.cs"), "InkPath.Each("));
    }

    // ────────── 逐点采集 ──────────

    private static List<PixelPoint> Stepped(PixelPoint a, PixelPoint b)
    {
        var seen = new List<PixelPoint>();
        InkPath.Each(a, b, (x, y) => seen.Add(new PixelPoint(x, y)));
        return seen;
    }

    // ────────── 参照实现（＝改动前的三份代码，逐字搬来，不许优化） ──────────

    /// <summary>截图那条 <c>AnnotationPainter.Segment</c> 的原样循环。</summary>
    private static List<PixelPoint> AsSegmentDid(PixelPoint a, PixelPoint b)
    {
        var seen = new List<PixelPoint>();
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (steps == 0) { seen.Add(a); return seen; }
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            seen.Add(new PixelPoint(
                (int)Math.Round(a.X + dx * t, MidpointRounding.AwayFromZero),
                (int)Math.Round(a.Y + dy * t, MidpointRounding.AwayFromZero)));
        }
        return seen;
    }

    /// <summary>同文件里马赛克那条 <c>AnnotationPainter.Walk</c> 的原样循环。</summary>
    private static List<PixelPoint> AsMosaicWalkDid(PixelPoint a, PixelPoint b)
    {
        var seen = new List<PixelPoint>();
        var steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        if (steps == 0) { seen.Add(a); return seen; }
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            seen.Add(new PixelPoint(
                (int)Math.Round(a.X + (b.X - a.X) * t, MidpointRounding.AwayFromZero),
                (int)Math.Round(a.Y + (b.Y - a.Y) * t, MidpointRounding.AwayFromZero)));
        }
        return seen;
    }

    /// <summary>画布那条 <c>CanvasCompositor.Walk</c> 的原样循环（把"0 步"判在循环体里的那一份）。</summary>
    private static List<PixelPoint> AsCanvasWalkDid(PixelPoint a, PixelPoint b)
    {
        var seen = new List<PixelPoint>();
        var steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
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
            seen.Add(new PixelPoint(x, y));
        }
        return seen;
    }
}
