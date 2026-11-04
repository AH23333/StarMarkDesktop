using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 画板开关接线那面闸门（Phase C 片20）：设置键默认关、VM 两颗出口、设置卡绑定、托盘入口受开关收、
/// 表五键三列。判据读仓库源文本（与 BoardWindowBridgeGateTests 同一族）。
/// </summary>
public sealed class BoardsSettingsGateTests
{
    private static readonly string RepoRoot = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarMark.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根（没有 StarMark.sln）");
    }

    private static string ReadRepo(string relative) => File.ReadAllText(Path.Combine(RepoRoot, relative));

    private const string StoreMain = "src/StarMark.UI/Helpers/SettingsStore.cs";
    private const string StoreBoards = "src/StarMark.UI/Helpers/SettingsStore.Boards.cs";
    private const string VmBoards = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Boards.cs";
    private const string VmMain = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string Tray = "src/StarMark.UI/MainWindow.Tray.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";

    private static readonly string[] NewKeys =
    [
        "settings.extend.boardsTitle",
        "settings.extend.boardsNote",
        "tray.boards",
        "tray.boardsNew",
        "boards.openFailedTitle",
        "boards.openFailedBody",
    ];

    [Fact]
    public void StoreSwitchDefaultsOff()
    {
        var main = ReadRepo(StoreMain);
        Assert.Contains("public bool? BoardsEnabled { get; set; }", main, StringComparison.Ordinal);
        var boards = ReadRepo(StoreBoards);
        // 默认关的形状＝「== true」判等：没表过态（null）落 false。写成 "!= false" 就是把默认翻成开。
        // ⚠ 下面那条禁项读的是<b>抹掉注释后</b>的那一份（#123／#131 同族）：片204 起那颗档的 doc 注释
        // 引用「!= false」那颗旧比较子是为了讲清倒置的来历，按原文数会把给人看的理由数成第二个出口。
        var boardsCode = SourceGate.Code(boards);
        Assert.Contains("d.BoardsEnabled == true", boards, StringComparison.Ordinal);
        Assert.Contains("d.BoardsEnabled = enabled;", boards, StringComparison.Ordinal);
        Assert.DoesNotContain("!= false", boardsCode, StringComparison.Ordinal);
        // 片204＝P-289（本批新立同批结案）：那句判据曾经整颗倒置——旧文是「Load() is not { } d || …」，
        // 那是 Capture 那颗「默认开」的句式（文件缺失回 null 那一臂交 true），照抄过来只翻了比较子
        // 而⛔ 翻句式 ⇒ 未表过态＝开，与档头注、本闸门名与片20 提交语三处口径全部相反，而上面那三行
        // 「== true／!= false」的碎片判据全绿（那颗比较子确实在、确实没写成 != false——倒置住在句式里）。
        // 所以这里把<b>整颗形状</b>钉死：默认关＝is { } d && … == true（null 那一臂交 false），
        // 而默认开的句式在那颗档里一颗也不许再出现。
        // ⚠ 两条禁项读的都是上面那份<b>抹掉注释后</b>的代码（#123／#131 同族）：那颗档的 doc 注释
        // 引用旧倒置句式是为了讲清为什么倒着——按原文数会把给人看的理由算成第二个出口。
        Assert.Contains("Load() is { } d && d.BoardsEnabled == true", boardsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("is not { } d ||", boardsCode, StringComparison.Ordinal);
    }

    /// <summary>片204＝P-286 处置①：开关 OFF ⇒ 已开的画板窗跟着收（关功能就该把它的窗收走，
    /// 留着开着的窗＝开关在说谎）。收＝每扇窗自己的那套关窗协商（拦一次→藏→冲写→被拒那一支
    /// 亮回「另存／继续编辑」面板＝「收前问一句要不要存」），⛔ 另起一份"直接拆"的捷径。</summary>
    [Fact]
    public void SwitchOffAsksEveryOpenBoardToCloseThroughItsOwnNegotiation()
    {
        var vm = ReadRepo(VmBoards);
        Assert.Contains("StarMark.UI.Boards.BoardsWindows.CloseAll();", vm, StringComparison.Ordinal);
        // 收窗那一拍排在存设置与刷托盘之后（先落盘再动窗）
        Assert.True(vm.IndexOf("App.MainWindow?.ApplyTraySettings();", StringComparison.Ordinal)
                    < vm.IndexOf("StarMark.UI.Boards.BoardsWindows.CloseAll();", StringComparison.Ordinal),
            "收窗排在存设置/刷托盘之前＝半路红一扇窗丢了状态");
        // OFF 那一臂才动窗：ON 侧开关只说"从今往后的入口"，⛔ 顺手拆已开的窗
        Assert.Contains("if (!value)", vm, StringComparison.Ordinal);
        // 名册是收窗的唯一主人：快照后逐扇 Close()＋逐扇兜异常（一扇收不掉⛔ 配带走其余几扇）
        var roster = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Boards/BoardsWindows.cs"));
        var close = SourceGate.MethodBody(roster, "public static void CloseAll()");
        Assert.Contains("lock (_open) windows = _open.ToArray();", close, StringComparison.Ordinal);
        Assert.Contains("w.Close();", close, StringComparison.Ordinal);
        Assert.Contains("catch", close, StringComparison.Ordinal);
    }

    [Fact]
    public void VmPartialWiresSaveAndTrayAndBackfill()
    {
        var vm = ReadRepo(VmBoards);
        Assert.Contains("[ObservableProperty] private bool _boardsEnabled;", vm, StringComparison.Ordinal);
        Assert.Contains("_settings.SaveBoardsEnabled(value);", vm, StringComparison.Ordinal);
        // 关掉→托盘当场收（「关掉就别留入口」口径的两半之一：另一半在 Tray 的 Where 里）
        Assert.Contains("App.MainWindow?.ApplyTraySettings();", vm, StringComparison.Ordinal);
        Assert.Contains("if (_suppressBoardsApply) return;", vm, StringComparison.Ordinal);
        var main = ReadRepo(VmMain);
        Assert.Contains("_suppressBoardsApply = true;", main, StringComparison.Ordinal);
        Assert.Contains("BoardsEnabled = Safe(_settings.LoadBoardsEnabled, false, \"画板\");", main, StringComparison.Ordinal);
        Assert.Contains("_suppressBoardsApply = false;", main, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPageCardBindsTheSwitch()
    {
        var x = ReadRepo(Xaml);
        Assert.Equal(1, Count(x, "settings.extend.boardsTitle"));
        Assert.Equal(1, Count(x, "settings.extend.boardsNote"));
        Assert.Equal(1, Count(x, "ViewModel.BoardsEnabled"));
        Assert.Contains("Mode=TwoWay", x, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayEntryFollowsTheSwitch()
    {
        var t = ReadRepo(Tray);
        Assert.Contains("private const int TrayBoards = 33;", t, StringComparison.Ordinal);
        Assert.Contains("private const int TrayBoardsNew = 34;", t, StringComparison.Ordinal);
        Assert.Contains("var boardsOn = _settings.LoadBoardsEnabled();", t, StringComparison.Ordinal);
        Assert.Contains("new(Locale.T(\"tray.boards\"), TrayBoards, SeparatorBefore: true),", t, StringComparison.Ordinal);
        Assert.Contains("new(Locale.T(\"tray.boardsNew\"), TrayBoardsNew),", t, StringComparison.Ordinal);
        // 关掉整条消失（与画布/截屏同一句 Where）：留一颗灰的＝「关掉还留入口」。
        // 片21 起画板那两颗入口共用一位开关，过滤条件必须一起收——只收前一颗＝留一颗"点得出却没板可开"的假入口。
        Assert.Contains("(boardsOn || (item.Tag != TrayBoards && item.Tag != TrayBoardsNew))", t, StringComparison.Ordinal);
        Assert.Contains("case TrayBoards:", t, StringComparison.Ordinal);
        Assert.Contains("case TrayBoardsNew:", t, StringComparison.Ordinal);
        // 两颗入口分岔只由那颗实参命名（⛔ 两个 case 各写一套起窗代码＝第二主人）
        Assert.Contains("OpenBoardsWindowAsync(resumeLast: true)", t, StringComparison.Ordinal);
        Assert.Contains("OpenBoardsWindowAsync(resumeLast: false)", t, StringComparison.Ordinal);
        // 片146：建那一扇搬进唯一主人 BoardsWindows（托盘两颗入口＋收藏系统点开一块板共用一趟起窗）；
        // 本档仍要点名"托盘那一路真的走到起窗"，只是点的从"新建"换成"请那一颗主人"。
        Assert.Contains("BoardsWindows.OpenSceneAsync(", t, StringComparison.Ordinal);
        Assert.Contains("new BoardWindow();",
            SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Boards/BoardsWindows.cs")), StringComparison.Ordinal);
        // 起窗失败必须有可见原因（「点了没反应」＝本仓定性过的坏）
        Assert.Contains("ShowNotice(LocMessage.Of(\"boards.openFailedTitle\"), LocMessage.Of(\"boards.openFailedBody\"));", t, StringComparison.Ordinal);
    }

    [Fact]
    public void TableKeysAreThreeColumnAndUnique()
    {
        var table = ReadRepo(Table);
        foreach (var key in NewKeys)
        {
            var matches = Regex.Matches(table, "new Entry\\(\"" + Regex.Escape(key) + "\", \"([^\"]*)\", \"([^\"]*)\", \"([^\"]*)\"\\)");
            Assert.True(matches.Count == 1, "键 " + key + " 在表里出现 " + matches.Count + " 次（须恰 1）");
            var m = matches[0];
            Assert.True(m.Groups[1].Value.Length > 0, key + " zh 列空");
            Assert.True(m.Groups[2].Value.Length > 0, key + " en 列空");
            Assert.True(m.Groups[3].Value.Length > 0, key + " ja 列空");
        }
    }

    private static int Count(string haystack, string needle)
        => haystack.Split(needle).Length - 1;
}
