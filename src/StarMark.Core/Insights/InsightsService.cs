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

        // 1. 疑似重复（按归一化标题分组）
        var dupList = items
            .GroupBy(i => NormalizeTitle(i.Title))
            .Where(g => g.Count() >= 2)
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
                    Detail = $"{untagged}/{total}（{ratio:P0}）的条目没有标签",
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
                    Detail = $"{stale}/{total}（{ratio:P0}）超过 {StaleDays} 天未更新",
                });
            }
        }

        var score = Math.Max(0, 100 - deduction);

        // 语言分布 Top8
        var languageTop = items
            .Select(ExtractLanguage)
            .Where(l => !string.IsNullOrEmpty(l))
            .GroupBy(l => l!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LanguageStat { Language = g.Key, Count = g.Count() })
            .OrderByDescending(s => s.Count)
            .Take(8)
            .ToList();

        // 近 N 天新增趋势：按 anchor 日期分桶
        var today = clock().LocalDateTime.Date;
        var trend = new List<DateCount>(days);
        var trendIndex = new Dictionary<string, int>(days);
        for (var k = days - 1; k >= 0; k--)
        {
            var date = today.AddDays(-k).ToString("MM-dd", CultureInfo.InvariantCulture);
            trendIndex[date] = trend.Count;
            trend.Add(new DateCount { Date = date, Count = 0 });
        }
        foreach (var item in items)
        {
            var key = AnchorDate(item, clock).ToString("MM-dd", CultureInfo.InvariantCulture);
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

    private static string? ExtractLanguage(Item item)
    {
        if (item.Type != ItemType.GitHubStar || string.IsNullOrEmpty(item.ExtraJson)) return null;
        try
        {
            var meta = JsonSerializer.Deserialize<GitHubStarMeta>(item.ExtraJson);
            return string.IsNullOrEmpty(meta?.Language) ? null : meta.Language;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>新增归属日期：GitHubStar 用 starredAt（否则 createdAt），其它类型用 createdAt。</summary>
    private static DateTime AnchorDate(Item item, Func<DateTimeOffset> clock)
    {
        long anchorSec;
        if (item.Type == ItemType.GitHubStar && !string.IsNullOrEmpty(item.ExtraJson))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<GitHubStarMeta>(item.ExtraJson);
                anchorSec = meta?.StarredAt ?? item.CreatedAt;
            }
            catch (JsonException) { anchorSec = item.CreatedAt; }
        }
        else
        {
            anchorSec = item.CreatedAt;
        }
        return DateTimeOffset.FromUnixTimeSeconds(anchorSec).LocalDateTime.Date;
    }
}
