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
}
