#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using StarMark.Abstractions.Feed;
using StarMark.Abstractions.I18n;
using StarMark.Core.Feed;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「抓这份订阅为什么没成」按<b>异常类型</b>分档那一族（批次 片47／P-216）。
/// <para>
/// 这一格欠的现场是英文界面 RSS 那一行的<b>后半句</b>：正本 <c>bin/WI82/ShotY/cap_82y_log.txt</c> 量到
/// <c>Failed: Fetching this source failed: 由于目标计算机积极拒绝，无法连接。 (127.0.0.1:9)</c>——
/// 表里那两句取的都是 en 列（那一半 0 颗汉字），而<b>槽里那颗</b>是运行库交给 <c>ex.Message</c> 的原文，
/// 它跟着系统语言，换界面语言一个字都不动。
/// </para>
/// <para>
/// 所以这一族守的是三件事，缺一件就回到那串读数上：①那五档<b>各有自己的表行</b>，且三列都念得出自己那一档
/// （en 列⛔ 汉字＝这颗坏的本体，ja 列⛔ 空、zh 列必有汉字＝那三列读对了的自证）；
/// ②<b>认档只按类型</b>，读 <c>Message</c> 里的字当判据＝让"屏幕上念哪一句"也随系统语言走；
/// ③原文只剩<b>日志那一颗出口</b>（形状：调用点在 <c>StarLog.</c> 那一行上）。
/// </para>
/// <para>⚠ 为什么⛔ 用「那一行里有英文」当判据：异常正文本来就是英文的话，中文界面也能半英。
/// 要钉的是<b>槽里交出去的那一样东西是键</b>，与<b>那一键在三列里各念什么</b>。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class RssTransportWordGateTests : IDisposable
{
    private const string Client = "src/StarMark.Integrations/Feed/RssClient.cs";
    private const string Reason = "src/StarMark.Abstractions/Feed/RssTransportReason.cs";
    private const string DeadKey = "rss.fetch.transportError";

    private static readonly string[] Arms =
    [
        "rss.fetch.err.refused", "rss.fetch.err.tls", "rss.fetch.err.timeout",
        "rss.fetch.err.unreachable", "rss.fetch.err.other",
    ];

    private readonly AppLanguage _before = Locale.Current;

    public void Dispose() => Locale.TrySet(_before);

    private static SocketException Refused() => new((int)SocketError.ConnectionRefused);

    /// <summary>五档这颗名单由<b>表</b>那一侧数出来（⛔ 是抄在断言里的常数）：
    /// 下一批加第六档而证人没跟着加，这一格当场红（名册 413「恒绿断言数的是自己那份名单」同一族）。</summary>
    [Fact]
    public void TheRosterOfArmsIsCountedFromTheTableNotFromThisTest()
        => Assert.Equal(Arms.OrderBy(k => k, StringComparer.Ordinal),
            UiStrings.Keys().Where(k => k.StartsWith("rss.fetch.err.", StringComparison.Ordinal))
                .OrderBy(k => k, StringComparer.Ordinal).ToArray());

    /// <summary>①五档各有表行、<b>三列各念自己那一档</b>、槽数＝0（这一族不带货）。
    /// <para>那颗"槽数＝0"不是省事：交进去的实参多一颗，屏幕上不会崩，只会把多出来的那串字（＝原文）
    /// 又念回给用户——正是本条登记的那颗坏的另一副脸。</para></summary>
    [Theory]
    [InlineData("rss.fetch.err.refused")]
    [InlineData("rss.fetch.err.tls")]
    [InlineData("rss.fetch.err.timeout")]
    [InlineData("rss.fetch.err.unreachable")]
    [InlineData("rss.fetch.err.other")]
    public void EachArmIsRowedInThreeLanguagesWithNoSlot(string key)
    {
        Assert.True(UiStrings.TryGet(key, out var row), $"表里没有这一档：{key}");
        Assert.Equal(0, RowSlots(key));

        Assert.False(Cjk.IsMatch(row.En), $"{key} 的英文列夹着中日文＝英文界面上那一格还是半句中文：{row.En}");
        Assert.True(Cjk.IsMatch(row.Zh), $"{key} 的中文列一个汉字都没有＝那三列压根没读对：{row.Zh}");
        Assert.False(string.IsNullOrWhiteSpace(row.Ja), $"{key} 的日文列是空的");
        Assert.NotEqual(row.Zh, row.Ja);
    }

    /// <summary>②认档<b>只按类型</b>：五种现场各归自己那一格，而"最具体那颗赢"要由一条深链来证
    /// （<c>HttpRequestException</c> 套 <c>InvalidOperationException</c> 套 <c>SocketException(拒绝)</c>——
    /// 按外层归就是"连不上"，而用户该做的两件事完全不同）。</summary>
    [Theory]
    [InlineData("rss.fetch.err.timeout")]
    [InlineData("rss.fetch.err.unreachable")]
    [InlineData("rss.fetch.err.other")]
    public void TheTypeDecidesTheArmAndNeverTheWords(string expected)
    {
        var error = expected switch
        {
            "rss.fetch.err.timeout" => (Exception)new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 20 seconds elapsing"),
            "rss.fetch.err.unreachable" => new HttpRequestException("No such host is known. (a.test:443)",
                new SocketException((int)SocketError.HostNotFound)),
            _ => new InvalidOperationException("something else"),
        };
        Assert.Equal(expected, RssTransportReason.Classify(error).Key);
    }

    /// <summary>③同一条链上的<b>最具体那一颗</b>说话，而不是最先看到的那一颗——这两位在屏幕上做的是两件事：
    /// 被拒＝去查对方，连不上＝查自己这头的网络。</summary>
    [Fact]
    public void TheMostSpecificHopWinsAndNotTheOutermost()
    {
        var refusedDeep = new HttpRequestException("An error occurred while sending the request.",
            new InvalidOperationException("wrapped by the layer above", Refused()));

        Assert.Equal("rss.fetch.err.refused", RssTransportReason.Classify(refusedDeep).Key);
        Assert.Equal("rss.fetch.err.refused", RssTransportReason.Classify(Refused()).Key);
        Assert.Equal("rss.fetch.err.tls", RssTransportReason.Classify(new AuthenticationException("认证失败")).Key);
        Assert.Equal("rss.fetch.err.unreachable", RssTransportReason.Classify(new HttpRequestException("boom")).Key);
        // 那颗用户按下的取消⛔ 会走到这里（<c>RssClient</c> 里 <c>ct.IsCancellationRequested</c> 那一臂先原样上抛），
        // 但运行库自己那发超时两种形状都出得来，所以这一档认两颗＝同句"超时"⛔ 随形状变。
        Assert.Equal("rss.fetch.err.timeout", RssTransportReason.Classify(new OperationCanceledException()).Key);
        Assert.Equal("rss.fetch.err.timeout", RssTransportReason.Classify(new TaskCanceledException()).Key);
    }

    /// <summary>④本条的本体在屏幕上：把<b>现场那串中文原文</b>当作异常正文，英文界面上那一行整颗逐字＝
    /// 表里那句 en 列，一个汉字都没有；而<b>日志那一头必须还读得到那串原文</b>——两半在同一格里比，
    /// 于是"屏幕上没有汉字"不可能是被"整句都没了"糊过去的。最后一拍把同一颗异常放回中文界面，
    /// 同一把尺读到<b>有</b>汉字＝这把尺⛔ 瞎。</summary>
    [Fact]
    public void TheSystemSentenceStaysInTheLogAndOffTheEnglishScreen()
    {
        const string systemText = "由于目标计算机积极拒绝，无法连接。 (127.0.0.1:9)";
        var error = new HttpRequestException(systemText, Refused());
        var outcome = new RssSourceOutcome(
            new RssSourceConfig(1, "Bogus local feed", "http://127.0.0.1:9/feed"), Array.Empty<RssEntry>(),
            RssTransportReason.Classify(error));

        Assert.True(Locale.TrySet(AppLanguage.English));
        var en = RssSourceStatus.Describe(outcome).Render();
        Assert.Equal("Failed: The source refused the connection", en, StringComparer.Ordinal);
        Assert.DoesNotContain(systemText, en, StringComparison.Ordinal);
        Assert.DoesNotMatch(Cjk, en);
        Assert.Contains(systemText, RssTransportReason.Detail(error), StringComparison.Ordinal);

        Assert.True(Locale.TrySet(AppLanguage.Chinese));
        var zh = RssSourceStatus.Describe(outcome).Render();
        Assert.Equal("失败：这个源拒掉了连接", zh, StringComparer.Ordinal);
        Assert.Matches(Cjk, zh);
    }

    /// <summary>⑤接线那一头的形状（编译器帮不上忙的那一半）：屏幕那颗出口交的是<b>分档</b>，
    /// 原文那颗出口只许住在 <c>StarLog.</c> 那一行上；<b>旧键整仓归零</b>（留着＝<c>TheTableHasNoRowNothingAsksFor</c>
    /// 那一格说过的"谎报覆盖面"）。正件与禁项钉在同一次比较里。</summary>
    [Fact]
    public void TheClientHandsTheArmAndKeepsTheRawTextOnTheLogLine()
    {
        var code = Code(ReadRepoFile(Client));
        Assert.Equal(1, Count(code, "RssTransportReason.Classify(ex)"));
        Assert.Equal(0, Count(code, "ex.Message"));
        Assert.Equal(0, Count(code, '"' + DeadKey + '"'));

        var detail = code.Split('\n').Where(l => l.Contains("RssTransportReason.Detail(ex)", StringComparison.Ordinal)).ToList();
        Assert.Single(detail);
        Assert.Contains("StarLog.", detail[0], StringComparison.Ordinal);

        Assert.Equal(5, Count(Code(ReadRepoFile(Reason)), "LocMessage.Of(\"rss.fetch.err."));
        Assert.Equal(0, ReadRepoUnder("src").Count(s => s.Text.Contains(DeadKey, StringComparison.Ordinal)));
    }

    /// <summary>⑥那条链<b>走到第八层就停</b>是本条刻意选的数（自指的链真出得来：第三方库把同一颗异常塞回自己）。
    /// 这一格钉的是那颗封顶的<b>两侧</b>：拒绝那颗挂在第 8 跳上＝看得见，挂在第 9 跳上＝本条刻意不去看，
    /// 于是屏幕上落的是链上看得见的那一档（连不上），日志同样只列八跳。
    /// ⚠ 只钉"看不见那一侧"是一把单边尺：把封顶改成 3 也照样绿（名册 427 那颗差集）。</summary>
    [Fact]
    public void TheChainWalkStopsAtItsOwnCap()
    {
        static Exception Deep(int wrappers)
        {
            Exception chain = Refused();
            for (var i = 0; i < wrappers; i++) chain = new HttpRequestException("hop " + i, chain);
            return chain;
        }

        Assert.Equal("rss.fetch.err.refused", RssTransportReason.Classify(Deep(7)).Key);
        Assert.Equal("rss.fetch.err.unreachable", RssTransportReason.Classify(Deep(8)).Key);
        Assert.Equal(7, Count(RssTransportReason.Detail(Deep(8)), " <- "));
    }
}
