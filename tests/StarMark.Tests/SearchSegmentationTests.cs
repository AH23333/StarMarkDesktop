#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Core.Search;
using StarMark.Data;

namespace StarMark.Tests;

/// <summary>
/// P2-7 搜索体验：精确/相关分段、语言筛选、starred/collected 排序。
/// 分段规则照搬扩展 selectors.ts：isStrong = title.startsWith(needle) || url.includes(needle)（不分大小写）。
/// </summary>
public sealed class SearchSegmentationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;

    public SearchSegmentationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_seg_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private static Item MakeItem(long id, string title, string uri, ItemType type = ItemType.Bookmark,
        string? language = null, long createdAt = 100, long updatedAt = 100)
    {
        var item = new Item
        {
            Id = id,
            Type = type,
            Source = "test",
            SourceId = $"t-{id}",
            Title = title,
            Subtitle = string.Empty,
            Uri = uri,
            SearchText = $"{title} {uri}",
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
        if (language is not null)
        {
            item.ExtraJson = JsonSerializer.Serialize(new GitHubStarMeta { Language = language });
        }
        return item;
    }

    private SearchService MakeService()
        => new(_repo, Array.Empty<IItemSource>()); // 无实时源，纯走 SQLite

    [Fact]
    public async Task Segmentation_TitlePrefix_GoesToExact()
    {
        // 注意：FTS 索引只含 title+description+notes+tags（URI 不入索引）
        await _repo.UpsertAsync(new[]
        {
            // 标题以关键词开头 → 精确匹配
            MakeItem(1, "spring framework", "https://example.com/green"),
            // 标题命中但非前缀、URI 不含 → 相关结果
            MakeItem(2, "learn spring in action", "https://example.com/notes"),
        }, CancellationToken.None);

        var result = await MakeService().SearchAsync("spring", new SearchFilter { MaxResults = 50 }, CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.ExactCount);
        Assert.Equal("spring framework", result.Items[0].Title);
        Assert.Equal("learn spring in action", result.Items[1].Title);
    }

    [Fact]
    public async Task Segmentation_UriContains_GoesToExact()
    {
        await _repo.UpsertAsync(new[]
        {
            // 标题不含关键词开头，但 URI 包含 → 仍算精确匹配（url.includes 规则）
            MakeItem(1, "微软 MAUI 文档", "https://docs.microsoft.com/dotnet/maui"),
            // 标题与 URI 都不含关键词开头/包含 → 相关结果段
            MakeItem(2, "关于 MAUI 框架的教程合集", "https://example.com/hello"),
        }, CancellationToken.None);

        var result = await MakeService().SearchAsync("maui", new SearchFilter { MaxResults = 50 }, CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.ExactCount);
        Assert.Equal("微软 MAUI 文档", result.Items[0].Title);
        Assert.Equal("关于 MAUI 框架的教程合集", result.Items[1].Title);
    }

    [Fact]
    public async Task Segmentation_Neither_RunsInRelated()
    {
        await _repo.UpsertAsync(new[]
        {
            MakeItem(1, "学习 maui 的笔记", "https://example.com/notes"),
        }, CancellationToken.None);

        var result = await MakeService().SearchAsync("maui", new SearchFilter { MaxResults = 50 }, CancellationToken.None);

        // 命中但既非标题前缀也非 URI 包含 → 相关结果段
        Assert.Equal(1, result.Total);
        Assert.Equal(0, result.ExactCount);
    }

    [Fact]
    public async Task LanguageFilter_FiltersGitHubStarsByExtraJson()
    {
        await _repo.UpsertAsync(new[]
        {
            MakeItem(1, "rust tool", "https://github.com/a/rust-tool", ItemType.GitHubStar, language: "Rust"),
            MakeItem(2, "go tool", "https://github.com/a/go-tool", ItemType.GitHubStar, language: "Go"),
            MakeItem(3, "rust tool notes", "https://example.com/rust-notes"),
        }, CancellationToken.None);

        // 语言=Rust：只留 1 条（书签无语言字段，被排除）
        var filtered = await _repo.SearchAsync("tool", new SearchFilter { MaxResults = 50, Language = "Rust" }, CancellationToken.None);
        Assert.Single(filtered.Items);
        Assert.Equal("rust tool", filtered.Items[0].Title);

        // 不筛选：3 条都在
        var all = await _repo.SearchAsync("tool", new SearchFilter { MaxResults = 50 }, CancellationToken.None);
        Assert.Equal(3, all.Total);
    }

    [Fact]
    public async Task SortStarred_UsesStarredAtFromExtraJson()
    {
        await _repo.UpsertAsync(new[]
        {
            MakeItem(1, "old star repo", "https://github.com/a/old", ItemType.GitHubStar, createdAt: 1000, updatedAt: 1000),
            MakeItem(2, "new star repo", "https://github.com/a/new", ItemType.GitHubStar, createdAt: 1000, updatedAt: 1000),
        }, CancellationToken.None);
        // 单独写 extra_json 的 StarredAt（UpsertAsync 不覆盖已传入的 ExtraJson，故直接重写两行）
        using (var conn = _factory.Open())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE items SET extra_json = @e WHERE id = @id";
            cmd.Parameters.AddWithValue("@e", JsonSerializer.Serialize(new GitHubStarMeta { StarredAt = 5000 }));
            cmd.Parameters.AddWithValue("@id", 1L);
            cmd.ExecuteNonQuery();
            cmd.Parameters.Clear();
            cmd.CommandText = "UPDATE items SET extra_json = @e WHERE id = @id";
            cmd.Parameters.AddWithValue("@e", JsonSerializer.Serialize(new GitHubStarMeta { StarredAt = 9000 }));
            cmd.Parameters.AddWithValue("@id", 2L);
            cmd.ExecuteNonQuery();
        }

        var result = await _repo.SearchAsync("star", new SearchFilter { MaxResults = 50, Sort = "starred" }, CancellationToken.None);

        // 按 StarredAt 降序：new star(9000) 在 old star(5000) 之前
        Assert.Equal(2, result.Total);
        Assert.Equal("new star repo", result.Items[0].Title);
        Assert.Equal("old star repo", result.Items[1].Title);
    }

    [Fact]
    public async Task SortCollected_UsesCreatedAt()
    {
        await _repo.UpsertAsync(new[]
        {
            MakeItem(1, "collected early", "https://example.com/early", createdAt: 1000),
            MakeItem(2, "collected late", "https://example.com/late", createdAt: 8000),
        }, CancellationToken.None);

        var result = await _repo.SearchAsync("collected", new SearchFilter { MaxResults = 50, Sort = "collected" }, CancellationToken.None);

        // 按收藏（入库）时间降序
        Assert.Equal("collected late", result.Items[0].Title);
        Assert.Equal("collected early", result.Items[1].Title);
    }
}
