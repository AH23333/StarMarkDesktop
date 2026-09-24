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
/// <param name="Stopped">这一条<b>根本没抓</b>：用户按了停止（或整轮到了时限）。它既不是"坏了"也不是"空的"，
/// 混进任何一边都会让人去改一个其实没问题的地址。</param>
/// <param name="Etag">源这一轮给的强/弱校验符（没重发就沿用带进去的那个），交调用方落盘进缓存。</param>
/// <param name="LastModified">同上，Last-Modified 版。</param>
public sealed record RssSourceOutcome(
    RssSourceConfig Source,
    IReadOnlyList<RssEntry> Entries,
    string? Error = null,
    bool NotModified = false,
    bool Stopped = false,
    string? Etag = null,
    string? LastModified = null)
{
    public bool Ok => Error is null && !Stopped;
}

/// <summary>
/// 一轮抓取的整体结果。
/// </summary>
/// <param name="Outcomes">每个启用的源一条，<b>顺序与配置顺序一致</b>（界面上源不该每次刷新就换位置）。</param>
/// <param name="Entries">所有条目按时间倒序摊平后的结果（无时间的排最后）。</param>
/// <param name="FailedCount">失败的源数。为 0 时界面不必显示"部分源失败"的提示。<b>不含被停掉的那些</b>。</param>
/// <param name="StoppedCount">因为停止/时限而没抓的源数。界面要用它说"还剩几条没抓"，
/// 而不是让用户自己数列表里哪几行是空的。</param>
public sealed record RssRunResult(
    IReadOnlyList<RssSourceOutcome> Outcomes,
    IReadOnlyList<RssEntry> Entries,
    int FailedCount,
    int StoppedCount = 0);

/// <summary>
/// 把"抓 → 解析 → 汇总"这条编排跑一遍。<b>本类不碰网络也不解析 XML</b>：
/// 抓取由调用方以委托注入（界面给 <see cref="RssClient"/>，单测给假数据），
/// 解析交给 <see cref="RssParser"/>。这样"一个源坏了会不会拖垮整页"这类判断才是可测的。
/// </summary>
public static class RssAggregator
{
    /// <summary>摊平后最多给多少条：源一多，全塞进设置页那一块列表会把真正新的挤出视野。</summary>
    public const int MaxTotalEntries = 200;

    /// <summary>
    /// 一轮里<b>同时</b>在飞的源数上限。
    /// <para>真机反馈"每次刷新极为缓慢"的主因就在这儿：原先逐源串行，一个连不上的源配上 20 秒超时，
    /// 就能把它后面所有源都按住——六个源里坏两个就是半分钟以上。四个一并跑，坏源只挡住自己那一路。</para>
    /// <para>刻意不是"来多少开多少"：订阅地址是第三方的站点，几十路并发等于对人家做一次小范围压测，
    /// 而且本机 HttpClient 的连接池也会被占满。四路是"感觉不到排队"与"不把对方打疼"之间的一档。</para>
    /// </summary>
    public const int MaxParallel = 4;

    public static async Task<RssRunResult> RunAsync(
        IReadOnlyList<RssSourceConfig> sources,
        Func<RssSourceConfig, Task<RssFetchResult>> fetch,
        CancellationToken ct = default,
        int maxParallel = MaxParallel)
    {
        // 结果按**下标**回填，不按完成顺序 append：界面拿到的顺序必须与配置顺序一致，
        // 否则"哪个源排第几"每次刷新都在变，用户点第二行点的就不是刚才那一行。
        var outcomes = new RssSourceOutcome[sources.Count];
        var cursor = -1;
        var workers = Enumerable.Range(0, Math.Max(1, Math.Min(maxParallel, sources.Count)))
            .Select(async _ =>
            {
                while (true)
                {
                    var index = Interlocked.Increment(ref cursor);
                    if (index >= sources.Count) return;
                    if (ct.IsCancellationRequested)
                    {
                        // 停止之后剩下的源逐条记为"没抓"：整轮结果照样返回，<b>已抓到的不能因为按了停止而丢掉</b>
                        outcomes[index] = new RssSourceOutcome(sources[index], Array.Empty<RssEntry>(), Stopped: true);
                        continue;
                    }
                    outcomes[index] = await OneAsync(sources[index], fetch, ct);
                }
            });
        await Task.WhenAll(workers);

        var list = outcomes.ToList();
        var entries = list.Where(o => o.Ok)
            .SelectMany(o => o.Entries)
            // 时间倒序；没有时间的排最后——**不给它编一个"现在"**，那样它会冒充最新的一条
            .OrderByDescending(e => e.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenBy(e => e.SourceId)
            .Take(MaxTotalEntries)
            .ToList();

        return new RssRunResult(
            list,
            entries,
            list.Count(o => !o.Ok && !o.Stopped),
            list.Count(o => o.Stopped));
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
                // 304 也要把校验符带回去（RssClient 在 304 时原样回带），否则调用方一"沿用旧值"就丢了它
                return new RssSourceOutcome(source, Array.Empty<RssEntry>(),
                    NotModified: true, Etag: fetched.ETag, LastModified: fetched.LastModified);
            if (!fetched.Ok)
                return new RssSourceOutcome(source, Array.Empty<RssEntry>(), fetched.Error ?? "抓取没有成功，也没有给出原因");

            var parsed = RssParser.Parse(fetched.Body, source);
            return parsed.Ok
                ? new RssSourceOutcome(source, parsed.Entries, Etag: fetched.ETag, LastModified: fetched.LastModified)
                : new RssSourceOutcome(source, Array.Empty<RssEntry>(), parsed.Error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 抓取途中被叫停：这一条记为"没抓"，而<b>不往上抛</b>——抛出去就把前面几个源已经抓到的结果一起扔了，
            // 于是"停止"变成了"撤销整轮"，用户只能再等一遍。
            return new RssSourceOutcome(source, Array.Empty<RssEntry>(), Stopped: true);
        }
        catch (Exception ex)
        {
            return new RssSourceOutcome(source, Array.Empty<RssEntry>(), "这个源抓取时异常：" + ex.Message);
        }
    }
}
