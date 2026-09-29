#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Abstractions.Insights;

namespace StarMark.Core.Insights;

/// <summary>
/// 收藏健康度分析（P2-6）。纯函数：输入 <see cref="Item"/> 列表，输出 <see cref="HealthReport"/>。
/// 算法移植自浏览器扩展 <c>insights.ts</c> 的 <c>buildHealthReport</c>：扣分制，起点 100。
///   - 疑似重复：按归一化标题分组，size &gt;= 2 成组，扣 min(40, 组数 × 10)
///   - 未打标签：untagged/total &gt; 0.2 才罚，扣 min(25, floor(ratio × 50))
///   - 长期未整理：now - updatedAt &gt; 180 天 且 staleRatio &gt; 0.4 才罚，扣 min(15, floor(ratio × 30))
/// 另输出语言分布 Top8、近 N 天新增趋势、独立域名数、标签直方图、重复项 Top10。
/// </summary>
public static class InsightsService
{
    private const int StaleDays = 180;
    private const long StaleSeconds = (long)StaleDays * 86400;

    public static HealthReport BuildHealthReport(
        IReadOnlyList<Item> items,
        int days = 14,
        Func<DateTimeOffset>? now = null)
    {
        var clock = now ?? (() => DateTimeOffset.UtcNow);
        var total = items.Count;

        var factors = new List<HealthFactor>();
        var deduction = 0;

        // 1. 疑似重复（按归一化标题 + 目标地址分组）
        var dupList = items
            .GroupBy(DupGroupKey)
            // 空键 = 标题为空或纯标点/符号，归一后塌成 ""；这类"无有效标题"的条目彼此本就不该算重复，
            // 否则多条无名/纯符号条目会被并成一个幽灵重复组、误扣分。
            .Where(g => g.Count() >= 2 && g.Key.Length > 0)
            .Select(g => new { Key = g.Key, Sample = g.First().Title, Count = g.Count() })
            .ToList();
        if (dupList.Count > 0)
        {
            var d = Math.Min(40, dupList.Count * 10);
            deduction += d;
            var examples = string.Join("、", dupList.OrderByDescending(x => x.Count).Take(5).Select(x => x.Sample));
            factors.Add(new HealthFactor
            {
                Key = "duplicates",
                Label = "疑似重复条目",
                Deduction = d,
                Detail = $"{dupList.Count} 组疑似重复（如：{examples}）",
            });
        }

        // 2. 未打标签
        if (total > 0)
        {
            var untagged = items.Count(i => i.Tags.Count == 0);
            var ratio = (double)untagged / total;
            if (ratio > 0.2)
            {
                var d = Math.Min(25, (int)Math.Floor(ratio * 50));
                deduction += d;
                factors.Add(new HealthFactor
                {
                    Key = "untagged",
                    Label = "未打标签比例偏高",
                    Deduction = d,
                    Detail = $"{untagged}/{total}（{NumberText.Percent(ratio)}）的条目没有标签",
                });
            }
        }

        // 3. 长期未整理
        if (total > 0)
        {
            var nowSec = clock().ToUnixTimeSeconds();
            var stale = items.Count(i => nowSec - i.UpdatedAt > StaleSeconds);
            var ratio = (double)stale / total;
            if (ratio > 0.4)
            {
                var d = Math.Min(15, (int)Math.Floor(ratio * 30));
                deduction += d;
                factors.Add(new HealthFactor
                {
                    Key = "stale",
                    Label = "大量条目长期未整理",
                    Deduction = d,
                    Detail = $"{stale}/{total}（{NumberText.Percent(ratio)}）超过 {StaleDays} 天未更新",
                });
            }
        }

        var score = Math.Max(0, 100 - deduction);

        // 一条条目在一次报告里<b>只解析一次</b> ExtraJson：语言分布与趋势归属过去各解析一遍，
        // 而这里是整表——等于每条都反序列化两遍。解析口径收在 GitHubStarExtra（坏 JSON 与"没有"同义）。
        var parsed = items.Select(item => (item, Meta: GitHubStarExtra.TryRead(item))).ToList();

        // 语言分布 Top8
        var languageTop = parsed
            .Select(p => string.IsNullOrEmpty(p.Meta?.Language) ? null : p.Meta!.Language)
            .Where(l => !string.IsNullOrEmpty(l))
            .GroupBy(l => l!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LanguageStat { Language = g.Key, Count = g.Count() })
            .OrderByDescending(s => s.Count)
            .Take(8)
            .ToList();

        // 近 N 天新增趋势：按 anchor 日期分桶
        // days 可能为负（调用方传入），new List<>/new Dictionary<> 的负 capacity 会抛；先钳到 >=0。
        var span = Math.Max(0, days);
        var today = clock().LocalDateTime.Date;
        var trend = new List<DateCount>(span);
        // 内部键用 yyyy-MM-dd：MM-dd 在 days>365 时会把跨年同日期的两天塌进同一桶；DateCount.Date 仍显示 MM-dd。
        var trendIndex = new Dictionary<string, int>(span);
        for (var k = span - 1; k >= 0; k--)
        {
            var day = today.AddDays(-k);
            var key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            trendIndex[key] = trend.Count;
            trend.Add(new DateCount { Date = day.ToString("MM-dd", CultureInfo.InvariantCulture), Count = 0 });
        }
        foreach (var (item, meta) in parsed)
        {
            var key = AnchorDate(item, meta, clock).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (trendIndex.TryGetValue(key, out var idx))
                trend[idx] = new DateCount { Date = trend[idx].Date, Count = trend[idx].Count + 1 };
        }

        // 独立域名
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (Uri.TryCreate(item.Uri, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
                domains.Add(u.Host);
        }

        // 标签直方图 Top20
        var tagHist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            foreach (var t in item.Tags)
                tagHist[t] = tagHist.TryGetValue(t, out var c) ? c + 1 : 1;
        var tagHistogram = tagHist
            .OrderByDescending(kv => kv.Value)
            .Take(20)
            .Select(kv => new TagStat { Tag = kv.Key, Count = kv.Value })
            .ToList();

        // 重复项 Top10
        var duplicateTop = dupList
            .OrderByDescending(x => x.Count)
            .Take(10)
            .Select(x => new DuplicateGroup { Title = x.Sample, Count = x.Count })
            .ToList();

        return new HealthReport
        {
            Score = score,
            Total = total,
            Factors = factors,
            LanguageTop = languageTop,
            NewTrend = trend,
            DistinctDomains = domains.Count,
            TagHistogram = tagHistogram,
            DuplicateTop = duplicateTop,
        };
    }

    /// <summary>标题归一化：小写、去标点、折叠空白。用于重复检测分组键。</summary>
    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var sb = new StringBuilder(title.Length);
        foreach (var c in title.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(c)) sb.Append(' ');
            else if (char.IsLetterOrDigit(c)) sb.Append(c);
            // 标点/符号直接丢弃
        }
        return string.Join(" ", sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// 重复检测分组键 = 归一化标题 + 目标主机路径。
    /// 仅按标题归一会把"仓库名+语言相同、但 owner 不同"的两个 GitHub Star（标题形如 "{Repo} · {Language}"，
    /// Subtitle=owner/repo、Uri=github.com/owner/repo）误判为同组重复。加上 host+path 后按真实目的地区分；
    /// 而 uri 为空的书签测试夹具仍塌到同一 host+path（空串），行为与只按标题分组一致（无回归）。
    /// 标题归一无意义（空）时返回空键，交由上游过滤（不参与重复判定）。
    /// </summary>
    private static string DupGroupKey(Item item)
    {
        var t = NormalizeTitle(item.Title);
        if (t.Length == 0) return string.Empty;
        return t + "\u241F" + HostPath(item.Uri);
    }

    /// <summary>http(s) URI 归一为小写 "host + path(去尾斜杠)"；非 http(s) 空 uri 返回空串；其它原样小写。</summary>
    private static string HostPath(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            return (u.Host + u.AbsolutePath).TrimEnd('/').ToLowerInvariant();
        return string.IsNullOrEmpty(uri) ? string.Empty : uri.ToLowerInvariant();
    }

    /// <summary>
    /// 新增归属日期：GitHubStar 用 starredAt（否则 createdAt），其它类型用 createdAt。
    /// <paramref name="meta"/> 由调用侧一次解析好传进来（<see cref="GitHubStarExtra.TryRead"/>），
    /// 这里不再碰 JSON——整表统计里每条二次反序列化是白花的钱。
    /// </summary>
    private static DateTime AnchorDate(Item item, GitHubStarMeta? meta, Func<DateTimeOffset> clock)
    {
        var anchorSec = meta?.StarredAt ?? item.CreatedAt;
        // FromUnixTimeSeconds 只接受 [-62135596800, 253402300799]；越界值（毫秒级/荒谬 long，
        // 手改或坏备份可注入）会抛 ArgumentOutOfRangeException，且趋势循环无 try 包裹 → 整份报告崩。
        // 落回条目入库时间；若 created_at 本身也被污染则退到 Unix 纪元，绝不抛出。
        if (!IsRepresentableUnixSec(anchorSec)) anchorSec = item.CreatedAt;
        if (!IsRepresentableUnixSec(anchorSec)) return new DateTime(1970, 1, 1);
        return DateTimeOffset.FromUnixTimeSeconds(anchorSec).LocalDateTime.Date;
    }

    private static bool IsRepresentableUnixSec(long sec) =>
        sec is >= -62_135_596_800L and <= 253_402_300_799L;
}
