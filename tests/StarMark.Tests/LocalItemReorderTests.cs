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
/// 拖动排序的写入出口（批次 PA-4）。守三点：<b>顺序确实按给定次序落地</b>、
/// <b>只该改的两列被改</b>（顺序不在全文索引里，整行覆盖是白担风险）、
/// <b>一次拖动只有一次往返</b>。
/// </summary>
public sealed class LocalItemReorderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;

    public LocalItemReorderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_reorder_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private async Task<List<Item>> TodosAsync() =>
        (await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Todo, 2000, CancellationToken.None)).ToList();

    private async Task<long[]> SeedAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await _repo.UpsertLocalItemAsync(new Item
            {
                Type = ItemType.Todo,
                Source = ItemSources.Local,
                SourceId = "inst1|" + i,
                Title = "待办 " + i,
                Notes = "备注 " + i,
            }, CancellationToken.None);
        }
        return (await TodosAsync()).Select(item => item.Id).ToArray();
    }

    [Fact]
    public async Task TheGivenSequenceBecomesTheStoredOrder()
    {
        var ids = await SeedAsync(5);
        var wanted = new[] { ids[3], ids[0], ids[4], ids[1], ids[2] };

        var changed = await _repo.ReorderLocalItemsAsync(wanted, CancellationToken.None);

        Assert.Equal(5, changed);
        var rows = (await TodosAsync()).ToDictionary(item => item.Id, item => LocalItemState.GetOrder(item));
        for (var position = 0; position < wanted.Length; position++)
            Assert.Equal(position, rows[wanted[position]]);
    }

    /// <summary>顺序不是全文索引的一部分，且整行覆盖会连带把别的列按调用方手上的旧值写回去。
    /// 所以这里必须证明：标题/备注/隐藏位都没被动过。</summary>
    [Fact]
    public async Task OnlyOrderAndUpdatedAtMoveNothingElse()
    {
        var ids = await SeedAsync(3);
        await _repo.SetNoteAsync(ids[0], "后来改的笔记", CancellationToken.None);
        await _repo.SetHiddenAsync(ids[1], true, CancellationToken.None);

        await _repo.ReorderLocalItemsAsync(new[] { ids[2], ids[1], ids[0] }, CancellationToken.None);

        Assert.Equal("后来改的笔记", await _repo.GetNoteAsync(ids[0], CancellationToken.None));
        var visible = await _repo.GetAllAsync(new BrowseFilter { IncludeHidden = true, Limit = 100 }, CancellationToken.None);
        Assert.True(visible.First(item => item.Id == ids[1]).Hidden);
        Assert.Contains(visible, item => item.Title == "待办 0");
    }

    [Fact]
    public async Task AReorderThatChangesNothingWritesNothing()
    {
        var ids = await SeedAsync(3);
        await _repo.ReorderLocalItemsAsync(ids, CancellationToken.None);
        var stamp = (await TodosAsync()).ToDictionary(item => item.Id, item => item.UpdatedAt);

        var again = await _repo.ReorderLocalItemsAsync(ids, CancellationToken.None);

        Assert.Equal(0, again);                                       // 拖回原位不该产生第二次写
        Assert.Equal(stamp, (await TodosAsync()).ToDictionary(item => item.Id, item => item.UpdatedAt));
    }

    [Fact]
    public async Task IdsNotInTheListKeepTheirOwnOrder()
    {
        var ids = await SeedAsync(4);

        await _repo.ReorderLocalItemsAsync(new[] { ids[1], ids[0] }, CancellationToken.None);   // 只排前两条

        var rows = (await TodosAsync()).ToDictionary(item => item.Id, item => LocalItemState.GetOrder(item));
        Assert.Equal(0, rows[ids[1]]);
        Assert.Equal(1, rows[ids[0]]);
        Assert.Null(rows[ids[2]]);                                    // 没参与的不会被顺手写成 2/3
        Assert.Null(rows[ids[3]]);
    }

    [Fact]
    public async Task ADeletedItemInTheListIsSkippedWithoutFailing()
    {
        var ids = await SeedAsync(2);
        await _repo.DeleteBySourceIdAsync(ItemSources.Local, "inst1|1", CancellationToken.None);

        var changed = await _repo.ReorderLocalItemsAsync(new[] { ids[1], ids[0] }, CancellationToken.None);

        // 批量整理/拖动期间条目被删是常态：给剩下那条写对位置，而不是抛
        Assert.Equal(1, changed);
        var rows = (await TodosAsync()).ToDictionary(item => item.Id, item => LocalItemState.GetOrder(item));
        Assert.Equal(1, rows[ids[0]]);
    }

    [Fact]
    public async Task OneDragIsOneConnectionWhileTheLoopCostedOnePerItem()
    {
        var ids = await SeedAsync(20);

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        await _repo.ReorderLocalItemsAsync(ids.Reverse().ToArray(), CancellationToken.None);
        var bulkOpens = counter.Connections;

        var loop = new DbActivityCounter();
        _factory.Counter = loop.Reset();
        foreach (var (position, id) in ids.Select((id, position) => (position, id)))
        {
            var item = (await TodosAsync()).First(row => row.Id == id);
            LocalItemState.SetOrder(item, position);
            await _repo.UpsertLocalItemAsync(item, CancellationToken.None);
        }
        var loopOpens = loop.Connections;
        _factory.Counter = null;

        Assert.Equal(1, bulkOpens);
        Assert.True(loopOpens > 20, $"逐条写应当远超一次连接，实测 {loopOpens}");   // 每条各开一次库（还各重建一次索引）
    }

    [Fact]
    public async Task FiveHundredItemsStillCostOneConnectionAcrossTheChunks()
    {
        var ids = await SeedAsync(500);

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        var changed = await _repo.ReorderLocalItemsAsync(ids.Reverse().ToArray(), CancellationToken.None);
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(500, changed);
        Assert.Equal(1, opens);                                       // IN 分块只拆语句，不多开连接
    }

    [Fact]
    public async Task EmptyOrBogusInputOpensNoConnectionAtAll()
    {
        await SeedAsync(2);
        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();

        Assert.Equal(0, await _repo.ReorderLocalItemsAsync(Array.Empty<long>(), CancellationToken.None));
        Assert.Equal(0, await _repo.ReorderLocalItemsAsync(new long[] { 0, -3 }, CancellationToken.None));
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(0, opens);
    }
}
