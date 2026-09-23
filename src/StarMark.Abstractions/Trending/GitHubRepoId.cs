#nullable enable
using System;

namespace StarMark.Abstractions.Trending;

/// <summary>
/// 仓库标识的解析与校验（纯函数）。
/// <para>
/// 它是热榜"已 Star 判定"与"Star 写接口 URL"的唯一口径来源，两处必须出自同一函数：
/// 一处宽松一处严格就会出现"列表里显示已 Star，点取消却报仓库不存在"。
/// </para>
/// </summary>
public static class GitHubRepoId
{
    /// <summary>
    /// 校验（并顺手去掉误粘的首尾空白与结尾斜杠）<c>owner/repo</c>。
    /// <para>不合格返回 null。<b>这个校验是安全边界</b>：它挡的是"外部来的字符串被拼进 REST 路径"
    /// （<c>PUT /user/starred/{fullName}</c>）——两段之外anything（<c>../../</c>、编码后的斜杠、空格、
    /// 控制字符）都会把一个"给某个仓库加星"的请求变成打到 GitHub 任意端点的请求。</para>
    /// </summary>
    public static string? Normalize(string? fullName)
    {
        var s = fullName?.Trim().Trim('/');
        if (string.IsNullOrEmpty(s)) return null;
        var slash = s.IndexOf('/');
        if (slash <= 0 || slash == s.Length - 1) return null;          // 必须恰好两段：无 / 在前在后都不行
        var owner = s[..slash];
        var repo = s[(slash + 1)..];
        if (repo.Contains('/')) return null;                            // 多段路径（stargazers/forks/tree/…）
        if (!IsSegment(owner) || !IsSegment(repo)) return null;
        return owner + "/" + repo;
    }

    /// <summary>从仓库网页地址取 <c>owner/repo</c>；不是 GitHub 仓库地址或段数不对 ⇒ null。</summary>
    public static string? TryFromUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        if (!Uri.TryCreate(uri.Trim(), UriKind.Absolute, out var parsed)) return null;
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) return null;
        var host = parsed.Host;
        if (!host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase)) return null;

        var segments = parsed.Segments;                                 // ["/", "owner/", "repo/"]
        var parts = new System.Collections.Generic.List<string>();
        foreach (var seg in segments)
        {
            var v = seg.Trim('/');
            if (v.Length > 0) parts.Add(v);
        }
        if (parts.Count != 2) return null;
        var repo = parts[1];
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];   // 克隆地址也指到同一个仓库
        return Normalize(parts[0] + "/" + repo);
    }

    /// <summary>给"我 star 过的仓库"集合用的大小写无关比较器（GitHub 的 owner/repo 大小写不敏感）。</summary>
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    private static bool IsSegment(string s)
    {
        if (s.Length == 0 || s.Length > 100) return false;
        foreach (var c in s)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                     || c == '-' || c == '_' || c == '.';
            if (!ok) return false;
        }
        return true;
    }
}

/// <summary>
/// 热榜候选 ⇄ 本地条目的形状（纯函数）。
/// <para>
/// 「收进收藏」写的是<b>本地书签条目</b>（<c>source = local</c>、<c>type = bookmark</c>），
/// 刻意不是 <c>githubstar</c>：star 是远端状态、由同步带回，把两者混成一种类型就会
/// 让"我想留着看"和"我 star 了"在计数、语言下拉、健康度分母里再也分不开。
/// </para>
/// <para>
/// 删除必须按 <c>(source, source_id)</c> 而不是按 URI：库里同一 URL 可能有别的生产者
/// （浏览器书签导入的书签、上次同步的 star），按 URI 删会连别人的行一起删掉（P-65 的同一形状）。
/// </para>
/// </summary>
public static class TrendingItemDraft
{
    /// <summary>本地书签条目的业务键前缀。</summary>
    public const string BookmarkSourcePrefix = "trending-bookmark:";

    public static string BookmarkSourceId(string fullName) => BookmarkSourcePrefix + fullName.Trim().Trim('/');

    /// <summary>由热榜候选造一条待入库的书签条目（search_text 由仓储统一重建，这里不填）。</summary>
    public static Item ForBookmark(TrendingRepo repo, long nowUnix)
    {
        var fullName = GitHubRepoId.Normalize(repo.FullName) ?? repo.FullName.Trim().Trim('/');
        return new Item
        {
            Id = 0,
            Type = ItemType.Bookmark,
            Source = ItemSources.Local,
            SourceId = BookmarkSourceId(fullName),
            Title = fullName,
            Subtitle = repo.Language is { Length: > 0 } lang ? $"{lang} · ★ {repo.Stars:N0}" : $"★ {repo.Stars:N0}",
            Uri = repo.Url,
            Description = string.IsNullOrWhiteSpace(repo.Description) ? null : repo.Description.Trim(),
            StarsCount = repo.Stars,
            CreatedAt = nowUnix,
            UpdatedAt = nowUnix,
        };
    }
}

/// <summary>
/// 「我 star 过哪些仓库」的查询集——只取<b>本机已同步</b>的 star 条目。
/// <para>
/// 刻意不发 <c>HEAD /user/starred/{owner}/{repo}</c> 探测：那会把 Token 配额花在每次渲染上，
/// 而桌面本来就有同步回来的 star 列表。代价必须说清也必须在文案里体现——
/// 刚在网页上 star 的仓库要等下一次同步才显示"已 Star"，所以这里得出的状态<b>不能</b>被写成"实时"。
/// </para>
/// </summary>
public static class TrendingStarIndex
{
    public static HashSet<string> FromItems(IEnumerable<Item>? items)
    {
        var set = new HashSet<string>(GitHubRepoId.Comparer);
        if (items is null) return set;
        foreach (var item in items)
        {
            var id = GitHubRepoId.TryFromUri(item?.Uri) ?? GitHubRepoId.Normalize(item?.Title);
            if (id is not null) set.Add(id);
        }
        return set;
    }

    public static bool IsStarred(HashSet<string>? set, string? fullName)
        => set is not null && GitHubRepoId.Normalize(fullName) is { } id && set.Contains(id);
}
