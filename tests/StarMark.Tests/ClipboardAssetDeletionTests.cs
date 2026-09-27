#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Abstractions.Clipboard;
using StarMark.Core.Backup;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 仓储层的<b>文件侧</b>：删一条图片历史＝同生同死的两张文件也要没（§3-Q5/Q6），
/// 而轮转的图片上限与文本上限必须各裁各的桶（§4）。
/// <para>
/// 这些用例走真 <see cref="ItemRepository"/> + 真临时目录（<see cref="ClipAssets.FolderOverride"/>），
/// 因为"删了行、文件留着"这件事**只有碰真文件系统才验得出来**——把它写成"返回该删哪些名字"
/// 再在内存里断言，就等于把最容易坏的那一段（拼路径、校验名字、提交前后顺序）留在了没测的地方。
/// </para>
/// <para>不测编码、不测采集（那是 1b/1a 的文件）。这里只管"行没了之后磁盘上该剩什么"。</para>
/// </summary>
public sealed class ClipboardAssetDeletionTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_clipdel_{Guid.NewGuid():N}.db");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"starmark_clipdel_{Guid.NewGuid():N}");
    private readonly string _other = Path.Combine(Path.GetTempPath(), $"starmark_clipdel_other_{Guid.NewGuid():N}");
    private readonly ItemRepository _repo;

    public ClipboardAssetDeletionTests()
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
        foreach (var d in new[] { _dir, _other })
            try { Directory.Delete(d, true); } catch { }
    }

    // ────────── 助手 ──────────

    /// <summary>基准时刻（写死，不取 UtcNow：文件名到分，测试必须能预知名字）。</summary>
    private static DateTimeOffset At(int seconds)
        => new DateTimeOffset(2026, 9, 28, 9, 15, 0, TimeSpan.FromHours(8)).AddSeconds(seconds);

    private static string Sid(string tag) => ClipboardPolicy.BuildImageSourceId(Encoding.UTF8.GetBytes(tag));

    /// <summary>记一条图片历史，并按行记着的名字把两张文件摆到磁盘上（模拟采集已成功写盘）。</summary>
    private async Task<Item> RecImage(string tag, int imageMax = 200, int at = 0)
    {
        var sid = Sid(tag);
        var when = At(at);
        var meta = new ClipboardEntry.ImageMeta(
            ClipAssets.MainNameOf(sid, when), ClipAssets.ThumbNameOf(sid, when), 800, 600, 4321);
        var item = await _repo.RecordClipboardAsync(
            ClipboardEntry.BuildImage(sid, meta, "Code", when), CancellationToken.None, imageMaxEntries: imageMax);
        Touch(ClipboardEntry.FileName(item));
        Touch(ClipboardEntry.ThumbFileName(item));
        return item;
    }

    private async Task<Item> RecText(string text, int textMax = 500, int at = 0)
    {
        var draft = ClipboardEntry.Build(text, "Code", ClipboardEntry.FormatText, At(at));
        return await _repo.RecordClipboardAsync(draft, CancellationToken.None, maxEntries: textMax);
    }

    private static void Touch(string? name)
        => File.WriteAllText(Path.Combine(ClipAssets.Folder, name!), "bytes");

    private string[] Files() => Directory.Exists(_dir) ? Directory.GetFiles(_dir) : Array.Empty<string>();

    /// <summary>目录里的文件名（比对名字一律用它，别让 <c>string?</c> 把断言挤成另一重载）。</summary>
    private string[] Names() => Files().Select(f => Path.GetFileName(f)!).ToArray();

    private async Task<int> Rows() => (await _repo.GetBySourceAsync(ItemSources.Clipboard, ItemType.Clipboard)).Count;

    /// <summary>把某行的 extra_json 换成手改过的样子（备份文件是文本，用户能改——这正是 §3-Q6 把对账升级为"容忍外部篡改"的理由）。</summary>
    private void OverwriteExtra(long id, string extraJson)
    {
        using var conn = new DbConnectionFactory(_db).Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE items SET extra_json = @e WHERE id = @id;";
        cmd.Parameters.AddWithValue("@e", extraJson);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    // ────────── 删行必须带走两张文件（同生同死，双倍计数） ──────────

    [Fact]
    public async Task DeletingOneImageRow_TakesBothOfItsFiles()
    {
        var item = await RecImage("第一张图");
        Assert.Equal(2, Files().Length);                     // 主图 + 缩略图

        Assert.True(await _repo.DeleteClipboardEntryAsync(item.Id, CancellationToken.None));
        Assert.Empty(Files());
    }

    [Fact]
    public async Task DeletingOneImageRow_LeavesOtherRowsFilesAlone()
    {
        var a = await RecImage("留着的", at: 0);
        var b = await RecImage("要删的", at: 10);

        await _repo.DeleteClipboardEntryAsync(b.Id, CancellationToken.None);

        Assert.Equal(2, Files().Length);
        Assert.Contains(ClipboardEntry.FileName(a)!, Names(), StringComparer.Ordinal);
    }

    [Fact]
    public async Task DeletingATextRow_LeavesTheFolderUntouched()
    {
        var image = await RecImage("旁边有张图");
        var text = await RecText("一段普通文字");
        var before = Files().Length;

        Assert.True(await _repo.DeleteClipboardEntryAsync(text.Id, CancellationToken.None));
        Assert.Equal(before, Files().Length);                // 文本行没有文件可删，也不许牵连别人
        Assert.True(File.Exists(Path.Combine(ClipAssets.Folder, ClipboardEntry.FileName(image)!)));
    }

    [Fact]
    public async Task DeletingARowThatIsAlreadyGone_TouchesNoFiles()
    {
        var item = await RecImage("已经没人认领");
        await _repo.DeleteClipboardEntryAsync(item.Id, CancellationToken.None);
        var orphan = Path.Combine(ClipAssets.Folder, "orphan.png");
        File.WriteAllText(orphan, "用户拷进来的");

        // 0 行＝那条本来就不在。此时目录里的东西一律是"图有行无"，归对账数，不由这次点击顺手删。
        Assert.False(await _repo.DeleteClipboardEntryAsync(item.Id, CancellationToken.None));
        Assert.True(File.Exists(orphan));
    }

    // ────────── 清空：带走每一条图片行，但不清空目录 ──────────

    [Fact]
    public async Task ClearingHistory_TakesEveryImageRowsFiles_ButNotAnOrphan()
    {
        await RecImage("第一张", at: 0);
        var second = await RecImage("第二张", at: 10);
        await RecText("一段文字", at: 20);
        await _repo.SetPinnedAsync(second.Id, true, CancellationToken.None);
        File.WriteAllText(Path.Combine(ClipAssets.Folder, "user_copied_in.png"), "不是我们的条目");

        var deleted = await _repo.ClearClipboardHistoryAsync(CancellationToken.None);

        Assert.Equal(3, deleted);                            // 含置顶那一条
        Assert.Equal(new[] { "user_copied_in.png" }, Names());
    }

    // ────────── 轮转：图片与文本各裁各的桶 ──────────

    [Fact]
    public async Task Rotation_TakesFilesOfTheImagesItPruned()
    {
        var oldest = await RecImage("最旧的", imageMax: 2, at: 0);
        await RecImage("中间的", imageMax: 2, at: 10);
        await RecImage("最新的", imageMax: 2, at: 20);

        Assert.Equal(2, await Rows());
        Assert.Equal(4, Files().Length);                     // 留下的两条各两张
        Assert.DoesNotContain(ClipboardEntry.FileName(oldest)!, Names(), StringComparer.Ordinal);
    }

    [Fact]
    public async Task TextCapDoesNotPruneImageRows_NorTheirFiles()
    {
        var image = await RecImage("一张图", at: 0);
        await RecText("文字一", textMax: 1, at: 10);
        await RecText("文字二", textMax: 1, at: 20);

        // 文本只留 1 条，而图片那条不在它的桶里——混桶的话这里就是"复制了几段文字，截图没了"。
        Assert.Equal(2, await Rows());
        Assert.True(File.Exists(Path.Combine(ClipAssets.Folder, ClipboardEntry.FileName(image)!)));
    }

    [Fact]
    public async Task ImageCapDoesNotPruneTextRows()
    {
        await RecText("文字一", at: 0);
        await RecText("文字二", at: 10);
        await RecImage("一张图", imageMax: 1, at: 20);

        Assert.Equal(3, await Rows());                       // 图片只留 1 张，两条文字不动
        Assert.Equal(2, Files().Length);
    }

    [Fact]
    public async Task PinnedImageRowSurvivesRotation_WithItsFiles()
    {
        var pinned = await RecImage("钉住的", imageMax: 1, at: 0);
        await _repo.SetPinnedAsync(pinned.Id, true, CancellationToken.None);
        await RecImage("后来的一张", imageMax: 1, at: 10);

        Assert.Contains(pinned.Id, (await _repo.GetBySourceAsync(ItemSources.Clipboard, ItemType.Clipboard))
            .Select(i => i.Id));
        Assert.Equal(4, Files().Length);                     // 两条都还在 ⇒ 四张文件
    }

    // ────────── 另一条删除入口：按 (source, source_id) 删 ──────────

    [Fact]
    public async Task DeleteBySourceId_OnClipboardRow_TakesItsFiles()
    {
        var item = await RecImage("按键删的那张");
        await _repo.DeleteBySourceIdAsync(ItemSources.Clipboard, item.SourceId, CancellationToken.None);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task DeleteBySourceId_OnAnotherSource_NeverEntersTheClipFolder()
    {
        await RecImage("目录里张图");
        var before = Files().Length;

        // 别的来源（书签/Ditto/Star）与"复制过的一张图"没有关系：连目录都不该去碰。
        await _repo.DeleteBySourceIdAsync("test", "a1", CancellationToken.None);
        Assert.Equal(before, Files().Length);
    }

    // ────────── 名字来自手改过的备份时，不许按它删目录外的文件 ──────────

    [Fact]
    public async Task UnsafeNameInExtra_IsRefused_AndNothingOutsideTheFolderIsTouched()
    {
        Directory.CreateDirectory(_other);
        var victim = Path.Combine(_other, "victim.png");
        File.WriteAllText(victim, "目录外的文件");

        var item = await RecImage("名字被改过的那张");
        Assert.Equal(2, Files().Length);                     // 先让真文件存在，才谈得上"有没有被越界删掉"
        OverwriteExtra(item.Id, """{"clipFormat":"image","clipFile":"../victim.png","clipThumb":"..\..\victim.png"}""");

        Assert.True(await _repo.DeleteClipboardEntryAsync(item.Id, CancellationToken.None));

        Assert.True(File.Exists(victim));                    // 越界的名字一个都不许删出去
        Assert.Equal(2, Files().Length);                     // 而它原本那两张也没人再认领：留给对账数
    }

    [Fact]
    public void UnsafeNamesAreRefusedByTheOneFunctionThatBuildsPaths()
    {
        // 写侧与删侧共用 ClipAssets.FullPathOf——校验只写在一侧的话，另一侧就是敞开的。
        ClipAssets.FolderOverride = _dir;
        foreach (var bad in new[] { "../a.png", "..\\a.png", "C:\\windows\\a.png", "a/b.png", "sub\\a.png",
                                    "..a.png", string.Empty, "   ", "a.txt", new string('n', 121) + ".png" })
            Assert.Null(ClipAssets.FullPathOf(bad));
        Assert.NotNull(ClipAssets.FullPathOf("2026-09-28_0915_9f2a3b8c.png"));
        Assert.NotNull(ClipAssets.FullPathOf("2026-09-28_0915_9f2a3b8c_thumb.jpg"));
    }

    // ────────── 上限的写入口：坏值进不来 ──────────

    [Fact]
    public void WatcherCapsClampBeforeTheyReachRotation()
    {
        var w = new StarMark.Integrations.Clipboard.ClipboardWatcher(_repo);
        Assert.Equal(ClipboardPolicy.MaxEntries, w.TextMaxEntries);          // 默认＝现行为
        Assert.Equal(ClipboardPolicy.DefaultImageMaxEntries, w.ImageMaxEntries);

        w.ImageMaxEntries = 0;                                               // 坏值回默认，而不是"记一条删一条"
        Assert.Equal(ClipboardPolicy.DefaultImageMaxEntries, w.ImageMaxEntries);
        w.ImageMaxEntries = 5;
        Assert.Equal(ClipboardPolicy.MinEntries, w.ImageMaxEntries);
        w.ImageMaxEntries = 99_999;
        Assert.Equal(ClipboardPolicy.ImageMaxEntriesCeil, w.ImageMaxEntries);

        w.TextMaxEntries = -1;
        Assert.Equal(ClipboardPolicy.MaxEntries, w.TextMaxEntries);
        w.TextMaxEntries = 10_000;
        Assert.Equal(ClipboardPolicy.TextMaxEntriesCeil, w.TextMaxEntries);
    }

    // ────────── b 期备份：文案如实，恢复不删文件 ──────────

    [Fact]
    public async Task Restore_SaysTheBackupCarriesNoImageBodies()
    {
        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        var snap = Path.Combine(Path.GetTempPath(), $"snap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(snap);
        BackupService.SnapshotDirectoryOverride = snap;
        var db2 = Path.Combine(Path.GetTempPath(), $"starmark_clipdel2_{Guid.NewGuid():N}.db");
        try
        {
            await RecImage("要进备份的一张");
            await RecText("要进备份的一段");
            var export = new BackupService(new BackupRepository(new DbConnectionFactory(_db)));
            await export.ExportToFileAsync(file, CancellationToken.None);

            var f2 = new DbConnectionFactory(db2);
            new MigrationRunner(f2).EnsureSchema();
            var env = await BackupService.ReadAsync(file, CancellationToken.None);
            var rr = await new BackupService(new BackupRepository(f2))
                .RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None);

            Assert.True(rr.Success, rr.Message);
            Assert.Contains("1 条是剪贴板图片", rr.Message);
            Assert.Contains("不带图片本体", rr.Message);
            Assert.False(rr.Message.Contains("省空间", StringComparison.Ordinal),
                "洞 2：备份文案只许说\"打包\"，压缩收益不成立——a 期的词不许提前用掉");
        }
        finally
        {
            BackupService.SnapshotDirectoryOverride = null;
            foreach (var p in new[] { file, db2, db2 + "-wal", db2 + "-shm" })
                try { File.Delete(p); } catch { }
            try { Directory.Delete(snap, true); } catch { }
        }
    }

    [Fact]
    public async Task Restore_KeepsQuiet_WhenThereAreNoImages()
    {
        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        var snap = Path.Combine(Path.GetTempPath(), $"snap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(snap);
        BackupService.SnapshotDirectoryOverride = snap;
        var db2 = Path.Combine(Path.GetTempPath(), $"starmark_clipdel3_{Guid.NewGuid():N}.db");
        try
        {
            await RecText("只有文字的一份");
            await new BackupService(new BackupRepository(new DbConnectionFactory(_db)))
                .ExportToFileAsync(file, CancellationToken.None);

            var f2 = new DbConnectionFactory(db2);
            new MigrationRunner(f2).EnsureSchema();
            var rr = await new BackupService(new BackupRepository(f2)).RestoreAsync(
                await BackupService.ReadAsync(file, CancellationToken.None), RestoreMode.Merge, null, CancellationToken.None);

            Assert.True(rr.Success, rr.Message);
            Assert.DoesNotContain("剪贴板图片", rr.Message);        // 没有图片就别多嘴那半句
        }
        finally
        {
            BackupService.SnapshotDirectoryOverride = null;
            foreach (var p in new[] { file, db2, db2 + "-wal", db2 + "-shm" })
                try { File.Delete(p); } catch { }
            try { Directory.Delete(snap, true); } catch { }
        }
    }

    // ────────── 闸门：只有源码能证的三条纪律 ──────────

    [Fact]
    public void RotationBucketHasExactlyOneDefinition()
    {
        // 桶的判据只许一份：SQL 里那个值走参数（由 C# 递 ClipboardEntry.FormatImage 进去）。
        // 两处各写一个字面量'image'的话，将来统一叫法只会改到一处，症状是"图片按文本上限被裁掉"，
        // 而那要攒够几百条历史才第一次看得见。
        var repo = SourceGate.ReadRepoFile("src/StarMark.Data/ItemRepository.Local.cs");
        Assert.Contains("@image_format", repo);
        Assert.Contains("ClipboardEntry.FormatImage", repo);
        Assert.Contains("ClipboardEntry.IsImageOf", repo);
        Assert.DoesNotContain("'image'", repo);
        Assert.Equal(1, SourceGate.Count(repo, "private const string ClipBucketClause"));
        Assert.True(SourceGate.Count(repo, "ClipBucketClause") >= 4,
            "分桶判据被复制成了两份以上——它必须只有一处定义");
    }

    [Fact]
    public void FileNamesAreReadBeforeTheDelete_AndFilesGoOnlyAfterTheCommit()
    {
        var body = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.Data/ItemRepository.Local.cs"),
            "public async Task<Item> RecordClipboardAsync");
        var read = body.IndexOf("ReadPrunedClipExtrasAsync", StringComparison.Ordinal);
        var commit = body.IndexOf("tx.CommitAsync", StringComparison.Ordinal);
        var delete = body.IndexOf("TryDeleteClipFiles", StringComparison.Ordinal);
        Assert.True(read >= 0 && commit > read, "文件名没在 DELETE 之前读出来：裁掉的行再也没人知道该删什么");
        Assert.True(delete > commit, "删文件排在提交之前：提交失败回滚就成了\"行还在、文件没了\"");
    }

    [Fact]
    public void ImageCapIsHandedOverByNameNotByPosition()
    {
        var watcher = SourceGate.ReadRepoFile("src/StarMark.Integrations/Clipboard/ClipboardWatcher.cs");
        Assert.Contains("RecordClipboardAsync(draft, ct, imageMaxEntries:", watcher);
        Assert.Contains("maxEntries: textMaxEntries", watcher);
        // 位置参数写法（第三槽）会把它交给文本桶——不报错，只是裁错桶。
        Assert.DoesNotContain("RecordClipboardAsync(draft, ct, Clipboard", watcher);
    }

    [Fact]
    public void RestoreNeverDeletesClipFiles()
    {
        // §3-Q6 第二类：恢复/清库把图片行删掉时，那些 PNG 只能留着被数出来，不许被"顺手清理"。
        // 用户看得见那个目录，一次静默批量删除就是他再也解释不丢的锅。
        var repo = SourceGate.ReadRepoFile("src/StarMark.Data/BackupRepository.cs");
        Assert.DoesNotContain("ClipAssets", repo);
        Assert.DoesNotContain("File.Delete", repo);

        var service = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.Core/Backup/BackupService.cs"),
            "public async Task<RestoreResult> RestoreAsync");
        Assert.DoesNotContain("File.Delete", service);
        Assert.DoesNotContain("ClipAssets", service);
    }
}
