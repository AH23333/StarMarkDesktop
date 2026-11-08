#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 换语言重贴的逐颗兜底闸门（片246，扫源码不跑界面）。
/// <para>
/// 现场读数（starmark-20261108.log・pid 46592・19:42 段）：开着设置页连切三次语言（zh→ja→ja→en→en→zh），
/// 每次切换都发同一颗 <c>locale.changed.subscriber.threw subscriber=LocText.ReapplyAll type=COMException</c>。
/// 片205 在 Locale（StarMark.Abstractions.I18n）那一层逐户兜底救的是<b>订阅链上户与户之间</b>，
/// 救不了 <c>ReapplyAll</c> 自己循环内部：一颗已断线的元素（窗已关/树已拆但对象仍被别处揪着）一抛，
/// foreach 当场断链——排在它后面的注册项整批停在旧语言，且每次切换都断在同一颗（坑表 #1170）。
/// 这里守五半，缺一算没做：
/// ① 死引用照旧先清（快照前的既有行为不许被顺手删）；
/// ② 每颗注册项自己裹一层 try（SetValue 在 try 里，catch 在它后面）；
/// ③ 抛了的那颗当场按<b>注册项身份</b>逐出名单——断线的树回不来，留着只保证每次断在同一处；
/// ④ 窗口标题那张名单同款（Window 不是 DependencyObject，但关掉的窗一样会断链）；
/// ⑤ 丢弃要在一行有界汇总里点名（仅丢弃时发・封顶三颗・带颗数）——揪着已拆窗/树里元素不放的那处
/// （根因）按这行名单另立一案，⛔ 在重贴那一拍里滚雪球。
/// </para>
/// </summary>
public sealed class LocTextReapplyResilienceGateTests
{
    private static string Code() => SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Helpers/LocText.cs"));

    private static string ReapplyAllBody()
        => SourceGate.MethodBody(Code(), "private static void ReapplyAll()");

    /// <summary>既有行为不许被顺手删：快照前先清死引用——弱表已经放掉的项不进快照、不参与重贴。</summary>
    [Fact]
    public void DeadReferencesAreStillPrunedBeforeTheSnapshot()
    {
        var body = ReapplyAllBody();
        Assert.Contains("Live.RemoveAll(r => !r.TryGetTarget(out _))", body);
        Assert.Contains("WindowTitles.RemoveAll(t => !t.TryGetTarget(out _))", body);
        Assert.True(body.IndexOf("Live.RemoveAll(r => !r.TryGetTarget(out _))", System.StringComparison.Ordinal)
                    < body.IndexOf("snapshot = Live.ToArray()", System.StringComparison.Ordinal),
            "清死引用必须发生在快照之前（快照里留着死项＝重贴时白试一遍再抛）");
    }

    /// <summary>每颗注册项自己裹一层 try：SetValue 在 try 里、catch 紧随——一颗断线元素只烧自己那颗，
    /// 不再截断整批（片205 的逐户兜底救的是户与户之间，救不了这里循环内部）。</summary>
    [Fact]
    public void EachRegistrationIsAppliedInsideItsOwnTryCatch()
    {
        var body = ReapplyAllBody();
        var loop = body.Substring(body.IndexOf("foreach (var registration in snapshot)", System.StringComparison.Ordinal));
        Assert.True(loop.IndexOf("try", System.StringComparison.Ordinal)
                    < loop.IndexOf("target.SetValue(registration.Property", System.StringComparison.Ordinal),
            "SetValue 必须裹在 try 里");
        Assert.True(loop.IndexOf("target.SetValue(registration.Property", System.StringComparison.Ordinal)
                    < loop.IndexOf("catch (Exception)", System.StringComparison.Ordinal),
            "catch 必须紧随 SetValue（抛了才兜，不吞别处的错）");
    }

    /// <summary>抛了的那颗当场逐出名单：按<b>注册项身份</b>（ReferenceEquals 那条注册）而不是按元素重找——
    /// 断线的树回不来，留着它只保证下次换语言断在同一处、后面整批又停在旧语言。</summary>
    [Fact]
    public void AThrowingRegistrationIsEvictedByIdentity()
    {
        var body = ReapplyAllBody();
        var liveCatch = body.Substring(body.IndexOf("catch (Exception)", System.StringComparison.Ordinal));
        Assert.Contains("Live.RemoveAll(r => ReferenceEquals(r, registration))", liveCatch);
        Assert.Contains("(dropped ??= new List<string>()).Add($\"", liveCatch);
    }

    /// <summary>窗口标题那张名单同款：关掉的窗（或拆掉的树）一样会断链——Title 那一设也在 try 里，
    /// 抛了同样按身份逐出，不许一张名单修了另一张照断。</summary>
    [Fact]
    public void WindowTitlesGetTheSameTreatment()
    {
        var body = ReapplyAllBody();
        var titleLoop = body.Substring(body.IndexOf("foreach (var title in titles)", System.StringComparison.Ordinal));
        Assert.True(titleLoop.IndexOf("try", System.StringComparison.Ordinal)
                    < titleLoop.IndexOf("window.Title = Text(title.Key, title.Args)", System.StringComparison.Ordinal)
                    && titleLoop.IndexOf("window.Title = Text(title.Key, title.Args)", System.StringComparison.Ordinal)
                    < titleLoop.IndexOf("catch (Exception)", System.StringComparison.Ordinal),
            "Title 重设必须裹在 try 里");
        var titleCatch = titleLoop.Substring(titleLoop.IndexOf("catch (Exception)", System.StringComparison.Ordinal));
        Assert.Contains("WindowTitles.RemoveAll(t => ReferenceEquals(t, title))", titleCatch);
    }

    /// <summary>丢弃要在一行有界汇总里点名：仅真有丢弃时发・带颗数・封顶三颗——
    /// 既不吞没痕迹（根因要按名单查），也不刷屏（一颗断线元素一行就够，逐出后不会重发）。</summary>
    [Fact]
    public void DropsAreNamedInOneBoundedLine()
    {
        var body = ReapplyAllBody();
        Assert.Contains("if (dropped is { Count: > 0 })", body);
        Assert.Contains("dropped.Take(3)", body);
        Assert.Contains("×{dropped.Count}", body);
        var warn = body.IndexOf("StarLog.Warn", System.StringComparison.Ordinal);
        Assert.True(body.IndexOf("WindowTitles.RemoveAll(t => ReferenceEquals(t, title))", System.StringComparison.Ordinal)
                    < warn,
            "汇总行必须在两轮循环都结束之后（单轮丢弃要等另一轮的份一起报）");
    }
}
