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
/// 一次拖入的落库出口（批次 PA-6）。拖 N 个文件进快捷启动时，界面侧只该读一次档、写一次盘，
/// 库侧只该开一次连接——这两条都由<b>整数</b>（连接数）断言，不靠秒表感觉。
/// </summary>
public sealed class BulkDropWritesTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;

    public BulkDropWritesTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_drop_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    /// <summary>拖入项在库里的样子：按路径生成的文件系统条目（Id=0，业务键是路径哈希）。</summary>
    private static Item Draft(string path, string? title = null) => LocalFileIdentity.FromPath(path, title);

    private static List<Item> Drafts(int count, string dir = @"C:\拖入")
        => Enumerable.Range(0, count).Select(i => Draft($@"{dir}\文件 {i}.txt")).ToList();

    private async Task<List<Item>> AllAsync()
        => (await _repo.GetAllAsync(new BrowseFilter { Limit = 5000, IncludeHidden = true }, CancellationToken.None)).ToList();

    // ── 登记：整批一次连接 ──

    [Fact]
    public async Task TheWholeDropCostsOneConnection()
    {
        var drafts = Drafts(60);

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        var recorded = await _repo.RecordItemsAsync(drafts, CancellationToken.None);
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(60, recorded);
        Assert.Equal(1, opens);                                   // 逐条登记时这里是 60（每次还带三遍 PRAGMA）
        Assert.Equal(60, drafts.Count(item => item.Id > 0));      // 真实 Id 全部回填
        Assert.Equal(60, (await AllAsync()).Count(item => item.Source == ItemSources.FileSystem));
    }

    /// <summary>反向对照：被取代的那个循环形状确实每项一趟往返。<b>没有这条，"1 次"可能只是计数器坏了。</b></summary>
    [Fact]
    public async Task TheLoopItReplacedCostsOneConnectionPerFile()
    {
        var drafts = Drafts(40);

        var bulk = new DbActivityCounter();
        _factory.Counter = bulk.Reset();
        await _repo.RecordItemsAsync(drafts, CancellationToken.None);
        var bulkOpens = bulk.Connections;

        var loop = new DbActivityCounter();
        _factory.Counter = loop.Reset();
        foreach (var draft in Drafts(40))
            await _repo.RecordItemAsync(draft, CancellationToken.None);
        var loopOpens = loop.Connections;
        _factory.Counter = null;

        Assert.Equal(1, bulkOpens);
        Assert.Equal(40, loopOpens);
    }

    /// <summary>一次拖放里带进同一个文件两遍（资源管理器里很常见）：只登记一次，
    /// 第二次不改任何事实，却要白重建一次全文索引。</summary>
    [Fact]
    public async Task APathDroppedTwiceInOneBatchIsRecordedOnce()
    {
        var twice = new[] { Draft(@"C:\拖入\同一份.txt"), Draft(@"C:\拖入\同一份.txt") };

        var recorded = await _repo.RecordItemsAsync(twice, CancellationToken.None);

        Assert.Equal(1, recorded);
        Assert.Single(await AllAsync());
    }

    [Fact]
    public async Task ReDroppingAnExistingPathKeepsWhatTheUserSet()
    {
        var path = @"C:\拖入\已置顶.txt";
        var first = Draft(path, "已置顶");
        await _repo.RecordItemAsync(first, CancellationToken.None);
        await _repo.SetPinnedAsync(first.Id, true, CancellationToken.None);
        await _repo.SetNoteAsync(first.Id, "用户笔记", CancellationToken.None);

        var recorded = await _repo.RecordItemsAsync(new[] { Draft(path, "已置顶") }, CancellationToken.None);

        Assert.Equal(1, recorded);
        var row = Assert.Single(await AllAsync());
        Assert.Equal(first.Id, row.Id);                           // 命中同一行，不是新行
        Assert.True(row.Pinned);                                  // 重拖不许把用户态冲掉
        Assert.Equal("用户笔记", await _repo.GetNoteAsync(row.Id, CancellationToken.None));
    }

    /// <summary>缺业务键的那些<b>不写</b>，但也不许连累同批其它项——返回值只数真正登记成功的。</summary>
    [Fact]
    public async Task ItemsWithoutABusinessKeyAreSkippedNotFatal()
    {
        var batch = new List<Item>
        {
            Draft(@"C:\拖入\能进.txt"),
            new() { Type = ItemType.File, Source = "", SourceId = "有键没来源" },
            new() { Type = ItemType.File, Source = ItemSources.FileSystem, SourceId = "" },
            Draft(@"C:\拖入\也能进.txt"),
        };

        var recorded = await _repo.RecordItemsAsync(batch, CancellationToken.None);

        Assert.Equal(2, recorded);
        Assert.Equal(2, (await AllAsync()).Count);
    }

    /// <summary>空批 / 整批都没键：<b>连库都不开</b>。开了就要发一圈空语句、还要惊动一次组件通知。</summary>
    [Fact]
    public async Task AnEmptyOrKeylessBatchOpensNoConnectionAtAll()
    {
        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();

        Assert.Equal(0, await _repo.RecordItemsAsync(Array.Empty<Item>(), CancellationToken.None));
        Assert.Equal(0, await _repo.RecordItemsAsync(
            new[] { new Item { Type = ItemType.File, Source = "", SourceId = "" } }, CancellationToken.None));
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task FiveHundredPathsStillCostOneConnection()
    {
        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        var recorded = await _repo.RecordItemsAsync(Drafts(500), CancellationToken.None);
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(500, recorded);
        Assert.Equal(1, opens);
    }

    /// <summary>批量路径必须和单条一样<b>重建全文索引</b>——漏了的话文件确实在库里，
    /// 但搜不到，而这只有用户去搜才发现。</summary>
    [Fact]
    public async Task EveryRowOfTheBatchIsSearchableByItsCjkTitle()
    {
        await _repo.RecordItemsAsync(
            new[] { Draft(@"C:\拖入\季度报告.txt"), Draft(@"C:\拖入\现场数据.txt") }, CancellationToken.None);

        var hit = await _repo.SearchAsync("报告", new SearchFilter(), CancellationToken.None);
        Assert.Contains(hit.Items, item => item.Title == "季度报告");
        var other = await _repo.SearchAsync("现场", new SearchFilter(), CancellationToken.None);
        Assert.Contains(other.Items, item => item.Title == "现场数据");
    }

    // ── 活动流：整批一次连接 ──

    [Fact]
    public async Task AWholeBatchOfActivitiesCostsOneConnection()
    {
        var events = Enumerable.Range(0, 30)
            .Select(i => new ActivityDraft(ActivityKind.ItemAdd, null, "入口 " + i, "file://C:/拖入/" + i))
            .ToList();

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        await _repo.LogActivitiesAsync(events, CancellationToken.None);
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(1, opens);                                   // 逐条记时是 30（每条还各数一次总数、各裁一次）
        var rows = await _repo.GetActivityAsync(30, CancellationToken.None);
        Assert.Equal(30, rows.Count);
        Assert.All(rows, row => Assert.Equal(ActivityKind.ItemAdd, row.Kind));
    }

    [Fact]
    public async Task AnEmptyActivityBatchOpensNoConnection()
    {
        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();

        await _repo.LogActivitiesAsync(Array.Empty<ActivityDraft>(), CancellationToken.None);
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(0, opens);
    }

    /// <summary>环形缓冲口径不许因为改批量而走形：仍然只留最近 500 条，<b>且留下的是新的那批</b>。</summary>
    [Fact]
    public async Task TheRingBufferStillKeepsOnlyTheNewestFiveHundred()
    {
        await _repo.LogActivitiesAsync(
            Enumerable.Range(0, 300).Select(i => new ActivityDraft(ActivityKind.ItemAdd, null, "早期 " + i, null)).ToList(),
            CancellationToken.None);
        await _repo.LogActivitiesAsync(
            Enumerable.Range(0, 300).Select(i => new ActivityDraft(ActivityKind.ItemAdd, null, "近期 " + i, null)).ToList(),
            CancellationToken.None);

        var rows = await _repo.GetActivityAsync(600, CancellationToken.None);

        Assert.Equal(500, rows.Count);
        Assert.Contains(rows, row => row.Title == "近期 299");
        Assert.DoesNotContain(rows, row => row.Title == "早期 0");
    }

    /// <summary>快捷入口不是 items 行，<c>item_key</c> 就该是 NULL——批量写时若把 null 写成空串，
    /// 活动格上会多出一个"能点但什么都打不开"的空关联。</summary>
    [Fact]
    public async Task ANullItemKeyStaysNull()
    {
        await _repo.LogActivitiesAsync(
            new[] { new ActivityDraft(ActivityKind.ItemAdd, null, "只有链接", "file://C:/拖入/a.txt") },
            CancellationToken.None);

        var row = Assert.Single(await _repo.GetActivityAsync(10, CancellationToken.None));
        Assert.Null(row.ItemKey);
        Assert.Equal("file://C:/拖入/a.txt", row.Uri);
    }
}
