#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;

namespace StarMark.Data;

/// <summary>
/// ai_usage 表读写（§20.1 计量）。风格与 <see cref="ItemRepository"/> 一致：短连接 + 参数化。
/// <para><b>写路径吞异常只记日志，绝不向上抛</b>：这是账本，不是功能本体——
/// 为"这行用量没记上"打断用户正在等的整理，是让观测系统拖垮被观测系统；
/// 读路径抛（面板读不到必须说出来，读不到账与没有账是两码事）。</para>
/// </summary>
public sealed class AiUsageRepository : IAiUsageRepository
{
    private readonly DbConnectionFactory _factory;

    public AiUsageRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task LogAsync(AiUsageEntry entry, CancellationToken ct = default)
    {
        try
        {
            using var conn = _factory.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ai_usage(at, feature, provider, model, input_tokens, output_tokens, estimated)
                VALUES(@at, @feature, @provider, @model, @input, @output, @estimated);";
            cmd.Parameters.AddWithValue("@at", entry.At.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("@feature", entry.Feature);
            cmd.Parameters.AddWithValue("@provider", entry.Provider);
            cmd.Parameters.AddWithValue("@model", (object?)entry.Model ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@input", entry.Usage.InputTokens);
            cmd.Parameters.AddWithValue("@output", entry.Usage.OutputTokens);
            cmd.Parameters.AddWithValue("@estimated", entry.Usage.Estimated ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            StarLog.Warn($"[AI 用量] 一条用量没记上（{entry.Feature}）：{ex.Message}");
        }
    }

    public async Task<AiUsageTotals> TotalsSinceAsync(long fromUnixSec, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*), COALESCE(SUM(input_tokens), 0), COALESCE(SUM(output_tokens), 0),
                   COALESCE(SUM(estimated), 0)
            FROM ai_usage WHERE at >= @from;";
        cmd.Parameters.AddWithValue("@from", fromUnixSec);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new AiUsageTotals(0, 0, 0, 0);
        return new AiUsageTotals(
            Convert.ToInt32(reader.GetValue(0)),
            Convert.ToInt64(reader.GetValue(1)),
            Convert.ToInt64(reader.GetValue(2)),
            Convert.ToInt32(reader.GetValue(3)));
    }

    public Task<IReadOnlyList<AiUsageSlice>> ByFeatureSinceAsync(long fromUnixSec, CancellationToken ct = default)
        => GroupSinceAsync(fromUnixSec, "feature", ct);

    public Task<IReadOnlyList<AiUsageSlice>> ByModelSinceAsync(long fromUnixSec, CancellationToken ct = default)
        => GroupSinceAsync(fromUnixSec, "COALESCE(model, '')", ct);

    /// <summary>两组查询只差分组列，共用一条聚合：<b>EstimatedOnly＝该组全是估算</b>
    /// （估算与实测混在一组的合计标成"含估算"是界面渲染的事，数据层只交代事实）。</summary>
    private async Task<IReadOnlyList<AiUsageSlice>> GroupSinceAsync(long fromUnixSec, string column, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT {column} AS name, COUNT(*), COALESCE(SUM(input_tokens + output_tokens), 0),
                   COALESCE(SUM(estimated), 0)
            FROM ai_usage WHERE at >= @from
            GROUP BY name
            ORDER BY COALESCE(SUM(input_tokens + output_tokens), 0) DESC;";
        cmd.Parameters.AddWithValue("@from", fromUnixSec);
        var rows = new List<AiUsageSlice>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var calls = Convert.ToInt32(reader.GetValue(1));
            var estimated = Convert.ToInt32(reader.GetValue(3));
            rows.Add(new AiUsageSlice(
                reader.GetValue(0)?.ToString() ?? string.Empty,
                calls,
                Convert.ToInt64(reader.GetValue(2)),
                calls > 0 && estimated == calls));
        }
        return rows;
    }

    public async Task<long?> MaxRecordedAtAsync(CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(at) FROM ai_usage;";
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    public async Task<IReadOnlyList<AiUsageEntry>> RecentAsync(int take, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        // rowid 作次序的 tie-break：at 只精确到秒，同一秒内的两行也该有稳定的先后
        cmd.CommandText = @"
            SELECT at, feature, provider, model, input_tokens, output_tokens, estimated
            FROM ai_usage ORDER BY at DESC, rowid DESC LIMIT @take;";
        cmd.Parameters.AddWithValue("@take", take);
        var rows = new List<AiUsageEntry>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new AiUsageEntry(
                DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(reader.GetValue(0))),
                reader.GetValue(1)?.ToString() ?? string.Empty,
                reader.GetValue(2)?.ToString() ?? string.Empty,
                reader.IsDBNull(3) ? null : reader.GetValue(3)?.ToString(),
                new AiUsage(
                    Convert.ToInt32(reader.GetValue(4)),
                    Convert.ToInt32(reader.GetValue(5)),
                    Convert.ToInt32(reader.GetValue(6)) != 0)));
        }
        return rows;
    }

    public async Task<IReadOnlyList<AiUsageDay>> ByDaySinceAsync(long fromUnixSec, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        // 'localtime'：趋势按用户过的那一天切，不按 UTC——半夜跑的整理该记在用户看见的那一天上
        cmd.CommandText = @"
            SELECT date(at, 'unixepoch', 'localtime') AS day, COUNT(*),
                   COALESCE(SUM(input_tokens + output_tokens), 0)
            FROM ai_usage WHERE at >= @from
            GROUP BY day ORDER BY day;";
        cmd.Parameters.AddWithValue("@from", fromUnixSec);
        var rows = new List<AiUsageDay>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new AiUsageDay(
                reader.GetValue(0)?.ToString() ?? string.Empty,
                Convert.ToInt32(reader.GetValue(1)),
                Convert.ToInt64(reader.GetValue(2))));
        }
        return rows;
    }
}
