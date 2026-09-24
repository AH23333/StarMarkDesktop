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
/// 待办/随记每次编辑的那一圈往返（批次 PA-8）。改的是同一个形状：
/// <b>"把整表读回来，再在内存里 FirstOrDefault 找那一行"</b>——用户有 300 条待办时，
/// 勾一次框就要先物化 300 行（每行还带一次标签 <c>GROUP_CONCAT</c>）。
/// 另一半是作用域：限定必须写在 SQL 里，否则一个来自别处的 id 就能被组件改走/删走一整行。
/// </summary>
public sealed class LocalItemLookupTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;

    public LocalItemLookupTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_lookup_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private async Task<Item> TodoAsync(string title)
    {
        var item = new Item
        {
            Type = ItemType.Todo,
            Source = ItemSources.Local,
            SourceId = LocalItemState.EncodeSourceId("inst1", WidgetStorage.NewId()),
            Title = title,
        };
        await _repo.UpsertLocalItemAsync(item, CancellationToken.None);
        return item;
    }

    private async Task<long> ForeignFileAsync(string title)
    {
        var item = new Item
        {
            Type = ItemType.File,
            Source = ItemSources.FileSystem,
            SourceId = "hash-" + title,
            Title = title,
            Uri = "file://C:/docs/" + title,
        };
        await _repo.RecordItemAsync(item, CancellationToken.None);
        return item.Id;
    }

    // ── 取一条 ──

    [Fact]
    public async Task ReturnsJustThatLocalTodo()
    {
        var wanted = await TodoAsync("买牛奶");
        await TodoAsync("另一条");

        var got = await _repo.GetLocalItemAsync(wanted.Id, ItemType.Todo, CancellationToken.None);

        Assert.NotNull(got);
        Assert.Equal("买牛奶", got!.Title);
        Assert.Equal(wanted.SourceId, got.SourceId);
    }

    /// <summary>标签必须跟着回来：<b>组件改完一条要按最终标签集重建索引</b>，读漏了就等于把标签从索引里抹掉。</summary>
    [Fact]
    public async Task TagsComeBackOnTheSingleRow()
    {
        var item = await TodoAsync("带标签的");
        await _repo.AddTagAsync(item.Id, "家杂", CancellationToken.None);

        var got = await _repo.GetLocalItemAsync(item.Id, ItemType.Todo, CancellationToken.None);

        Assert.NotNull(got);
        Assert.Contains("家杂", got!.Tags);
    }

    /// <summary>隐藏的那条也要按 id 取到：撤销与删除要能作用在用户刚操作过的那一行上，
    /// 隐藏状态本身不是"不许改"的信号。</summary>
    [Fact]
    public async Task AHiddenTodoIsStillFoundById()
    {
        var item = await TodoAsync("已隐藏的");
        await _repo.SetHiddenAsync(item.Id, true, CancellationToken.None);

        Assert.NotNull(await _repo.GetLocalItemAsync(item.Id, ItemType.Todo, CancellationToken.None));
    }

    /// <summary>反向对照：被取代的那个形状确实会把整表读回来。<b>没有这条，"1 次连接"可能只是计数器坏了。</b></summary>
    [Fact]
    public async Task OneRowIsEnoughEvenWithFiveHundredTodos()
    {
        var wanted = await TodoAsync("要点的那一条");
        for (var i = 0; i < 499; i++) await TodoAsync("陪跑 " + i);

        var oldShape = await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Todo, ct: CancellationToken.None);
        Assert.Equal(500, oldShape.Count);                   // 旧写法为了这一行物化了 500 行（每行还拼一次标签）

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        var got = await _repo.GetLocalItemAsync(wanted.Id, ItemType.Todo, CancellationToken.None);
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(1, opens);
        Assert.NotNull(got);
        Assert.Equal("要点的那一条", got!.Title);
    }

    /// <summary>作用域写在 SQL 里：同一个 id 属于文件系统条目时，这条路必须回 null。</summary>
    [Fact]
    public async Task RefusesARowThatIsNotALocalItem()
    {
        var foreign = await ForeignFileAsync("别处的文件");

        Assert.Null(await _repo.GetLocalItemAsync(foreign, ItemType.Todo, CancellationToken.None));
        Assert.Null(await _repo.GetLocalItemAsync(foreign, ItemType.File, CancellationToken.None));
    }

    /// <summary>类型也要对得上：把随记当待办取会拿到 null，而不是让组件把随记改写成待办。</summary>
    [Fact]
    public async Task RefusesTheWrongLocalType()
    {
        var note = new Item
        {
            Type = ItemType.Note,
            Source = ItemSources.Local,
            SourceId = LocalItemState.EncodeSourceId("inst1", WidgetStorage.NewId()),
            Title = "一条随记",
        };
        await _repo.UpsertLocalItemAsync(note, CancellationToken.None);

        Assert.Null(await _repo.GetLocalItemAsync(note.Id, ItemType.Todo, CancellationToken.None));
        Assert.NotNull(await _repo.GetLocalItemAsync(note.Id, ItemType.Note, CancellationToken.None));
    }

    /// <summary>虚拟行（Id=0，来自实时源、从不入库）不该惊动数据库。</summary>
    [Fact]
    public async Task IdZeroOpensNoConnectionAtAll()
    {
        await TodoAsync("陪跑");
        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();

        Assert.Null(await _repo.GetLocalItemAsync(0, ItemType.Todo, CancellationToken.None));
        Assert.Null(await _repo.DeleteLocalItemAsync(-7, ItemType.Todo, null, CancellationToken.None));
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(0, opens);
    }

    // ── 删一条（并把活动记在同一事务里）──

    [Fact]
    public async Task DeletesAndLogsInOneConnection()
    {
        var item = await TodoAsync("要删掉的");

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        var deleted = await _repo.DeleteLocalItemAsync(item.Id, ItemType.Todo, ActivityKind.ItemDelete, CancellationToken.None);
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(1, opens);                               // 旧形状：读全表 1 次 + 删 1 次 + 记活动 1 次
        Assert.NotNull(deleted);
        Assert.Equal("要删掉的", deleted!.Title);
        Assert.Equal(item.SourceId, deleted.SourceId);        // 撤销快照要的正是这一行的内容

        var rows = await _repo.GetActivityAsync(10, CancellationToken.None);
        var logged = Assert.Single(rows, r => r.Kind == ActivityKind.ItemDelete);
        Assert.Equal("要删掉的", logged.Title);
        Assert.Equal($"{ItemSources.Local}:{item.SourceId}", logged.ItemKey);
    }

    [Fact]
    public async Task NothingDeletedMeansNoActivityAndNoRowLost()
    {
        var kept = await TodoAsync("留下来的");

        Assert.Null(await _repo.DeleteLocalItemAsync(kept.Id + 999, ItemType.Todo, ActivityKind.ItemDelete, CancellationToken.None));

        Assert.NotNull(await _repo.GetLocalItemAsync(kept.Id, ItemType.Todo, CancellationToken.None));
        Assert.Empty(await _repo.GetActivityAsync(10, CancellationToken.None));   // 没删成就不该在时间线里留痕
    }

    /// <summary>删别人的行必须失败：作用域限定是安全属性，不只是"少读几行"。</summary>
    [Fact]
    public async Task CannotDeleteSomeoneElsesRow()
    {
        var foreign = await ForeignFileAsync("别处的文件");

        Assert.Null(await _repo.DeleteLocalItemAsync(foreign, ItemType.File, ActivityKind.ItemDelete, CancellationToken.None));
        Assert.NotNull(await _repo.GetByIdAsync(foreign, CancellationToken.None));
        Assert.Empty(await _repo.GetActivityAsync(10, CancellationToken.None));
    }

    /// <summary>不带活动 kinds 时删除照旧发生，但时间线保持安静（组件里"改颜色"这类动作不该刷屏）。</summary>
    [Fact]
    public async Task DeletingWithoutAnActivityKindStillDeletesButStaysQuiet()
    {
        var item = await TodoAsync("静默删除");

        Assert.Null(await _repo.DeleteLocalItemAsync(item.Id, ItemType.Note, null, CancellationToken.None));
        Assert.NotNull(await _repo.DeleteLocalItemAsync(item.Id, ItemType.Todo, null, CancellationToken.None));

        Assert.Null(await _repo.GetByIdAsync(item.Id, CancellationToken.None));
        Assert.Empty(await _repo.GetActivityAsync(10, CancellationToken.None));
    }

    // ── 写一条 + 记一笔活动（同一次事务）──

    [Fact]
    public async Task AddAndLogShareOneConnection()
    {
        var item = new Item
        {
            Type = ItemType.Note,
            Source = ItemSources.Local,
            SourceId = LocalItemState.EncodeSourceId("inst1", WidgetStorage.NewId()),
            Title = "新随记",
            Uri = "file://C:/x",
        };

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        await _repo.UpsertLocalItemAsync(item, CancellationToken.None, ActivityKind.ItemAdd);
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(1, opens);
        var logged = Assert.Single(await _repo.GetActivityAsync(10, CancellationToken.None));
        Assert.Equal(ActivityKind.ItemAdd, logged.Kind);
        // 活动的主体字段来自被写的这一行：调用方没有第二条路可以把标题或 uri 说成别的。
        Assert.Equal("新随记", logged.Title);
        Assert.Equal($"{ItemSources.Local}:{item.SourceId}", logged.ItemKey);
        Assert.Equal("file://C:/x", logged.Uri);
    }

    /// <summary>待办没有 uri，那一笔活动的 uri 槽就该是 NULL——写成空串会让活动格上出现
    /// 一个"看着能点、点开什么都没有"的行。</summary>
    [Fact]
    public async Task AnEmptyUriIsLoggedAsNullNotAsAnEmptyString()
    {
        var item = new Item
        {
            Type = ItemType.Todo,
            Source = ItemSources.Local,
            SourceId = LocalItemState.EncodeSourceId("inst1", WidgetStorage.NewId()),
            Title = "没有链接的一条",
        };

        await _repo.UpsertLocalItemAsync(item, CancellationToken.None, ActivityKind.ItemAdd);

        var logged = Assert.Single(await _repo.GetActivityAsync(10, CancellationToken.None));
        Assert.Null(logged.Uri);
    }

    /// <summary>不带活动时老调用点行为不变（也不该白开一个事务、白记一笔活动）。</summary>
    [Fact]
    public async Task APlainWriteLeavesTheTimelineAlone()
    {
        var item = new Item
        {
            Type = ItemType.Todo,
            Source = ItemSources.Local,
            SourceId = LocalItemState.EncodeSourceId("inst1", WidgetStorage.NewId()),
            Title = "普通写入",
        };

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        await _repo.UpsertLocalItemAsync(item, CancellationToken.None);
        var opens = counter.Connections;
        _factory.Counter = null;

        Assert.Equal(1, opens);
        Assert.Empty(await _repo.GetActivityAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task TheOldPerEditShapeCostedThreeConnectionsWhereTheNewOnesCostsTwo()
    {
        var item = await TodoAsync("对比用的");

        // 旧：读全表找一行 + 删 + 记活动 = 3 次开库；新：删（含读与活动）1 次 + 组件重读列表 1 次
        var old = new DbActivityCounter();
        _factory.Counter = old.Reset();
        var found = (await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Todo, ct: CancellationToken.None))
            .First(i => i.Id == item.Id);
        await _repo.DeleteBySourceIdAsync(ItemSources.Local, found.SourceId, CancellationToken.None);
        await _repo.LogActivityAsync(ActivityKind.ItemDelete, $"{ItemSources.Local}:{found.SourceId}", found.Title,
            null, CancellationToken.None);
        var oldOpens = old.Connections;
        _factory.Counter = null;

        var again = await TodoAsync("对比用的2");
        var neu = new DbActivityCounter();
        _factory.Counter = neu.Reset();
        await _repo.DeleteLocalItemAsync(again.Id, ItemType.Todo, ActivityKind.ItemDelete, CancellationToken.None);
        var newOpens = neu.Connections;
        _factory.Counter = null;

        Assert.Equal(3, oldOpens);
        Assert.Equal(1, newOpens);
    }
}

/// <summary>
/// 组件里"每次编辑"的形状守门（批次 PA-8）。<b>逐条往返一旦回来，功能一点都不坏</b>：
/// 勾一次框照样成功，只是先把 300 行读回来再用其中一行——只有量具能看见，所以写成闸门。
/// 扫不到锚点就抛，免得一条永不执行的检查冒充绿灯。
/// </summary>
public sealed class LocalItemEditShapeGateTests
{
    private static readonly string[] Vms =
    [
        "src/StarMark.UI/ViewModels/TodoWidgetViewModel.cs",
        "src/StarMark.UI/ViewModels/QuickNoteWidgetViewModel.cs",
    ];

    [Fact]
    public void NoVmReadsTheWholeListToFindOneRow()
    {
        foreach (var vm in Vms)
        {
            var source = SourceGate.ReadRepoFile(vm);
            Assert.Equal(0, SourceGate.Count(source, "i => i.Id =="));          // 内存里筛那一行的写法
            Assert.Contains("DeleteLocalItemAsync(", source);                   // 删除走"限定作用域 + 同事务记活动"
            Assert.Equal(0, SourceGate.Count(source, "DeleteBySourceIdAsync("));
            Assert.Equal(0, SourceGate.Count(source, "LogActivityAsync("));
        }
    }

    /// <summary>待办的"改一条"必须先按 id 取那一行（而不是取全表）。</summary>
    [Fact]
    public void TheTodoVmLooksUpOneRowById()
        => Assert.Contains("GetLocalItemAsync(", SourceGate.ReadRepoFile(Vms[0]));

    [Fact]
    public void ActivityWritingIsLeftToTheWritePrimitives()
    {
        foreach (var vm in Vms)
        {
            var source = SourceGate.ReadRepoFile(vm);
            Assert.Equal(0, SourceGate.Count(source, "_repo.LogActivityAsync("));  // 一次编辑再开一次库
            Assert.Contains("ActivityKind.ItemAdd", source);
            Assert.Contains("ActivityKind.ItemDelete", source);
        }
    }
}
