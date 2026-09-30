#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Data;
using StarMark.Integrations.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 启动对账（§3-Q6 三分类）。分两层写：<see cref="ClipAssets.Reconcile"/> 是纯函数，逐格钉判据；
/// 另几条走真 <see cref="ItemRepository"/> + 真目录，钉的是"对账只允许写标记，一件文件都不许删、
/// 一格 extra 都不许抹"——那两条都是只有碰真盘才测得出来的。
/// </summary>
public sealed class ClipboardReconcileTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_recon_{Guid.NewGuid():N}.db");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"starmark_recon_{Guid.NewGuid():N}");
    private readonly ItemRepository _repo;

    public ClipboardReconcileTests()
    {
        var factory = new DbConnectionFactory(_db);
        new MigrationRunner(factory).EnsureSchema();
        _repo = new ItemRepository(factory);
        Directory.CreateDirectory(_dir);
        ClipAssets.FolderOverride = _dir;
    }

    public void Dispose()
    {
        ClipAssets.FolderOverride = null;
        foreach (var p in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(p); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static ClipboardEntry.ClipAssetRow Row(long id, string? main, string? thumb = null, bool flagged = false)
        => new(id, main, thumb, flagged);

    // ────────── 纯判据：四格 + 两类文件 ──────────

    [Fact]
    public void RowWithoutFile_IsTheOnlyClassThatGetsMarked()
    {
        var r = ClipAssets.Reconcile(new[] { Row(7, "a.png", "a_thumb.jpg") }, new[] { "a_thumb.jpg" });
        Assert.Equal(new[] { 7L }, r.MissingRowIds);              // 主图不在＝打不开＝缺失（缩略图缺不在这格）
        Assert.Empty(r.RestoredRowIds);
        Assert.Empty(r.OrphanNames);
    }

    [Theory]
    [InlineData(true, true, true, false)]    // 文件在 + 已标着 ⇒ 清标记（用户把文件拷回来了）
    [InlineData(true, false, false, false)]  // 文件在 + 没标 ⇒ 什么都不做
    [InlineData(false, true, false, false)]  // 文件没 + 已标着 ⇒ 也不重复标（幂等，不然每次启动重写全表）
    [InlineData(false, false, false, true)]  // 文件没 + 没标 ⇒ 打标记
    public void TheFourCellsOfMissingTimesFlag(bool fileHere, bool flagged, bool expectRestore, bool expectMark)
    {
        var r = ClipAssets.Reconcile(new[] { Row(1, "a.png", flagged: flagged) }, fileHere ? new[] { "a.png" } : Array.Empty<string>());
        Assert.Equal(expectRestore, r.RestoredRowIds.Count == 1);
        Assert.Equal(expectMark, r.MissingRowIds.Count == 1);
    }

    [Fact]
    public void MissingThumbnailAloneIsNotAMissingRow()
    {
        // 缩略图缺了只是列表要回退解码主图（贵一点），条目照样打得开——标成"文件缺失"就是谎报。
        var r = ClipAssets.Reconcile(new[] { Row(3, "a.png", "a_thumb.jpg") }, new[] { "a.png" });
        Assert.Empty(r.MissingRowIds);
        Assert.Empty(r.OrphanNames);                       // 缩略图名仍被那行认领着，不算孤儿
    }

    [Fact]
    public void ComparisonIgnoresCaseBecauseNtfsDoes()
    {
        // 名字是我们写出去的（哈希段可能大写），目录里回来的是什么大小写不由我们保证。
        // 按序数敏感比就会把"其实在"的图判成缺失——用户看到的是"我的图打不开了"。
        var r = ClipAssets.Reconcile(new[] { Row(9, "2026-09-28_0915_AB12CD34.PNG") },
            new[] { "2026-09-28_0915_ab12cd34.png" });
        Assert.Empty(r.MissingRowIds);
        Assert.Empty(r.OrphanNames);
    }

    [Fact]
    public void TempFilesAreTheirOwnClass_NotOrphans()
    {
        var r = ClipAssets.Reconcile(Array.Empty<ClipboardEntry.ClipAssetRow>(),
            new[] { "x.png.tmp", "y_thumb.jpg.tmp", "z.png" });
        Assert.Equal(new[] { "z.png" }, r.OrphanNames);     // 只有真正落过盘的件才算孤儿
        Assert.Equal(2, r.TempNames.Count);
    }

    [Fact]
    public void TwoRowsClaimingTheSameFile_LeaveNoOrphan()
    {
        // 同图回放会保住旧名字（1b），于是两行共享一对文件是常态；此时删掉一行不该让文件变孤儿。
        var r = ClipAssets.Reconcile(new[] { Row(1, "a.png", "a_thumb.jpg"), Row(2, "a.png", "a_thumb.jpg") },
            new[] { "a.png", "a_thumb.jpg" });
        Assert.Empty(r.OrphanNames);
        Assert.True(r.NothingToDo);
    }

    [Fact]
    public void BlankMainIsIgnoredInsteadOfCrashing()
    {
        var r = ClipAssets.Reconcile(new[] { Row(1, null), Row(2, "  ") }, new[] { "a.png" });
        Assert.Empty(r.MissingRowIds);
        Assert.Equal(new[] { "a.png" }, r.OrphanNames);
    }

    // ────────── 走真盘：对账之后磁盘与库里剩什么 ──────────

    private async Task<Item> RecText(string text)
        => await _repo.RecordClipboardAsync(ClipboardEntry.Build(text, "Code", ClipboardEntry.FormatText,
            new DateTimeOffset(2026, 9, 28, 9, 16, 0, TimeSpan.FromHours(8))));

    private async Task<Item> RecImage(string tag, int at = 0)
    {
        var sid = ClipboardPolicy.BuildImageSourceId(System.Text.Encoding.UTF8.GetBytes(tag));
        var when = new DateTimeOffset(2026, 9, 28, 9, 15, 0, TimeSpan.FromHours(8)).AddSeconds(at);
        var meta = new ClipboardEntry.ImageMeta(
            ClipAssets.MainNameOf(sid, when), ClipAssets.ThumbNameOf(sid, when), 800, 600, 4321);
        var item = await _repo.RecordClipboardAsync(ClipboardEntry.BuildImage(sid, meta, "Code", when));
        foreach (var n in new[] { ClipboardEntry.FileName(item), ClipboardEntry.ThumbFileName(item) })
            File.WriteAllText(Path.Combine(_dir, n!), "bytes");
        return item;
    }

    [Fact]
    public async Task HandDeletedFile_BecomesMissingFlag_AndTheRowSurvives()
    {
        var item = await RecImage("被手删的那张");
        File.Delete(Path.Combine(_dir, ClipboardEntry.FileName(item)!));

        var r = await Audit();

        Assert.Single(r.MissingRowIds);
        var reloaded = await _repo.GetBySourceAsync(ItemSources.Clipboard, ItemType.Clipboard);
        var row = Assert.Single(reloaded);                                  // 条目必须还在：文件不见了≠这条历史没价值
        Assert.True(ClipboardEntry.IsMissing(row));
        Assert.Equal(item.Id, row.Id);
    }

    [Fact]
    public async Task FilePutBackAgain_ClearsTheStaleFlag()
    {
        var item = await RecImage("拷回来的那张");
        var main = ClipboardEntry.FileName(item)!;
        File.Delete(Path.Combine(_dir, main));
        await ClipboardAssetAudit.RunAsync(_repo, CancellationToken.None);   // 先标上
        File.WriteAllText(Path.Combine(_dir, main), "bytes");                // 用户又放回来

        await ClipboardAssetAudit.RunAsync(_repo, CancellationToken.None);

        var row = Assert.Single(await _repo.GetBySourceAsync(ItemSources.Clipboard, ItemType.Clipboard));
        Assert.False(ClipboardEntry.IsMissing(row));                         // 挂着半句假话比没说过更糟
    }

    [Fact]
    public async Task OrphanFile_IsCountedButSurvives()
    {
        await RecImage("有主的一张");
        var orphan = Path.Combine(_dir, "someone_elses.png");
        File.WriteAllText(orphan, "用户自己拷进来的");

        var r = await Audit();

        Assert.Contains("someone_elses.png", r.OrphanNames);
        Assert.True(File.Exists(orphan));        // ★ 只数不删：清理得用户点，P1 连那颗按钮都不放（见闸门）
    }

    [Fact]
    public async Task SecondRunChangesNothing()
    {
        var item = await RecImage("幂等的那张");
        File.Delete(Path.Combine(_dir, ClipboardEntry.FileName(item)!));
        await ClipboardAssetAudit.RunAsync(_repo, CancellationToken.None);

        var again = await Audit();

        Assert.Empty(again.MissingRowIds);                                   // 已标过的不再标
        Assert.Empty(again.RestoredRowIds);
    }

    /// <summary>跑一次对账。<b>null＝没跑成，测试要在这里就炸得可见</b>，而不是在下一行断言里报个莫名 NullReference。</summary>
    private async Task<ClipAssets.ReconcileResult> Audit()
        => await ClipboardAssetAudit.RunAsync(_repo, CancellationToken.None)
           ?? throw new InvalidOperationException("对账没跑成（返回 null＝出错），见日志");

    [Fact]
    public async Task OneCorruptRowDoesNotBreakRecording_Reconcile_OrItsOwnExtra()
    {
        // 这条测的是 ClipIMG-1e 自己踩出来的那个坑：json_extract 遇到不合法 JSON <b>抛错</b>而不是给 NULL，
        // 所以一行被手改过的 extra 能让"分桶"这件事整条语句失败——症状是"剪贴板历史再也不记东西了"，
        // 而且没有任何一行日志指向那一行坏数据。判据现在用 json_valid 兜住，三头都要验：
        //   ① 记新的历史照旧成功（轮转语句没被炸）；② 对账照旧跑完并把该标的标上；③ 坏那行的 extra 一个字符都不动。
        var good = await RecImage("好的一张", at: 0);
        var bad = await RecImage("坏的一张", at: 10);
        Execute("UPDATE items SET extra_json = '{' WHERE id = @id", bad.Id);
        File.Delete(Path.Combine(_dir, ClipboardEntry.FileName(good)!));
        File.Delete(Path.Combine(_dir, ClipboardEntry.FileName(bad)!));

        var later = await RecText("坏行存在时仍然记上了的一段文字");
        var r = await Audit();

        Assert.Contains(r.MissingRowIds, id => id == good.Id);          // 好行照常标缺失
        var reloaded = await _repo.GetBySourceAsync(ItemSources.Clipboard, ItemType.Clipboard);
        Assert.Contains(later.Id, reloaded.Select(i => i.Id));           // 采集没被那一行坏数据卡死
        Assert.Equal(3, reloaded.Count);                                 // 三行都还在：对账不删行
        Assert.Equal("{", Scalar("SELECT extra_json FROM items WHERE id = @id", bad.Id));
    }

    private void Execute(string sql, long id)
    {
        using var conn = new DbConnectionFactory(_db).Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private string Scalar(string sql, long id)
    {
        using var conn = new DbConnectionFactory(_db).Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);
        return (string)(cmd.ExecuteScalar() ?? string.Empty);
    }

    // ────────── 闸门：三条"只有源码能证"的规矩 ──────────

    [Fact]
    public void TheAssetQueryHasNoRowWindow()
    {
        // 借一个带 limit 的查询来对账，窗口外的行会被当成"没人认领的文件"⇒ 完好的目录被报成一堆孤儿。
        var body = MethodBodyOf("ClipAssetRow>> GetClipboardImageAssetsAsync");
        Assert.DoesNotContain("LIMIT", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetBySourceAsync", body);
    }

    [Fact]
    public void ReconcileNeverDeletesAnything()
    {
        var body = MethodBodyOf("RunAsync");
        foreach (var banned in new[] { "File.Delete", "TryDelete", "Directory.Delete", "ClipAssets.FullPathOf" })
            Assert.DoesNotContain(banned, body);
    }

    [Fact]
    public void FlagWritesGuardAgainstInvalidJson()
    {
        var body = MethodBodyOf("private async Task<int> ApplyMissingFlagAsync");
        Assert.Contains("json_valid(extra_json)", body);
        Assert.Contains("json_remove(extra_json, '$.clipMissing')", body);
        // 幂等判据问的是那颗守卫（SK 起措辞只有一份），不再是裸 extra_json。
        Assert.Contains("COALESCE(json_extract({ExtraJsonGuard.Safe(\"extra_json\")}, '$.clipMissing'), 0) <> ", body);   // 幂等
    }

    [Fact]
    public void BucketClauseSurvivesMalformedExtraJson()
    {
        // 判据本体必须带 json_valid：sqlite 的 json_extract 遇到不合法 JSON 是<b>抛错</b>，
        // 一行坏数据就会让"记一条历史"这条日常路径整个失败（这比少记一条严重得多）。
        // 批次 SK 起，<b>守卫的措辞</b>不再抄在各条 SQL 里，而是住在 ExtraJsonGuard（全仓唯一一份，
        // 由 ExtraJsonGuardGateTests 钉住）；这里钉的是"分桶那一句真的问了它"——
        // 只读磁盘源码，不依赖运行时拼接，所以"以后有人把这一句改回裸列"会当场红。
        var repo = ReadRepoFile("src/StarMark.Data/ItemRepository.Local.cs");
        Assert.Contains("json_extract({ExtraJsonGuard.Safe(\"extra_json\")}, ", repo);
        Assert.Equal(1, StarMark.Tests.SourceGate.Count(repo, "private static readonly string ClipBucketClause"));
    }

    [Fact]
    public void StartupAuditStillDeletesNothing()
    {
        // 判据从 1e 的"整界面不许出现清理孤儿"换成 3a 的"启动那一条一件都不删"：
        // §3-Q6 要的是"只数不删 + 给一个要先确认的入口"，两件事分别钉在两处（入口那一处见
        // ClipboardOrphanCleanupGateTests）。把上一轮那条禁词留着，就会把决议明列的入口判成违例。
        var watcher = ReadRepoFile("src/StarMark.Integrations/Clipboard/ClipboardWatcher.cs");
        var run = MethodBodyOf("public static async Task<ClipAssets.ReconcileResult?> RunAsync(");
        Assert.DoesNotContain("TryDelete", run);
        Assert.DoesNotContain("CleanAsync(", run);
        Assert.Contains("启动只数不删", run);
        // 而"计数与体积"这件事仍只有一份出处：孤儿口径不许在 UI 再算一遍。
        Assert.Equal(1, SourceGate.Count(watcher, "ClipAssets.Reconcile(rows"));
    }

    [Fact]
    public void AuditRunsAtStartup_AndNotBehindASwitch()
    {
        // §4：启动对账默认开、不设关——安全护栏留一个"可以关掉不看"的口子就没护栏了。
        var app = ReadRepoFile("src/StarMark.UI/App.xaml.cs");
        Assert.Contains("ClipboardAssetAudit", app);
        Assert.DoesNotContain("ReconcileEnabled", app);
    }

    private static string MethodBodyOf(string fragment)
    {
        foreach (var file in new[]
        {
            "src/StarMark.Data/ItemRepository.Local.cs",
            "src/StarMark.Integrations/Clipboard/ClipboardWatcher.cs",
        })
        {
            var source = ReadRepoFile(file);
            if (!source.Contains(fragment, StringComparison.Ordinal)) continue;
            return StarMark.Tests.SourceGate.MethodBody(source, fragment);
        }
        throw new InvalidOperationException($"闸门锚点没命中任何文件：{fragment}");
    }

    private static string ReadRepoFile(string relative) => StarMark.Tests.SourceGate.ReadRepoFile(relative);
}
