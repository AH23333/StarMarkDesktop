#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-67 片9b（P-188 第五刀）的闸门：<b>搜索页与快捷搜索组件那一族的屏幕上字全部交「键＋实参」，取词只许发生在贴上去那一刻</b>。
/// <para>射程按<b>屏幕上那一处宿主</b>划（台架规则 #115）＝主窗「搜索」那一页的四行（两颗段名＋空态＋那颗「加载更多」按钮词）
/// 与快捷搜索组件那一句空态，加上组件那颗排序下拉的三档措辞。</para>
/// <para><b>本批读码读出来的三件事，各配一格</b>：
/// ① <c>SearchPageViewModel.StatusText</c> ⛔ 被任何绑定读过（<c>{x:Bind ViewModel.StatusText}</c> 现场 0 处、全仓⛔ 有读者）
/// ＝一句死出口，本批删掉并把失败那句改交这一屏唯一看得见的空态行（✅HG／P-56 的口径：要说得出原因与下一步）；
/// ② 那三行<b>不带读数的段名</b>（"精确匹配"／"相关结果"）⛔ 上过屏——可见性绑的 <c>HasExact</c> 恰在那时为假，
/// 所以本批给它 null 而⛔ 为看不见的句子造键；
/// ③ <c>SearchWidget.xaml</c> 那颗排序下拉的三颗 <c>&lt;ComboBoxItem Content="中文"&gt;</c> 与那颗空态字面量，
/// 是 <c>I18nWiringGateTests</c> 那份<b>在案豁免</b>的第①／②类，本批走正路收回（下拉＝数据项＋模板，空态＝宿主交 LocMessage）。</para>
/// <para>尺＝<see cref="SourceGate.ScreenWords"/>（抹注释后逐行取汉字片段、排除日志头那一行）＋
/// <see cref="SourceGate.KeyedCallSites"/>（按括号深度切实参）＋ <see cref="SourceGate.RowSlots"/>；⛔ 在本档再写一份字符类（规则 #86）。
/// 剩余量的数来自 <c>bin/WI67/census_67n.txt</c> 这一跑，⛔ 凭记忆（规则 #129）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class SearchWordGateTests : IDisposable
{
    private const string PageVm = "src/StarMark.UI/ViewModels/SearchPageViewModel.cs";
    private const string WidgetVm = "src/StarMark.UI/ViewModels/SearchWidgetViewModel.cs";
    private const string PageXaml = "src/StarMark.UI/Views/SearchPage.xaml";
    private const string PageCs = "src/StarMark.UI/Views/SearchPage.xaml.cs";
    private const string WidgetXaml = "src/StarMark.UI/Views/SearchWidget.xaml";
    private const string WidgetCs = "src/StarMark.UI/Views/SearchWidget.xaml.cs";

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => Code(ReadRepoFile(rel));

    /// <summary>本批落表那一族（名单⛔ 手抄，从<b>加载好的表</b>里按前缀现读；颗数一漂这格就红）。</summary>
    private static string[] SliceKeys()
        => UiStrings.All.Where(e => e.Key.StartsWith("search.empty.", StringComparison.Ordinal)
                                   || e.Key.StartsWith("search.header.", StringComparison.Ordinal)
                                   || e.Key.StartsWith("widget.sw.empty.", StringComparison.Ordinal)
                                   || e.Key.StartsWith("widget.sw.sort.", StringComparison.Ordinal)
                                   || e.Key is "search.loadMore" or "search.loadingMore" or "search.failed" or "search.tagJoin")
            .Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();

    private static string[] AskedCorpus()
        => ReadRepoUnder("src").Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Select(f => f.Text).ToArray();

    // ────────── ① 整档口径：屏幕上⛔ 留一句写死的汉字 ──────────

    /// <summary>
    /// <b>这一族的四档⛔ 再带屏幕上会念出来的汉字字面</b>（整档口径＝⛔ 只钉我点到的那一行，下一颗漏网的自己红）。
    /// <para>⚠ 参数递的是 <see cref="SourceGate.Code"/> 那一份：doc-comment 里的汉字是给人的理由，⛔ 第二个出口（规则 #131）。</para>
    /// </summary>
    [Theory]
    [InlineData(PageVm)]
    [InlineData(WidgetVm)]
    [InlineData(PageCs)]
    [InlineData(WidgetCs)]
    public void AScreenFacingFileInTheSearchFamilyKeptNoHardCodedWords(string rel)
    {
        var words = ScreenWords(CodeOf(rel));
        Assert.True(words.Count == 0, $"{rel} 仍留着非日志的屏幕上汉字片段 {words.Count} 处：{string.Join(" / ", words.Take(5))}");
    }

    // ────────── ② 取词时机：产地⛔ 念，贴上去那一刻才念 ──────────

    /// <summary><b>两颗 VM 交出的是没取过词的那句</b>：里头⛔ 有 <c>.Render()</c>／<c>Locale.T(</c>／<c>Locale.Fmt(</c>。
    /// <para>一旦"顺手先把句子念好"，语言就被冻在算出它的那一刻，换语言只重贴外层——坑表 #276 那一族，
    /// 而形状闸门（上一格）看不见，因为它只数汉字。</para></summary>
    [Theory]
    [InlineData(PageVm)]
    [InlineData(WidgetVm)]
    public void TheSearchViewModelsNeverTakeWordsThemselves(string rel)
    {
        var code = CodeOf(rel);
        foreach (var banned in new[] { "Locale.T(", "Locale.Fmt(", ".Render()" })
            Assert.DoesNotContain(banned, code, StringComparison.Ordinal);
    }

    // ────────── ③ 表里那一族每一行都有人在问 ──────────

    /// <summary>
    /// <b>本批落表的每一行，代码里真的在问它</b>（⛔ 留一颗没人念的死键进表）。
    /// <para>语料⛔ 把表自己算进来（否则"这一行没人问"会被表里那一行自己满足＝仪器骗人，台架规则 #95/#102）；
    /// 语料的形状要自己作证：收得到两颗 VM，且真的没把表收进来。</para>
    /// </summary>
    [Fact]
    public void EveryRowThisSliceShippedIsAskedFor()
    {
        var keys = SliceKeys();
        Assert.Equal(20, keys.Length);          // 名单漂了要出声：⛔ 把"没红"当"过了"
        var corpus = AskedCorpus();
        Assert.Contains(corpus, t => t.Contains("SearchPageViewModel", StringComparison.Ordinal));
        var all = string.Join("\n", corpus);
        var unasked = keys.Where(key => !all.Contains(@"""" + key + @"""", StringComparison.Ordinal)).ToArray();
        Assert.Equal(Array.Empty<string>(), unasked);
    }

    // ────────── ④ 一句两个主人：那两句由页与格共用一颗键 ──────────

    /// <summary>
    /// <b>「没有同时带 … 的条目」与「未找到与 … 相关的条目」这两个 VM 从前各抄一份，今天只剩一行两宿主</b>。
    /// <para>这一格钉的是<b>宿主颗数</b>而⛔ 句子本身：多一颗宿主＝有人又抄了一份，少一颗＝那句在某一屏上没了主人。
    /// 标签那一串的连接符也一起交表（<c>search.tagJoin</c>＋<c>LocJoin</c>），⛔ 在 C# 里 <c>string.Join(" + ", …)</c> 先念完
    /// （先例＝<c>capture.bar.nameJoin</c>，规则 #100）。</para>
    /// </summary>
    [Fact]
    public void TheTwoSharedSentencesHaveOneRowAndExactlyTwoHosts()
    {
        var corpus = string.Join("\n", AskedCorpus());
        foreach (var key in new[] { "search.empty.tagAnd", "search.empty.noMatch" })
            Assert.Equal(2, Count(corpus, @"""" + key + @""""));
        Assert.Equal(2, Count(corpus, @"""search.tagJoin"""));
        foreach (var rel in new[] { PageVm, WidgetVm })
            Assert.Equal(0, Count(CodeOf(rel), @"string.Join("" + """));
    }

    /// <summary>两颗失败那句各在<b>一处</b>：页那颗走空态行，格那颗走它自己那句（措辞各归各的屏，⛔ 共用一句假话）。</summary>
    [Fact]
    public void EachFailureSentenceIsAskedFromItsOwnHost()
    {
        Assert.Equal(2, Count(CodeOf(PageVm), @"""search.failed"""));
        Assert.Equal(1, Count(CodeOf(WidgetVm), @"""widget.sw.empty.failed"""));
        Assert.DoesNotContain(@"""search.failed""", CodeOf(WidgetVm), StringComparison.Ordinal);
    }

    // ────────── ⑤ 死出口：那颗没人读的 StatusText ──────────

    /// <summary>
    /// <b>那颗从来没人绑定的 <c>StatusText</c> ⛔ 回来</b>（本批删掉＝那一族 6 行汉字的真去处）。
    /// <para>为什么单独一格：这种形状⛔ 是"翻译漏了一颗"，而是<b>有话没人听</b>——它每轮都写、每轮都没人读，
    /// 于是"搜索失败"在屏幕上等于没发生。与 ✅片5c／✅片7 删的那几颗死出口同一族（先例＝<c>MainViewModel.cs</c>
    /// 那颗同样无人绑定的 StatusText，当年也是整颗摘掉）。</para>
    /// <para>两个方向都钉：① VM 里⛔ 再有那颗属性；② 标记里⛔ 再有人把它绑回去（那种写法编译得过，屏幕上是一行没人读的字符串）。</para>
    /// <para>⚠ ①那颗的<b>形状</b>要按<b>大小写无关</b>数：字段在源码里写的是 <c>_statusText</c>，而 <c>StatusText</c> 那颗属性是
    /// <c>[ObservableProperty]</c> 生成的、根本不住源码文本里。第一跑这里数的是大小写敏感的 <c>"StatusText"</c>，于是台架 A7
    /// 把那颗字段原样补回去<b>编译通过、判据全绿</b>＝死出口回来了而这格看不见（坑表 #531／同一把尺的两种瞎法见 #423）。</para>
    /// </summary>
    [Fact]
    public void TheSearchPageKeepsNoUnboundStatusOutlet()
    {
        Assert.Empty(Regex.Matches(CodeOf(PageVm), "status_?text", RegexOptions.IgnoreCase).Cast<Match>());
        Assert.DoesNotContain("{x:Bind ViewModel.StatusText", ReadRepoFile(PageXaml), StringComparison.Ordinal);
        // 坏消息现在只有一个去处：那一屏唯一看得见的空态行（两颗 catch 各写一次）。
        Assert.Equal(2, Count(CodeOf(PageVm), @"EmptyHint = LocMessage.Of(""search.failed"", ex.Message);"));
    }

    // ────────── ⑥ 看不见的句子⛔ 造键 ──────────

    /// <summary>
    /// <b>那三行不带读数的段名⛔ 进表</b>：可见性绑的是 <c>HasExact</c>／<c>HasRelated</c>，而它们恰在"没有那颗段名可读"时为假
    /// ⇒ 表里 <c>search.header.</c> 那一族只有<b>三颗带槽的</b>行，浏览态那颗精确匹配段交的是 <c>null</c>。
    /// <para>这一格拦的是"把默认值也翻一遍"这种注水：多造一行没人念的键＝下一批复算时以为那一族还有两处没做完。</para>
    /// </summary>
    [Fact]
    public void NoRowWasInventedForASentenceThatNeverReachesTheScreen()
    {
        var headerRows = SliceKeys().Where(k => k.StartsWith("search.header.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "search.header.exactCount", "search.header.recentCount", "search.header.relatedCount" }, headerRows);
        foreach (var key in headerRows) Assert.Equal(1, RowSlots(key));
        Assert.Equal(1, Count(CodeOf(PageVm), "ExactHeader = null;"));
    }

    // ────────── ⑦ 槽与实参成对 ──────────

    /// <summary>
    /// <b>每一颗带槽的话，代码递出的实参数＝表里声明的槽数</b>（坑表 #393 那一族：槽数对得上、递过去的却是拼好的串）。
    /// <para>现场颗数自己作证（少于 18 处说明这把尺瞎了，⛔ 让"看不见"读成"没有"＝台架规则 #188）。</para></summary>
    [Fact]
    public void EveryCallHandsExactlyTheSlotsItsRowDeclares()
    {
        var offenders = new List<string>();
        var watched = 0;
        foreach (var rel in new[] { PageVm, WidgetVm })
            foreach (var (key, args, line) in KeyedCallSites(CodeOf(rel)))
            {
                var slots = RowSlots(key);
                watched++;
                if (args != slots) offenders.Add($"{rel}:{line} 的 {key} 表里 {slots} 颗槽，代码递了 {args} 颗实参");
            }
        Assert.True(watched >= 18, $"这一族只读到 {watched} 处带字面键的 LocMessage.Of 调用（少于 18）⇒ 判据自己失效了");
        Assert.Equal(Array.Empty<string>(), offenders);
        // 那颗三元的选键行（加载更多／加载中…）不归 KeyedCallSites 那把尺，所以在这里逐颗钉它⛔ 槽。
        Assert.Equal(0, RowSlots("search.loadMore"));
        Assert.Equal(0, RowSlots("search.loadingMore"));
        Assert.Contains(@"LocMessage.Of(IsLoadingMore ? ""search.loadingMore"" : ""search.loadMore"")", CodeOf(PageVm));
    }

    // ────────── ⑧ 成对判据：换实参就换读数 ──────────

    /// <summary>
    /// <b>读数那颗⛔ 抄进句子，也⛔ 抄进表</b>：递 <c>99</c> 就必须念出 <c>99</c>、屏幕上⛔ 留着上一个读数、也⛔ 露出 <c>{0}</c>。
    /// <para>这一格与上面那颗计数判据<b>成对</b>（规则 #124）：颗数与形状都抓不到"表里那一行把 7 写死了"，
    /// 而这一条抓得到（坑表 #530 那一课的第二次兑现：⛔ 扫哪几档就⛔ 拦得住那几档之外的第二个主人）。</para>
    /// <para>三语各念一次且两两⛔ 相等＝那句真的按语言各说各的；⛔ 把某一句的精确措辞钉死在这里——
    /// 表里换个措辞⛔ 该红（对照件 C2），红的是"数没了"而⛔ "字变了"。</para>
    /// </summary>
    [Theory]
    [InlineData("search.header.exactCount")]
    [InlineData("search.header.relatedCount")]
    [InlineData("search.header.recentCount")]
    [InlineData("search.failed")]
    [InlineData("widget.sw.empty.failed")]
    public void ARowWithAReadoutChangesWhenTheReadoutChanges(string key)
    {
        var readings = new List<string>();
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            var seven = LocMessage.Of(key, 7).Render();
            var ninetyNine = LocMessage.Of(key, 99).Render();
            Assert.Contains("99", ninetyNine, StringComparison.Ordinal);   // 换实参就换读数
            Assert.DoesNotContain("99", seven, StringComparison.Ordinal);   // 上一个读数⛔ 留在句子里
            Assert.DoesNotContain("{0}", seven, StringComparison.Ordinal);  // 填空没做＝屏幕上挂着键的形状
            if (language == AppLanguage.English) Assert.Empty(Cjk.Matches(seven));   // 拉丁文那句里⛔ 夹一颗汉字
            readings.Add(seven);
            Assert.Equal(readings.Count, readings.Distinct(StringComparer.Ordinal).Count());   // 三语各说各的
        }
    }

    // ────────── ⑨ 宿主那一拍与那颗门 ──────────

    /// <summary>
    /// <b>主窗那一页把四行交一颗门贴，标记里那四条成品串绑定⛔ 回来</b>（✅片8 那颗 <c>WidgetsTradeoffHint</c> 同一形状）。
    /// <para>两颗可见性绑定留着是对的：它们绑的是 bool，⛔ 语言。⚠ 计数型钉写的是<b>每一行</b>，⛔ "看起来差不多"的总数。</para>
    /// </summary>
    [Fact]
    public void ThePagePastesItsFourRowsFromOneHostBeat()
    {
        var code = CodeOf(PageCs);
        foreach (var pair in new[]
                 {
                     @"LocText.SetMessage(EmptyHintText, ViewModel.EmptyHint);",
                     @"LocText.SetMessage(ExactHeaderText, ViewModel.ExactHeader);",
                     @"LocText.SetMessage(RelatedHeaderText, ViewModel.RelatedHeader);",
                     @"LocText.SetMessage(LoadMoreButton, ViewModel.LoadMoreLabel);",
                 })
            Assert.Equal(1, Count(code, pair));
        Assert.Equal(1, Count(code, "private void ApplySearchWords()"));
        Assert.Equal(2, Count(code, "ApplySearchWords();"));   // 构造函数那一拍＋属性变了再来一拍
        var xaml = ReadRepoFile(PageXaml);
        foreach (var gone in new[] { "ViewModel.EmptyHint", "ViewModel.ExactHeader", "ViewModel.RelatedHeader", "ViewModel.LoadMoreLabel" })
            Assert.DoesNotContain("{x:Bind " + gone, xaml, StringComparison.Ordinal);
        foreach (var named in new[] { "EmptyHintText", "ExactHeaderText", "RelatedHeaderText", "LoadMoreButton" })
            Assert.Contains(@"x:Name=" + '"' + named + '"', xaml, StringComparison.Ordinal);
        // ⛔ 新主人：本页⛔ 为这几行再挂一位 Locale.Changed，重贴归 LocText 那份名单。
        Assert.Equal(0, Count(code, "Locale.Changed"));
    }

    /// <summary><b>那颗 VM 是单例而本页每次导航新建 ⇒ 订阅与摘掉成对</b>（坑表 #386：这一族的失效是挂了没人摘）。</summary>
    [Fact]
    public void ThePageUnsubscribesWhatItSubscribed()
    {
        var code = CodeOf(PageCs);
        Assert.Equal(1, Count(code, "ViewModel.PropertyChanged += OnViewModelPropertyChanged;"));
        Assert.Equal(1, Count(code, "ViewModel.PropertyChanged -= OnViewModelPropertyChanged;"));
    }

    // ────────── ⑩ 组件那颗下拉与那颗空态：在案豁免收回 ──────────

    /// <summary>
    /// <b>排序下拉走「数据项＋模板」：措辞住表里，身份（relevance／recent／name）住在数据项的 <c>Value</c> 里</b>。
    /// <para>两个方向各拦一种坏法：① 项写回标记＝闭着的那一格停在旧语言（#272／#275，那份在案豁免本批收回）；
    /// ② 拿屏幕上那行字当排序键＝换了语言之后查询参数再也认不出来（<c>ItemRepositoryTests</c> 钉着那几个 ASCII 键）。</para>
    /// <para>那颗「名称」⛔ 再造一行，复用主窗排序档已在册的 <c>sort.name</c>＝同一句措辞⛔ 两个主人。</para>
    /// </summary>
    [Fact]
    public void TheSortComboHandsIdentityNotWords()
    {
        var code = CodeOf(WidgetCs);
        foreach (var row in new[]
                 {
                     @"LocOption.T(""widget.sw.sort.relevance"", ""relevance"")",
                     @"LocOption.T(""widget.sw.sort.recent"", ""recent"")",
                     @"LocOption.T(""sort.name"", ""name"")",
                 })
            Assert.Equal(1, Count(code, row));
        Assert.Contains("option.Value is string key", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SortKeys", code, StringComparison.Ordinal);   // 那颗按下标认键的旧形状⛔ 回来
        var xaml = ReadRepoFile(WidgetXaml);
        Assert.DoesNotContain("<ComboBoxItem", xaml, StringComparison.Ordinal);
        Assert.Contains(@"ItemsSource=""{x:Bind SortOptions, Mode=OneTime}""", xaml, StringComparison.Ordinal);
        Assert.Contains(@"ItemTemplate=""{StaticResource LocOptionTemplate}""", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>组件那颗空态只许有一个主人</b>：标记里那句字面量删了（它由 code-behind 每次重写＝留着就是没人读的副本，
    /// 挂了静态键更坏＝换语言把带值那句盖回兜底那句），代码只许走那颗门。
    /// </summary>
    [Fact]
    public void TheWidgetEmptyHintHasOneOwnerAndGoesThroughTheDoor()
    {
        var xaml = ReadRepoFile(WidgetXaml);
        Assert.DoesNotContain(@"Text=""输入关键词或选择标签开始搜索""", xaml, StringComparison.Ordinal);
        var code = CodeOf(WidgetCs);
        Assert.Equal(1, Count(code, "LocText.SetMessage(EmptyHint, ViewModel.EmptyHint);"));
        Assert.Equal(0, Count(code, "EmptyHint.Text ="));
        // 兜底那句由构造那一拍贴进去（删了标记里的字面量之后，第一次搜索之前也得有话可念）。
        Assert.Contains("UpdateEmptyHint();", code, StringComparison.Ordinal);
    }
}
