#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.I18n;

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

/// <param name="Notice">这一批为什么走了兜底（页面 HTTP 码 / 解析为 0 条 / 响应过大…）。界面标注用，可为 null。
/// 交的是<b>键＋实参</b>而⛔ 已经取好词的那一句：这一批可能在中文档抓、在英文档贴（批次 WI-57 给诊断那一族
/// 立的同一条判据＝坑表 #393／#407），取词必须发生在贴上去那一刻。</param>
public sealed record TrendingFetchResult(IReadOnlyList<TrendingRepo> Repos, TrendingSource Via, LocMessage? Notice = null);

/// <summary>两条腿都拿不到结果时的错误（<b>带的是那颗键</b>，界面念的是 <see cref="Reason"/>）。</summary>
public sealed class TrendingException : Exception
{
    public TrendingException(LocMessage reason) : base(reason.Render()) => Reason = reason;

    /// <summary>给人看的那句的<b>身份</b>（键＋实参，还没取词）。宿主按此刻语言渲染它，⛔ 读 <see cref="Exception.Message"/>。</summary>
    public LocMessage Reason { get; }
}

/// <summary>
/// 「抓取这一路失败了，界面上该念哪一句」——<b>只许一处决定</b>（主窗热榜页、今日速览那一块、
/// Core 的 stale 那一路都问它，⛔ 各自拼一份＝P-171 那一族）。
/// <para>两条腿都失败的 <see cref="TrendingException"/> 已经带着骨架（含两条腿各自的原因），直接交出它那颗键；
/// 其它异常（网络栈、取消之外的 IO…）没有骨架可用，就套进「抓取失败：{原因}」那颗已有行。</para>
/// </summary>
public static class TrendingFailure
{
    public static LocMessage Of(Exception ex) =>
        ex is TrendingException trended ? trended.Reason : LocMessage.Of("trending.emptyFailed", ex.Message);
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
