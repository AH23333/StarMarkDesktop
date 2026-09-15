#nullable enable
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

    public GitHubClient(GitHubOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrEmpty(_options.Token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        }
    }

    /// <summary>检测 Token 是否配置。MVP 阶段不主动验证 Token 有效性（懒失败）。</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(_options.Token);

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

        var url = $"{ApiBase}/user/starred?per_page={_options.PageSize}&page={page}";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();

        var list = await resp.Content.ReadFromJsonAsync<List<GitHubStarApiModel>>(JsonOpts, ct)
                   ?? new List<GitHubStarApiModel>();

        // 遍历每条记录的 starred_at 需要单独 API 调用，过于昂贵。
        // 改为从 pushed_at 推断（最近一次 push，用作 updated_at 即可）。
        return list;
    }

    /// <summary>
    /// 拉取全量 starred 列表（自动分页，遵循 GitHub API 分页）。
    /// 当返回数 &lt; pageSize 时停止翻页。
    /// </summary>
    public async Task<IReadOnlyList<GitHubStarApiModel>> GetAllStarredAsync(CancellationToken ct)
    {
        if (!IsConfigured) return Array.Empty<GitHubStarApiModel>();

        var all = new List<GitHubStarApiModel>();
        var page = 1;
        const int maxPages = 50;  // 安全上限：5000 仓库已远超常见用户的 starred 数
        while (page <= maxPages)
        {
            if (ct.IsCancellationRequested) break;
            var pageList = await GetStarredPageAsync(page, ct);
            if (pageList.Count == 0) break;
            all.AddRange(pageList);
            if (pageList.Count < _options.PageSize) break;
            page++;
        }
        return all;
    }

    /// <summary>
    /// 获取当前认证用户信息（验证 Token 有效性 + 提取 login 名）。
    /// 对应 GET /user。
    /// </summary>
    public async Task<string?> GetLoginAsync(CancellationToken ct)
    {
        if (!IsConfigured) return null;
        var resp = await _http.GetAsync($"{ApiBase}/user", ct);
        if (!resp.IsSuccessStatusCode) return null;
        var user = await resp.Content.ReadFromJsonAsync<GitHubUserApiModel>(JsonOpts, ct);
        return user?.Login;
    }

    public void Dispose() => _http.Dispose();
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
