#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 卡顿恢复行里"其间 UI 线程自己用了约 Y ms CPU"这句的<b>口径</b>（批次 TB；P-141 的前置）。
/// <para>
/// 缺陷形状很安静：<c>wall</c> 的起点是"最后一次回执"（≈卡顿真起点），而 CPU 基线早先是
/// <b>判定为卡顿那一刻</b>才取的——也就是越过 1 s 阈值之后。于是恢复行的两个数量的是<b>不同的窗口</b>，
/// 相减凭空多出约一个阈值的"看起来在等"。我据此把"阻塞 2484 ms／CPU 843 ms"读成了"66% 在等"，
/// 并写进了报告与账本（TB 就地撤回）。
/// </para>
/// <para>
/// <c>StarMark.Tests</c> 不引用 <c>StarMark.UI</c>（#184）⇒ 这里只能钉接线形状：
/// 两个读数必须来自<b>同一个快照</b>、取样点必须在回执端而不是判定时、取不到仍要说"未知"而不是补一个 0。
/// </para>
/// </summary>
public sealed class UiStallWatchdogBaselineGateTests
{
    private const string Watchdog = "src/StarMark.UI/Helpers/UIStallWatchdog.cs";

    private static string Code() => SourceGate.Code(SourceGate.ReadRepoFile(Watchdog));

    private static string ProbeBody() => SourceGate.MethodBody(Code(), "static void Probe");

    private static string AckBody() => SourceGate.MethodBody(Code(), "static void Ack");

    /// <summary>起点与 CPU 基线都取自同一份回执快照（<c>mark?.AtMs</c> ＋ <c>mark?.Cpu</c>）。</summary>
    [Fact]
    public void BothStallBaselinesComeFromTheSameSnapshot()
    {
        var probe = ProbeBody();
        Assert.Contains("mark?.AtMs", probe, StringComparison.Ordinal);
        Assert.Contains("mark?.Cpu", probe, StringComparison.Ordinal);
    }

    /// <summary>
    /// 反向钉那条错口径：<b>不许再在"判定为卡顿"那一刻才取 CPU 基线</b>——
    /// 那一瞬间已经比墙钟起点晚了一个阈值，两个数就不是同一段。
    /// </summary>
    [Fact]
    public void TheCpuBaselineIsNoLongerSampledAtTheDetectionMoment()
        => Assert.DoesNotContain("_stallStartCpu = CpuMs()", ProbeBody(), StringComparison.Ordinal);

    /// <summary>回执端把时刻与 CPU <b>一次写成一对</b>；单独写 <c>_lastAck</c> 而不带快照的旧写法不许回来。</summary>
    [Fact]
    public void AckWritesOnePairNotTwoIndependentFields()
    {
        var ack = AckBody();
        Assert.Equal(1, SourceGate.Count(ack, "_mark = new AckMark("));
        Assert.DoesNotContain("Interlocked.Exchange(ref _lastAck, Environment.TickCount64)", ack, StringComparison.Ordinal);
    }

    /// <summary>快照必须真的带两个读数（哪天有人把 CPU 那个成员摘掉，配对就又是假的）。</summary>
    [Fact]
    public void TheSnapshotCarriesBothReadings()
        => Assert.Contains("record AckMark(long AtMs, long Cpu", Code(), StringComparison.Ordinal);

    /// <summary>
    /// 取不到 CPU 时仍要写"未知"，<b>不许退化成 0</b>；进程被挂起那一支仍要把基线复位，
    /// 否则下一段卡顿会拿"挂起前"的 CPU 当起点。
    /// </summary>
    [Fact]
    public void AnUnmeasurableCpuStaysUnknownAndSuspensionResetsTheBaseline()
    {
        var probe = ProbeBody();
        Assert.Contains("未知", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("cpu ?? 0", probe, StringComparison.Ordinal);
        Assert.Contains("_stallStartCpu = -1", probe, StringComparison.Ordinal);
    }

    /// <summary>墙钟那条线仍按"最后一次回执"算 <c>stalled</c>（快照只是给基线用，不是把观测换掉）。</summary>
    [Fact]
    public void TheStallSpanStillRunsOffTheAckClock()
        => Assert.Contains("now - Interlocked.Read(ref _lastAck)", ProbeBody(), StringComparison.Ordinal);

    /// <summary>心跳采样只准在回执端发生一次：探测线程每次 tick 都读 CPU，就把量具放进被量的那段了。
    /// （<c>Start()</c> 里那一份是"起点快照的第一版"，一个进程只取一次，由下一条门看着。）</summary>
    [Fact]
    public void CpuIsSampledOnTheAckNotOnEveryProbeTick()
    {
        Assert.Equal(1, SourceGate.Count(ProbeBody(), "CpuMs()"));     // 只在恢复那一刻用来求差
        Assert.Equal(1, SourceGate.Count(AckBody(), "CpuMs()"));       // 基线在这一端采
    }

    /// <summary>
    /// 起点快照在 <c>Start()</c> 里就要有一份，<b>不能等第一次回执</b>（批次 TC）。
    /// <para>
    /// 真机 6 颗那一跑的恢复行写的是「其间 UI 线程自己用了约 <b>未知</b> ms CPU」，而 12 颗那一跑同一列是
    /// 「1828 ms 墙钟／1703 ms CPU」。原因是那一段长阻塞开始得比<b>第一次回执</b>还早，<c>_mark</c> 还是 null；
    /// 同一行「前台页=未知」是同一根因的第二症状（回执那条链整条都没跑过）。
    /// 结果就是这一列<b>在最该有数的地方空白</b>——最早、也最重的那一段。
    /// </para>
    /// <para>
    /// 钉三点：Start 里确实种了一份、那份是<b>成对</b>的（时刻与 CPU 同一个 <c>now</c>，口径同批次 TB），
    /// 并且它排在 <c>OpenThread</c> 之后、计时器起来之前——排在句柄之前种下去的就是一份 -1，排在计时器之后则第一段已经没人量。
    /// </para>
    /// </summary>
    [Fact]
    public void TheBaselineExistsBeforeTheFirstAck()
    {
        var start = SourceGate.MethodBody(Code(), "static void Start(");
        Assert.Equal(1, SourceGate.Count(start, "_mark = new AckMark("));
        SourceGate.Between(start, "_uiThread = OpenThread(", "_mark = new AckMark(now, CpuMs() ?? -1);");
        SourceGate.Between(start, "_mark = new AckMark(now, CpuMs() ?? -1);", "_timer = new Timer(");
    }
}
