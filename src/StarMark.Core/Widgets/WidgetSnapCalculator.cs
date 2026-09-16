#nullable enable
using Windows.Graphics;

namespace StarMark.Core.Widgets;

/// <summary>被吸附的源窗口上的那条边。</summary>
public enum WidgetSnapEdge
{
    Left,
    Right,
    Top,
    Bottom
}

/// <summary>
/// 一个可吸附目标（通常是另一个组件窗口）。
/// <see cref="WindowHandle"/> 为 <see cref="IntPtr.Zero"/> 时表示显示器工作区。
/// </summary>
public readonly record struct WidgetSnapTarget(RectInt32 Bounds, IntPtr WindowHandle)
{
    public WidgetSnapTarget(RectInt32 bounds) : this(bounds, IntPtr.Zero) { }
}

/// <summary>一次成功吸附的完整描述，供调用方画对齐参考线 / 维持 sticky 态。</summary>
public readonly record struct WidgetSnapMatch(
    WidgetSnapEdge SourceEdge,
    WidgetSnapEdge TargetEdge,
    int Coordinate,
    int ResolvedOrigin,
    IntPtr TargetWindowHandle,
    bool UsesSpacing,
    int Delta);

public readonly record struct WidgetMoveSnapResult(
    RectInt32 Bounds,
    WidgetSnapMatch? HorizontalMatch,
    WidgetSnapMatch? VerticalMatch);

/// <summary>
/// 纯物理像素吸附求解器，移动与缩放会话共用。
/// 移植自 DeskBox <c>Services/WidgetSnapCalculator.cs</c>（v1.5.x），行为与其保持一致：
/// <list type="bullet">
///   <item>水平、垂直两轴都基于<b>原始</b> proposed 矩形求解，互不干扰（禁止把已吸附结果传给另一轴）；</item>
///   <item>垂直投影距离只作<b>打散条件</b>，不作门槛过滤——与 Figma / PowerPoint 的智能参考线一致，
///         远处窗口的同轴边依然可以对齐；</item>
///   <item>工作区四边按<b>零间隙</b>吸附，留边距由调用方自行内缩 workArea 实现；</item>
///   <item>支持 sticky 迟滞：engage 阈值内吸附，release 阈值外才松开，避免拖动时抖动。</item>
/// </list>
/// 调用方负责把用户侧的有效像素一次性换算为物理像素，使整次会话处于同一坐标系。
/// </summary>
public static class WidgetSnapCalculator
{
    /// <summary>组件贴合时的默认间距（物理像素）。</summary>
    public const int DefaultSpacing = 8;

    /// <summary>进入吸附的默认阈值（物理像素）。</summary>
    public const int DefaultEngageThreshold = 24;

    /// <summary>脱离吸附的默认阈值（物理像素），必须 ≥ engage 才能形成迟滞区间。</summary>
    public const int DefaultReleaseThreshold = 32;

    /// <summary>屏幕边缘默认留白（物理像素）。计算器按零间隙吸附，边距由调用方内缩 workArea 得到。</summary>
    public const int DefaultScreenMargin = 8;

    /// <summary>
    /// 求解一次移动吸附。两轴彼此独立，均基于 <paramref name="proposedBounds"/>。
    /// </summary>
    /// <param name="proposedBounds">拖动中的窗口矩形（物理像素）。</param>
    /// <param name="targets">其他组件窗口；不应包含自身。</param>
    /// <param name="workArea">当前显示器工作区（可为 null 表示不吸附到屏幕边缘）。</param>
    /// <param name="spacing">组件贴合时保留的间距。</param>
    /// <param name="engageThreshold">进入吸附的阈值。</param>
    /// <param name="releaseThreshold">脱离吸附的阈值。</param>
    /// <param name="stickyHorizontal">上一次的水平吸附结果，用于迟滞。</param>
    /// <param name="stickyVertical">上一次的垂直吸附结果，用于迟滞。</param>
    public static WidgetMoveSnapResult ResolveMove(
        RectInt32 proposedBounds,
        IReadOnlyList<WidgetSnapTarget> targets,
        RectInt32? workArea,
        int spacing = DefaultSpacing,
        int engageThreshold = DefaultEngageThreshold,
        int releaseThreshold = DefaultReleaseThreshold,
        WidgetSnapMatch? stickyHorizontal = null,
        WidgetSnapMatch? stickyVertical = null)
    {
        spacing = Math.Max(0, spacing);
        engageThreshold = Math.Max(0, engageThreshold);
        releaseThreshold = Math.Max(engageThreshold, releaseThreshold);

        // 关键：两个轴都以原始 proposedBounds 求解。
        // 早期实现把已吸附的 X 矩形传给 Y 轴，导致 X 吸附后横向投影间距变小、
        // Y 轴意外参与吸附（拖动时窗口纵向跳动）。
        WidgetSnapMatch? horizontal = ResolveStickyMoveMatch(
            proposedBounds.X,
            releaseThreshold,
            stickyHorizontal,
            horizontal: true) ??
            ResolveBestMoveMatch(
                proposedBounds,
                targets,
                workArea,
                spacing,
                engageThreshold,
                horizontal: true);
        WidgetSnapMatch? vertical = ResolveStickyMoveMatch(
            proposedBounds.Y,
            releaseThreshold,
            stickyVertical,
            horizontal: false) ??
            ResolveBestMoveMatch(
                proposedBounds,
                targets,
                workArea,
                spacing,
                engageThreshold,
                horizontal: false);

        return new WidgetMoveSnapResult(
            new RectInt32(
                horizontal?.ResolvedOrigin ?? proposedBounds.X,
                vertical?.ResolvedOrigin ?? proposedBounds.Y,
                proposedBounds.Width,
                proposedBounds.Height),
            horizontal,
            vertical);
    }

    /// <summary>求解缩放时某一条边的吸附（只处理给定的 <paramref name="sourceEdge"/>）。</summary>
    public static WidgetSnapMatch? ResolveResizeEdge(
        RectInt32 proposedBounds,
        WidgetSnapEdge sourceEdge,
        IReadOnlyList<WidgetSnapTarget> targets,
        RectInt32? workArea,
        int spacing = DefaultSpacing,
        int threshold = DefaultEngageThreshold)
    {
        spacing = Math.Max(0, spacing);
        threshold = Math.Max(0, threshold);
        SnapCandidate? best = null;
        EvaluateEdgeCandidates(
            proposedBounds,
            targets,
            workArea,
            spacing,
            sourceEdge,
            threshold,
            ref best);
        return best?.Match;
    }

    /// <summary>
    /// 便捷重载：只要矩形、不要匹配详情时使用（调用方无需跟踪 sticky）。
    /// </summary>
    public static RectInt32 SnapMove(
        RectInt32 proposed,
        IReadOnlyList<RectInt32> targets,
        RectInt32? workArea,
        int spacing = DefaultSpacing,
        int engageThreshold = DefaultEngageThreshold)
    {
        var list = new WidgetSnapTarget[targets.Count];
        for (int i = 0; i < targets.Count; i++) list[i] = new WidgetSnapTarget(targets[i]);
        return ResolveMove(proposed, list, workArea, spacing, engageThreshold).Bounds;
    }

    /// <summary>把显示器工作区内缩出留白，配合本计算器的零间隙屏幕吸附使用。</summary>
    public static RectInt32 InsetWorkArea(RectInt32 workArea, int margin)
    {
        if (margin <= 0) return workArea;
        int w = Math.Max(0, workArea.Width - margin * 2);
        int h = Math.Max(0, workArea.Height - margin * 2);
        return new RectInt32(workArea.X + margin, workArea.Y + margin, w, h);
    }

    private static WidgetSnapMatch? ResolveBestMoveMatch(
        RectInt32 source,
        IReadOnlyList<WidgetSnapTarget> targets,
        RectInt32? workArea,
        int spacing,
        int threshold,
        bool horizontal)
    {
        SnapCandidate? best = null;
        if (horizontal)
        {
            EvaluateEdgeCandidates(source, targets, workArea, spacing, WidgetSnapEdge.Left, threshold, ref best);
            EvaluateEdgeCandidates(source, targets, workArea, spacing, WidgetSnapEdge.Right, threshold, ref best);
        }
        else
        {
            EvaluateEdgeCandidates(source, targets, workArea, spacing, WidgetSnapEdge.Top, threshold, ref best);
            EvaluateEdgeCandidates(source, targets, workArea, spacing, WidgetSnapEdge.Bottom, threshold, ref best);
        }

        return best?.Match;
    }

    private static WidgetSnapMatch? ResolveStickyMoveMatch(
        int proposedOrigin,
        int releaseThreshold,
        WidgetSnapMatch? sticky,
        bool horizontal)
    {
        if (sticky is not { } match ||
            horizontal != (match.SourceEdge is WidgetSnapEdge.Left or WidgetSnapEdge.Right))
        {
            return null;
        }

        int delta = Math.Abs(proposedOrigin - match.ResolvedOrigin);
        return delta <= releaseThreshold
            ? match with { Delta = delta }
            : null;
    }

    private static void EvaluateEdgeCandidates(
        RectInt32 source,
        IReadOnlyList<WidgetSnapTarget> targets,
        RectInt32? workArea,
        int spacing,
        WidgetSnapEdge sourceEdge,
        int threshold,
        ref SnapCandidate? best)
    {
        foreach (WidgetSnapTarget target in targets)
        {
            EvaluateWidgetCandidates(source, target, spacing, sourceEdge, threshold, ref best);
        }

        if (workArea is { } area)
        {
            ConsiderCandidate(CreateWorkAreaCandidate(source, area, sourceEdge), threshold, ref best);
        }
    }

    private static void EvaluateWidgetCandidates(
        RectInt32 source,
        WidgetSnapTarget target,
        int spacing,
        WidgetSnapEdge sourceEdge,
        int threshold,
        ref SnapCandidate? best)
    {
        RectInt32 other = target.Bounds;
        int perpendicularGap = sourceEdge is WidgetSnapEdge.Left or WidgetSnapEdge.Right
            ? IntervalGap(source.Y, source.Y + source.Height, other.Y, other.Y + other.Height)
            : IntervalGap(source.X, source.X + source.Width, other.X, other.X + other.Width);
        int perpendicularCenterDistance = sourceEdge is WidgetSnapEdge.Left or WidgetSnapEdge.Right
            ? Math.Abs(source.Y + source.Height / 2 - (other.Y + other.Height / 2))
            : Math.Abs(source.X + source.Width / 2 - (other.X + other.Width / 2));

        switch (sourceEdge)
        {
            case WidgetSnapEdge.Left:
                ConsiderCandidate(CreateCandidate(
                    source, sourceEdge, WidgetSnapEdge.Left, other.X,
                    target.WindowHandle, usesSpacing: false, priority: 2,
                    perpendicularGap, perpendicularCenterDistance), threshold, ref best);
                ConsiderCandidate(CreateCandidate(
                    source, sourceEdge, WidgetSnapEdge.Right, other.X + other.Width + spacing,
                    target.WindowHandle, usesSpacing: true, priority: 1,
                    perpendicularGap, perpendicularCenterDistance), threshold, ref best);
                break;

            case WidgetSnapEdge.Right:
                ConsiderCandidate(CreateCandidate(
                    source, sourceEdge, WidgetSnapEdge.Right, other.X + other.Width,
                    target.WindowHandle, usesSpacing: false, priority: 2,
                    perpendicularGap, perpendicularCenterDistance), threshold, ref best);
                ConsiderCandidate(CreateCandidate(
                    source, sourceEdge, WidgetSnapEdge.Left, other.X - spacing,
                    target.WindowHandle, usesSpacing: true, priority: 1,
                    perpendicularGap, perpendicularCenterDistance), threshold, ref best);
                break;

            case WidgetSnapEdge.Top:
                ConsiderCandidate(CreateCandidate(
                    source, sourceEdge, WidgetSnapEdge.Top, other.Y,
                    target.WindowHandle, usesSpacing: false, priority: 2,
                    perpendicularGap, perpendicularCenterDistance), threshold, ref best);
                ConsiderCandidate(CreateCandidate(
                    source, sourceEdge, WidgetSnapEdge.Bottom, other.Y + other.Height + spacing,
                    target.WindowHandle, usesSpacing: true, priority: 1,
                    perpendicularGap, perpendicularCenterDistance), threshold, ref best);
                break;

            case WidgetSnapEdge.Bottom:
                ConsiderCandidate(CreateCandidate(
                    source, sourceEdge, WidgetSnapEdge.Bottom, other.Y + other.Height,
                    target.WindowHandle, usesSpacing: false, priority: 2,
                    perpendicularGap, perpendicularCenterDistance), threshold, ref best);
                ConsiderCandidate(CreateCandidate(
                    source, sourceEdge, WidgetSnapEdge.Top, other.Y - spacing,
                    target.WindowHandle, usesSpacing: true, priority: 1,
                    perpendicularGap, perpendicularCenterDistance), threshold, ref best);
                break;
        }
    }

    private static SnapCandidate CreateWorkAreaCandidate(
        RectInt32 source,
        RectInt32 workArea,
        WidgetSnapEdge sourceEdge)
    {
        int coordinate = sourceEdge switch
        {
            WidgetSnapEdge.Left => workArea.X,
            WidgetSnapEdge.Right => workArea.X + workArea.Width,
            WidgetSnapEdge.Top => workArea.Y,
            WidgetSnapEdge.Bottom => workArea.Y + workArea.Height,
            _ => 0
        };
        return CreateCandidate(
            source,
            sourceEdge,
            sourceEdge,
            coordinate,
            IntPtr.Zero,
            usesSpacing: false,
            priority: 0,
            perpendicularGap: 0,
            perpendicularCenterDistance: 0);
    }

    private static SnapCandidate CreateCandidate(
        RectInt32 source,
        WidgetSnapEdge sourceEdge,
        WidgetSnapEdge targetEdge,
        int coordinate,
        IntPtr targetWindowHandle,
        bool usesSpacing,
        int priority,
        int perpendicularGap,
        int perpendicularCenterDistance)
    {
        int currentCoordinate = GetEdgeCoordinate(source, sourceEdge);
        int resolvedOrigin = sourceEdge switch
        {
            WidgetSnapEdge.Left => coordinate,
            WidgetSnapEdge.Right => coordinate - source.Width,
            WidgetSnapEdge.Top => coordinate,
            WidgetSnapEdge.Bottom => coordinate - source.Height,
            _ => 0
        };
        int delta = Math.Abs(currentCoordinate - coordinate);
        return new SnapCandidate(
            new WidgetSnapMatch(
                sourceEdge,
                targetEdge,
                coordinate,
                resolvedOrigin,
                targetWindowHandle,
                usesSpacing,
                delta),
            priority,
            perpendicularGap,
            perpendicularCenterDistance);
    }

    private static void ConsiderCandidate(
        SnapCandidate candidate,
        int threshold,
        ref SnapCandidate? best)
    {
        if (candidate.Match.Delta > threshold ||
            best is { } current && !IsBetter(candidate, current))
        {
            return;
        }

        best = candidate;
    }

    private static bool IsBetter(SnapCandidate candidate, SnapCandidate current) =>
        candidate.Match.Delta < current.Match.Delta ||
        candidate.Match.Delta == current.Match.Delta &&
        (candidate.Priority < current.Priority ||
         candidate.Priority == current.Priority &&
         (candidate.PerpendicularGap < current.PerpendicularGap ||
          candidate.PerpendicularGap == current.PerpendicularGap &&
          candidate.PerpendicularCenterDistance < current.PerpendicularCenterDistance));

    private static int GetEdgeCoordinate(RectInt32 bounds, WidgetSnapEdge edge) =>
        edge switch
        {
            WidgetSnapEdge.Left => bounds.X,
            WidgetSnapEdge.Right => bounds.X + bounds.Width,
            WidgetSnapEdge.Top => bounds.Y,
            WidgetSnapEdge.Bottom => bounds.Y + bounds.Height,
            _ => 0
        };

    /// <summary>两个区间在另一轴上的距离：重叠为 0，否则为间隔像素。</summary>
    public static int IntervalGap(int firstStart, int firstEnd, int secondStart, int secondEnd)
    {
        if (firstEnd < secondStart) return secondStart - firstEnd;
        return secondEnd < firstStart ? firstStart - secondEnd : 0;
    }

    private readonly record struct SnapCandidate(
        WidgetSnapMatch Match,
        int Priority,
        int PerpendicularGap,
        int PerpendicularCenterDistance);
}
