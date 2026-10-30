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
        Assert.Contains("d.BoardsEnabled == true", boards, StringComparison.Ordinal);
        Assert.Contains("d.BoardsEnabled = enabled;", boards, StringComparison.Ordinal);
        Assert.DoesNotContain("!= false", boards, StringComparison.Ordinal);
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
