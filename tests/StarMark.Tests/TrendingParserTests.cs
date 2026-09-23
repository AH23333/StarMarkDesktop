#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using StarMark.Core.Trending;

namespace StarMark.Tests;

/// <summary>
/// GitHub 热榜页解析器的契约护栏。
/// <para>
/// 这些断言大部分不是"我想要的形状"，而是<b>扩展在生产里踩过后修出来的形状</b>
/// （<c>src/core/trending.ts</c> 的 2026-09 修复注释）：
/// 主链接必须在 <c>&lt;h2&gt;</c> 内、星数要先剥标签、描述只认 <c>col-9</c>。
/// 逐条钉住的原因与 IH 轮同：<b>抄来的前车之鉴只抄一半也会全绿</b>——
/// 没有断言的修复，下一次"看着更简洁的改写"就会把坑原样挖回来，而页面抓取只在真机跑，构建照不出来。
/// </para>
/// </summary>
public sealed class TrendingParserTests
{
    /// <summary>一页两条正常结果：字段逐一对上，顺序＝页面顺序（热榜排名有语义，不许重排）。</summary>
    [Fact]
    public void Parse_TwoNormalRepos_MapsEveryField()
    {
        var html = Article("openclaw/openclaw", desc: "AI 助手 <b>入口</b>", lang: "TypeScript",
                           stars: "12,345", today: "230 stars today")
                   + Article("vuejs/core", desc: "Vue.js core repo", lang: null,
                           stars: "8,000", today: "12 stars this week");

        var list = TrendingHtmlParser.Parse(html);

        Assert.Equal(2, list.Count);
        Assert.Equal("openclaw/openclaw", list[0].FullName);
        Assert.Equal("https://github.com/openclaw/openclaw", list[0].Url);
        Assert.Equal("AI 助手 入口", list[0].Description);
        Assert.Equal("TypeScript", list[0].Language);
        Assert.Equal(12345, list[0].Stars);
        Assert.Equal(230, list[0].StarsToday);
        Assert.Null(list[1].Language);
        Assert.Equal(12, list[1].StarsToday);
    }

    /// <summary>
    /// 修复①：article 里<b>第一个</b>链接是 stargazers 之类的辅助链接时，主链接仍取 <c>&lt;h2&gt;</c> 内那条。
    /// 旧写法（取第一个 <c>&lt;a&gt;</c>）会把整条过滤掉 ⇒ 现象是"爬取不全"。
    /// </summary>
    [Fact]
    public void Parse_IgnoreAuxiliaryLinksBeforeHeading()
    {
        var html = "<article><div class=\"d-flex\">"
                 + "<a href=\"/octocat/hello/stargazers\">1.2k</a>"
                 + "<a href=\"/octocat/hello/forks\">30</a>"
                 + "</div>"
                 + "<h2 class=\"h3\"><a href=\"/octocat/hello\">octocat / hello</a></h2>"
                 + "</article>";

        var repo = Assert.Single(TrendingHtmlParser.Parse(html));

        Assert.Equal("octocat/hello", repo.FullName);
        Assert.Equal("octocat", repo.Owner);
        Assert.Equal("hello", repo.Repo);
    }

    /// <summary>多段路径（<c>/o/r/stargazers</c>）不能被当成 <c>owner/repo</c>；认不出主链接就整条丢弃。</summary>
    [Fact]
    public void Parse_RejectsMultiSegmentHeading()
    {
        var html = "<article><h2><a href=\"/octocat/hello/stargazers\">x</a></h2></article>";

        Assert.Empty(TrendingHtmlParser.Parse(html));
    }

    /// <summary>
    /// 修复③：内置 Star 按钮那段 <c>&lt;p&gt;</c> 的文案不得混进描述——描述只认 <c>col-9</c> 段，<b>不回退</b>到任意 <c>&lt;p&gt;</c>。
    /// </summary>
    [Fact]
    public void Parse_NeverFallsBackToOtherParagraphs()
    {
        var html = "<article><h2><a href=\"/o/r\">o / r</a></h2>"
                 + "<p class=\"d-inline-flex\"> Star </p>"
                 + "<p>按钮文案不能当描述</p>"
                 + "</article>";

        Assert.Equal(string.Empty, Assert.Single(TrendingHtmlParser.Parse(html)).Description);
    }

    /// <summary>修复②：星数在数字前常夹一截 <c>&lt;svg&gt;</c>，必须先剥标签再取数字。</summary>
    [Fact]
    public void Parse_StripsSvgBeforeStarCount()
    {
        var html = "<article><h2><a href=\"/o/r\">o / r</a></h2>"
                 + "<a href=\"/o/r/stargazers\"><svg aria-hidden=\"1\" viewBox=\"0 0 16 16\"><path d=\"M2\"></path></svg> 42,113</a>"
                 + "</article>";

        Assert.Equal(42113, Assert.Single(TrendingHtmlParser.Parse(html)).Stars);
    }

    /// <summary>缺字段逐条兜底：描述空串、语言 null、星数 0、<b>本期新增为 null（不是 0）</b>。</summary>
    [Fact]
    public void Parse_MissingFieldsFallBackToUnknownNotZero()
    {
        var html = "<article><h2><a href=\"/o/r\">o / r</a></h2></article>";

        var repo = Assert.Single(TrendingHtmlParser.Parse(html));

        Assert.Equal(string.Empty, repo.Description);
        Assert.Null(repo.Language);
        Assert.Equal(0, repo.Stars);
        Assert.Null(repo.StarsToday);
    }

    /// <summary>零条目＝页面改版或被反爬拦成验证页，必须被当成"解析失败"（空列表），不能伪装成"今天没有热榜"。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><body><p>We noticed unusual traffic</p></body></html>")]
    public void Parse_EmptyResultMeansFailure(string? html)
        => Assert.Empty(TrendingHtmlParser.Parse(html));

    /// <summary>被截断的尾块（没有 <c>&lt;/article&gt;</c>）跳过，不能拿半截内容当一条。</summary>
    [Fact]
    public void Parse_SkipsUnterminatedTrailingBlock()
    {
        var html = Article("good/one", "d", "Go", "10", "3 stars today")
                 + "<article><h2><a href=\"/bad/two\">b / t</a></h2>";   // 无闭合标签

        Assert.Equal("good/one", Assert.Single(TrendingHtmlParser.Parse(html)).FullName);
    }

    /// <summary>描述里的实体与空白：解码 + 折叠成单行（换行会把卡片撑坏）。</summary>
    [Fact]
    public void Parse_DecodesEntitiesAndCollapsesWhitespace()
    {
        var html = "<article><h2><a href=\"/o/r\">o / r</a></h2>"
                 + "<p class=\"col-9\">a &amp; b&#39;s\n   <em>c</em>&nbsp;d &#x1F600;</p>"
                 + "</article>";

        Assert.Equal("a & b's c d \U0001F600", Assert.Single(TrendingHtmlParser.Parse(html)).Description);
    }

    /// <summary>非法码位（外部 HTML 里是常态）不能抛异常，也不能把整段描述丢掉。</summary>
    [Fact]
    public void Parse_ToleratesInvalidCharacterReference()
    {
        var html = "<article><h2><a href=\"/o/r\">o / r</a></h2>"
                 + "<p class=\"col-9\">ok &#x110000; tail</p></article>";

        Assert.StartsWith("ok", Assert.Single(TrendingHtmlParser.Parse(html)).Description);
    }

    // ===== 请求与缓存的形状（都是纯函数，键/URL 写错只会表现为"永远抓不到"或"永远读旧数据"） =====

    [Theory]
    [InlineData(TrendingPeriod.Daily, null, "https://github.com/trending?since=daily")]
    [InlineData(TrendingPeriod.Weekly, null, "https://github.com/trending?since=weekly")]
    [InlineData(TrendingPeriod.Monthly, "rust", "https://github.com/trending/rust?since=monthly")]
    [InlineData(TrendingPeriod.Weekly, "C++", "https://github.com/trending/c%2B%2B?since=weekly")]
    public void BuildTrendingUrl_Shape(TrendingPeriod period, string? language, string expected)
        => Assert.Equal(expected, TrendingHtmlParser.BuildTrendingUrl(period, language));

    /// <summary>兜底查询：窗口天数取自 <see cref="TrendingPeriods"/>（日榜给 2 天，1 天必然空）。</summary>
    [Theory]
    [InlineData(TrendingPeriod.Daily, "2026-09-21")]
    [InlineData(TrendingPeriod.Weekly, "2026-09-16")]
    [InlineData(TrendingPeriod.Monthly, "2026-08-24")]
    public void BuildSearchQuery_WindowFollowsPeriod(TrendingPeriod period, string expectedSince)
    {
        var query = TrendingHtmlParser.BuildSearchQuery(period, null,
            new DateTimeOffset(2026, 9, 23, 1, 0, 0, TimeSpan.Zero));

        Assert.Contains(Uri.EscapeDataString($"created:>{expectedSince} stars:>50"), query);
        Assert.Contains("sort=stars", query);
        Assert.Contains("order=desc", query);
        Assert.Contains("per_page=25", query);
    }

    [Fact]
    public void BuildSearchQuery_AddsLanguageAndTrimsIt()
    {
        var query = TrendingHtmlParser.BuildSearchQuery(TrendingPeriod.Weekly, "  TypeScript ",
            new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains(Uri.EscapeDataString("created:>2026-09-16 language:TypeScript stars:>50"), query);
    }

    /// <summary>缓存键归一大小写与首尾空白：否则"同一个筛选"会产生两份缓存，看着就像"切换周期不生效"。</summary>
    [Fact]
    public void CacheKeyOf_NormalizesLanguageCaseAndPadding()
    {
        var a = TrendingHtmlParser.CacheKeyOf(TrendingPeriod.Weekly, "  TypeScripT ");
        var b = TrendingHtmlParser.CacheKeyOf(TrendingPeriod.Weekly, "typescript");

        Assert.Equal(b, a);
        Assert.Equal("weekly|", TrendingHtmlParser.CacheKeyOf(TrendingPeriod.Weekly, null));
        Assert.NotEqual(a, TrendingHtmlParser.CacheKeyOf(TrendingPeriod.Daily, "typescript"));
    }

    [Fact]
    public void Periods_RoundTripCodesAndRejectUnknown()
    {
        Assert.Equal("daily", TrendingPeriods.Code(TrendingPeriod.Daily));
        Assert.Equal(2, TrendingPeriods.FallbackDays(TrendingPeriod.Daily));
        Assert.True(TrendingPeriods.TryParse("weekly", out var w));
        Assert.Equal(TrendingPeriod.Weekly, w);
        Assert.True(TrendingPeriods.TryParse(null, out var dflt));
        Assert.Equal(TrendingPeriod.Weekly, dflt);
        Assert.False(TrendingPeriods.TryParse("yearly", out var fallback));
        Assert.Equal(TrendingPeriod.Weekly, fallback);   // 不认识的周期退回默认值，不抛
    }

    /// <summary>兜底来源给不出"本期新增"，必须保持 null——把"不知道"画成"+0"是在骗人。</summary>
    [Fact]
    public void FromSearchApi_LeavesPeriodStarsUnknownAndDropsBlankRows()
    {
        var list = TrendingHtmlParser.FromSearchApi(new[]
        {
            new SearchApiRepo("o/r", "https://github.com/o/r", null, "Go", 77),
            new SearchApiRepo("  ", "", null, null, 0),
            new SearchApiRepo("o2/r2", "", null, null, 5),
        });

        var repo = list[0];
        Assert.Equal("o/r", repo.FullName);
        Assert.Null(repo.StarsToday);
        Assert.Equal(string.Empty, repo.Description);
        // fullName 空白的行被丢掉 ⇒ 只剩两条；第三条没有 html_url，按 fullName 补规范地址
        Assert.Equal(2, list.Count);
        Assert.Equal("o2/r2", list[1].FullName);
        Assert.Equal("https://github.com/o2/r2", list[1].Url);
    }

    private static string Article(string fullName, string? desc, string? lang, string stars, string today)
        => $"<article><div class=\"float-sm-right\"><a href=\"/{fullName}/stargazers\"><svg viewBox=\"0 0 16 16\"><path d=\"M2\"></path></svg> {stars}</a>"
         + $"<span>{today}</span></div>"
         + $"<h2 class=\"h3 lh-condensed\"><a href=\"/{fullName}\" data-x=\"1\">{fullName.Replace("/", " / ")}</a></h2>"
         + (desc is null ? string.Empty : $"<p class=\"col-9 css-truncate\">{desc}</p>")
         + (lang is null ? string.Empty : $"<span itemprop=\"programmingLanguage\">{lang}</span>")
         + "</article>";
}
