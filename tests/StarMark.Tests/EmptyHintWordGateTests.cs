#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.I18n;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-67 片9c 第一刀（P-188 第六刀）的闸门：<b>「加载失败」那一句全仓只留一行，且各页空态那句由 VM 交「键＋实参」、取词只许发生在贴上去那一刻</b>。
/// <para>射程按<b>屏幕上那一处宿主</b>划（台架规则 #115）＝隐藏条目／标签／活动／文件夹树／剪贴板那五页的空态行，
/// 加上世界时钟组件那一行；文件夹页还多一处<b>代码里建的</b>失败行（手风琴展开时那一段）。</para>
/// <para><b>本批读码读出来的三件事，各配一格</b>：
/// ① 那句「加载失败」从前在<b>六档里各写一份</b>（四档还是 VM 里当场 <c>Locale.Fmt</c> 过的成品字），
/// 现在只剩 <c>common.loadFailed</c> 一颗、七个宿主（<c>clip.status.load</c> 同批删掉＝一句话⛔ 两个名字）；
/// ② 那五页的 <c>EmptyHint</c> 全是 <c>string</c> 出口 ⇒ 语言被冻在"算出它的那一刻"，本批一起升成 <c>LocMessage?</c>；
/// ③ <c>FolderTreePageViewModel.HasEmptyHint</c> 从前按 <c>string.IsNullOrEmpty</c> 判"有没有话要说"，
/// 现在判的是<b>那颗键在不在</b>（✅VQ 那条口径：有话要说与有东西可删必须是两颗判据，⛔ 拿念出来的字当身份）。</para>
/// <para>尺＝<see cref="SourceGate.ScreenWords"/>（抹注释后逐行取汉字片段、排除日志头那一行）＋
/// <see cref="SourceGate.KeyedCallSites"/>（按括号深度切实参）＋ <see cref="SourceGate.RowSlots"/>；⛔ 在本档再写一份字符类（规则 #86）。
/// 剩余量的数来自 <c>bin/WI67/probe_67q.txt</c> 这一跑，⛔ 凭记忆（规则 #129）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class EmptyHintWordGateTests : IDisposable
{
    private const string HiddenVm = "src/StarMark.UI/ViewModels/HiddenPageViewModel.cs";
    private const string TagsVm = "src/StarMark.UI/ViewModels/TagsPageViewModel.cs";
    private const string ActivityVm = "src/StarMark.UI/ViewModels/ActivityPageViewModel.cs";
    private const string FolderVm = "src/StarMark.UI/ViewModels/FolderTreePageViewModel.cs";
    private const string ClipVm = "src/StarMark.UI/ViewModels/ClipboardPageViewModel.cs";
    private const string ClockVm = "src/StarMark.UI/ViewModels/WorldClockViewModel.cs";

    private const string HiddenCs = "src/StarMark.UI/Views/HiddenPage.xaml.cs";
    private const string TagsCs = "src/StarMark.UI/Views/TagsPage.xaml.cs";
    private const string ActivityCs = "src/StarMark.UI/Views/ActivityPage.xaml.cs";
    private const string FolderCs = "src/StarMark.UI/Views/FolderTreePage.xaml.cs";
    private const string ClipCs = "src/StarMark.UI/Views/ClipboardPage.xaml.cs";
    private const string ClockCs = "src/StarMark.UI/Views/WorldClockWidget.xaml.cs";

    private static readonly string[] Hosts = [HiddenCs, TagsCs, ActivityCs, FolderCs, ClipCs, ClockCs];

    /// <summary>六档 ViewModel（本批那颗空态出口的主人）。</summary>
    private static readonly string[] Vms = [HiddenVm, TagsVm, ActivityVm, FolderVm, ClipVm, ClockVm];

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => Code(ReadRepoFile(rel));

    /// <summary>本批落表那一族（名单⛔ 手抄，从<b>加载好的表</b>里现读；颗数一漂这格就红）。</summary>
    private static string[] SliceKeys()
        => UiStrings.All.Where(e => e.Key is "common.loadFailed" or "widget.clock.noRows"
                                   || e.Key.StartsWith("page.", StringComparison.Ordinal))
            .Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();

    /// <summary>语料⛔ 把表自己算进来（否则"这一行没人问"会被表里那一行自己满足＝仪器骗人，台架规则 #95/#102）。</summary>
    private static string[] AskedCorpus()
        => ReadRepoUnder("src").Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Select(f => f.Text).ToArray();

    // ────────── ① 整档口径：屏幕上⛔ 留一句写死的汉字 ──────────

    /// <summary>
    /// <b>六档宿主与五档 VM ⛔ 再带屏幕上会念出来的汉字字面</b>（整档口径＝⛔ 只钉我点到的那一行，下一颗漏网的自己红）。
    /// <para>⚠ 参数递的是 <see cref="SourceGate.Code"/> 那一份：doc-comment 里的汉字是给人的理由，⛔ 第二个出口（规则 #131）。</para>
    /// <para>两颗例外各自单独成格（②③），因为它们的剩量是<b>读数已经量过的两处/一处</b>，⛔ 混在这一格里读成"还剩三行屏幕字"。</para>
    /// </summary>
    [Theory]
    [InlineData(HiddenVm)]
    [InlineData(TagsVm)]
    [InlineData(ActivityVm)]
    [InlineData(ClipVm)]
    [InlineData(HiddenCs)]
    [InlineData(TagsCs)]
    [InlineData(ActivityCs)]
    [InlineData(FolderCs)]
    [InlineData(ClipCs)]
    [InlineData(ClockCs)]
    public void AScreenFacingFileInTheEmptyHintFamilyKeptNoHardCodedWords(string rel)
    {
        var words = ScreenWords(CodeOf(rel));
        Assert.True(words.Count == 0, $"{rel} 仍留着非日志的屏幕上汉字片段 {words.Count} 处：{string.Join(" / ", words.Take(5))}");
    }

    /// <summary>
    /// <b>文件夹那颗 VM 只剩两行汉字，而且两行都⛔ 上屏</b>：<c>StartupProfile.Mark</c> 的段名只住日志（✅P-191 他裁「文字可删去」，
    /// 界面那一行早改成"没有话要说"），日志按既定口径⛔ 交表。
    /// <para>为什么数着钉而⛔ 把这颗加进 <see cref="SourceGate.LogHeads"/>：那颗共用尺一放宽，全仓每道字闸门就都跟着少看一类行（坑表 #423 同族——一把尺⛔ 为个别宿主弯）。</para>
    /// </summary>
    [Fact]
    public void TheFolderViewModelsTwoLeftoverWordsAreBothLogOnlySegmentNames()
    {
        var code = CodeOf(FolderVm);
        Assert.Equal(2, ScreenWords(code).Count);
        Assert.Equal(2, Count(code, "StartupProfile.Mark(\""));
        // 这一族的两句话（空态与失败）都在门上，⛔ 再有一行写死的字。
        Assert.Equal(0, Count(code, "EmptyHint = \""));
    }

    // ────────── ② 一句话⛔ 两个名字：那句「加载失败」收成一颗 ──────────

    /// <summary>
    /// <b>那一句在六档里各写一份，今天只剩一行、七个宿主</b>（隐藏／标签／活动各一颗 catch，文件夹两颗 catch，剪贴板一颗 catch，文件夹页代码里建的那一行一颗）。
    /// <para>两个方向都钉：① 旧键 <c>clip.status.load</c> 在<b>代码与表里</b>都归零（留着＝改了其中一颗另一颗就在说假话）；
    /// ② 全仓⛔ 再有一句中文写着"加载失败："的成品串（表里那一行除外，它是唯一出处）。</para>
    /// </summary>
    [Fact]
    public void TheSharedLoadFailedSentenceIsOneRowWithSevenHosts()
    {
        var corpus = string.Join("\n", AskedCorpus());
        Assert.Equal(7, Count(corpus, @"""common.loadFailed"""));
        Assert.Equal(0, Count(corpus, "clip.status.load"));
        Assert.False(UiStrings.TryGet("clip.status.load", out _), "那颗旧键还在表里＝一句话两个名字");
        Assert.Single(UiStrings.All.Where(e => e.Zh == "加载失败：{0}").ToArray());
        foreach (var rel in Vms.Concat(Hosts))
            Assert.Equal(0, Count(CodeOf(rel), "加载失败"));   // 汉字⛔ 再出现在宿主那一侧（表里那颗是唯一出处）
    }

    // ────────── ③ 取词时机：产地⛔ 念，贴上去那一刻才念 ──────────

    /// <summary>
    /// <b>五档 VM 交出的是没取过词的那句</b>：里头⛔ 有 <c>.Render()</c>／<c>Locale.T(</c>／<c>Locale.Fmt(</c>。
    /// <para>一旦"顺手先把句子念好"，语言就被冻在算出它的那一刻，换语言只重贴外层——坑表 #276 那一族，
    /// 而形状闸门（①那一格）看不见，因为它只数汉字。</para>
    /// <para>✅片9c 第二刀把<b>六档</b>全数进来：剪贴板那颗 VM 从前有 9 处当场念（8 处 <c>Locale.T/Fmt</c>＋1 处 <c>.Render()</c>），
    /// 现在它的状态行与空态行都交「键＋实参」，取词推迟到 <c>LocText.SetMessage</c> 那一拍（<see cref="TheClipboardStatusLineHasOneDoorAndNoReadingAtTheSource"/>）。</para>
    /// <para>⚠ <b>世界时钟那颗 VM 不在这一格</b>（✅片9c 第三刀之后）：它屏上那一行住在 <c>DataTemplate</c> 里，走的是
    /// ✅WI-40-3b 机制 (C)——<b>显示侧 getter 里那颗 <c>.Render()</c> 正是机制规定的位置</b>（"念出来"发生在被绑上去那一刻）。
    /// 那一族由 <c>TemplateRowWordGateTests</c> 逐颗钉（那里数 <c>.Render()</c>＝恰一颗、且 <c>Locale.T/Fmt</c>＝0），
    /// ⛔ 在这一格顺手放宽共用的禁词表——那是全仓每道字闸门共用的尺（坑表 #423 同族）。</para>
    /// </summary>
    [Theory]
    [InlineData(HiddenVm)]
    [InlineData(TagsVm)]
    [InlineData(ActivityVm)]
    [InlineData(FolderVm)]
    [InlineData(ClipVm)]
    public void TheseViewModelsNeverTakeWordsThemselves(string rel)
    {
        var code = CodeOf(rel);
        foreach (var banned in new[] { "Locale.T(", "Locale.Fmt(", ".Render()" })
            Assert.DoesNotContain(banned, code, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>剪贴板那一页的状态行也交「键＋实参」，本页⛔ 一处当场取词</b>（✅片9c 第二刀）。
    /// <para>从前这 9 处在 VM 里 <c>Locale.T/Fmt</c> 念成成品串：那句话的语言被冻在"用户点了那一下"，
    /// 而状态行会一直挂着到用户下一次操作才换——换语言那一刻它是<b>唯一还在念旧语言的那一行</b>。</para>
    /// <para>⚠ 计数只落<b>本页那一颗出口</b>：<c>clip.status.copyFail</c> 在计算器组件里另有两处当场念
    /// （✅WI-45 裁的"瞬时提示⛔ 挂名单"，两种宿主两种口径），⛔ 拿全仓那颗键的名字当这一格的靶子（名册 263）。</para>
    /// </summary>
    [Fact]
    public void TheClipboardStatusLineHasOneDoorAndNoReadingAtTheSource()
    {
        var vm = CodeOf(ClipVm);
        Assert.Equal(0, Count(vm, "Locale.T(") + Count(vm, "Locale.Fmt(") + Count(vm, ".Render()"));
        Assert.Equal(1, Count(vm, @"private LocMessage? _statusText;"));
        Assert.Equal(1, Count(vm, "public bool HasStatus => StatusText is not null;"));    // 判那颗键在不在，⛔ 比念出来的字
        Assert.Equal(1, Count(vm, "partial void OnStatusTextChanged(LocMessage? value)"));
        Assert.Equal(9, Count(vm, "StatusText = "));    // 八颗键＋那颗"图已不在本机"交回策略层的 LocMessage
        Assert.Equal(1, Count(vm, @"EmptyHint = LocMessage.Of(""common.loadFailed"", ex.Message);"));

        var host = CodeOf(ClipCs);
        Assert.Equal(1, Count(host, "LocText.SetMessage(StatusLine, ViewModel.StatusText);"));
        Assert.Equal(2, Count(host, "ApplyStatusLine();"));   // 构造那一拍＋属性变了再来一拍
        Assert.Equal(0, Count(host, "Locale.T(") + Count(host, "Locale.Fmt("));   // 暂停那两句也交键（从前宿主自己念）
        Assert.Equal(0, Count(host, "Locale.Changed"));   // ⛔ 新主人：换语言的重贴归 LocText 那一份名单

        var xaml = ReadRepoFile(ClipCs[..^3]);
        Assert.DoesNotContain("{x:Bind ViewModel.StatusText", xaml, StringComparison.Ordinal);
        Assert.Contains(@"x:Name=""StatusLine""", xaml, StringComparison.Ordinal);
        Assert.Contains(@"Visibility=""{x:Bind ViewModel.HasStatus, Mode=OneWay", xaml, StringComparison.Ordinal);  // 那颗 bool 绑定留着
    }

    // ────────── ④ 出口类型：⛔ 一颗 string 出口留着 ──────────

    /// <summary>
    /// <b>六档 VM 的空态出口全是 <c>LocMessage?</c></b>（一颗 <c>string</c> 出口＝那句话又被冻在上一次算出它的语言里，与 ✅片8 那颗 <c>WidgetsLoadHint</c> 同一形状）。
    /// <para>⚠ 数的是<b>声明形状</b>而⛔ 属性名：<c>StatusText</c> 那一族栽在"属性由 <c>[ObservableProperty]</c> 生成、源码里只有 <c>_statusText</c>"（坑表 #531），
    /// 所以这里连字段声明一起钉，且用 <c>LocMessage?</c> 那颗<b>类型</b>当判据。</para>
    /// </summary>
    [Fact]
    public void EveryEmptyHintOutletIsAKeyNotASentence()
    {
        foreach (var rel in Vms)
            Assert.Equal(1, Count(CodeOf(rel), @"private LocMessage? _emptyHint;"));
        // 文件夹那颗"有没有话要说"改判那颗键在不在，⛔ 再拿念出来的字当身份（✅VQ：有话要说与有东西可删是两颗 bool）。
        Assert.Equal(1, Count(CodeOf(FolderVm), "public bool HasEmptyHint => EmptyHint is not null;"));
        Assert.Equal(1, Count(CodeOf(FolderVm), "partial void OnEmptyHintChanged(LocMessage? value)"));
    }

    // ────────── ⑤ 表里那一族每一行都有人在问 ──────────

    /// <summary><b>本批落表的每一行，代码里真的在问它</b>（⛔ 留一颗没人念的死键进表；名单漂了也要出声）。</summary>
    [Fact]
    public void EveryRowThisSliceShippedIsAskedFor()
    {
        var keys = SliceKeys();
        Assert.Equal(7, keys.Length);
        var corpus = AskedCorpus();
        Assert.Contains(corpus, t => t.Contains("HiddenPageViewModel", StringComparison.Ordinal));
        Assert.DoesNotContain(corpus, t => t.Contains("new Entry(\"common.loadFailed\"", StringComparison.Ordinal));   // ⛔ 把表自己算进语料
        var all = string.Join("\n", corpus);
        var unasked = keys.Where(key => !all.Contains(@"""" + key + @"""", StringComparison.Ordinal)).ToArray();
        Assert.Equal(Array.Empty<string>(), unasked);
    }

    // ────────── ⑥ 槽与实参成对 ──────────

    /// <summary>
    /// <b>每一颗带槽的话，代码递出的实参数＝表里声明的槽数</b>（坑表 #393 那一族：槽数对得上、递过去的却是拼好的串）。
    /// <para>空态那六颗⛔ 带槽（递了实参＝屏幕上多出一段没人负责的话），失败那颗正好一颗 <c>ex.Message</c>。</para>
    /// </summary>
    [Fact]
    public void EveryCallHandsExactlyTheSlotsItsRowDeclares()
    {
        var offenders = new List<string>();
        var watched = 0;
        foreach (var rel in Vms.Concat(Hosts))
            foreach (var (key, args, line) in KeyedCallSites(CodeOf(rel)))
            {
                var slots = RowSlots(key);
                watched++;
                if (args != slots) offenders.Add($"{rel}:{line} 的 {key} 表里 {slots} 颗槽，代码递了 {args} 颗实参");
            }
        Assert.True(watched >= 7, $"这一族只读到 {watched} 处带字面键的 LocMessage.Of 调用（少于 7）⇒ 判据自己失效了");
        Assert.Equal(Array.Empty<string>(), offenders);
        foreach (var key in SliceKeys().Where(k => k != "common.loadFailed")) Assert.Equal(0, RowSlots(key));
        Assert.Equal(1, RowSlots("common.loadFailed"));
    }

    // ────────── ⑦ 宿主那一拍与那颗门 ──────────

    /// <summary>
    /// <b>六处宿主各把那一句交一颗门贴，标记里那条成品串绑定⛔ 回来</b>（✅片8／✅片9b 同一形状）。
    /// <para>两颗可见性绑定留着是对的：它们绑的是 bool，⛔ 语言。⚠ 计数型钉写的是<b>每一处宿主</b>，⛔ "看起来差不多"的总数。</para>
    /// </summary>
    [Theory]
    [InlineData(HiddenCs)]
    [InlineData(TagsCs)]
    [InlineData(ActivityCs)]
    [InlineData(FolderCs)]
    [InlineData(ClipCs)]
    [InlineData(ClockCs)]
    public void EachHostPastesItsEmptyRowFromOneBeat(string csRel)
    {
        var code = CodeOf(csRel);
        Assert.Equal(1, Count(code, "LocText.SetMessage(EmptyHintText, ViewModel.EmptyHint);"));
        Assert.Equal(1, Count(code, "private void ApplyEmptyHint()"));
        Assert.Equal(2, Count(code, "ApplyEmptyHint();"));   // 构造那一拍＋属性变了再来一拍
        var xaml = ReadRepoFile(csRel[..^3]);   // "X.xaml.cs" 去掉末尾那颗 ".cs" 就是标记本体
        Assert.DoesNotContain("{x:Bind ViewModel.EmptyHint", xaml, StringComparison.Ordinal);
        Assert.Contains(@"x:Name=""EmptyHintText""", xaml, StringComparison.Ordinal);
        // ⛔ 为"这一行空态"再挂一位主人：重贴归 LocText 那份名单。
        // ⚠ 唯一两处例外＝✅片9c 第三刀给"模板行"挂的那一对（活动页与世界时钟组件各一次挂／一次摘，钉在
        //   TemplateRowWordGateTests.EveryNewLocaleOwnerUnsubscribesWhatItSubscribed 与 I18nWiringGateTests 那份主人名单里）：
        //   行模板上的字⛔ 进得了那颗门（每行一颗、行会重建），只能由宿主逐行重念。
        Assert.Equal(csRel == ActivityCs || csRel == ClockCs ? 2 : 0, Count(code, "Locale.Changed"));
    }

    /// <summary><b>订阅与摘掉成对</b>（坑表 #386：这一族的失效是挂了没人摘；标签页与文件夹页那颗 VM 是单例，更要成对）。</summary>
    [Fact]
    public void EachHostUnsubscribesWhatItSubscribed()
    {
        foreach (var rel in Hosts)
        {
            var code = CodeOf(rel);
            Assert.Equal(1, Count(code, "ViewModel.PropertyChanged += OnEmptyHintChanged;"));
            Assert.Equal(1, Count(code, "ViewModel.PropertyChanged -= OnEmptyHintChanged;"));
        }
    }

    /// <summary>
    /// <b>失败那句必须看得见</b>：那一行的可见性绑的是 <c>HasItems</c>／<c>HasEmptyHint</c>，
    /// 所以写话之前必须先把那颗 bool 复位——否则屏幕上是一页空白加一句藏在门外的报错（批次 IJ／✅VQ 同一颗判据）。
    /// </summary>
    [Theory]
    [InlineData(HiddenVm)]
    [InlineData(ActivityVm)]
    [InlineData(ClipVm)]
    public void TheFailureRowResetsItsVisibilityBeforeItSpeaks(string rel)
    {
        var code = CodeOf(rel);
        var reset = code.IndexOf("HasItems = false;", StringComparison.Ordinal);
        var speaks = code.IndexOf(@"EmptyHint = LocMessage.Of(""common.loadFailed"", ex.Message);", StringComparison.Ordinal);
        Assert.True(speaks >= 0, $"{rel} 那句失败话没走这颗键");
        Assert.True(reset >= 0 && reset < speaks, $"{rel} 的 HasItems 没在写话之前复位＝那句失败提示被可见性绑藏住了");
    }

    // ────────── ⑧ 成对判据：换实参就换读数 ──────────

    /// <summary>
    /// <b>读数那颗⛔ 抄进句子，也⛔ 抄进表</b>：递 <c>disk full</c> 就必须念出 <c>disk full</c>、屏幕上⛔ 留着上一条原因、也⛔ 露出 <c>{0}</c>。
    /// <para>这一格与②那颗计数判据<b>成对</b>（规则 #124）：颗数与形状都抓不到"表里那一行把原因写死了"，而这一条抓得到。</para>
    /// <para>三语各念一次且两两⛔ 相等＝那句真的按语言各说各的；⛔ 把某一句的精确措辞钉死在这里（表里换个措辞⛔ 该红）。</para>
    /// </summary>
    [Fact]
    public void TheLoadFailedRowChangesWhenTheReasonChanges()
    {
        var readings = new List<string>();
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            var one = LocMessage.Of("common.loadFailed", "数据库被占用").Render();
            var other = LocMessage.Of("common.loadFailed", "disk is full").Render();
            Assert.Contains("disk is full", other, StringComparison.Ordinal);   // 换实参就换读数
            Assert.DoesNotContain("disk is full", one, StringComparison.Ordinal);   // 上一条原因⛔ 留在句子里
            Assert.DoesNotContain("{0}", one, StringComparison.Ordinal);   // 填空没做＝屏幕上挂着键的形状
            // 拉丁文那一句的<b>骨架</b>⛔ 夹汉字：把实参换成 ASCII 再量，否则量到的是我自己递进去的那句中文。
            if (language == AppLanguage.English) Assert.Empty(Cjk.Matches(LocMessage.Of("common.loadFailed", "disk is full").Render()).Cast<System.Text.RegularExpressions.Match>());
            readings.Add(one);
            Assert.Equal(readings.Count, readings.Distinct(StringComparer.Ordinal).Count());   // 三语各说各的
        }
    }

    /// <summary>
    /// <b>那六颗不带读数的句子也按语言各说各的</b>（⛔ 拉丁文那句里夹一颗汉字／全角标点，⛔ 日文列照抄中文列）。
    /// <para>这一格拦的是"新行只填了中文列"那种形状：<c>Entry</c> 四参必填让编译先拦一道，这一道拦的是<b>填了同一份</b>。</para>
    /// </summary>
    [Theory]
    [InlineData("page.hidden.empty")]
    [InlineData("page.tags.empty")]
    [InlineData("page.activity.empty")]
    [InlineData("page.folder.tagEmpty")]
    [InlineData("page.folder.noRows")]
    [InlineData("widget.clock.noRows")]
    public void AnEmptyStateRowSaysItDifferentlyInEachLanguage(string key)
    {
        Assert.True(UiStrings.TryGet(key, out var entry), $"表里没有这颗键：{key}");
        Assert.Empty(Cjk.Matches(entry.En).Cast<System.Text.RegularExpressions.Match>());
        Assert.NotEqual(entry.Zh, entry.Ja);
        Assert.NotEqual(entry.Zh, entry.En);
        Locale.TrySet(AppLanguage.Chinese);
        var zh = LocMessage.Of(key).Render();
        Locale.TrySet(AppLanguage.Japanese);
        Assert.NotEqual(zh, LocMessage.Of(key).Render());
    }
}
