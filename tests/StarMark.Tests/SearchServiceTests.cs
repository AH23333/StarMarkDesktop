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
}
