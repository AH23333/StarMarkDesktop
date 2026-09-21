#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Core.Search;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// SearchService 跨源合并后的兜底过滤：SQL 闸门只覆盖「已入库」条目，实时源
/// （Everything 文件 / Ditto 剪贴板）返回的异构虚拟条目必须按 filter 标量谓词重新核对。
/// </summary>
public sealed class SearchServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;

    public SearchServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_search_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    // 实时源桩：始终可用，SearchAsync 原样返回给定条目——模拟 Ditto/Everything 这类只认
    // MaxResults、返回异构 Type、不经 SQLite SQL 类型闸门的源。
    private sealed class StubSource : IItemSource
    {
        private readonly IReadOnlyList<Item> _items;
        public StubSource(IReadOnlyList<Item> items) => _items = items;
        public string SourceId => "stub";
        public string DisplayName => "stub";
        public bool IsAvailable => true;
        public Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct) => Task.FromResult(_items);
        public Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct) => Task.FromResult(_items);
    }

    [Fact]
    public async Task KeywordSearch_RealTimeSourceItems_RespectTypeFilter()
    {
        // 回归 R-AS-1：来源=Star(Type=GitHubStar) 时，实时源返回的 Clipboard 行必须被剔除。
        // 旧实现合并后只兜底过滤了 Language，漏了 Type → stars-only 列表被剪贴板行污染。
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "a/b",
                       Title = "widget", Uri = "https://github.com/a/b" },
        }, CancellationToken.None);

        var clipboard = new Item
        {
            Type = ItemType.Clipboard, Source = ItemSources.Ditto, SourceId = "ditto:9",
            Title = "widget clipboard note", Uri = string.Empty,
        };
        var svc = new SearchService(repo, new IItemSource[] { new StubSource(new[] { clipboard }) });

        var onlyStar = await svc.SearchAsync("widget",
            new SearchFilter { Type = ItemType.GitHubStar, MaxResults = 10 }, CancellationToken.None);
        Assert.DoesNotContain(onlyStar.Items, i => i.Type == ItemType.Clipboard);
        Assert.Contains(onlyStar.Items, i => i.Type == ItemType.GitHubStar);

        // 护栏：无类型过滤时实时源条目仍应出现（修复不得误伤普通搜索）。
        var all = await svc.SearchAsync("widget",
            new SearchFilter { MaxResults = 10 }, CancellationToken.None);
        Assert.Contains(all.Items, i => i.Type == ItemType.Clipboard);
    }

    [Fact]
    public async Task TagBrowse_AppliesLanguageFilter()
    {
        // 回归 R-AS-2：空关键词 + 标签浏览此前只转发 Tag/Type/Hidden，漏了 Language →
        // 「选了标签 + 选了语言」的浏览态里语言静默失效（语言是持久化的全局工具栏控件）。
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "x/cs",
                       Title = "csharp repo", ExtraJson = """{"Language":"C#"}""" },
            new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "x/rust",
                       Title = "rust repo", ExtraJson = """{"Language":"Rust"}""" },
        }, CancellationToken.None);

        var stored = await repo.GetAllAsync(new BrowseFilter { Limit = 100 }, CancellationToken.None);
        foreach (var it in stored)
            await repo.AddTagAsync(it.Id, "shared", CancellationToken.None);   // 两条同挂一标签

        var svc = new SearchService(repo, Array.Empty<IItemSource>());

        // 语言="C#" 应把共同标签下的两条收窄到一条。
        var filtered = await svc.SearchAsync("",
            new SearchFilter { Tags = new[] { "shared" }, Language = "C#", MaxResults = 10 }, CancellationToken.None);
        Assert.Single(filtered.Items);
        Assert.Equal("x/cs", filtered.Items[0].SourceId);

        // 护栏：不设语言时两条都在（修复不得误伤无语言浏览）。
        var both = await svc.SearchAsync("",
            new SearchFilter { Tags = new[] { "shared" }, MaxResults = 10 }, CancellationToken.None);
        Assert.Equal(2, both.Items.Count);
    }

    [Fact]
    public async Task KeywordSearch_AppliesLanguageFilterToRealTimeItems()
    {
        // 关键词路径下的「语言兜底过滤」臂（SearchService.cs:105-109）：SQL 只过滤已入库条目，
        // 实时源（Everything/Ditto）返回的异构虚拟条目须在此按 filter.Language 重核。
        // 既有 KeywordSearch_..._RespectTypeFilter 覆盖了 Type 臂、TagBrowse 覆盖了浏览路径语言臂，
        // 此臂此前从未被任一测在「非空关键词」下进入。
        var repo = new ItemRepository(_factory);   // 空库 → FTS 返回 0，全实时源
        var rustStar = new Item
        {
            Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "o/rust",
            Title = "widget rust repo", Uri = "https://github.com/o/rust",
            ExtraJson = """{"Language":"Rust"}""",
        };
        var clipboard = new Item
        {
            Type = ItemType.Clipboard, Source = ItemSources.Ditto, SourceId = "ditto:9",
            Title = "widget clipboard note", Uri = string.Empty,
        };
        var svc = new SearchService(repo, new IItemSource[] { new StubSource(new[] { rustStar, clipboard }) });

        // 语言=Rust：无语言的实时剪贴板行应被兜底剔除（GetLanguage 对非 star 恒 null）。
        var filtered = await svc.SearchAsync("widget",
            new SearchFilter { Language = "Rust", MaxResults = 10 }, CancellationToken.None);
        Assert.Contains(filtered.Items, i => i.SourceId == "o/rust");
        Assert.DoesNotContain(filtered.Items, i => i.Type == ItemType.Clipboard);

        // 护栏：不设语言时实时剪贴板行仍保留（兜底过滤不得误伤普通关键词搜索）。
        var all = await svc.SearchAsync("widget",
            new SearchFilter { MaxResults = 10 }, CancellationToken.None);
        Assert.Contains(all.Items, i => i.Type == ItemType.Clipboard);
    }

    [Fact]
    public async Task ExactCount_ClampedToTruncatedItems_WhenMaxResultsSplitsExactSegment()
    {
        // 钉死 SearchService.cs:137 `ExactCount = Math.Min(exact.Count, ordered.Count)` 的取小侧。
        // MaxResults 截断切进「精确匹配」段时，ExactCount 必须跟着降到实际展示条数——否则
        // UI「精确匹配 (N)」表头会比渲染出的列表虚高（SearchPageViewModel 用 i<ExactCount 分段）。
        var repo = new ItemRepository(_factory);   // 空库 → 结果全来自实时源
        var a = new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "o/aa",
                           Title = "widget one", Uri = "https://github.com/o/aa" };
        var b = new Item { Type = ItemType.GitHubStar, Source = ItemSources.GitHub, SourceId = "o/bb",
                           Title = "widget two", Uri = "https://github.com/o/bb" };
        var svc = new SearchService(repo, new IItemSource[] { new StubSource(new[] { a, b }) });

        // 两条 title 均以 "widget" 开头 → 全落精确段（exact.Count=2），但 MaxResults=1 只展示 1 条。
        var result = await svc.SearchAsync("widget",
            new SearchFilter { MaxResults = 1 }, CancellationToken.None);
        Assert.Single(result.Items);
        Assert.Equal(1, result.ExactCount);        // 被 Math.Min 夹到展示数；去掉 Min 会变 2 → 断言失败
    }
}
