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
    public async Task Upsert_NewItem_LogsBookmarkAdd()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "https://x.com/p", Title = "新条目" },
        }, CancellationToken.None);

        var acts = await repo.GetActivityAsync(100, CancellationToken.None);
        Assert.Contains(acts, a => a.Kind == ActivityKind.BookmarkAdd && a.Title == "新条目");
    }

    [Fact]
    public async Task Upsert_ExistingItem_NoDuplicateActivity()
    {
        var repo = new ItemRepository(_factory);
        var item = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "https://x.com/p", Title = "t" };
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);   // 二次为更新，不记活动

        var acts = await repo.GetActivityAsync(100, CancellationToken.None);
        Assert.Single(acts.Where(a => a.Kind == ActivityKind.BookmarkAdd));
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
