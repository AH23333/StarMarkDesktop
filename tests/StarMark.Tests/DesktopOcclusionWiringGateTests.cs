#nullable enable
using System;
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 整屏遮挡收窗的接线与形状闸门（片281）。
/// <para>判据再对，宿主没接也一样省不下一个字节——而且<b>全绿</b>（#196/#195 两次记过：
/// 接线断言必须钉在<b>方法体</b>里，整文件 Contains 会被同文件另一处合法用法顶住）。
/// 测试工程不引用 StarMark.UI，UI 结构只能这样守。</para>
/// <para>本族与 <see cref="WidgetReclaimPolicyTests"/> 那族分工：那边守"隐藏回收"的判据与接线，
/// 这边守"遮挡感知→标钟→收窗→复原"这一段新接线；两边的判据只许各有一处问询。</para>
/// </summary>
public sealed class DesktopOcclusionWiringGateTests
{
    private const string Lifecycle = "src/StarMark.UI/Services/WidgetManager.Lifecycle.cs";
    private const string Occlusion = "src/StarMark.UI/Services/WidgetManager.Occlusion.cs";
    private const string Window = "src/StarMark.UI/Views/WidgetWindow.xaml.cs";

    /// <summary>取一个方法的<b>代码</b>（注释已抹掉）——接线断言读原文等于没读（#195/#203 同族）。</summary>
    private static string Body(string file, string signatureFragment)
        => SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(file), signatureFragment));

    /// <summary>
    /// lambda 体方法的取体法（<c>public Task XxxAsync() => OnUiAsync(() => { … });</c>）：
    /// 这类方法的分号藏在嵌套闭包里，<see cref="SourceGate.MethodBody"/> 的表达式体规则
    /// 会在第一颗内层分号处截断——按既有约定（几何落盘族同款）用前后锚切整段。
    /// </summary>
    private static string Span(string file, string from, string to)
        => SourceGate.Code(SourceGate.Between(SourceGate.ReadRepoFile(file), from, to));

    /// <summary>遮挡轮询是常转 2 秒的 UI 队列表：粒度从政策取（不许在宿主写死第二份），
    /// 且必须可重复（一次性表感知不到"遮挡结束"，复原那条腿就断了）。</summary>
    [Fact]
    public void ThePollIsAConstantTwoSecondUiQueueTimer()
    {
        var body = Body(Occlusion, "private DispatcherQueueTimer BuildOcclusionTimer");
        Assert.Contains("Ui().CreateTimer()", body, StringComparison.Ordinal);
        Assert.Contains("IsRepeating = true", body, StringComparison.Ordinal);
        Assert.Contains("DesktopOcclusionPolicy.PollInterval", body, StringComparison.Ordinal);
    }

    /// <summary>状态住主 partial（这个类的既有约定：partial 的分段文件不各自持状态）。</summary>
    [Fact]
    public void TheOcclusionFieldsLiveInTheMainPartial()
    {
        var all = SourceGate.ReadRepoPartials("src/StarMark.UI/Services/WidgetManager.cs");
        Assert.Equal(1, SourceGate.Count(all, "DispatcherQueueTimer? _occlusionTimer"));
        Assert.Equal(1, SourceGate.Count(all, "Dictionary<string, long> _occludedReclaimed"));
    }

    /// <summary>轮询必须在首颗组件窗显示时起表：不接这一句，判据永远没人问，而测试全绿。</summary>
    [Fact]
    public void PollingIsEnsuredWheneverAWindowShows()
        => Assert.Contains("EnsureOcclusionPolling();",
            Body(Lifecycle, "private void ShowNow(string id, WidgetStoreData? pendingData"), StringComparison.Ordinal);

    /// <summary>巡查必须看得见"可见但被遮挡"的窗：漏了这一条，遮挡那条腿永远收不了窗。</summary>
    [Fact]
    public void ThePatrolAlsoVisitsOccludedVisibleWindows()
        => Assert.Contains("if (window.IsVisible && !window.IsOccluded) continue;",
            Body(Lifecycle, "private void ReclaimHiddenWindows"), StringComparison.Ordinal);

    /// <summary>遮挡钟的标记必须幂等：轮询每 2 秒都来置一次，重记时刻＝宽限永远满不了。
    /// 点亮时两钟都要清，否则下一次判宽限带着上一轮的年龄。</summary>
    [Fact]
    public void OcclusionMarksAreIdempotentAndClearedOnReveal()
    {
        Assert.Contains("OccludedSince ??= DateTimeOffset.UtcNow;",
            Body(Window, "public void SetOccluded(bool occluded, long monitorKey"), StringComparison.Ordinal);
        Assert.Contains("SetOccluded(false);",
            Body(Window, "public void Reveal()"), StringComparison.Ordinal);
    }

    /// <summary>复原必须分帧（一颗一拍，与启动恢复同款节奏），且只复原"那块屏已露出来"的。</summary>
    [Fact]
    public void RestoreIsSlicedAndWaitsForTheMonitorToClear()
    {
        var body = Body(Occlusion, "private async Task RestoreOccludedReclaimedAsync");
        Assert.Contains("ShowInternal(id)", body, StringComparison.Ordinal);
        Assert.Contains("WidgetStartupPolicy.RevealIntervalMs", body, StringComparison.Ordinal);
        Assert.Contains("occludedMonitors.Contains(key)", body, StringComparison.Ordinal);
    }

    /// <summary>用户的整群动作（隐藏全部/收起全部/退出）压过自动复原债——
    /// 留着册子的话，"全部隐藏"之后遮挡一结束又自动弹回几颗，与用户动作直接矛盾。</summary>
    [Fact]
    public void UserBulkActionsClearTheRestoreDebt()
    {
        Assert.Contains("_occludedReclaimed.Clear();",
            Body(Lifecycle, "private void HideTemporaryAll"), StringComparison.Ordinal);
        Assert.Contains("_occludedReclaimed.Clear();",
            Span(Lifecycle, "public Task CollapseAllAsync()", "private void ArmHiddenWindowReclaim"), StringComparison.Ordinal);
        Assert.Contains("_occludedReclaimed.Clear();",
            Span(Lifecycle, "public Task ShutdownAllAsync()", "private void ShowInternal"), StringComparison.Ordinal);
    }

    /// <summary>遮挡判据只有一个出处：src 里问 <c>DesktopOcclusionPolicy.</c> 的只许是遮挡段那一份。
    /// Lifecycle 走 <c>WidgetReclaimPolicy</c>（宽限）是另一颗判据的事，不在此列。</summary>
    [Fact]
    public void TheOcclusionVerdictIsAskedInExactlyOnePlace()
    {
        var callSites = SourceGate.ReadRepoUnder("src")
            .Where(file => !file.RelativePath.Replace('\\', '/').EndsWith("DesktopOcclusionPolicy.cs", StringComparison.Ordinal))
            .Where(file => SourceGate.Code(file.Text).Contains("DesktopOcclusionPolicy.", StringComparison.Ordinal))
            .Select(file => file.RelativePath.Replace('\\', '/'))
            .ToList();
        Assert.Equal(new[] { Occlusion }, callSites);
    }

    /// <summary>遮挡腿的巡查必须骑隐藏那张一次性表（已转就不再起）：两张表各等各的＝先到的白转一轮，
    /// 互不知会；Interval 也必须走同一段宽限，不许写第二份数字。</summary>
    [Fact]
    public void AnOcclusionGraceRidesTheHiddenReclaimTimer()
    {
        var body = Body(Lifecycle, "private void ArmOcclusionReclaim");
        Assert.Contains("if (timer.IsRunning) return;", body, StringComparison.Ordinal);
        Assert.Contains("WidgetReclaimPolicy.HiddenGrace", body, StringComparison.Ordinal);
    }

    /// <summary>遮挡评估的单轮必须整体兜错：常转的巡查被一颗异常窗打死，遮挡就永远"停在上一次"。</summary>
    [Fact]
    public void ThePollSurvivesASingleRoundsFailure()
    {
        var body = Body(Occlusion, "private void OnOcclusionPoll");
        Assert.Contains("catch", body, StringComparison.Ordinal);
        // 兜错的 catch 里必须说实话：静默吞掉的巡查失败没人修得起
        Assert.Contains("StarLog.Warn", body, StringComparison.Ordinal);
    }

    /// <summary>枚举候选时自家窗必须被排除：组件窗挂桌面图标层，永远盖不住屏；
    /// 不排除的话，一颗组件窗就能把自己所在屏"判成被自己盖住"。</summary>
    [Fact]
    public void OwnWindowsAreExcludedFromTheOccluders()
        => Assert.Contains("IsOwnProcessWindow(hwnd)",
            Body(Occlusion, "private static void CollectOcclusionCandidate"), StringComparison.Ordinal);
}
