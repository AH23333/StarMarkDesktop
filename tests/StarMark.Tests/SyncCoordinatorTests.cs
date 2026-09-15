#nullable enable
using System;
using System.IO;
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

        public bool IsAvailable => Available;
        public Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
        {
            if (Throw != null) throw Throw;
            return Task.FromResult(Result ?? Array.Empty<Item>());
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
        Assert.Contains(summary.Sources, s => s.SourceId == "bad" && !s.Success && s.Error == "boom");
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
}