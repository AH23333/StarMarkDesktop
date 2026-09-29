#nullable enable
using System.Text;

namespace StarMark.Abstractions;

/// <summary>
/// 书签 URL 归一化。移植自浏览器扩展 <c>normalize.ts</c>。
/// 目的：同一资源的不同 URL 变体（尾斜杠、utm 参数、默认端口、GitHub 的 <c>tab=</c> 查询等）
/// 归一为同一 <c>source_id</c>，避免重复条目把标签 / 笔记分裂到多条记录上
/// （见扩展对比方案 P1-5）。
/// 仅对 <c>http/https</c> 绝对 URI 生效；<c>file://</c> 等非 http(s) 原样返回，避免破坏文件路径键。
/// GitHub 额外三折（批次 RW，P-118）：<c>www.github.com</c>→主域、主域与子域一律 https、
/// 主域的 <c>/owner/repo</c> 段折小写——三者都是"同一仓库裂成两个 source_id"的真实成因。
/// 幂等：对已是标准形的 URL 再次归一得到自身。
/// </summary>
public static class UriNormalizer
{
    // 追踪参数名（逐家分析器抄来的常见项，见扩展 normalize.ts 的同一份清单）。
    // 这里不写"共几颗"——计数注释一旦漏更就是第二份真值（#144），名单本身才是判据。
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

        var scheme = uri.Scheme;
        var host = uri.Host.ToLowerInvariant();
        // www.github.com 与 github.com 指的是同一个仓库，而本机的去重键就是这条归一串本身
        // （扩展 normalize.ts:42 那条注释写着"用户实测踩坑"的原件）。不折叠 ⇒ 书签存成 www 那条、
        // Star 同步带回主域那条，同一仓库裂成两行，还被"疑似重复"误报。
        // 只折 www 这一种：gist.github.com 等子域各自是独立服务，并进主域会把**不同资源**错并成一个键，
        // 那比裂更糟（标签/笔记会挂到别人的条目上）。
        if (host == "www.github.com") host = "github.com";
        var github = IsGitHub(host);
        var githubApex = host == "github.com";
        // GitHub 只服务 https；http://github.com/... 是书签里手打的旧形，不升级同样裂两行。
        if (github) scheme = "https";
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
        // 形如 http://host// 的多斜杠根：TrimEnd 后塌成空串，重建得 http://host（无尾斜杠）；
        // 再归一时 AbsolutePath 变 "/"（长度 1，不再触发上面的去尾斜杠）→ http://host/。两次结果不一致，
        // 破坏幂等且让同一资源裂成两个 source_id。钳回 "/" 使两条路径收敛到同一标准形。
        if (path.Length == 0) path = "/";

        string query;
        if (github)
        {
            // GitHub 专用：pathname 收窄到 /{owner}/{repo}
            var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length >= 2) path = "/" + segs[0] + "/" + segs[1];
            else if (segs.Length == 1) path = "/" + segs[0];
            // owner/repo 在 GitHub 侧大小写无关（服务器会重定向到规范名），而这里的键就是这条串本身
            // ⇒ 不折叠则 /Foo/Bar 与 /foo/bar 各占一行。只对主域折：gist 的 id 段不保证大小写无关。
            if (githubApex) path = path.ToLowerInvariant();
            // search 含 tab= 时清空 query
            query = HasParam(uri.Query, "tab") ? string.Empty : StripTrackingParams(uri.Query);
        }
        else
        {
            query = StripTrackingParams(uri.Query);
        }

        var sb = new StringBuilder();
        sb.Append(scheme).Append("://").Append(authority).Append(path);
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
