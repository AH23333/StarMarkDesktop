#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions.Feed;
using StarMark.Abstractions.I18n;
using StarMark.Core.Feed;
using StarMark.Integrations.Feed;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// RSS「为什么没成」那一族的形状闸门（批次 WI-79·P-211 落地腿）。
/// <para>这一族从前是四颗 <c>string?</c> 出口（<c>RssSourceConfig.UrlProblem</c>／<c>RssFetchResult.Error</c>／
/// <c>RssParseResult.Error</c>／<c>RssSourceOutcome.Error</c>）在各自那一层<b>当场拼好成品句</b>，再由
/// <c>RssSourceStatus.Describe</c> 塞进「失败：{0}」。屏幕上那一行的<b>外层</b>会随语言走，
/// 而<b>括号里那句原因永远停在拼它那一刻的语言</b>——这正是 P-211 登记的那颗缺陷。</para>
/// <para>所以这一颗闸门守的是三件事：①四颗出口交的是<b>键</b>不是句子（形状，编译器帮不上忙的那一半）；
/// ②那 20 句成品字<b>住在表里且各一处主人</b>（⛔ 在代码里再拼一份）；③<b>套着的那一句在被贴上去的那一刻</b>
/// 按当下语言取词（行为，①②都绿而它红＝交的是键却已经念完了）。</para>
/// <para>这颗闸门（<c>I18nWiringGateTests.EveryLocaleMutatingTestClassIsInTheSerialCollection</c>）在本批第一次跑全量时
/// 就把这一档点了红：会换那颗进程级语言的证人类⛔ 各跑各的，否则红的那格只指向"谁先抢到静态"。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class RssFeedWordGateTests : IDisposable
{
    private const string Feed = "src/StarMark.Abstractions/Feed/RssFeed.cs";
    private const string Client = "src/StarMark.Integrations/Feed/RssClient.cs";
    private const string Parser = "src/StarMark.Core/Feed/RssParser.cs";
    private const string Aggregator = "src/StarMark.Core/Feed/RssAggregator.cs";
    private const string Reason = "src/StarMark.Abstractions/Feed/RssTransportReason.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";

    private static readonly string[] Family = [Feed, Client, Parser, Aggregator, Reason];

    private readonly AppLanguage _before = Locale.Current;

    public void Dispose() => Locale.TrySet(_before);

    private static RssSourceConfig Source() => new(1, "测试源", "https://a.test/feed");

    /// <summary>①四颗出口的形参⛔ 再是 <c>string</c>：那句「为什么」必须带着<b>键＋实参</b>跨层，
    /// 而不是带着上一次算好的那串字。禁项与正件钉在同一次比较里（⛔ 一把只数 0 的尺）。</summary>
    [Fact]
    public void TheFourOutletsHandOutKeysAndNeverStrings()
    {
        Assert.Equal(1, Count(Code(ReadRepoFile(Feed)), "public static LocMessage? UrlProblem(string? url)"));
        Assert.Equal(0, Count(Code(ReadRepoFile(Feed)), "public static string? UrlProblem"));
        Assert.Equal(1, Count(Code(ReadRepoFile(Client)), "    LocMessage? Error)"));
        Assert.Equal(0, Count(Code(ReadRepoFile(Client)), "string? Error)"));
        Assert.Equal(1, Count(Code(ReadRepoFile(Parser)), "string? FeedTitle, LocMessage? Error)"));
        Assert.Equal(0, Count(Code(ReadRepoFile(Parser)), "string? Error)"));
        Assert.Equal(1, Count(Code(ReadRepoFile(Aggregator)), "    LocMessage? Error = null,"));
        Assert.Equal(0, Count(Code(ReadRepoFile(Aggregator)), "string? Error = null,"));
    }

    /// <summary>②那 20 句：表里各一颗、代码里各一处主人、<b>槽数＝实参数</b>。
    /// <para>槽数那一半是这套机制独有的错法：交出去的实参少了或多了，屏幕上不是崩，
    /// 而是一句话里凭空少一个数——那种红没人会去查取词那一层。</para>
    /// <para>颗数从 16 变 20＝批次 片47／P-216 那一刀：删掉递 <c>ex.Message</c> 的那颗槽，
    /// 换成按异常类型分档的五颗（它们的主人住在 <c>RssTransportReason</c>，那一档也一起进本族）。</para></summary>
    [Fact]
    public void TheTwentySentencesLiveInTheTableEachWithOneOwner()
    {
        var sites = Family.SelectMany(f => KeyedCallSites(Code(ReadRepoFile(f)))).ToList();
        Assert.Equal(20, sites.Count);
        Assert.Equal(20, sites.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (var (key, args, _) in sites)
        {
            Assert.Equal(1, Count(ReadRepoFile(Table), "new Entry(\"" + key + "\""));
            Assert.Equal(args, RowSlots(key));
        }
    }

    /// <summary>③这一族五档里⛔ 再没有第二份成品句。<b>那颗 1 是点名的</b>：<c>RssFolders.Root</c> 的
    /// 「RSS订阅」既上屏又落盘，动它要先解决"名字当身份"那一格（P-210 已登记），本批⛔ 顺手搬。
    /// <para>正件在同一格里：<b>同一把尺在同一跑里读到 1 与 0</b>，于是那四个 0 不是"尺瞎了"。</para>
    /// <para>新加的那一档（<c>RssTransportReason</c>）本来就是为了不再搬原文而生的，它交的全是键——
    /// 这一格把它算进来，是防下一次有人嫌"分档那句不够具体"而把 <c>error.Message</c> 拼回屏幕上。</para></summary>
    [Fact]
    public void TheWordFactoryKeepsOnlyTheNamedResidual()
    {
        var perFile = Family.ToDictionary(f => f, f => ScreenWords(Code(ReadRepoFile(f))));
        Assert.Equal(new[] { 1, 0, 0, 0, 0 }, Family.Select(f => perFile[f].Count).ToArray());
        Assert.Equal(new[] { "订阅" }, perFile[Feed].ToArray());   // 认不出这一颗＝这把尺对本族是瞎的
    }

    /// <summary>④那颗被<b>套着</b>的原因：同一个 <see cref="LocMessage"/> 对象，两次渲染跟着两次语言走。
    /// 这一格守的是 P-211 的本体——①②都绿而它红＝键是交了，句子却在交出去之前已经念完。</summary>
    [Fact]
    public void TheNestedReasonIsWordedAtPasteTime()
    {
        var line = RssSourceStatus.Describe(
            new RssSourceOutcome(Source(), Array.Empty<RssEntry>(),
                LocMessage.Of("rss.fetch.status", 503, "Service Unavailable")), 9);

        Assert.True(Locale.TrySet(AppLanguage.English));
        var en = line.Render();
        Assert.True(Locale.TrySet(AppLanguage.Japanese));
        var ja = line.Render();

        Assert.Contains("Failed: The source returned 503 Service Unavailable", en, StringComparison.Ordinal);
        Assert.Contains("失敗：ソースが 503 Service Unavailable を返しました", ja, StringComparison.Ordinal);
    }

    /// <summary>⑤两颗只递"半个事实"的出口：<b>协议名</b>与<b>根节点名</b>是实参，得留在当下语言里才念得出来。</summary>
    [Fact]
    public void TheArgumentSurvivesWhileTheSentenceRetakes()
    {
        Assert.True(Locale.TrySet(AppLanguage.English));
        Assert.Contains("Only http / https are supported; this one is ftp",
            RssSourceConfig.UrlProblem("ftp://a.test/feed")!.Render(), StringComparison.Ordinal);
        Assert.Contains("root node: html",
            RssParser.Parse("<html><body/></html>", Source()).Error!.Render(), StringComparison.Ordinal);

        Assert.True(Locale.TrySet(AppLanguage.Chinese));
        Assert.Contains("只支持 http / https，这个是 ftp",
            RssSourceConfig.UrlProblem("ftp://a.test/feed")!.Render(), StringComparison.Ordinal);
    }
}
