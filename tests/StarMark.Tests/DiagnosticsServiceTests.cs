#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Core.Diagnostics;
using StarMark.Data;

namespace StarMark.Tests;

/// <summary>P2-8 诊断信息服务：只读聚合的正确性。</summary>
public sealed class DiagnosticsServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;

    public DiagnosticsServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_diag_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    [Fact]
    public async Task CollectAsync_ReportsDbCountsSourcesAndSyncState()
    {
        await _repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.GitHubStar, Source = "github", SourceId = "repo-1",
                       Title = "tool", Subtitle = "", Uri = "https://github.com/a/tool", SearchText = "tool" },
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "b-1",
                       Title = "note", Subtitle = "", Uri = "https://example.com", SearchText = "note" },
        }, CancellationToken.None);
        await _repo.SetSyncStateAsync("github:last_synced_at", "1700000000", CancellationToken.None);
        await _repo.SetSyncStateAsync("github:etag", "W/\"abc123\"", CancellationToken.None);

        var svc = new DiagnosticsService(_factory, _repo, Array.Empty<IItemSource>());
        var entries = await svc.CollectAsync(CancellationToken.None);

        var dict = entries.ToDictionary(e => e.Label, e => e.Value);

        // 数据库事实
        Assert.Equal(_dbPath, dict["数据库路径"]);
        Assert.Contains("B", dict["数据库体积"]);
        Assert.Equal("4", dict["Schema 版本"]);

        // 条目计数
        Assert.Contains("Stars 1", dict["条目总数"]);
        Assert.Contains("书签 1", dict["条目总数"]);

        // 索引
        Assert.Equal("2", dict["FTS 索引行数"]);

        // 同步检查点（P1-4 落库的 sync_state 被读取展示；按本机时区动态计算期望值）
        var expected = DateTimeOffset.FromUnixTimeSeconds(1700000000)
            .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        Assert.Equal(expected, dict["上次 GitHub 同步"]);
        Assert.Equal("W/\"abc123\"", dict["GitHub ETag"]);

        // 无源时不出「源：」行
        Assert.DoesNotContain(entries, e => e.Label.StartsWith("源："));
    }

    [Fact]
    public async Task CollectAsync_WithoutSyncState_ShowsNever()
    {
        var svc = new DiagnosticsService(_factory, _repo, Array.Empty<IItemSource>());
        var entries = await svc.CollectAsync(CancellationToken.None);

        Assert.Contains(entries, e => e.Label == "上次 GitHub 同步" && e.Value == "从未");
        Assert.Contains(entries, e => e.Label == "GitHub ETag" && e.Value.Contains("下次全量拉取"));
    }

    [Fact]
    public async Task SchemaVersion_ReflectsActualDbValue_NotCodeConst()
    {
        // R8#6：诊断必须显示库内**实际** schema_version，而非硬编码的 MigrationRunner.CurrentVersion。
        // 迁移半途中断/失败时二者背离，常量会永远显示"最新"而掩盖问题。
        // 手动把库内值改成 2（低于代码目标 4），模拟"迁移未成功"。
        using (var conn = _factory.Open())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE sync_state SET value='2' WHERE key='schema_version';";
            cmd.ExecuteNonQuery();
        }

        var svc = new DiagnosticsService(_factory, _repo, Array.Empty<IItemSource>());
        var entries = await svc.CollectAsync(CancellationToken.None);
        var dict = entries.ToDictionary(e => e.Label, e => e.Value);

        var display = dict["Schema 版本"];
        Assert.StartsWith("2", display);                 // 读到的是库内真实值 2
        Assert.DoesNotContain("4", display[..1]);        // 不是代码常量 4
        Assert.Contains("目标 4", display);              // 并明确警示与目标不一致
    }
}
