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
/// <para>批次 WI-2u 起字进语言表（<c>rss.src.*</c>，键逐颗写字面＝坑表 #331）。
/// <b>批次 WI-65 片2 起交的是「键＋实参」而不是念完的那句</b>：取词发生在<b>贴到屏幕上那一刻</b>
/// （宿主那一拍走 <c>LocText.SetMessage</c>），所以换语言时已经铺出来的那一行会跟着换——
/// 从前那句「切语言后要重进那一页」是这一族已知不彻底的登记形状，本批把它收掉了（✅P-194 片2）。</para>
/// </summary>
public static class RssSourceStatus
{
    /// <summary>还没抓过的行给的不是空字符串：空着看着像渲染坏了。</summary>
    public static LocMessage NeverFetched => LocMessage.Of("rss.src.neverFetched");

    /// <summary>
    /// 从<b>缓存</b>摆出来时的那句（还没轮到这一源的抓取）。
    /// <para>必须说清"这是上一次的结果、什么时候的、下一次自动刷新大概什么时候"：
    /// 一个每天只抓一次的页面如果只写"共 20 条"，用户会以为它是实时的，
    /// 看不到新文章时就判定功能坏了（同一页上"提示要长在看得见的那一页"）。</para>
    /// <para>夹在外层句子里的那两段（<b>多久以前</b>与<b>什么时候到期</b>）交的都是「键＋实参」——
    /// 外层将来由宿主重贴，里面这半句若在这里就先念完，屏幕上就剩「Last fetched 3 天前: …」那种半中半英
    /// （＝✅WI-63 结案 P-195 时同一条判据，也是本批⛔ 半搬的那一件事）。</para>
    /// </summary>
    public static LocMessage FromCache(RssCachedSource cached, long nowUnix)
    {
        var age = TimeSpan.FromSeconds(Math.Max(0, nowUnix - cached.FetchedAtUnix));
        // 档位（两分钟内都算"刚刚"）是这一页的判断；四档措辞与取整归 DateTimeText（批次 SJ）。
        var when = age < TimeSpan.FromMinutes(2) ? DateTimeText.JustNowMessage()
            : age < TimeSpan.FromHours(1) ? DateTimeText.MinutesAgoMessage((long)age.TotalMinutes)
            : age < TimeSpan.FromDays(1) ? DateTimeText.HoursAgoMessage((long)age.TotalHours)
            : DateTimeText.DaysAgoMessage(age.Days);
        var next = RssFeedCache.UntilDue(cached, nowUnix);
        var tail = next <= TimeSpan.Zero
            ? LocMessage.Of("rss.src.dueNow")
            : LocMessage.Of("rss.src.dueIn", ((int)Math.Ceiling(next.TotalHours)).ToString());
        return LocMessage.Of("rss.src.cacheLine", when, cached.Entries.Count.ToString(), tail);
    }

    /// <summary>
    /// <paramref name="outcome"/> 为 null 表示这一轮它压根没参与（例如刚添加进来还没刷新过）。
    /// 五种"没有内容"必须分得开：停止 / 停用 / 未变化 / 失败 / 通了但空。
    /// <para><paramref name="cachedTotal"/> 是这一组<b>现在摆出来</b>的条数（并进缓存之后那份的总数）。
    /// 有了增量刷新，这两个数绝大多数时候不相等，而标题上的"共 N 条"就是它——只写"抓到 8 条"
    /// 会被读成"这个源一共就 8 条"，用户以为缓存丢了。</para>
    /// </summary>
    public static LocMessage Describe(RssSourceOutcome? outcome, int? cachedTotal = null) => outcome switch
    {
        null => LocMessage.Of("rss.src.notInRound"),
        var o when o.Stopped => Join(LocMessage.Of("rss.src.stopped"), StillThere(cachedTotal)),
        var o when !o.Source.Enabled => LocMessage.Of("rss.src.disabled"),
        { NotModified: true } => Join(LocMessage.Of("rss.src.notModified"), StillThere(cachedTotal)),
        { Ok: false } o => Join(LocMessage.Of("rss.src.failed", o.Error), StillThere(cachedTotal)),
        var o when o.Entries.Count == 0 => Join(LocMessage.Of("rss.src.emptyOk"), StillThere(cachedTotal)),
        var o => Join(LocMessage.Of("rss.src.fetched", o.Entries.Count.ToString()), Grew(o, cachedTotal)),
    };

    /// <summary>
    /// 那半句与主句的<b>语序与标点归表</b>：⛔ 在 C# 里用 <c>+</c> 把两句拼成品字——
    /// 拼出来的串把语言定死在拼它那一刻，而那颗胶水键（<c>rss.statusJoin</c>＝<c>{0}{1}</c>，三语逐字相同）
    /// 才是"这两段谁在前"的唯一出处；那半句自带的标点仍住在各句自己的键里（zh「，」／en「, 」／ja「。」）。
    /// <para>同页表头那句早就是这一颗键的主人（<c>RssPageViewModel</c> 那三处），这里只是不再另写一种拼法。</para>
    /// </summary>
    private static LocMessage Join(LocMessage head, LocMessage? tail)
        => tail is null ? head : LocMessage.Of("rss.statusJoin", head, tail);

    /// <summary>只在两个数真的不相等时才补那半句（相等时重复一遍数字是噪音）。</summary>
    private static LocMessage? Grew(RssSourceOutcome outcome, int? cachedTotal)
        => cachedTotal is int total && total != outcome.Entries.Count
            ? LocMessage.Of("rss.src.grew", total.ToString()) : null;

    /// <summary>这一轮没拿到新条目，但界面上仍留着上次缓存的那一列：不说的话看上去就像"内容被清空了"。</summary>
    private static LocMessage? StillThere(int? cachedTotal)
        => cachedTotal is > 0 ? LocMessage.Of("rss.src.stillThere", cachedTotal.Value.ToString()) : null;
}
