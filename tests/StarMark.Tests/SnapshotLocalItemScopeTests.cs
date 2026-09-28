#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 快照"谁需要各开一次库"的那一次问话（批次 PA-9）。
/// <para>
/// 捕获/还原本地条目本来是<b>每个实例一次连接</b>：十几个实例里通常只有两三台真有内容，
/// 其余每次都是"开库、跑三遍 PRAGMA、确认这里是空的"。改成一连接问出名单，只对名单上的实例跑。
/// </para>
/// <para>这里最要紧的一条是<b>省下来的必须真是空集</b>：跳过某台实例，结果与逐台去查完全相同。
/// 否则省下的连接数会换来一张静默缺数据的快照。</para>
/// </summary>
public sealed class SnapshotLocalItemScopeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;
    private readonly WidgetSnapshotService _service;

    public SnapshotLocalItemScopeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_scope_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
        _service = new WidgetSnapshotService(_repo);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private static string NewInstanceId() => Guid.NewGuid().ToString("N");

    private async Task<Item> AddTodoAsync(string instanceId, string title)
    {
        var item = new Item
        {
            Type = ItemType.Todo,
            Source = ItemSources.Local,
            SourceId = LocalItemState.EncodeSourceId(instanceId, WidgetStorage.NewId()),
            Title = title,
        };
        await _repo.UpsertLocalItemAsync(item, CancellationToken.None);
        return item;
    }

    [Fact]
    public async Task OnlyInstancesThatActuallyHaveLocalItemsAreListed()
    {
        var a = NewInstanceId();
        var b = NewInstanceId();
        var empty = NewInstanceId();
        await AddTodoAsync(a, "甲的待办");
        await AddTodoAsync(b, "乙的待办");
        await AddTodoAsync(b, "乙的另一条");

        var owners = await _repo.GetInstancesWithLocalItemsAsync(CancellationToken.None);

        Assert.Equal(new[] { a, b }.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                     owners.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(empty, owners);
        Assert.Equal(2, owners.Count);                        // 同实例多条只算一个实例
    }

    /// <summary>两种"看着像但不该算"的行：别的来源（文件系统条目也可能带 <c>|</c>），
    /// 以及本地但键不是组件编码的行。被误算就会让快照为根本不存在的实例逐台开库。</summary>
    [Fact]
    public async Task ForeignSourcesAreNotInstances()
    {
        var mine = NewInstanceId();
        await AddTodoAsync(mine, "真的待办");
        await _repo.RecordItemAsync(new Item
        {
            Type = ItemType.File,
            Source = ItemSources.FileSystem,
            SourceId = "C:\\docs|a.txt",                                // 带分隔符也不该被算进来
            Title = "别处的文件",
        }, CancellationToken.None);
        // 真·本地行，但 source_id 不是"实例|本地号"编码（历史遗留或别的写入方）：解不出实例就必须整条忽略，
        // 否则会凭空多出一个不存在的实例，快照还为它各开一次库。
        await _repo.UpsertLocalItemAsync(new Item
        {
            Type = ItemType.Note,
            Source = ItemSources.Local,
            SourceId = "没有分隔符的一条",
            Title = "不像组件编码",
        }, CancellationToken.None);

        var owners = await _repo.GetInstancesWithLocalItemsAsync(CancellationToken.None);

        Assert.Equal(new[] { mine }, owners);
    }

    [Fact]
    public async Task OneConnectionAnswersForEveryInstanceOnTheDesktop()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => NewInstanceId()).ToArray();
        await AddTodoAsync(ids[0], "有内容");                            // 只有两台真有
        await AddTodoAsync(ids[3], "也有内容");

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        var owners = await _service.GetInstancesWithLocalItemsAsync(CancellationToken.None);
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(1, opens);                                          // 旧形状要为这 6 台各开一次库
        Assert.Equal(2, owners.Count);
        Assert.Contains(ids[0], owners);
        Assert.Contains(ids[3], owners);
        Assert.DoesNotContain(ids[1], owners);
    }

    [Fact]
    public async Task AnEmptyLibraryStillAnswersInOneConnection()
    {
        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        var owners = await _service.GetInstancesWithLocalItemsAsync(CancellationToken.None);
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Empty(owners);
        Assert.Equal(1, opens);
    }

    /// <summary>
    /// 关键的一条：<b>跳过不等于漏捕获</b>。不在名单上的实例，逐台去查也必定是空列表，
    /// 于是快照里写的"空"与"没查"是同一个值；反过来在名单上的必须有内容（否则整条判据没被真正测到）。
    /// </summary>
    [Fact]
    public async Task SkippingANonOwnerLosesNothing()
    {
        var owner = NewInstanceId();
        var outsider = NewInstanceId();
        await AddTodoAsync(owner, "要被捕获的");

        var owners = await _service.GetInstancesWithLocalItemsAsync(CancellationToken.None);

        Assert.Contains(owner, owners);
        Assert.NotEmpty(await _service.CaptureLocalItemsAsync(owner, CancellationToken.None));

        Assert.DoesNotContain(outsider, owners);
        Assert.Empty(await _service.CaptureLocalItemsAsync(outsider, CancellationToken.None));
    }

    /// <summary>还原侧同一条判据的另一半：<b>有内容时不许跳</b>——否则用户应用快照后待办会凭空消失。</summary>
    [Fact]
    public async Task AnOwnerMustNeverBeSkippedOnRestore()
    {
        var owner = NewInstanceId();
        await AddTodoAsync(owner, "还原后要还在的");

        var owners = await _service.GetInstancesWithLocalItemsAsync(CancellationToken.None);
        Assert.Contains(owner, owners);

        await _repo.DeleteLocalItemAsync(
            (await _repo.GetLocalItemsForInstanceAsync(owner, CancellationToken.None)).First().Id,
            ItemType.Todo, null, CancellationToken.None);

        Assert.Empty(await _service.GetInstancesWithLocalItemsAsync(CancellationToken.None));
    }
}

/// <summary>快照两侧都要先问一次名单（PA-9）——这两处都在 UI 层，按仓库做法扫源码守。</summary>
public sealed class SnapshotScopeGateTests
{
    [Fact]
    public void BothSidesOfTheSnapshotAskTheScopeOnce()
    {
        var manager = SourceGate.ReadRepoPartials("src/StarMark.UI/Services/WidgetManager.cs");

        Assert.Equal(2, SourceGate.Count(manager, "GetInstancesWithLocalItemsAsync("));   // 捕获侧 + 还原侧
        var restore = SourceGate.Between(manager, "foreach (var (instanceId, items) in dataRestore)",
                                         "if (items.Count == 0 && !owners.Contains(instanceId))");
        Assert.Contains("foreach", restore);                                              // 反空转：确实是从循环里切出来的
        // 捕获侧：名单是在循环之前一次问好的，循环体内不许再问（那就退回逐台开库）
        var captureLoop = SourceGate.Between(manager, "for (var i = 0; i < snapshot.Entries.Count; i++)", "AppendSnapshot");
        Assert.Contains("owners.Contains(instanceIds[i])", captureLoop);
        Assert.Equal(0, SourceGate.Count(captureLoop, "GetInstancesWithLocalItemsAsync("));
    }
}
