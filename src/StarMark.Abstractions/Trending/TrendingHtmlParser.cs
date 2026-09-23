#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StarMark.Abstractions.Trending;

/// <summary>
/// GitHub trending 页 HTML → 候选列表（纯函数：无网络、无 DOM、可单测）。
/// <para>
/// 解析口径逐条抄自浏览器扩展 <c>src/core/trending.ts</c> 的 <c>parseTrendingHtml</c>，
/// 包括它在 2026-09 踩过后修掉的三个坑，**不要"顺手优化"成别的写法**（改回去就是把已修的坑重新挖开）：
/// ① 仓库主链接必须限定在 <c>&lt;h2&gt;</c> 内且严格 <c>owner/repo</c> 两段——取 article 里第一个 <c>&lt;a&gt;</c>
///    会被 stargazers / forks 等辅助链接命中，整条被跳过（现象是"爬取不全"）或误捕多段路径（fullName 与 url 错）；
/// ② 星数在数字前常夹一截 <c>&lt;svg&gt;</c> 图标 ⇒ 必须先剥标签再取数字；
/// ③ 描述只认 <c>col-9</c> 那一段，<b>不回退</b>到任意 <c>&lt;p&gt;</c>——否则内置 Star 按钮的文案会混进描述。
/// </para>
/// <para>
/// 用正则而不是本项目惯用的手写扫描：这套模式在扩展里是已经跑通的既有事实，
/// 逐字符重写一遍等于把上面三条修复重新赌一次。
/// </para>
/// </summary>
public static class TrendingHtmlParser
{
    private const string GithubHost = "https://github.com/";

    private static readonly Regex ArticleBoundary = New(@"<article\b");
    private static readonly Regex RepoLink = New(@"<h2[^>]*>\s*<a\s[^>]*href=""/([\w.\-]+/[\w.\-]+)""[^>]*>");
    private static readonly Regex Description = New(@"<p[^>]*col-9[^>]*>([\s\S]*?)</p>");
    private static readonly Regex Language = New(@"itemprop=""programmingLanguage""\s*>\s*([^<]+?)\s*<");
    private static readonly Regex StarLinkText = New(@"href=""[^""]*stargazers""[^>]*>([\s\S]*?)</a>");
    private static readonly Regex PeriodStars = New(@"([\d,]+)\s+stars?\s+(?:today|this week|this month)");
    private static readonly Regex Tags = New(@"<[^>]+>");
    private static readonly Regex NonDigits = New(@"[^\d,]");
    private static readonly Regex HexRef = New(@"&#x([0-9a-f]+);", RegexOptions.IgnoreCase);
    private static readonly Regex DecRef = New(@"&#(\d+);");

    /// <summary>解析 trending 页。零条目＝页面改版或被反爬拦截（返回验证页），一律按"解析失败"处理，交由调用方兜底。</summary>
    public static IReadOnlyList<TrendingRepo> Parse(string? html)
    {
        var result = new List<TrendingRepo>();
        if (string.IsNullOrEmpty(html)) return result;

        foreach (var piece in ArticleBoundary.Split(html).Skip(1))
        {
            var close = piece.IndexOf("</article>", StringComparison.Ordinal);
            if (close < 0) continue;                 // 尾块常被截断，跳过而不是当成一条
            var chunk = piece.Substring(0, close);

            var repo = RepoLink.Match(chunk);
            if (!repo.Success) continue;             // 认不出主链接就整条丢弃：宁可少一条也不要一条错的
            var fullName = repo.Groups[1].Value.TrimEnd('/');

            var stars = ToLong(NonDigits.Replace(
                StarLinkText.Match(chunk) is { Success: true } s ? StripTags(s.Groups[1].Value) : string.Empty,
                string.Empty));
            var periodStars = PeriodStars.Match(chunk);

            result.Add(new TrendingRepo(
                fullName,
                GithubHost + fullName,
                Description.Match(chunk) is { Success: true } d ? Clean(DecodeEntities(StripTags(d.Groups[1].Value))) : string.Empty,
                Language.Match(chunk) is { Success: true } l ? l.Groups[1].Value.Trim() : null,
                stars,
                periodStars.Success ? ToLong(periodStars.Groups[1].Value) : null));
        }
        return result;
    }

    /// <summary>兜底 Search API 的响应形状 → 候选。它<b>给不出本期新增星数</b>，故 StarsToday 一律 null（界面据此降级显示）。</summary>
    public static IReadOnlyList<TrendingRepo> FromSearchApi(IEnumerable<SearchApiRepo> repos)
    {
        var list = new List<TrendingRepo>();
        foreach (var r in repos)
        {
            if (string.IsNullOrWhiteSpace(r.FullName)) continue;
            list.Add(new TrendingRepo(
                r.FullName.Trim(),
                string.IsNullOrWhiteSpace(r.HtmlUrl) ? GithubHost + r.FullName.Trim() : r.HtmlUrl,
                r.Description ?? string.Empty,
                r.Language,
                r.Stars < 0 ? 0 : r.Stars));
        }
        return list;
    }

    /// <summary>兜底查询串（与扩展同口径：新建时间窗口 + 语言 + stars:&gt;50，按星数降序取 25 条）。</summary>
    public static string BuildSearchQuery(TrendingPeriod period, string? language, DateTimeOffset nowUtc)
    {
        var since = nowUtc.AddDays(-TrendingPeriods.FallbackDays(period)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var q = "created:>" + since
              + (string.IsNullOrWhiteSpace(language) ? string.Empty : " language:" + language.Trim())
              + " stars:>50";
        return "q=" + Uri.EscapeDataString(q)
             + "&sort=stars&order=desc&per_page=25";
    }

    /// <summary>热榜页 URL：<c>/trending/{语言小写}?since={周期}</c>（无语言时不带路径段）。</summary>
    public static string BuildTrendingUrl(TrendingPeriod period, string? language)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? string.Empty : "/" + Uri.EscapeDataString(language.Trim().ToLowerInvariant());
        return "https://github.com/trending" + lang + "?since=" + TrendingPeriods.Code(period);
    }

    /// <summary>缓存 / 请求共用的键：周期 + 语言小写（语言大小写不同不该产生两份缓存）。</summary>
    public static string CacheKeyOf(TrendingPeriod period, string? language)
        => TrendingPeriods.Code(period) + "|" + (language ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>页面上的星数带千分位逗号（"12,345"），必须先去掉再解析——否则 TryParse 直接失败变 0。</summary>
    private static long ToLong(string digits)
        => long.TryParse(digits.Replace(",", string.Empty), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static string StripTags(string s) => Tags.Replace(s, string.Empty);

    private static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        var space = false;
        foreach (var c in s)
        {
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string DecodeEntities(string s)
    {
        if (s.IndexOf('&') < 0) return s;
        // 顺序与扩展一致：先换具名实体，再解数值字符引用
        var outS = s.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                    .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&apos;", "'")
                    .Replace("&nbsp;", " ");
        outS = HexRef.Replace(outS, m => CodePoint(m.Groups[1].Value, fromHex: true));
        outS = DecRef.Replace(outS, m => CodePoint(m.Groups[1].Value, fromHex: false));
        return outS;
    }

    /// <summary>数值字符引用解不出来时给空串，不整段丢弃（外部 HTML 里出现非法码位是常态而非异常）。</summary>
    private static string CodePoint(string digits, bool fromHex)
    {
        if (!int.TryParse(digits, fromHex ? NumberStyles.HexNumber : NumberStyles.None,
                          CultureInfo.InvariantCulture, out var value)) return string.Empty;
        try { return char.ConvertFromUtf32(value); }
        catch { return string.Empty; }
    }

    private static Regex New(string pattern, RegexOptions extra = RegexOptions.None)
        => new(pattern, extra | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}

/// <summary>兜底 Search API 里我们需要的字段（放在 Core 以免解析层依赖 HTTP 类型）。</summary>
public sealed record SearchApiRepo(string FullName, string HtmlUrl, string? Description, string? Language, long Stars);
