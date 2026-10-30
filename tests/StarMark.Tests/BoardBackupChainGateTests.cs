#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Core.Backup;
using StarMark.Core.Boards;

namespace StarMark.Tests;

/// <summary>
/// 「场景 → 备份链」那一行（集成矩阵第六行＝Phase C 片146）：用户手画的板要能跟着备份换机，
/// 而还原⛔ 得把用户今天画的板顶掉。
/// <para>这一族真跑盘（⛔ mock 那颗包）：<c>TestSandboxRedirect</c> 已经把进程级的
/// <c>STARMARK_DB_PATH</c> 指进 <c>%TEMP%\StarMark.Tests\</c>，所以 <see cref="BoardPolicy.ScenesFolder"/>
/// 在测试里就是那棵沙盒树——这颗证人验的正是"改道带不带得动画板档"（批次 SS 那族坑的正件）。</para>
/// <para>每颗测试⛔ 清整棵目录，只删自己造的那几颗档：那是共享沙盒树，别的批次今天没碰、明天可能碰。</para>
/// </summary>
public sealed class BoardBackupChainGateTests : IDisposable
{
    private static readonly byte[] SceneDoc = System.Text.Encoding.UTF8.GetBytes(
        """{"type":"excalidraw","version":2,"source":"https://excalidraw.com","elements":[{"id":"t","type":"text","isDeleted":false,"originalText":"那块板上写的字"}],"appState":{},"files":{}}""");

    private readonly List<string> _mine = new();
    private readonly string _packages;

    public BoardBackupChainGateTests()
    {
        Directory.CreateDirectory(BoardPolicy.ScenesFolder());
        _packages = Path.Combine(Path.GetTempPath(), "StarMark.Tests", "board-backup-pkgs");
        Directory.CreateDirectory(_packages);
    }

    public void Dispose()
    {
        foreach (var f in _mine) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
    }

    /// <summary>在沙盒的板档目录里造一块板（那颗 id 同时进 <see cref="_mine"/>，收尾只删自己造的）。</summary>
    private string PutScene(string id, byte[]? bytes = null)
    {
        var path = BoardPolicy.ScenePath(id);
        File.WriteAllBytes(path, bytes ?? SceneDoc);
        _mine.Add(path);
        return path;
    }

    private string PackagePath(string name)
    {
        var p = Path.Combine(_packages, name);
        _mine.Add(p);
        return p;
    }

    // ─────────── ① 导出：那一族真进了包，而清单字节一字⛔ 变 ───────────

    [Fact]
    public async Task ExportPutsSceneFilesUnderBoardsPrefix_AndTheManifestBytesStayIdentical()
    {
        var id1 = "261030-071500-a1a1";
        var id2 = "261030-071501-b2b2";
        PutScene(id1);
        PutScene(id2);
        const string manifest = """{"app":"StarMark","version":4,"checksum":"deadbeef"}""";
        var package = PackagePath("carry.zip");

        var (clips, clipBytes, boards, boardBytes) = await BackupContainer.WriteAsync(
            package, manifest, Array.Empty<string>(),
            new[] { BoardPolicy.SceneFileName(id1), BoardPolicy.SceneFileName(id2) }, CancellationToken.None);

        Assert.Equal(0, clips);
        Assert.Equal(2, boards);
        Assert.True(boardBytes > 0);
        Assert.True(clipBytes == 0);

        using var zip = ZipFile.OpenRead(package);
        Assert.Equal(manifest, new StreamReader(zip.GetEntry(BackupContainer.ManifestEntryName)!.Open()).ReadToEnd());
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains(BackupContainer.BoardsPrefix + BoardPolicy.SceneFileName(id1), names);
        Assert.Contains(BackupContainer.BoardsPrefix + BoardPolicy.SceneFileName(id2), names);
        // ⛔ 是一条绝对路径、⛔ 是退路那份、⛔ 是半途那一颗
        Assert.DoesNotContain(names, n => n.Contains(".."));
    }

    /// <summary>
    /// 计划里混进 <c>.bak</c>／<c>*.tmp</c>／一颗不像档名的东西：三颗都进不了包。
    /// <para>那颗退路按定义是<b>上一版</b>，把它带进备份＝用户换机后同一块板有两份互不相干的档，
    /// 而"哪一份是真的"从此没人答得出（✅P-223 那条线在备份链上的另一半）。</para>
    /// </summary>
    [Fact]
    public async Task ExportNeverCarriesTheBackupCopyOrAHalfWrittenFile()
    {
        var id = "261030-071600-c3c3";
        PutScene(id);
        var bak = BoardPolicy.ScenePath(id) + BoardPolicy.BackupSuffix;
        File.WriteAllBytes(bak, SceneDoc);
        _mine.Add(bak);
        var tmp = BoardPolicy.ScenePath(id) + BoardPolicy.SceneTempSuffix;
        File.WriteAllBytes(tmp, SceneDoc);
        _mine.Add(tmp);

        var package = PackagePath("cleaned.zip");
        var (_, _, boards, _) = await BackupContainer.WriteAsync(package, "{}",
            Array.Empty<string>(),
            new[]
            {
                BoardPolicy.SceneFileName(id),
                BoardPolicy.BackupFileName(id),
                BoardPolicy.SceneFileName(id) + BoardPolicy.SceneTempSuffix,
                "not-a-scene.txt",
            }, CancellationToken.None);

        Assert.Equal(1, boards);
        using var zip = ZipFile.OpenRead(package);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Equal(2, names.Count);        // 清单 + 那块板
        Assert.Contains(BackupContainer.ManifestEntryName, names);
        Assert.Contains(BackupContainer.BoardsPrefix + BoardPolicy.SceneFileName(id), names);
    }

    /// <summary>报出去的块数说的是<b>包里到底有什么</b>：计划要带、当场读不出的那颗不许算进去。</summary>
    [Fact]
    public async Task CountReportedComesFromThePackageNotFromThePlan()
    {
        var here = "261030-071700-d4d4";
        var gone = "261030-071701-e5e5";
        PutScene(here);
        // 那颗档压根没造（正被删掉／同步工具抽走的形状）
        var package = PackagePath("partial.zip");
        var (_, _, boards, _) = await BackupContainer.WriteAsync(package, "{}", Array.Empty<string>(),
            new[] { BoardPolicy.SceneFileName(here), BoardPolicy.SceneFileName(gone) }, CancellationToken.None);

        Assert.Equal(1, boards);
        Assert.Equal(1, BackupContainer.CountBoardEntries(package));
    }

    // ─────────── ② 还原：三档各归各的箱 ───────────

    [Fact]
    public async Task RestoreWritesBackAMissingBoard_AndLeavesTheOneAlreadyHereAlone()
    {
        var mine = "261030-071800-f6f6";
        var theirs = "261030-071801-a7a7";
        PutScene(theirs);                                   // 本机这块板是"今天刚画的"
        var onDisk = File.ReadAllBytes(BoardPolicy.ScenePath(theirs));

        // 包里两块的内容⛔ 同：写回来的那颗必须一眼看得出是包里那份而不是本机那份
        var package = PackagePath("restore.zip");
        var oldDoc = System.Text.Encoding.UTF8.GetBytes(
            """{"type":"excalidraw","version":2,"elements":[{"id":"t","type":"text","isDeleted":false,"originalText":"包里那一份"}],"appState":{},"files":{}}""");
        using (var fs = new FileStream(package, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var m = zip.CreateEntry(BackupContainer.ManifestEntryName);
            using (var w = new StreamWriter(m.Open())) w.Write("{}");
            foreach (var id in new[] { mine, theirs })
            {
                var e = zip.CreateEntry(BackupContainer.BoardsPrefix + BoardPolicy.SceneFileName(id));
                using var s = e.Open();
                await s.WriteAsync(oldDoc);
            }
        }

        var (written, skipped, failed, carried, opened) =
            await BackupContainer.ExtractBoardsAsync(package, CancellationToken.None);
        Assert.True(opened);
        Assert.Equal(2, carried);
        Assert.Equal(1, written);
        Assert.Equal(1, skipped);
        Assert.Equal(0, failed);

        // ① 本机没有的那块回来了，且内容＝包里那份
        var restored = BoardPolicy.ScenePath(mine);
        _mine.Add(restored);
        Assert.True(File.Exists(restored));
        Assert.Equal(oldDoc, File.ReadAllBytes(restored));
        // ② 本机已有的那块⛔ 动（一个字节也不）
        Assert.Equal(onDisk, File.ReadAllBytes(BoardPolicy.ScenePath(theirs)));
        // ③ 那颗临时件⛔ 留在盘上（"先 .tmp 再改名"这条原子性如果漏了，盘上会多一颗废件）
        Assert.False(File.Exists(restored + BoardPolicy.SceneTempSuffix));
    }

    /// <summary>
    /// 包里的内容是<b>外部输入</b>：一颗档名过不了形状判据、一颗体积超限、一颗内容⛔ 像档，三颗都不许写回盘。
    /// 这一颗证的是"还原不会把用户目录当成谁都能写的一棵树"。
    /// </summary>
    [Fact]
    public async Task RestoreRefusesNamesThatAreNotSceneFilesAndBodiesThatAreNotDocs()
    {
        var good = "261030-071900-b8b8";
        var badName = "26-10-30 半夜.excalidraw.bak";        // 那颗退路的形状：⛔ 算一块板
        var weirdName = "..\\escape.excalidraw";             // 带目录的形式（前缀之外一律不认）
        var halfDoc = System.Text.Encoding.UTF8.GetBytes("""{"type":"excalidraw","elements":[""");

        var package = PackagePath("hostile.zip");
        using (var fs = new FileStream(package, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var m = zip.CreateEntry(BackupContainer.ManifestEntryName);
            using (var w = new StreamWriter(m.Open())) w.Write("{}");
            void Add(string name, byte[] body)
            {
                var e = zip.CreateEntry(BackupContainer.BoardsPrefix + name);
                using var s = e.Open();
                s.Write(body);
            }
            Add(BoardPolicy.SceneFileName(good), SceneDoc);
            Add(badName, SceneDoc);
            Add(weirdName, SceneDoc);
            Add("261030-071901-c9c9.excalidraw", halfDoc);   // 名字合规、内容半颗
        }

        var (written, skipped, failed, carried, _) =
            await BackupContainer.ExtractBoardsAsync(package, CancellationToken.None);
        Assert.Equal(4, carried);
        Assert.Equal(1, written);
        Assert.Equal(3, skipped);
        Assert.Equal(0, failed);      // "不认"与"写不进"必须分箱，否则一句"已恢复"把拒绝说成故障

        _mine.Add(BoardPolicy.ScenePath(good));
        Assert.True(File.Exists(BoardPolicy.ScenePath(good)));
        Assert.False(File.Exists(Path.Combine(BoardPolicy.ScenesFolder(), badName)));
        Assert.False(File.Exists(Path.Combine(BoardPolicy.ScenesFolder(), weirdName)));
        Assert.False(File.Exists(BoardPolicy.ScenePath("261030-071901-c9c9")));
    }

    /// <summary>两族前缀互不吃到：只带图的包对画板那一族报 0，反之亦然（并⛔ 是"包打不开"那一档）。</summary>
    [Fact]
    public async Task TheTwoAttachmentFamiliesNeverReadEachOther()
    {
        var id = "261030-072000-d0d0";
        PutScene(id);
        var boardsOnly = PackagePath("boards-only.zip");
        await BackupContainer.WriteAsync(boardsOnly, "{}", Array.Empty<string>(),
            new[] { BoardPolicy.SceneFileName(id) }, CancellationToken.None);

        Assert.Equal(1, BackupContainer.CountBoardEntries(boardsOnly));
        Assert.Equal(0, BackupContainer.CountClipEntries(boardsOnly));
        // 那块板得先从盘上没了，这一发才量得到"包里那块写回来了"（⛔ 改：本机已有＝skipped，另一格已经证过那一档）
        File.Delete(BoardPolicy.ScenePath(id));
        var (written, skipped, _, carried, opened) = await BackupContainer.ExtractBoardsAsync(boardsOnly, CancellationToken.None);
        Assert.True(opened);
        Assert.Equal(1, carried);
        Assert.Equal(0, skipped);
        Assert.Equal(1, written);

        // 反向：一颗 clip 条目都不该被画板那一族读成"包里有一块板"
        var clipsOnly = PackagePath("clips-only.zip");
        using (var fs = new FileStream(clipsOnly, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var m = zip.CreateEntry(BackupContainer.ManifestEntryName);
            using var w = new StreamWriter(m.Open());
            w.Write("{}");
        }
        var (_, _, _, carriedBack, openedBack) = await BackupContainer.ExtractBoardsAsync(clipsOnly, CancellationToken.None);
        Assert.True(openedBack);
        Assert.Equal(0, carriedBack);
        Assert.Equal(0, BackupContainer.CountBoardEntries(clipsOnly));
    }

    /// <summary><c>.json</c> 载体恒 0、⛔ 抛：老备份与"开关关着导出的那一份"都走这一档。</summary>
    [Fact]
    public void PlainManifestCarriesNoBoardsAndSaysSoWithoutThrowing()
    {
        var plain = PackagePath("plain.json");
        File.WriteAllText(plain, "{}");
        Assert.Equal(0, BackupContainer.CountBoardEntries(plain));
        var task = BackupContainer.ExtractBoardsAsync(plain, CancellationToken.None);
        Assert.True(task.Wait(2000));
        Assert.Equal(0, task.Result.Carried);
        Assert.True(task.Result.Opened);     // "没带"与"打不开"是两档，那颗布尔只管后者
    }

    // ─────────── ③ 接线与开关（⛔ 新键，复用"含图片"那一颗） ───────────

    [Fact]
    public void TheExportAndTheRestoreBothHangBoardsOnTheExistingImagesSwitch()
    {
        var svc = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Core/Backup/BackupService.cs"));
        var export = SourceGate.MethodBody(svc, "Task<BackupExport> ExportWithClipImagesAsync");
        Assert.Contains("PlanBoardScenes()", export, StringComparison.Ordinal);
        // 载体跟着内容走只问一次：两处各问一遍＝改一处漏一处（一个叫 .json 的 zip）
        Assert.Equal(1, SourceGate.Count(export, "AgreeExtensionWithContent"));
        Assert.Contains("var carries = names.Count > 0 || sceneNames.Count > 0;", export, StringComparison.Ordinal);
        Assert.DoesNotContain("carriesImages", svc, StringComparison.Ordinal);

        // §坑23 那句"别加新键"落到盘上：全库不许出现第二颗"带不带画板"的开关
        var offenders = SourceGate.ReadRepoUnder("src")
            .Where(f => f.Text.Contains("includeBoards", StringComparison.Ordinal)
                     || f.Text.Contains("carryBoards", StringComparison.Ordinal))
            .Select(f => f.RelativePath).ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void TheRestoreReportsThreeBoardCounts_AndTheSentencesCarryTheSlots()
    {
        var table = SourceGate.ReadRepoFile("src/StarMark.Abstractions/I18n/UiStrings.cs");
        // 外层句子多出来的那颗槽必须真有槽位（写了一半＝那一档的话永远念不出来）
        Assert.Contains("settings.data.backup.exportedWith\", \"已导出备份到：{0}（条目 {1} 条{2}{3}{4}{5}）",
            table, StringComparison.Ordinal);
        foreach (var head in new[] { "restoredMerge\", \"已合并恢复 {0} 条条目。{1}{2}{3}",
                                     "restoredReplace\", \"已覆盖恢复 {0} 条条目。{1}{2}{3}" })
            Assert.Contains(head, table, StringComparison.Ordinal);
        Assert.Contains("settings.data.backup.restoredBoards", table, StringComparison.Ordinal);
        Assert.Contains("settings.data.backup.restoredBoardsFailed", table, StringComparison.Ordinal);
        Assert.Contains("settings.data.backup.exportedBoards", table, StringComparison.Ordinal);
        Assert.Contains("settings.data.backup.carriesBoards", table, StringComparison.Ordinal);

        var core = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Core/Backup/BackupService.cs"));
        Assert.Contains("BoardsRestored = boards.Written", core, StringComparison.Ordinal);
        Assert.Contains("BoardsSkipped = boards.Skipped", core, StringComparison.Ordinal);
        Assert.Contains("BoardsFailed = boards.Failed", core, StringComparison.Ordinal);
    }

    [Fact]
    public void TheImportDialogSaysWhatIsInThePackage_AndReindexesTheBoardsThatCameBack()
    {
        var ui = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Views/SettingsPage.Backup.cs"));
        // 确认框那句"带几块板"读的是包（⛔ 清单条目数——后者会说"带了"而其实一块都没进包）
        Assert.Contains("BackupContainer.CountBoardEntries(source)", ui, StringComparison.Ordinal);
        Assert.Contains("settings.data.backup.carriesBoards", ui, StringComparison.Ordinal);
        Assert.Contains("settings.data.backup.exportedBoards", ui, StringComparison.Ordinal);
        // 导完就搜得到：⛔ 把"重启一次才进索引"留给用户当缺陷（P-53 那句"要说在用户能做的那件事上"的反面）
        Assert.Contains("rr.BoardsRestored > 0", ui, StringComparison.Ordinal);
        Assert.Contains("BoardSceneIndex.BackfillAsync", ui, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBoardsFolderHasOneOwnerAndTheUiStoreOnlyForwardsToIt()
    {
        var policy = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Core/Boards/BoardPolicy.cs"));
        Assert.Contains("""Subdirectory(BoardsDirectoryName)""", policy, StringComparison.Ordinal);
        // 落点这一颗⛔ 许在两处各拼一遍（片23 那句"根只许一位主人"的同一条线）
        var offenders = SourceGate.ReadRepoUnder("src")
            .Where(f => SourceGate.Code(f.Text).Contains("""Subdirectory("boards")""", StringComparison.Ordinal))
            .Select(f => f.RelativePath).ToList();
        Assert.Empty(offenders);
        var store = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Boards/BoardSceneStore.cs"));
        Assert.Contains("Folder => BoardPolicy.ScenesFolder()", store, StringComparison.Ordinal);
        Assert.Contains("PathOf(string sceneId) => BoardPolicy.ScenePath(sceneId)", store, StringComparison.Ordinal);
        Assert.Contains("TempSuffix = BoardPolicy.SceneTempSuffix", store, StringComparison.Ordinal);
    }
}
