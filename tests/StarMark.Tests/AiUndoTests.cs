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
/// P-90「整批撤销这次整理」的两颗批量基元：<see cref="ItemRepository.TagItemsRecordedAsync"/>
/// （读半边——把<b>真的新挂上的差集</b>交回来）与 <see cref="ItemRepository.UntagItemsAsync"/>
/// （写半边——只摘列出来的挂接）。与 <see cref="BulkTaggingTests"/> 同一族的两件事一起守：
/// <b>语义要与"撤销只动 AI 加的、保得住用户手打的"严格一致，往返次数要压成常数</b>。
/// <para>撤销记录只信 Recorded 交回来的差集——应用前就挂着的同名标签不在里面，
/// 撤销时才摘得掉 AI 加的而保得住用户手打的。这条判据在写入事务内部算好（同核心），
/// ⛔ 由调用方再读一遍现状自己算：那是第二个事实源，与写入之间没有隔离。</para>
/// </summary>
public sealed class AiUndoTests : IAsyncLifetime, IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private ItemRepository _repo = null!;

    public AiUndoTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_aiundo_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
    }

    public async Task InitializeAsync()
    {
        _repo = new ItemRepository(_factory);
        await Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() { try { File.Delete(_dbPath); } catch { /* 临时库删不掉不影响结论 */ } }

    private async Task<long> NewItem(string title, string sourceId, params string[] tags)
    {
        var item = new Item
        {
            Type = ItemType.Bookmark,
            Source = ItemSources.Local,
            SourceId = sourceId,
            Title = title,
            Uri = "https://example.test/" + sourceId,
            Tags = tags.ToList(),
        };
        await _repo.RecordItemAsync(item, CancellationToken.None);
        return item.Id;
    }

    // ────────── 读半边：差集只含真的新挂上的 ──────────

    [Fact]
    public async Task TheRecordedOutcomeCarriesOnlyWhatWasActuallyAdded()
    {
        var id = await NewItem("浏览器扩展", "au1", "手打过");

        var outcome = await _repo.TagItemsRecordedAsync(
            new[] { new ItemTagAssignment(id, new[] { "手打过", "AI加" }) }, CancellationToken.None);

        Assert.Equal(1, outcome.ChangedItems);
        var assignment = Assert.Single(outcome.Additions);
        Assert.Equal(id, assignment.ItemId);
        // 用户应用前就有的同名标签⛔ 不进撤销记录——撤销连着它一起摘就是删用户的手工
        Assert.Equal(new[] { "AI加" }, assignment.Tags);
    }

    [Fact]
    public async Task TheRecordedOutcomeIsEmptyWhenNothingWasActuallyAdded()
    {
        var id = await NewItem("原样", "au2", "早就有");

        var outcome = await _repo.TagItemsRecordedAsync(
            new[] { new ItemTagAssignment(id, new[] { "早就有" }) }, CancellationToken.None);

        Assert.Equal(0, outcome.ChangedItems);          // 幂等重放：没有变化
        Assert.Empty(outcome.Additions);                // 也就没有可撤销的——记录空着，按钮不现身
    }

    [Fact]
    public async Task TheRecordedOutcomeSkipsItemsDeletedMidFlight()
    {
        var gone = await NewItem("会被删掉", "au3a");
        var live = await NewItem("还在", "au3b");
        await _repo.DeleteBySourceIdAsync(ItemSources.Local, "au3a", CancellationToken.None);

        var outcome = await _repo.TagItemsRecordedAsync(new[]
        {
            new ItemTagAssignment(gone, new[] { "孤儿" }),
            new ItemTagAssignment(live, new[] { "正常" }),
        }, CancellationToken.None);

        // 删掉的条目一个挂接都没建，撤销记录里也⛔ 有它——否则撤销时对着空气摘标签
        var assignment = Assert.Single(outcome.Additions);
        Assert.Equal(live, assignment.ItemId);
        Assert.Equal(new[] { "正常" }, assignment.Tags);
    }

    // ────────── 写半边：只摘列出来的，其余一根手指都不碰 ──────────

    [Fact]
    public async Task UntagRemovesOnlyTheListedLinksAndKeepsTheRest()
    {
        var id = await NewItem("混合", "au4", "手打甲", "手打乙");

        var changed = await _repo.UntagItemsAsync(
            new[] { new ItemTagAssignment(id, new[] { "手打甲", "没挂过的" }) }, CancellationToken.None);

        Assert.Equal(1, changed);
        // 列到的「手打甲」摘了；没列到的「手打乙」原样保留；列到但本来就没挂的只是跳过
        Assert.Equal(new[] { "手打乙" }, await _repo.GetTagsForItemAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task UntagIsIdempotentSoReplayingItWritesNothing()
    {
        var id = await NewItem("重复", "au5", "AI加");

        await _repo.UntagItemsAsync(new[] { new ItemTagAssignment(id, new[] { "AI加" }) }, CancellationToken.None);
        var changed = await _repo.UntagItemsAsync(
            new[] { new ItemTagAssignment(id, new[] { "AI加" }) }, CancellationToken.None);

        Assert.Equal(0, changed);       // 第二遍没有可摘的：不写、不广播、不记活动
        Assert.Empty(await _repo.GetTagsForItemAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task UntagSkipsDeletedItemsWithoutThrowing()
    {
        var gone = await NewItem("已删", "au6");
        await _repo.DeleteBySourceIdAsync(ItemSources.Local, "au6", CancellationToken.None);

        // 撤销离应用可能隔了很久：条目没了就跳过，而不是让整批撤销报错
        var changed = await _repo.UntagItemsAsync(
            new[] { new ItemTagAssignment(gone, new[] { "AI加" }) }, CancellationToken.None);
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task UntagWritesOneActivityPerChangedItem()
    {
        var a = await NewItem("活动甲", "au7a");
        await NewItem("活动乙", "au7b");
        await _repo.TagItemsAsync(new[]
        {
            new ItemTagAssignment(a, new[] { "AI加" }),
        }, CancellationToken.None);

        await _repo.UntagItemsAsync(new[] { new ItemTagAssignment(a, new[] { "AI加" }) }, CancellationToken.None);

        var acts = await _repo.GetActivityAsync(50, CancellationToken.None);
        Assert.Equal(2, acts.Count(act => act.Title == "活动甲"));    // 加一条「修改」＋摘一条「修改」
        Assert.All(acts.Where(act => act.Title == "活动甲"),
            act => Assert.Equal(ActivityKind.ItemModify, act.Kind));
    }

    // ────────── 端到端：应用 → 撤销 → 回原样 ──────────

    [Fact]
    public async Task ApplyingThenUndoingRestoresTheOriginalTagSet()
    {
        var kept = await NewItem("保得住手打", "au8", "手打过");
        var fresh = await NewItem("无标签", "au8b");

        var outcome = await _repo.TagItemsRecordedAsync(new[]
        {
            new ItemTagAssignment(kept, new[] { "手打过", "AI加" }),
            new ItemTagAssignment(fresh, new[] { "AI加" }),
        }, CancellationToken.None);
        var removed = await _repo.UntagItemsAsync(outcome.Additions, CancellationToken.None);

        Assert.Equal(2, outcome.ChangedItems);
        Assert.Equal(2, removed);
        // 用户手打的保得住；AI 加的摘干净——「回原样」的两半都得起算
        Assert.Equal(new[] { "手打过" }, await _repo.GetTagsForItemAsync(kept, CancellationToken.None));
        Assert.Empty(await _repo.GetTagsForItemAsync(fresh, CancellationToken.None));
    }

    // ────────── 往返次数（与 BulkTaggingTests 同一把尺） ──────────

    [Fact]
    public async Task AWholeUndoCostsOneConnection()
    {
        var ids = new List<long>();
        for (var i = 0; i < 30; i++) ids.Add(await NewItem("撤销批量" + i, "au9-" + i));
        await _repo.TagItemsAsync(
            ids.Select(id => new ItemTagAssignment(id, new[] { "AI加", "另一个" })).ToList(), CancellationToken.None);

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        var changed = await _repo.UntagItemsAsync(
            ids.Select(id => new ItemTagAssignment(id, new[] { "AI加", "另一个" })).ToList(), CancellationToken.None);
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(30, changed);
        Assert.Equal(1, opens);        // 整批一次：撤销 500 条与撤销 5 条在"开几次库"上没有差别
    }

    [Fact]
    public async Task EmptyUntagInputCostsNoConnectionAtAll()
    {
        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();

        Assert.Equal(0, await _repo.UntagItemsAsync(Array.Empty<ItemTagAssignment>(), CancellationToken.None));
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(0, opens);        // 空集就是空集：不开库、不假装忙过
    }
}
