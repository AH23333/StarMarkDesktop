#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 画布"模型那一份"与"渲放那一份"之间的往返对照（方案 §3.4：`CanvasStroke` 退成渲放层内部形状，
/// 持久笔迹归 <see cref="InkDoc"/>）。
/// <para>
/// 这条链是 S2 里最容易"看起来一样"的一段：形状换了名字、点序换了载体，屏幕上的墨却还是那滩墨——
/// 一旦往返丢了一个点或换了一次半径，症状只有真机上看得出来（椭圆露棱角、橡皮擦过头、撤销留残影）。
/// 所以这里不比"字段相等"就算完，而是<b>把两条路径各自画进缓冲，逐像素对差分</b>
/// （沿用 <c>CanvasKernelIdentityTests</c> 的口径：数字守不住机器差异，形状与像素守得住）。
/// </para>
/// </summary>
public sealed class BoardInkRoundTripTests
{
    private const int W = 64;
    private const int H = 48;

    private static uint[] Paint(CanvasStroke stroke)
    {
        var buffer = new uint[W * H];
        CanvasCompositor.Clear(buffer);
        CanvasCompositor.Paint(buffer, W, H, stroke);
        return buffer;
    }

    /// <summary>三种点串形状：单点（落一下）、两点（拖一下）、密点（长笔迹／图形采样轮廓）。</summary>
    private static readonly CanvasTool[] AllTools = Enum.GetValues<CanvasTool>();

    private static IEnumerable<PixelPoint> Dense(PixelPoint from, PixelPoint to)
    {
        var steps = 24;
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            yield return new PixelPoint(
                (int)Math.Round(from.X + (to.X - from.X) * t),
                (int)Math.Round(from.Y + (to.Y - from.Y) * t));
        }
    }

    private static CanvasStroke Build(CanvasTool tool, int width, IReadOnlyList<PixelPoint> points)
    {
        if (points.Count == 0) throw new ArgumentException("空点串没意义");
        // 走实时那条路（Begin + Extend）＝与真机上画出来的一模一样：相邻 <2px 的抖动会被合并掉
        var stroke = new CanvasStroke(tool, Annotation.Opaque(96, 32, 255), width, points[0]);
        foreach (var p in points.Skip(1)) stroke.AddPoint(p);
        return stroke;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RoundTripPaintsIdenticalPixels(CanvasTool tool, int width, PixelPoint[] points)
    {
        var live = Build(tool, width, points);
        var rebuilt = CanvasStroke.FromAnnotation(live.ToAnnotation());

        Assert.True(Paint(live).AsSpan().SequenceEqual(Paint(rebuilt)),
            $"同一条墨走两条路（旧笔迹直接画 / 先落成标注再还原）画出来的像素必须逐位相等：{tool} 宽 {width}");
    }

    public static TheoryData<CanvasTool, int, PixelPoint[]> Cases()
    {
        var data = new TheoryData<CanvasTool, int, PixelPoint[]>();
        foreach (var tool in AllTools)
            foreach (var width in CanvasWidths.Steps.Append(CanvasWidths.EraserDiameter))
            {
                data.Add(tool, width, new[] { new PixelPoint(12, 10) });
                data.Add(tool, width, new[] { new PixelPoint(6, 8), new PixelPoint(40, 30) });
                data.Add(tool, width, Dense(new PixelPoint(4, 6), new PixelPoint(58, 40)).ToArray());
            }
        return data;
    }

    [Fact]
    public void RoundTripKeepsPointsBoundsColourAndOrder()
    {
        foreach (var tool in AllTools)
        {
            var live = Build(tool, CanvasWidths.Steps[2], Dense(new PixelPoint(3, 4), new PixelPoint(50, 44)).ToArray());
            var rebuilt = CanvasStroke.FromAnnotation(live.ToAnnotation());

            Assert.Equal(live.Tool, rebuilt.Tool);
            Assert.Equal(live.Width, rebuilt.Width);
            Assert.Equal(live.ColorBgra, rebuilt.ColorBgra);
            Assert.Equal(live.EffectiveColorBgra, rebuilt.EffectiveColorBgra);
            Assert.Equal(live.Bounds, rebuilt.Bounds);
            Assert.True(live.Order == rebuilt.Order, "换一次载体不许换一次时间位置：撤销比的就是这个数");
            Assert.True(live.Points.SequenceEqual(rebuilt.Points),
                "点串必须整份还原——少一个点就是形状变了（椭圆的采样点被再削一次会露棱角）");
        }
    }

    [Fact]
    public void StoredAnnotationDoesNotAliasTheLivePointList()
    {
        // 存进模型的是快照。若把活的 List 直接交出去，之后这条笔迹还在被追加，
        // 撤销之后就可能出现"多出一截"或"越拖越像橡皮筋"那种只有真机看得见的错。
        var stroke = new CanvasStroke(CanvasTool.Pen, Annotation.Opaque(96, 32, 255), CanvasWidths.Steps[1], new PixelPoint(5, 5));
        var stored = stroke.ToAnnotation();
        var before = stored.Points.Count;

        stroke.AddPoint(new PixelPoint(30, 25));

        Assert.Equal(before, stored.Points.Count);
        Assert.NotEqual(stored.Points.Count, stroke.Points.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryWidthStepIsPaintableThroughTheModel(int step)
    {
        // 档位要真的落到最终产物上（WR 那一课的口径）：这里不数像素面积，只确认"经模型走一遍"这条路
        // 对每一档都成立——半径是从档位算出来的，档位接错就会在这里红。
        var width = CanvasWidths.At(step);
        var live = Build(CanvasTool.Pen, width, Dense(new PixelPoint(2, 24), new PixelPoint(61, 24)).ToArray());
        var rebuilt = CanvasStroke.FromAnnotation(live.ToAnnotation());
        Assert.True(Paint(live).AsSpan().SequenceEqual(Paint(rebuilt)), $"档位 {step}（宽 {width}）往返必须同图");
    }
}
