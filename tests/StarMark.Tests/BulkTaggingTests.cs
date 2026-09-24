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
/// 批量打标签的写入基元（批次 PA-1）。<b>两件事一起守：语义要与逐条调用严格一致，
/// 往返次数要压成常数</b>——前者错了就是用户库里的数据被改坏，后者没了这条优化就会在下次改动里悄悄退化。
/// <para>往返用的是<b>连接计数</b>而不是秒表：秒表在这种环境下测不出任何可回归的结论，
/// 而"这批活开了几次库连接"是个确定的整数（每次 Open 都带一遍 PRAGMA）。</para>
/// </summary>
public sealed class BulkTaggingTests : IAsyncLifetime, IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private ItemRepository _repo = null!;

    public BulkTaggingTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_bulktag_{Guid.NewGuid():N}.db");
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

    // ────────── 语义 ──────────

    [Fact]
    public async Task AddsOnlyWhatIsMissingAndNeverReplacesExisting()
    {
        var id = await NewItem("浏览器扩展", "bt1", "手打过");

        var changed = await _repo.TagItemsAsync(new[] { new ItemTagAssignment(id, new[] { "手打过", "新标签" }) }, CancellationToken.None);

        Assert.Equal(1, changed);
        // 顺序按码点：手(U+624B) 在 新(U+65B0) 之前
        Assert.Equal(new[] { "手打过", "新标签" }, (await _repo.GetTagsForItemAsync(id, CancellationToken.None)).OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReusesTheSameTagRowRegardlessOfCase()
    {
        var a = await NewItem("甲", "bt2a");
        var b = await NewItem("乙", "bt2b");

        await _repo.TagItemsAsync(new[]
        {
            new ItemTagAssignment(a, new[] { "FrontEnd" }),
            new ItemTagAssignment(b, new[] { "frontend" }),
        }, CancellationToken.None);

        // 两个写法必须落在同一行 tags 上：否则标签列表会出现两个看起来一样的类目（正是用户要整理掉的东西）
        var catalog = await _repo.GetAllTagsAsync(CancellationToken.None);
        Assert.Single(catalog, row => string.Equals(row.Name, "FrontEnd", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NothingToAddMeansNoWriteNoActivityFootprint()
    {
        var id = await NewItem("丙", "bt3", "工具");
        await _repo.TagItemsAsync(new[] { new ItemTagAssignment(id, new[] { "工具" }) }, CancellationToken.None);
        var before = (await _repo.GetActivityAsync(500, CancellationToken.None)).Count;

        var again = await _repo.TagItemsAsync(new[] { new ItemTagAssignment(id, new[] { "工具 " }) }, CancellationToken.None);

        // 幂等重放（用户连点两次「全部应用」）不该在活动流里留下第二笔
        Assert.Equal(0, again);
        Assert.Equal(before, (await _repo.GetActivityAsync(500, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task SearchTextIsRebuiltWithTheTagsIncluded()
    {
        var id = await NewItem("2023年度报告", "bt4");

        await _repo.TagItemsAsync(new[] { new ItemTagAssignment(id, new[] { "财报" }) }, CancellationToken.None);

        var text = await SearchTextOfAsync(id);
        Assert.Contains("财报", text);                       // 标签要能搜到（AM/EP 两批的教训都在这条上）
        Assert.Contains("2023", text);
        Assert.Contains("年度", text);                       // 中文在索引侧被展开过，不是原样塞进去
    }

    private async Task<string> SearchTextOfAsync(long id)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT search_text FROM items WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task MissingAndBlankInputIsFoldedAway()
    {
        var id = await NewItem("丁", "bt5");

        var changed = await _repo.TagItemsAsync(new[]
        {
            new ItemTagAssignment(id, new string?[] { "  ", null, " 甲 ", "甲", "乙" }!),
            new ItemTagAssignment(0, new[] { "不该被理会" }),
            new ItemTagAssignment(-7, new[] { "也不该" }),
        }, CancellationToken.None);

        Assert.Equal(1, changed);                            // 只有那个真实存在的条目算变化
        // 顺序按码点：乙(U+4E59) 在 甲(U+7532) 之前
        Assert.Equal(new[] { "乙", "甲" }, (await _repo.GetTagsForItemAsync(id, CancellationToken.None)).OrderBy(t => t, StringComparer.Ordinal));
        Assert.Empty(await _repo.GetTagsForItemAsync(-7, CancellationToken.None));
    }

    [Fact]
    public async Task ItemsDeletedMidFlightAreSkippedNotFatal()
    {
        var gone = await NewItem("会被删掉", "bt6");
        var live = await NewItem("还在", "bt7");
        await _repo.DeleteBySourceIdAsync(ItemSources.Local, "bt6", CancellationToken.None);

        var changed = await _repo.TagItemsAsync(new[]
        {
            new ItemTagAssignment(gone, new[] { "孤儿" }),
            new ItemTagAssignment(live, new[] { "正常" }),
        }, CancellationToken.None);

        // 批量整理要跑几分钟，期间用户删条目是正常事：跳过它，而不是让整批回滚
        Assert.Equal(1, changed);
        Assert.Equal(new[] { "正常" }, await _repo.GetTagsForItemAsync(live, CancellationToken.None));
    }

    [Fact]
    public async Task OneActivityPerChangedItemWithItsOwnKey()
    {
        var a = await NewItem("活动甲", "bt8a");
        await NewItem("活动乙", "bt8b");

        await _repo.TagItemsAsync(new[]
        {
            new ItemTagAssignment(a, new[] { "标签甲" }),
        }, CancellationToken.None);

        var acts = await _repo.GetActivityAsync(50, CancellationToken.None);
        Assert.Single(acts, act => act.Title == "活动甲");
        var mine = acts.First(act => act.Title == "活动甲");
        Assert.Equal(ActivityKind.ItemModify, mine.Kind);
        Assert.Equal($"{ItemSources.Local}:bt8a", mine.ItemKey);     // 键要能指回这一行：撤销/排查都靠它
    }

    // ────────── 往返次数（这条批次存在的理由） ──────────

    [Fact]
    public async Task AWholeBatchCostsOneConnection_WhileTheLoopCostsOnePerTag()
    {
        var ids = new List<long>();
        for (var i = 0; i < 40; i++) ids.Add(await NewItem("批量" + i, "bt9-" + i));
        var assignments = ids.Select(id => new ItemTagAssignment(id, new[] { "共用标签", "另一个" })).ToList();

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        var changed = await _repo.TagItemsAsync(assignments, CancellationToken.None);
        var bulkOpens = counter.Connections;

        // 逐条 AddTagAsync：一个条目两个标签就是两次连接（每次还带一遍 PRAGMA）
        var loopCounter = new DbActivityCounter();
        _factory.Counter = loopCounter.Reset();
        foreach (var assignment in assignments)
            foreach (var tag in assignment.Tags)
                await _repo.AddTagAsync(assignment.ItemId, tag, CancellationToken.None);
        var loopOpens = loopCounter.Connections;
        _factory.Counter = null;

        Assert.Equal(40, changed);
        Assert.Equal(1, bulkOpens);                                   // 整批一次
        Assert.Equal(80, loopOpens);                                  // 条目 × 标签
        Assert.True(bulkOpens * 4 < loopOpens, $"批量路径 {bulkOpens} 次连接，逐条 {loopOpens} 次——省得不够明显就说明这条优化退了");
    }

    [Fact]
    public async Task FiveHundredItemsStillUseOneConnectionAcrossTheInClauseChunks()
    {
        var ids = new List<long>();
        for (var i = 0; i < 500; i++) ids.Add(await NewItem("分块" + i, "btc-" + i));

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        var changed = await _repo.TagItemsAsync(
            ids.Select(id => new ItemTagAssignment(id, new[] { "分块标签" })).ToList(), CancellationToken.None);
        var opens = counter.Connections;
        _factory.Counter = null;

        // IN (…) 分块只是把一条语句拆成几条，不许因此多开连接（400 一块，500 条会走两块）
        Assert.Equal(500, changed);
        Assert.Equal(1, opens);
        Assert.Contains("分块标签", await _repo.GetTagsForItemAsync(ids[^1], CancellationToken.None));
    }

    [Fact]
    public async Task EmptyInputCostsNoConnectionAtAll()
    {
        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();

        Assert.Equal(0, await _repo.TagItemsAsync(Array.Empty<ItemTagAssignment>(), CancellationToken.None));
        Assert.Equal(0, await _repo.TagItemsAsync(new[] { new ItemTagAssignment(0, Array.Empty<string>()) }, CancellationToken.None));
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(0, opens);       // 空集就是空集：不开库、不假装忙过
    }

    [Fact]
    public async Task ActivityRingStillHoldsAtFiveHundredAfterABulkApply()
    {
        for (var i = 0; i < 505; i++) await NewItem("灌满" + i, "btd-" + i);
        var ids = (await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Bookmark, 600, CancellationToken.None))
            .Select(item => item.Id).ToList();

        await _repo.TagItemsAsync(ids.Select(id => new ItemTagAssignment(id, new[] { "裁剪" })).ToList(), CancellationToken.None);

        // 多行 INSERT + 末尾裁一次，与逐条写的最终形态一致（都是最近 500 条）
        Assert.True((await _repo.GetActivityAsync(600, CancellationToken.None)).Count <= 500);
    }
}
