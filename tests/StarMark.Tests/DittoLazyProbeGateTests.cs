#nullable enable
using Xunit;
using StarMark.Integrations.Ditto;

namespace StarMark.Tests;

/// <summary>
/// 批次 SU（P-135 第一条）：Ditto 源的"找库"必须<b>推到第一次真要答案时</b>，而且<b>一辈子只探一次</b>。
/// <para>
/// 为什么要为"少探一次"写一整档闸门：候选路径里含"枚举进程＋读别的进程的 MainModule"，
/// 而 DI 在主窗构造那一刻就把五颗源全建出来（`App.xaml.cs` → `MainWindow` → `MainViewModel` → `SearchService`）。
/// 这台机器上 Ditto <b>没装</b>（库不存在、进程也没有），于是那笔钱每次启动都付、且永远为空——
/// 正是他这次要的"未启用的拓展功能不要加载"。见报告 §二百一十一。
/// </para>
/// </summary>
public sealed class DittoLazyProbeGateTests
{
    private const string File = "src/StarMark.Integrations/Ditto/DittoSource.cs";

    /// <summary>抹掉注释后的方法体：接线断言一律读这个，读原文会把注释里的名字当成调用（坑表 #217）。</summary>
    private static string Body(string signature) =>
        SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(File), signature));

    // ───────── 行为：什么时候探、探几次 ─────────

    /// <summary>构造一颗源<b>不应该问系统任何东西</b>——这条是整个批次的目的，也是唯一能机检的形式。</summary>
    [Fact]
    public void ConstructingTheSourceAsksNothingOfTheSystem()
    {
        var source = new DittoSource();

        Assert.Equal(0, source.ProbeCount);
    }

    /// <summary>第一次有人问可用性时才探，而且就探一次。</summary>
    [Fact]
    public void TheFirstQuestionIsWhereItProbes()
    {
        var source = new DittoSource();

        _ = source.IsAvailable;

        Assert.Equal(1, source.ProbeCount);
    }

    /// <summary>
    /// 反复问不许反复探。<b>这条不是洁癖</b>：<c>IsAvailable</c> 在一次搜索里会被问两遍
    /// （Core 侧筛源 <c>SearchService.cs:78</c> ＋ 队列侧查询前 <c>EverythingQueryQueue.cs:161</c> 的同族写法），
    /// 每次重探＝把刚省下的税按搜索次数交回去。
    /// </summary>
    [Fact]
    public void AskingAgainNeverProbesTwice()
    {
        var source = new DittoSource();

        for (var i = 0; i < 3; i++) _ = source.IsAvailable;
        var hint = source.AvailabilityHint;

        Assert.Equal(1, source.ProbeCount);
        Assert.Contains("Ditto", hint, StringComparison.Ordinal);
    }

    /// <summary>显式给了路径（测试/自定义安装位置）＝<b>不探</b>，也不许被"懒"顺手改成去枚举进程。</summary>
    [Fact]
    public void AnExplicitDbPathNeedsNoProbeAtAll()
    {
        var source = new DittoSource(Path.Combine(Path.GetTempPath(), "这台机器上不存在的DittoDB.db"));

        Assert.False(source.IsAvailable);
        _ = source.AvailabilityHint;
        Assert.Equal(0, source.ProbeCount);
    }

    // ───────── 接线与形状：防止以后被"顺手改回去" ─────────

    /// <summary>构造体里不许再出现任何探测调用——这是 P-135 这条优化唯一会被破坏的方式。</summary>
    [Fact]
    public void TheConstructorNamesNoProbeCall()
    {
        var body = Body("public DittoSource(string? dbPath = null)");

        Assert.DoesNotContain("CandidateDbPaths(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("FindDbPath(", body, StringComparison.Ordinal);
    }

    /// <summary>探测只有<b>一个出处</b>：<c>EnsureProbed</c>，而且两条都要在里面（少一条就变成"提示说找过 0 处"）。</summary>
    [Fact]
    public void TheProbeHasExactlyOneHome()
    {
        var body = Body("private void EnsureProbed()");

        Assert.Contains("CandidateDbPaths();", body, StringComparison.Ordinal);
        Assert.Contains("FindDbPath(_probed)", body, StringComparison.Ordinal);
        Assert.Contains("ProbeCount++;", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 两个出口（<c>IsAvailable</c>／<c>AvailabilityHint</c>）都必须先过闸门。
    /// <b>方向反过来也要钉</b>：整档里"调用闸门"的次数只许是 2——将来有人加第三个问口却忘了过闸门，
    /// 就会在那一处偷偷重探（一次红比一次"全绿但其实漏了一处"便宜得多）。
    /// </summary>
    [Fact]
    public void BothEntryPointsAskTheGateAndThereAreNoOthers()
    {
        Assert.Contains("EnsureProbed();", Body("public bool IsAvailable"), StringComparison.Ordinal);
        Assert.Contains("EnsureProbed();", Body("public string? AvailabilityHint"), StringComparison.Ordinal);

        // 调用闸门的地方全档只许两处（定义那行是 "EnsureProbed()"，不带分号，不会被数进来）。
        var whole = SourceGate.Code(SourceGate.ReadRepoFile(File));
        Assert.Equal(2, SourceGate.Count(whole, "EnsureProbed();"));
    }

    /// <summary>探测过的位置清单必须还是那句话的来源——"找过哪里"是本组件的既有承诺（批次 MH）。</summary>
    [Fact]
    public void TheHintStillNamesEveryProbedPath()
    {
        Assert.Contains("string.Join(\"；\", _probed)", Body("public string? AvailabilityHint"), StringComparison.Ordinal);
    }

    /// <summary>探过一次就把旗子立起来：靠旗子而不是靠"_dbPath 非空"判断，因为<b>探不到时 _dbPath 也可能是候选首项</b>。</summary>
    [Fact]
    public void TheGateUsesItsOwnFlagNotTheDbPath()
    {
        var body = Body("private void EnsureProbed()");

        Assert.Contains("if (_probeDone) return;", body, StringComparison.Ordinal);
    }
}
