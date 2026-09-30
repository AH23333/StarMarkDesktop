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

    /// <summary>
    /// 这一轮拉到、但<b>还没资格落库</b>的检查点（P-19）。null＝这一轮没跑完（取消/抛出/源不可用），
    /// 所以 <see cref="CommitCheckpointAsync"/> 什么都不写。Etag 可为 null（GitHub 没回 ETag 时只提交时间）。
    /// </summary>
    private (string? Etag, long At)? _pendingCheckpoint;

    public GitHubSource(GitHubOptions options, IItemRepository repository)
        : this(options, repository, new GitHubClient(options))
    {
    }

    /// <summary>测试缝：注入带假 HTTP 的客户端，用来钉死"取消/失败的一轮绝不推进 sync_state 检查点"。</summary>
    internal GitHubSource(GitHubOptions options, IItemRepository repository, GitHubClient client)
    {
        _options = options;
        _repository = repository;
        _client = client;
    }

    public string SourceId => ItemSources.GitHub;

    public string DisplayName => "GitHub Stars";

    public bool IsAvailable => _client.IsConfigured;

    /// <summary>"不可用"在这里的成因是<b>没配 Token</b>，不是坏了；且只影响 Star 同步这一条源。</summary>
    public string? AvailabilityHint
        => "未配置 GitHub Token（设置 → GitHub Stars 同步 里填入带 public_repo 范围的 Token 才会同步 Star；书签 / 本地文件 / 热榜浏览都不受影响）";

    /// <summary>
    /// 全量拉取 starred 列表。<b>本方法不写检查点</b>——它只把"这一轮拉到了什么检查点"暂存起来，
    /// 等 <see cref="CommitCheckpointAsync"/> 在协调器把载荷落库之后再写（P-19）。
    /// 对应技术文档 §4.7 sync_state（P1-4 兑现）：etag + last_synced_at 存 DB 的 sync_state 表。
    /// 同步策略：
    ///   - 拉取前载入上次 ETag，用于条件请求（命中 304 省掉整轮拉取）
    ///   - 拉取后暂存最新 ETag 与 last_synced_at，落库成功后才提交
    /// <para>
    /// <b>两道时序都要，缺一道都会永久丢数据</b>：
    /// ① <b>整轮分页跑完</b>才暂存（中途取消/失败的那一轮抛出 ⇒ 什么都不留，P-55）；
    /// ② <b>载荷已 <c>UpsertAsync</c> 进库</b>才落检查点（P-19）——旧写法在 <c>FetchAsync</c> 内部就写库，
    /// 而 upsert 是协调器拿到返回后才做的，中间那一崩＝<b>新 ETag 已进、条目没进</b>，
    /// 下一轮首页带 <c>If-None-Match</c> 命中 304 ⇒ 返回零条 ⇒ 那批新 Star 再也不会被拉回来，
    /// 直到用户的 starred 列表再次变动产生新 ETag。
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
    {
        _pendingCheckpoint = null;      // 上一轮没提交成的（崩溃/异常）一律不带进这一轮：宁可重复提交，不可提交空结果
        if (!IsAvailable) return Array.Empty<Item>();

        // P1-4：载入上次 ETag，用于条件请求（命中 304 直接短路整轮拉取）
        var priorEtag = await _repository.GetSyncStateAsync("github:etag", ct);
        if (!string.IsNullOrEmpty(priorEtag)) _client.CachedETag = priorEtag;

        var repos = await _client.GetAllStarredAsync(ct);

        // P1-4：整轮跑完才暂存 etag + last_synced_at（落库交给 CommitCheckpointAsync，见上面②）
        _pendingCheckpoint = _client.CachedETag is { Length: > 0 } freshEtag
            ? (freshEtag, DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            : (null, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var now = _pendingCheckpoint.Value.At;

        if (repos.Count == 0) return Array.Empty<Item>();

        var items = new List<Item>(repos.Count);
        foreach (var repo in repos)
        {
            items.Add(MapToItem(repo, now));
        }
        return items;
    }

    /// <summary>
    /// 提交这一轮的检查点（etag + last_synced_at）到 sync_state。<b>由同步协调器在载荷 <c>UpsertAsync</c>
    /// 成功之后调用</b>（P-19）——顺序反过来就是缺陷本身：ETag 先进库、条目没进，下一轮命中 304 返回零条，
    /// 那批新 Star 就永远不会被重新拉回来。
    /// <para>这里刻意<b>不吞异常</b>：提交失败意味着这一轮的条目其实已经落库了，下一次同步会带着旧 ETag
    /// 再全量拉一遍（多花一次请求），而不是反过来丢数据——报错比悄悄少一批好。</para>
    /// </summary>
    public async Task CommitCheckpointAsync(CancellationToken ct)
    {
        var pending = _pendingCheckpoint;
        if (pending is null) return;                      // 没跑完的一轮：什么都不写，下一次照样全量拉
        _pendingCheckpoint = null;

        if (pending.Value.Etag is { Length: > 0 } etag)
            await _repository.SetSyncStateAsync("github:etag", etag, ct);
        await _repository.SetSyncStateAsync("github:last_synced_at", pending.Value.At.ToString(), ct);
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
