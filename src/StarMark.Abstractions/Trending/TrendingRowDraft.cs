#nullable enable
using System.Globalization;

namespace StarMark.Abstractions.Trending;

/// <summary>热榜行的星数文案（纯函数）。唯一但重要的口径：兜底来源给不出"本期新增"，就不能画成 "＋0"。</summary>
public static class TrendingStarsText
{
    /// <summary>
    /// "＋230 本周"。<paramref name="starsToday"/> 为 null（Search API 兜底没有这个字段）⇒ 返回 null 并省略，
    /// <b>不显示 0</b>：把"不知道"画成"没涨"是编数据。兜底这件事由页面顶部一句来源标注说清，不逐行重复。
    /// </summary>
    public static string? PeriodGain(long? starsToday, string periodLabel)
        => starsToday is null ? null : $"＋{NumberText.Grouped(starsToday.Value)} {periodLabel}";

    /// <summary>总数一律带千分位；null 与负数（异常输入）都显示为 0，不显示空白（空白看着像坏了）。</summary>
    public static string Total(long? stars)
        => $"★ {NumberText.Grouped(stars is > 0 ? stars.Value : 0)}";
}

/// <summary>
/// 热榜候选 → 卡片行的形状（纯函数）。
/// <para>刻意与 <see cref="TrendingItemDraft"/>（入库的书签条目）分开：这一条<b>不入库</b>，
/// <c>Id = 0</c> 且 <c>Source = trending</c>，靠 <c>ItemCardPolicy</c> 关掉一切会写主库的动作。</para>
/// </summary>
public static class TrendingRowDraft
{
    public static Item ForRow(TrendingRepo repo, TrendingPeriod period, TrendingSource via)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(repo.Language)) parts.Add(repo.Language.Trim());
        parts.Add(TrendingStarsText.Total(repo.Stars));
        // 只有热榜页那条腿才可能带"本期新增"，兜底来源直接不显示这一段
        if (via == TrendingSource.TrendingHtml
            && TrendingStarsText.PeriodGain(repo.StarsToday, TrendingPeriods.Label(period)) is { } gain)
            parts.Add(gain);

        return new Item
        {
            Id = 0,
            Type = ItemType.GitHubStar,                 // 只为取图标与星数列的既有渲染口径；这行不入库
            Source = ItemSources.Trending,
            SourceId = "trending:" + repo.FullName,
            Title = repo.FullName,
            Subtitle = string.Join(" · ", parts),
            Uri = repo.Url,
            Description = string.IsNullOrWhiteSpace(repo.Description) ? null : repo.Description.Trim(),
            StarsCount = repo.Stars,
        };
    }
}
