#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Data;
using StarMark.Core.Search;

namespace StarMark.Tests;

public sealed class ItemRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;

    public ItemRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_test_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    [Fact]
    public async Task UpsertAndGetById_RoundTrip()
    {
        var repo = new ItemRepository(_factory);
        var item = new Item
        {
            Type = ItemType.Bookmark,
            Source = "test",
            SourceId = "b1",
            Title = "GitHub",
            Subtitle = "主页",
            Uri = "https://github.com",
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);
        Assert.True(item.Id > 0);

        var fetched = await repo.GetByIdAsync(item.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal("GitHub", fetched!.Title);
        Assert.Equal("https://github.com", fetched.Uri);
    }

    [Fact]
    public async Task GetAll_UnknownTypeColumn_FallsBackToBookmarkInsteadOfThrowing()
    {
        // AE-1 回归：items.type 无 CHECK 约束，降级/手工/坏备份可留未知值。
        // Enum.Parse 会让 MapItem(所有读路径的水合入口)对整页抛 ArgumentException；
        // TryParse 兜底后应回落 Bookmark 且整页仍可读。
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.File, Source = "test", SourceId = "x1", Title = "坏类型行" },
        }, CancellationToken.None);

        using (var conn = _factory.Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE items SET type = 'nonsense_type' WHERE source = 'test' AND source_id = 'x1';";
            cmd.ExecuteNonQuery();
        }

        var all = await repo.GetAllAsync(new BrowseFilter { IncludeHidden = true, Limit = 1000 }, CancellationToken.None);
        var row = Assert.Single(all);
        Assert.Equal(ItemType.Bookmark, row.Type);
    }

    [Fact]
    public async Task Upsert_BlankAndWhitespaceTags_AreNormalizedAway()
    {
        // AQ-2 回归：UpsertOne 的标签循环曾缺 IsNullOrWhiteSpace + Trim（同仓储的本地写入路径
        // LinkTagByNameAsync 已有），tags.name 又无 CHECK 约束——书签匿名文件夹产出的 "" 会落成一条
        // 真实空白标签行，" work"/"work" 因 NOCASE 只并大小写而裂成两行。
        var repo = new ItemRepository(_factory);
        var item = new Item
        {
            Type = ItemType.Bookmark,
            Source = "test",
            SourceId = "tags1",
            Title = "脏标签",
            Tags = new List<string> { "", "   ", " work", "work", "C++" },
        };
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);
        Assert.True(item.Id > 0);

        var names = new List<string>();
        using (var conn = _factory.Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT t.name FROM tags t JOIN item_tags it ON it.tag_id = t.id WHERE it.item_id = @id;";
            cmd.Parameters.AddWithValue("@id", item.Id);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) names.Add(reader.GetString(0));
        }

        // 空串/纯空白不入库；" work"+"work" 归并为一条 "work"；"C++" 原样保留。
        Assert.Equal(2, names.Count);
        Assert.Contains("work", names);
        Assert.Contains("C++", names);
        Assert.DoesNotContain(names, n => string.IsNullOrWhiteSpace(n));
    }

    [Fact]
    public async Task UpsertIdempotent_UpdatesExisting()
    {
        var repo = new ItemRepository(_factory);
        var item = new Item { Type = ItemType.File, Source = "test", SourceId = "f1", Title = "old" };
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);
        var id = item.Id;

        item.Title = "new";
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);
        Assert.Equal(id, item.Id);

        var fetched = await repo.GetByIdAsync(id, CancellationToken.None);
        Assert.Equal("new", fetched!.Title);
    }

    [Fact]
    public async Task SearchAsync_ReturnsFTSMatch()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "s1", Title = "Python 文档", Uri = "https://docs.python.org" },
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "s2", Title = "Rust 手册", Uri = "https://doc.rust-lang.org" },
        }, CancellationToken.None);

        var result = await repo.SearchAsync("Python", new SearchFilter { MaxResults = 10 }, CancellationToken.None);
        Assert.Single(result.Items);
        Assert.Equal("Python 文档", result.Items[0].Title);
    }

    [Fact]
    public async Task Tag_RoundTrip_AndSearchIncludesTags()
    {
        var repo = new ItemRepository(_factory);
        var item = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "t1", Title = "NoTag", Uri = "https://a.com" };
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);
        await repo.AddTagAsync(item.Id, "AI", CancellationToken.None);

        var tags = await repo.GetTagsForItemAsync(item.Id, CancellationToken.None);
        Assert.Contains("AI", tags);

        // Upsert 合并式：源侧标签加入，用户手动添加的标签不因同步丢失
        item.Tags = new System.Collections.Generic.List<string> { "DevTools" };
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);
        var afterUpsert = await repo.GetTagsForItemAsync(item.Id, CancellationToken.None);
        Assert.Contains("DevTools", afterUpsert);
        Assert.Contains("AI", afterUpsert);

        // Search includes "DevTools" in search_text
        var search = await repo.SearchAsync("DevTools", new SearchFilter { MaxResults = 10 }, CancellationToken.None);
        Assert.Single(search.Items);
        Assert.Equal(item.Id, search.Items[0].Id);
    }

    /// <summary>
    /// 标签过滤必须是 AND：原实现用 `JOIN + t.name IN(...) + GROUP BY` 实际是 OR，
    /// 多选标签时结果反而变多，与「多选收窄」的预期相反。此用例钉死 AND 语义。
    /// </summary>
    [Fact]
    public async Task BrowseFilter_TagsAreAndSemantics()
    {
        var repo = new ItemRepository(_factory);
        var both = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "a1", Title = "Both", Uri = "https://both.com" };
        var onlyAi = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "a2", Title = "OnlyAi", Uri = "https://onlyai.com" };
        var onlyRust = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "a3", Title = "OnlyRust", Uri = "https://onlyrust.com" };
        await repo.UpsertAsync(new[] { both, onlyAi, onlyRust }, CancellationToken.None);

        await repo.AddTagAsync(both.Id, "ai", CancellationToken.None);
        await repo.AddTagAsync(both.Id, "rust", CancellationToken.None);
        await repo.AddTagAsync(onlyAi.Id, "ai", CancellationToken.None);
        await repo.AddTagAsync(onlyRust.Id, "rust", CancellationToken.None);

        // 单个标签：命中 2 条（both + onlyX）
        var one = await repo.GetAllAsync(new BrowseFilter { TagFilters = new[] { "ai" }, Limit = 50 }, CancellationToken.None);
        Assert.Equal(2, one.Count);

        // 两个标签 AND：只剩同时带 ai 与 rust 的那一条
        var two = await repo.GetAllAsync(new BrowseFilter { TagFilters = new[] { "ai", "rust" }, Limit = 50 }, CancellationToken.None);
        Assert.Single(two);
        Assert.Equal("Both", two[0].Title);
    }

    /// <summary>搜索同样支持标签 AND 过滤，且与关键词叠加。</summary>
    [Fact]
    public async Task SearchFilter_TagsCombineWithKeywordAsAnd()
    {
        var repo = new ItemRepository(_factory);
        var hit = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "k1", Title = "Rust 异步编程", Uri = "https://rust-async.com" };
        var decoy = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "k2", Title = "Rust 入门", Uri = "https://rust-intro.com" };
        await repo.UpsertAsync(new[] { hit, decoy }, CancellationToken.None);

        await repo.AddTagAsync(hit.Id, "async", CancellationToken.None);
        await repo.AddTagAsync(decoy.Id, "beginner", CancellationToken.None);

        // 仅关键词：两条都匹配
        var byKeyword = await repo.SearchAsync("Rust", new SearchFilter { MaxResults = 10 }, CancellationToken.None);
        Assert.Equal(2, byKeyword.Items.Count);

        // 关键词 + 标签：只剩同时满足的一条
        var combined = await repo.SearchAsync("Rust",
            new SearchFilter { MaxResults = 10, Tags = new[] { "async" } }, CancellationToken.None);
        Assert.Single(combined.Items);
        Assert.Equal("Rust 异步编程", combined.Items[0].Title);
    }

    /// <summary>
    /// 回归钉（P0-1）：编辑中文随记 / 加删中文标签后，中文子串全文检索必须继续命中。
    /// 旧实现里 <c>SetNoteAsync</c> 用纯 SQL 写未展开原文、<c>AddTag/RemoveTag</c> 根本不动
    /// <c>search_text</c>，会让中文召回静默失效（unicode61 把连续中文当单个 token）。
    /// </summary>
    [Fact]
    public async Task CjkSearch_SurvivesNoteEditAndTagChanges()
    {
        var repo = new ItemRepository(_factory);
        var item = new Item
        {
            Type = ItemType.Note,
            Source = "test",
            SourceId = "cjk1",
            Title = "搜索笔记工具",
        };
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);

        async Task<bool> Hits(string q)
        {
            var r = await repo.SearchAsync(q, new SearchFilter { MaxResults = 10 }, CancellationToken.None);
            return r.Items.Any(i => i.Id == item.Id);
        }

        Assert.True(await Hits("笔记"), "基线：标题中文子串应命中");

        // 编辑中文笔记后，标题仍可达、笔记内容亦可达
        await repo.SetNoteAsync(item.Id, "记录随想片段", CancellationToken.None);
        Assert.True(await Hits("笔记"), "改笔记后标题中文子串不应失效");
        Assert.True(await Hits("随想"), "改笔记后笔记内容中文子串应命中");

        // 加中文标签：标签词应进入索引
        await repo.AddTagAsync(item.Id, "重要待办", CancellationToken.None);
        Assert.True(await Hits("待办"), "加中文标签后该标签词应命中");

        // 删中文标签：该词应随之从索引移除
        await repo.RemoveTagAsync(item.Id, "重要待办", CancellationToken.None);
        Assert.False(await Hits("待办"), "删中文标签后该标签词不应再命中");
    }

    [Fact]
    public async Task Upsert_PreservesUserState()
    {
        var repo = new ItemRepository(_factory);
        var item = new Item { Type = ItemType.File, Source = "test", SourceId = "u1", Title = "v1" };
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);
        await repo.SetNoteAsync(item.Id, "用户笔记", CancellationToken.None);
        await repo.SetHiddenAsync(item.Id, true, CancellationToken.None);
        await repo.SetPinnedAsync(item.Id, true, CancellationToken.None);

        // 模拟同步：同 source+source_id 的新实例（Hidden/Notes/Pinned 均为默认值）
        var synced = new Item { Type = ItemType.File, Source = "test", SourceId = "u1", Title = "v2" };
        await repo.UpsertAsync(new[] { synced }, CancellationToken.None);
        Assert.Equal(item.Id, synced.Id);

        var fetched = await repo.GetByIdAsync(item.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal("v2", fetched!.Title);                       // 源侧字段更新
        Assert.Equal("用户笔记", fetched.Notes);                  // 用户笔记保留
        Assert.True(fetched.Hidden);                              // 隐藏状态保留
        Assert.True(fetched.Pinned);                              // 置顶状态保留
    }

    [Fact]
    public async Task RecordItemAsync_ReturnsRealId_AndIsIdempotent()
    {
        var repo = new ItemRepository(_factory);
        // 模拟 Everything 虚拟条目：Id=0、带 (filesystem, 路径哈希 source_id)、尚未入库。
        var virtualItem = new Item
        {
            Type = ItemType.File,
            Source = ItemSources.FileSystem,
            SourceId = "abc123hash",
            Title = "report.pdf",
            Subtitle = @"C:\docs",
            Uri = "file://C:/docs/report.pdf",
        };
        Assert.Equal(0, virtualItem.Id);

        var id = await repo.RecordItemAsync(virtualItem, CancellationToken.None);
        Assert.True(id > 0);
        Assert.Equal(id, virtualItem.Id);   // 真实 Id 回填

        // 再次登记同一 (source, source_id)：返回既有 Id，不产生重复行。
        var again = new Item
        {
            Type = ItemType.File,
            Source = ItemSources.FileSystem,
            SourceId = "abc123hash",
            Title = "report.pdf",
            Subtitle = @"C:\docs",
            Uri = "file://C:/docs/report.pdf",
        };
        var id2 = await repo.RecordItemAsync(again, CancellationToken.None);
        Assert.Equal(id, id2);

        var fetched = await repo.GetByIdAsync(id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(ItemSources.FileSystem, fetched!.Source);
    }

    [Fact]
    public async Task RecordItemAsync_MergesWithSync_AndPreservesUserState()
    {
        var repo = new ItemRepository(_factory);
        // 先按同步口径落库一条文件系统条目。
        var synced = new Item { Type = ItemType.File, Source = ItemSources.FileSystem, SourceId = "h1", Title = "v1" };
        await repo.UpsertAsync(new[] { synced }, CancellationToken.None);
        await repo.SetPinnedAsync(synced.Id, true, CancellationToken.None);

        // 再登记同一路径的虚拟条目：应命中同一行、返回既有 Id，且保留用户置顶（upsert 不覆盖 pinned）。
        var virtualItem = new Item { Type = ItemType.File, Source = ItemSources.FileSystem, SourceId = "h1", Title = "v1-again" };
        var id = await repo.RecordItemAsync(virtualItem, CancellationToken.None);
        Assert.Equal(synced.Id, id);

        var fetched = await repo.GetByIdAsync(id, CancellationToken.None);
        Assert.True(fetched!.Pinned);   // 置顶未被登记冲掉
    }

    [Fact]
    public async Task RecordItemAsync_MissingBusinessKey_ReturnsZero()
    {
        var repo = new ItemRepository(_factory);
        Assert.Equal(0, await repo.RecordItemAsync(
            new Item { Type = ItemType.File, Source = "", SourceId = "x" }, CancellationToken.None));
        Assert.Equal(0, await repo.RecordItemAsync(
            new Item { Type = ItemType.File, Source = ItemSources.FileSystem, SourceId = "" }, CancellationToken.None));
    }

    [Fact]
    public async Task SetPinned_BrowseSortsPinnedFirst()
    {
        var repo = new ItemRepository(_factory);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var a = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "p1", Title = "older", UpdatedAt = now };
        var b = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "p2", Title = "newer", UpdatedAt = now + 10 };
        await repo.UpsertAsync(new[] { a }, CancellationToken.None);
        await repo.UpsertAsync(new[] { b }, CancellationToken.None);

        // 默认最近排序：newer 在前
        var before = await repo.GetAllAsync(new BrowseFilter { Sort = "recent", Limit = 10 }, CancellationToken.None);
        Assert.Equal("newer", before[0].Title);

        // 置顶 older 后：older 稳定居首
        await repo.SetPinnedAsync(a.Id, true, CancellationToken.None);
        var after = await repo.GetAllAsync(new BrowseFilter { Sort = "recent", Limit = 10 }, CancellationToken.None);
        Assert.Equal("older", after[0].Title);
        Assert.True(after[0].Pinned);
    }

    [Fact]
    public async Task SetHidden_GetHidden()
    {
        var repo = new ItemRepository(_factory);
        var item = new Item { Type = ItemType.Clipboard, Source = "test", SourceId = "h1", Title = "secret" };
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);
        await repo.SetHiddenAsync(item.Id, true, CancellationToken.None);

        var all = await repo.GetAllAsync(new BrowseFilter { Limit = 100 }, CancellationToken.None);
        Assert.DoesNotContain(all, i => i.Id == item.Id);

        var hidden = await repo.GetHiddenAsync(CancellationToken.None);
        Assert.Contains(hidden, i => i.Id == item.Id);
    }

    [Fact]
    public async Task SearchAsync_IncludeHidden_ReturnsHiddenItems()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.File, Source = "test", SourceId = "n1", Title = "alpha visible doc" },
        }, CancellationToken.None);
        var hidden = new Item { Type = ItemType.File, Source = "test", SourceId = "n2", Title = "alpha secret doc" };
        await repo.UpsertAsync(new[] { hidden }, CancellationToken.None);
        await repo.SetHiddenAsync(hidden.Id, true, CancellationToken.None);

        var without = await repo.SearchAsync("alpha", new SearchFilter { MaxResults = 10 }, CancellationToken.None);
        Assert.DoesNotContain(without.Items, i => i.Id == hidden.Id);

        var with = await repo.SearchAsync("alpha", new SearchFilter { MaxResults = 10, IncludeHidden = true }, CancellationToken.None);
        Assert.Contains(with.Items, i => i.Id == hidden.Id);
    }

    [Fact]
    public async Task SearchService_MergesResults()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "x1", Title = "Alpha 架构", Uri = "https://alpha.com" },
        }, CancellationToken.None);

        var search = new SearchService(repo, Array.Empty<IItemSource>());
        var result = await search.SearchAsync("Alpha", new SearchFilter { MaxResults = 10 }, CancellationToken.None);
        Assert.Single(result.Items);
        Assert.Equal("https://alpha.com", result.Items[0].Uri);
    }

    // ===== #53 V1：快照忠实还原仓储层 =====

    [Fact]
    public async Task ReplaceLocalItemsForInstance_PreservesFaithfulFields_AndIsolates()
    {
        var repo = new ItemRepository(_factory);

        // 别的来源/别的实例的条目必须不受前缀 Replace 影响。
        var other = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "keep1", Title = "别来源不动" };
        await repo.UpsertAsync(new[] { other }, CancellationToken.None);
        var sib = new Item { Type = ItemType.Todo, Source = ItemSources.Local, SourceId = "Y|9", Title = "别实例不动" };
        await repo.UpsertLocalItemAsync(sib, CancellationToken.None);

        // 目标实例先有一条将被覆盖。
        await repo.UpsertLocalItemAsync(
            new Item { Type = ItemType.Todo, Source = ItemSources.Local, SourceId = "X|1", Title = "将被替换掉的旧条目" },
            CancellationToken.None);

        var toInsert = new List<Item>
        {
            new Item
            {
                Type = ItemType.Todo,
                Source = ItemSources.Local,
                SourceId = "X|2",
                Title = "忠实还原",
                Subtitle = "副",
                Uri = "u",
                Description = "d",
                Notes = "n",
                Hidden = true,
                Pinned = true,
                Tags = new List<string> { "重要", "工作" },
                ExtraJson = """{"done":true}""",
            },
        };
        await repo.ReplaceLocalItemsForInstanceAsync("X", toInsert, CancellationToken.None);

        var got = await repo.GetLocalItemsForInstanceAsync("X", CancellationToken.None);
        var s = Assert.Single(got);
        Assert.Equal("忠实还原", s.Title);
        Assert.Equal("副", s.Subtitle);
        Assert.Equal("u", s.Uri);
        Assert.Equal("d", s.Description);
        Assert.Equal("n", s.Notes);
        Assert.True(s.Hidden);
        Assert.True(s.Pinned);
        Assert.Contains("重要", s.Tags);
        Assert.Contains("工作", s.Tags);
        Assert.Contains("\"done\":true", s.ExtraJson);

        // 别来源 + 别实例都还在
        Assert.NotNull(await repo.GetByIdAsync(other.Id, CancellationToken.None));
        var sibGot = await repo.GetLocalItemsForInstanceAsync("Y", CancellationToken.None);
        Assert.Equal("别实例不动", Assert.Single(sibGot).Title);
    }

    [Fact]
    public async Task GetLocalItemsForInstance_PrefixBoundaryDoesNotMatchSibling()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertLocalItemAsync(
            new Item { Type = ItemType.Todo, Source = ItemSources.Local, SourceId = "12|1", Title = "本实例" },
            CancellationToken.None);
        await repo.UpsertLocalItemAsync(
            new Item { Type = ItemType.Todo, Source = ItemSources.Local, SourceId = "123|1", Title = "兄弟前缀" },
            CancellationToken.None);

        // LIKE "12|%" 必须因 '|' 边界而不命中 "123|…"。
        var only = await repo.GetLocalItemsForInstanceAsync("12", CancellationToken.None);
        Assert.Equal("本实例", Assert.Single(only).Title);
    }

    // ===== 批次 W：数据层缺陷回归 =====

    [Fact]
    public async Task GetAllTags_ExcludesHiddenItems()
    {
        var repo = new ItemRepository(_factory);
        var item = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "t1", Title = "tagged" };
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);
        await repo.AddTagAsync(item.Id, "计数标签", CancellationToken.None);

        var before = await repo.GetAllTagsAsync(CancellationToken.None);
        Assert.Equal(1, before.Single(t => t.Name == "计数标签").Count);

        // 隐藏该条目后徽标计数必须随之下降（旧实现数 item_tags 连接行，隐藏不减）。
        await repo.SetHiddenAsync(item.Id, true, CancellationToken.None);
        var after = await repo.GetAllTagsAsync(CancellationToken.None);
        Assert.Equal(0, after.Single(t => t.Name == "计数标签").Count);
    }

    [Fact]
    public async Task GetAllTags_OmittedAfterLastTagReferenceRemoved()
    {
        // 批次 AJ-1：全库无 DELETE FROM tags，RemoveTagAsync 只删 item_tags 关联 → tags 行残留成孤儿。
        // 旧 GetAllTagsAsync 用 LEFT JOIN 驱动，会让零引用孤儿永久以「(标签, 0)」幽灵挂在列表。
        // 与上面的隐藏用例对照：这里断言「无任何关联」的标签必须整体消失（而隐藏标签仍保留 count=0）。
        var repo = new ItemRepository(_factory);
        var item = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "g1", Title = "幽灵" };
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);
        await repo.AddTagAsync(item.Id, "临时标签", CancellationToken.None);

        Assert.Contains("临时标签", (await repo.GetAllTagsAsync(CancellationToken.None)).Select(t => t.Name));

        await repo.RemoveTagAsync(item.Id, "临时标签", CancellationToken.None);
        Assert.DoesNotContain("临时标签", (await repo.GetAllTagsAsync(CancellationToken.None)).Select(t => t.Name));
    }

    [Fact]
    public async Task UpsertLocalItem_Updates_PersistsHiddenAndNotes()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertLocalItemAsync(
            new Item { Type = ItemType.Todo, Source = ItemSources.Local, SourceId = "Z|1", Title = "待办" },
            CancellationToken.None);

        // 取回完整行（FindItemAsync 语义），改隐藏 + 加笔记后回写。
        var loaded = Assert.Single(await repo.GetLocalItemsForInstanceAsync("Z", CancellationToken.None));
        loaded.Hidden = true;
        loaded.Notes = "用户笔记";
        await repo.UpsertLocalItemAsync(loaded, CancellationToken.None);

        var again = Assert.Single(await repo.GetLocalItemsForInstanceAsync("Z", CancellationToken.None));
        Assert.True(again.Hidden);                 // 旧实现 DO UPDATE 漏 hidden → 回滚
        Assert.Equal("用户笔记", again.Notes);      // 旧实现 DO UPDATE 漏 notes → 丢失
    }

    [Fact]
    public async Task TagNameWithComma_RoundTripsAsSingleTag()
    {
        var repo = new ItemRepository(_factory);
        var item = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "c1", Title = "逗号标签" };
        await repo.UpsertAsync(new[] { item }, CancellationToken.None);
        await repo.AddTagAsync(item.Id, "含,逗号", CancellationToken.None);

        // 读路径曾以 ',' 切 GROUP_CONCAT，会把一个标签错拆成两个。
        var fetched = await repo.GetByIdAsync(item.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(new[] { "含,逗号" }, fetched!.Tags.ToArray());
    }

    // ===== 批次 AA（R3）：本地条目 search_text 口径 / GetBySource 隐藏过滤回归 =====

    [Fact]
    public async Task UpsertLocalItem_SearchTextCoversNotesDescriptionTags()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertLocalItemAsync(new Item
        {
            Type = ItemType.Todo,
            Source = ItemSources.Local,
            SourceId = "S|1",
            Title = "买咖啡",
            Description = "描述zzdesc",
            Notes = "笔记zznote",
            Tags = new List<string> { "zztag" },
        }, CancellationToken.None);

        var search = new SearchService(repo, Array.Empty<IItemSource>());
        // 旧实现 UpsertLocalItemAsync 的 search_text 只含 Title → 每次待办/随记 re-save 把描述/笔记/
        // 标签词从 FTS 索引抹掉，表现为「明明写了却搜不到」。三个词都必须命中。
        Assert.Single((await search.SearchAsync("zzdesc", new SearchFilter { MaxResults = 10 }, CancellationToken.None)).Items);
        Assert.Single((await search.SearchAsync("zznote", new SearchFilter { MaxResults = 10 }, CancellationToken.None)).Items);
        Assert.Single((await search.SearchAsync("zztag", new SearchFilter { MaxResults = 10 }, CancellationToken.None)).Items);
    }

    [Fact]
    public async Task GetBySource_ExcludesHidden()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertLocalItemAsync(
            new Item { Type = ItemType.Todo, Source = ItemSources.Local, SourceId = "H|1", Title = "可见项" },
            CancellationToken.None);
        var hideMe = new Item { Type = ItemType.Todo, Source = ItemSources.Local, SourceId = "H|2", Title = "隐藏项" };
        await repo.UpsertLocalItemAsync(hideMe, CancellationToken.None);
        await repo.SetHiddenAsync(hideMe.Id, true, CancellationToken.None);

        // 旧实现无 hidden 过滤 → 已隐藏的待办/随记仍会回到组件行列表。
        var visible = await repo.GetBySourceAsync(ItemSources.Local, ItemType.Todo, 1000, CancellationToken.None);
        Assert.Contains(visible, i => i.Title == "可见项");
        Assert.DoesNotContain(visible, i => i.Title == "隐藏项");
    }

    // ===== 批次 AZ：浏览模式 GetAllAsync 排序分支 / 语言闸门回归 =====

    [Fact]
    public async Task GetAllAsync_SortCollected_OrdersByCreatedAtDesc()
    {
        // AZ-1：主窗工具栏「最近收藏」经 BrowseFilter.Sort="collected" 直达 GetAllAsync，
        // 旧 switch 只有 stars/name/recent → 落 default 变成「最近更新」。用 created/updated 反向
        // 排列钉死：collected 必须按 created_at DESC，而非 updated_at DESC。
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "c1", Title = "早收藏", CreatedAt = 100, UpdatedAt = 300 },
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "c2", Title = "中收藏", CreatedAt = 200, UpdatedAt = 200 },
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "c3", Title = "晚收藏", CreatedAt = 300, UpdatedAt = 100 },
        }, CancellationToken.None);

        var items = await repo.GetAllAsync(new BrowseFilter { Sort = "collected", Limit = 10 }, CancellationToken.None);
        Assert.Equal(new[] { "晚收藏", "中收藏", "早收藏" }, items.Select(i => i.Title).ToArray());
    }

    [Fact]
    public async Task GetAllAsync_SortStarred_OrdersByStarredAtDesc()
    {
        // AZ-1：主窗工具栏「最近 Star」→ Sort="starred" → GetAllAsync 旧实现漏支落 default。
        // starredAt 与 updated_at 反向排列，钉死须按 extra_json.StarredAt DESC。
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s1", Title = "晚star", Uri = "https://github.com/o/r1", UpdatedAt = 100, ExtraJson = """{"StarredAt":900}""" },
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s2", Title = "中star", Uri = "https://github.com/o/r2", UpdatedAt = 200, ExtraJson = """{"StarredAt":800}""" },
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s3", Title = "早star", Uri = "https://github.com/o/r3", UpdatedAt = 300, ExtraJson = """{"StarredAt":700}""" },
        }, CancellationToken.None);

        var items = await repo.GetAllAsync(new BrowseFilter { Sort = "starred", Limit = 10 }, CancellationToken.None);
        Assert.Equal(new[] { "晚star", "中star", "早star" }, items.Select(i => i.Title).ToArray());
    }

    [Fact]
    public async Task GetAllAsync_LanguageFilter_ExcludesNonStarItems()
    {
        // AZ-2：语言下拉语义是「按编程语言筛 star」（LanguageDetector / FolderTreePage 注释），
        // 「所有来源」视图（TypeFilter=null）下旧实现只比 json_extract($.Language) 无 type 闸门。
        // EnsureLanguage 会给 .py 书签兜底打 Python → 它会冒充 star 混入。加 i.type='githubstar' 闸门后
        // 只应返回 star 那条。
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "st1", Title = "Python Star", Uri = "https://github.com/o/r", ExtraJson = """{"Language":"Python"}""" },
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "bk1", Title = "Python 脚本书签", Uri = "https://example.com/tool.py" },
        }, CancellationToken.None);

        var items = await repo.GetAllAsync(new BrowseFilter { Language = "Python", Limit = 10 }, CancellationToken.None);
        var only = Assert.Single(items);
        Assert.Equal(ItemType.GitHubStar, only.Type);
        Assert.Equal("Python Star", only.Title);
    }

    [Fact]
    public async Task GetStarLanguages_OnlyFromStarItems_NormalizesRetainsAndOrders()
    {
        // EC：主界面语言下拉的唯一数据源。钉死三层契约——
        // ① type 闸门：EnsureLanguage 会给书签兜底打 Language，非 star 的语言不得混入下拉；
        // ② Normalize 归一（python→Python、assembly→Assembly）；未收录语言原样保留（Brainfuck）；
        // ③ 末尾按 OrdinalIgnoreCase 升序（A<B<P<R，纯 ASCII 与语言无歧义）。
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s1", Title = "A", Uri = "https://github.com/o/a", ExtraJson = """{"Language":"python"}""" },
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s2", Title = "B", Uri = "https://github.com/o/b", ExtraJson = """{"Language":"Rust"}""" },
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s3", Title = "C", Uri = "https://github.com/o/c", ExtraJson = """{"Language":"assembly"}""" },
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s4", Title = "D", Uri = "https://github.com/o/d", ExtraJson = """{"Language":"Brainfuck"}""" },
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "b1", Title = "E", Uri = "https://example.com", ExtraJson = """{"Language":"Python"}""" },
        }, CancellationToken.None);

        var langs = await repo.GetStarLanguagesAsync(CancellationToken.None);
        Assert.Equal(new[] { "Assembly", "Brainfuck", "Python", "Rust" }, langs.ToArray());
    }

    [Fact]
    public async Task GetStarLanguages_CollapsesCaseAndWhitespaceVariantsOnlyAfterNormalize()
    {
        // EC：SQL 的 DISTINCT 对文本是二进制、区分大小写的——"c#" 与 "C#" 在 SQL 层是两行。
        // 折叠只能靠 C# 侧「先 Normalize 再 Distinct(OrdinalIgnoreCase)」这二次去重完成；
        // 若删该 C# Distinct（误以为 SQL DISTINCT 已够），下拉会同时出现 "C#" 与 "c#"。
        // 顺带钉死 SQL trim + Normalize 内 Trim 的双层空白兜底。
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s1", Title = "A", Uri = "https://github.com/o/a", ExtraJson = """{"Language":"c#"}""" },
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s2", Title = "B", Uri = "https://github.com/o/b", ExtraJson = """{"Language":"C#"}""" },
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s3", Title = "C", Uri = "https://github.com/o/c", ExtraJson = """{"Language":"  C++  "}""" },
        }, CancellationToken.None);

        var langs = await repo.GetStarLanguagesAsync(CancellationToken.None);
        // '#'(0x23) < '+'(0x2B)：OrdinalIgnoreCase 下 "C#" 先于 "C++"。
        Assert.Equal(new[] { "C#", "C++" }, langs.ToArray());
    }

    [Fact]
    public async Task GetStarLanguages_DropsNullAndBlankLanguages()
    {
        // EC：缺 Language 键、空串、纯空白三种 star 都不应产出下拉项
        // （SQL IS NOT NULL + trim<>'' 与 C# IsNullOrWhiteSpace 双闸，回归时任一失效都会冒出空选项）。
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s1", Title = "A", Uri = "https://github.com/o/a", ExtraJson = """{"Language":"Go"}""" },
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s2", Title = "B", Uri = "https://github.com/o/b", ExtraJson = """{}""" },
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s3", Title = "C", Uri = "https://github.com/o/c", ExtraJson = """{"Language":""}""" },
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s4", Title = "D", Uri = "https://github.com/o/d", ExtraJson = """{"Language":"   "}""" },
        }, CancellationToken.None);

        var langs = await repo.GetStarLanguagesAsync(CancellationToken.None);
        var only = Assert.Single(langs);
        Assert.Equal("Go", only);
    }

    [Fact]
    public async Task GetCountsByType_ExcludesHiddenAndGroupsPerType()
    {
        // ED：MainViewModel:126 类型徽标计数的唯一来源。钉死 `WHERE hidden=0` 闸门
        // （隐藏条目不得计入徽标，与浏览默认视图/标签徽标排除 hidden 同口径）+ 按 type 分组计数正确。
        var repo = new ItemRepository(_factory);
        var s1 = new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s1", Title = "可见star", Uri = "https://github.com/o/a" };
        var s2 = new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "s2", Title = "隐藏star", Uri = "https://github.com/o/b" };
        var b1 = new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "b1", Title = "书签", Uri = "https://example.com" };
        var t1 = new Item { Type = ItemType.Todo, Source = "test", SourceId = "t1", Title = "待办" };
        await repo.UpsertAsync(new[] { s1, s2, b1, t1 }, CancellationToken.None);

        // 把其中一条 star 隐藏：GitHubStar 徽标应只数可见的 1 条，而非 2。
        await repo.SetHiddenAsync(s2.Id, true, CancellationToken.None);

        var counts = await repo.GetCountsByTypeAsync(CancellationToken.None);
        Assert.Equal(1, counts[ItemType.GitHubStar]);
        Assert.Equal(1, counts[ItemType.Bookmark]);
        Assert.Equal(1, counts[ItemType.Todo]);
    }

    [Fact]
    public async Task GetCountsByType_UnknownTypeRow_SilentlyDroppedNotThrown()
    {
        // ED 与 AE-1 刻意不对称的契约护栏：AE-1 钉 `MapItem` 读路径对未知 type **回落 Bookmark**（整页不抛）；
        // GetCountsByTypeAsync 用 `Enum.TryParse(ignoreCase)` 且**无 else 兜底**——未知 type 组被**静默丢弃**
        // （既不抛、也不并入任何已知键）。徽标计数与浏览列表对损坏 type 的处理本就不同，须各自钉死防回归：
        // 若有人把此处改成 `Enum.Parse`（误与 MapItem 对齐），统计页会对一行坏数据整页抛 ArgumentException。
        var repo = new ItemRepository(_factory);
        var ok = new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "g1", Title = "正常star", Uri = "https://github.com/o/a" };
        var bad = new Item { Type = ItemType.File, Source = "test", SourceId = "x1", Title = "坏类型行", Uri = "https://example.com/x" };
        await repo.UpsertAsync(new[] { ok, bad }, CancellationToken.None);

        using (var conn = _factory.Open())
        using (var cmd = conn.CreateCommand())
        {
            // items.type 无 CHECK 约束（AE-1 同源）：降级/手工/坏备份可留未知值。
            cmd.CommandText = "UPDATE items SET type = 'nonsense_type' WHERE source = 'test' AND source_id = 'x1';";
            cmd.ExecuteNonQuery();
        }

        var counts = await repo.GetCountsByTypeAsync(CancellationToken.None); // 能返回即证未抛
        Assert.Equal(new[] { ItemType.GitHubStar }, counts.Keys.ToArray());
        Assert.Equal(1, counts[ItemType.GitHubStar]);
    }
}