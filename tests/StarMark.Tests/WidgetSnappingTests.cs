#nullable enable
using Windows.Graphics;
using Xunit;
using StarMark.Core.Widgets;

namespace StarMark.Tests;

/// <summary>
/// 桌面组件边缘吸附算法测试（移植自 DeskBox WidgetSnapCalculator 的行为契约）：
/// 边缘对齐 / 边缘贴合（留间距）/ 显示器边缘 / 垂直投影门限 / 阈值外不吸附。
/// 全部坐标为物理像素。
/// </summary>
public sealed class WidgetSnappingTests
{
    private static readonly RectInt32 Work = new(0, 0, 1920, 1040);

    private static RectInt32 Snap(RectInt32 proposed, params RectInt32[] targets)
        => WidgetSnapping.SnapMove(proposed, targets, Work, threshold: 24, spacing: 8);

    [Fact]
    public void LeftEdge_AbutsToTargetRight_WithSpacing()
    {
        // 目标右缘 500，贴合位 = 508；候选窗口左缘 530（偏差 22）应吸到 508；
        // 水平投影相距 30（>阈值），纵向不应被吸附
        var target = new RectInt32(300, 100, 200, 300);
        var proposed = new RectInt32(530, 120, 200, 200);
        var snapped = Snap(proposed, target);
        Assert.Equal(508, snapped.X);
        Assert.Equal(120, snapped.Y);
    }

    [Fact]
    public void LeftEdge_AlignsWithTargetLeft_WhenOverlapping()
    {
        // 垂直投影重叠（350..550 与 100..400），左缘偏差 5 → 对齐到 300
        var target = new RectInt32(300, 100, 200, 300);
        var proposed = new RectInt32(305, 350, 200, 200);
        var snapped = Snap(proposed, target);
        Assert.Equal(300, snapped.X);
    }

    [Fact]
    public void NoSnap_WhenPerpendicularGapExceedsThreshold()
    {
        // 垂直方向相距 100（500.. vs 目标底 400），即使左缘接近也不应吸附
        var target = new RectInt32(300, 100, 200, 300);
        var proposed = new RectInt32(305, 500, 200, 200);
        var snapped = Snap(proposed, target);
        Assert.Equal(305, snapped.X);
    }

    [Fact]
    public void Snaps_ToMonitorLeftEdge()
    {
        var proposed = new RectInt32(4, 100, 200, 200);
        var snapped = Snap(proposed);
        Assert.Equal(8, snapped.X);
    }

    [Fact]
    public void Snaps_ToMonitorTopEdge()
    {
        var proposed = new RectInt32(300, 3, 200, 200);
        var snapped = Snap(proposed);
        Assert.Equal(8, snapped.Y);
    }

    [Fact]
    public void NoSnap_WhenDeltaExceedsThreshold()
    {
        // 目标右缘 500 → 贴合位 508；候选左缘 560，偏差 52 > 24
        var target = new RectInt32(300, 100, 200, 300);
        var proposed = new RectInt32(560, 120, 200, 200);
        var snapped = Snap(proposed, target);
        Assert.Equal(560, snapped.X);
        Assert.Equal(120, snapped.Y);
    }

    [Fact]
    public void TopEdge_AbutsToTargetBottom_WithSpacing()
    {
        // 目标底缘 400，贴合位 = 408；候选顶 414（偏差 6）；X 错开超过阈值避免横向对齐干扰
        var target = new RectInt32(100, 100, 300, 300);
        var proposed = new RectInt32(140, 414, 200, 200);
        var snapped = Snap(proposed, target);
        Assert.Equal(408, snapped.Y);
        Assert.Equal(140, snapped.X);
    }

    [Fact]
    public void RightEdge_AlignsWithTargetRight()
    {
        // 目标右缘 500；候选右缘 506（X=306,W=200），水平投影重叠
        var target = new RectInt32(300, 100, 200, 300);
        var proposed = new RectInt32(306, 150, 200, 200);
        var snapped = Snap(proposed, target);
        Assert.Equal(300, snapped.X); // 右缘对齐 500 → X=300
    }

    [Fact]
    public void EmptyTargets_OnlyMonitorSnaps()
    {
        var proposed = new RectInt32(900, 500, 200, 200);
        var snapped = Snap(proposed);
        Assert.Equal(900, snapped.X);
        Assert.Equal(500, snapped.Y);
        Assert.Equal(200, snapped.Width);
        Assert.Equal(200, snapped.Height);
    }
}
