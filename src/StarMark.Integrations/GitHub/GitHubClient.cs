#nullable enable
using StarMark.Abstractions.Trending;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarMark.Integrations.GitHub;

/// <summary>
/// GitHub REST API 客户端。
/// 只暴露 StarMarkDesktop 需要的接口：分页拉取 starred 列表。
/// 对应 GitHub REST API：GET /user/starred?per_page=100&amp;page=N
/// 文档：https://docs.github.com/en/rest/activity/starring#list-repositories-starred-by-the-authenticated-user
/// </summary>
public sealed class GitHubClient : IDisposable
{
    private const string ApiBase = "https://api.github.com";
    private const string UserAgent = "StarMarkDesktop/1.0";
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly GitHubOptions _options;

    /// <summary>
    /// 实际发给 GitHub 的每页条数。GitHub 硬性上限 100：请求 &gt;100 时它静默按 100 返回，
    /// 而翻页停止判定若用原始 PageSize 会把「返回 100 &lt; PageSize(200)」误判为末页 → 只拉第一页、
    /// 静默丢后续 Star。故 URL 与停止判定统一用这个钳到 [1,100] 的同源值（github.json 属外部输入，可被
    /// 手改/坏备份塞进越界值）。
    /// </summary>
    private readonly int _perPage;

    /// <summary>
    /// 条件请求缓存的 ETag（P1-4）。非 null 时首页请求带 If-None-Match，命中 304 直接短路整轮拉取。
    /// <para><b>不变式：它只代表"完整拉完的一轮"</b>——页 1 的 ETag 描述的是全量列表，中途取消/失败的
    /// 一轮若也把它前移，下一轮就会被 304 短路而永远补不回缺失部分（见 <see cref="GetAllStarredAsync"/>）。</para>
    /// </summary>
    public string? CachedETag { get; set; }

    public GitHubClient(GitHubOptions options, HttpClient? http = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _perPage = Math.Clamp(_options.PageSize, 1, 100);

        // 自己 new 的 HttpClient 必须有界：默认 100 s，代理/门户网络（TCP 连上但不回包）下每页都要
        // 冻满 100 s，而同步入口传的是 CancellationToken.None ⇒ 用户只能等或杀进程。
        // 注入进来的不动 Timeout（可能是共享实例，改它会波及别处）。
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        SyncCredentials();
    }

    /// <summary>
    /// 把 <c>Authorization</c> 头换成 <see cref="_options"/> 里的当前 Token（设置页保存 Token 后调用）。
    /// <para>
    /// 少这一步就会出一个"改了还得重启"的坑：<see cref="IsConfigured"/> 读的是配置对象的当前值，
    /// 而请求带的是<b>构造时</b>那一次的凭据 ⇒ 界面以为已配置好、GitHub 回 401，用户只会觉得"填了没用"。
    /// </para>
    /// </summary>
    public void SyncCredentials()
    {
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(_options.Token)
            ? null
            : new AuthenticationHeaderValue("Bearer", _options.Token);
    }

    /// <summary>
    /// 检测 Token 是否配置。MVP 阶段不主动验证 Token 有效性（懒失败）。
    /// <b>判据本体在 <see cref="GitHubOptions.IsConfigured"/>，这里只是转发</b>——
    /// 源的 <c>IsAvailable</c> 要在不建客户端的情况下问同一件事，两处各写一遍就会分岔。
    /// </summary>
    public bool IsConfigured => _options.IsConfigured;

    /// <summary>
    /// 拉取当前用户 starred 仓库列表（分页）。
    /// 对应 GitHubStarMeta 字段：full_name / language / stargazers_count / topics / archived / homepage / html_url
    /// starred_at 由 Link header 不可用，需要从 /user/starred 的 header 中读 pushed_at 替代。
    /// </summary>
    /// <param name="page">页码（1 起始）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>该页的 starred 仓库列表。</returns>
    public async Task<IReadOnlyList<GitHubStarApiModel>> GetStarredPageAsync(int page, CancellationToken ct)
    {
        if (!IsConfigured) return Array.Empty<GitHubStarApiModel>();

        var url = $"{ApiBase}/user/starred?per_page={_perPage}&page={page}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        // P1-4 条件请求：仅首页带 If-None-Match，命中 304 可省掉整轮拉取
        if (page == 1 && !string.IsNullOrEmpty(CachedETag))
        {
            req.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(CachedETag));
        }

        using var resp = await _http.SendAsync(req, ct);

        // P1-4 命中 304：内容未变，返回空（调用方据此短路整轮拉取）
        if (resp.StatusCode == HttpStatusCode.NotModified)
            return Array.Empty<GitHubStarApiModel>();

        // P1-4 错误分类：把 401/403/429 从通用 Exception 里分出来
        if (!resp.IsSuccessStatusCode)
            throw Classify(resp);

        // 仅在首页持久化 ETag（分页场景以首页为准），供下次条件请求
        if (page == 1 && resp.Headers.ETag is { } etag)
            CachedETag = etag.Tag;

        var list = await resp.Content.ReadFromJsonAsync<List<GitHubStarApiModel>>(JsonOpts, ct)
                   ?? new List<GitHubStarApiModel>();

        // 遍历每条记录的 starred_at 需要单独 API 调用，过于昂贵。
        // 改为从 pushed_at 推断（最近一次 push，用作 updated_at 即可）。
        return list;
    }

    /// <summary>
    /// 拉取全量 starred 列表（自动分页，遵循 GitHub API 分页）。当返回数 &lt; pageSize 时停止翻页。
    /// <para>
    /// <b>取消＝抛，不是"带回半截"</b>：旧写法中途取消会 <c>break</c> 返回已拉到的那几页，而在调用方
    /// （<c>GitHubSource.FetchAsync</c>）看来这与完整一轮毫无差别 ⇒ 照样把页 1 带回的<b>全量列表</b> ETag
    /// 与 last_synced_at 落进 sync_state。下一次同步首页带 <c>If-None-Match</c> 直接命中 304 短路，
    /// 被掐掉的那几页<b>永远补不回来</b>（P-55；与 P-19 的崩溃窗口同一失序面，但取消是日常操作，窗口宽得多）。
    /// </para>
    /// <para>同理，异常/取消退回去时把 <see cref="CachedETag"/> 复位：单例客户端内存里留着"跑完才成立"的
    /// ETag，下一轮即使换了机器也会凭空 304。</para>
    /// </summary>
    public async Task<IReadOnlyList<GitHubStarApiModel>> GetAllStarredAsync(CancellationToken ct)
    {
        if (!IsConfigured) return Array.Empty<GitHubStarApiModel>();

        var etagBefore = CachedETag;   // 本轮开始前的检查点：未完成的一轮不得推进它
        var all = new List<GitHubStarApiModel>();
        var page = 1;
        // 安全上限按"条目数"而非"固定页数"封顶。过去 maxPages 固定 50，使真实上限 = 50 × _perPage，
        // AZ-3 让 _perPage 可随 PageSize 调小后，PageSize=10 会把可拉取量从宣称的 5000 静默砍到 500。
        const int maxItems = 5000;  // 安全上限：5000 仓库已远超常见用户的 starred 数
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var pageList = await GetStarredPageAsync(page, ct);
                if (pageList.Count == 0) break;
                all.AddRange(pageList);
                if (pageList.Count < _perPage) break;   // 末页：返回数不足一页
                if (all.Count >= maxItems) break;        // 条目预算封顶
                page++;
            }
        }
        catch
        {
            CachedETag = etagBefore;
            throw;
        }
        if (all.Count > maxItems) all.RemoveRange(maxItems, all.Count - maxItems);
        return all;
    }

    /// <summary>
    /// 获取当前认证用户信息（验证 Token 有效性 + 提取 login 名）。
    /// 对应 GET /user。
    /// </summary>
    public async Task<string?> GetLoginAsync(CancellationToken ct)
    {
        if (!IsConfigured) return null;
        using var resp = await _http.GetAsync($"{ApiBase}/user", ct);
        if (!resp.IsSuccessStatusCode) return null;
        var user = await resp.Content.ReadFromJsonAsync<GitHubUserApiModel>(JsonOpts, ct);
        return user?.Login;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// 给仓库加星 / 取消星（本应用<b>第一个 GitHub 写操作</b>）：
    /// <c>PUT</c> / <c>DELETE</c> <c>/user/starred/{owner}/{repo}</c>，成功是 204 无正文。
    /// <para>
    /// 与读侧的关键差别：读侧失败可以"少一批数据"，这里是<b>用户点了一下按钮</b>，
    /// 所以任何不成功的分支都必须抛出让界面说出原因——包括没配 Token
    /// （读侧那句 <c>if (!IsConfigured) return null/空;</c> 的静默处置在这里就是"点了没反应"）。
    /// </para>
    /// <para>
    /// <paramref name="fullName"/> 必须先过 <see cref="GitHubRepoId.Normalize"/>：它会被拼进 REST 路径，
    /// 不合法的两段之外的形状（<c>../../</c>、编码斜杠、空格）能把"给某仓库加星"变成打到任意端点的请求。
    /// </para>
    /// <param name="starred">true＝加星，false＝取消星。</param>
    /// <remarks>取消星时 404 视为成功：目标状态（"不再 star"）已经达成，报失败只会让用户以为没生效。</remarks>
    public async Task SetStarredAsync(string fullName, bool starred, CancellationToken ct)
    {
        if (GitHubRepoId.Normalize(fullName) is not { } repo)
            throw new GitHubApiException(GitHubErrorKind.Unknown, $"仓库标识不合法，无法{(starred ? "加星" : "取消星")}：「{fullName}」");
        if (!IsConfigured)
            throw new GitHubApiException(GitHubErrorKind.Auth,
                "未配置 GitHub Token（在设置里填入带 public_repo 范围的 Token 后即可）",
                HttpStatusCode.Unauthorized);

        using var req = new HttpRequestMessage(starred ? HttpMethod.Put : HttpMethod.Delete,
            $"{ApiBase}/user/starred/{repo}");
        req.Content = new ByteArrayContent(Array.Empty<byte>());   // GitHub 要求空正文 + Content-Length: 0
        using var resp = await _http.SendAsync(req, ct);

        if (resp.StatusCode == HttpStatusCode.NoContent) return;
        if (!starred && resp.StatusCode == HttpStatusCode.NotFound) return;
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new GitHubApiException(GitHubErrorKind.Forbidden,
                "GitHub 找不到该仓库（可能已改名、被删除或不可见）", HttpStatusCode.NotFound);
        if (!resp.IsSuccessStatusCode) throw Classify(resp);
    }

    /// <summary>P1-4 把非成功响应分类为可操作的错误类型。</summary>
    private static GitHubApiException Classify(HttpResponseMessage resp)
    {
        var status = resp.StatusCode;
        // 403 且限流剩余为 0 视为限流（GitHub 常以 403 返回限流）
        var remaining = resp.Headers.TryGetValues("X-RateLimit-Remaining", out var vals)
            ? vals.FirstOrDefault() : null;
        var isRateLimited = status == HttpStatusCode.TooManyRequests
                            || (status == HttpStatusCode.Forbidden && remaining == "0");

        var kind = status switch
        {
            HttpStatusCode.Unauthorized => GitHubErrorKind.Auth,
            _ when isRateLimited => GitHubErrorKind.RateLimit,
            HttpStatusCode.Forbidden => GitHubErrorKind.Forbidden,
            _ when (int)status >= 500 => GitHubErrorKind.Server,
            _ => GitHubErrorKind.Unknown,
        };

        var message = kind switch
        {
            GitHubErrorKind.Auth => "GitHub 令牌无效或已过期，请在设置中重新配置 Token（需 public_repo 或 read:user 范围）",
            GitHubErrorKind.RateLimit => "GitHub API 触发限流，请稍后再试（通常 1 小时后自动恢复）",
            GitHubErrorKind.Forbidden => "GitHub 拒绝访问，可能 Token 权限不足（需 public_repo 范围）",
            GitHubErrorKind.Server => "GitHub 服务端异常，请稍后重试",
            _ => $"GitHub 请求失败（{(int)status} {status}）",
        };
        return new GitHubApiException(kind, message, status);
    }
}

/// <summary>P1-4 GitHub 同步错误分类，供 UI 给出可操作文案。</summary>
public enum GitHubErrorKind
{
    Unknown,
    /// <summary>401：Token 无效/过期。</summary>
    Auth,
    /// <summary>限流（429 或 403 且 X-RateLimit-Remaining:0）。</summary>
    RateLimit,
    /// <summary>403：权限不足（如 Token 缺少 public_repo 范围）。</summary>
    Forbidden,
    /// <summary>5xx 服务端异常。</summary>
    Server,
}

/// <summary>P1-4 携带分类信息的 GitHub API 异常。</summary>
public sealed class GitHubApiException : Exception
{
    public GitHubErrorKind Kind { get; }
    public System.Net.HttpStatusCode? StatusCode { get; }

    public GitHubApiException(GitHubErrorKind kind, string message,
        System.Net.HttpStatusCode? status = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        StatusCode = status;
    }
}

/// <summary>
/// GET /user/starred 返回的 repo 数据模型（仅保留 StarMarkDesktop 关心的字段）。
/// 全部字段均为 snake_case，对应 GitHub API JSON 命名（snake_case）。
/// </summary>
public sealed class GitHubStarApiModel
{
    public long Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Homepage { get; set; }
    public string HtmlUrl { get; set; } = string.Empty;
    public string? Language { get; set; }
    public long StargazersCount { get; set; }
    public bool Archived { get; set; }
    public List<string> Topics { get; set; } = new();
    public string PushedAt { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? UpdatedAt { get; set; }

    /// <summary>Owner login，仅在 topics 子对象里有用。</summary>
    public GitHubOwnerApiModel? Owner { get; set; }
}

public sealed class GitHubOwnerApiModel
{
    public string Login { get; set; } = string.Empty;
}

public sealed class GitHubUserApiModel
{
    public string Login { get; set; } = string.Empty;
    public string? Name { get; set; }
}
