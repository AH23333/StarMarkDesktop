using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Core.Boards;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 板档持久化那面闸门（Phase C 片21＝§21.3 存储适配器那一半）。
/// <para>
/// ⚠ 测试工程引用不到 <c>StarMark.UI</c>（坑表 #370 那条边界），所以下面分两族：
/// <b>能真跑的</b>只有 Core 那几颗纯判定（档位分流、那颗 .tmp 尾名、跨语言那颗数字的等值），
/// UI 侧（板目录、原子写、托盘两颗入口）只能钉<b>源码形状</b>——"关掉之后菜单里还有吗""重开真的载得回来吗"
/// 那两格仍归真机点验（欠项清单在报告 §四百零五 末尾）。
/// </para>
/// </summary>
public sealed class BoardSceneStoreGateTests
{
    private static readonly string RepoRoot = FindRoot();
    private static readonly string Js = Read("tools/boards/board-bridge.js");
    private static readonly string Store = Read("src/StarMark.UI/Boards/BoardSceneStore.cs");
    private static readonly string WindowCs = Read("src/StarMark.UI/Boards/BoardWindow.cs");
    private static readonly string Tray = Read("src/StarMark.UI/MainWindow.Tray.cs");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarMark.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根（没有 StarMark.sln）");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepoRoot, relative));

    private static int Count(string haystack, string needle) => haystack.Split(needle).Length - 1;

    /// <summary>取两颗方法名之间那一段（先算两颗整数下标，⛔ 把 Range 当 Index 用——CS0029 的形状）。</summary>
    private static string Between(string text, string from, string to)
    {
        var a = text.IndexOf(from, StringComparison.Ordinal);
        var b = text.IndexOf(to, StringComparison.Ordinal);
        Assert.True(a >= 0 && b > a, $"切片找不齐锚点：{from}({a}) → {to}({b})");
        return text[a..b];
    }

    /// <summary>
    /// ① 档位分流那颗判定住在 Core，且<b>三档各归一处</b>：只有 <c>scene</c> 是板的本体。
    /// 正件＝那颗名必须真在 <see cref="BridgeProtocol.ExportKinds"/> 里（⛔ 判据认一颗名单外的名字）。
    /// </summary>
    [Fact]
    public void OnlyTheSceneKindIsTheBoardItself()
    {
        Assert.True(BridgeProtocol.LandsInSceneArchive("scene"));
        Assert.False(BridgeProtocol.LandsInSceneArchive("png"));
        Assert.False(BridgeProtocol.LandsInSceneArchive("svg"));
        Assert.False(BridgeProtocol.LandsInSceneArchive(null));
        Assert.False(BridgeProtocol.LandsInSceneArchive("SCENE"));   // 大小写敏感＝Ordinal，与那颗名单同一口径
        Assert.Contains("scene", BridgeProtocol.ExportKinds);
    }

    /// <summary>
    /// ② 命名的唯一主人＝<see cref="BoardPolicy"/>：存储那一档⛔ 再拼一份后缀字面量。
    /// <para>反面就是"两颗主人对不上"：板档后缀哪天改了，写出来的还是旧的，而 <c>IsSceneFileName</c> 认不出它＝那颗板凭空消失。</para>
    /// </summary>
    [Fact]
    public void TheStoreNeverInventsItsOwnFileNames()
    {
        Assert.Contains("BoardPolicy.SceneFileName(sceneId)", Store, StringComparison.Ordinal);
        Assert.Contains("BoardPolicy.BackupFileName(sceneId)", Store, StringComparison.Ordinal);
        Assert.DoesNotContain("\".excalidraw\"", Store, StringComparison.Ordinal);
        Assert.DoesNotContain("\".bak\"", Store, StringComparison.Ordinal);
        // 那颗 .tmp 的尾名只许有一颗主人：一颗声明＋一处拼名字（⛔ 数"出现了几次"——那颗数把注释也算进去了）
        Assert.Equal(1, Count(Store, "public const string TempSuffix"));
        Assert.Equal(1, Count(Store, "main + TempSuffix"));
    }

    /// <summary>
    /// ③ 写的那条顺序：滚上一份 → 写临时件 → 同卷改名盖过去。三处缺一处就不是"最少丢一次改动"而是"可能丢整块板"。
    /// <para>且那颗 id 必须先过身份判据（⛔ 拿内容／拿 web 递来的串当身份），失败必须回布尔而⛔ 抛到调用方脸上。</para>
    /// </summary>
    [Fact]
    public void TheWritePathIsRollThenTempThenMoveAndGatesOnIdentity()
    {
        Assert.Contains("BoardPolicy.IsSceneId(sceneId)", Store, StringComparison.Ordinal);
        var save = Between(Store, "public static bool TrySave", "public static bool TryLoad");
        var roll = save.IndexOf("File.Copy(main, BackupPathOf(sceneId), overwrite: true)", StringComparison.Ordinal);
        var temp = save.IndexOf("File.WriteAllBytes(temp, bytes)", StringComparison.Ordinal);
        var move = save.IndexOf("File.Move(temp, main, overwrite: true)", StringComparison.Ordinal);
        Assert.True(0 < roll && roll < temp && temp < move,
            "写盘三处的先后＝滚→临时件→改名，现在是 roll=" + roll + " temp=" + temp + " move=" + move);
        Assert.Contains("catch (Exception ex)", save, StringComparison.Ordinal);
        Assert.DoesNotContain("throw", save, StringComparison.Ordinal);
    }

    /// <summary>
    /// ④ 读的那条退路：主档没了要退到 <c>.bak</c>，两颗都没了才说"没有这颗板"。
    /// <para>"最近一块板"按<em>盘上的修改时间</em>算（⛔ 另存一份"最近"指针＝第二个主人，档删了它还指着）。</para>
    /// </summary>
    [Fact]
    public void TheLoadFallsBackToTheBackupAndTheLatestIsReadFromDisk()
    {
        var load = Between(Store, "public static bool TryLoad", "public static string? LatestSceneId");
        Assert.Contains("PathOf(sceneId)", load, StringComparison.Ordinal);
        Assert.Contains("BackupPathOf(sceneId)", load, StringComparison.Ordinal);
        var latest = Between(Store, "public static string? LatestSceneId", "public const string TempSuffix");
        Assert.Contains("BoardPolicy.IsSceneFileName(name)", latest, StringComparison.Ordinal);   // .bak／.tmp ⛔ 算一颗板
        Assert.Contains("GetLastWriteTimeUtc(file)", latest, StringComparison.Ordinal);
        Assert.DoesNotContain("recent", Store, StringComparison.OrdinalIgnoreCase);                 // ⛔ 第二份"最近"状态
    }

    /// <summary>
    /// ⑤ 页面侧那颗"存"必须是 <c>serializeAsJSON(…, "local")</c>，⛔ 用 <c>exportToBlob</c> 的 PNG 顶包；
    /// 载必须补第三步 <c>addFiles</c>（<c>SceneData</c> 逐字⛔ files 字段），撤销立场那颗 <c>NEVER</c> 也在这里钉住。
    /// </summary>
    [Fact]
    public void ThePageSavesRealSceneJsonAndRestoresTheFilesToo()
    {
        Assert.Equal(1, Count(Js, "L.serializeAsJSON("));
        Assert.Contains("getFiles(), \"local\")", Js, StringComparison.Ordinal);   // 第四颗形参定 "local"（§21.3）
        Assert.Contains("API.addFiles(files)", Js, StringComparison.Ordinal);
        Assert.Contains("captureUpdate: L.CaptureUpdateAction.NEVER", Js, StringComparison.Ordinal);
        // 场景那颗⛔ 再走"PNG 里嵌一份场景"那条路：只有 png／svg 用 exportToBlob／exportToSvg。
        // ⚠ 禁的是<b>那颗实参的形状</b>（<c>exportEmbedScene:</c>）而⛔ 光秃秃的名字——名字在本档的注释里
        // 还要用来讲清"为什么不走那条路"（名册 308：禁项串被证人自己抄了一份＝恒红）。
        Assert.DoesNotContain("exportEmbedScene:", Js, StringComparison.Ordinal);
        Assert.Equal(1, Count(Js, "postIn(\"export.bytes\""));                      // 交字节只有一处出口
        Assert.Contains("onChange:", Js, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑥ 那颗"停笔多久"有两颗持有者（Core 的常数＋页面侧的去抖字面量），跨语言⛔ 编译器管不着——
    /// 这一格就是那把尺。改一颗而不动另一颗＝这里红（⛔ 把"两处各写一份"当成已经对齐）。
    /// </summary>
    [Fact]
    public void TheIdleBeatIsOneNumberAcrossTheTwoLanguages()
    {
        var m = Regex.Match(Js, @"var AUTOSAVE_MS\s*=\s*(\d+)\s*;");
        Assert.True(m.Success, "页面侧那颗去抖常数找不到了（改名／换写法要连这一格一起改）");
        Assert.Equal(BoardPolicy.AutosaveIdleMs, int.Parse(m.Groups[1].Value));
    }

    /// <summary>
    /// ⑦ 那颗现读缺陷的尺（片21）：闸门读 <c>tools/boards/</c>，发出去的是 <c>assets/boards/</c>，
    /// 而<em>本批之前没有任何一把尺验过这两份相等</em>——改了源档而忘了重跑 <c>stage-assets.py</c>
    /// 就是"测试全绿而用户跑的是旧桥"。这一格把那条缝堵上。
    /// </summary>
    [Theory]
    [InlineData("board-bridge.js")]
    [InlineData("board.html")]
    public void TheTestedSourceCopyIsTheShippedCopy(string fileName)
    {
        var source = File.ReadAllBytes(Path.Combine(RepoRoot, "tools", "boards", fileName));
        var shipped = File.ReadAllBytes(Path.Combine(RepoRoot, "assets", "boards", fileName));
        Assert.Equal(Convert.ToBase64String(source), Convert.ToBase64String(shipped));
    }

    /// <summary>
    /// ⑧ 宿主那一半的分流与索取：scene 字节进板档（⛔ 时刻前缀），其余仍进导出目录；
    /// 失焦那一拍才发 <c>export.pick{kind:"scene"}</c>，而桥没通时⛔ 发（发了没人回＝假动作）。
    /// </summary>
    [Fact]
    public void TheHostRoutesSceneBytesAndAsksOnlyAfterTheBridgeIsUp()
    {
        Assert.Contains("BridgeProtocol.LandsInSceneArchive(envelope.Kind)", WindowCs, StringComparison.Ordinal);
        Assert.Contains("BoardSceneStore.TrySave(_boardId, bytes, out var fault)", WindowCs, StringComparison.Ordinal);
        Assert.Contains("save-failed", WindowCs, StringComparison.Ordinal);                 // 写不成要回一颗可上屏的故障名
        var ask = Between(WindowCs, "private void RequestSceneSnapshot", "private static string Envelope");
        Assert.Contains("if (!_bridgeReady) return;", ask, StringComparison.Ordinal);
        Assert.Contains("JsonString(\"kind\") + \":\" + JsonString(\"scene\")", ask, StringComparison.Ordinal);
        Assert.Contains("WindowActivationState.Deactivated) RequestSceneSnapshot();", WindowCs, StringComparison.Ordinal);
        Assert.Contains("await win.OpenSceneAsync(last)", Tray, StringComparison.Ordinal);
    }

    /// <summary>⑨ 托盘那一族：画板的两颗入口共用一位开关（关掉＝两颗一起没，⛔ 留一颗"点得出却没有板可开"）。</summary>
    [Fact]
    public void BothBoardTrayEntriesFollowTheOneSwitch()
    {
        Assert.Equal(1, Count(Tray, "(boardsOn || (item.Tag != TrayBoards && item.Tag != TrayBoardsNew))"));
        Assert.Equal(1, Count(Tray, "new(Locale.T(\"tray.boards\"), TrayBoards, SeparatorBefore: true),"));
        Assert.Equal(1, Count(Tray, "new(Locale.T(\"tray.boardsNew\"), TrayBoardsNew),"));
        Assert.Equal(1, Count(Tray, "case TrayBoardsNew:"));
    }

    /// <summary>
    /// ⑩ 那颗显示名<b>真的到得了宿主</b>（片21 欠项⑤）：页面出口、白名单、宿主读者三头对上；
    /// 而板档那一路⛔ 读它——档名＝那颗 id（§21.3 那句"显示名只当显示名、⛔ 当身份"）。
    /// <para>页面侧禁的是<b>引擎那颗出口的实参形状</b>：现读 bundle 里它永远⛔ 回空
    /// （<c>state.name || props.name || 未命名-时刻</c>），递它＝宿主那颗"id＋时刻"的兜底名又在链路上死一颗——
    /// 正是本格要修的那件事换了个名字重犯。⚠ 禁串写成 <c>getName(</c> 而⛔ 光一颗名字（名册 308）。</para>
    /// </summary>
    [Fact]
    public void TheSuggestedNameReachesTheHostButNeverNamesTheBoardFile()
    {
        Assert.Equal(1, Count(Js, "function deliver(bytesB64, kind, sessionId, suggested)"));
        Assert.Contains("suggested: String(suggested == null ? \"\" : suggested)", Js, StringComparison.Ordinal);
        Assert.Equal(3, Count(Js, "boardName())"));        // 三拍出口各递一次：停笔／被索取的 scene／png·svg

        var nameReader = Between(Js, "function boardName", "function flushSave");
        Assert.Contains("API.getAppState()", nameReader, StringComparison.Ordinal);   // 读的是那颗字段本尊
        Assert.DoesNotContain("getName(", nameReader, StringComparison.Ordinal);

        Assert.Contains("BridgeProtocol.SanitizeExportName(envelope.Suggested, stem, envelope.Kind)",
            WindowCs, StringComparison.Ordinal);
        var sceneBranch = Between(WindowCs, "if (BridgeProtocol.LandsInSceneArchive(envelope.Kind)", "var stem =");
        Assert.DoesNotContain("Suggested", sceneBranch, StringComparison.Ordinal);     // 板档档名⛔ 跟着显示名走
    }

    /// <summary>
    /// ⑪ §21.4 那颗脏位（片21 欠项④）：字节逐颗相等就⛔ 动盘、⛔ 滚。这一格钉的是<b>先后</b>而⛔ 只是"那颗判据存在"——
    /// 门排在 <c>TrySave</c> 之前才保得住退路，排在后面照旧每拍滚一次。
    /// 另一头钉两颗"必写"的形状：写失败⛔ 点亮（盘上仍是旧的那份）、从 <c>.bak</c> 捡回来的板也⛔ 点亮（主档位置是空的，
    /// 而 <c>LatestSceneId</c> 只认主档）——那颗旗由存储那一档交回，UI 侧⛔ 自己再摸一遍盘＝第二个主人。
    /// </summary>
    [Fact]
    public void TheUnchangedSceneNeitherWritesNorRollsTheBackup()
    {
        Assert.Contains("if (BoardPolicy.DiskAlreadyHolds(_lastWritten, bytes))", WindowCs, StringComparison.Ordinal);
        var gate = Between(WindowCs, "if (BoardPolicy.DiskAlreadyHolds", "if (BoardSceneStore.TrySave");
        Assert.Contains("Reply(envelope.Cmd, envelope.Id, ok: true);", gate, StringComparison.Ordinal);
        Assert.Contains("return;", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("TrySave", gate, StringComparison.Ordinal);

        var success = Between(WindowCs, "if (BoardSceneStore.TrySave(_boardId, bytes, out var fault))",
            "StarMark.Abstractions.StarLog.Warn");
        Assert.Contains("_lastWritten = bytes;", success, StringComparison.Ordinal);   // 只有真写成的那一支点亮
        Assert.Equal(1, Count(WindowCs, "_lastWritten = bytes;"));

        Assert.Contains("_lastWritten = null;", WindowCs, StringComparison.Ordinal);   // 新板：第一拍必写
        Assert.Contains("if (!BoardSceneStore.TryLoad(sceneId, out var bytes, out var fromBackup) || bytes.Length == 0) return OpenNewAsync();",
            WindowCs, StringComparison.Ordinal);
        Assert.Contains("_lastWritten = fromBackup ? null : bytes", WindowCs, StringComparison.Ordinal);
        Assert.Contains("out bool fromBackup", Store, StringComparison.Ordinal);
        Assert.Equal(1, Count(Store, "fromBackup = true;"));
    }
}
