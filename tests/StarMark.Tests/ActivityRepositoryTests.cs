#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;
using StarMark.Abstractions;
using StarMark.Data;

namespace StarMark.Tests;

public sealed class ActivityRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;

    public ActivityRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_act_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    [Fact]
    public async Task LogAndGet_RoundTrip_OrderedByTimeDesc()
    {
        var repo = new ItemRepository(_factory);
        await repo.LogActivityAsync(ActivityKind.BookmarkAdd, "test:b1", "书签一", "https://x.com/1", CancellationToken.None);
        await repo.LogActivityAsync(ActivityKind.StarRemove, "github:r2", "取消 Star", null, CancellationToken.None);

        var acts = await repo.GetActivityAsync(100, CancellationToken.None);
        Assert.Equal(2, acts.Count);
        Assert.Equal(ActivityKind.StarRemove, acts[0].Kind);   // 时间倒序
        Assert.Equal("取消 Star", acts[0].Title);
        Assert.Equal("https://x.com/1", acts[1].Uri);
    }

    [Fact]
    public async Task Log_PruneKeepsLatest500()
    {
        var repo = new ItemRepository(_factory);
        for (int i = 0; i < 505; i++)
            await repo.LogActivityAsync(ActivityKind.BookmarkAdd, null, $"e{i}", null, CancellationToken.None);

        var acts = await repo.GetActivityAsync(1000, CancellationToken.None);
        Assert.Equal(500, acts.Count);
        Assert.Equal("e504", acts[0].Title);   // 仅保留最近 500 条
    }

    [Fact]
    public async Task Upsert_BackgroundPath_DoesNotLogActivity()
    {
        // #51：UpsertAsync 只服务后台来源同步 / 种子 / 还原，一律不再自动写活动流，
        // 否则「最近活动」会被批量同步刷屏。用户主动增删改改由 UI 显式 LogActivityAsync。
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "https://x.com/p", Title = "新条目" },
        }, CancellationToken.None);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.GitHubStar, Source = "github", SourceId = "a/b", Title = "仓库" },
        }, CancellationToken.None);

        var acts = await repo.GetActivityAsync(100, CancellationToken.None);
        Assert.Empty(acts);
    }

    [Fact]
    public async Task LogActivity_UserKinds_RoundTrip()
    {
        // 用户主动事件（新增 / 修改）能被记录并原样解析回对应 Kind。
        var repo = new ItemRepository(_factory);
        await repo.LogActivityAsync(ActivityKind.ItemAdd, "local:t1", "买牛奶", null, CancellationToken.None);
        await repo.LogActivityAsync(ActivityKind.ItemModify, null, "改了笔记", "https://x.com/1", CancellationToken.None);

        var acts = await repo.GetActivityAsync(100, CancellationToken.None);
        Assert.Equal(2, acts.Count);
        Assert.Equal(ActivityKind.ItemModify, acts[0].Kind);   // 时间倒序
        Assert.Equal(ActivityKind.ItemAdd, acts[1].Kind);
        Assert.Equal("买牛奶", acts[1].Title);
    }

    [Fact]
    public async Task Upsert_NormalizesUrlAndMergesVariants()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "https://github.com/a/b/", Title = "r" },
        }, CancellationToken.None);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "https://github.com/a/b?tab=readme", Title = "r2" },
        }, CancellationToken.None);

        var all = await repo.GetAllAsync(new BrowseFilter { Limit = 1000 }, CancellationToken.None);
        var bm = all.Where(i => i.Type == ItemType.Bookmark).ToList();
        Assert.Single(bm);
        Assert.Equal("https://github.com/a/b", bm[0].SourceId);   // 归一为标准键，未分裂
    }

    [Fact]
    public async Task MigrateV4_MergesDuplicateBookmarksByNormalizedUrl()
    {
        // 模拟归一化前的重复：同一 GitHub 仓库的三种 URL 变体（各自独立条目）
        var ids = InsertRawBookmarks(new[]
        {
            ("https://github.com/a/b", "短笔记"),
            ("https://github.com/a/b/", "这是一条更长的笔记应该被保留"),
            ("https://github.com/a/b?tab=readme", "短笔记"),
        });
        var repo = new ItemRepository(_factory);
        await repo.AddTagAsync(ids[0], "alpha", CancellationToken.None);
        await repo.AddTagAsync(ids[1], "beta", CancellationToken.None);
        await repo.AddTagAsync(ids[2], "gamma", CancellationToken.None);

        // 把 schema 版本降到 3，使 V4 去重迁移在下次 EnsureSchema 时执行
        using (var c = _factory.Open())
        using (var down = c.CreateCommand())
        {
            down.CommandText = "UPDATE sync_state SET value='3' WHERE key='schema_version';";
            down.ExecuteNonQuery();
        }
        new MigrationRunner(_factory).EnsureSchema();

        var all = await repo.GetAllAsync(new BrowseFilter { Limit = 1000 }, CancellationToken.None);
        var bm = all.Where(i => i.Type == ItemType.Bookmark).ToList();
        Assert.Single(bm);                       // 三条变体合并为一条
        Assert.Equal("https://github.com/a/b", bm[0].SourceId);
        Assert.Contains("alpha", bm[0].Tags);    // 标签合并
        Assert.Contains("beta", bm[0].Tags);
        Assert.Contains("gamma", bm[0].Tags);
        Assert.Equal("这是一条更长的笔记应该被保留", bm[0].Notes);   // 非空冲突保留较长者
    }

    [Fact]
    public async Task MigrateV4_RebuildsSearchIndexForMergedContent()
    {
        // 三条 URL 变体（InsertRawBookmarks 的 search_text 插入时只含 title）。
        var ids = InsertRawBookmarks(new[]
        {
            ("https://github.com/m/merge", "普通笔记"),
            ("https://github.com/m/merge/", "zzmergednote更长的笔记应当被合并保留"),
            ("https://github.com/m/merge?tab=readme", "普通笔记"),
        });
        var repo = new ItemRepository(_factory);
        // keeper=ids[0]（最早 created_at）。zztgamma 挂在 dup ids[2] 上，合并后移到 keeper。
        await repo.AddTagAsync(ids[0], "zztalpha", CancellationToken.None);
        await repo.AddTagAsync(ids[2], "zztgamma", CancellationToken.None);

        using (var c = _factory.Open())
        using (var down = c.CreateCommand())
        {
            down.CommandText = "UPDATE sync_state SET value='3' WHERE key='schema_version';";
            down.ExecuteNonQuery();
        }
        new MigrationRunner(_factory).EnsureSchema();   // 触发 V4 合并

        // 合并改写了 keeper.notes 并新增 item_tags，但都没走 UpsertOne，若不事后重算 search_text +
        // FTS rebuild，则「被合并进来的更长笔记词 zzmergednote」与「dup 标签 zztgamma」永久搜不到（回归 R2 F1）。
        var search = new StarMark.Core.Search.SearchService(repo, Array.Empty<IItemSource>());
        Assert.Single((await search.SearchAsync("zztgamma", new SearchFilter { MaxResults = 10 }, CancellationToken.None)).Items);
        Assert.Single((await search.SearchAsync("zzmergednote", new SearchFilter { MaxResults = 10 }, CancellationToken.None)).Items);
    }

    [Fact]
    public void MigrateV4_FoldsHiddenAndPinnedOfMergedDuplicateOntoKeeper()
    {
        // 回归 AR-2：合并只搬标签+笔记、dup 一律 DELETE。若用户当初把"较晚那条 URL 变体"
        // 隐藏/置顶（keeper 保持可见未置顶），旧实现会连同 dup 行一起把这份用户状态抹掉，
        // 升级后条目重新现身且丢置顶。修后须整组 OR 归并回 keeper。
        var ids = InsertRawBookmarks(new[]
        {
            ("https://github.com/h/x", "keeper笔记"),   // ids[0] created_at 最早 → keeper（hidden=0,pinned=0）
            ("https://github.com/h/x/", "dup笔记"),    // ids[1] 较晚 → dup，被用户隐藏+置顶
        });
        using (var c = _factory.Open())
        using (var upd = c.CreateCommand())
        {
            upd.CommandText = "UPDATE items SET hidden=1, pinned=1 WHERE id=@id;";
            upd.Parameters.AddWithValue("@id", ids[1]);
            upd.ExecuteNonQuery();
        }

        using (var c = _factory.Open())
        using (var down = c.CreateCommand())
        {
            down.CommandText = "UPDATE sync_state SET value='3' WHERE key='schema_version';";
            down.ExecuteNonQuery();
        }
        new MigrationRunner(_factory).EnsureSchema();   // 触发 V4 合并

        using var conn = _factory.Open();
        using (var cnt = conn.CreateCommand())
        {
            cnt.CommandText = "SELECT COUNT(*) FROM items WHERE source='test';";
            Assert.Equal(1L, (long)cnt.ExecuteScalar()!);   // 两条变体并为一条
        }
        using (var q = conn.CreateCommand())
        {
            q.CommandText = "SELECT hidden, pinned FROM items WHERE source_id='https://github.com/h/x';";
            using var r = q.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(1L, r.GetInt64(0));   // dup 的隐藏态并入 keeper
            Assert.Equal(1L, r.GetInt64(1));   // dup 的置顶态并入 keeper
        }
    }

    private long[] InsertRawBookmarks((string Url, string Notes)[] rows)
    {
        var ids = new long[rows.Length];
        using var conn = _factory.Open();
        for (int i = 0; i < rows.Length; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO items(type, source, source_id, title, subtitle, uri, search_text,
                                  description, created_at, updated_at, synced_at, extra_json, hidden, notes)
                VALUES('bookmark','test',@sid,@title,'','',@title,'',@ts,@ts,@ts,'',0,@notes);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@sid", rows[i].Url);
            cmd.Parameters.AddWithValue("@title", "repo " + i);
            cmd.Parameters.AddWithValue("@ts", 1000L + i);
            cmd.Parameters.AddWithValue("@notes", rows[i].Notes);
            ids[i] = (long)cmd.ExecuteScalar()!;
        }
        return ids;
    }
}
