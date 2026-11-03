#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Data;
using StarMark.Core.Sync;

namespace StarMark.Tests;

public sealed class SyncCoordinatorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;

    public SyncCoordinatorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_sync_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private sealed class FakeSource : IItemSource
    {
        public required string SourceId { get; init; }
        public required string DisplayName { get; init; }
        public bool Available { get; init; } = true;
        public IReadOnlyList<Item>? Result { get; set; }
        public Exception? Throw { get; init; }

        /// <summary>提交检查点时问一句"载荷那时在库里了吗"（P-19 钉的就是这个次序）。</summary>
        public Func<Task<bool>>? PayloadIsInDb { get; set; }
        public int Commits { get; private set; }
        public bool? PayloadWasInDbAtCommit { get; private set; }

        public bool IsAvailable => Available;
        public Task<FetchResult> FetchAsync(SyncContext ctx, CancellationToken ct)
        {
            if (Throw != null) throw Throw;
            return Task.FromResult(new FetchResult(Result ?? Array.Empty<Item>(), false));
        }

        public async Task CommitCheckpointAsync(CancellationToken ct)
        {
            Commits++;
            if (PayloadIsInDb is not null) PayloadWasInDbAtCommit = await PayloadIsInDb();
        }

        public Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Item>>(Result ?? Array.Empty<Item>());
    }

    [Fact]
    public async Task SyncAll_PullsAndPersists()
    {
        var repo = new ItemRepository(_factory);
        var source = new FakeSource
        {
            SourceId = "fake",
            DisplayName = "Fake",
            Result = new[] { new Item { Type = ItemType.Bookmark, Source = "fake", SourceId = "a", Title = "A" } },
        };
        var coordinator = new SyncCoordinator(repo, new[] { source });

        var summary = await coordinator.SyncAllAsync(CancellationToken.None);

        Assert.Equal(1, summary.TotalPulled);
        Assert.Equal(0, summary.FailedCount);

        var all = await repo.GetAllAsync(new BrowseFilter { Limit = 100 }, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal("A", all[0].Title);
    }

    [Fact]
    public async Task SyncAll_UnavailableSource_IsSkipped()
    {
        var repo = new ItemRepository(_factory);
        var source = new FakeSource { SourceId = "off", DisplayName = "Off", Available = false };
        var coordinator = new SyncCoordinator(repo, new[] { source });

        var summary = await coordinator.SyncAllAsync(CancellationToken.None);

        Assert.Equal(0, summary.TotalPulled);
        Assert.Equal(0, summary.FailedCount);
    }

    [Fact]
    public async Task SyncAll_FailingSource_IsRecorded_OthersProceed()
    {
        var repo = new ItemRepository(_factory);
        var bad = new FakeSource { SourceId = "bad", DisplayName = "Bad", Throw = new InvalidOperationException("boom") };
        var good = new FakeSource
        {
            SourceId = "good",
            DisplayName = "Good",
            Result = new[] { new Item { Type = ItemType.File, Source = "good", SourceId = "a", Title = "OK" } },
        };
        var coordinator = new SyncCoordinator(repo, new[] { bad, good });

        var summary = await coordinator.SyncAllAsync(CancellationToken.None);

        Assert.Equal(1, summary.TotalPulled);
        Assert.Equal(1, summary.FailedCount);
        // 批次 WI-81／P-212 走 B：`Error` 从成品中文串换成了「键＋实参」。异常原文不再当"那句话"交出，
        // 而是当**读数**进 sync.src.other 的 {0} 槽——句子本身归表管，换语言那一刻才取词。
        Assert.Contains(summary.Sources, s => s.SourceId == "bad" && !s.Success
            && s.Error is { Key: "sync.src.other" } && (string?)s.Error.Args?[0] == "boom");
        Assert.Contains(summary.Sources, s => s.SourceId == "good" && s.Success);
    }

    [Fact]
    public async Task SyncAll_MultipleUpserts_UpdatesInPlace()
    {
        var repo = new ItemRepository(_factory);
        var source = new FakeSource
        {
            SourceId = "fake",
            DisplayName = "Fake",
            Result = new[] { new Item { Type = ItemType.Bookmark, Source = "fake", SourceId = "a", Title = "V1" } },
        };
        var coordinator = new SyncCoordinator(repo, new[] { source });
        await coordinator.SyncAllAsync(CancellationToken.None);

        source.Result = new[] { new Item { Type = ItemType.Bookmark, Source = "fake", SourceId = "a", Title = "V2" } };
        var summary = await coordinator.SyncAllAsync(CancellationToken.None);

        var all = await repo.GetAllAsync(new BrowseFilter { Limit = 100 }, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal("V2", all[0].Title);
    }

    [Fact]
    public async Task SyncAll_SourceLocalTimeout_DoesNotAbortBatch()
    {
        // 单源内部超时（HttpClient 的 TaskCanceledException 是 OCE 子类），而顶层 ct 未取消：
        // 旧实现无差别上抛 → 整批中断、good 源永不执行。修复后应记为 bad 失败、good 照常拉取。
        var repo = new ItemRepository(_factory);
        var bad = new FakeSource { SourceId = "slow", DisplayName = "Slow", Throw = new TaskCanceledException() };
        var good = new FakeSource
        {
            SourceId = "good",
            DisplayName = "Good",
            Result = new[] { new Item { Type = ItemType.File, Source = "good", SourceId = "a", Title = "OK" } },
        };
        var coordinator = new SyncCoordinator(repo, new[] { bad, good });

        var summary = await coordinator.SyncAllAsync(CancellationToken.None);

        Assert.Equal(1, summary.TotalPulled);
        Assert.Equal(1, summary.FailedCount);
        Assert.Contains(summary.Sources, s => s.SourceId == "slow" && !s.Success);
        Assert.Contains(summary.Sources, s => s.SourceId == "good" && s.Success);
    }

    [Fact]
    public async Task SyncAll_GenuineCancellation_Rethrows()
    {
        // 顶层确已取消时的 OCE 仍须上抛中断整次同步（不能被当作单源失败吞掉）。
        var repo = new ItemRepository(_factory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var src = new FakeSource
        {
            SourceId = "x",
            DisplayName = "X",
            Throw = new OperationCanceledException(cts.Token),
        };
        var coordinator = new SyncCoordinator(repo, new[] { src });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.SyncAllAsync(cts.Token));
    }

    // ===== 检查点提交的时机（P-19）：载荷先进库，检查点后提交 =====

    private static FakeSource SourceSayingWhatIsInDb(ItemRepository repo, string source, string title) => new()
    {
        SourceId = source,
        DisplayName = source,
        Result = new[] { new Item { Type = ItemType.Bookmark, Source = source, SourceId = "k1", Title = title } },
        PayloadIsInDb = async () =>
        {
            var all = await repo.GetAllAsync(new BrowseFilter { Limit = 100 }, CancellationToken.None);
            return all.Any(i => i.Source == source && i.SourceId == "k1" && i.Title == title);
        },
    };

    [Fact]
    public async Task CheckpointIsCommittedOnlyAfterThePayloadIsInDb()
    {
        // 这一条钉的是本批的全部意义：源自己写检查点的旧顺序，让"崩在两步之间"变成永久少一批
        // （下一轮首页带 If-None-Match 命中 304 ⇒ 返回零条 ⇒ 那批新 Star 再也不会被拉回来）。
        var repo = new ItemRepository(_factory);
        var source = SourceSayingWhatIsInDb(repo, "p19", "落库了才算数");
        var coordinator = new SyncCoordinator(repo, new[] { source });

        var summary = await coordinator.SyncAllAsync(CancellationToken.None);

        Assert.Equal(0, summary.FailedCount);
        Assert.Equal(1, source.Commits);
        Assert.True(source.PayloadWasInDbAtCommit, "提交检查点时载荷还没进库＝崩溃窗口还在");
    }

    [Fact]
    public async Task FailingSource_NeverCommitsItsCheckpoint()
    {
        var repo = new ItemRepository(_factory);
        var bad = new FakeSource { SourceId = "bad", DisplayName = "Bad", Throw = new InvalidOperationException("boom") };
        var coordinator = new SyncCoordinator(repo, new[] { bad });

        var summary = await coordinator.SyncAllAsync(CancellationToken.None);

        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(0, bad.Commits);       // 抛出来的那一轮不许留下任何检查点（留下的代价是下一轮 304）
    }

    [Fact]
    public async Task ASourceThatReturnedNothing_StillCommits()
    {
        // 304 / 空列表那一轮：载荷本来就没什么可写，但"这一轮确实同步完了"要留下记录，
        // 否则诊断页的"上次同步时间"会永远停在旧值（而 ETag 未变 ⇒ 提交它无害）。
        var repo = new ItemRepository(_factory);
        var empty = new FakeSource { SourceId = "empty", DisplayName = "Empty", Result = Array.Empty<Item>() };
        var coordinator = new SyncCoordinator(repo, new[] { empty });

        await coordinator.SyncAllAsync(CancellationToken.None);

        Assert.Equal(1, empty.Commits);
    }

    // ─────────────── 批次 WI-82 片2：那颗「上一次同步」的读数只有一个主人（P-212 走 B 的后半） ───────────────

    [Fact]
    public void LastSummary_IsNullUntilTheFirstRunFinishes()
    {
        // 设置页那一格靠这颗 null 决定"整行收着"：把它当成"同步过了、什么都没拉"就是编话。
        var coordinator = new SyncCoordinator(new ItemRepository(_factory), Array.Empty<IItemSource>());
        Assert.Null(coordinator.LastSummary);
    }

    [Fact]
    public async Task LastSummary_IsTheSameObjectTheCallerWasHanded()
    {
        var repo = new ItemRepository(_factory);
        var good = new FakeSource { SourceId = "good", DisplayName = "Good", Result = new[] { ItemFile("good") } };
        var coordinator = new SyncCoordinator(repo, new[] { good });

        var summary = await coordinator.SyncAllAsync(CancellationToken.None);

        // ⛔ "内容相等就算"：界面上读的那一颗与调用方拿到的那一颗必须同一颗，
        // 否则"提示卡说的"与"设置页说的"可以来自两份不同时刻的汇总（＝分岔预定）。
        Assert.Same(summary, coordinator.LastSummary);
    }

    [Fact]
    public async Task SyncCompleted_FiresOncePerRunWithThatSameObject()
    {
        var repo = new ItemRepository(_factory);
        var good = new FakeSource { SourceId = "good", DisplayName = "Good", Result = new[] { ItemFile("good") } };
        var coordinator = new SyncCoordinator(repo, new[] { good });
        var seen = new System.Collections.Generic.List<SyncSummary>();
        coordinator.SyncCompleted += seen.Add;

        var first = await coordinator.SyncAllAsync(CancellationToken.None);
        var second = await coordinator.SyncAllAsync(CancellationToken.None);

        Assert.Equal(2, seen.Count);
        Assert.Same(first, seen[0]);
        Assert.Same(second, seen[1]);
        Assert.Same(second, coordinator.LastSummary);   // 后一发覆盖前一发（屏幕上要的永远是"最后一次"）
    }

    [Fact]
    public async Task CancelledRun_LeavesThePreviousReadingAlone()
    {
        var repo = new ItemRepository(_factory);
        var ok = new FakeSource { SourceId = "ok", DisplayName = "OK", Result = new[] { ItemFile("ok") } };
        var canceller = new FakeSource { SourceId = "cancel", DisplayName = "Cancel", Throw = new OperationCanceledException() };
        var coordinator = new SyncCoordinator(repo, new[] { ok });
        var fired = 0;
        coordinator.SyncCompleted += _ => fired++;

        var first = await coordinator.SyncAllAsync(CancellationToken.None);
        // 取消这一发要走到"上抛"那一条 catch 才算真取消：那颗 catch 带 `when (!ct.IsCancellationRequested)` 的兄弟
        // 管的是"单源自己超时"（记成一源失败、其余继续）。所以这里交一颗<b>已经取消</b>的令牌——
        // 拿一颗没取消的令牌丢进去会被当成超时，那是我读错 catch 的顺序、⛔ 读那颗过滤器（坑表同族）。
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await new SyncCoordinator(repo, new[] { canceller }).SyncAllAsync(cts.Token));

        // 被取消的那一发⛔ 是一次"结果"：它没有失败名单可报，记下来会让设置页念一句假的"上一次同步没成"。
        Assert.Same(first, coordinator.LastSummary);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task LastSummary_StillCarriesThePerSourceReasons()
    {
        var repo = new ItemRepository(_factory);
        var bad = new FakeSource { SourceId = "bad", DisplayName = "Bad", Throw = new InvalidOperationException("boom") };
        var bad2 = new FakeSource { SourceId = "bad2", DisplayName = "Bad2", Throw = new InvalidOperationException("bang") };
        var coordinator = new SyncCoordinator(repo, new[] { bad, bad2 });

        await coordinator.SyncAllAsync(CancellationToken.None);

        // 这一格钉的是 P-212 那颗缺陷的本体：界面上那句"哪一源、为什么"必须还能从<b>这颗被留下的汇总</b>里取出来，
        // ⛔ 留下的只是"失败几颗"那颗数（那是 WI-80 判出来的形状）。
        var lines = coordinator.LastSummary!.FailureLines();
        Assert.NotNull(lines);
        Assert.Equal(2, lines!.Render().Split('\n').Length);
        Assert.Contains("boom", lines.Render(), StringComparison.Ordinal);
    }

    private static Item ItemFile(string source)
        => new() { Type = ItemType.File, Source = source, SourceId = "a", Title = "T" };
}