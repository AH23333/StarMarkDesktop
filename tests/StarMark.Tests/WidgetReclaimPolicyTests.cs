#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 隐藏组件的窗口回收（批次 ST，P-133）。
/// <para>
/// 这笔账是 SS 那批量出来的：<b>12 颗组件窗＝80 MB 私有字节 / 771 个句柄</b>，而托管堆只有 5–6 MB，
/// 所以省不下这笔钱的原因不是"缓存没清"，而是<b>隐藏只 <c>SW_HIDE</c>，窗口整块留着</b>。
/// 要收就得真的 <c>Close()</c>，而那件事的代价直接落在用户手上——重新显示要重建窗口（实测每颗 33–52 ms）。
/// 于是这批的判据不是一句"隐藏的可以收"，而是<b>两个方向都要钉住</b>：
/// 时间（持续隐藏满 5 分钟，用户裁决 2026-10-01）与身份（哪一类有只在这颗窗口里的东西）。
/// </para>
/// <para>
/// 第二类判据里最贵的是 <see cref="WidgetReclaimVerdict.NotAudited"/>：还没逐颗读过实现的那些<b>保守不收</b>。
/// 这不是偷懒——"收了把用户的字弄丢"一旦发生就不可挽回，而少收几颗只是少省几 MB。
/// 升级要带证据（读过那颗组件）与用例，所以这里同时钉住"哪些已经核实过"。
/// </para>
/// </summary>
public sealed class WidgetReclaimPolicyTests
{
    /// <summary>宽限期是**手感答案**不是实现细节：改成 1 分钟就是"藏一下马上调出来也要等重建"，那是改产品。</summary>
    [Fact]
    public void TheGraceIsTheFiveMinutesTheUserChose()
        => Assert.Equal(TimeSpan.FromMinutes(5), WidgetReclaimPolicy.HiddenGrace);

    /// <summary>每一类都必须被点名表态——包括以后新增的（那条"逐个数过枚举名字"的闸门才是这件事的证人）。</summary>
    [Fact]
    public void EveryKindGivesAVerdictAndSaysWhy()
    {
        foreach (var kind in Enum.GetValues<WidgetKind>())
        {
            var verdict = WidgetReclaimPolicy.Verdict(kind);
            Assert.True(Enum.IsDefined(typeof(WidgetReclaimVerdict), verdict), $"{kind} 给出的表态不在枚举里");
            Assert.False(string.IsNullOrWhiteSpace(WidgetReclaimPolicy.Reason(kind, TimeSpan.FromMinutes(7))),
                $"{kind} 没说出为什么，界面上就只剩『点了清理但内存没降』");
        }
    }

    /// <summary>已经核实"没有独有输入"的那八类——这份名单改动了就是要动用户能看到的行为。</summary>
    [Fact]
    public void TheseKindsWereAuditedAsReclaimable()
    {
        var reclaimable = Enum.GetValues<WidgetKind>()
            .Where(k => WidgetReclaimPolicy.Verdict(k) == WidgetReclaimVerdict.Reclaimable)
            .ToList();
        Assert.Equal(new[]
        {
            WidgetKind.Clock, WidgetKind.TagGrid, WidgetKind.Clipboard, WidgetKind.Activity,
            WidgetKind.Pinned, WidgetKind.Glance, WidgetKind.WorldClock, WidgetKind.SystemMonitor,
        }, reclaimable);
    }

    /// <summary>有草稿／有会话／没核实过的，一条都不许混进"可收"（反向那支：漏写就等于丢用户的字）。</summary>
    [Fact]
    public void NothingWithInFlightContentIsEverReclaimable()
    {
        foreach (var kind in new[]
                 {
                     WidgetKind.QuickNote, WidgetKind.Todo, WidgetKind.Search, WidgetKind.Calc,
                     WidgetKind.Focus, WidgetKind.Weather, WidgetKind.QuickLaunch, WidgetKind.Music,
                     WidgetKind.Countdown,
                 })
            Assert.NotEqual(WidgetReclaimVerdict.Reclaimable, WidgetReclaimPolicy.Verdict(kind));
    }

    /// <summary>边界：4 分 59 秒不收、正好 5 分钟收（"已经给满一次机会"），差一秒都还差着。</summary>
    [Theory]
    [InlineData(0, 0, false)]        // 刚隐藏
    [InlineData(4, 59, false)]       // 差一分钟
    [InlineData(5, 0, true)]         // 正好给满
    [InlineData(5, 1, true)]
    [InlineData(60, 0, true)]
    public void TimeIsJudgedAgainstTheGrace(int minutes, int seconds, bool want)
        => Assert.Equal(want, WidgetReclaimPolicy.ShouldReclaim(
            WidgetKind.Clock, TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds)));

    /// <summary>
    /// 时间不能是唯一条件（<b>只看时间</b>的实现会让番茄钟跑到一半也被收掉，而它照样通过上一条）：
    /// 隐藏一百年也不许收有在途内容的类。
    /// </summary>
    [Theory]
    [InlineData(WidgetKind.Focus)]
    [InlineData(WidgetKind.QuickNote)]
    [InlineData(WidgetKind.Search)]
    [InlineData(WidgetKind.Calc)]
    [InlineData(WidgetKind.Todo)]
    [InlineData(WidgetKind.Weather)]
    [InlineData(WidgetKind.QuickLaunch)]
    [InlineData(WidgetKind.Music)]
    [InlineData(WidgetKind.Countdown)]
    public void ALongHideDoesNotOverruleTheVerdict(WidgetKind kind)
        => Assert.False(WidgetReclaimPolicy.ShouldReclaim(kind, TimeSpan.FromDays(365)));

    /// <summary>类别对了也不能越过时间（<b>只看类别</b>的实现会让"藏一下就调出来"每次付一次重建）。</summary>
    [Fact]
    public void AReclaimableKindStillWaitsOutTheGrace()
        => Assert.False(WidgetReclaimPolicy.ShouldReclaim(WidgetKind.Clock, TimeSpan.FromMinutes(4)));

    /// <summary>
    /// 时钟回拨／未来时刻给出的<b>负时长</b>不收，也<b>不夹成端点</b>。
    /// <para>把 -3 秒夹成 0 或直接判"满了"，等于在时钟异常时挑最坏的那一支：窗口被收而用户不知道。</para>
    /// </summary>
    [Fact]
    public void ANegativeDurationIsNotClampedIntoAReclaim()
    {
        Assert.False(WidgetReclaimPolicy.ShouldReclaim(WidgetKind.Clock, TimeSpan.FromSeconds(-3)));
        Assert.False(WidgetReclaimPolicy.ShouldReclaim(WidgetKind.Clock, TimeSpan.MinValue));
    }

    /// <summary>没收时说的那句话要能自证：还在宽限期内就报"已隐藏多久 / 满多久才收"。</summary>
    [Fact]
    public void TheWaitingReasonCarriesBothDurations()
    {
        var reason = WidgetReclaimPolicy.Reason(WidgetKind.Clock, TimeSpan.FromMinutes(2));
        Assert.Contains("宽限期", reason, StringComparison.Ordinal);
        Assert.Contains("2 分 0 秒", reason, StringComparison.Ordinal);
        Assert.Contains("5 分 0 秒", reason, StringComparison.Ordinal);
    }

    /// <summary>"没核实"与"核实过不能收"必须是两句不同的话——把它们混成一句，下一次就没人知道该去核实哪颗。</summary>
    [Fact]
    public void UnauditedAndHoldsDraftAreDifferentSentences()
    {
        Assert.Contains("没", WidgetReclaimPolicy.Reason(WidgetKind.QuickLaunch, TimeSpan.FromMinutes(9)),
            StringComparison.Ordinal);
        Assert.Contains("输入", WidgetReclaimPolicy.Reason(WidgetKind.QuickNote, TimeSpan.FromMinutes(9)),
            StringComparison.Ordinal);
        Assert.DoesNotContain("输入", WidgetReclaimPolicy.Reason(WidgetKind.QuickLaunch, TimeSpan.FromMinutes(9)),
            StringComparison.Ordinal);
    }

    /// <summary>时长写法：不满一分钟只报秒（"0 分 8 秒"读起来像笔误），过了分钟就带秒。</summary>
    [Theory]
    [InlineData(0, 0, "0 秒")]
    [InlineData(0, 8, "8 秒")]
    [InlineData(0, 59, "59 秒")]
    [InlineData(1, 0, "1 分 0 秒")]
    [InlineData(5, 0, "5 分 0 秒")]
    [InlineData(12, 34, "12 分 34 秒")]
    public void DurationReadsTheWayAHumanSaysIt(int minutes, int seconds, string want)
        => Assert.Equal(want, WidgetReclaimPolicy.Format(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds)));
}

/// <summary>
/// 接线与形状闸门（批次 ST）。
/// <para>判据再对，宿主没接也一样省不下一个字节——而且<b>全绿</b>。
/// 这一族坑（#196/#195）已经记过两次：接线断言必须钉在<b>方法体</b>里，整文件 Contains 会被同文件另一处合法用法顶住。</para>
/// </summary>
public sealed class WidgetReclaimWiringGateTests
{
    private const string Manager = "src/StarMark.UI/Services/WidgetManager.Lifecycle.cs";
    private const string Window = "src/StarMark.UI/Views/WidgetWindow.xaml.cs";

    /// <summary>
    /// 取一个方法的<b>代码</b>（注释已抹掉）。
    /// <para>台架 ST6／ST8 亲自撞出来的：把 <c>ArmHiddenWindowReclaim();</c> 整行注释掉，
    /// 按原文数的 <c>Contains</c> 照样绿——接线断言读原文等于没读（#195／#203 同一族，规则 14）。
    /// 这一批的闸门全部走这里，不再有一处按原文数。</para>
    /// </summary>
    private static string Body(string file, string signatureFragment)
        => SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(file), signatureFragment));

    /// <summary>隐藏整群时必须排上回收这一趟；漏掉这一句＝判据永远没人调用，而测试全绿。</summary>
    [Fact]
    public void HidingABatchArmsTheReclaim()
    {
        var body = Body(Manager, "private void HideTemporaryAll");
        Assert.Contains("ArmHiddenWindowReclaim();", body, StringComparison.Ordinal);
        // 顺序也要钉：先落几何再排回收，反过来等于给一次还没写完盘的窗口去 Close
        Assert.True(body.IndexOf("PersistBoundsFor", StringComparison.Ordinal)
                    < body.IndexOf("ArmHiddenWindowReclaim();", StringComparison.Ordinal),
            "回收排在几何落盘之前——收了窗口就没机会把摆位写进去了");
    }

    /// <summary>定时器必须挂在 UI 队列上：内存门禁那张表跑在线程池，Close 窗口在那儿是当场炸。</summary>
    [Fact]
    public void TheReclaimTimerLivesOnTheUiQueueAndFiresOnce()
    {
        var body = Body(Manager, "private DispatcherQueueTimer BuildReclaimTimer");
        Assert.Contains("Ui().CreateTimer()", body, StringComparison.Ordinal);
        Assert.Contains("IsRepeating = false", body, StringComparison.Ordinal);
    }

    /// <summary>宽限期是"从这一刻重新算满"，不是"到点再看"——每次新隐藏都要重排一次。</summary>
    [Fact]
    public void EachFreshHideRestartsTheWholeGrace()
    {
        var body = Body(Manager, "private void ArmHiddenWindowReclaim");
        Assert.Contains("timer.Stop();", body, StringComparison.Ordinal);
        Assert.Contains("timer.Interval = WidgetReclaimPolicy.HiddenGrace;", body, StringComparison.Ordinal);
        Assert.Contains("timer.Start();", body, StringComparison.Ordinal);
    }

    /// <summary>回收路径不许再写一趟整档：几何在隐藏那一刻已经整批落过盘（AU 收掉过的坑，别再引回来）。</summary>
    [Fact]
    public void ReclaimingClosesWithoutAnotherWholeFileWrite()
    {
        var body = Body(Manager, "private void ReclaimHiddenWindows");
        Assert.Contains("CloseInternal(id, persist: false)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseInternal(id, persist: true)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PersistBoundsFor", body, StringComparison.Ordinal);
    }

    /// <summary>哪一类能收只有一个出处：宿主里不许出现"自己判时间/自己判类别"的第二份。</summary>
    [Fact]
    public void TheVerdictIsAskedInExactlyOnePlace()
    {
        var callSites = SourceGate.ReadRepoUnder("src")
            .Where(file => !file.RelativePath.Replace('\\', '/').EndsWith("WidgetReclaimPolicy.cs", StringComparison.Ordinal))
            .Where(file => SourceGate.Code(file.Text).Contains("WidgetReclaimPolicy.", StringComparison.Ordinal))
            .Select(file => file.RelativePath.Replace('\\', '/'))
            .ToList();
        Assert.Equal(new[] { Manager }, callSites);
    }

    /// <summary>
    /// 每一类组件都必须在这张表里<b>被点名</b>。
    /// <para>编译器管不了这件事：C# 的 enum switch 永远不算穷尽（可以 cast 出一个没名字的值），
    /// 消掉 CS8524 只能加 <c>default</c> 臂，而 <c>default</c> 正是"新类型悄悄落到某一档"的那条缝。
    /// 所以这里逐个数枚举里的名字在不在方法体里——加一种组件而不表态，这条就红。</para>
    /// </summary>
    [Fact]
    public void TheVerdictTableNamesEveryKind()
    {
        var body = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.Core/Widgets/WidgetReclaimPolicy.cs"),
            "public static WidgetReclaimVerdict Verdict(WidgetKind kind)");
        var missing = Enum.GetValues<WidgetKind>()
            .Where(kind => !body.Contains($"WidgetKind.{kind} =>", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(missing);
    }

    /// <summary>隐藏时刻由隐藏那一笔写、点亮那一笔清——两边漏一边，宽限期就会带着上一轮的年龄。</summary>
    [Fact]
    public void HideRecordsTheMomentAndRevealClearsIt()
    {
        Assert.Contains("HiddenSince = DateTimeOffset.UtcNow;",
            Body(Window, "public void HideTemporary()"), StringComparison.Ordinal);
        Assert.Contains("HiddenSince = null;", Body(Window, "public void Reveal()"), StringComparison.Ordinal);
    }

    /// <summary>窗口没在藏时 <c>HiddenFor()</c> 给 0 而不是 null：宿主那一圈不该各写一套兜底（写漏一个就是提前收）。</summary>
    [Fact]
    public void HiddenForAnswersZeroRatherThanNothing()
    {
        var body = Body(Window, "public TimeSpan HiddenFor()");
        Assert.Contains("TimeSpan.Zero", body, StringComparison.Ordinal);
        // 返回类型不许改成 TimeSpan?：那等于把"要不要兜底"这个决定推给每个调用点
        var source = SourceGate.ReadRepoFile(Window);
        Assert.Contains("public TimeSpan HiddenFor()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public TimeSpan? HiddenFor()", source, StringComparison.Ordinal);
    }

    /// <summary>字段住在主文件（这个类的既有约定：partial 的分段文件不各自持状态）。</summary>
    [Fact]
    public void TheReclaimTimerFieldLivesInTheMainPartial()
    {
        Assert.Equal(1, SourceGate.Count(SourceGate.ReadRepoPartials("src/StarMark.UI/Services/WidgetManager.cs"),
            "DispatcherQueueTimer? _reclaimTimer"));
    }
}
