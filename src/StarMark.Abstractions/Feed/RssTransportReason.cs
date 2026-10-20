#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using StarMark.Abstractions.I18n;

namespace StarMark.Abstractions.Feed;

/// <summary>
/// 「抓这份订阅为什么没成」按<b>异常类型</b>分档（批次 片47／P-216）。
/// <para>
/// 从前那一颗 catch 把 <c>ex.Message</c> 直接递进表里那句「抓取这个源时出错：{0}」的槽。槽里那串字
/// 由 <b>Winsock／系统错误表</b>给出，它跟着<b>这台机器的系统语言</b>，⛔ 跟着界面语言：英文界面上量到的
/// 整颗读数就是 <c>Failed: Fetching this source failed: 由于目标计算机积极拒绝，无法连接。 (127.0.0.1:9)</c>
/// （正本 <c>bin/WI82/ShotY/cap_82y_log.txt</c>）。表里两句都取了 en 列而槽里是十五颗汉字——
/// 这一族要消灭的正是「换语言只换了外层」。
/// </para>
/// <para>
/// 所以这里⛔ 搬原文，而是<b>认档</b>：屏幕上念表里那句按类型选出来的话，原文只从 <see cref="Detail"/>
/// 交日志（先例＝✅WI-81 同步那四档「认得出档就⛔ 再摊机器话」、✅WI-63「说出原因但⛔ 摊整条 <c>ex.Message</c>」）。
/// 认不出档的那一档（<c>…err.other</c>）说的是「出了别的错」而⛔ 假装说清了——日志里那条带着类型名，
/// 下次查「这一档为什么归错」有的可查。
/// </para>
/// </summary>
public static class RssTransportReason
{
    /// <summary><b>沿 <c>InnerException</c> 链往上走的封顶层数</b>：链是用户看不见的，而自指的链
    /// （<c>ex.InnerException == ex</c>）真出得来（第三方库把同一颗异常塞回自己）。封顶之后
    /// 最坏是"少看几层"，不封顶是"这一发永远走不完"。</summary>
    private const int MaxChainHops = 8;

    /// <summary>五档，<b>排在前面的更具体</b>：一条链上同时出现两颗认得的异常时赢的是靠前那颗
    /// （<c>HttpRequestException</c> 里面套 <c>SocketException(拒绝)</c> 是连接被拒的标准形状，
    /// 按外层归就成了"连不上"，而用户要做的两件事完全不同：前者查对方，后者查自己这头的网络）。</summary>
    private enum Arm
    {
        Refused,
        Tls,
        Timeout,
        Unreachable,
        Other,
    }

    /// <summary>这一发该念表里哪一句。<b>交回的是键＋实参</b>（本族同一口径）：取词留到贴上屏幕那一刻，
    /// 换语言时那一行整颗跟着走。</summary>
    public static LocMessage Classify(Exception error)
    {
        var arm = Best(error);
        if (arm == Arm.Refused) return LocMessage.Of("rss.fetch.err.refused");
        if (arm == Arm.Tls) return LocMessage.Of("rss.fetch.err.tls");
        if (arm == Arm.Timeout) return LocMessage.Of("rss.fetch.err.timeout");
        if (arm == Arm.Unreachable) return LocMessage.Of("rss.fetch.err.unreachable");
        return LocMessage.Of("rss.fetch.err.other");
    }

    /// <summary>日志那一头要的是<b>原文</b>：分档给人看，类型名与链给查的人。
    /// <para>这一颗的出口只有日志。把它接回屏幕上的那一格＝把本条登记的坏原样搬回来，
    /// 而且搬回来之后表里那五档就成了没人问的行（<c>TheTableHasNoRowNothingAsksFor</c> 那一格会当场说）。</para></summary>
    public static string Detail(Exception error)
    {
        var hops = new List<string>();
        var e = error;
        for (var hop = 0; e is not null && hop < MaxChainHops; hop++, e = e.InnerException)
            hops.Add(e.GetType().Name + ": " + e.Message);
        return string.Join(" <- ", hops);
    }

    private static Arm Best(Exception error)
    {
        var best = Arm.Other;
        var e = error;
        for (var hop = 0; e is not null && hop < MaxChainHops; hop++, e = e.InnerException)
        {
            var rank = Rank(e);
            if (rank < best) best = rank;
        }
        return best;
    }

    /// <summary>认档只按<b>类型</b>，⛔ 读 <c>Message</c> 里的字：那串字随系统语言变，
    /// 拿它做判据就等于让"屏幕上念哪一句"也跟着系统语言走——正是本条要修的那件事。</summary>
    private static Arm Rank(Exception e) => e switch
    {
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => Arm.Refused,
        AuthenticationException => Arm.Tls,
        TaskCanceledException or OperationCanceledException => Arm.Timeout,
        SocketException or HttpRequestException => Arm.Unreachable,
        _ => Arm.Other,
    };
}
