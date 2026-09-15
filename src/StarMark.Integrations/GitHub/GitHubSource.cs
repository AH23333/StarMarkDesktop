#nullable enable
using System.Text.Json;
using StarMark.Abstractions;

namespace StarMark.Integrations.GitHub;

/// <summary>
/// GitHub Stars 源适配器。实现 <see cref="IItemSource"/>。
/// 对应技术文档 §3.2 GitHubSource：拉取用户 starred 列表，写入 items 表。
/// 同步策略：
///   - FetchAsync：全量拉取所有页，写入 DB（按 source+source_id 幂等）
///   - SearchAsync：透传给 SearchService.FTS5（GitHub Stars 不支持实时查询；MVP 不实现）
/// 增量同步通过 SyncContext.ContinuationToken 传递上次最后一条 repo id，本 MVP 暂未启用。
/// </summary>
public sealed class GitHubSource : IItemSource, IAsyncDisposable
{
    private readonly GitHubClient _client;
    private readonly GitHubOptions _options;
    private bool _disposed;

    public GitHubSource(GitHubOptions options)
    {
        _options = options;
        _client = new GitHubClient(options);
    }

    public string SourceId => ItemSources.GitHub;

    public string DisplayName => "GitHub Stars";

    public bool IsAvailable => _client.IsConfigured;

    /// <summary>
    /// 全量拉取 starred 列表。
    /// 对应技术文档 §4.7 sync_state：此处 last_synced_at 写入 DB 的 sync_state 表。
    /// 当前 MVP 直接返回 Item 列表，由 SyncCoordinator 统一调度 upsert 与状态写入。
    /// </summary>
    public async Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
    {
        if (!IsAvailable) return Array.Empty<Item>();

        var repos = await _client.GetAllStarredAsync(ct);
        if (repos.Count == 0) return Array.Empty<Item>();

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var items = new List<Item>(repos.Count);
        foreach (var repo in repos)
        {
            items.Add(MapToItem(repo, now));
        }
        return items;
    }

    /// <summary>
    /// 实时搜索：MVP 阶段不实现（GitHub Search API 限流较严，且已有本地 DB 缓存）。
    /// 返回空，由 SearchService 落到本地 FTS5 查询。
    /// </summary>
    public Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());

    /// <summary>GitHub REST API repo 模型 → StarMark Item。</summary>
    private static Item MapToItem(GitHubStarApiModel repo, long now)
    {
        var starredAt = ParseUnixTime(repo.PushedAt) ?? now;
        var updatedAt = ParseUnixTime(repo.UpdatedAt) ?? ParseUnixTime(repo.PushedAt) ?? now;
        var createdAt = ParseUnixTime(repo.CreatedAt) ?? now;

        var meta = new GitHubStarMeta
        {
            FullName = repo.FullName,
            Owner = repo.Owner?.Login ?? string.Empty,
            Repo = repo.FullName.Contains('/') ? repo.FullName.Split('/', 2)[1] : repo.FullName,
            Language = repo.Language,
            Stars = repo.StargazersCount,
            Topics = repo.Topics ?? new(),
            Archived = repo.Archived,
            Homepage = repo.Homepage,
            Url = repo.HtmlUrl,
            StarredAt = starredAt,
        };

        var ownerLogin = meta.Owner;
        var title = meta.Repo + (string.IsNullOrEmpty(meta.Language) ? string.Empty : $" · {meta.Language}");

        return new Item
        {
            Type = ItemType.GitHubStar,
            Source = ItemSources.GitHub,
            SourceId = "repo-" + repo.Id,
            Title = title,
            Subtitle = meta.FullName,
            Uri = meta.Url,
            Description = repo.Description ?? string.Empty,
            StarsCount = meta.Stars,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            SyncedAt = now,
            Tags = meta.Topics.Take(10).ToList(),
            ExtraJson = JsonSerializer.Serialize(meta),
        };
    }

    /// <summary>ISO 8601 时间字符串 → Unix 秒。失败返回 null。</summary>
    private static long? ParseUnixTime(string? iso)
    {
        if (string.IsNullOrEmpty(iso)) return null;
        if (DateTimeOffset.TryParse(iso, out var dto))
            return dto.ToUnixTimeSeconds();
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await Task.Run(() => _client.Dispose());
    }
}
