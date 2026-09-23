#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StarMark.Abstractions.Trending;

/// <summary>这一批候选是从哪条腿来的——界面要据此标注来源，解析失败兜底不是"免费的成功"。</summary>
public enum TrendingSource
{
    /// <summary>GitHub 热榜页（有"本期新增星数"）。</summary>
    TrendingHtml,
    /// <summary>Search API 兜底（<b>没有</b>"本期新增星数"，界面必须降级显示而不是画 +0）。</summary>
    SearchApi,
}

/// <summary>来源的持久化/展示代码串。缓存里存的是它，不是枚举名小写——枚举改名会悄悄作废旧缓存。</summary>
public static class TrendingSources
{
    public static string Code(this TrendingSource via) => via switch
    {
        TrendingSource.TrendingHtml => "trending-html",
        _ => "search-api",
    };

    /// <summary>解析不出就给 null（＝来源未知），不要默认成某一条腿：标注错了等于撒谎。</summary>
    public static TrendingSource? ParseCode(string? code) => code switch
    {
        "trending-html" => TrendingSource.TrendingHtml,
        "search-api" => TrendingSource.SearchApi,
        _ => null,
    };
}

/// <param name="Notice">这一批为什么走了兜底（页面 HTTP 码 / 解析为 0 条 / 响应过大…）。界面标注用，可为 null。</param>
public sealed record TrendingFetchResult(IReadOnlyList<TrendingRepo> Repos, TrendingSource Via, string? Notice = null);

/// <summary>两条腿都拿不到结果时的错误（消息即给人看的原因，界面直接贴出来）。</summary>
public sealed class TrendingException : Exception
{
    public TrendingException(string message) : base(message) { }
}

/// <summary>
/// 热榜抓取缝（实现在 <c>StarMark.Integrations.Trending.TrendingFetcher</c>）。
/// <para>放在 Abstractions 是因为依赖方向：<c>Core → Integrations</c>，编排服务在 Core，
/// 它只能看见 Abstractions 里的类型；抓取实现留在 Integrations（外网访问属适配层）。</para>
/// </summary>
public interface ITrendingSource
{
    Task<TrendingFetchResult> FetchAsync(TrendingPeriod period, string? language, CancellationToken ct);
}
