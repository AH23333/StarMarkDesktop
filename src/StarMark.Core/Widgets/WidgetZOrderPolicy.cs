#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StarMark.Core.Widgets;

/// <summary>参与空闲期 Z 序排列的候选窗口。</summary>
/// <param name="WindowHandle">窗口句柄；0 表示无效，会被剔除。</param>
/// <param name="DisplayKey">所在显示器标识（跨屏排列时的首要分组键）。</param>
/// <param name="Top">窗口上边缘（物理像素）。</param>
/// <param name="Left">窗口左边缘（物理像素）。</param>
/// <param name="StableKey">稳定排序键，保证同位置时的确定性。</param>
public readonly record struct WidgetZOrderCandidate(
    long WindowHandle,
    string DisplayKey,
    double Top,
    double Left,
    string StableKey);

/// <summary>
/// 空闲期（无交互）组件 Z 序策略。
/// 移植自 DeskBox <c>Services/IdleWidgetZOrderPolicy.cs</c>：
/// <b>位置靠下的组件保持在上方</b>，这样上方组件的投影不会压暗下方组件的顶边。
/// 纯函数，便于单元测试。返回值第一个元素位于最上层。
/// </summary>
public static class WidgetZOrderPolicy
{
    public static IReadOnlyList<WidgetZOrderCandidate> OrderHighestToLowest(
        IEnumerable<WidgetZOrderCandidate> candidates)
    {
        return candidates
            .Where(candidate => candidate.WindowHandle != 0)
            .GroupBy(candidate => candidate.WindowHandle)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.DisplayKey, StringComparer.Ordinal)
            .ThenByDescending(candidate => candidate.Top)
            .ThenByDescending(candidate => candidate.Left)
            .ThenBy(candidate => candidate.StableKey, StringComparer.Ordinal)
            .ToList();
    }
}
