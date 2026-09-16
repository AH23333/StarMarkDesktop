#nullable enable
using System.Collections.Generic;

namespace StarMark.Abstractions.Insights;

/// <summary>
/// 单个扣分因子（P2-6 健康度报告）。起点 100，按因子扣分得到总分。
/// </summary>
public sealed class HealthFactor
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int Deduction { get; init; }
    public string Detail { get; init; } = string.Empty;
}

/// <summary>语言分布条目。</summary>
public sealed class LanguageStat
{
    public string Language { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>标签直方图条目。</summary>
public sealed class TagStat
{
    public string Tag { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>按日期分桶的计数（趋势图数据点）。</summary>
public sealed class DateCount
{
    public string Date { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>疑似重复组（按归一化标题分组，size >= 2）。</summary>
public sealed class DuplicateGroup
{
    public string Title { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>
/// 收藏健康度报告（P2-6）。由 <see cref="StarMark.Core.Insights.InsightsService.BuildHealthReport"/> 纯函数产出，
/// 输入 <see cref="Item"/> 列表，零外部依赖、零网络。
/// </summary>
public sealed class HealthReport
{
    /// <summary>健康度总分（0–100，扣分制）。</summary>
    public int Score { get; init; }

    /// <summary>参与评估的条目总数。</summary>
    public int Total { get; init; }

    /// <summary>扣分因子明细。</summary>
    public List<HealthFactor> Factors { get; init; } = new();

    /// <summary>语言分布 Top8。</summary>
    public List<LanguageStat> LanguageTop { get; init; } = new();

    /// <summary>近 N 天新增趋势（按 anchor 日期分桶，含 0 值以保持连续）。</summary>
    public List<DateCount> NewTrend { get; init; } = new();

    /// <summary>独立域名数（http/https）。</summary>
    public int DistinctDomains { get; init; }

    /// <summary>标签直方图 Top20。</summary>
    public List<TagStat> TagHistogram { get; init; } = new();

    /// <summary>疑似重复 Top10。</summary>
    public List<DuplicateGroup> DuplicateTop { get; init; } = new();
}
