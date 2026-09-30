#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Abstractions.Feed;
using StarMark.Core.Feed;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 订阅条目的<b>落盘缓存</b>与"什么时候该联网"的那套判据（批次 RF-3）。
/// <para>
/// 用户原话："RSS 依旧直接抓取大量文章（足足 60），导致每次刷新极为缓慢，建议添加缓存机制，
/// 每天仅刷新一次，且增量刷新。" 三件事各自都要钉住，因为它们各自会以一种不报错的方式坏掉：
/// </para>
/// <list type="number">
/// <item><b>每天一次</b>：判据写成 <c>now - fetchedAt &gt;= 24h</c>，边界（正好一天）与"从没抓过"必须都试过——
/// 把"从没抓过"判成"没到期"，这一页就永远是空的，看上去像功能没做。</item>
/// <item><b>增量</b>：已有条目必须<b>原地更新、不挪位置</b>。整列重排的代价是用户手指正 pointing 的那条
/// 突然跳走；而"新条目插最前"如果反过来写成 append，一天之后列表顶部全是旧的。</item>
/// <item><b>缓存</b>：坏档/缺文件/只读目录三种情况都要能继续用（这一档只是加速件，最重的后果是多抓一次），
/// 但<b>不能多抓一次都不说</b>——写盘失败要回报，否则"我明明刷新了，怎么下次还是旧的"无从归因。</item>
/// </list>
/// <para>判据全在 Core：这份档的读写落在 UI 层的 <c>SettingsStore</c> 旁边就一条都断言不到（批次 NF 同一课）。</para>
/// <para>与 <see cref="UserDataPathsTests"/> 同集合：<see cref="TheCacheFileSitsNextToSettingsJson"/> 会改
/// <c>STARMARK_DB_PATH</c>／<c>STARMARK_SETTINGS_PATH</c> 这两个<b>进程级</b>变量，而那边按同一组变量断言默认落点
/// ——两个集合并行时，任何一边都可能读到对方那一刻的值（xUnit 默认并行，进程环境变量却是全局的）。</para>
/// </summary>
[Collection("UserDataPaths")]
public sealed class RssFeedCacheTests : IDisposable
{
    private const long Day = 86_400;
    private const long Now = 1_770_000_000;

    private readonly string _dir;

    public RssFeedCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "starmark_rsscache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static RssEntry Entry(string link, string title = "标题", DateTimeOffset? at = null)
        => new(title, link, at, "摘要", null, 1, "源");

    private static RssCachedSource Cached(long fetchedAtUnix, params RssEntry[] entries) => new()
    {
        SourceId = 1,
        FetchedAtUnix = fetchedAtUnix,
        Entries = entries.Select(RssCachedEntry.From).ToList(),
    };

    private static RssSourceConfig Source(string name = "开源中国") => new(1, name, "https://a.test/feed");

    // ────────── 每天一次 ──────────

    /// <summary>"从没抓过"与"今天刚抓过"之间的距离是这一栏的全部行为差别，两种都必须试过。</summary>
    [Theory]
    [InlineData(null, true)]                        // 档里压根没有这一源
    [InlineData(0L, true)]                          // 有这一源但从没成功过
    [InlineData(Now - Day + 1, false)]              // 差一秒不到一天：不打扰
    [InlineData(Now - Day, true)]                   // 正好一天：到期（闭区间，与状态行那句"约 24 小时后"同一口径）
    [InlineData(Now - 2 * Day, true)]
    public void IsDueOnlyForASourceThatHasNotBeenFetchedToday(long? fetchedAt, bool expected)
        => Assert.Equal(expected, RssFeedCache.IsDue(fetchedAt is null ? null : Cached(fetchedAt.Value), Now));

    [Fact]
    public void AnEmptyCacheFileIsNeverMistakenForUpToDate()
        => Assert.True(RssFeedCache.IsDue(new RssCacheFile().Find(1), Now));   // 冷启动第一次进页面必须真抓

    /// <summary>节奏要说人话：<b>24 小时得说"一天"</b>（用户原话就是"每天仅刷新一次"，用他的词回给他，
    /// 才看得出这条口径真的落实了）。而这句话只能从这里产生——界面上那几处都引用它，
    /// 改 <see cref="RssFeedCache.AutoRefreshGap"/> 就不会留下还在说旧数字的假话。</summary>
    [Theory]
    [InlineData(0.5, "30 分钟")]
    [InlineData(6, "6 小时")]
    [InlineData(24, "一天")]
    [InlineData(48, "2 天")]
    [InlineData(30, "1 天多")]
    public void TheCadenceIsSaidInHumanWords(double hours, string expected)
        => Assert.Equal(expected, RssFeedCache.GapTextFor(TimeSpan.FromHours(hours)));

    [Fact]
    public void TheCadenceQuotedOnScreenIsTheOneTheGateActuallyUses()
        => Assert.Equal(RssFeedCache.GapTextFor(RssFeedCache.AutoRefreshGap), RssFeedCache.GapText);

    /// <summary>还有多久到期：给状态行说"约 X 小时后自动刷新"。已经过了的不能给负数（那会显示成"约 -3 小时"）。</summary>
    [Theory]
    [InlineData(Now - 23 * 3600, 3600)]
    [InlineData(Now - Day, 0)]
    [InlineData(Now - 3 * Day, 0)]
    public void UntilDueCountsDownAndNeverGoesNegative(long fetchedAt, int expectedLeft)
        => Assert.Equal(TimeSpan.FromSeconds(expectedLeft), RssFeedCache.UntilDue(Cached(fetchedAt), Now));

    // ────────── 增量并入 ──────────

    [Fact]
    public void MergeInsertsNewEntriesNewestFirstAndKeepsTheOldOnesBehind()
    {
        var cached = Cached(Now - Day, Entry("https://a.test/c", "很早", DateTimeOffset.FromUnixTimeSeconds(Now - 3 * 3600)));
        var added = RssFeedCache.Merge(cached, new[]
        {
            Entry("https://a.test/b", "昨天", DateTimeOffset.FromUnixTimeSeconds(Now - 2 * 3600)),
            Entry("https://a.test/a", "刚刚", DateTimeOffset.FromUnixTimeSeconds(Now - 3600)),
        }, Now);

        Assert.Equal(2, added);
        Assert.Equal(new[] { "刚刚", "昨天", "很早" }, cached.Entries.Select(e => e.Title));
    }

    [Fact]
    public void MergeUpdatesAnEntryItAlreadyHasWithoutMovingIt()
    {
        // 源方改了标题/摘要很常见。跟着重排的后果是"我手指着的那条突然跳到别处"，所以位置必须钉住。
        var cached = Cached(Now - Day,
            Entry("https://a.test/x", "旧标题", DateTimeOffset.FromUnixTimeSeconds(Now - 3600)),
            Entry("https://a.test/y", "另一条", DateTimeOffset.FromUnixTimeSeconds(Now - 7200)));

        var added = RssFeedCache.Merge(cached, new[]
        {
            Entry("https://a.test/x", "新标题", DateTimeOffset.FromUnixTimeSeconds(Now - 3600)),
        }, Now);

        Assert.Equal(0, added);
        Assert.Equal(2, cached.Entries.Count);
        Assert.Equal("新标题", cached.Entries[0].Title);
        Assert.Equal("另一条", cached.Entries[1].Title);
    }

    [Fact]
    public void MergeIgnoresCaseAndSurroundingSpacesWhenMatching()
    {
        var cached = Cached(Now - Day, Entry("https://a.test/1"));
        Assert.Equal(0, RssFeedCache.Merge(cached, new[] { Entry("  HTTPS://A.TEST/1 ") }, Now));
        Assert.Single(cached.Entries);                        // 没有多出第二条一模一样的
    }

    /// <summary>
    /// 只有 query 不同的两条是两个条目。<b>这一条是对"顺手复用 <c>UriNormalizer</c>"的防守</b>：
    /// 那个归一是给"同一个页面的多个地址"去重用的，它会剥掉 query，用到条目身份上就是静默丢条目。
    /// </summary>
    [Fact]
    public void TwoEntriesDifferingOnlyByQueryAreTwoDifferentEntries()
    {
        Assert.NotEqual(RssFeedCache.Identity("https://a.test/feed?feed=rss2"),
                        RssFeedCache.Identity("https://a.test/feed?feed=atom"));

        var cached = Cached(Now - Day, Entry("https://a.test/p?id=1"));
        Assert.Equal(1, RssFeedCache.Merge(cached, new[] { Entry("https://a.test/p?id=2") }, Now));
        Assert.Equal(2, cached.Entries.Count);
    }

    [Fact]
    public void AnEntryWithoutALinkIsNotMergeableAndNotWorthKeeping()
    {
        // 没有地址的条目既打不开也记不住（收藏落点靠链接算），留着只是每轮重复出现一条"点了没反应"
        var cached = Cached(Now - Day, Entry("https://a.test/ok"));
        Assert.Equal(0, RssFeedCache.Merge(cached, new[] { Entry("   ") }, Now));
        Assert.Single(cached.Entries);
    }

    [Fact]
    public void MergeTrimsTheOldestEndNotTheNewArrivals()
    {
        var seeded = Enumerable.Range(1, RssFeedCache.KeepPerSource)
            .Select(i => Entry($"https://a.test/old{i}", $"旧{i}", DateTimeOffset.FromUnixTimeSeconds(Now - (i + 10) * 3600)))
            .ToArray();
        var cached = Cached(Now - Day, seeded);

        var fresh = Enumerable.Range(1, 5)
            .Select(i => Entry($"https://a.test/new{i}", $"新{i}", DateTimeOffset.FromUnixTimeSeconds(Now - i * 60)))
            .ToArray();
        Assert.Equal(5, RssFeedCache.Merge(cached, fresh, Now));

        Assert.Equal(RssFeedCache.KeepPerSource, cached.Entries.Count);
        Assert.Equal("新1", cached.Entries[0].Title);
        Assert.Contains("新5", cached.Entries.Select(e => e.Title));
        Assert.Contains("旧55", cached.Entries.Select(e => e.Title));   // 旧的那 55 条留在后面
        Assert.DoesNotContain("旧56", cached.Entries.Select(e => e.Title));   // 从最旧那头裁掉
    }

    [Fact]
    public void MergeAdvancesTheFetchedMomentSoTheDailyGateResets()
    {
        var cached = Cached(Now - Day, Entry("https://a.test/x"));
        RssFeedCache.Merge(cached, Array.Empty<RssEntry>(), Now);
        Assert.Equal(Now, cached.FetchedAtUnix);
        Assert.False(RssFeedCache.IsDue(cached, Now));        // 抓过之后今天就不要再自动抓它
    }

    [Fact]
    public void EntriesWithoutAPublicationTimeSurviveAsUndatedNotAsNow()
    {
        // 拿"现在"补一个时间，缓存隔天再读就冒出一条假装刚发布的内容
        var cached = Cached(Now - Day, Entry("https://a.test/x", "源没给时间", null));
        Assert.Equal(RssCachedEntry.NoTime, cached.Entries[0].PublishedAtUnix);

        var back = RssFeedCache.ToEntries(cached, Source());
        Assert.Null(back[0].PublishedAt);
    }

    /// <summary>
    /// 源名<b>不</b>跟着条目存进缓存：那一行收藏会落进哪个文件夹是从 <c>entry.SourceName</c> 算出来的
    /// （见 <see cref="RssRowDraft"/>），存一份旧名字就会让用户改了源名之后"标题写新文件夹、点收藏进旧文件夹"。
    /// </summary>
    [Fact]
    public void RowsReadBackFromTheCacheCarryTheCurrentSourceName()
    {
        var cached = Cached(Now - Day, Entry("https://a.test/x"));
        var rows = RssFeedCache.ToEntries(cached, Source("改名之后的源"));

        Assert.Equal("改名之后的源", rows[0].SourceName);
        var meta = JsonSerializer.Deserialize<BookmarkMeta>(RssRowDraft.ForCandidate(rows[0]).ExtraJson!)!;
        Assert.Equal(new[] { RssFolders.Root, "改名之后的源" }, meta.FolderPaths);   // 标题说的文件夹＝真正写进去的那个
        Assert.DoesNotContain("SourceName", JsonSerializer.Serialize(cached));       // 档里根本没有这个字段
    }

    // ────────── 读回来的那份要先清洗 ──────────

    [Fact]
    public void AnUnknownVersionIsDiscardedRatherThanMisread()
    {
        var stored = new RssCacheFile { Version = RssFeedCache.CacheVersion + 1 };
        stored.Sources.Add(Cached(Now, Entry("https://a.test/x")));
        Assert.Empty(RssFeedCache.Normalize(stored).Sources);       // 宁可从头抓一次，也不按猜的格式解读

        var known = new RssCacheFile { Version = RssFeedCache.CacheVersion };
        known.Sources.Add(Cached(Now, Entry("https://a.test/x")));
        Assert.Single(RssFeedCache.Normalize(known).Sources);       // 认得的版本一条都不该丢
    }

    [Fact]
    public void NormalizeDropsJunkAndKeepsEveryGoodEntry()
    {
        var good = Cached(Now, Enumerable.Range(1, RssFeedCache.KeepPerSource + 9)
            .Select(i => Entry($"https://a.test/{i}")).ToArray());
        good.Entries.Add(new RssCachedEntry { Link = "  ", Title = "没有地址" });

        var duplicate = Cached(Now - 10, Entry("https://b.test/x"));      // 同一个源出现两份（手改文件）
        var zeroId = new RssCachedSource { SourceId = 0, Entries = new List<RssCachedEntry>() };

        var stored = new RssCacheFile();
        stored.Sources.Add(good);
        stored.Sources.Add(duplicate);
        stored.Sources.Add(zeroId);
        stored.Sources.Add(new RssCachedSource { SourceId = 3, Entries = null! });   // 手改出来的 null

        var clean = RssFeedCache.Normalize(stored);

        Assert.Equal(new[] { 1, 3 }, clean.Sources.Select(s => s.SourceId));         // 同 id 取第一条，0 号丢掉
        Assert.Equal(RssFeedCache.KeepPerSource, clean.Sources[0].Entries.Count);   // 超出的从尾部裁掉
        Assert.All(clean.Sources[0].Entries, e => Assert.NotEqual("没有地址", e.Title));
        Assert.Empty(clean.Sources[1].Entries);                                     // null 变成空列表，不是崩溃
    }

    [Fact]
    public void NormalizeTakesANullArchiveAndEmptyFileGracefully()
    {
        Assert.NotNull(RssFeedCache.Normalize(null));
        Assert.Empty(RssFeedCache.Normalize(null).Sources);
        Assert.Empty(RssFeedCache.Normalize(new RssCacheFile()).Sources);
    }

    // ────────── 落盘 ──────────

    private RssCacheStore Store(string name) => new(Path.Combine(_dir, name));

    [Fact]
    public void MissingFileReadsAsNoCacheInsteadOfThrowing()
        => Assert.Empty(Store("没有这个文件.json").Load().Sources);

    [Fact]
    public void AWholeRoundTripsThroughDiskIncludingTheValidators()
    {
        var store = Store("rss-cache.json");
        var file = new RssCacheFile();
        var cached = Cached(Now - Day, Entry("https://a.test/x", "带校验符的", DateTimeOffset.FromUnixTimeSeconds(Now - 60)));
        cached.Etag = "W/\"abc\"";
        cached.LastModified = "Wed, 04 Mar 2026 00:00:00 GMT";
        file.Sources.Add(cached);

        Assert.True(store.Save(file));
        var read = store.Load();

        Assert.Equal("W/\"abc\"", read.Find(1)?.Etag);                              // 校验符不落盘＝下次又是全量下载
        Assert.Equal(Now - Day, read.Find(1)?.FetchedAtUnix);
        Assert.Equal("带校验符的", read.Find(1)?.Entries[0].Title);
        Assert.Equal(RssFeedCache.CacheVersion, read.Version);
    }

    [Fact]
    public void ACorruptArchiveReadsAsNoCacheAndDoesNotBreakThePage()
    {
        var store = Store("rss-cache.json");
        File.WriteAllText(store.StorePath, "{这不是 JSON");
        Assert.Empty(store.Load().Sources);                     // 最重的后果只是多抓一次

        File.WriteAllText(store.StorePath, "半截写坏的");        // 上一次写盘中途崩溃留下的残档
        Assert.Empty(store.Load().Sources);
    }

    [Fact]
    public void AnAtomicWriteLeavesNoPartialFileBehind()
    {
        var store = Store("rss-cache.json");
        var file = new RssCacheFile();
        file.Sources.Add(Cached(Now, Entry("https://a.test/x", "第一条")));
        store.Save(file);

        file.Sources.Add(new RssCachedSource { SourceId = 2, FetchedAtUnix = Now });
        store.Save(file);

        Assert.False(File.Exists(store.StorePath + ".tmp"));    // 临时文件搬走了，不是留在原地当垃圾
        Assert.Equal(new[] { 1, 2 }, store.Load().Sources.Select(s => s.SourceId));
    }

    [Fact]
    public void AFailedWriteIsReportedInsteadOfSilentlyLosingTheRound()
    {
        // 父路径是一个文件 ⇒ 建目录必失败。这一档只是加速件，写不成不能让整页抛异常，
        // 但必须把"没写成"回传出去（否则用户只会觉得"我明明刷新了，下次还是旧的"）。
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "我是个文件不是目录");
        var store = new RssCacheStore(Path.Combine(blocker, "rss-cache.json"));

        Assert.False(store.Save(new RssCacheFile()));
        Assert.Empty(store.Load().Sources);                     // 读也一样：按空缓存继续
    }

    [Fact]
    public void DeletingASourceTakesItsCacheWithIt()
    {
        var file = new RssCacheFile();
        file.Sources.Add(new RssCachedSource { SourceId = 1, FetchedAtUnix = Now });
        file.Sources.Add(new RssCachedSource { SourceId = 2, FetchedAtUnix = Now });
        file.Sources.Add(new RssCachedSource { SourceId = 3, FetchedAtUnix = Now });

        new RssCacheStore(Path.Combine(_dir, "无关.json")).Without(file, new[] { 1, 3 });

        Assert.Equal(new[] { 1, 3 }, file.Sources.Select(s => s.SourceId));   // 这一档会跟着源数长大，没人读的就不是缓存
    }

    /// <summary>路径必须跟着 settings.json 那一套环境变量走：测试整体重定向、以及"同目录才能一起备份"都靠它。</summary>
    [Fact]
    public void TheCacheFileSitsNextToSettingsJson()
    {
        var (settings, db) = (Environment.GetEnvironmentVariable("STARMARK_SETTINGS_PATH"),
                              Environment.GetEnvironmentVariable("STARMARK_DB_PATH"));
        try
        {
            Environment.SetEnvironmentVariable("STARMARK_DB_PATH", null);
            Environment.SetEnvironmentVariable("STARMARK_SETTINGS_PATH", Path.Combine(_dir, "sub", "settings.json"));
            Assert.Equal(Path.Combine(_dir, "sub", "rss-cache.json"), RssCacheStore.DefaultPath());

            Environment.SetEnvironmentVariable("STARMARK_SETTINGS_PATH", null);
            Environment.SetEnvironmentVariable("STARMARK_DB_PATH", Path.Combine(_dir, "db", "starmark.db"));
            Assert.Equal(Path.Combine(_dir, "db", "rss-cache.json"), RssCacheStore.DefaultPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable("STARMARK_SETTINGS_PATH", settings);
            Environment.SetEnvironmentVariable("STARMARK_DB_PATH", db);
        }
    }

    // ────────── 界面要说的那两句 ──────────

    /// <summary>从缓存摆出来时必须说清"这是上次抓的、什么时候抓的、下一次什么时候自动再来"：
    /// 一个每天只抓一次的页面如果只写"共 20 条"，用户会以为它是实时的，看不到新文章就判定功能坏了。</summary>
    [Theory]
    [InlineData(Now - 30, "刚刚")]
    [InlineData(Now - 10 * 60, "10 分钟前")]
    [InlineData(Now - 5 * 3600, "5 小时前")]
    [InlineData(Now - 2 * Day, "2 天前")]
    public void TheCacheLineSaysHowOldItIs(long fetchedAt, string expectedAge)
    {
        var text = RssSourceStatus.FromCache(Cached(fetchedAt, Entry("https://a.test/x"), Entry("https://a.test/y")), Now);
        Assert.Contains(expectedAge, text);
        Assert.Contains("共 2 条", text);
    }

    [Fact]
    public void TheCacheLineTellsTheUserWhenTheNextAutomaticRoundComes()
    {
        Assert.Contains("现在到期，会自动补抓",
            RssSourceStatus.FromCache(Cached(Now - Day, Entry("https://a.test/x")), Now));
        Assert.Contains("约 1 小时后自动刷新",
            RssSourceStatus.FromCache(Cached(Now - 23 * 3600, Entry("https://a.test/x")), Now));
    }

    /// <summary>增量刷新之后，"本轮抓到几条"与"这一组现在摆着几条"是两个数。
    /// 只报前者会被读成"这个源一共就 8 条"，用户以为缓存把内容弄丢了。</summary>
    [Fact]
    public void TheRoundLineDistinguishesWhatItFetchedFromWhatIsOnScreen()
    {
        var outcome = new RssSourceOutcome(Source(), new[] { Entry("https://a.test/x") });
        Assert.Contains("抓到 1 条，缓存共 20 条", RssSourceStatus.Describe(outcome, 20));
        Assert.Equal("抓到 1 条", RssSourceStatus.Describe(outcome, 1));        // 相等时不重复一遍数字
        Assert.Equal("抓到 1 条", RssSourceStatus.Describe(outcome));           // 没有缓存这个数时保持原样
    }

    /// <summary>失败/304/停止那三种"这一轮没拿到东西"如果界面仍显示着上次那一列，就得说清"还摆着缓存里的 N 条"，
    /// 否则看上去像内容被清空了。五种状态本身不许被这半句糊掉。</summary>
    [Fact]
    public void AQuietRoundStillSaysWhatIsStillOnScreen()
    {
        var stopped = new RssSourceOutcome(Source(), Array.Empty<RssEntry>(), Stopped: true);
        var failed = new RssSourceOutcome(Source(), Array.Empty<RssEntry>(), "对方返回 503");
        var unchanged = new RssSourceOutcome(Source(), Array.Empty<RssEntry>(), NotModified: true);

        Assert.Contains("仍然摆着缓存里的 9 条", RssSourceStatus.Describe(stopped, 9));
        Assert.Contains("失败：对方返回 503", RssSourceStatus.Describe(failed, 9));
        Assert.Contains("没有新内容（源说未变化）", RssSourceStatus.Describe(unchanged, 9));

        // 那半句不许把五种状态互相抹平（批次 NF 的教训：分不开就不知道该改地址还是查网络）
        var disabled = new RssSourceOutcome(new RssSourceConfig(1, "源", "https://a.test/feed", false), Array.Empty<RssEntry>());
        var five = new[]
        {
            RssSourceStatus.Describe(null),
            RssSourceStatus.Describe(stopped, 9),
            RssSourceStatus.Describe(disabled, 9),
            RssSourceStatus.Describe(unchanged, 9),
            RssSourceStatus.Describe(failed, 9),
        };
        Assert.Equal(five.Distinct().Count(), five.Length);
        Assert.All(five, line => Assert.NotEqual(string.Empty, line));   // 空标题看着像渲染坏了
    }
}
