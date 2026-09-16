#nullable enable
using Windows.Graphics;

namespace StarMark.Core.Widgets;

/// <summary>
/// 窗口边缘吸附计算（移植自 DeskBox WidgetSnapCalculator）。
/// 全部坐标为 Win32/AppWindow 物理像素，纯函数、无 UI 依赖，便于单元测试。
///
/// 吸附规则：
/// - 组件之间：边缘对齐（同一条边）或边缘贴合（中间留间距）；
/// - 显示器工作区：吸附到工作区四边；
/// - 垂直方向移动只比较横向边，横向移动只比较纵向边；
/// - 垂直/水平投影必须有重叠（或接近重叠）才吸附，避免远处窗口乱吸。
/// </summary>
public static class WidgetSnapping
{
    /// <summary>默认吸附阈值（物理像素）。</summary>
    public const int DefaultThreshold = 24;

    /// <summary>组件贴合时默认间距（物理像素）。</summary>
    public const int DefaultSpacing = 8;

    public sealed record SnapTarget(RectInt32 Bounds);

    private readonly record struct EdgeCandidate(
        int SourceEdge, int CandidateEdge, int PerpendicularGap,
        int Priority, bool IsAbutment)
    {
        public int Delta => Math.Abs(SourceEdge - CandidateEdge);
    }

    /// <summary>
    /// 计算移动后窗口应吸附到的位置；无可吸附目标时原样返回。
    /// </summary>
    /// <param name="proposed">当前移动中的窗口矩形（物理像素）。</param>
    /// <param name="targets">其他组件窗口矩形。</param>
    /// <param name="workArea">当前显示器工作区。</param>
    /// <param name="threshold">吸附阈值（物理像素）。</param>
    /// <param name="spacing">贴合间距（物理像素）。</param>
    public static RectInt32 SnapMove(
        RectInt32 proposed,
        IReadOnlyList<RectInt32> targets,
        RectInt32 workArea,
        int threshold = DefaultThreshold,
        int spacing = DefaultSpacing)
    {
        var snappedX = SnapAxis(
            proposed, targets, workArea,
            horizontal: true, threshold, spacing,
            static (r, edge) => r with { X = edge });
        var snappedY = SnapAxis(
            snappedX, targets, workArea,
            horizontal: false, threshold, spacing,
            static (r, edge) => r with { Y = edge });
        return snappedY;
    }

    private static RectInt32 SnapAxis(
        RectInt32 source,
        IReadOnlyList<RectInt32> targets,
        RectInt32 workArea,
        bool horizontal,
        int threshold,
        int spacing,
        Func<RectInt32, int, RectInt32> applyEdge)
    {
        EdgeCandidate? best = null;

        // 组件之间：四条边两两比较（对齐 + 贴合）
        foreach (var target in targets)
        {
            var perpendicularGap = horizontal
                ? PerpendicularGap(source.Y, source.Height, target.Y, target.Height)
                : PerpendicularGap(source.X, source.Width, target.X, target.Width);
            // 投影完全不搭边且离得较远时不参与吸附
            if (perpendicularGap > threshold) continue;

            if (horizontal)
            {
                var sourceRight = source.X + source.Width;
                var targetRight = target.X + target.Width;
                ConsiderEdge(source.X, target.X, perpendicularGap, 0, false, ref best);
                ConsiderEdge(source.X, targetRight + spacing, perpendicularGap, 1, true, ref best);
                ConsiderEdge(sourceRight, targetRight, perpendicularGap, 0, false, ref best);
                ConsiderEdge(sourceRight, target.X - spacing, perpendicularGap, 1, true, ref best);
            }
            else
            {
                var sourceBottom = source.Y + source.Height;
                var targetBottom = target.Y + target.Height;
                ConsiderEdge(source.Y, target.Y, perpendicularGap, 0, false, ref best);
                ConsiderEdge(source.Y, targetBottom + spacing, perpendicularGap, 1, true, ref best);
                ConsiderEdge(sourceBottom, targetBottom, perpendicularGap, 0, false, ref best);
                ConsiderEdge(sourceBottom, target.Y - spacing, perpendicularGap, 1, true, ref best);
            }
        }

        // 显示器工作区四边（视为优先级最高的对齐边）
        if (horizontal)
        {
            var sourceRight = source.X + source.Width;
            var workAreaRight = workArea.X + workArea.Width;
            ConsiderEdge(source.X, workArea.X + spacing, 0, -10, false, ref best);
            ConsiderEdge(sourceRight, workAreaRight - spacing, 0, -10, false, ref best);
        }
        else
        {
            var sourceBottom = source.Y + source.Height;
            var workAreaBottom = workArea.Y + workArea.Height;
            ConsiderEdge(source.Y, workArea.Y + spacing, 0, -10, false, ref best);
            ConsiderEdge(sourceBottom, workAreaBottom - spacing, 0, -10, false, ref best);
        }

        if (best is not { } chosen || chosen.Delta > threshold) return source;
        return applyEdge(source, chosen.CandidateEdge);
    }

    private static void ConsiderEdge(
        int sourceEdge, int candidateEdge, int perpendicularGap,
        int priority, bool isAbutment, ref EdgeCandidate? best)
    {
        var candidate = new EdgeCandidate(sourceEdge, candidateEdge, perpendicularGap, priority, isAbutment);
        if (best is not { } current)
        {
            best = candidate;
            return;
        }

        var better =
            candidate.Delta < current.Delta ||
            (candidate.Delta == current.Delta && candidate.PerpendicularGap < current.PerpendicularGap) ||
            (candidate.Delta == current.Delta && candidate.PerpendicularGap == current.PerpendicularGap &&
             candidate.Priority < current.Priority) ||
            (candidate.Delta == current.Delta && candidate.PerpendicularGap == current.PerpendicularGap &&
             candidate.Priority == current.Priority && !candidate.IsAbutment && current.IsAbutment);
        if (better) best = candidate;
    }

    /// <summary>两个区间在垂直（或水平）方向上的距离：重叠为 0，否则为间隔像素。</summary>
    public static int PerpendicularGap(int aPos, int aSize, int bPos, int bSize)
    {
        var aEnd = aPos + aSize;
        var bEnd = bPos + bSize;
        if (aEnd <= bPos) return bPos - aEnd;
        if (bEnd <= aPos) return aPos - bEnd;
        return 0;
    }
}
