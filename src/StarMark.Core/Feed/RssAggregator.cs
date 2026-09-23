#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Feed;
using StarMark.Integrations.Feed;

namespace StarMark.Core.Feed;

/// <summary>一个源这一轮的结果。<b>失败与"没有新内容"都必须能单独回报</b>：
/// 用户在设置页看到的应该是"哪几个源坏了、坏在哪"，而不是整页空白。</summary>
/// <param name="Source">是哪个源（原样带回来，界面上要能对上号）。</param>
/// <param name="Entries">这个源给出的条目（失败时为空）。</param>
/// <param name="Error">失败原因；null＝这一轮成功。</param>
/// <param name="NotModified">源回了 304（没变化）。这是好消息，不能算失败。</param>
public sealed record RssSourceOutcome(
    RssSourceConfig Source,
    IReadOnlyList<RssEntry> Entries,
    string? Error = null,
    bool NotModified = false)
{
    public bool Ok => Error is null;
}

/// <summary>
/// 一轮抓取的整体结果。
/// </summary>
/// <param name="Outcomes">每个启用的源一条，<b>顺序与配置顺序一致</b>（界面上源不该每次刷新就换位置）。</param>
/// <param name="Entries">所有条目按时间倒序摊平后的结果（无时间的排最后）。</param>
/// <param name="FailedCount">失败的源数。为 0 时界面不必显示"部分源失败"的提示。</param>
public sealed record RssRunResult(
    IReadOnlyList<RssSourceOutcome> Outcomes,
    IReadOnlyList<RssEntry> Entries,
    int FailedCount);

/// <summary>
/// 把"抓 → 解析 → 汇总"这条编排跑一遍。<b>本类不碰网络也不解析 XML</b>：
/// 抓取由调用方以委托注入（界面给 <see cref="RssClient"/>，单测给假数据），
/// 解析交给 <see cref="RssParser"/>。这样"一个源坏了会不会拖垮整页"这类判断才是可测的。
/// </summary>
public static class RssAggregator
{
    /// <summary>摊平后最多给多少条：源一多，全塞进设置页那一块列表会把真正新的挤出视野。</summary>
    public const int MaxTotalEntries = 200;

    public static async Task<RssRunResult> RunAsync(
        IReadOnlyList<RssSourceConfig> sources,
        Func<RssSourceConfig, Task<RssFetchResult>> fetch,
        CancellationToken ct = default)
    {
        var outcomes = new List<RssSourceOutcome>(sources.Count);
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            outcomes.Add(await OneAsync(source, fetch, ct));
        }

        var entries = outcomes.Where(o => o.Ok)
            .SelectMany(o => o.Entries)
            // 时间倒序；没有时间的排最后——**不给它编一个"现在"**，那样它会冒充最新的一条
            .OrderByDescending(e => e.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenBy(e => e.SourceId)
            .Take(MaxTotalEntries)
            .ToList();

        return new RssRunResult(outcomes, entries, outcomes.Count(o => !o.Ok));
    }

    /// <summary>
    /// 单个源：任何异常都收在这里，<b>绝不让它冒到循环外</b>。
    /// 一个源超时/断连/返回半截 XML 是常态（订阅服务比本地程序脆弱得多），
    /// 而"因为第三个源坏了所以整页没内容"是用户最没法理解的那种失败。
    /// </summary>
    private static async Task<RssSourceOutcome> OneAsync(
        RssSourceConfig source, Func<RssSourceConfig, Task<RssFetchResult>> fetch, CancellationToken ct)
    {
        if (!source.Enabled) return new RssSourceOutcome(source, Array.Empty<RssEntry>());
        if (RssSourceConfig.UrlProblem(source.Url) is { } bad)
            return new RssSourceOutcome(source, Array.Empty<RssEntry>(), bad);

        try
        {
            var fetched = await fetch(source);
            if (fetched.Status == RssFetchStatus.NotModified)
                return new RssSourceOutcome(source, Array.Empty<RssEntry>(), NotModified: true);
            if (!fetched.Ok)
                return new RssSourceOutcome(source, Array.Empty<RssEntry>(), fetched.Error ?? "抓取没有成功，也没有给出原因");

            var parsed = RssParser.Parse(fetched.Body, source);
            return parsed.Ok
                ? new RssSourceOutcome(source, parsed.Entries)
                : new RssSourceOutcome(source, Array.Empty<RssEntry>(), parsed.Error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;      // 用户按了取消：这是控制流，不是一个源的失败
        }
        catch (Exception ex)
        {
            return new RssSourceOutcome(source, Array.Empty<RssEntry>(), "这个源抓取时异常：" + ex.Message);
        }
    }
}
