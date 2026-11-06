#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 本地条目「删除→撤销」的数据层往返（P-7，片210 拍 (A)、片214 落地）。
/// <para>
/// 撤销忠实回挂依赖三颗<b>仓储前提</b>，缺任何一颗撤销都会悄悄变残废——而这颗缺陷在 UI 沙箱里看不见：
/// ① <c>DeleteLocalItemAsync</c> 的快照在 CASCADE 之前就把标签与置顶读齐（<c>ReadLocalItemAsync</c> 的
/// GROUP_CONCAT 子查询＋<c>pinned</c> 列）；② <c>UpsertLocalItemAsync</c> 重插后把真实行 id 盖回
/// <c>Item.Id</c>（<c>RETURNING id</c>）；③ <c>AddTagAsync</c>／<c>SetPinnedAsync</c> 按那颗 id
/// 能把挂接与用户状态挂回新行。VM 侧「谁在撤销路径里调了它们」由 <c>TodoWriterWiringGateTests</c>
/// 的源码闸钉住，这里只管这三颗前提。
/// </para>
/// </summary>
public sealed class LocalItemUndoRoundTripTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;

    public LocalItemUndoRoundTripTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_undo_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private async Task<Item> SeedAsync(string sourceId)
    {
        var item = new Item
        {
            Type = ItemType.Todo,
            Source = ItemSources.Local,
            SourceId = sourceId,
            Title = "带标签的待办",
            Notes = "备注",
        };
        await _repo.UpsertLocalItemAsync(item, CancellationToken.None);
        return item;                                   // 行 id 已由 RETURNING 盖回
    }

    [Fact]
    public async Task DeleteSnapshotCarriesTagsAndPinnedBeforeTheCascade()
    {
        var item = await SeedAsync("inst1|1");
        Assert.True(await _repo.AddTagAsync(item.Id, "工作", CancellationToken.None));
        Assert.True(await _repo.AddTagAsync(item.Id, "urgent", CancellationToken.None));
        Assert.True(await _repo.SetPinnedAsync(item.Id, true, CancellationToken.None));

        var snapshot = await _repo.DeleteLocalItemAsync(
            item.Id, ItemType.Todo, ActivityKind.ItemDelete, CancellationToken.None);

        // 快照必须在 CASCADE 走掉 item_tags 之前把标签与置顶读齐——这是撤销"有东西可回挂"的前提
        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot!.Tags.Count);
        Assert.Contains("工作", snapshot.Tags);
        Assert.Contains("urgent", snapshot.Tags);
        Assert.True(snapshot.Pinned);
        Assert.Null(await _repo.GetLocalItemAsync(item.Id, ItemType.Todo, CancellationToken.None));
    }

    /// <summary>账本 P-7 钉的那条往返：seed 带标签本地项→Delete→按快照重插→断言 item_tags 复原。</summary>
    [Fact]
    public async Task SnapshotReinsertRestoresTagsAndPinnedOnTheFreshRowId()
    {
        var item = await SeedAsync("inst1|2");
        Assert.True(await _repo.AddTagAsync(item.Id, "工作", CancellationToken.None));
        Assert.True(await _repo.SetPinnedAsync(item.Id, true, CancellationToken.None));
        var snapshot = await _repo.DeleteLocalItemAsync(
            item.Id, ItemType.Todo, ActivityKind.ItemDelete, CancellationToken.None);
        Assert.NotNull(snapshot);

        // 撤销第一步：快照重插。业务键 (source, source_id) 不变，真实行 id 由 RETURNING 盖回 snapshot.Id
        await _repo.UpsertLocalItemAsync(snapshot!, CancellationToken.None, ActivityKind.ItemAdd);
        var fresh = await _repo.GetLocalItemAsync(snapshot!.Id, ItemType.Todo, CancellationToken.None);
        Assert.NotNull(fresh);                          // 行回来了，且能按盖回的 id 找到

        // 重插的那条 INSERT 不含 pinned、也不写 item_tags（标签词只拼进 search_text）——
        // 这正是 P-7 的缺口：没有下面那两笔，标签与置顶就在"删除→撤销"这一步永久丢失
        Assert.False(fresh!.Pinned);
        Assert.Empty(fresh.Tags);

        // 撤销第二步：按那颗真实行 id 忠实回挂（VM 侧形状见 TodoWidgetViewModel.UndoDeleteAsync）
        foreach (var tag in snapshot.Tags)
            await _repo.AddTagAsync(snapshot.Id, tag, CancellationToken.None);
        Assert.True(await _repo.SetPinnedAsync(snapshot.Id, true, CancellationToken.None));

        var restored = await _repo.GetLocalItemAsync(snapshot.Id, ItemType.Todo, CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Contains("工作", restored!.Tags);
        Assert.True(restored.Pinned);
        Assert.Equal("带标签的待办", restored.Title);   // items 列随快照原样回来
    }
}
