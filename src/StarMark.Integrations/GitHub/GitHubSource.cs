#nullable enable
using System.Globalization;
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
    private readonly IItemRepository _repository;
    private bool _disposed;

    public GitHubSource(GitHubOptions options, IItemRepository repository)
    {
        _options = options;
        _repository = repository;
        _client = new GitHubClient(options);
    }

    public string SourceId => ItemSources.GitHub;

    public string DisplayName => "GitHub Stars";

    public bool IsAvailable => _client.IsConfigured;

    /// <summary>
    /// 全量拉取 starred 列表。
    /// 对应技术文档 §4.7 sync_state（P1-4 兑现）：此处把 etag + last_synced_at 写入 DB 的 sync_state 表。
    /// 同步策略：
    ///   - 拉取前载入上次 ETag，用于条件请求（命中 304 省掉整轮拉取）
    ///   - 拉取后回写最新 ETag 与 last_synced_at 检查点
    /// </summary>
    public async Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
    {
        if (!IsAvailable) return Array.Empty<Item>();

        // P1-4：载入上次 ETag，用于条件请求（命中 304 直接短路整轮拉取）
        var priorEtag = await _repository.GetSyncStateAsync("github:etag", ct);
        if (!string.IsNullOrEmpty(priorEtag)) _client.CachedETag = priorEtag;

        var repos = await _client.GetAllStarredAsync(ct);

        // P1-4：兑现注释——落 etag + last_synced_at 到 sync_state
        if (!string.IsNullOrEmpty(_client.CachedETag))
            await _repository.SetSyncStateAsync("github:etag", _client.CachedETag, ct);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _repository.SetSyncStateAsync("github:last_synced_at", now.ToString(), ct);

        if (repos.Count == 0) return Array.Empty<Item>();

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

    /// <summary>GitHub REST API repo 模型 → StarMark Item。<c>internal</c> 仅为可机检单测（StarMark.Tests 已 InternalsVisibleTo）。</summary>
    internal static Item MapToItem(GitHubStarApiModel repo, long now)
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

    /// <summary>
    /// ISO 8601 时间字符串 → Unix 秒。失败返回 null。<c>internal</c> 仅为可机检单测。
    /// <para>
    /// 锁 InvariantCulture 是<b>纵深防御</b>：GitHub 现网回的 <c>2024-05-01T12:00:00Z</c> 这类完整
    /// ISO 走 .NET 的 ISO 快路，本就不吃机器文化的历法（已实测）；但日期一旦退化成
    /// <c>2024-05-01</c> / <c>2024-05-01 12:00:00</c>（代理改写、缓存字段、以后别的来源），
    /// th-TH/ar-SA 的 <c>DateTimeFormatInfo.Calendar</c> 会把四位数年份按佛历/希吉来历解释，
    /// 实测同一字符串从 1.71e9 掉到 -1.54e10（差 ~540 年），排序/相对时间/洞察一起歪。
    /// 与日志文件名的同源处置（见 StarLog），也与 OpenMeteoClient 已锁文化的时间解析一致。
    /// 逐例断言见 CultureInvariantNamingTests。
    /// </para>
    /// </summary>
    internal static long? ParseUnixTime(string? iso)
    {
        if (string.IsNullOrEmpty(iso)) return null;
        if (DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
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
