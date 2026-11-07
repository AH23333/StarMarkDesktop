#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Ai;

namespace StarMark.Abstractions;

/// <summary>一条用量记录：谁花的、多少 token、数字是哪来的（原数还是折算）。</summary>
/// <param name="Model">配置里的模型名；没填则 null——<b>记"当时实际用的配置"而不是猜</b>，
/// 空着就空着，比编一个"未知模型"诚实。</param>
public sealed record AiUsageEntry(
    DateTimeOffset At,
    string Feature,
    string Provider,
    string? Model,
    AiUsage Usage);

/// <summary>一段窗口内的用量汇总（设置页面板与预算闸门的共同数据源——<b>判额和展示读同一份，
/// 两处各算一个数就是"显示没超但被熔断"的诞生方式</b>）。</summary>
public sealed record AiUsageTotals(int Calls, long InputTokens, long OutputTokens, int EstimatedCalls)
{
    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>窗口内某一项（功能名或模型名）的分布。</summary>
public sealed record AiUsageSlice(string Name, int Calls, long TotalTokens, bool EstimatedOnly);

/// <summary>一个本地日历日的用量（P-294 仪表盘按天趋势的最小格）。Day 是 <c>yyyy-MM-dd</c> 本地日字符串。</summary>
public sealed record AiUsageDay(string Day, int Calls, long TotalTokens);

/// <summary>面板一次读齐的快照（合计 + 两个分布。<b>三读取自同一窗口口径</b>——分开读会出现
/// "合计与分布加起来对不上"的显示鬼态，因为两读之间又写进去了一行）。</summary>
public sealed record AiUsageSnapshot(
    long? MaxRecordedAt,
    AiUsageTotals Window,
    System.Collections.Generic.IReadOnlyList<AiUsageSlice> ByFeature,
    System.Collections.Generic.IReadOnlyList<AiUsageSlice> ByModel);

/// <summary>AI 仪表盘（P-294）一次读齐的快照：全部合计＋最近明细＋按天趋势。
/// <para>与 <see cref="AiUsageSnapshot"/> 同一份账本、另一只读面：预算栏判额看窗口，
/// 仪表盘看全期与明细——<b>两者⛔ 各自另算第二套数</b>。</para></summary>
public sealed record AiDashboardSnapshot(
    DateTimeOffset? Latest,
    AiUsageTotals AllTime,
    System.Collections.Generic.IReadOnlyList<AiUsageEntry> Recent,
    System.Collections.Generic.IReadOnlyList<AiUsageDay> Days);

/// <summary>
/// 按天趋势的纯函数面（P-294）：把仓储 GROUP BY 出来的散日补成<b>连续</b>的日列。
/// <b>缺的天补零、窗外的旧账不并进来</b>——趋势图上"这周烧了多少"要的是每个格子都在，
/// 空格与"零消耗"在图上是两种不同的诚实。
/// </summary>
public static class AiUsageTrend
{
    /// <summary>以 <paramref name="anchor"/> 的本地日为最后一天，往前铺满 <paramref name="dayCount"/> 个连续本地日；
    /// <paramref name="raw"/> 里落在窗内的日子带原值，窗外的丢弃，缺的补零。升序返回。</summary>
    public static System.Collections.Generic.IReadOnlyList<AiUsageDay> LastDays(
        System.Collections.Generic.IReadOnlyList<AiUsageDay> raw, DateTimeOffset anchor, int dayCount)
    {
        if (dayCount <= 0) return System.Array.Empty<AiUsageDay>();
        var byDay = new System.Collections.Generic.Dictionary<string, AiUsageDay>(System.StringComparer.Ordinal);
        foreach (var day in raw) byDay[day.Day] = day;   // 同名后写胜：仓储侧 GROUP BY 后本无重名，防御一句
        var end = anchor.LocalDateTime.Date;
        var days = new System.Collections.Generic.List<AiUsageDay>(dayCount);
        for (var i = dayCount - 1; i >= 0; i--)
        {
            var key = end.AddDays(-i).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            days.Add(byDay.TryGetValue(key, out var found) ? found : new AiUsageDay(key, 0, 0));
        }
        return days;
    }
}

/// <summary>
/// AI 用量计量的读写面（§20.1）。<b>写只有一处</b>：每次真正发出并获得答复的调用结束后记一行；
/// "配置能连但还没干活"（测试连接）不记——那是探测不是消耗。
/// 月度窗口不以墙钟为基准而以 <see cref="MaxRecordedAtAsync"/> 为准（§20.5：改系统时间绕预算）。
/// </summary>
public interface IAiUsageRepository
{
    Task LogAsync(AiUsageEntry entry, CancellationToken ct = default);

    /// <summary>聚合 at ≥ 指定时刻的全部记录。<paramref name="fromUnixSec"/> 由调用方定窗口。</summary>
    Task<AiUsageTotals> TotalsSinceAsync(long fromUnixSec, CancellationToken ct = default);

    /// <summary>窗口内按功能分组（feature 列）。</summary>
    Task<IReadOnlyList<AiUsageSlice>> ByFeatureSinceAsync(long fromUnixSec, CancellationToken ct = default);

    /// <summary>窗口内按模型分组（model 列；没记模型的行归入 <paramref name="Name"/> 为 null 之外？——不，
    /// 归入空名切片，由界面显示成「未记模型」。<b>分组不能悄悄丢行</b>：丢了分布就对不上总数）。</summary>
    Task<IReadOnlyList<AiUsageSlice>> ByModelSinceAsync(long fromUnixSec, CancellationToken ct = default);

    /// <summary>表内最大 at（Unix 秒；空表返回 null）。月度窗口、熔断展示都以它为"现在"。</summary>
    Task<long?> MaxRecordedAtAsync(CancellationToken ct = default);

    /// <summary>最近若干条记录（新→旧）。P-294 仪表盘的「最近请求明细」那一格。</summary>
    Task<System.Collections.Generic.IReadOnlyList<AiUsageEntry>> RecentAsync(int take, CancellationToken ct = default);

    /// <summary>按本地日历日分组（P-294 按天趋势）。只回<b>有账的日子</b>，铺满日历由 <see cref="AiUsageTrend"/> 做。</summary>
    Task<System.Collections.Generic.IReadOnlyList<AiUsageDay>> ByDaySinceAsync(long fromUnixSec, CancellationToken ct = default);
}
