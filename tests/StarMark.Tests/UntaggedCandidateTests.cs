#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// "还没有标签的条目"这一条查询。<b>它守的不是速度而是少报</b>：
/// 先取一批再在内存里筛会带一个窗口，窗口会把早期真正待整理的条目静默吞掉，
/// 而界面会老实报告"没有待整理的条目"——少报比报错难发现得多。
/// </summary>
public sealed class UntaggedCandidateTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;

    public UntaggedCandidateTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_untagged_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private async Task<long> New(string sourceId, ItemType type = ItemType.Bookmark, params string[] tags)
    {
        var item = new Item
        {
            Type = type,
            Source = ItemSources.Local,
            SourceId = sourceId,
            Title = "条目标题 " + sourceId,
            Uri = "https://example.test/" + sourceId,
            Tags = tags.ToList(),
        };
        await _repo.RecordItemAsync(item, CancellationToken.None);
        return item.Id;
    }

    /// <summary>把条目的更新时间推早。</summary>
    /// <remarks><c>updated_at</c> 只有秒级精度：同一秒里插入的一批条目之间顺序不保证，
    /// 所以凡是拿顺序做断言的用例都必须显式拉开时间，否则今天绿明天红。</remarks>
    private async Task AgeAsync(long id, int seconds)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE items SET updated_at = updated_at - @s WHERE id = @id;";
        cmd.Parameters.AddWithValue("@s", seconds);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task OnlyUntaggedComeBackAndTheirTagsAreEmptyByDefinition()
    {
        var clean = await New("ut1");
        await New("ut2", tags: "已有");

        var rows = await _repo.GetUntaggedAsync(new[] { ItemType.Bookmark }, 50, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(clean, row.Id);
        Assert.Empty(row.Tags);
    }

    /// <summary>这条是整批的重点：<b>最近几千条都打了标签时，早期没标签的那条必须在</b>。
    /// 用"取最近 N 条再筛"的写法，这条会静默返回空。</summary>
    [Fact]
    public async Task ARecentTaggedFloodDoesNotHideAnOlderUntaggedItem()
    {
        var old = await New("ut-old");
        for (var i = 0; i < 120; i++) await New($"ut-flood-{i}", tags: "标签" + (i % 5));
        await AgeAsync(old, 3600);                       // 让它确实排在窗口之外（而不是靠插入顺序碰运气）

        var rows = await _repo.GetUntaggedAsync(new[] { ItemType.Bookmark }, 10, CancellationToken.None);

        Assert.Contains(old, rows.Select(row => row.Id).ToList());
    }

    /// <summary>
    /// 反面用例：把"取最近 N 条再在内存里筛没标签的"这个旧写法本身测一遍——
    /// <b>它确实会漏</b>。留着这条不是为了怀旧，而是为了说明新接口在守什么：
    /// 窗口一旦被人"为了省事"改回去，这里会红，而界面上只会显示"没有待整理的条目"。
    /// </summary>
    [Fact]
    public async Task TheOldWindowShapeWouldHaveMissedIt()
    {
        var old = await New("ut-window-old");
        for (var i = 0; i < 60; i++) await New($"ut-window-flood-{i}", tags: "占位标签");
        await AgeAsync(old, 3600);

        var window = await _repo.GetAllAsync(new BrowseFilter { Limit = 20, Sort = "recent" }, CancellationToken.None);
        var viaWindow = window.Where(item => item.Tags.Count == 0).Select(item => item.Id).ToList();

        Assert.DoesNotContain(old, viaWindow);                                   // 前 20 条全带标签 → 一条候选都出不来
        Assert.Contains(old, (await _repo.GetUntaggedAsync(new[] { ItemType.Bookmark }, 5, CancellationToken.None))
            .Select(item => item.Id).ToList());                                  // 下推之后才找得到
    }

    [Fact]
    public async Task LimitIsHonouredExactly()
    {
        for (var i = 0; i < 25; i++) await New($"ut-lim-{i}");

        Assert.Equal(10, (await _repo.GetUntaggedAsync(new[] { ItemType.Bookmark }, 10, CancellationToken.None)).Count);
        Assert.Equal(25, (await _repo.GetUntaggedAsync(new[] { ItemType.Bookmark }, 999, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task TypesActAsAWhitelist()
    {
        var note = await New("ut-note", ItemType.Note);
        var file = await New("ut-file", ItemType.File);
        await New("ut-star", ItemType.GitHubStar);

        var rows = await _repo.GetUntaggedAsync(new[] { ItemType.Note, ItemType.File }, 50, CancellationToken.None);

        Assert.Equal(new[] { file, note }.OrderBy(id => id), rows.Select(row => row.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task HiddenItemsAreNotOffered()
    {
        var hidden = await New("ut-hid");
        await _repo.SetHiddenAsync(hidden, true, CancellationToken.None);

        Assert.DoesNotContain(hidden, (await _repo.GetUntaggedAsync(new[] { ItemType.Bookmark }, 50, CancellationToken.None)).Select(row => row.Id));
    }

    /// <summary>一个标签刚被去掉，条目就该重新成为候选——<b>整理过的东西可以再来一次</b>，
    /// 而且这条判定必须实时，不能依赖任何缓存的"已整理清单"。</summary>
    [Fact]
    public async Task AnItemBecomesCandidateAgainOnceItsLastTagIsRemoved()
    {
        var id = await New("ut-cycle", tags: "唯一标签");
        Assert.Empty(await _repo.GetUntaggedAsync(new[] { ItemType.Bookmark }, 50, CancellationToken.None));

        await _repo.RemoveTagAsync(id, "唯一标签", CancellationToken.None);

        Assert.Contains(id, (await _repo.GetUntaggedAsync(new[] { ItemType.Bookmark }, 50, CancellationToken.None)).Select(row => row.Id));
    }

    [Fact]
    public async Task PinnedFirstThenNewest_MatchingTheBrowsePageOrder()
    {
        var plain = await New("ut-p1");
        var pinned = await New("ut-p2");
        await _repo.SetPinnedAsync(pinned, true, CancellationToken.None);

        var rows = await _repo.GetUntaggedAsync(new[] { ItemType.Bookmark }, 50, CancellationToken.None);

        Assert.Equal(new[] { pinned, plain }, rows.Select(row => row.Id));   // 与浏览页 "recent" 档同口径
    }

    [Fact]
    public async Task OneQueryOneConnectionRegardlessOfHowManyAreAskedFor()
    {
        for (var i = 0; i < 200; i++) await New($"ut-perf-{i}");

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        var rows = await _repo.GetUntaggedAsync(new[] { ItemType.Bookmark }, 150, CancellationToken.None);
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(150, rows.Count);
        Assert.Equal(1, opens);       // 一次取回，不是"每行再问一次标签"
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ANonPositiveLimitAsksTheDatabaseNothing(int limit)
    {
        await New("ut-zero");
        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();

        Assert.Empty(await _repo.GetUntaggedAsync(new[] { ItemType.Bookmark }, limit, CancellationToken.None));
        Assert.Equal(0, counter.Connections);
        _factory.Counter = null;
    }

    [Fact]
    public async Task AnEmptyTypeListAsksTheDatabaseNothing()
    {
        await New("ut-none");
        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();

        Assert.Empty(await _repo.GetUntaggedAsync(Array.Empty<ItemType>(), 10, CancellationToken.None));
        Assert.Equal(0, counter.Connections);
        _factory.Counter = null;
    }
}
