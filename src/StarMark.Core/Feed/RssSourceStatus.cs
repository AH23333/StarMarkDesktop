#nullable enable
using System;
using StarMark.Abstractions.Feed;

namespace StarMark.Core.Feed;

/// <summary>
/// 一行订阅源"这一轮到底怎么样了"的那句文案（判据只在这里说一次）。
/// <para>原来这份写法住在设置页里，于是它一条都断言不到；而"没抓到"这件事有五种截然不同的原因，
/// <b>混成一句"没有内容"用户就不知道该改地址还是该查网络</b>（批次 NF 的教训）。搬到 Core 之后
/// 每一种状态各有一条用例钉着，改坏了会红。</para>
/// </summary>
public static class RssSourceStatus
{
    /// <summary>还没抓过的行给的不是空字符串：空着看着像渲染坏了。</summary>
    public const string NeverFetched = "还没抓过";

    /// <summary>
    /// 从<b>缓存</b>摆出来时的那句（还没轮到这一源的抓取）。
    /// <para>必须说清"这是上一次的结果、什么时候的、下一次自动刷新大概什么时候"：
    /// 一个每天只抓一次的页面如果只写"共 20 条"，用户会以为它是实时的，
    /// 看不到新文章时就判定功能坏了（同一页上"提示要长在看得见的那一页"）。</para>
    /// </summary>
    public static string FromCache(RssCachedSource cached, long nowUnix)
    {
        var age = TimeSpan.FromSeconds(Math.Max(0, nowUnix - cached.FetchedAtUnix));
        var when = age < TimeSpan.FromMinutes(2) ? "刚刚"
            : age < TimeSpan.FromHours(1) ? $"{(int)age.TotalMinutes} 分钟前"
            : age < TimeSpan.FromDays(1) ? $"{(int)age.TotalHours} 小时前"
            : $"{age.Days} 天前";
        var next = RssFeedCache.UntilDue(cached, nowUnix);
        var tail = next <= TimeSpan.Zero ? "现在到期，会自动补抓" : $"约 {(int)Math.Ceiling(next.TotalHours)} 小时后自动刷新";
        return $"上次抓取 {when} · 共 {cached.Entries.Count} 条 · {tail}";
    }

    /// <summary>
    /// <paramref name="outcome"/> 为 null 表示这一轮它压根没参与（例如刚添加进来还没刷新过）。
    /// 五种"没有内容"必须分得开：停止 / 停用 / 未变化 / 失败 / 通了但空。
    /// <para><paramref name="cachedTotal"/> 是这一组<b>现在摆出来</b>的条数（并进缓存之后那份的总数）。
    /// 有了增量刷新，这两个数绝大多数时候不相等，而标题上的"共 N 条"就是它——只写"抓到 8 条"
    /// 会被读成"这个源一共就 8 条"，用户以为缓存丢了。</para>
    /// </summary>
    public static string Describe(RssSourceOutcome? outcome, int? cachedTotal = null) => outcome switch
    {
        null => "没有参与这一轮",
        var o when o.Stopped => "没抓到它（已停止或整轮到时）" + StillThere(cachedTotal),
        var o when !o.Source.Enabled => "已停用，这一轮没有抓",
        { NotModified: true } => "没有新内容（源说未变化）" + StillThere(cachedTotal),
        { Ok: false } o => "失败：" + o.Error + StillThere(cachedTotal),
        var o when o.Entries.Count == 0 => "通了，但没有条目" + StillThere(cachedTotal),
        var o => $"抓到 {o.Entries.Count} 条" + Grew(o, cachedTotal),
    };

    /// <summary>只在两个数真的不相等时才补那半句（相等时重复一遍数字是噪音）。</summary>
    private static string Grew(RssSourceOutcome outcome, int? cachedTotal)
        => cachedTotal is int total && total != outcome.Entries.Count ? $"，缓存共 {total} 条" : string.Empty;

    /// <summary>这一轮没拿到新条目，但界面上仍留着上次缓存的那一列：不说的话看上去就像"内容被清空了"。</summary>
    private static string StillThere(int? cachedTotal)
        => cachedTotal is > 0 ? $"，仍然摆着缓存里的 {cachedTotal} 条" : string.Empty;
}
