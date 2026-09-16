#nullable enable
using System.IO;
using StarMark.Abstractions;
using StarMark.Data;

namespace StarMark.Core.Diagnostics;

/// <summary>诊断面板中的一行（标签 + 值）。对应扩展设置页的「诊断」区块（对比方案 P2-8）。</summary>
public sealed record DiagnosticEntry(string Label, string Value);

/// <summary>
/// 只读诊断信息服务：聚合数据库、索引、各源与同步状态的关键事实。
/// 这是排查用户报障时最省沟通成本的一屏（对比方案 P2-8）。
/// </summary>
public sealed class DiagnosticsService
{
    private readonly DbConnectionFactory _factory;
    private readonly IItemRepository _repository;
    private readonly IEnumerable<IItemSource> _sources;

    public DiagnosticsService(DbConnectionFactory factory, IItemRepository repository, IEnumerable<IItemSource> sources)
    {
        _factory = factory;
        _repository = repository;
        _sources = sources;
    }

    public async Task<IReadOnlyList<DiagnosticEntry>> CollectAsync(CancellationToken ct = default)
    {
        var entries = new List<DiagnosticEntry>();

        // ── 数据库 ──
        entries.Add(new DiagnosticEntry("数据库路径", _factory.DbPath));
        entries.Add(new DiagnosticEntry("数据库体积", FormatBytes(GetDbSizeBytes())));
        entries.Add(new DiagnosticEntry("Schema 版本", MigrationRunner.CurrentVersion.ToString()));

        // ── 条目与索引 ──
        var counts = await _repository.GetCountsByTypeAsync(ct);
        long total = 0;
        foreach (var c in counts.Values) total += c;
        string TypeCount(ItemType t) => counts.TryGetValue(t, out var v) ? v.ToString() : "0";
        entries.Add(new DiagnosticEntry("条目总数",
            $"{total}（Stars {TypeCount(ItemType.GitHubStar)} / 书签 {TypeCount(ItemType.Bookmark)}" +
            $" / 文件 {TypeCount(ItemType.File)} / 剪贴板 {TypeCount(ItemType.Clipboard)}）"));

        using (var conn = _factory.Open())
        {
            entries.Add(new DiagnosticEntry("FTS 索引行数", Scalar(conn, "SELECT COUNT(*) FROM items_fts;")));
            entries.Add(new DiagnosticEntry("活动记录数", Scalar(conn, "SELECT COUNT(*) FROM activity;")));
        }

        // ── 各源可用性 ──
        foreach (var source in _sources)
        {
            entries.Add(new DiagnosticEntry($"源：{source.DisplayName}", source.IsAvailable ? "可用" : "不可用"));
        }

        // ── GitHub 同步检查点（P1-4 落库的 sync_state）──
        var lastSynced = await _repository.GetSyncStateAsync("github:last_synced_at", ct);
        entries.Add(new DiagnosticEntry("上次 GitHub 同步", FormatUnixSeconds(lastSynced)));
        var etag = await _repository.GetSyncStateAsync("github:etag", ct);
        entries.Add(new DiagnosticEntry("GitHub ETag",
            string.IsNullOrEmpty(etag) ? "（无，下次全量拉取）" : Shorten(etag)));

        return entries;
    }

    private long GetDbSizeBytes()
    {
        try
        {
            long size = 0;
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var p = _factory.DbPath + suffix;
                if (File.Exists(p)) size += new FileInfo(p).Length;
            }
            return size;
        }
        catch { return 0; }
    }

    private static string Scalar(Microsoft.Data.Sqlite.SqliteConnection conn, string sql)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar()?.ToString() ?? "0";
        }
        catch (Exception ex)
        {
            return $"（查询失败：{ex.Message}）";
        }
    }

    private static string FormatUnixSeconds(string? seconds)
    {
        if (string.IsNullOrEmpty(seconds) || !long.TryParse(seconds, out var sec) || sec <= 0)
            return "从未";
        return DateTimeOffset.FromUnixTimeSeconds(sec).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string Shorten(string value, int max = 24)
        => value.Length <= max ? value : value[..12] + "…" + value[^6..];

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }
}
