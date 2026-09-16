#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Core.Backup;
using StarMark.Data;

namespace StarMark.Tests;

/// <summary>
/// 备份与恢复（P0-2）的核心契约：
/// 1) 导出→导入在标准库之间完整往返（条目 / 用户元数据 / 标签 / 关联）；
/// 2) 合并不丢现有条目、覆盖清空后仅留备份内容；
/// 3) 校验和前置——损坏或被篡改直接拒绝。
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    private readonly string _db1;
    private readonly string _db2;
    private readonly string _snapshotDir;

    public BackupServiceTests()
    {
        _db1 = Path.Combine(Path.GetTempPath(), $"starmark_bk1_{Guid.NewGuid():N}.db");
        _db2 = Path.Combine(Path.GetTempPath(), $"starmark_bk2_{Guid.NewGuid():N}.db");
        _snapshotDir = Path.Combine(Path.GetTempPath(), $"starmark_snap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_snapshotDir);
        // 把恢复前快照重定向到临时目录，避免污染真实 %LOCALAPPDATA%
        BackupService.SnapshotDirectoryOverride = _snapshotDir;
    }

    public void Dispose()
    {
        BackupService.SnapshotDirectoryOverride = null;
        foreach (var p in new[] { _db1, _db2 })
        {
            try { File.Delete(p); } catch { }
            try { File.Delete(p + "-wal"); } catch { }
            try { File.Delete(p + "-shm"); } catch { }
        }
        try { Directory.Delete(_snapshotDir, recursive: true); } catch { }
    }

    private sealed record Ctx(DbConnectionFactory Factory, ItemRepository Items, BackupService Backup);

    private static Ctx Seed(string dbPath)
    {
        var factory = new DbConnectionFactory(dbPath);
        new MigrationRunner(factory).EnsureSchema();
        var items = new ItemRepository(factory);
        var backup = new BackupService(new BackupRepository(factory));
        return new Ctx(factory, items, backup);
    }

    private static async Task<Item> UpsertAsync(ItemRepository repo, string source, string sourceId, string title)
    {
        var item = new Item
        {
            Type = ItemType.Bookmark,
            Source = source,
            SourceId = sourceId,
            Title = title,
            Uri = "https://example.com/" + sourceId,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);
        return item;
    }

    [Fact]
    public async Task ExportAndRestore_RoundTripsItemsUserStateTagsAndLinks()
    {
        var src = Seed(_db1);
        var item = await UpsertAsync(src.Items, "test", "a1", "笔记标题");
        await src.Items.SetNoteAsync(item.Id, "我的笔记", CancellationToken.None);
        await src.Items.SetPinnedAsync(item.Id, true, CancellationToken.None);
        await src.Items.AddTagAsync(item.Id, "重要", CancellationToken.None);

        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        await src.Backup.ExportToFileAsync(file, CancellationToken.None);

        // 恢复到全新空库：行 id 会变，必须靠 (source, source_id) 关联
        var dst = Seed(_db2);
        var env = await BackupService.ReadAsync(file, CancellationToken.None);
        var rr = await dst.Backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None);

        Assert.True(rr.Success);
        Assert.Equal(1, rr.ItemsRestored);
        Assert.Equal(1, rr.UserStatesRestored); // 隐藏/置顶/笔记
        Assert.Equal(1, rr.TagsRestored);
        Assert.Equal(1, rr.LinksRestored);

        var all = await dst.Items.GetAllAsync(new BrowseFilter { IncludeHidden = true, Limit = 1000 }, CancellationToken.None);
        var restored = Assert.Single(all);
        Assert.Equal("我的笔记", restored.Notes);
        Assert.True(restored.Pinned);
        var tags = await dst.Items.GetTagsForItemAsync(restored.Id, CancellationToken.None);
        Assert.Contains("重要", tags);
    }

    [Fact]
    public async Task Restore_Replace_ClearsExistingItems()
    {
        var src = Seed(_db1);
        await UpsertAsync(src.Items, "src", "a1", "来自备份");

        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        await src.Backup.ExportToFileAsync(file, CancellationToken.None);

        var dst = Seed(_db2);
        await UpsertAsync(dst.Items, "dst", "b1", "现有条目");

        var env = await BackupService.ReadAsync(file, CancellationToken.None);
        var rr = await dst.Backup.RestoreAsync(env, RestoreMode.Replace, null, CancellationToken.None);

        Assert.True(rr.Success);
        var all = await dst.Items.GetAllAsync(new BrowseFilter { IncludeHidden = true, Limit = 1000 }, CancellationToken.None);
        var only = Assert.Single(all);
        Assert.Equal("src", only.Source);
        Assert.Equal("a1", only.SourceId);
    }

    [Fact]
    public async Task Restore_Merge_KeepsExistingItems()
    {
        var src = Seed(_db1);
        await UpsertAsync(src.Items, "src", "a1", "来自备份");

        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        await src.Backup.ExportToFileAsync(file, CancellationToken.None);

        var dst = Seed(_db2);
        await UpsertAsync(dst.Items, "dst", "b1", "现有条目");

        var env = await BackupService.ReadAsync(file, CancellationToken.None);
        var rr = await dst.Backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None);

        Assert.True(rr.Success);
        var all = await dst.Items.GetAllAsync(new BrowseFilter { IncludeHidden = true, Limit = 1000 }, CancellationToken.None);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, i => i.Source == "src" && i.SourceId == "a1");
        Assert.Contains(all, i => i.Source == "dst" && i.SourceId == "b1");
    }

    [Fact]
    public async Task ReadAsync_RejectsTamperedPayload()
    {
        var src = Seed(_db1);
        await UpsertAsync(src.Items, "test", "a1", "原始标题");

        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        await src.Backup.ExportToFileAsync(file, CancellationToken.None);

        // 篡改载荷但保留原校验和——读入时必须被拒绝
        var text = await File.ReadAllTextAsync(file);
        text = text.Replace("原始标题", "篡改标题");
        await File.WriteAllTextAsync(file, text);

        await Assert.ThrowsAsync<BackupFormatException>(
            () => BackupService.ReadAsync(file, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_RejectsWrongAppId()
    {
        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file,
            "{\"app\":\"some-other-app\",\"version\":1,\"exportedAt\":0,\"checksum\":\"\",\"payload\":{}}");

        await Assert.ThrowsAsync<BackupFormatException>(
            () => BackupService.ReadAsync(file, CancellationToken.None));
    }

    [Fact]
    public async Task Peek_ReturnsSummaryForValidFile()
    {
        var src = Seed(_db1);
        var item = await UpsertAsync(src.Items, "test", "a1", "标题");
        await src.Items.AddTagAsync(item.Id, "标签X", CancellationToken.None);

        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        await src.Backup.ExportToFileAsync(file, CancellationToken.None);

        var summary = BackupService.Peek(file);
        Assert.NotNull(summary);
        Assert.Equal(1, summary!.ItemCount);
        Assert.Equal(1, summary.TagCount);
    }

    [Fact]
    public async Task Restore_AlwaysWritesPreRestoreSnapshot()
    {
        var src = Seed(_db1);
        await UpsertAsync(src.Items, "test", "a1", "标题");
        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        await src.Backup.ExportToFileAsync(file, CancellationToken.None);

        var dst = Seed(_db2);
        var env = await BackupService.ReadAsync(file, CancellationToken.None);
        var rr = await dst.Backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None);

        Assert.True(rr.Success);
        Assert.NotNull(rr.SnapshotPath);
        Assert.True(File.Exists(rr.SnapshotPath));
        Assert.StartsWith(_snapshotDir, rr.SnapshotPath);
    }
}
