#nullable enable
using System.Globalization;
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

        // ── 这一份是哪次构建 ──
        // 放在最前面：它的用途是回答"你刚才跑的那次，到底含不含最新改动"。
        // 这个问题以前只能靠人记，记错一次的代价是把一轮已修好的判据当成无效又重新猜一遍。
        entries.Add(new DiagnosticEntry("本次运行的构建时间", StarMark.Abstractions.BuildInfo.Display));

        // ── 这一次启动的各段花了多久 ──
        // 与上一行同一个用途：报"启动慢"时不必来回猜，面板上就有分段数字（也才判断得出优化有没有真的变快）。
        if (StarMark.Abstractions.StartupProfile.Segments is { Count: > 0 } segments)
            entries.Add(new DiagnosticEntry("本次启动分段", string.Join(" · ", segments)));

        // ── 数据库 ──
        entries.Add(new DiagnosticEntry("数据库路径", _factory.DbPath));
        entries.Add(new DiagnosticEntry("数据库体积", FormatBytes(GetDbSizeBytes())));
        // 展示库内实际 schema_version，而非代码常量 MigrationRunner.CurrentVersion——
        // 迁移失败/半途中断时二者会背离，常量会让面板永远显示"最新"，掩盖问题。
        entries.Add(new DiagnosticEntry("Schema 版本", GetSchemaVersionDisplay()));

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
        // 不能只写"不可用"：那会把"这台机器压根没装"（用 Edge 的人看见 Chrome 不可用）与
        // "装了但读不到"、"没配 Token" 压成同一句话，用户只能猜。成因由来源自己给（它才知道找过什么）。
        foreach (var source in _sources)
        {
            entries.Add(new DiagnosticEntry(
                $"源：{source.DisplayName}", SourceAvailabilityText.Of(source.IsAvailable, source.AvailabilityHint)));
        }

        // ── GitHub 同步检查点（P1-4 落库的 sync_state）──
        var lastSynced = await _repository.GetSyncStateAsync("github:last_synced_at", ct);
        entries.Add(new DiagnosticEntry("上次 GitHub 同步", FormatUnixSeconds(lastSynced)));
        var etag = await _repository.GetSyncStateAsync("github:etag", ct);
        entries.Add(new DiagnosticEntry("GitHub ETag",
            string.IsNullOrEmpty(etag) ? "（无，下次全量拉取）" : Shorten(etag)));

        return entries;
    }

    /// <summary>读取库内实际 schema_version 并与代码目标版本对比展示；不一致时高亮警示。</summary>
    private string GetSchemaVersionDisplay()
    {
        try
        {
            using var conn = _factory.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM sync_state WHERE key = 'schema_version';";
            var raw = cmd.ExecuteScalar()?.ToString();
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var actual))
                return $"（未记录，目标 {MigrationRunner.CurrentVersion}）";
            return actual == MigrationRunner.CurrentVersion
                ? actual.ToString(CultureInfo.InvariantCulture)
                : $"{actual}（≠ 目标 {MigrationRunner.CurrentVersion}，迁移可能未成功）";
        }
        catch (Exception ex)
        {
            return $"（查询失败：{ex.Message}）";
        }
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
        if (string.IsNullOrEmpty(seconds) || !long.TryParse(seconds, CultureInfo.InvariantCulture, out var sec) || sec <= 0)
            return "从未";
        // InvariantCulture：诊断时间戳必须恒为公历数字；否则 th-TH 等区域会把年份显示成佛历（2569），
        // 这是本仓多次踩过的"佛历漂移"坑（见 RelativeTimeHelper / ActivityItemViewModel 的同款修）。
        return DateTimeOffset.FromUnixTimeSeconds(sec).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string Shorten(string value, int max = 24)
        => value.Length <= max ? value : value[..12] + "…" + value[^6..];

    private static string FormatBytes(long bytes) => FileSizeText.Human(bytes);
}
