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
    private readonly string _widgetsPath;

    public BackupServiceTests()
    {
        _db1 = Path.Combine(Path.GetTempPath(), $"starmark_bk1_{Guid.NewGuid():N}.db");
        _db2 = Path.Combine(Path.GetTempPath(), $"starmark_bk2_{Guid.NewGuid():N}.db");
        _snapshotDir = Path.Combine(Path.GetTempPath(), $"starmark_snap_{Guid.NewGuid():N}");
        _widgetsPath = Path.Combine(Path.GetTempPath(), $"starmark_wg_{Guid.NewGuid():N}.json");
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
        foreach (var suffix in new[] { "", ".bak", ".tmp" })
        {
            try { File.Delete(_widgetsPath + suffix); } catch { }
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
    public async Task Restore_PreservesCjkTagWordSearch()
    {
        // D5 回归：导出条目不带 Tags 集合，还原只补 item_tags 关联行不足以把标签词烘进 search_text。
        // 若不收尾重建，还原后按标签词（标题/笔记里都没有它）做中文全文检索会失灵。
        var src = Seed(_db1);
        var item = await UpsertAsync(src.Items, "test", "t1", "季度报告");   // 标题不含标签词
        await src.Items.AddTagAsync(item.Id, "重要资料", CancellationToken.None);
        // 前提：源库本身可按标签词搜到
        Assert.Contains((await src.Items.SearchAsync("重要", new SearchFilter { MaxResults = 20 }, CancellationToken.None)).Items,
                        i => i.Title == "季度报告");

        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        await src.Backup.ExportToFileAsync(file, CancellationToken.None);

        var dst = Seed(_db2);
        var env = await BackupService.ReadAsync(file, CancellationToken.None);
        var rr = await dst.Backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None);
        Assert.True(rr.Success);

        // 关键断言：还原后目标库仍能按标签词「重要」全文召回
        var hits = await dst.Items.SearchAsync("重要", new SearchFilter { MaxResults = 20 }, CancellationToken.None);
        Assert.Contains(hits.Items, i => i.Title == "季度报告");
    }

    [Fact]
    public async Task Restore_PreservesCjkNoteSearch_OnUntaggedItem()
    {
        // AE-2 回归：ImportUserStateAsync 用裸 UPDATE SET notes 改正文却不重算 search_text，
        // 旧的还原收尾只重建「带标签的行」→ 未打标签条目还原进来的笔记永远搜不到。
        // 全表重算后应能按笔记词召回。笔记词刻意与标题/标签都不同，确保命中的确是笔记贡献。
        var src = Seed(_db1);
        var item = await UpsertAsync(src.Items, "test", "n1", "普通标题");   // 不打标签
        await src.Items.SetNoteAsync(item.Id, "紫水晶收藏笔记", CancellationToken.None);
        // 前提：源库本身可按笔记词搜到
        Assert.Contains((await src.Items.SearchAsync("紫水晶", new SearchFilter { MaxResults = 20 }, CancellationToken.None)).Items,
                        i => i.Title == "普通标题");

        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        await src.Backup.ExportToFileAsync(file, CancellationToken.None);

        var dst = Seed(_db2);
        var env = await BackupService.ReadAsync(file, CancellationToken.None);
        var rr = await dst.Backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None);
        Assert.True(rr.Success);

        // 关键断言：还原后目标库仍能按笔记词「紫水晶」全文召回（未打标签行）
        var hits = await dst.Items.SearchAsync("紫水晶", new SearchFilter { MaxResults = 20 }, CancellationToken.None);
        Assert.Contains(hits.Items, i => i.Title == "普通标题");
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

    [Fact]
    public async Task Restore_WidgetsWriteFails_ReportsSuccessWithWarning()
    {
        // 回归 R1#2：备份含组件数据但组件写盘失败时，旧实现返回纯成功文案（调用方只看 Message、
        // 不单独看 WidgetsRestored），构成「假成功」。修复后 Success 仍 true（条目本体确还原），
        // 但 Message 必须点出组件恢复失败。
        var src = Seed(_db1);
        await UpsertAsync(src.Items, "test", "a1", "标题");
        var env = await src.Backup.ExportAsync(CancellationToken.None);
        env.Payload.WidgetsJson = "[]";   // 触发组件写盘分支

        // 让组件写盘必失败：目标父级指向一个「文件」→ Directory.CreateDirectory 抛异常。
        var blocker = Path.Combine(Path.GetTempPath(), $"blk_{Guid.NewGuid():N}.bin");
        await File.WriteAllTextAsync(blocker, "x");
        var badWidgetsPath = Path.Combine(blocker, "widgets.json");

        try
        {
            var dst = Seed(_db2);
            var rr = await dst.Backup.RestoreAsync(env, RestoreMode.Merge, badWidgetsPath, CancellationToken.None);

            Assert.True(rr.Success);
            Assert.False(rr.WidgetsRestored);
            Assert.Contains("组件", rr.Message);
            Assert.Contains("失败", rr.Message);
        }
        finally { try { File.Delete(blocker); } catch { } }
    }

    /// <summary>
    /// 回归 AT（备份破坏性顺序）：Replace 先清库再导入，若导入途中崩（缺业务键触发 NOT NULL / 半截事务），
    /// 已提交的清空不回滚 → 库被毁却只报「恢复失败」。校验和只保证字节完整、不保证载荷语义合法。
    /// 修后 RestoreAsync 在快照与清库之前先做语义校验并拒绝，现有数据原样保留、绝不清空。
    /// </summary>
    [Fact]
    public async Task Restore_Replace_BadBusinessKey_DoesNotWipeExistingItems()
    {
        var src = Seed(_db1);
        await UpsertAsync(src.Items, "src", "a1", "来自备份");
        var env = await src.Backup.ExportAsync(CancellationToken.None);
        // 追加一条缺业务键（Source 空白）的条目：直接改内存 env 走 RestoreAsync（不经 ReadAsync 校验和复查），
        // 模拟一份校验和自洽但载荷语义非法的构造备份。
        env.Payload.Items.Add(new Item { Source = "  ", SourceId = "x", Title = "坏条目", Uri = "https://evil" });

        var dst = Seed(_db2);
        await UpsertAsync(dst.Items, "dst", "b1", "现有条目");

        var rr = await dst.Backup.RestoreAsync(env, RestoreMode.Replace, null, CancellationToken.None);

        Assert.False(rr.Success);
        Assert.Contains("业务键", rr.Message);
        // 关键：拒绝发生在清库之前 → dst 原有条目仍在，未被抹掉（旧实现此处已清空、Assert.Empty）。
        var all = await dst.Items.GetAllAsync(new BrowseFilter { IncludeHidden = true, Limit = 1000 }, CancellationToken.None);
        var only = Assert.Single(all);
        Assert.Equal("dst", only.Source);
        Assert.Equal("b1", only.SourceId);
    }

    /// <summary>
    /// 回归 AU（组件写盘原子性）：WriteWidgetsJson 此前用裸 File.WriteAllText 覆盖线上 widgets.json，
    /// 是本仓**唯一**未走 tmp+move 的用户数据写站点（WidgetStorage.Save / SettingsStore.Save / GitHubOptions.Save 皆原子）。
    /// 写一半崩溃/磁盘满即把整份组件配置截断成非法 JSON，而 .bak 无任何代码自动回滚 → 下次启动整块组件全丢。
    /// 修后先写 .tmp 再 File.Move(overwrite)：还原成功的组件文件内容须逐字节忠实，且不得残留 .tmp。
    /// </summary>
    [Fact]
    public async Task Restore_WritesWidgetsJson_Atomically_AndFaithfully()
    {
        const string widgets = """{"Instances":[{"Kind":1}]}""";
        var src = Seed(_db1);
        await UpsertAsync(src.Items, "test", "a1", "标题");
        var env = await src.Backup.ExportAsync(CancellationToken.None);
        env.Payload.WidgetsJson = widgets;   // 触发组件写盘（成功）分支

        var dst = Seed(_db2);
        var rr = await dst.Backup.RestoreAsync(env, RestoreMode.Merge, _widgetsPath, CancellationToken.None);

        Assert.True(rr.Success);
        Assert.True(rr.WidgetsRestored);
        Assert.True(File.Exists(_widgetsPath));
        Assert.Equal(widgets, await File.ReadAllTextAsync(_widgetsPath));
        Assert.False(File.Exists(_widgetsPath + ".tmp"));   // 原子写不得留下半截临时文件
    }

    /// <summary>
    /// 契约护栏（AW）：跨库还原时，即使备份里的标签名与目标库既有标签仅大小写不同，链接仍应重挂到同一标签身份。
    /// tags.name 声明为 UNIQUE COLLATE NOCASE，SQLite 会把列自身的 collation 用于 `t.name = @tag` 比较，
    /// 故 ImportItemTagLinksAsync 的裸 '=' 本就大小写无关（已实测：去掉显式 COLLATE 仍通过）。此测锁死该不变式——
    /// 若未来 tags.name 被改成 BINARY，链接会静默丢失而 RestoreResult.LinksRestored 仍按载荷条数报 1，无人察觉。
    /// </summary>
    [Fact]
    public async Task Restore_Merge_ReattachesTagLinkAcrossDivergentCasing()
    {
        // 源库：条目打小写标签 "work"
        var src = Seed(_db1);
        var srcItem = await UpsertAsync(src.Items, "test", "x1", "跨机还原条目");
        await src.Items.AddTagAsync(srcItem.Id, "work", CancellationToken.None);
        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        await src.Backup.ExportToFileAsync(file, CancellationToken.None);

        // 目标库：另一条目先把同名标签以不同大小写 "Work" 落库（决定 tags 行的实际大小写）
        var dst = Seed(_db2);
        var seedItem = await UpsertAsync(dst.Items, "test", "u9", "目标库既有条目");
        await dst.Items.AddTagAsync(seedItem.Id, "Work", CancellationToken.None);

        var env = await BackupService.ReadAsync(file, CancellationToken.None);
        var rr = await dst.Backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None);
        Assert.True(rr.Success);

        // 关键：还原进来的条目必须重新挂上目标库里大小写不同的 "Work"（同一 NOCASE 标签身份），且不新增 "work" 行
        var restored = Assert.Single(
            await dst.Items.GetAllAsync(new BrowseFilter { IncludeHidden = true, Limit = 1000 }, CancellationToken.None),
            i => i.SourceId == "x1");
        var tags = await dst.Items.GetTagsForItemAsync(restored.Id, CancellationToken.None);
        Assert.Equal("Work", Assert.Single(tags));
    }
}
