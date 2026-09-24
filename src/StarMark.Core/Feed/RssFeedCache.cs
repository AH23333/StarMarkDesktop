#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Feed;

namespace StarMark.Core.Feed;

/// <summary>
/// 订阅条目的<b>落盘形态</b>（缓存文件里存的那一份）。
/// <para>为什么不用 <see cref="RssEntry"/> 直接序列化：那是个记录类型，字段跟着解析器长；
/// 哪天加一个字段，旧档读出来就是 null（批次 B 同一课）。这里全是最原始的类型，
/// 且 <see cref="PublishedAtUnix"/> 用 <c>-1</c> 表示"源没给时间"——不拿"现在"冒充最新。</para>
/// <para><b>也刻意不存源名</b>：条目收进哪个文件夹是从 <c>entry.SourceName</c> 算出来的
/// （<see cref="RssRowDraft.ForCandidate"/> 把它写进行自带的 ExtraJson）。缓存里存一份名字，用户改了源名
/// 之后就会出现"文件夹标题写新的、点收藏进旧的"——同一个源的条目被劈成两半。名字只有一个来源：
/// <see cref="RssFeedCache.ToEntries"/> 当场按当前配置填。</para>
/// </summary>
public sealed class RssCachedEntry
{
    public string Title { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;

    /// <summary>发布时间的 Unix 秒；<c>-1</c>＝源没给或形式认不出（<b>不拿"现在"冒充</b>）。</summary>
    public long PublishedAtUnix { get; set; } = NoTime;

    public string Summary { get; set; } = string.Empty;
    public string? Author { get; set; }

    /// <summary>源没给时间时的取值。刻意用负数：0 是 1970-01-01，会冒充一条"很旧的条目"。</summary>
    public const long NoTime = -1;

    public static RssCachedEntry From(RssEntry entry) => new()
    {
        Title = entry.Title,
        Link = entry.Link,
        PublishedAtUnix = entry.PublishedAt?.ToUnixTimeSeconds() ?? NoTime,
        Summary = entry.Summary,
        Author = entry.Author,
    };

    public RssEntry ToEntry(int sourceId, string sourceName) => new(
        string.IsNullOrWhiteSpace(Title) ? Link : Title,
        Link,
        PublishedAtUnix == NoTime ? null : DateTimeOffset.FromUnixTimeSeconds(PublishedAtUnix),
        Summary,
        Author,
        sourceId,
        sourceName);
}

/// <summary>一个源在缓存里的那一份：校验符 + 上次成功抓取的时刻 + 条目。</summary>
public sealed class RssCachedSource
{
    public int SourceId { get; set; }
    public string? Etag { get; set; }
    public string? LastModified { get; set; }

    /// <summary>上次<b>成功</b>抓取的时刻（Unix 秒）；0＝从没成功过。失败不推进它，所以坏源明天还会再试。</summary>
    public long FetchedAtUnix { get; set; }

    public List<RssCachedEntry> Entries { get; set; } = new();
}

/// <summary>缓存文件。</summary>
public sealed class RssCacheFile
{
    public int Version { get; set; } = RssFeedCache.CacheVersion;
    public List<RssCachedSource> Sources { get; set; } = new();

    public RssCachedSource? Find(int sourceId) => Sources.FirstOrDefault(s => s.SourceId == sourceId);
}

/// <summary>
/// <b>缓存与"什么时候该抓"的全部判据</b>——放在 Core 而不是设置存储里：这几条决定
/// "打开这一页会不会联网、抓回来的要不要盖掉用户正在看的那一列"，而 <c>SettingsStore</c>/<c>RssCacheStore</c>
/// 在 UI 层、测试项目引用不到（批次 NF 同一课）。
/// </summary>
public static class RssFeedCache
{
    /// <summary>存档格式版本。<b>只加不改</b>：改了旧档读出来是错的东西，不如直接判为不认。</summary>
    public const int CacheVersion = 1;

    /// <summary>每个源在缓存里最多留多少条（源方给 500 条也只留这些；界面上另有 30 条一屏的口径）。</summary>
    public const int KeepPerSource = 60;

    /// <summary>
    /// 自动刷新的间隔：<b>一天一次</b>（用户裁决"每天仅刷新一次"）。
    /// <para>他原话是"每次刷新极为缓慢"，而订阅源是第三方站点：反复自动访问等于替用户承诺了他
    /// 没同意的流量，所以自动那一档压到一天一次，手动「刷新」仍然随时可用。</para>
    /// </summary>
    public static readonly TimeSpan AutoRefreshGap = TimeSpan.FromHours(24);

    /// <summary>这一源到没到期（该不该被这一轮自动抓取带上）。
    /// <para>从没抓过（<c>FetchedAtUnix == 0</c>）算到期——第一次进这一页必须真去抓一次，
    /// 否则缓存永远是空的，看上去就像"功能没生效"。</para></summary>
    public static bool IsDue(RssCachedSource? cached, long nowUnix)
        => cached is null || cached.FetchedAtUnix == 0 || nowUnix - cached.FetchedAtUnix >= (int)AutoRefreshGap.TotalSeconds;

    /// <summary>还有多久到期（给状态行说"下次自动刷新 X 小时后"，让用户知道它不是坏了）。</summary>
    public static TimeSpan UntilDue(RssCachedSource? cached, long nowUnix)
    {
        var elapsed = nowUnix - (cached?.FetchedAtUnix ?? 0);
        var left = (int)AutoRefreshGap.TotalSeconds - elapsed;
        return left <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(left);
    }

    /// <summary>
    /// 条目的身份键＝<b>条目自己的链接</b>（去首尾空白，忽略大小写）。
    /// <para><b>刻意不做 <c>UriNormalizer</c> 那套归一</b>：它会剥掉 query，而
    /// <c>?id=1</c> 与 <c>?id=2</c> 是两个不同的条目，剥掉就是静默丢条目
    /// （复用现成解前先问它守的是哪个不变量——那条是给"同一个页面的多个地址"去重用的）。</para>
    /// </summary>
    public static string Identity(string? link) => (link ?? string.Empty).Trim();

    /// <summary>
    /// 把这一轮抓回来的条目<b>并进</b>缓存（增量），返回"其中有多少是以前没见过的"。
    /// <para>
    /// 三条规矩，各自对着一种真机现象：
    /// ① <b>已有条目原地更新、不挪位置</b>——源方改了标题/摘要很常见，但把整列重排会让用户
    /// 手指正 pointing 的那条突然跳到别处；
    /// ② 新条目<b>插在最前</b>（按发布时间倒序，没给时间的排最后，与 <see cref="RssAggregator"/> 同口径）；
    /// ③ 只留 <see cref="KeepPerSource"/> 条，超出部分从<b>最旧</b>那头裁掉，而不是把刚进来的裁掉。
    /// </para>
    /// <para>缓存对象就地更新（它是这一轮的唯一持有者，没有并发读者）；<paramref name="nowUnix"/>
    /// 推进到这一次成功的时刻——<b>失败的那一轮不该推进它</b>，否则坏源会被"每天一次"按住一整天。</para>
    /// </summary>
    public static int Merge(RssCachedSource cached, IReadOnlyList<RssEntry> fresh, long nowUnix)
    {
        cached.FetchedAtUnix = nowUnix;
        var known = new HashSet<string>(cached.Entries.Select(e => Identity(e.Link)), StringComparer.OrdinalIgnoreCase);
        var additions = new List<RssCachedEntry>();
        foreach (var entry in fresh)
        {
            var key = Identity(entry.Link);
            if (key.Length == 0) continue;                       // 没有地址的条目既打不开也记不住，不收
            if (!known.Add(key))
            {
                // 见过的：内容取新的，位置不动
                var slot = cached.Entries.FindIndex(e => Identity(e.Link).Equals(key, StringComparison.OrdinalIgnoreCase));
                if (slot >= 0) cached.Entries[slot] = RssCachedEntry.From(entry);
                continue;
            }
            additions.Add(RssCachedEntry.From(entry));
        }

        if (additions.Count > 0)
            cached.Entries = additions
                .OrderByDescending(e => e.PublishedAtUnix == RssCachedEntry.NoTime ? long.MinValue : e.PublishedAtUnix)
                .ThenBy(e => e.Title, StringComparer.Ordinal)      // 同一时刻的次序要稳定，否则"没给时间"的那批每次刷新换个位置
                .Concat(cached.Entries)
                .Take(KeepPerSource)
                .ToList();
        return additions.Count;
    }

    /// <summary>缓存里的那一份变成界面能直接用的条目。<b>源名按当前配置现填</b>（见 <see cref="RssCachedEntry"/> 那一段）。</summary>
    public static List<RssEntry> ToEntries(RssCachedSource cached, RssSourceConfig source)
        => cached.Entries.Select(e => e.ToEntry(source.Id, source.Name)).ToList();

    /// <summary>
    /// 清洗一份读回来的缓存档：<b>坏的数据丢掉，好的数据一条都不能少</b>。
    /// <para>地址是空的条目留着只会"点开没反应"；同一个源出现两份（手改文件/版本回退）时取第一条，
    /// 因为校验符与条目只能有一份是真的；版本不认识时整档作废（宁可从头抓一次，也不按错的格式解读）。</para>
    /// </summary>
    public static RssCacheFile Normalize(RssCacheFile? stored)
    {
        var result = new RssCacheFile();
        if (stored is null || stored.Version != CacheVersion) return result;

        var seen = new HashSet<int>();
        foreach (var source in stored.Sources)
        {
            if (source is null || source.SourceId <= 0 || !seen.Add(source.SourceId)) continue;
            source.Entries ??= new List<RssCachedEntry>();
            source.Entries.RemoveAll(e => e is null || Identity(e.Link).Length == 0);
            if (source.Entries.Count > KeepPerSource)
                source.Entries = source.Entries.Take(KeepPerSource).ToList();
            result.Sources.Add(source);
        }
        return result;
    }
}
