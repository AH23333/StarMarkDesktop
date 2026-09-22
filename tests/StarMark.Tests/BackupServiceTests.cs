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
    public async Task ImportItems_SlicesAcross200BatchBoundary()
    {
        // 钉死 BackupRepository.ImportItemsAsync 的 200 条分批（私有 Slice）在批边界处不重不漏：
        // 250 = 首批满 200 + 尾批 50。Slice 若少切/越界/尾批 off-by-one/负容量，本测以总数+跨边界抽样显形。
        // （Slice 的负容量路径在当前唯一调用方 for(i=0;i<items.Count;i+=batch) 下不可达——offset 恒 < Count、
        //  count 恒为常量 200 故 n∈[1,200]；本测固化该批边界契约以防将来改循环/批大小悄悄破坏切分。）
        var ctx = Seed(_db1);
        const int total = 250;
        var items = new System.Collections.Generic.List<Item>();
        for (int i = 1; i <= total; i++)
        {
            items.Add(new Item
            {
                Type = ItemType.Bookmark,
                Source = "test",
                SourceId = "bulk" + i,
                Title = "条" + i,
                Uri = "https://example.com/bulk" + i,
                CreatedAt = 1000 + i,
            });
        }

        var repo = new BackupRepository(ctx.Factory);
        await repo.ImportItemsAsync(items, CancellationToken.None);

        var all = await ctx.Items.GetAllAsync(
            new BrowseFilter { IncludeHidden = true, Limit = 1000 }, CancellationToken.None);
        Assert.Equal(total, all.Count);

        // 无重复：Slice 切分不得重漏（配合 items UNIQUE(source,source_id)）
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var it in all) Assert.True(seen.Add(it.SourceId), $"重复 source_id: {it.SourceId}");
        Assert.Contains("bulk200", seen); // 首批末
        Assert.Contains("bulk201", seen); // 次批首（跨 200 边界）
        Assert.Contains("bulk250", seen); // 尾批末（部分批的收尾）
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
    public async Task ReadAsync_RejectsFutureVersion()
    {
        // 契约护栏：前向兼容闸门（BackupService.ReadAsync:106）——高于当前支持版本的备份必须被拒，
        // 否则旧版会用自己不认识的 schema 半导入未来备份、静默丢弃其新增字段（比"直接失败"更坏）。
        // 版本校验(:106)在校验和校验(:110)之前，故用正确 app 段 + 空 checksum 即命中版本分支；
        // 再断言错误文案含"高于当前支持的"，以防该断言实际是被校验和不匹配那一支抛出的（那样等于没钉住版本闸门）。
        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file,
            $"{{\"app\":\"{BackupEnvelope.AppId}\",\"version\":{BackupEnvelope.CurrentVersion + 1},\"exportedAt\":0,\"checksum\":\"\",\"payload\":{{}}}}");

        var ex = await Assert.ThrowsAsync<BackupFormatException>(
            () => BackupService.ReadAsync(file, CancellationToken.None));
        Assert.Contains("高于当前支持的", ex.Message);
    }

    [Fact]
    public async Task Summarize_CountsMatchExportedPayload()
    {
        var src = Seed(_db1);
        var item = await UpsertAsync(src.Items, "test", "a1", "标题");
        await src.Items.AddTagAsync(item.Id, "标签X", CancellationToken.None);

        var file = Path.Combine(Path.GetTempPath(), $"bk_{Guid.NewGuid():N}.json");
        await src.Backup.ExportToFileAsync(file, CancellationToken.None);

        // 与 UI 同一条路径：先按校验过的 ReadAsync 解析，再由已解析的信封算摘要。
        // （旧 BackupService.Peek(path) 会为看个计数把整份备份再读盘+反序列化一次，已删。）
        var env = await BackupService.ReadAsync(file, CancellationToken.None);
        var summary = BackupService.Summarize(env);
        Assert.Equal(1, summary.ItemCount);
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
            Assert.Contains("未能写入", rr.Message);

            // P-54：失败原因必须一路带到状态栏。旧文案只写"请稍后重试或检查 %APPDATA% 是否被占用"，
            // 让用户去猜一个日志里早已写明的事——真正的错因（这里是往"文件"下建目录）被丢进了日志。
            const string marker = "（组件保持原状）：";
            Assert.Contains(marker, rr.Message);
            var reason = rr.Message[(rr.Message.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
            Assert.NotEmpty(reason.Trim());
            Assert.DoesNotContain("请稍后重试", rr.Message);
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
    /// 回归 BC-#3（RestoreAsync 契约）：方法约定"对非法载荷返回 RestoreResult 而非抛异常"，
    /// 但 ValidatePayload 在 try 之外被调用，且对 p.Items/UserState/Tags/ItemTags 直接 RemoveAll，
    /// 未判空。STJ 会把 JSON 显式 null 覆盖到 `= new()` 初始值之上，故"校验和自洽但集合为 null"
    /// （甚至 payload 整体为 null）的构造备份会以 NullReferenceException 逃出 RestoreAsync，
    /// 而非返回失败——破坏"挡在清库之前、现有数据原样保留"的既有契约。
    /// </summary>
    [Fact]
    public async Task Restore_NullPayload_ReturnsFailureNoThrowAndKeepsData()
    {
        var dst = Seed(_db2);
        await UpsertAsync(dst.Items, "dst", "b1", "现有条目");

        var rr = await dst.Backup.RestoreAsync(
            new BackupEnvelope { Payload = null! }, RestoreMode.Replace, null, CancellationToken.None);

        Assert.False(rr.Success);
        var all = await dst.Items.GetAllAsync(new BrowseFilter { IncludeHidden = true, Limit = 1000 }, CancellationToken.None);
        Assert.Equal("dst", Assert.Single(all).Source);   // 拒绝发生在清库之前，数据未被抹
    }

    [Fact]
    public async Task Restore_NullItemsCollection_ReturnsFailureNoThrowAndKeepsData()
    {
        var src = Seed(_db1);
        await UpsertAsync(src.Items, "src", "a1", "来自备份");
        var env = await src.Backup.ExportAsync(CancellationToken.None);
        // 模拟校验和自洽、但 items 字段被显式置 null 的语义非法备份（不经 ReadAsync 校验和复查）。
        env.Payload.Items = null!;

        var dst = Seed(_db2);
        await UpsertAsync(dst.Items, "dst", "b1", "现有条目");

        var rr = await dst.Backup.RestoreAsync(env, RestoreMode.Replace, null, CancellationToken.None);

        Assert.False(rr.Success);
        var all = await dst.Items.GetAllAsync(new BrowseFilter { IncludeHidden = true, Limit = 1000 }, CancellationToken.None);
        Assert.Equal("dst", Assert.Single(all).Source);   // 未 NRE 逃出、也未清空现有库
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

    [Fact]
    public async Task ImportTags_ExistingColorWins_BlankColorFilledFromBackup()
    {
        // EG：ImportTagsAsync:155 的 `color = COALESCE(tags.color, excluded.color)`（extra_json 同理）——
        // 主键 name 冲突时【已存在行的值胜出】，备份仅填目标库的空缺。这是合并式还原(RestoreMode.Merge)的
        // 承重取向：用户在目标库亲手挑的标签颜色，绝不能被一份旧备份静默覆盖；反之目标库该标签本无色时
        // 才用备份颜色补上。若有人把 COALESCE 参数写反(excluded, tags)或改成 excluded 直取(后写覆盖)，
        // 用户本地配色会在每次合并还原后被旧备份悄然改掉、RestoreResult 照常报成功——无人察觉。
        var ctx = Seed(_db1);
        var repo = new BackupRepository(ctx.Factory);

        // 预置：A 带用户色 #111111 + extra；B 无色无 extra（模拟用户新建尚未配色的标签）。
        await repo.ImportTagsAsync(new[]
        {
            new TagRecord("A", "#111111", """{"u":1}"""),
            new TagRecord("B", null, null),
        }, CancellationToken.None);

        // 再导入一份同名、但颜色/extra 皆不同的备份载荷 → COALESCE：A 原样保留，B 空缺被填。
        await repo.ImportTagsAsync(new[]
        {
            new TagRecord("A", "#222222", """{"u":2}"""),
            new TagRecord("B", "#333333", """{"v":9}"""),
        }, CancellationToken.None);

        var (aColor, aExtra, bColor) = ReadTagColors(ctx.Factory);
        Assert.Equal("#111111", aColor);              // 已存在色胜出，未被备份 #222222 覆盖
        Assert.Equal("""{"u":1}""", aExtra);          // extra 同理保留原值
        Assert.Equal("#333333", bColor);              // 原本无色 → 用备份色填上（COALESCE 另一臂）
    }

    [Fact]
    public async Task ImportItemTagLinks_ReapplyExistingLink_IsIdempotentNoThrow()
    {
        // EG：ImportItemTagLinksAsync:189 的 `AND NOT EXISTS (…item_tags x WHERE x.item_id=i.id AND x.tag_id=t.id)`。
        // item_tags 主键 = (item_id, tag_id)：闸门缺失时，对已存在的 (条目,标签) 关联再执行 INSERT…SELECT
        // 会撞主键抛 "PRIMARY KEY constraint failed"，把整个导入事务在还原中途打断（合并式还原本就常在
        // 已有相同关联的库上重跑）。闸门把撞键变成静默跳过——幂等、不抛、行数恒 1。
        var ctx = Seed(_db1);
        var item = await UpsertAsync(ctx.Items, "test", "l1", "带标签条目");
        await ctx.Items.AddTagAsync(item.Id, "重要", CancellationToken.None);   // 该关联此时已存在

        var repo = new BackupRepository(ctx.Factory);
        var link = new[] { new ItemTagLink("test", "l1", "重要") };
        await repo.ImportItemTagLinksAsync(link, CancellationToken.None);   // 无闸门则此处即撞主键抛
        await repo.ImportItemTagLinksAsync(link, CancellationToken.None);   // 再重跑一次仍应静默无操作

        Assert.Equal(1, CountItemTagLinks(ctx.Factory, "l1", "重要"));
    }

    private static (string? AColor, string? AExtra, string? BColor) ReadTagColors(DbConnectionFactory factory)
    {
        using var conn = factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, color, extra_json FROM tags;";
        string? aColor = null, aExtra = null, bColor = null;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var color = reader.IsDBNull(1) ? null : reader.GetString(1);
            var extra = reader.IsDBNull(2) ? null : reader.GetString(2);
            if (name == "A") { aColor = color; aExtra = extra; }
            else if (name == "B") { bColor = color; }
        }
        return (aColor, aExtra, bColor);
    }

    private static int CountItemTagLinks(DbConnectionFactory factory, string sourceId, string tagName)
    {
        using var conn = factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM item_tags it
              JOIN items i ON i.id = it.item_id
              JOIN tags  t ON t.id = it.tag_id
             WHERE i.source = @s AND i.source_id = @sid AND t.name = @tag;";
        cmd.Parameters.AddWithValue("@s", "test");
        cmd.Parameters.AddWithValue("@sid", sourceId);
        cmd.Parameters.AddWithValue("@tag", tagName);
        return System.Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public async Task ExportUserState_OnlyTouchedRows_EmptyNoteAndCleanExcluded()
    {
        // EH：ExportUserStateAsync:50-52 的门 `WHERE hidden<>0 OR pinned<>0 OR (notes IS NOT NULL AND notes<>'')`——
        // 仅"用户动过"的行进入用户态导出。三析取臂各须生效、两条排除臂（全默认 / notes 空串）亦须生效。
        // 承重且不可见：漏任一整臂→该维度用户态不导出、还原后静默丢失；`notes<>''` 尤为隐蔽——
        // SetNoteAsync 原样存 content（:318 无空串早退），故用户"清空笔记"会留下 notes=''，若去掉 `<>''`
        // 空笔记行将冒充"有笔记"混入导出、还原时对目标真实笔记徒生扰动。
        var ctx = Seed(_db1);
        var clean  = await UpsertAsync(ctx.Items, "test", "u_clean", "全默认");
        var noted  = await UpsertAsync(ctx.Items, "test", "u_note",  "有笔记");
        var empty  = await UpsertAsync(ctx.Items, "test", "u_empty", "空笔记");
        var pinned = await UpsertAsync(ctx.Items, "test", "u_pin",   "置顶");
        var hidden = await UpsertAsync(ctx.Items, "test", "u_hide",  "隐藏");
        _ = clean;   // clean 不加任何状态，仅用于验证排除

        await ctx.Items.SetNoteAsync(noted.Id, "正文笔记", CancellationToken.None);
        await ctx.Items.SetNoteAsync(empty.Id, string.Empty, CancellationToken.None);   // notes='' → 应被 `<>''` 剔
        await ctx.Items.SetPinnedAsync(pinned.Id, true, CancellationToken.None);
        await ctx.Items.SetHiddenAsync(hidden.Id, true, CancellationToken.None);

        var repo = new BackupRepository(ctx.Factory);
        var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var s in await repo.ExportUserStateAsync(CancellationToken.None)) ids.Add(s.SourceId);

        Assert.Equal(3, ids.Count);                 // 不多不少：恰三条被用户动过
        Assert.Contains("u_note", ids);             // notes 臂
        Assert.Contains("u_pin", ids);              // pinned 臂
        Assert.Contains("u_hide", ids);             // hidden 臂
        Assert.DoesNotContain("u_clean", ids);      // 三臂全 0 → 排除
        Assert.DoesNotContain("u_empty", ids);      // notes 空串 → `<>''` 排除（隐蔽承重臂）
    }

    [Fact]
    public async Task ExportUserState_ProjectsFieldValuesFaithfully_HiddenOnlyNoteStaysNull()
    {
        // EH②：ExportUserStateAsync:57-62 的 reader 值投影——Hidden/Pinned 由 `int != 0`、Notes 由
        // `IsDBNull(4) ? null : GetString(4)`。承重：hidden-only 行（无笔记）须投为 Notes==null；
        // 若误把 null 读成 ""（或 hidden/pinned/notes 列序错位），ImportUserStateAsync 的
        // `(object?)s.Notes ?? DBNull.Value` 会因拿到 ""（非 null）而在还原时把目标库该条真实笔记静默洗成空。
        // 逐字段钉死，防列序/判定翻转（列序错位会把 Pinned 的 1 当 Notes 之类，导出即腐）。
        var ctx = Seed(_db1);
        var hide = await UpsertAsync(ctx.Items, "test", "h", "隐藏无笔记");
        var both = await UpsertAsync(ctx.Items, "test", "b", "又置顶又有笔记");
        await ctx.Items.SetHiddenAsync(hide.Id, true, CancellationToken.None);
        await ctx.Items.SetPinnedAsync(both.Id, true, CancellationToken.None);
        await ctx.Items.SetNoteAsync(both.Id, "备注B", CancellationToken.None);

        var repo = new BackupRepository(ctx.Factory);
        var recs = await repo.ExportUserStateAsync(CancellationToken.None);

        var h = Assert.Single(recs, r => r.SourceId == "h");
        Assert.True(h.Hidden);
        Assert.False(h.Pinned);
        Assert.Null(h.Notes);                       // 无笔记 → null（非 ""），否则还原会洗掉目标笔记
        var b = Assert.Single(recs, r => r.SourceId == "b");
        Assert.False(b.Hidden);
        Assert.True(b.Pinned);
        Assert.Equal("备注B", b.Notes);
    }

    [Fact]
    public async Task Restore_BlankKeyAuxRows_AreSilentlyPruned_RestoreSucceedsWithFaithfulCounts()
    {
        // EJ：ValidatePayload 的软剔除臂（BackupService.cs:281-283）与条目硬失败臂（:285-288）刻意不对称——
        // 用户态/标签/关联里的「空白业务键行」静默剔除、还原照旧成功；而条目缺 source/source_id 硬拒整份备份。
        // 既有 Restore_Replace_BadBusinessKey 只钉了条目那一侧的硬失败，辅助集合的软剔除侧此前零测。
        // 承重且不可见：RestoreResult 的 UserStatesRestored/TagsRestored/LinksRestored（:226-228）取的是
        // 「剔除后」计数——若 ValidatePayload 漏剔，计数会按载荷原长虚报（ImportTags 自身虽跳空白名，
        // 但还原计数仍会谎报），故断言 ==1 精确钉住「验证层的剔除」而非仅靠导入层兜底。
        var dst = Seed(_db2);
        var env = new BackupEnvelope();
        env.Payload.Items.Add(new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "k1", Title = "真条目", Uri = "https://example.com/k1", CreatedAt = 1000 });
        env.Payload.UserState.Add(new UserStateRecord("test", "k1", false, true, null));   // 有效：置顶
        env.Payload.UserState.Add(new UserStateRecord("  ", "k1", true, false, "x"));      // 空白 Source → 剔
        env.Payload.UserState.Add(new UserStateRecord("test", " ", false, false, "y"));    // 空白 SourceId → 剔
        env.Payload.Tags.Add(new TagRecord("重要", null, null));                            // 有效
        env.Payload.Tags.Add(new TagRecord("  ", "red", null));                             // 空白 Name → 剔
        env.Payload.ItemTags.Add(new ItemTagLink("test", "k1", "重要"));                     // 有效关联
        env.Payload.ItemTags.Add(new ItemTagLink("test", "k1", "  "));                       // 空白 TagName → 剔
        env.Payload.ItemTags.Add(new ItemTagLink(" ", "k1", "重要"));                        // 空白 Source → 剔

        var rr = await dst.Backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None);

        Assert.True(rr.Success);                       // 不对称：辅助空白键不致命（对比条目空白键 → Success=false）
        Assert.Equal(1, rr.UserStatesRestored);        // 计数忠实于剔除后集合（虚报即暴露漏剔）
        Assert.Equal(1, rr.TagsRestored);
        Assert.Equal(1, rr.LinksRestored);
        var item = Assert.Single(await dst.Items.GetAllAsync(new BrowseFilter { IncludeHidden = true, Limit = 1000 }, CancellationToken.None));
        Assert.Equal("k1", item.SourceId);
        Assert.True(item.Pinned);                      // 有效 userstate 照常生效
        var tags = await dst.Items.GetTagsForItemAsync(item.Id, CancellationToken.None);
        Assert.Equal("重要", Assert.Single(tags));      // 恰有效标签，空白幽灵标签从未生成
    }

    [Fact]
    public async Task Restore_NullElementAuxRows_ArePrunedWithoutThrowing()
    {
        // EJ②：剔除的另一独立条件臂——`x is null`（BackupService.cs:280-283 各 RemoveAll 的首项合取），
        // 与空白键是不同的谓词分支。STJ 把 `[null, {...}]` 反序列化成含 null 元素的 List，四集合皆可现。
        // :272-273 注释明确点出「null 元素若不先剔、下游 Import*/Reindex 遍历 s.Source/t.Name 直接 NRE，
        // 且 RemoveAll 在 try 之外会逃出本应『返回失败』的方法」。此臂（含 Items 的 `it is null`）此前零测。
        var dst = Seed(_db2);
        var env = new BackupEnvelope();
        env.Payload.Items.Add(new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "k2", Title = "另一条目", Uri = "https://example.com/k2", CreatedAt = 1000 });
        env.Payload.Items.Add(null!);
        env.Payload.UserState.Add(new UserStateRecord("test", "k2", false, false, "备注")); // 有效：笔记
        env.Payload.UserState.Add(null!);
        env.Payload.Tags.Add(new TagRecord("标签Z", null, null));                            // 有效
        env.Payload.Tags.Add(null!);
        env.Payload.ItemTags.Add(new ItemTagLink("test", "k2", "标签Z"));                     // 有效
        env.Payload.ItemTags.Add(null!);

        var rr = await dst.Backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None);

        Assert.True(rr.Success);                       // null 元素被剔，未以 NRE 逃出
        Assert.Equal(1, rr.ItemsRestored);
        Assert.Equal(1, rr.UserStatesRestored);
        Assert.Equal(1, rr.TagsRestored);
        Assert.Equal(1, rr.LinksRestored);
        var item = Assert.Single(await dst.Items.GetAllAsync(new BrowseFilter { IncludeHidden = true, Limit = 1000 }, CancellationToken.None));
        Assert.Equal("k2", item.SourceId);
        Assert.Equal("备注", item.Notes);              // 有效 userstate 照常生效
        var tags = await dst.Items.GetTagsForItemAsync(item.Id, CancellationToken.None);
        Assert.Equal("标签Z", Assert.Single(tags));    // 有效关联仍在
    }
}
