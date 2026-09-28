#nullable enable
using StarMark.Core.Startup;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「二次启动时对面那台进程算活实例还是残留」的判定契约。
/// <para>
/// 这块判定的产出是<b>结束另一个进程</b>，或者<b>本进程自我退出</b>——后者在真机上表现成
/// "双击图标没反应"（互斥体被一具没有窗口的残留攥着，新实例唤起不到任何东西又自己退了），
/// 前者表现成"用户的另一个窗口凭空消失"。两个方向都是只有真机能撞见的缺陷，所以判据逐臂钉死。
/// </para>
/// </summary>
public sealed class InstanceHandoffTests
{
    private static PeerInstance Peer(int pid, bool ours = true, bool window = false, bool pastGrace = true)
        => new(pid, ours, window, pastGrace);

    [Fact]
    public void AnyWindowedPeer_IsAnAliveInstance_AndGetsActivated()
    {
        var (action, pid, _) = InstanceHandoff.Decide(new[] { Peer(11, window: true) });

        Assert.Equal(HandoffAction.ActivatePeer, action);
        Assert.Equal(11, pid);
    }

    [Fact]
    public void WindowedPeer_Wins_EvenWhenAWindowlessOneLooksStale()
        // 同时看到"有窗口"和"没窗口"时绝不回收：只要还有一台在正常显示东西，本进程就该退出让位。
        => Assert.Equal(HandoffAction.ActivatePeer,
            InstanceHandoff.Decide(new[] { Peer(7), Peer(22, window: true) }).Action);

    [Fact]
    public void FirstWindowedPeerIsActivated_OrderIsPinnedSoTheChoiceIsReproducible()
        => Assert.Equal(31, InstanceHandoff.Decide(new[] { Peer(31, window: true), Peer(32, window: true) }).Pid);

    [Fact]
    public void SoleWindowlessPeerPastGrace_IsRecovered()
    {
        var (action, pid, reason) = InstanceHandoff.Decide(new[] { Peer(7) });

        Assert.Equal(HandoffAction.RecoverPeer, action);
        Assert.Equal(7, pid);
        Assert.Contains("pid=7", reason);
    }

    [Fact]
    public void WindowlessButStillWithinGrace_IsLeftAlone()
        // 起来不到宽限期的同名进程很可能正在建首帧窗口，这时结束它＝和"用户刚双击的另一个实例"抢命。
        => Assert.Equal(HandoffAction.GiveUp,
            InstanceHandoff.Decide(new[] { Peer(7, pastGrace: false) }).Action);

    [Fact]
    public void TwoWindowlessPeers_AreAmbiguous_AndNothingIsKilled()
    {
        var (action, pid, reason) = InstanceHandoff.Decide(new[] { Peer(7), Peer(8) });

        Assert.Equal(HandoffAction.GiveUp, action);
        Assert.Equal(0, pid);
        Assert.Contains("2 台", reason);
    }

    [Fact]
    public void ForeignProcessNames_AreNeverTreatedAsOurs()
        // 任意程序都能把窗口标题设成 StarMark；认"自己人"只认进程名。
        => Assert.Equal(HandoffAction.GiveUp,
            InstanceHandoff.Decide(new[] { Peer(7, ours: false) }).Action);

    [Fact]
    public void NoPeerAtAll_GivesUp_InsteadOfStartingAnyway()
        // 互斥体被占、对面却又不在进程表里：多半是持有者正好在结束。这一臂交给 UI 侧"再等一把锁"，
        // 判定本身不许猜，更不许结束任何东西。
        => Assert.Equal(HandoffAction.GiveUp, InstanceHandoff.Decide(System.Array.Empty<PeerInstance>()).Action);

    [Theory]
    [InlineData(new int[] { 1 }, new bool[] { false }, new bool[] { true })]
    [InlineData(new int[] { 1 }, new bool[] { true }, new bool[] { true })]
    [InlineData(new int[] { 1 }, new bool[] { false }, new bool[] { false })]
    public void EveryDecisionCarriesAReason_LogsNeverComeOutBlank(int[] pids, bool[] windows, bool[] grace)
    {
        // 热键/二次启动这类路径上日志是唯一的反馈渠道：结论没有原因＝用户问"为什么没起来"时无从答起。
        var peers = System.Linq.Enumerable.Select(
            System.Linq.Enumerable.Range(0, pids.Length),
            i => Peer(pids[i], window: windows[i], pastGrace: grace[i])).ToArray();

        Assert.False(string.IsNullOrWhiteSpace(InstanceHandoff.Decide(peers).Reason));
    }

    // ────────── UI 层接线闸门（测试工程不引用 StarMark.UI，只能扫源码） ──────────

    [Fact]
    public void ExitApp_ActuallyEndsTheProcess()
    {
        // WinUI 3 的 Application.Exit 不带下线进程；这一条守的就是"退出＝进程真的没了"，
        // 也是"托盘图标早退了、dll 还被锁着"能否重开的唯一依赖。
        var body = SourceGate.MethodBody(
            SourceGate.ReadRepoPartials("src/StarMark.UI/MainWindow.xaml.cs"),
            "private async void ExitApp()");

        Assert.Contains("Environment.Exit(0)", body);
        Assert.Contains("Thread.Sleep(5000)", body);   // 收尾卡住也要死的兜底
    }

    [Fact]
    public void HandoffIdentity_IsNotHardcodedToTheWindowTitle()
    {
        // 早先这里比对的是 "StarMark"，而 apphost 叫 StarMark.UI.exe ⇒ 校验永远不通过 ⇒
        // 二次启动"放弃转交"后自我退出。名字改成取自自身所在 exe，闸门钉住别再退回字面量。
        var body = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.UI/App.xaml.cs"),
            "private static bool IsOurMainWindow(IntPtr hwnd)");

        Assert.Contains("InstanceProbe.OurProcessName", body);
        Assert.Equal(0, SourceGate.Count(body, "\"StarMark\""));
    }

    [Fact]
    public void SecondLaunch_RecoveryIsWiredNotJustImplemented()
    {
        // 判定与执行分开写容易留一条"函数有了但没人调"的空档（RI-5 那次规则写进没人经过的方法）。
        // 这里钉的就是：抢锁失败的那一段确实调了回收，而不是只把方法躺在文件里。
        var launched = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.UI/App.xaml.cs"),
            "protected override void OnLaunched(LaunchActivatedEventArgs args)");

        Assert.Contains("TryActivateExistingInstance()", launched);
        Assert.Contains("TryRecoverStaleInstance()", launched);
        Assert.Contains("ResolveStaleInstanceFromArgs()", launched);
    }
}
