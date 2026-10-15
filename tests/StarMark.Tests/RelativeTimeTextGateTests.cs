#nullable enable
using System;
using System.IO;
using System.Linq;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「多久以前」四档的接线守门（批次 SJ，P-131 清单 #5）。
/// <para>要守的是两件事：<b>①措辞只有一颗</b>（四家宿主各自写一份就会分岔出"差一小时／差一天"，本批实测就是这么坏的）；
/// <b>②档位各归各的宿主</b>——热榜与 RSS 那两页刻意把"刚刚"的上限放宽到两分钟，那是它们的判断，
/// 谁把两处一起"统一"成 60 秒也会在这里红（两向都钉＝#189 的教训）。</para>
/// <para>禁项一律读<b>抹掉注释后的代码</b>（<see cref="SourceGate.Code"/>，#195/#200/#201）：
/// 注释里引用一句旧措辞是为了讲清为什么，不是又开一个出口。</para>
/// </summary>
public sealed class RelativeTimeTextGateTests
{
    // 批次 WI-2k 起这一族有两位主人，而且必须分开钉：**措辞**住在界面文案表，**档位与取整**住在判据那颗。
    private const string Judge = "src/StarMark.Abstractions/DateTimeText.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";
    private const string Helper = "src/StarMark.UI/Helpers/RelativeTimeHelper.cs";
    private const string ActivityVm = "src/StarMark.UI/ViewModels/ActivityItemViewModel.cs";
    private const string CardVm = "src/StarMark.UI/ViewModels/ItemCardViewModel.cs";
    private const string Trending = "src/StarMark.Abstractions/Trending/TrendingCacheCodec.cs";
    private const string RssStatus = "src/StarMark.Core/Feed/RssSourceStatus.cs";

    /// <summary>✅WI-65 片1 起，四档判据的<b>本体</b>住在这颗「还没取词」的孪生里（<c>Relative</c> 只剩一行转发）。</summary>
    private const string Twin = "public static LocMessage RelativeMessage(long unixSeconds, DateTimeOffset now, TimeZoneInfo zone)";

    private static readonly string[] Phrases = { "刚刚", "分钟前", "小时前", "天前" };

    /// <summary>四档措辞在<b>整个 src 的代码里</b>各只出现一次，而且就在界面文案表那一颗上（批次 WI-2k 起的主人）。</summary>
    [Fact]
    public void EachWordingBucketExistsNowhereButTheJudge()
    {
        var files = ReadRepoUnder("src");
        Assert.True(files.Count >= 200, $"只扫到 {files.Count} 个源文件（普查路径错了＝闸门在空转）");
        foreach (var phrase in Phrases)
        {
            var hits = files
                .Select(f => (f.RelativePath, Count(Code(f.Text), phrase)))
                .Where(f => f.Item2 > 0)
                .ToList();
            Assert.True(hits.Count == 1,
                $"「{phrase}」在 {hits.Count} 个文件的代码里出现（应只剩文案表一颗）：{string.Join("、", hits)}");
            Assert.Equal(Table, hits[0].RelativePath);
            Assert.Equal(1, hits[0].Item2);
        }
    }

    /// <summary>
    /// 判据那颗<b>只留键与档位</b>：字搬进表之后，如果代码里又出现一句中文措辞，就是"两份可各自被改"回来了
    /// （批次 WI-2k 之所以要有这一格——搬字这件事最容易搬成"表里有、代码里也留一份兜底"）。
    /// </summary>
    [Fact]
    public void TheJudgeHoldsKeysNotWords()
    {
        var code = Code(ReadRepoFile(Judge));
        foreach (var phrase in Phrases) Assert.DoesNotContain(phrase, code, StringComparison.Ordinal);
        foreach (var key in new[] { "time.justNow", "time.minutesAgo", "time.hoursAgo", "time.daysAgo" })
            Assert.Contains(key, code, StringComparison.Ordinal);
        // ✅WI-65 片5c：这四档只剩「交键」那一条出口（从前并存的"当场念完"那四颗生产侧无人消费，已连根拔掉）
        // ⇒ 这一格从"两条路都问表"改成"只问那一条路"，⛔ 留着钉一颗已经不存在的函数（那只会静默变成永远成立）。
        Assert.Contains("LocMessage.Of(JustNowKey)", code, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(MinutesAgoKey, count)", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// 那四颗「当场念完」的档出口删掉之后⛔ 回来（批次 WI-65 片5c）。
    /// <para>它们不是"多余的便利"而是<b>第二个主人</b>：谁把 <c>HoursAgo(3)</c> 塞进另一句的 <c>{0}</c> 槽，
    /// 那句话就从此钉死在一句旧语言上（坑表 #393／P-196 那一族）——留一条看起来无害的 <c>string</c> 出口，
    /// 下一次半搬就有了一条正当路径。四颗 <c>*Message</c> 各一颗，是这一族唯一的出口。</para>
    /// </summary>
    [Fact]
    public void TheRenderedBucketExitsStayDeleted()
    {
        var code = Code(ReadRepoFile(Judge));
        foreach (var dead in new[] { "public static string JustNow", "public static string MinutesAgo(",
                                    "public static string HoursAgo(", "public static string DaysAgo(" })
            Assert.DoesNotContain(dead, code, StringComparison.Ordinal);
        foreach (var one in new[] { "public static LocMessage JustNowMessage()", "public static LocMessage MinutesAgoMessage(long count)",
                                    "public static LocMessage HoursAgoMessage(long count)", "public static LocMessage DaysAgoMessage(long count)" })
            Assert.Equal(1, Count(code, one));
        // ⚠ 「壳只剩一行转发」那颗格（<see cref="TheRenderedWrapperIsNothingButADelegateToTheUnrenderedTwin"/>）
        // 守的是 <c>Relative</c> 这一颗——它是活动列表与卡片两处的真宿主，⛔ 与上面那四颗同罪同罚。
        Assert.Equal(1, Count(code, "public static string Relative(long unixSeconds"));
    }

    [Fact]
    public void NoXamlPageWritesThoseWordsItself()
    {
        var xaml = Directory
            .GetFiles(Path.Combine(RepoRoot(), "src"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Replace(Path.DirectorySeparatorChar, '/').Contains("/obj/")
                        && !f.Replace(Path.DirectorySeparatorChar, '/').Contains("/bin/"))
            .ToList();
        Assert.True(xaml.Count >= 30, $"只扫到 {xaml.Count} 个 xaml（扫描器没跑起来，别把这条当绿）");
        Assert.All(xaml, f =>
        {
            foreach (var phrase in Phrases)
                Assert.DoesNotContain(phrase, File.ReadAllText(f), StringComparison.Ordinal);
        });
    }

    /// <summary>四个宿主各自的方法体里真的问了判据（#196：整文件 Contains 会被同文件另一处合法用法顶住）。</summary>
    [Fact]
    public void EveryHostAsksTheJudgeFromInsideItsOwnMethod()
    {
        Assert.Contains("DateTimeText.Relative(", MethodBody(ReadRepoFile(Helper), "public static string Format(long unixSeconds)"));
        // ⚠ 这一格的锚点从<b>构造函数</b>搬到<b>显示侧 getter</b>（批次 WI-67 片9c 第六刀）：
        // 构造函数里取一次词＝把语言冻在那一刻，宿主那一拍重念的是 getter，屏幕上就剩「事件名换了、时间没换」那半行。
        Assert.Contains("RelativeTimeHelper.Format(", MethodBody(ReadRepoFile(ActivityVm), "public string AtText =>"));
        Assert.DoesNotContain("RelativeTimeHelper.Format(", MethodBody(ReadRepoFile(ActivityVm), "public ActivityItemViewModel(ActivityRecord rec)"));
        Assert.Contains("RelativeTimeHelper.Format(", MethodBody(ReadRepoFile(CardVm), "public string RelativeTime =>"));
        // 签名交回的是**还没取词**的那句（LocMessage?）——这一格按现场形状重定锚（坑表 #278：改了被守的那份文件，
        // 判据的锚点必须跟着改，且那一格要带重编重跑）。
        var trending = MethodBody(ReadRepoFile(Trending), "public static LocMessage? DescribeAge(long fetchedAtUnixSeconds, DateTimeOffset now)");
        Assert.Contains("DateTimeText.JustNowMessage()", trending);
        Assert.Contains("DateTimeText.HoursAgoMessage(", trending);
        // ✅WI-65 片2：这一档的 FromCache 也换成交「键＋实参」，锚点跟着签名搬（坑表 #278/#484：改了被守的那份文件，
        // 守门要先跟上——⛔ 留它读那颗已经不存在的旧签名，更⛔ 让它静默变成永远成立）。
        var rss = MethodBody(ReadRepoFile(RssStatus), "public static LocMessage FromCache(RssCachedSource cached, long nowUnix)");
        Assert.Contains("DateTimeText.JustNowMessage()", rss);
        Assert.Contains("DateTimeText.DaysAgoMessage(", rss);
    }

    /// <summary>活动列表那第二份 if 链<b>整条消失</b>，不留转发壳（SF 的口径：零读者的壳只会让人以为还有两处）。</summary>
    [Fact]
    public void TheActivityListKeepsNoTraceOfItsOwnLadder()
    {
        var vm = Code(ReadRepoFile(ActivityVm));
        Assert.DoesNotContain("FormatRelative", vm);
        Assert.DoesNotContain("TotalMinutes", vm);
        Assert.DoesNotContain("TotalSeconds", vm);
        Assert.DoesNotContain("DateTimeText.Day(", vm);
    }

    /// <summary>
    /// 判据算日期时<b>必须按交进来的时区换算</b>——直接把 <c>FromUnixTimeSeconds</c> 那个零偏移值交给
    /// <see cref="DateTimeText.Day(DateTimeOffset)"/> 就是本批修掉的那条一天差。
    /// </summary>
    [Fact]
    public void TheJudgeConvertsBeforeTakingTheDate()
    {
        // ✅WI-65 片1：判据本体搬进了那颗「还没取词」的孪生，<c>Relative</c> 只剩一行转发
        // （坑表 #278 同族：改了被守的那份文件，锚点必须跟着搬——⛔ 留这一格去读那颗壳）。
        var body = MethodBody(ReadRepoFile(Judge), Twin);
        Assert.Contains("TimeZoneInfo.ConvertTime(", body);
        Assert.DoesNotContain("Day(DateTimeOffset.FromUnixTimeSeconds", body);
        Assert.DoesNotContain("TimeZoneInfo.Local", body);      // 判据不许自己偷本机时区：那样用例就测不到跨日
    }

    /// <summary>「念完那一版」现在只是那颗孪生的薄壳：它的身体里⛔ 再长出第二份档位梯。</summary>
    [Fact]
    public void TheRenderedWrapperIsNothingButADelegateToTheUnrenderedTwin()
    {
        var shell = MethodBody(ReadRepoFile(Judge), "public static string Relative(long unixSeconds, DateTimeOffset now, TimeZoneInfo zone)");
        Assert.Contains("=> RelativeMessage(unixSeconds, now, zone).Render();", shell);
        // 上面三格钉的都是「本体在这颗孪生里」；这里补的是「壳⛔ 也偷偷算一遍」——换算、阈值、取词一个都不许出现在壳里
        // （⚠ 方法体是从<b>签名那一行</b>切起的，所以禁项要点名到"只有算过才会出现"的那一串，⛔ 光写 TimeZoneInfo）。
        foreach (var was in new[] { "TimeZoneInfo.ConvertTime", "AppConstants", "FromUnixTimeSeconds", "Locale.", "diff <" })
            Assert.DoesNotContain(was, shell);
    }

    /// <summary>
    /// 两处"两分钟内都算刚刚"是<b>宿主的选择</b>，判据那颗是 60 秒——两个方向都要钉：
    /// 放宽被改小 ⇒ 本条红；判据被放宽成两分钟 ⇒ 本条也红（"顺手统一"要先把这个决定说清楚）。
    /// </summary>
    [Fact]
    public void TheTwoMinuteCeilingStaysWithThoseTwoHosts()
    {
        Assert.Equal(1, Count(Code(ReadRepoFile(Trending)), "TimeSpan.FromMinutes(2)"));
        Assert.Equal(1, Count(Code(ReadRepoFile(RssStatus)), "TimeSpan.FromMinutes(2)"));
        Assert.DoesNotContain("FromMinutes(2)", MethodBody(ReadRepoFile(Judge), Twin));
    }

    /// <summary>阈值那颗常数仍由判据用（不是抄成 2_592_000 字面量——那是 RZ 那批"字节梯子四份抄本"的同款病）。</summary>
    [Fact]
    public void TheThirtyDayThresholdComesFromTheConstant()
    {
        var body = MethodBody(ReadRepoFile(Judge), Twin);
        Assert.Contains("AppConstants.ThirtyDaysInSeconds", body);
        Assert.DoesNotContain("2_592_000", body);
        Assert.DoesNotContain("2592000", body);
    }

    /// <summary>
    /// <b>「unix 瞬间直接交给格式化器」这一形状全仓不许有</b>（坑表 #202）。
    /// <para>本批在它上面修掉了两处：活动列表那句相对时间、设置页备份摘要那句"导出时间"。
    /// <c>FromUnixTimeSeconds</c> 交出来的偏移<b>恒为 0</b>，而 <c>Day(DateTimeOffset)</c>／<c>Minute(DateTimeOffset)</c>
    /// 是"按自带偏移格式化"——少写一次换算就是把 UTC 当本地，而且只在本地 00:00–08:00 那段里差一天，
    /// 属于最难报障的那一类。写成正则而不是数字，是为了放过<b>已经换算过</b>的两种写法
    /// （<c>…FromUnixTimeSeconds(x).LocalDateTime</c>／<c>….ToLocalTime()</c>，后者就是快照页那颗）
    /// 与判据那颗 <c>ConvertTime(…)</c>——第一版只放过了 <c>.LocalDateTime</c>，当场把快照页误伤成违规。</para>
    /// </summary>
    [Fact]
    public void AUnixInstantIsNeverHandedToTheFormatterUnconverted()
    {
        var unconverted = new System.Text.RegularExpressions.Regex(
            "(?:Day|Minute)\\(DateTimeOffset\\.FromUnixTimeSeconds\\([^()]*\\)(?!\\.LocalDateTime|\\.ToLocalTime\\(\\))");
        foreach (var (path, text) in ReadRepoUnder("src"))
        {
            var hit = unconverted.Match(Code(text));
            Assert.False(hit.Success,
                $"{path} 把一个 unix 瞬间直接交给了格式化（「{hit.Value}」）：先换算成本地或交进时区，否则显示的是 UTC");
        }
    }
}
