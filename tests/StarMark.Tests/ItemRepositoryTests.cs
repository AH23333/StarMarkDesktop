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
}