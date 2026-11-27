#nullable enable
using System;
using System.Collections.Generic;

namespace StarMark.Core.Widgets;

/// <summary>
/// 一颗可能盖住整块屏的候选窗快照（片281）。UI 侧枚举完顶层窗口后打包成它，
/// 判据在这里做成能逐条断言的纯函数——屏 key 用 <see cref="long"/>（hMonitor.ToInt64()），
/// <see cref="IntPtr"/> 不进 Core。
/// </summary>
public sealed record OccluderCandidate(
    long MonitorKey,
    bool IsZoomed,
    string ClassName,
    int Left,
    int Top,
    int Right,
    int Bottom);

/// <summary>一块屏的几何（物理像素）：整屏矩形与工作区矩形。</summary>
public sealed record MonitorGeometry(
    long Key,
    int MonitorLeft,
    int MonitorTop,
    int MonitorRight,
    int MonitorBottom,
    int WorkLeft,
    int WorkTop,
    int WorkRight,
    int WorkBottom)
{
    /// <summary>整屏面积（物理像素²）。零/负边（异常几何）按 0 算，不让判据拿负数开方。</summary>
    public long MonitorArea =>
        Math.Max(0L, MonitorRight - MonitorLeft) *
        Math.Max(0L, MonitorBottom - MonitorTop);

    /// <summary>工作区面积（物理像素²）。</summary>
    public long WorkArea =>
        Math.Max(0L, WorkRight - WorkLeft) *
        Math.Max(0L, WorkBottom - WorkTop);
}

/// <summary>
/// 整屏遮挡的判据（片281）：DeskBox <c>MonitorOcclusionTracker</c> 的评估段照搬成纯函数——
/// 输入是「屏的几何」与「候选遮挡窗的快照」，输出是「被整屏盖住的屏 key 集合」。
/// 枚举窗口、读 DWM 属性那些碰操作系统的活全在 UI 侧（<c>DesktopOcclusionInterop</c>），
/// 这里只做能逐条断言的算术与名单。
/// <para>
/// 为什么要它：挂桌面图标层的组件窗会被任何最大化的应用窗整块盖住，而窗口照旧全价驻留
/// （隐藏不释放＝80 MB 那笔账的来源，见 <c>WidgetReclaimPolicy</c> 的开场白）。
/// "看不见满一段宽限就收、遮挡结束再复原"由 <c>WidgetManager</c> 的巡查与轮询接线，
/// 这里只回答"此刻哪些屏被盖住了"。
/// </para>
/// <para>
/// 与 DeskBox 的有意分野：①它感知到遮挡只停 ticker 不关窗（它没有那 80 MB 的问题），
/// 我们只有关窗一条腿，所以判据之外还必须有复原（接线在 <c>WidgetManager.Occlusion</c>）；
/// ②z-order 模拟段不搬——StarMark 组件窗永远在应用窗之下，"自家窗护住屏"的推断在这里不成立。
/// </para>
/// </summary>
public static class DesktopOcclusionPolicy
{
    /// <summary>
    /// 轮询粒度（片281 口径：P-302 之后不新增原生事件钩，2 秒常转轮询）。
    /// <para>宽限是分钟量级的（隐藏那条腿 5 分钟），2 秒的感知粒度在账上为零；
    /// 复原延迟上限＝一个轮询周期（2 秒），用户"撤掉最大化窗"到"组件回来"之间感受不到缝。</para>
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>工作区/整屏覆盖率门槛（DeskBox 同款 0.98：留 2% 给阴影与出屏误差）。</summary>
    public const double WorkAreaCoverageThreshold = 0.98;

    /// <summary>
    /// 评估此刻哪些屏被整屏盖住。
    /// <para><paramref name="foregroundIsOwnOrDesktopShell"/> 为真时直接空集——用户正看着自家窗
    /// 或桌面本体，谈不上"桌面被盖住"（UI 侧拿到这个结论时连枚举都可以省掉）。</para>
    /// <para><paramref name="exclusiveFullscreenActive"/>（独占全屏/演示模式）时前台所在那块屏
    /// 直接判被盖：全屏游戏不进 EnumWindows 的常规候选也能被感知到。</para>
    /// </summary>
    public static IReadOnlySet<long> EvaluateOccludedMonitors(
        IReadOnlyList<MonitorGeometry> monitors,
        IReadOnlyList<OccluderCandidate> candidates,
        bool foregroundIsOwnOrDesktopShell,
        bool exclusiveFullscreenActive,
        long foregroundMonitorKey)
    {
        var occluded = new HashSet<long>();
        if (foregroundIsOwnOrDesktopShell) return occluded;
        if (exclusiveFullscreenActive && foregroundMonitorKey != 0)
            occluded.Add(foregroundMonitorKey);
        var byKey = new Dictionary<long, MonitorGeometry>();
        foreach (var m in monitors) byKey[m.Key] = m;
        foreach (var c in candidates)
        {
            if (c.MonitorKey == 0 || occluded.Contains(c.MonitorKey)) continue;
            if (IsExcludedShellClass(c.ClassName)) continue;
            if (!byKey.TryGetValue(c.MonitorKey, out var geometry)) continue;
            if (DoesOccludeMonitor(c, geometry)) occluded.Add(c.MonitorKey);
        }
        return occluded;
    }

    /// <summary>
    /// 一颗候选窗盖没盖住这块屏（DeskBox <c>DoesCandidateOccludeMonitor</c> 三规则照搬）：
    /// <para>①最大化的窗与屏的交集 ≥ 屏面积一半——最大化的窗按定义铺满工作区，这条最快；</para>
    /// <para>②与工作区的交集 ≥ 工作区 98%——无边框"伪全屏"应用（浏览器 F11）走这条；</para>
    /// <para>③与整屏的交集 ≥ 整屏 98%——连任务栏一起盖掉的那种走这条。</para>
    /// <para>异常几何（面积为 0）时最大化候选直接判盖住：那是"读不到尺寸"的最保守处理，
    /// 漏判一次是用户白等一轮宽限，误判一次只是宽限从满开始计。</para>
    /// </summary>
    public static bool DoesOccludeMonitor(OccluderCandidate candidate, MonitorGeometry bounds)
    {
        var monitorArea = bounds.MonitorArea;
        if (candidate.IsZoomed)
        {
            if (monitorArea <= 0) return true;
            var zoomedIntersection = ComputeIntersectionArea(
                candidate.Left, candidate.Top, candidate.Right, candidate.Bottom,
                bounds.MonitorLeft, bounds.MonitorTop, bounds.MonitorRight, bounds.MonitorBottom);
            if (zoomedIntersection * 2 >= monitorArea) return true;
        }
        var workArea = bounds.WorkArea;
        if (workArea > 0)
        {
            var workIntersection = ComputeIntersectionArea(
                candidate.Left, candidate.Top, candidate.Right, candidate.Bottom,
                bounds.WorkLeft, bounds.WorkTop, bounds.WorkRight, bounds.WorkBottom);
            if (workIntersection >= (long)Math.Ceiling(workArea * WorkAreaCoverageThreshold)) return true;
        }
        if (monitorArea > 0)
        {
            var monitorIntersection = ComputeIntersectionArea(
                candidate.Left, candidate.Top, candidate.Right, candidate.Bottom,
                bounds.MonitorLeft, bounds.MonitorTop, bounds.MonitorRight, bounds.MonitorBottom);
            if (monitorIntersection >= (long)Math.Ceiling(monitorArea * WorkAreaCoverageThreshold)) return true;
        }
        return false;
    }

    /// <summary>前台判定用的"桌面壳"类名：桌面本体（Progman/WorkerW）、菜单（#32768）、
    /// 任务栏托管的 XAML 岛。前台是它们＝用户正看着桌面，组件不可能被盖。</summary>
    public static bool IsDesktopShellClass(string? className) =>
        className is not null &&
        (className == "Progman" || className == "WorkerW"
         || className == "#32768" || className == "XamlExplorerHostIslandWindow");

    /// <summary>候选排除用的壳类名超集：桌面壳之外再排除任务栏两代、托盘溢出窗与系统 CoreWindow
    /// ——它们常驻"铺满一条屏幕边"，不排除的话任务栏本身就能把每块屏判成被盖。</summary>
    public static bool IsExcludedShellClass(string? className) =>
        IsDesktopShellClass(className)
        || className == "Shell_TrayWnd"
        || className == "Shell_SecondaryTrayWnd"
        || className == "NotifyIconOverflowWindow"
        || className == "Windows.UI.Core.CoreWindow";

    /// <summary>两个矩形的交集面积；不相交给 0。全 long 中转，4K 屏对角相乘也不溢出。</summary>
    private static long ComputeIntersectionArea(
        int leftA, int topA, int rightA, int bottomA,
        int leftB, int topB, int rightB, int bottomB)
    {
        var left = Math.Max(leftA, leftB);
        var top = Math.Max(topA, topB);
        var right = Math.Min(rightA, rightB);
        var bottom = Math.Min(bottomA, bottomB);
        if (right <= left || bottom <= top) return 0;
        return (long)(right - left) * (bottom - top);
    }
}
