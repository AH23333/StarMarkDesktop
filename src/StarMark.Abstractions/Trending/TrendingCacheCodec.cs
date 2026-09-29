#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace StarMark.Abstractions.Trending;

/// <summary>一条已缓存的热榜结果（含抓取时刻，单位 Unix 秒——本仓所有持久化时间戳都是秒，不是毫秒）。</summary>
public sealed record TrendingCachedEntry(IReadOnlyList<TrendingRepo> Repos, long FetchedAt, string Via)
{
    /// <summary>缓存装不下/损坏时的判据：没有可用条目就不能当成"抓到了空热榜"。</summary>
    public bool HasContent => Repos.Count > 0;
}

/// <summary>
/// 热榜缓存的编解码 + "同一本地日历日"判定（纯函数）。
/// <para>
/// 缓存值放在现成的 <c>sync_state</c> 键里（不新增表、不做 schema 迁移）。这里刻意只管"字符串 ⇄ 条目"，
/// 读写仓储交给编排层：这样 JSON 形状与日历日判定这两个最容易出静默错的点都能单测。
/// </para>
/// </summary>
public static class TrendingCacheCodec
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>缓存键前缀：<c>sync_state</c> 是全局共享的键空间，必须带命名空间避免与同步检查点撞名。</summary>
    public const string KeyPrefix = "trending:";

    public static string CacheStateKey(TrendingPeriod period, string? language)
        => KeyPrefix + TrendingHtmlParser.CacheKeyOf(period, language);

    public static string Encode(TrendingCachedEntry entry) => JsonSerializer.Serialize(entry, Opts);

    /// <summary>
    /// 解码失败/形状不对一律返回 null（＝"没有缓存"），<b>不抛</b>：
    /// 缓存是可重抓的临时数据，坏一次不该让页面打不开，更不该被当成"今天没有热榜"。
    /// </summary>
    public static TrendingCachedEntry? Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var e = JsonSerializer.Deserialize<TrendingCachedEntry>(json, Opts);
            return e is null || e.Repos is null ? null : e with { FetchedAt = Math.Max(0, e.FetchedAt) };
        }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }   // 旧版本留下的完全不同形状
    }

    /// <summary>
    /// 是否同一个<b>本地</b>日历日（"每天首次打开才抓一次"的判据）。
    /// <para>偏移量作参数传入而不是内部取 <see cref="TimeZoneInfo.Local"/>：否则测试就只能在特定时区成立，
    /// 而"跨日"这件事恰恰是最容易被时区/夏令时带偏的（同一 UTC 瞬间在 +08:00 与 -05:00 下不是同一天）。</para>
    /// </summary>
    public static bool IsSameLocalDay(long aUnixSeconds, long bUnixSeconds, TimeSpan offset)
        => LocalDate(aUnixSeconds, offset) == LocalDate(bUnixSeconds, offset);

    private static DateTime LocalDate(long unixSeconds, TimeSpan offset)
        => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToOffset(offset).Date;

    /// <summary>给界面的一句话："上次更新于 …"。缓存缺失或时刻非法（≤0）时返回空串，不显示"1970/1/1"。</summary>
    public static string DescribeAge(long fetchedAtUnixSeconds, DateTimeOffset now)
    {
        if (fetchedAtUnixSeconds <= 0) return string.Empty;
        var span = now - DateTimeOffset.FromUnixTimeSeconds(fetchedAtUnixSeconds);
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;      // 机器时钟被往回调过时不显示"-3 小时"
        if (span < TimeSpan.FromMinutes(2)) return "刚刚";
        if (span < TimeSpan.FromHours(1))
            return $"{(int)span.TotalMinutes} 分钟前";
        if (span < TimeSpan.FromDays(1))
            return $"{NumberText.Grouped(span.TotalHours)} 小时前";
        return $"{(int)span.TotalDays} 天前";
    }
}
