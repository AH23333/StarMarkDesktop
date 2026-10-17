using System;
using System.IO;
using System.Linq;
using StarMark.Core.Boards;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 画板桥回路那面闸门（Phase C 片19）：页面侧名单与 Core <see cref="BridgeProtocol"/> 逐颗对账、
/// BoardWindow 的宿主形状（虚拟宿主名／userDataFolder 改道／关窗即散／默认关）。
/// </summary>
public sealed class BoardWindowBridgeGateTests
{
    private static readonly string RepoRoot = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarMark.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根（没有 StarMark.sln）");
    }

    private static string ReadRepo(string relative) => File.ReadAllText(Path.Combine(RepoRoot, relative));

    private const string BridgeJs = "tools/boards/board-bridge.js";
    private const string BoardHtml = "tools/boards/board.html";
    private const string WindowCs = "src/StarMark.UI/Boards/BoardWindow.cs";

    [Fact]
    public void BridgeJsSpeaksTheWiredSubsetOfTheProtocol()
    {
        var js = ReadRepo(BridgeJs);
        // 片19 最小回路：页面侧只会喊它真正实现了的那两颗；其余三颗 ⛔ 假喊（宿主回 not-wired）
        Assert.Equal(1, Count(js, "postIn(\"bridge.ready\""));
        Assert.Equal(1, Count(js, "postIn(\"export.bytes\""));
        Assert.Equal(0, Count(js, "postIn(\"paste.query\""));
        Assert.Equal(0, Count(js, "postIn(\"pin.request\""));
        Assert.Equal(0, Count(js, "postIn(\"background.request\""));
        // 出向五颗：页面侧逐颗处理（case 逐颗一处）
        foreach (var name in BridgeProtocol.OutboundEvents)
            Assert.Equal(1, Count(js, "case \"" + name + "\""));
    }

    [Fact]
    public void BridgeJsNeverPostsAnUnknownCmdLiteral()
    {
        var js = ReadRepo(BridgeJs);
        var matches = System.Text.RegularExpressions.Regex.Matches(js, "postIn\\(\"([^\"]+)\"");
        foreach (System.Text.RegularExpressions.Match m in matches)
            Assert.True(BridgeProtocol.IsKnownName(m.Groups[1].Value, BridgeProtocol.Direction.Inbound),
                "页面侧出现了名单外的 cmd：" + m.Groups[1].Value);
    }

    [Fact]
    public void BoardPageWiresTheAssetGlobalAndTheBundle()
    {
        var html = ReadRepo(BoardHtml);
        Assert.Contains("window.EXCALIDRAW_ASSET_PATH = \"/\";", html, StringComparison.Ordinal);
        Assert.Contains("excalidraw.min.js", html, StringComparison.Ordinal);
        Assert.Contains("board-bridge.js", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BoardWindowUsesTheVirtualHostAndThePinnedUserDataFolder()
    {
        var cs = ReadRepo(WindowCs);
        Assert.Contains("SetVirtualHostNameToFolderMapping", cs, StringComparison.Ordinal);
        Assert.Contains("https://\" + VirtualHostName + \"/\" + BoardPageName", cs, StringComparison.Ordinal);
        // v1 §坑8 那条改道：userDataFolder 钉在 %LOCALAPPDATA%\StarMark\wbview2，⛔ 跟 exe 目录
        Assert.Contains("SpecialFolder.LocalApplicationData", cs, StringComparison.Ordinal);
        Assert.Contains(@"StarMark\wbview2", cs, StringComparison.Ordinal);
        Assert.Equal(1, Count(cs, "SetVirtualHostNameToFolderMapping"));
    }

    [Fact]
    public void BoardWindowDisposesOnClosedAndStaysDefaultOff()
    {
        var cs = ReadRepo(WindowCs);
        Assert.Contains("Closed += (_, _) => OnClosed()", cs, StringComparison.Ordinal);
        Assert.Contains("_web.Close()", cs, StringComparison.Ordinal);
        Assert.Contains("WebMessageReceived -= OnWebMessageReceived", cs, StringComparison.Ordinal);
        // 默认关：本档⛔ 自动挂常驻入口（主窗接线归接线批）
        Assert.DoesNotContain("MainWindow", cs, StringComparison.Ordinal);
        Assert.Contains("OpenNewAsync", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void BoardWindowNeverInventsItsOwnWhitelist()
    {
        var cs = ReadRepo(WindowCs);
        // 名单主人＝BridgeProtocol：窗口⛔ 复写一份名单数组（第二主人＝对不上时静默失效那颗坑）
        Assert.DoesNotContain("string[] InboundCommands", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("string[] OutboundEvents", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InboundFields =", cs, StringComparison.Ordinal);
        // 三颗已放行的行为分支在；未接线的三颗走 default 的 not-wired
        Assert.Contains("case \"bridge.ready\"", cs, StringComparison.Ordinal);
        Assert.Contains("case \"open.external\"", cs, StringComparison.Ordinal);
        Assert.Contains("case \"export.bytes\"", cs, StringComparison.Ordinal);
        Assert.Contains("not-wired", cs, StringComparison.Ordinal);
    }

    private static int Count(string haystack, string needle)
        => haystack.Split(needle).Length - 1;

    // ────────── 片24：四拍剩下那两拍 ──────────

    /// <summary>
    /// <b>崩</b>那一拍与<b>关窗</b>那一拍都真挂上了，而且开口之前先问 Core「这一拍问得到吗」。
    /// <para>这一格里最要紧的是 <c>_bridgeReady = false;</c> 出现在那颗 handler 里：那一颗旗要是还亮着，
    /// 下一次失焦就往一颗<b>死控件</b> <c>PostWebMessageAsJson</c>——COM 异常当场抛在 UI 线程上，
    /// 用户看到的正是"画板自己崩给我看"。</para>
    /// </summary>
    [Fact]
    public void TheLastTwoSaveBeatsAreWiredAndBothAskThroughCore()
    {
        var cs = ReadRepo(WindowCs);
        Assert.Contains("ProcessFailed += OnProcessFailed", cs, StringComparison.Ordinal);
        Assert.Contains("ProcessFailed -= OnProcessFailed", cs, StringComparison.Ordinal);   // 拆窗那一拍摘干净
        Assert.Contains("AppWindow.Closing += OnAppWindowClosing", cs, StringComparison.Ordinal);
        Assert.Contains("BoardPolicy.BeatCanAskForSnapshot(beat)", cs, StringComparison.Ordinal);
        Assert.Contains("RequestSceneSnapshot(BoardPolicy.SaveBeat.LosingFocus)", cs, StringComparison.Ordinal);
        Assert.Contains("RequestSceneSnapshot(BoardPolicy.SaveBeat.ClosingWindow)", cs, StringComparison.Ordinal);
        Assert.Equal(3, Count(cs, "RequestSceneSnapshot("));        // 定义一颗＋两拍各一颗
        Assert.Contains("_bridgeReady = false;",
            SourceGate.MethodBody(cs, "void OnProcessFailed"), StringComparison.Ordinal);
    }

    /// <summary>
    /// 恢复起来那一页拿到的＝<b>宿主见过的那一份</b>，⛔ 一颗空板（§21.5 那句硬约束落在这颗字段上）。
    /// <para>首开与重载共用同一条 <c>OpenAsync</c>：两份开场代码就是第二个主人，而第二主人的第一号坏法
    /// 是"其中一份忘了递档"。那颗被摘掉的 <c>_pendingScene</c> 名字两头都⛔ 留（正件＝新名字在这一档里读得到）。</para>
    /// </summary>
    [Fact]
    public void TheRecoveredPageGetsTheSceneTheHostHasSeenNotAnEmptyBoard()
    {
        var cs = ReadRepo(WindowCs);
        Assert.Contains("if (_seenScene is { Length: > 0 } bytes)", cs, StringComparison.Ordinal);
        Assert.Contains("RecoverByRecreatingWeb", cs, StringComparison.Ordinal);
        Assert.Contains("_web.CoreWebView2?.Reload()", cs, StringComparison.Ordinal);
        Assert.Equal(1, Count(cs, "private async System.Threading.Tasks.Task OpenAsync("));
        Assert.Contains("await OpenAsync(_seenScene)",
            SourceGate.MethodBody(cs, "Task OpenRecoverAsync"), StringComparison.Ordinal);
        Assert.DoesNotContain("_pendingScene", cs, StringComparison.Ordinal);
        Assert.True(Count(cs, "_seenScene") >= 5, "这把尺要是瞎的，新名字在这一档里也读不到");
    }

    /// <summary>
    /// 关窗那一拍的等待<b>有界</b>、等的是"这一问有答案"而⛔"答案好听"，而且到点会说话。
    /// <para>那颗毫秒数只许住在主人那里（UI 档里再写一遍 2000＝第二主人，改的人只改得动一处）；
    /// 两处 <c>FinishClosingFlush();</c> 分别对应"盘上本来就是它"与"这一问交回答案了（写成功还是写失败都算）"——
    /// 少了前者那次关不掉，少了后者那颗写失败要用户白等两秒。</para>
    /// </summary>
    [Fact]
    public void TheCloseFlushIsBoundedAndSaysWhenItGaveUp()
    {
        var raw = ReadRepo(WindowCs);
        var code = SourceGate.Code(raw);
        Assert.Contains("BoardPolicy.CloseFlushBudgetMs", code, StringComparison.Ordinal);
        Assert.Equal(0, Count(code, "2000"));                       // 那颗数⛔ 在这档里再出现一次
        Assert.Equal(2000, BoardPolicy.CloseFlushBudgetMs);         // 正件：主人那里它还在
        Assert.Equal(2, Count(code, "FinishClosingFlush();"));
        Assert.Contains("AppWindow.Hide()", code, StringComparison.Ordinal);
        Assert.Contains("boards.close.flush.timeout", raw, StringComparison.Ordinal);
        Assert.Contains("_recovering", code, StringComparison.Ordinal);   // 两颗控件抢同一扇窗＝更坏的现场
    }
}
