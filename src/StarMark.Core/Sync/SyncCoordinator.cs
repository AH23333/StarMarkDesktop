#nullable enable
using System.Diagnostics;
using StarMark.Abstractions;
using StarMark.Integrations.GitHub;

namespace StarMark.Core.Sync;

/// <summary>
/// 同步协调器。对应技术文档 §4.7 sync_state 与 §3.2 SyncCoordinator。
/// 职责：
///   - 遍历所有 IItemSource，串行 FetchAsync（避免 GitHub API 限流叠加）
///   - 全量结果批量 UpsertAsync 到 DB
///   - 返回汇总结果，供 UI 显示
/// </summary>
public sealed class SyncCoordinator
{
    private readonly IItemRepository _repository;
    private readonly IReadOnlyList<IItemSource> _sources;

    public SyncCoordinator(IItemRepository repository, IEnumerable<IItemSource> sources)
    {
        _repository = repository;
        _sources = sources.ToList();
    }

    /// <summary>
    /// 执行一次全量同步。每个源拉取后立即 upsert（避免缓存全部结果占用内存）。
    /// </summary>
    public async Task<SyncSummary> SyncAllAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var results = new List<SourceSyncResult>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        StarLog.Info($"同步开始，共 {_sources.Count} 个源");

        foreach (var source in _sources)
        {
            if (!source.IsAvailable)
            {
                StarLog.Warn($"源 {source.SourceId} 不可用（未配置或依赖未运行）");
                results.Add(new SourceSyncResult
                {
                    SourceId = source.SourceId,
                    DisplayName = source.DisplayName,
                    Success = true,
                    PulledCount = 0,
                    Error = "源不可用（未配置或依赖未运行）",
                });
                continue;
            }

            try
            {
                var items = await source.FetchAsync(new SyncContext(), ct);
                if (items.Count > 0)
                {
                    // 防御：本地条目（待办/随记，source=local）永不参与同步覆盖。
                    // 正常来源不会产出 local 条目，此处仅作保险，避免未来误覆盖。
                    var syncable = items.Where(i => i.Source != ItemSources.Local).ToList();
                    if (syncable.Count > 0)
                        await _repository.UpsertAsync(syncable, ct);
                }
                StarLog.Info($"源 {source.SourceId} 拉取 {items.Count} 条");
                results.Add(new SourceSyncResult
                {
                    SourceId = source.SourceId,
                    DisplayName = source.DisplayName,
                    Success = true,
                    PulledCount = items.Count,
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (GitHubApiException gex)
            {
                StarLog.Error($"源 {source.SourceId} 同步失败（{gex.Kind}）: {gex.Message}");
                var kind = gex.Kind switch
                {
                    GitHubErrorKind.Auth => SourceSyncErrorKind.Auth,
                    GitHubErrorKind.RateLimit => SourceSyncErrorKind.RateLimit,
                    GitHubErrorKind.Forbidden => SourceSyncErrorKind.Forbidden,
                    GitHubErrorKind.Server => SourceSyncErrorKind.Server,
                    _ => SourceSyncErrorKind.Unknown,
                };
                results.Add(new SourceSyncResult
                {
                    SourceId = source.SourceId,
                    DisplayName = source.DisplayName,
                    Success = false,
                    PulledCount = 0,
                    Error = gex.Message,
                    ErrorKind = kind,
                });
            }
            catch (Exception ex)
            {
                StarLog.Error($"源 {source.SourceId} 同步失败: {ex.Message}");
                results.Add(new SourceSyncResult
                {
                    SourceId = source.SourceId,
                    DisplayName = source.DisplayName,
                    Success = false,
                    PulledCount = 0,
                    Error = ex.Message,
                    ErrorKind = SourceSyncErrorKind.Unknown,
                });
            }
        }

        sw.Stop();
        StarLog.Info($"同步结束，耗时 {sw.ElapsedMilliseconds}ms，失败 {results.Count(r => !r.Success)} 源");
        return new SyncSummary
        {
            StartedAt = now,
            ElapsedMs = sw.ElapsedMilliseconds,
            Sources = results,
        };
    }
}

/// <summary>单次同步的汇总报告。</summary>
public sealed class SyncSummary
{
    public long StartedAt { get; init; }
    public long ElapsedMs { get; init; }
    public List<SourceSyncResult> Sources { get; init; } = new();

    public int TotalPulled => Sources.Sum(s => s.PulledCount);
    public int FailedCount => Sources.Count(s => !s.Success);

    public string FormatText()
    {
        var ok = TotalPulled;
        var fail = FailedCount;
        var elapsed = ElapsedMs;
        if (fail == 0)
            return $"同步完成: {ok} 条 · {elapsed}ms";
        return $"同步部分失败: 成功 {ok} 条 · 失败 {fail} 源 · {elapsed}ms";
    }
}

/// <summary>同步错误分类（P1-4），供设置页/同步状态给出可操作文案。</summary>
public enum SourceSyncErrorKind
{
    Unknown,
    /// <summary>鉴权失败（Token 无效/过期）。</summary>
    Auth,
    /// <summary>API 限流。</summary>
    RateLimit,
    /// <summary>权限不足（403）。</summary>
    Forbidden,
    /// <summary>服务端异常（5xx）。</summary>
    Server,
}

/// <summary>单个源的同步结果。</summary>
public sealed class SourceSyncResult
{
    public string SourceId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool Success { get; init; }
    public int PulledCount { get; init; }
    public string? Error { get; init; }
    /// <summary>P1-4 错误分类，便于 UI 给出针对性引导。</summary>
    public SourceSyncErrorKind ErrorKind { get; init; } = SourceSyncErrorKind.Unknown;
}
