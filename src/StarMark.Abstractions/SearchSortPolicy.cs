#nullable enable

namespace StarMark.Abstractions;

/// <summary>
/// 关键词搜索的<b>排序归属</b>判据：哪些 <see cref="SearchFilter.Sort"/> 值算「按 FTS 相关度优先」。
/// <para>
/// 这条判据决定 SQL 走哪条腿（P-33）。只有相关度那条腿可以让 <c>LIMIT</c> 在 CTE 里按 bm25 先截断——
/// 截断与最终排序是同一个次序，所以"取前 N"是诚实的。用户改成按名字/时间/Star 数排序后，
/// 截断必须落在<b>过滤与排序之后</b>：否则前排那些相关度命中会占住名额，外层再按用户选的次序重排
/// 幸存的那一小撮，结果是"该出现的根本没出现、出现的也不是你要的那个顺序"，而界面看起来只是"排序没生效"。
/// </para>
/// <para>
/// null 与空串也归相关度：<see cref="SearchFilter.Sort"/> 是可空字符串，老调用方与测试桩都不填，
/// 把它们算进默认腿才不会让没显式传排序的调用突然换一条 SQL 路径。
/// </para>
/// </summary>
public static class SearchSortPolicy
{
    /// <summary>相关度优先的那条腿（唯一可以在 CTE 内截断的排序）。</summary>
    public const string Relevance = "relevance";

    /// <summary>
    /// 该排序值是否属于「相关度优先」这一档。判据只有这一处：SQL 的两条腿、UI 的下拉、测试的夹具都问它，
    /// 免得出现"UI 认为这是默认而 SQL 认为不是"这种两边各自判档的分岔。
    /// </summary>
    public static bool IsRelevanceFirst(string? sort)
        => string.IsNullOrEmpty(sort) || sort.Equals(Relevance, StringComparison.OrdinalIgnoreCase);
}
