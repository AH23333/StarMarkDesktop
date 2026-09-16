#nullable enable
using System;
using System.IO;
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
}