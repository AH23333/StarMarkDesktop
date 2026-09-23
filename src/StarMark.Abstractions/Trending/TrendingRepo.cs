#nullable enable

namespace StarMark.Abstractions.Trending;

/// <summary>热榜周期（页面上的 ?since= 参数，也决定 Search API 兜底的 created 窗口）。</summary>
public enum TrendingPeriod
{
    Daily,
    Weekly,
    Monthly,
}

/// <summary>
/// 周期的对外口径：GitHub 用的代码字符串，与兜底查询用的天数窗口。
/// <para>
/// 天数取扩展已验证的 2 / 7 / 30：<b>日榜给 2 天而不是 1 天</b>，因为"今天新建的仓库"几乎必然没几个能过
/// <c>stars:&gt;50</c> 的门槛 ⇒ 兜底会稳定返回空列表（看着像"今天没有热榜"，实际是查询写死得太严）。
/// </para>
/// </summary>
public static class TrendingPeriods
{
    public static string Code(TrendingPeriod period) => period switch
    {
        TrendingPeriod.Daily => "daily",
        TrendingPeriod.Monthly => "monthly",
        _ => "weekly",
    };

    /// <summary>兜底 Search API 的 <c>created:&gt;</c> 回看天数。</summary>
    public static int FallbackDays(TrendingPeriod period) => period switch
    {
        TrendingPeriod.Daily => 2,
        TrendingPeriod.Monthly => 30,
        _ => 7,
    };

    /// <summary>周期的人话标签（筛选按钮与来源标注共用）。</summary>
    public static string Label(TrendingPeriod period) => period switch
    {
        TrendingPeriod.Daily => "今日",
        TrendingPeriod.Monthly => "本月",
        _ => "本周",
    };

    /// <summary>缓存与展示都用代码字符串，避免两套写法。</summary>
    public static bool TryParse(string? code, out TrendingPeriod period)
    {
        switch (code)
        {
            case "daily": period = TrendingPeriod.Daily; return true;
            case "monthly": period = TrendingPeriod.Monthly; return true;
            case "weekly": case null or "": period = TrendingPeriod.Weekly; return true;
            default: period = TrendingPeriod.Weekly; return false;
        }
    }
}

/// <summary>
/// 一个热榜候选仓库。刻意<b>不是</b> <c>Item</c>：热榜默认不入库（P-67），
/// 只有用户主动点「收进收藏」才写一条书签条目，点「Star」则只改远端状态、由下次同步带回来。
/// </summary>
/// <param name="StarsToday">本期新增星数。null＝这一路数据没有该信息（Search API 兜底就没有），
/// 界面必须据此显示"新增星数未知"而不是 0 —— 把"不知道"画成"+0"是在骗人。</param>
public sealed record TrendingRepo(
    string FullName,
    string Url,
    string Description,
    string? Language,
    long Stars,
    long? StarsToday = null)
{
    /// <summary><c>owner/repo</c> 两段；Star 写接口按这个形状拼 URL。</summary>
    public string Owner => Segment(0);
    public string Repo => Segment(1);

    private string Segment(int i)
    {
        var parts = FullName.Split('/');
        return parts.Length > i ? parts[i] : string.Empty;
    }
}
