#nullable enable
using System.Text;

namespace StarMark.Abstractions;

/// <summary>
/// 书签 URL 归一化。移植自浏览器扩展 <c>normalize.ts</c>。
/// 目的：同一资源的不同 URL 变体（尾斜杠、utm 参数、默认端口、GitHub 的 <c>tab=</c> 查询等）
/// 归一为同一 <c>source_id</c>，避免重复条目把标签 / 笔记分裂到多条记录上
/// （见扩展对比方案 P1-5）。
/// 仅对 <c>http/https</c> 绝对 URI 生效；<c>file://</c> 等非 http(s) 原样返回，避免破坏文件路径键。
/// 幂等：对已是标准形的 URL 再次归一得到自身。
/// </summary>
public static class UriNormalizer
{
    // 11 个追踪参数：utm_source/medium/campaign/term/content、fbclid、gclid、mc_cid、mc_eid、ref_source
    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "fbclid", "gclid", "mc_cid", "mc_eid", "ref_source",
    };

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return raw!;
        if (uri.Scheme != "http" && uri.Scheme != "https") return raw!;

        var host = uri.Host.ToLowerInvariant();
        var authority = host;
        // 默认端口清零：http:80 / https:443 不出现在重建串里
        if (uri.Port > 0
            && !((uri.Scheme == "http" && uri.Port == 80) || (uri.Scheme == "https" && uri.Port == 443)))
        {
            authority += ":" + uri.Port;
        }

        var path = uri.AbsolutePath;
        // 去尾斜杠（根路径 / 保留）
        if (path.Length > 1 && path.EndsWith('/')) path = path.TrimEnd('/');

        string query;
        if (IsGitHub(host))
        {
            // GitHub 专用：pathname 收窄到 /{owner}/{repo}
            var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length >= 2) path = "/" + segs[0] + "/" + segs[1];
            else if (segs.Length == 1) path = "/" + segs[0];
            // search 含 tab= 时清空 query
            query = HasParam(uri.Query, "tab") ? string.Empty : StripTrackingParams(uri.Query);
        }
        else
        {
            query = StripTrackingParams(uri.Query);
        }

        var sb = new StringBuilder();
        sb.Append(uri.Scheme).Append("://").Append(authority).Append(path);
        if (!string.IsNullOrEmpty(query)) sb.Append(query);
        return sb.ToString();
    }

    private static bool IsGitHub(string host)
        => host == "github.com" || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);

    private static bool HasParam(string query, string name)
    {
        if (string.IsNullOrEmpty(query)) return false;
        foreach (var part in query.TrimStart('?').Split('&'))
        {
            if (part.Length == 0) continue;
            if (string.Equals(part.Split('=')[0], name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string StripTrackingParams(string query)
    {
        if (string.IsNullOrEmpty(query)) return string.Empty;
        var kept = new List<string>();
        foreach (var part in query.TrimStart('?').Split('&'))
        {
            if (part.Length == 0) continue;
            if (TrackingParams.Contains(part.Split('=')[0])) continue;
            kept.Add(part);
        }
        return kept.Count == 0 ? string.Empty : "?" + string.Join("&", kept);
    }
}
