#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Abstractions.Trending;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-58（P-190）的闸门：<b>热榜那一路念给人的每一句，从抓取到贴上屏幕之间不许有一次取词</b>。
/// <para>
/// 这一族的前半（<c>trending.status*</c>／<c>widget.glance.trending.*</c>）早在 ✅WI-2z／✅WI-56 交进表里，
/// 但<b>供话者</b>在 <c>StarMark.Integrations</c> 与 <c>StarMark.Core</c>：那两档一共十处
/// <c>Locale.T／Locale.Fmt</c> 都发生在"抓取那一刻"。抓完把成品字塞进 <c>Notice</c> 与
/// <c>TrendingException.Message</c>，之后换语言就只有<b>外层那半句</b>跟着改口——
/// 屏幕上得到"来源…in cache｜热榜页请求失败：…"这种半中半英（发起人那句「国际化不全」的一种）。
/// </para>
/// <para>
/// 射程按<b>屏幕上同一处宿主</b>划（台架规则 #115）＝热榜页那一行状态＋今日速览那一块的状态行，
/// 于是两档供话者、契约里的两个字段与那颗异常、以及<b>两处宿主</b>一起进这一族（✅WI-57 的诊断那一族同一条路子）。
/// </para>
/// <para>尺的口径与 <c>DiagnosticsWordGateTests</c> 同一份（共用件＝<see cref="SourceGate"/>，规则 #86）：
/// 抹注释后逐行取 CJK <b>含全角那一族</b>、排除日志头；登记剩余量必须把尺子写进句子（规则 #126）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class TrendingWordGateTests : IDisposable
{
    private const string FetcherCs = "src/StarMark.Integrations/Trending/TrendingFetcher.cs";
    private const string ServiceCs = "src/StarMark.Core/Trending/TrendingService.cs";
    private const string ContractsCs = "src/StarMark.Abstractions/Trending/TrendingContracts.cs";
    private const string PageVmCs = "src/StarMark.UI/ViewModels/TrendingPageViewModel.cs";
    private const string GlanceCs = "src/StarMark.UI/Views/GlanceWidget.xaml.cs";

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>屏幕上会念出来的字＝共用件 <see cref="SourceGate.ScreenWords"/> 那颗尺
    /// （⛔ 在这里再写一遍字符类＝第二个口径，台架规则 #86）。</summary>
    private static List<string> ScreenWords(string text) => SourceGate.ScreenWords(text);

    /// <summary>
    /// <b>供话者⛔ 自己取词</b>：抓取与编排那两档只许交出"键＋实参"。
    /// <para>这一颗是形状禁项，与下面那颗语言读数<b>成对</b>（规则 #124）：只钉读数的话，有人把取词搬回
    /// 抓取那一刻、读数照样对；只钉形状的话，有人把整句写死成中文字面量，形状尺看不见字面量。</para>
    /// </summary>
    [Fact]
    public void TheFetcherAndTheServiceNeverTakeWordsThemselves()
    {
        foreach (var rel in new[] { FetcherCs, ServiceCs })
        {
            var code = CodeOf(rel);
            Assert.Equal(0, SourceGate.Count(code, "Locale.T("));
            Assert.Equal(0, SourceGate.Count(code, "Locale.Fmt("));
            Assert.Equal(0, SourceGate.Count(code, ".Render()"));   // 交出成品字的那一种写法
        }

        // 现场数出来的两颗数（⛔ 把期望改成"实测值"之前先把名单抄出来＝规则 #120）：
        //   抓取那一档 13 颗键＝fetch.{httpBad,httpStatus,retryFailed,htmlFailed,htmlZero,apiFailed,apiEmpty,
        //     fallbackApi,bothEmpty,allFailed,tooLarge} ＋ notice.{join,compose}；编排那一档 2 颗＝notice.{write,join}。
        Assert.Equal(13, SourceGate.KeyedCallSites(CodeOf(FetcherCs)).Select(c => c.Key).Distinct().Count());
        Assert.Equal(2, SourceGate.KeyedCallSites(CodeOf(ServiceCs)).Select(c => c.Key).Distinct().Count());

        // 契约档只许<b>一处</b>取词，而且必须是那颗异常对外的 <c>Message</c>（那一串只进日志，⛔ 上屏幕）：
        // 界面念的是 <c>Reason</c> 那颗键。少了这条豁免，下一批有人把 Message 也键化就会红在仪器上。
        var contracts = CodeOf(ContractsCs);
        Assert.Equal(0, SourceGate.Count(contracts, "Locale.T("));
        Assert.Equal(0, SourceGate.Count(contracts, "Locale.Fmt("));
        Assert.Equal(1, SourceGate.Count(contracts, ".Render()"));
        Assert.Contains("base(reason.Render())", contracts, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>汉字与全角标点只许住在表里</b>（登记剩余量＝<b>0 行</b>，尺＝上面那句口径，⛔ 去重⛔ 上屏数）。
    /// <para>顺带钉死本批拆掉的那一种"看着无害"：从前 <c>Compose</c> 对每片从句
    /// <c>TrimEnd('。', '；')</c>——两颗 CJK 标点写死在代码里，正是坑表 #407 那一族（<c>health.join.list</c>
    /// 那颗「、」是同一条）。表里那几颗从句本来不带句末标点，⛔ 需要再削。</para>
    /// </summary>
    [Fact]
    public void ChineseSurvivesOnlyInTheLogLinesOnTheTrendingNoticePath()
    {
        var fetcher = CodeOf(FetcherCs);
        var service = CodeOf(ServiceCs);
        Assert.Equal(Array.Empty<string>(), ScreenWords(fetcher));
        Assert.Equal(Array.Empty<string>(), ScreenWords(service));
        Assert.DoesNotContain("TrimEnd('。'", fetcher, StringComparison.Ordinal);
        Assert.DoesNotContain("TrimEnd('；'", fetcher, StringComparison.Ordinal);
        // 那条上限的"多少 MB"以前是内插出来的一句中文，现在那颗键必须还在被问
        Assert.Contains("LocMessage.Of(\"trending.fetch.tooLarge\"", fetcher, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"trending.fetch.httpStatus\"", fetcher, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>每一处调用点递的实参数＝那一行声明的槽数</b>（按括号深度扫，共用件 <see cref="SourceGate.KeyedCallSites"/>）。
    /// <para>为什么必须有这一颗：少递一颗实参时表闸门全绿、代码编译也过，屏幕上那一格<b>整空白</b>
    /// （坑表 #393 那一条；批次 WI-57 的 A4 臂就是把实参少递一颗，红在这里）。</para>
    /// </summary>
    [Fact]
    public void EveryNoticeCallHandsExactlyTheSlotsItsRowDeclares()
    {
        var offenders = new List<string>();
        var watched = 0;
        foreach (var rel in new[] { FetcherCs, ServiceCs, ContractsCs })
        {
            foreach (var (key, args, line) in SourceGate.KeyedCallSites(CodeOf(rel)))
            {
                var slots = SourceGate.RowSlots(key);
                watched++;
                if (args != slots) offenders.Add($"{rel}:{line} 的 {key} 表里 {slots} 颗槽，代码递了 {args} 颗实参");
            }
        }
        Assert.True(watched >= 10, $"这一族只读到 {watched} 处带字面键的 LocMessage.Of 调用（少于 10），判据自己失效了");
        Assert.Equal(Array.Empty<string>(), offenders);
    }

    /// <summary>
    /// <b>跨层那颗字段与那颗异常交的都是键</b>：两个 record 的 <c>Notice</c> 都是 <c>LocMessage?</c>，
    /// 而 <c>TrendingException</c> 带的是 <c>LocMessage Reason</c>（<c>Message</c> 只留给日志）。
    /// <para>⛔ 留 <c>string? Notice</c> 作"兼容"：那正是 P-190 的形状——字段类型本身就是缺陷的位置。</para>
    /// </summary>
    [Fact]
    public void TheNoticeCrossesTheLayerBoundaryAsAKey()
    {
        var contracts = CodeOf(ContractsCs);
        var service = CodeOf(ServiceCs);
        Assert.Equal(2, SourceGate.Count(contracts + service, "LocMessage? Notice = null"));
        Assert.Contains("public TrendingException(LocMessage reason) : base(reason.Render()) => Reason = reason;", contracts, StringComparison.Ordinal);
        Assert.Contains("public LocMessage Reason { get; }", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("string? Notice", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("string? Notice", service, StringComparison.Ordinal);
        // 失败原因那一路：Core 不再把 raw 的 ex.Message 当 Notice 递出去
        Assert.DoesNotContain("Stale: true, cached.FetchedAt, ex.Message", service, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>"抓取失败了该念哪一句"只许一处决定</b>：那颗共用件住在契约档，两处宿主与 Core 的 stale 那一路都问它。
    /// <para>三处各拼一份「抓取失败：{0}」＝三个主人（P-171 那一族）；改一处措辞，另外两处还是旧话。</para>
    /// </summary>
    [Fact]
    public void TheFailureSentenceHasOneOwnerForBothHostsAndForStale()
    {
        var contracts = CodeOf(ContractsCs);
        Assert.Contains("public static LocMessage Of(Exception ex) =>", contracts, StringComparison.Ordinal);
        Assert.Contains("ex is TrendingException trended ? trended.Reason : LocMessage.Of(\"trending.emptyFailed\", ex.Message)",
            contracts, StringComparison.Ordinal);

        foreach (var rel in new[] { ServiceCs, PageVmCs, GlanceCs })
            Assert.Contains("TrendingFailure.Of(ex)", CodeOf(rel), StringComparison.Ordinal);

        // 宿主⛔ 自己抄一份那颗句子（那样共用件就成了第三份出处）
        Assert.DoesNotContain("\"trending.emptyFailed\", ex.Message", CodeOf(PageVmCs), StringComparison.Ordinal);
        Assert.DoesNotContain("\"trending.emptyFailed\", ex.Message", CodeOf(GlanceCs), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>同一颗 Notice 在三语下各念各的，而机器事实三语都在</b>——这条只有真跑一遍才看得见。
    /// <para>形状禁项抓不到"在 Core 里先 Render 过一次"：那种代码看起来完全正常（✅WI-57 同一条教训）。
    /// 期望那一侧⛔ 抄表里的措辞（规则 #130）：钉的是<b>三语互不相同</b>＋<b>码在不在</b>＋
    /// <b>拉丁那一档⛔ 出现 CJK 宽度的符号</b>，措辞怎么改它都该绿。</para>
    /// </summary>
    [Fact]
    public void TheSameNoticeRendersDifferentlyPerLanguage()
    {
        var notice = LocMessage.Of("trending.notice.compose",
            LocMessage.Of("trending.fetch.fallbackApi"),
            LocMessage.Of("trending.notice.join",
                LocMessage.Of("trending.fetch.httpBad", 503),
                LocMessage.Of("trending.fetch.htmlZero")));

        Locale.TrySet(AppLanguage.Chinese); var zh = notice.Render();
        Locale.TrySet(AppLanguage.English); var en = notice.Render();
        Locale.TrySet(AppLanguage.Japanese); var ja = notice.Render();

        Assert.NotEqual(zh, en);
        Assert.NotEqual(en, ja);
        Assert.NotEqual(zh, ja);
        foreach (var text in new[] { zh, en, ja })
        {
            Assert.Contains("503", text, StringComparison.Ordinal);            // 两条腿的码是这一句的意义所在
            Assert.DoesNotContain("{0}", text, StringComparison.Ordinal);      // 少递实参会在这里露出来（#393）
        }
        Assert.Empty(SourceGate.Cjk.Matches(en).Cast<Match>().Select(m => m.Value));      // 拉丁那一档⛔ 混进 CJK 宽度符号
        Assert.NotEmpty(SourceGate.Cjk.Matches(ja).Cast<Match>());                        // 而日文那一档<b>应当</b>有本族文字
    }

    /// <summary>
    /// <b>那颗连接骨架从此是两颗槽</b>：代码里 <c>a + Locale.T(join) + b</c> 会把两头的语言定死在抓取那一刻，
    /// 而"怎么连"本身也是语言内容（en 列半角、中日全角）。
    /// </summary>
    [Fact]
    public void TheJoinSkeletonIsTwoSlotsAndNestsInsteadOfConcatenating()
    {
        Assert.Equal(2, SourceGate.RowSlots("trending.notice.join"));
        foreach (var rel in new[] { FetcherCs, ServiceCs })
            Assert.Contains("LocMessage.Of(\"trending.notice.join\"", CodeOf(rel), StringComparison.Ordinal);

        Locale.TrySet(AppLanguage.English);
        Assert.Equal("A, B", Locale.Fmt("trending.notice.join", "A", "B"));   // 半角：那颗宽度是这一列的事实
        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal("A；B", Locale.Fmt("trending.notice.join", "A", "B"));
    }

    /// <summary>
    /// <b>两处宿主只在贴上去那一刻取词</b>：主窗那一行把 Notice <b>套</b>进外层句子（交的是键），
    /// 今日速览那三片事实先攒成 <c>LocMessage</c>、在 <c>PaintTrending</c> 的最后一行才 Render。
    /// <para>⛔ 在换语言的回调里重新抓取（<c>CollectAsync</c>／<c>GetAsync</c> 有网络与全表扫描），
    /// 那一格由 <c>GlanceWordGateTests</c> 与 <c>I18nWiringGateTests</c> 守着，这里只钉"念法的时机"。</para>
    /// </summary>
    [Fact]
    public void BothHostsTakeWordsOnlyAtTheMomentTheyPaint()
    {
        var describe = SourceGate.MethodBody(CodeOf(PageVmCs), "private static LocMessage Describe(TrendingResult r)");
        Assert.Contains("r.Notice is { } notice", describe, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"trending.statusNotice\", head, notice)", describe, StringComparison.Ordinal);
        Assert.Equal(0, SourceGate.Count(describe, ".Render()"));      // 宿主这句交出去时还没取词

        var paint = SourceGate.MethodBody(CodeOf(GlanceCs), "private void PaintTrending()");
        Assert.Contains("if (result.Notice is { } notice) notes.Add(notice);", paint, StringComparison.Ordinal);
        Assert.Contains("notes.Aggregate((head, next) => LocMessage.Of(\"trending.statusNotice\", head, next)).Render();",
            paint, StringComparison.Ordinal);
        Assert.DoesNotContain("Locale.T(\"trending.", paint, StringComparison.Ordinal);
        Assert.DoesNotContain("result.Notice!", paint, StringComparison.Ordinal);   // 成品字段⛔ 再上屏幕
    }
}
