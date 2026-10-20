#nullable enable
using System;
using System.Text;
using StarMark.Core.Boards;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 那颗板<b>位置在盘上、却一颗字节也拿不出</b>时，宿主该说什么、该做什么（Phase C 片49＝P-239）。
/// <para>改前的形状只有一颗布尔：存储交回「读不到」，窗那边把它一律读成「没这颗板」，于是当场另起一块新板。
/// 屏幕上是一块空画布，而盘上那块板⛔ 动过一个字节——用户⛔ 知道是自己的板没了、还是别人的程序正攥着它。
/// 本批把那一颗布尔拆成<b>两族</b>（判据住 <see cref="BoardPolicy.SceneFileIsBlocked"/>，同 ✅P-238 那颗
/// <see cref="BoardPolicy.PickReadableScene"/> 一个口径：⛔ 住在存储里，测试工程⛔ 引用 <c>StarMark.UI</c>），
/// 先让<b>宿主自己</b>重试几回（P-54：那一手程序做得到，⛔ 转成用户的操作步数），到点还不行才开口，
/// 开口就给两颗按得动的按钮。</para>
/// <para>⚠ 这一族只有<b>一颗</b>形状尺以外的事实可钉：真盘上那颗被攥着的档只能真机摆（见 <c>bin/WI92/cap_49.py</c>
/// 那条腿，用 <c>FileShare.None</c> 借住那颗档）。这里的每一格都只说自己量得到的那一维。</para>
/// </summary>
public sealed class BoardSceneBlockedGateTests
{
    private const string Store = "src/StarMark.UI/Boards/BoardSceneStore.cs";
    private const string WindowCs = "src/StarMark.UI/Boards/BoardWindow.cs";

    /// <summary>①那颗三态的真身：四颗组合逐颗问，其中「位置在、读到了（哪怕是垃圾）」必须<b>不</b>算闸门——
    /// 那一档归 ✅P-238 的形状判据，混进来就是两句措辞共用一颗判据（改一句必染另一句）。
    /// <para>⚠ 空数组那一颗是<b>活的</b>：把判定写成 <c>present &amp;&amp; (bytes is null || bytes.Length == 0)</c>
    /// 就红在这里——而那正是把"读出一颗空档"重新折回"拿不出来"的第一步。</para></summary>
    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, "", false)]
    [InlineData(false, null, false)]
    [InlineData(false, "", false)]
    public void OnlyAFileThatIsThereButYieldsNoBytesIsBlocked(bool present, string? read, bool expected)
        => Assert.Equal(expected, BoardPolicy.SceneFileIsBlocked(
            present, read is null ? null : Encoding.UTF8.GetBytes(read)));

    /// <summary>②重试那两颗数住在 Core 而⛔ 住在调用点（同 <see cref="BoardPolicy.ArmRetryBudget"/> 那一条口径）。
    /// 颗数是本格<b>自己现读</b>的（2026-10-20）：下一批要改额度，就得同时改这一格与那句"重试过几回"的措辞。</summary>
    [Fact]
    public void TheHostIsGivenItsOwnRetryBudgetByCore()
    {
        Assert.Equal(3, BoardPolicy.LoadRetryTries);
        Assert.Equal(200, BoardPolicy.LoadRetryDelayMs);
        Assert.True(BoardPolicy.LoadRetryTries > 1, "额度一颗＝⛔ 重试，那句「宿主自己先试几回」就只剩一句空话");
    }

    /// <summary>③存储那一头只把两颗事实递给判据，⛔ 自己再拼一遍"在而读不出"（第二个主人），
    /// 也⛔ 在<b>没这颗档</b>那一支点亮那颗旗（点了＝"这块板没了"被说成"拿不出来"，两句措辞一起歪）。</summary>
    [Fact]
    public void TheStoreReportsTheThirdStateWithoutJudgingItTwice()
    {
        var load = Between(ReadRepoFile(Store), "public static bool TryLoad", "public static string? LatestSceneId");
        Assert.Contains("out bool blocked", load, StringComparison.Ordinal);
        Assert.Contains("blocked = BoardPolicy.SceneFileIsBlocked(true, onMain);", load, StringComparison.Ordinal);
        Assert.Contains("if (blocked) return false;", load, StringComparison.Ordinal);
        Assert.Equal(1, Count(Code(load), "BoardPolicy.SceneFileIsBlocked"));   // 用得上，而只用得上一次
        // 那颗旗只在「主档存在」那一支里点：早退（坏 id）与「压根没这颗档」两支⛔ 碰得到它
        Assert.Equal(0, Count(Code(load), "blocked = BoardPolicy.SceneFileIsBlocked(false"));
    }

    /// <summary>④窗那一头的分流：<b>拿不出</b>那一支⛔ 另起新板（本仓数过这一族：整段方法里那颗出口只许出现一次），
    /// 而且开口之前先把额度用满。那颗「再试一次」走的就是开场那一条，⛔ 在按钮那一手里再写一份重试。</summary>
    [Fact]
    public void TheWindowSpendsItsOwnRetryBeforeItSpeaks()
    {
        var open = MethodBody(Code(ReadRepoFile(WindowCs)),
            "public async System.Threading.Tasks.Task OpenSceneAsync");
        Assert.Contains("BoardPolicy.LoadRetryTries", open, StringComparison.Ordinal);
        Assert.Contains("BoardPolicy.LoadRetryDelayMs", open, StringComparison.Ordinal);
        Assert.Contains("ShowNotice(\"boards.blockedBody\", titled: true, offerCopy: false);", open, StringComparison.Ordinal);
        Assert.Equal(1, Count(open, "OpenNewAsync"));
        // 读到那一刻起，那颗闸门身份与那句坏话都得当场退场（挂着＝下一句坏话连带把「再试一次」贴回屏幕）
        var okBranch = Between(open, "if (BoardSceneStore.TryLoad", "await OpenAsync(bytes);");
        Assert.Contains("_blockedSceneId = null;", okBranch, StringComparison.Ordinal);
        Assert.Contains("HideNotice();", okBranch, StringComparison.Ordinal);
        var retry = MethodBody(Code(ReadRepoFile(WindowCs)),
            "private async System.Threading.Tasks.Task RetryBlockedSceneAsync");
        Assert.Contains("await OpenSceneAsync(sceneId);", retry, StringComparison.Ordinal);
        Assert.Equal(0, Count(retry, "Task.Delay"));     // 第二份重试代码＝第二个主人（一份忘了收面板是本仓定死的那类坏）
    }

    /// <summary>⑤那两颗按钮只有一位主人：在场资格只看 <c>_blockedSceneId</c>，算的地方只有重贴那一拍；
    /// 那颗「再试一次」把"正在跑"也念进同一拍（按下去有回音，跑完一定放开——⛔ 让一句"还在试"永远挂着）。
    /// <para>那颗「先开一块新板」收面板之外还必须把闸门身份清掉：新板那一途自己清（一处主人），本批两颗入口共用。</para></summary>
    [Fact]
    public void TheTwoButtonsOnThatSentenceHaveOneOwner()
    {
        var code = Code(ReadRepoFile(WindowCs));
        var paste = MethodBody(code, "private void PasteNotice");
        Assert.Contains("retry.Content = Word(\"boards.retryAction\");", paste, StringComparison.Ordinal);
        Assert.Contains("fresh.Content = Word(\"boards.freshBoardAction\");", paste, StringComparison.Ordinal);
        Assert.Contains("retry.IsEnabled = !_retrying;", paste, StringComparison.Ordinal);
        Assert.Equal(2, Count(paste, "_blockedSceneId is null ? Visibility.Collapsed : Visibility.Visible"));
        Assert.Equal(0, Count(MethodBody(code, "private void ShowNotice"), "_noticeRetryButton"));
        Assert.Equal(0, Count(MethodBody(code, "private void ShowNotice"), "_noticeFreshButton"));

        var retry = MethodBody(code, "private async System.Threading.Tasks.Task RetryBlockedSceneAsync");
        Assert.Contains("_retrying = true;", retry, StringComparison.Ordinal);
        Assert.Contains("_retrying = false;", retry, StringComparison.Ordinal);
        var fresh = MethodBody(code, "private void StartFreshBoard");
        Assert.Contains("_ = OpenNewAsync();", fresh, StringComparison.Ordinal);
        Assert.Contains("HideNotice();", fresh, StringComparison.Ordinal);
        Assert.Contains("public System.Threading.Tasks.Task OpenNewAsync", code, StringComparison.Ordinal);
        Assert.Contains("_blockedSceneId = null;", MethodBody(code, "public System.Threading.Tasks.Task OpenNewAsync"),
            StringComparison.Ordinal);
    }

    /// <summary>⑥旧形状⛔ 回来：一颗 <c>false</c> 同时说两件事那一行（片43 之前挂着半句字节数、本批之前折掉整族闸门）。
    /// <para>⚠ 这颗禁项读<b>抹掉注释后</b>的代码——本档注释里免不了要提那句旧形状（名册 308／483 那一族）。</para></summary>
    [Fact]
    public void OneFalseNoLongerStandsForTwoDifferentFailures()
    {
        Assert.Contains("out var bytes, out var fromBackup, out var blocked", ReadRepoFile(WindowCs), StringComparison.Ordinal);
        Assert.Equal(0, Count(Code(ReadRepoFile(WindowCs)), "out var bytes, out var fromBackup)) return OpenNewAsync();"));
        Assert.DoesNotContain("bytes.Length == 0", Code(ReadRepoFile(WindowCs)), StringComparison.Ordinal);
    }
}
