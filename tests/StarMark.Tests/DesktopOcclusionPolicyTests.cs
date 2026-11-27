#nullable enable
using System;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 整屏遮挡的判据（片281）：DeskBox <c>MonitorOcclusionTracker</c> 的评估段搬进 Core 后的逐条断言。
/// <para>
/// 为什么要它：挂桌面图标层的组件窗会被任何最大化的应用窗整块盖住，而窗口照旧全价驻留
/// （隐藏不释放＝80 MB 那笔账）。"看不见满宽限就收、遮挡结束再复原"的判据必须先在这里钉死，
/// 接线（<c>DesktopOcclusionWiringGateTests</c>）才有的守。
/// </para>
/// <para>坐标全用物理像素。标准样板屏：1920×1080，任务栏 40px（工作区 1920×1040）。</para>
/// </summary>
public sealed class DesktopOcclusionPolicyTests
{
    private static readonly TimeSpan TwoSeconds = TimeSpan.FromSeconds(2);

    /// <summary>样板屏：主屏 1920×1080、工作区 1920×1040，key=11。</summary>
    private static MonitorGeometry Primary()
        => new(11, 0, 0, 1920, 1080, 0, 0, 1920, 1040);

    /// <summary>副屏：与主屏并排，key=22。</summary>
    private static MonitorGeometry Secondary()
        => new(22, 1920, 0, 3840, 1080, 1920, 0, 3840, 1040);

    /// <summary>一颗普通候选窗（默认：铺满主屏工作区的外来应用窗）。</summary>
    private static OccluderCandidate Candidate(
        long key = 11, bool zoomed = false, string cls = "Chrome_WidgetWin_1",
        int left = 0, int top = 0, int right = 1920, int bottom = 1040)
        => new(key, zoomed, cls, left, top, right, bottom);

    /// <summary>轮询是"常转 2 秒"的口径：P-302 之后不新增原生钩，感知粒度就是它。</summary>
    [Fact]
    public void ThePollIntervalIsTwoSeconds()
        => Assert.Equal(TwoSeconds, DesktopOcclusionPolicy.PollInterval);

    /// <summary>覆盖率门槛与 DeskBox 同款 0.98（留 2% 给阴影与出屏误差），改它就是改判据本身。</summary>
    [Fact]
    public void TheCoverageThresholdMatchesDeskBox()
        => Assert.Equal(0.98, DesktopOcclusionPolicy.WorkAreaCoverageThreshold, 12);

    /// <summary>前台判定用的"桌面壳"类名——认错一个就是把用户看桌面的时刻判成被遮挡。</summary>
    [Theory]
    [InlineData("Progman", true)]                    // 桌面本体
    [InlineData("WorkerW", true)]                    // 桌面本体的另一张脸（Win+D 之后）
    [InlineData("#32768", true)]                     // 菜单/对话框类
    [InlineData("XamlExplorerHostIslandWindow", true)]   // 任务栏托管的 XAML 岛
    [InlineData("ApplicationFrameWindow", false)]    // UWP 应用窗：它真能盖住桌面
    [InlineData("Chrome_WidgetWin_1", false)]        // 普通应用窗
    public void DesktopShellClassesAreNamedExactly(string className, bool want)
        => Assert.Equal(want, DesktopOcclusionPolicy.IsDesktopShellClass(className));

    /// <summary>候选排除名单：任务栏两代/托盘溢出/系统 CoreWindow 常驻铺满屏幕一条边，不排除就块块屏都被它们"盖住"。</summary>
    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("NotifyIconOverflowWindow")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    public void ExcludedShellClassesNeverOcclude(string className)
        => Assert.True(DesktopOcclusionPolicy.IsExcludedShellClass(className));

    /// <summary>普通应用窗不在排除名单（它正是要被评估的那种）；空/无名也不算壳。</summary>
    [Theory]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("Notepad")]
    [InlineData("")]
    public void OrdinaryWindowsAreNotExcluded(string className)
        => Assert.False(DesktopOcclusionPolicy.IsExcludedShellClass(className));

    /// <summary>排除名单必须包含桌面壳全集（前台判定用的那四张脸在这里也不许盖屏）。</summary>
    [Fact]
    public void TheExcludedListIsASupersetOfTheDesktopShellList()
    {
        foreach (var name in new[] { "Progman", "WorkerW", "#32768", "XamlExplorerHostIslandWindow" })
            Assert.True(DesktopOcclusionPolicy.IsExcludedShellClass(name), $"{name} 必须同时在排除名单里");
    }

    // ───────────────── DoesOccludeMonitor：DeskBox 三规则 ─────────────────

    /// <summary>一颗窗盖没盖住这块屏（三规则逐条：最大化≥屏一半／工作区≥98%／整屏≥98%）。</summary>
    [Theory]
    [InlineData(true, 0, 0, 1920, 1080, true)]       // 最大化的窗铺满整屏
    [InlineData(true, 0, 0, 960, 1080, true)]        // 最大化只盖住一半：交集×2＝屏面积，"≥一半"成立
    [InlineData(true, 0, 0, 959, 1080, false)]       // 最大化盖不到一半：后两条也只盖一半，不成立
    [InlineData(false, 0, 0, 1920, 1040, true)]      // 无边框伪全屏（浏览器 F11）盖满工作区
    [InlineData(false, 0, 0, 1920, 1019, false)]     // 盖住工作区 97.98%：还差 0.02% 不到门槛
    [InlineData(false, 0, 0, 1920, 1020, true)]      // 盖住工作区 98.08%：过了门槛
    [InlineData(false, 0, 0, 800, 600, false)]       // 普通小窗
    [InlineData(false, -500, -500, 100, 100, false)] // 与屏不相交
    public void OcclusionFollowsTheThreeDeskBoxRules(bool zoomed, int l, int t, int r, int b, bool want)
        => Assert.Equal(want, DesktopOcclusionPolicy.DoesOccludeMonitor(
            Candidate(zoomed: zoomed, left: l, top: t, right: r, bottom: b), Primary()));

    /// <summary>异常几何（整屏面积 0）＋最大化候选：按"盖住"处理——漏判一次是白等一轮宽限，误判只是从满开始计。</summary>
    [Fact]
    public void AZoomedCandidateOnADegenerateMonitorCountsAsOccluded()
        => Assert.True(DesktopOcclusionPolicy.DoesOccludeMonitor(
            Candidate(zoomed: true), new MonitorGeometry(11, 0, 0, 0, 0, 0, 0, 0, 0)));

    /// <summary>工作区面积 0（任务栏吃满高的怪配置）时工作区规则让位，整屏规则接管。</summary>
    [Fact]
    public void AZeroWorkAreaFallsThroughToTheMonitorRule()
    {
        var weird = new MonitorGeometry(11, 0, 0, 1920, 1080, 0, 0, 1920, 0);
        Assert.True(DesktopOcclusionPolicy.DoesOccludeMonitor(
            Candidate(zoomed: false, left: 0, top: 0, right: 1920, bottom: 1080), weird));
        Assert.False(DesktopOcclusionPolicy.DoesOccludeMonitor(
            Candidate(zoomed: false, left: 0, top: 0, right: 800, bottom: 600), weird));
    }

    /// <summary>屏几何的两块面积：整屏与工作区（判据的分母，错一位整个门槛就歪）。</summary>
    [Fact]
    public void MonitorGeometryCarriesBothAreas()
    {
        var g = Primary();
        Assert.Equal(1920L * 1080, g.MonitorArea);
        Assert.Equal(1920L * 1040, g.WorkArea);
    }

    /// <summary>退化几何的面积按 0 算，不冒出负数去毒化判据。</summary>
    [Fact]
    public void DegenerateGeometryHasNoNegativeArea()
    {
        var g = new MonitorGeometry(0, 100, 100, 0, 0, 0, 0, 0, 0);
        Assert.Equal(0, g.MonitorArea);
        Assert.Equal(0, g.WorkArea);
    }

    // ───────────────── EvaluateOccludedMonitors：整屏口径 ─────────────────

    /// <summary>前台是自家窗/桌面壳＝用户正看着桌面：直接空集，候选再多也不看（DeskBox 同款短路）。</summary>
    [Fact]
    public void AnOwnOrShellForegroundShortCircuitsToNothing()
    {
        var occluded = DesktopOcclusionPolicy.EvaluateOccludedMonitors(
            new[] { Primary() }, new[] { Candidate() },
            foregroundIsOwnOrDesktopShell: true,
            exclusiveFullscreenActive: true, foregroundMonitorKey: 11);
        Assert.Empty(occluded);
    }

    /// <summary>独占全屏/演示模式：前台所在那块屏直接判被盖——全屏游戏不进常规候选也能被感知。</summary>
    [Fact]
    public void ExclusiveFullscreenMarksTheForegroundMonitor()
    {
        var occluded = DesktopOcclusionPolicy.EvaluateOccludedMonitors(
            new[] { Primary() }, Array.Empty<OccluderCandidate>(),
            foregroundIsOwnOrDesktopShell: false,
            exclusiveFullscreenActive: true, foregroundMonitorKey: 11);
        // ⛔ Assert.Equal(new HashSet<long>…, occluded)：那会命中引用相等的 Equal<T>(T,T) 重载
        // （IReadOnlySet<long> 是 HashSet<long> 的更好转换目标），单元素用 Single 最无歧义
        Assert.Equal(11L, Assert.Single(occluded));
    }

    /// <summary>独占全屏但探不到前台屏（key=0）：不猜屏，一块都不标。</summary>
    [Fact]
    public void ExclusiveFullscreenWithoutAForegroundMonitorMarksNothing()
        => Assert.Empty(DesktopOcclusionPolicy.EvaluateOccludedMonitors(
            new[] { Primary() }, Array.Empty<OccluderCandidate>(),
            foregroundIsOwnOrDesktopShell: false,
            exclusiveFullscreenActive: true, foregroundMonitorKey: 0));

    /// <summary>一颗最大化的外来窗把它的屏判成被盖。</summary>
    [Fact]
    public void AMaximizedForeignWindowOccludesItsMonitor()
        => Assert.Equal(11L, Assert.Single(DesktopOcclusionPolicy.EvaluateOccludedMonitors(
            new[] { Primary() }, new[] { Candidate(zoomed: true) },
            foregroundIsOwnOrDesktopShell: false, exclusiveFullscreenActive: false, foregroundMonitorKey: 11)));

    /// <summary>屏与屏互不连坐：主屏被盖不牵连副屏，副屏的小窗也盖不住自己。</summary>
    [Fact]
    public void MonitorsAreJudgedApart()
    {
        var occluded = DesktopOcclusionPolicy.EvaluateOccludedMonitors(
            new[] { Primary(), Secondary() },
            new[]
            {
                Candidate(zoomed: true),                                             // 主屏：盖住
                Candidate(key: 22, left: 1920, top: 0, right: 2600, bottom: 600),   // 副屏：小窗，盖不住
            },
            foregroundIsOwnOrDesktopShell: false, exclusiveFullscreenActive: false, foregroundMonitorKey: 11);
        Assert.Equal(11L, Assert.Single(occluded));
    }

    /// <summary>没有屏几何的候选（探不到所在屏）跳过，不让评估炸掉。</summary>
    [Fact]
    public void ACandidateWithoutMonitorGeometryIsIgnored()
        => Assert.Empty(DesktopOcclusionPolicy.EvaluateOccludedMonitors(
            new[] { Primary() }, new[] { Candidate(key: 33) },
            foregroundIsOwnOrDesktopShell: false, exclusiveFullscreenActive: false, foregroundMonitorKey: 0));
}
