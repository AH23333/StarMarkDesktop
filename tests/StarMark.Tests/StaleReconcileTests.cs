#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Data;

namespace StarMark.Tests;

/// <summary>
/// 失联对账三件套（片215·P-5B）的真库行为：<see cref="ItemRepository.ReconcileStaleAsync"/>、
/// <see cref="ItemRepository.CountStaleAsync"/>、<see cref="ItemRepository.DeleteStaleAsync"/>。
/// <para>
/// 拍板口径来自 P-5 账本 (B)「标灰＋一键清」：对账只置位清位、行留着（标签/笔记还在它身上），
/// 真删只发生在用户按下那一键之后，且只删 <c>stale=1 AND type='bookmark'</c> 那一族。
/// </para>
/// </summary>
public sealed class StaleReconcileTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;

    public StaleReconcileTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_stale_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private static Item Row(string source, string sourceId, ItemType type = ItemType.Bookmark)
        => new() { Type = type, Source = source, SourceId = sourceId, Title = sourceId };

    [Fact]
    public async Task Reconcile_MarksMissingRows_AndClearsThemWhenTheyReturn()
    {
        // 浏览器里删掉了 b、c：这一轮源侧全量清单里只剩 a
        await _repo.UpsertAsync(new[] { Row("chrome", "a"), Row("chrome", "b"), Row("chrome", "c") },
            CancellationToken.None);

        var newlyStale = await _repo.ReconcileStaleAsync("chrome", ItemType.Bookmark,
            new[] { "a" }, CancellationToken.None);

        Assert.Equal(2, newlyStale);                        // 只报这次新标的
        Assert.Equal(2, await _repo.CountStaleAsync(CancellationToken.None));

        // 标位要一路读到模型上：GetAllAsync 的 SELECT 带 i.stale、MapItem 的 18 号位解包，全在这几句里过
        var all = await _repo.GetAllAsync(new BrowseFilter { Limit = 100 }, CancellationToken.None);
        Assert.False(all.Single(i => i.SourceId == "a").Stale, "清单里还在的被误标了");
        Assert.True(all.Single(i => i.SourceId == "b").Stale, "源里已删的没被标灰");
        Assert.True(all.Single(i => i.SourceId == "c").Stale, "源里已删的没被标灰");

        // b、c 又被加回浏览器：下一轮清单全数回归 ⇒ 清位。回归是好消息但不计入返回值。
        var cleared = await _repo.ReconcileStaleAsync("chrome", ItemType.Bookmark,
            new[] { "a", "b", "c" }, CancellationToken.None);

        Assert.Equal(0, cleared);
        Assert.Equal(0, await _repo.CountStaleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Reconcile_EmptyPresentList_MarksEverythingStale()
    {
        // 源侧一个书签都不剩（用户清空了浏览器书签栏的这批）＝该源这一族全数失联
        await _repo.UpsertAsync(new[] { Row("chrome", "a"), Row("chrome", "b") }, CancellationToken.None);

        var newly = await _repo.ReconcileStaleAsync("chrome", ItemType.Bookmark,
            Array.Empty<string>(), CancellationToken.None);

        Assert.Equal(2, newly);
        Assert.Equal(2, await _repo.CountStaleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Reconcile_ScopeIsSourceAndType_SiblingsStayUntouched()
    {
        // 同源不同类、同类不同源：对账的作用域钉在 (source, type) 上，陪绑者一根毫毛不碰
        await _repo.UpsertAsync(new[]
        {
            Row("chrome", "bm"),
            Row("chrome", "star", ItemType.GitHubStar),
            Row("edge", "bm"),
        }, CancellationToken.None);

        var newly = await _repo.ReconcileStaleAsync("chrome", ItemType.Bookmark,
            Array.Empty<string>(), CancellationToken.None);

        Assert.Equal(1, newly);
        var all = await _repo.GetAllAsync(new BrowseFilter { Limit = 100 }, CancellationToken.None);
        Assert.True(all.Single(i => i.Source == "chrome" && i.Type == ItemType.Bookmark).Stale);
        Assert.False(all.Single(i => i.Source == "chrome" && i.Type == ItemType.GitHubStar).Stale,
            "同源不同类被陪绑了");
        Assert.False(all.Single(i => i.Source == "edge").Stale, "同类不同源被陪绑了");

        // 空源名是调用错误：按 0 处理，不许炸也不许动表
        Assert.Equal(0, await _repo.ReconcileStaleAsync("", ItemType.Bookmark,
            Array.Empty<string>(), CancellationToken.None));
    }

    [Fact]
    public async Task Reconcile_NoChange_DoesNotBroadcast()
    {
        // 幂等重放无痕：清单与库完全一致的那一轮，不写库、不发 DataChangeHub 通知
        // （每一轮同步都对账，若每次都广播，主界面/组件会以同步节拍空转重载）。
        await _repo.UpsertAsync(new[] { Row("chrome", "a") }, CancellationToken.None);
        var fired = 0;
        Action<DataChangeKind> handler = _ => fired++;
        using var sub = DataChangeHub.Subscribe(handler);

        var changed = await _repo.ReconcileStaleAsync("chrome", ItemType.Bookmark,
            new[] { "a" }, CancellationToken.None);

        Assert.Equal(0, changed);
        GC.KeepAlive(handler);                              // 弱引用订阅：处理器得活着才有得数
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task Reconcile_ChangeBroadcastsOncePerChangedRound()
    {
        await _repo.UpsertAsync(new[] { Row("chrome", "a"), Row("chrome", "b") }, CancellationToken.None);
        var fired = 0;
        Action<DataChangeKind> handler = _ => fired++;
        using var sub = DataChangeHub.Subscribe(handler);

        await _repo.ReconcileStaleAsync("chrome", ItemType.Bookmark, new[] { "a" }, CancellationToken.None);
        Assert.Equal(1, fired);                             // 标灰这一轮：一声

        await _repo.ReconcileStaleAsync("chrome", ItemType.Bookmark, new[] { "a" }, CancellationToken.None);
        Assert.Equal(1, fired);                             // 幂等重放：还是一声

        await _repo.ReconcileStaleAsync("chrome", ItemType.Bookmark, new[] { "a", "b" }, CancellationToken.None);
        Assert.Equal(2, fired);                             // 清位也是变化：再一声
        GC.KeepAlive(handler);
    }

    [Fact]
    public async Task DeleteStale_RemovesOnlyStaleBookmarkRows()
    {
        // gone 失联、keep 还在源里、edge 那颗根本没参与这次对账——一键清只许删走 gone
        await _repo.UpsertAsync(new[] { Row("chrome", "gone"), Row("chrome", "keep"), Row("edge", "bm") },
            CancellationToken.None);
        await _repo.ReconcileStaleAsync("chrome", ItemType.Bookmark, new[] { "keep" }, CancellationToken.None);

        var removed = await _repo.DeleteStaleAsync(CancellationToken.None);

        Assert.Equal(1, removed);
        var all = await _repo.GetAllAsync(new BrowseFilter { Limit = 100 }, CancellationToken.None);
        Assert.Equal(2, all.Count);
        Assert.DoesNotContain(all, i => i.SourceId == "gone");
        Assert.Contains(all, i => i.SourceId == "keep");
        Assert.Contains(all, i => i.SourceId == "bm");
        Assert.Equal(0, await _repo.CountStaleAsync(CancellationToken.None));
    }
}
