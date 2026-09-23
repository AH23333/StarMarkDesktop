#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions.Feed;
using StarMark.Core.Feed;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 网址来源（RSS / Atom）解析器的判据测试。
/// <para>
/// 订阅文档是<b>别人家的输出</b>：字段缺失、命名空间前缀乱起、时间三种写法、摘要塞 HTML，
/// 每一项都是真实存在的源形状。这些只能逐条钉住——"看着像能跑"的解析器，
/// 上线后的表现是"某个源恰好是空的"，而用户只会认为程序坏了。
/// </para>
/// </summary>
public sealed class RssParserTests
{
    private static readonly RssSourceConfig Source = new(7, "示例源", "https://blog.example.com/feed");

    private static RssParseResult Parse(string xml) => RssParser.Parse(xml, Source);

    // ────────── RSS 2.0 ──────────

    [Fact]
    public void ReadsRssChannelAndItems()
    {
        var result = Parse("""
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel>
              <title>Example Blog</title>
              <item><title>第一篇</title><link>https://blog.example.com/a</link>
                    <pubDate>Fri, 02 Jan 2026 15:04:05 GMT</pubDate>
                    <dc:creator xmlns:dc="http://purl.org/dc/elements/1.1/">张三</dc:creator>
                    <description>&lt;p&gt;这是&lt;b&gt;摘要&lt;/b&gt;&lt;/p&gt;</description></item>
              <item><title>第二篇</title><link>https://blog.example.com/b</link></item>
            </channel></rss>
            """);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("Example Blog", result.FeedTitle);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("第一篇", result.Entries[0].Title);
        Assert.Equal("https://blog.example.com/a", result.Entries[0].Link);
        Assert.Equal("张三", result.Entries[0].Author);          // dc:creator 只认本地名，前缀不算身份
        Assert.Equal("这是 摘要", result.Entries[0].Summary);
        Assert.Equal(2026, result.Entries[0].PublishedAt!.Value.Year);
        Assert.Null(result.Entries[1].PublishedAt);               // 没给时间就是没有，不拿"现在"冒充
        Assert.Equal(7, result.Entries[0].SourceId);
        Assert.Equal("示例源", result.Entries[0].SourceName);
    }

    [Fact]
    public void EmptyTitleFallsBackToLinkThenSourceName()
    {
        var result = Parse("""
            <rss><channel>
              <item><title>   </title><link>https://blog.example.com/very/long/path/that/keeps/going</link></item>
              <item><title></title><link></link></item>
            </channel></rss>
            """);
        Assert.True(result.Ok);
        Assert.StartsWith("https://blog.example.com/very", result.Entries[0].Title);
        Assert.Equal("示例源", result.Entries[1].Title);          // 一行空白条目在列表里等于"程序坏了"
    }

    // ────────── Atom ──────────

    [Fact]
    public void ReadsAtomAndPicksTheAlternateLinkNotTheIcon()
    {
        var result = Parse("""
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Atom 示例</title>
              <entry>
                <title>带图标的条目</title>
                <link rel="icon" href="https://blog.example.com/icon.png"/>
                <link rel="alternate" href="/posts/1"/>
                <updated>2026-03-04T10:00:00+08:00</updated>
                <author><name>李四</name></author>
                <summary>只有摘要的一条</summary>
              </entry>
            </feed>
            """);
        Assert.True(result.Ok, result.Error);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("https://blog.example.com/posts/1", entry.Link);   // 相对地址按源补全；不能拿 icon 当正文
        Assert.Equal("李四", entry.Author);
        Assert.Equal("只有摘要的一条", entry.Summary);                   // 没有 content 时退回 summary
        Assert.Equal(2026, entry.PublishedAt!.Value.Year);
        Assert.Equal(3, entry.PublishedAt!.Value.Month);                 // 带偏移的 ISO 时间要按它自己的偏移解释
        Assert.Equal(4, entry.PublishedAt!.Value.Day);
    }

    [Fact]
    public void AtomLinkWithoutRelIsTreatedAsAlternate()
    {
        var result = Parse("""
            <feed xmlns="http://www.w3.org/2005/Atom"><entry>
              <title>裸 link</title><link href="https://blog.example.com/naked"/>
            </entry></feed>
            """);
        Assert.Equal("https://blog.example.com/naked", Assert.Single(result.Entries).Link);
    }

    // ────────── 时间 ──────────

    [Theory]
    [InlineData("Fri, 02 Jan 2026 15:04:05 GMT", 2026, 1, 2)]
    [InlineData("Tue, 02 Jun 2026 08:00:00 +0800", 2026, 6, 2)]
    [InlineData("2026-07-08T09:10:11Z", 2026, 7, 8)]
    [InlineData("2026-07-08T09:10:11.500+02:00", 2026, 7, 8)]
    public void AcceptsTheTwoDateShapesFeedsActuallyUse(string raw, int year, int month, int day)
    {
        var parsed = RssParser.ParseDate(raw);
        Assert.NotNull(parsed);
        Assert.Equal(year, parsed!.Value.Year);
        Assert.Equal(month, parsed.Value.Month);
        Assert.Equal(day, parsed.Value.Day);
    }

    [Fact]
    public void AWrongWeekdayNameDoesNotLoseTheWholeDate()
    {
        // 源方把周五写成 "Mon" 是真实存在的笔误。星期名是冗余信息，
        // 带着它解析会让整条时间失败，用户只看到"这条没有日期"，而原因藏在别人的笔误里。
        var parsed = RssParser.ParseDate("Mon, 02 Jan 2026 15:04:05 GMT");
        Assert.NotNull(parsed);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 15, 4, 5, TimeSpan.Zero), parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("昨天")]
    [InlineData("14032026")]
    [InlineData("sometime last week")]
    public void UnparseableDatesStayNull(string raw) => Assert.Null(RssParser.ParseDate(raw));

    // ────────── 摘要 ──────────

    [Fact]
    public void SummaryDropsTagsScriptsAndKeepsLiteralEntities()
    {
        var clean = RssParser.Clean("<p>正文</p><script>var x=1;</script><style>a{}</style> 继续 &amp;amp; 结束");
        Assert.DoesNotContain("var x=1", clean);
        Assert.DoesNotContain("a{}", clean);
        Assert.DoesNotContain("<p>", clean);
        Assert.Equal("正文 继续 &amp; 结束", clean);       // HTML 实体只解一层：&amp;amp; → &amp;，不再继续解
    }

    [Fact]
    public void SummaryIsCappedSoASourceCannotSizeOurMemory()
    {
        var longText = new string('字', RssParser.MaxSummaryChars + 200);
        var clean = RssParser.Clean(longText);
        Assert.Equal(RssParser.MaxSummaryChars + 1, clean.Length);   // 截断 + 一个省略号
        Assert.EndsWith("…", clean);
    }

    // ────────── 去重 / 上限 ──────────

    [Fact]
    public void DuplicateLinksKeepTheFirstOccurrence()
    {
        var result = Parse("""
            <rss><channel>
              <item><title>新</title><link>https://x.test/A</link></item>
              <item><title>旧的同链接</title><link>https://x.test/a</link></item>
              <item><title>另一条</title><link>https://x.test/B</link></item>
            </channel></rss>
            """);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("新", result.Entries[0].Title);                 // 多数源把最新放最前，后来者不许覆盖
    }

    [Fact]
    public void EntriesBeyondTheCapAreDroppedDuringParsing()
    {
        var items = string.Concat(Enumerable.Range(0, RssSourceConfig.MaxEntries + 10)
            .Select(i => $"<item><title>第 {i} 条</title><link>https://x.test/{i}</link></item>"));
        var result = Parse($"<rss><channel>{items}</channel></rss>");
        Assert.Equal(RssSourceConfig.MaxEntries, result.Entries.Count);
    }

    // ────────── 安全与坏输入 ──────────

    [Theory]
    [InlineData("<!DOCTYPE rss [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]><rss><channel>&x;</channel></rss>")]
    [InlineData("<!DOCTYPE rss><rss><channel><item><title>a</title><link>https://x.test</link></item></channel></rss>")]
    [InlineData("<rss><!ENTITY evil \"x\"><channel/></rss>")]
    public void DocumentsWithDtdOrEntitiesAreRefusedOutright(string xml)
    {
        // 订阅地址是用户输入的，返回内容不可信；DTD/实体是唯一能让解析器"去读别处"的入口
        var result = Parse(xml);
        Assert.False(result.Ok);
        Assert.Contains("DTD", result.Error);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void APlainWebPageIsReportedAsNotAFeed()
    {
        var result = Parse("<html><body><p>这不是订阅</p></body></html>");
        Assert.False(result.Ok);
        Assert.Contains("不是 RSS", result.Error);
        Assert.Contains("订阅地址", result.Error);           // 要给出口：用户多半是把网页地址当订阅地址填了
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    public void NothingComesBackWhenTheSourceReturnedNothing(string? xml)
    {
        var result = Parse(xml!);
        Assert.False(result.Ok);
        Assert.Contains("没有返回任何内容", result.Error);
    }

    [Fact]
    public void MalformedXmlSaysSoInsteadOfReturningAnEmptyList()
    {
        var result = Parse("<rss><channel><item><title>忘了闭合");
        Assert.False(result.Ok);
        Assert.Contains("XML", result.Error);
    }

    [Fact]
    public void AbsurdlyLargeDocumentsAreRefused()
    {
        var result = RssParser.Parse(new string('x', RssParser.MaxDocumentChars + 1), Source);
        Assert.False(result.Ok);
        Assert.Contains("MB", result.Error);
    }

    [Fact]
    public void AnEmptyButValidFeedIsNotAnError()
    {
        // "这个源现在没有内容"与"这个源坏了"是两件事，混起来用户就不会去修地址了
        var result = Parse("<rss><channel><title>空频道</title></channel></rss>");
        Assert.True(result.Ok);
        Assert.Empty(result.Entries);
        Assert.Equal("空频道", result.FeedTitle);
    }

    // ────────── 源配置本身 ──────────

    [Theory]
    [InlineData(null, true)]
    [InlineData("   ", true)]
    [InlineData("blog.example.com", true)]              // 没写 scheme：Uri 会当相对地址，抓不了
    [InlineData("ftp://blog.example.com/feed", true)]   // 协议不对要说清是哪个
    [InlineData("javascript:alert(1)", true)]
    [InlineData("https://blog.example.com/feed", false)]
    [InlineData("http://localhost:8080/feed.xml", false)]
    public void OnlyHttpAndHttpsAreWorthFetching(string? url, bool rejected)
    {
        var problem = RssSourceConfig.UrlProblem(url);
        Assert.Equal(rejected, problem is not null);
        if (rejected && url is not null && url.Contains("ftp")) Assert.Contains("ftp", problem);
    }

    [Theory]
    [InlineData("https://www.example.com/feed", "example.com")]
    [InlineData("https://blog.example.com/x", "blog.example.com")]
    [InlineData("not a url", "not a url")]              // 兜底也不能给空白
    public void FallbackNameUsesTheHost(string url, string expected)
        => Assert.Equal(expected, RssSourceConfig.FallbackName(url));
}
