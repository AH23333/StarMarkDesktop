#nullable enable
using System;
using StarMark.Abstractions;
using StarMark.Abstractions.Feed;
using StarMark.Abstractions.I18n;

namespace StarMark.Core.Feed;

/// <summary>
/// 一行订阅源"这一轮到底怎么样了"的那句文案（判据只在这里说一次）。
/// <para>原来这份写法住在设置页里，于是它一条都断言不到；而"没抓到"这件事有五种截然不同的原因，
/// <b>混成一句"没有内容"用户就不知道该改地址还是该查网络</b>（批次 NF 的教训）。搬到 Core 之后
/// 每一种状态各有一条用例钉着，改坏了会红。</para>
/// <para>批次 WI-2u 起字进语言表（<c>rss.src.*</c>，键逐颗写字面＝坑表 #331）；
/// 取词发生在<b>调用那一刻</b>（建行/刷新那一下），切语言后已铺出的状态行要重进那一页＝绑定族口径。</para>
/// </summary>
public static class RssSourceStatus
{
    /// <summary>还没抓过的行给的不是空字符串：空着看着像渲染坏了。</summary>
    public static string NeverFetched => Locale.T("rss.src.neverFetched");

    /// <summary>
    /// 从<b>缓存</b>摆出来时的那句（还没轮到这一源的抓取）。
    /// <para>必须说清"这是上一次的结果、什么时候的、下一次自动刷新大概什么时候"：
    /// 一个每天只抓一次的页面如果只写"共 20 条"，用户会以为它是实时的，
    /// 看不到新文章时就判定功能坏了（同一页上"提示要长在看得见的那一页"）。</para>
    /// </summary>
    public static string FromCache(RssCachedSource cached, long nowUnix)
    {
        var age = TimeSpan.FromSeconds(Math.Max(0, nowUnix - cached.FetchedAtUnix));
        // 档位（两分钟内都算"刚刚"）是这一页的判断；四档措辞与取整归 DateTimeText（批次 SJ）。
        var when = age < TimeSpan.FromMinutes(2) ? DateTimeText.JustNow
            : age < TimeSpan.FromHours(1) ? DateTimeText.MinutesAgo((long)age.TotalMinutes)
            : age < TimeSpan.FromDays(1) ? DateTimeText.HoursAgo((long)age.TotalHours)
            : DateTimeText.DaysAgo(age.Days);
        var next = RssFeedCache.UntilDue(cached, nowUnix);
        var tail = next <= TimeSpan.Zero
            ? Locale.T("rss.src.dueNow")
            : Locale.Fmt("rss.src.dueIn", ((int)Math.Ceiling(next.TotalHours)).ToString());
        return Locale.Fmt("rss.src.cacheLine", when, cached.Entries.Count.ToString(), tail);
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
        null => Locale.T("rss.src.notInRound"),
        var o when o.Stopped => Locale.T("rss.src.stopped") + StillThere(cachedTotal),
        var o when !o.Source.Enabled => Locale.T("rss.src.disabled"),
        { NotModified: true } => Locale.T("rss.src.notModified") + StillThere(cachedTotal),
        { Ok: false } o => Locale.Fmt("rss.src.failed", o.Error) + StillThere(cachedTotal),
        var o when o.Entries.Count == 0 => Locale.T("rss.src.emptyOk") + StillThere(cachedTotal),
        var o => Locale.Fmt("rss.src.fetched", o.Entries.Count.ToString()) + Grew(o, cachedTotal),
    };

    /// <summary>只在两个数真的不相等时才补那半句（相等时重复一遍数字是噪音）。</summary>
    private static string Grew(RssSourceOutcome outcome, int? cachedTotal)
        => cachedTotal is int total && total != outcome.Entries.Count
            ? Locale.Fmt("rss.src.grew", total.ToString()) : string.Empty;

    /// <summary>这一轮没拿到新条目，但界面上仍留着上次缓存的那一列：不说的话看上去就像"内容被清空了"。</summary>
    private static string StillThere(int? cachedTotal)
        => cachedTotal is > 0 ? Locale.Fmt("rss.src.stillThere", cachedTotal.Value.ToString()) : string.Empty;
}
