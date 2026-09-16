#nullable enable
using Windows.Graphics;
using Xunit;
using StarMark.Core.Widgets;

namespace StarMark.Tests;

/// <summary>
/// 桌面组件边缘吸附算法测试。
/// 前 8 个用例为 DeskBox <c>WidgetSnapCalculatorTests</c> 的等价移植（权威行为契约），
/// 后 9 个为 StarMark 补充用例（物理像素尺度下的贴合/对齐/屏幕边缘）。
/// 全部坐标为物理像素。
/// </summary>
public sealed class WidgetSnapCalculatorTests
{
    private static readonly WidgetSnapTarget s_target = new(
        new RectInt32(202, 202, 100, 100),
        new IntPtr(42));

    private static readonly RectInt32 s_work = new(0, 0, 1920, 1040);

    // ───────── DeskBox 移植用例（spacing 5 / engage 8 / release 12）─────────

    [Fact]
    public void Move_RightEdgeKeepsConfiguredGapBeforeTarget()
    {
        WidgetMoveSnapResult result = Move(new RectInt32(96, 220, 100, 60));

        Assert.Equal(97, result.Bounds.X);
        WidgetSnapMatch match = AssertSnap(result.HorizontalMatch);
        Assert.Equal(WidgetSnapEdge.Right, match.SourceEdge);
        Assert.Equal(WidgetSnapEdge.Left, match.TargetEdge);
        Assert.True(match.UsesSpacing);
        Assert.Equal(197, match.Coordinate);
    }

    [Fact]
    public void Move_LeftEdgeKeepsConfiguredGapAfterTarget()
    {
        var target = new WidgetSnapTarget(new RectInt32(0, 20, 100, 100), new IntPtr(43));

        WidgetMoveSnapResult result = WidgetSnapCalculator.ResolveMove(
            new RectInt32(104, 40, 100, 60),
            [target],
            workArea: null,
            spacing: 5,
            engageThreshold: 8,
            releaseThreshold: 12);

        Assert.Equal(105, result.Bounds.X);
        WidgetSnapMatch match = AssertSnap(result.HorizontalMatch);
        Assert.Equal(WidgetSnapEdge.Left, match.SourceEdge);
        Assert.Equal(WidgetSnapEdge.Right, match.TargetEdge);
        Assert.True(match.UsesSpacing);
    }

    [Fact]
    public void Move_BottomEdgeKeepsConfiguredGapAboveTarget()
    {
        WidgetMoveSnapResult result = Move(new RectInt32(220, 96, 60, 100));

        Assert.Equal(97, result.Bounds.Y);
        WidgetSnapMatch match = AssertSnap(result.VerticalMatch);
        Assert.Equal(WidgetSnapEdge.Bottom, match.SourceEdge);
        Assert.Equal(WidgetSnapEdge.Top, match.TargetEdge);
        Assert.True(match.UsesSpacing);
    }

    [Fact]
    public void Move_TopEdgeKeepsConfiguredGapBelowTarget()
    {
        var target = new WidgetSnapTarget(new RectInt32(20, 0, 100, 100), new IntPtr(44));

        WidgetMoveSnapResult result = WidgetSnapCalculator.ResolveMove(
            new RectInt32(40, 104, 60, 100),
            [target],
            workArea: null,
            spacing: 5,
            engageThreshold: 8,
            releaseThreshold: 12);

        Assert.Equal(105, result.Bounds.Y);
        WidgetSnapMatch match = AssertSnap(result.VerticalMatch);
        Assert.Equal(WidgetSnapEdge.Top, match.SourceEdge);
        Assert.Equal(WidgetSnapEdge.Bottom, match.TargetEdge);
        Assert.True(match.UsesSpacing);
    }

    [Fact]
    public void Move_SameEdgeAlignmentDoesNotAddSpacing()
    {
        WidgetMoveSnapResult result = Move(new RectInt32(205, 220, 80, 60));

        Assert.Equal(202, result.Bounds.X);
        WidgetSnapMatch match = AssertSnap(result.HorizontalMatch);
        Assert.Equal(WidgetSnapEdge.Left, match.SourceEdge);
        Assert.Equal(WidgetSnapEdge.Left, match.TargetEdge);
        Assert.False(match.UsesSpacing);
    }

    [Fact]
    public void Move_WorkAreaRightAndBottomEdgesUseZeroGap()
    {
        WidgetMoveSnapResult result = WidgetSnapCalculator.ResolveMove(
            new RectInt32(901, 701, 100, 100),
            [],
            new RectInt32(0, 0, 1000, 800),
            spacing: 5,
            engageThreshold: 8,
            releaseThreshold: 12);

        Assert.Equal(900, result.Bounds.X);
        Assert.Equal(700, result.Bounds.Y);
        Assert.Equal(IntPtr.Zero, AssertSnap(result.HorizontalMatch).TargetWindowHandle);
        Assert.Equal(IntPtr.Zero, AssertSnap(result.VerticalMatch).TargetWindowHandle);
    }

    [Fact]
    public void Move_StickyMatchHoldsUntilReleaseThreshold()
    {
        WidgetMoveSnapResult engaged = Move(new RectInt32(96, 220, 100, 60));
        WidgetSnapMatch sticky = AssertSnap(engaged.HorizontalMatch);

        WidgetMoveSnapResult held = WidgetSnapCalculator.ResolveMove(
            new RectInt32(108, 220, 100, 60),
            [s_target],
            workArea: null,
            spacing: 5,
            engageThreshold: 8,
            releaseThreshold: 12,
            stickyHorizontal: sticky);
        WidgetMoveSnapResult released = WidgetSnapCalculator.ResolveMove(
            new RectInt32(110, 220, 100, 60),
            [s_target],
            workArea: null,
            spacing: 5,
            engageThreshold: 8,
            releaseThreshold: 12,
            stickyHorizontal: sticky);

        Assert.Equal(97, held.Bounds.X);
        Assert.NotNull(held.HorizontalMatch);
        Assert.Equal(110, released.Bounds.X);
        Assert.Null(released.HorizontalMatch);
    }

    [Fact]
    public void Resize_RightEdgeUsesConfiguredGap()
    {
        WidgetSnapMatch? match = WidgetSnapCalculator.ResolveResizeEdge(
            new RectInt32(96, 220, 100, 60),
            WidgetSnapEdge.Right,
            [s_target],
            workArea: null,
            spacing: 5,
            threshold: 8);

        WidgetSnapMatch snap = AssertSnap(match);
        Assert.Equal(197, snap.Coordinate);
        Assert.True(snap.UsesSpacing);
    }

    // ───────── StarMark 补充用例（spacing 8 / engage 24，1920×1040）─────────

    [Fact]
    public void LeftEdge_AbutsToTargetRight_WithSpacing()
    {
        // 目标右缘 500 → 贴合位 508；候选左缘 530（偏差 22 ≤ 24）吸到 508。
        // 同时候选顶缘 120 与目标顶缘 100 相差 20，属真实的顶边对齐，纵向也吸附到 100。
        // 两个轴都基于原始 proposed 求解，互不影响。
        var target = new RectInt32(300, 100, 200, 300);
        var snapped = Snap(new RectInt32(530, 120, 200, 200), target);
        Assert.Equal(508, snapped.X);
        Assert.Equal(100, snapped.Y);
    }

    [Fact]
    public void LeftEdge_AlignsWithTargetLeft_WhenOverlapping()
    {
        var target = new RectInt32(300, 100, 200, 300);
        var snapped = Snap(new RectInt32(305, 350, 200, 200), target);
        Assert.Equal(300, snapped.X);
    }

    [Fact]
    public void PerpendicularDistanceIsTiebreakerNotGate()
    {
        // DeskBox 语义：垂直距离只作同 delta 时的打散条件，不作门槛。
        // 纵向相距 100px 时，左缘偏差 5 依然对齐（与 Figma 智能参考线一致）。
        var target = new RectInt32(300, 100, 200, 300);
        var snapped = Snap(new RectInt32(305, 500, 200, 200), target);
        Assert.Equal(300, snapped.X);
    }

    [Fact]
    public void Snaps_ToMonitorLeftEdge()
    {
        // 计算器按零间隙吸附屏幕边，留白由 InsetWorkArea 完成
        var snapped = SnapInset(new RectInt32(4, 100, 200, 200));
        Assert.Equal(8, snapped.X);
    }

    [Fact]
    public void Snaps_ToMonitorTopEdge()
    {
        var snapped = SnapInset(new RectInt32(300, 3, 200, 200));
        Assert.Equal(8, snapped.Y);
    }

    [Fact]
    public void NoSnap_WhenDeltaExceedsThreshold()
    {
        // 目标右缘 500 → 贴合位 508；候选左缘 560，偏差 52 > 24
        var target = new RectInt32(300, 100, 200, 300);
        var snapped = Snap(new RectInt32(560, 120, 200, 200), target);
        Assert.Equal(560, snapped.X);
    }

    [Fact]
    public void TopEdge_AbutsToTargetBottom_WithSpacing()
    {
        // 目标底缘 400 → 贴合位 408；候选顶缘 414（偏差 6）
        var target = new RectInt32(100, 100, 300, 300);
        var snapped = Snap(new RectInt32(140, 414, 200, 200), target);
        Assert.Equal(408, snapped.Y);
        Assert.Equal(140, snapped.X);
    }

    [Fact]
    public void RightEdge_AlignsWithTargetRight()
    {
        var target = new RectInt32(300, 100, 200, 300);
        var snapped = Snap(new RectInt32(306, 150, 200, 200), target);
        Assert.Equal(300, snapped.X);
    }

    [Fact]
    public void EmptyTargets_OnlyMonitorSnaps()
    {
        var snapped = Snap(new RectInt32(900, 500, 200, 200));
        Assert.Equal(900, snapped.X);
        Assert.Equal(500, snapped.Y);
    }

    [Fact]
    public void InsetWorkArea_KeepsCenterAndClamps()
    {
        var inset = WidgetSnapCalculator.InsetWorkArea(new RectInt32(0, 0, 1000, 800), 8);
        Assert.Equal(new RectInt32(8, 8, 984, 784), inset);
        Assert.Equal(new RectInt32(0, 0, 1000, 800), WidgetSnapCalculator.InsetWorkArea(new RectInt32(0, 0, 1000, 800), 0));
    }

    // ───────── 辅助 ─────────

    private static WidgetMoveSnapResult Move(RectInt32 proposedBounds) =>
        WidgetSnapCalculator.ResolveMove(
            proposedBounds,
            [s_target],
            workArea: null,
            spacing: 5,
            engageThreshold: 8,
            releaseThreshold: 12);

    private static RectInt32 Snap(RectInt32 proposed, params RectInt32[] targets) =>
        WidgetSnapCalculator.SnapMove(proposed, targets, s_work, spacing: 8, engageThreshold: 24);

    private static RectInt32 SnapInset(RectInt32 proposed, params RectInt32[] targets) =>
        WidgetSnapCalculator.SnapMove(
            proposed,
            targets,
            WidgetSnapCalculator.InsetWorkArea(s_work, WidgetSnapCalculator.DefaultScreenMargin),
            spacing: 8,
            engageThreshold: 24);

    private static WidgetSnapMatch AssertSnap(WidgetSnapMatch? match)
    {
        Assert.True(match.HasValue);
        return match.GetValueOrDefault();
    }
}
