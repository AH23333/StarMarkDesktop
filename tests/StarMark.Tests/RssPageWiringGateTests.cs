#nullable enable
using System;
using System.IO;
using System.Linq;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// RSS 从"设置页里的一块预览"变成"主窗一栏 + 一个总开关"的接线守门（批次 RB）。
/// <para>
/// 这一批的改动大半在 UI 层（导航项、页面、开关绑定），单测引用不到；而它恰恰是<b>用户点名过没做到位</b>
/// 的那类缺陷——"导航栏没加、总控开关没加、文章信息还在设置页里"。纯函数全绿而界面没接上，
/// 就是这一族判据最典型的失效方式（与 KJ 那条"修好真因后要问：被改回去时什么会红"同一形状）。
/// </para>
/// <para>每条守门都带"锚点必须扫到"的断言：扫空不等于通过，那意味着守门本身已经失效。</para>
/// </summary>
public sealed class RssPageWiringGateTests
{
    private const string SettingsRss = "src/StarMark.UI/Views/SettingsPage.Rss.cs";
    private const string SettingsXaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string MainWindowXaml = "src/StarMark.UI/MainWindow.xaml";
    private const string MainWindowCs = "src/StarMark.UI/MainWindow.xaml.cs";
    private const string SettingsVm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string RssPageXaml = "src/StarMark.UI/Views/RssPage.xaml";
    private const string RssPageCode = "src/StarMark.UI/Views/RssPage.xaml.cs";
    private const string RssPageVm = "src/StarMark.UI/ViewModels/RssPageViewModel.cs";
    private const string ItemCardXaml = "src/StarMark.UI/Controls/ItemCard.xaml";

    /// <summary>扫 XAML 前先把注释剥掉：<b>守门要管的是控件，不是散文</b>——
    /// "这一页没有排序"这句话本身不该被"不许出现'排序'"判红（否则只能逼着注释改词，那是本末倒置）。</summary>
    private static string Markup(string xaml)
        => System.Text.RegularExpressions.Regex.Replace(xaml, "<!--.*?-->", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

    // ────────── 设置页只剩配置 ──────────

    /// <summary>设置页这一栏<b>不再抓取、不再列条目、不再收藏</b>：那些都搬到「RSS」页了。
    /// 留着就会出现"两个地方各有一份抓取轮次"，而两边的校验符与停止按钮互不知情。</summary>
    [Fact]
    public void SettingsPageNoLongerHostsTheReader()
    {
        var code = ReadRepoFile(SettingsRss);
        var xaml = Markup(Between(ReadRepoFile(SettingsXaml), "网址来源（RSS / Atom）", "</controls:ColumnFlowPanel>"));

        Assert.Contains("InitRssSection", code);            // 锚点：这一栏还在
        foreach (var gone in new[] { "RssClient", "RssAggregator", "CancellationTokenSource", "RecordItemAsync", "LogActivityAsync" })
            Assert.False(code.Contains(gone, StringComparison.Ordinal));
        foreach (var gone in new[] { "RssEntryList", "RssRefreshButton", "刷新并预览", "RssStatusText" })
            Assert.False(xaml.Contains(gone, StringComparison.Ordinal));
    }

    /// <summary>总开关必须真的绑到 VM（两态以上消费点：开关本体 + "打开 RSS 页"那颗就地按钮）。</summary>
    [Fact]
    public void RssCardHasItsOwnMasterSwitchBoundToTheViewModel()
    {
        // 终点取该页容器的收尾：RSS 卡片自批次 WC-2 起住进「拓展功能」页，不再是「AI 助手」的邻居
        var xaml = Between(ReadRepoFile(SettingsXaml), "网址来源（RSS / Atom）", "</controls:ColumnFlowPanel>");

        Assert.Equal(2, Count(xaml, "ViewModel.RssEnabled"));
        Assert.Contains("ViewModel.RssStatus", xaml);
        Assert.Contains("OpenRss_Click", xaml);
    }

    /// <summary>源列表一改动就要重算"这一栏算不算开着"并立刻反映到导航栏——
    /// 否则用户加完源还得自己去翻那个总开关，凭空多一步。</summary>
    [Fact]
    public void EverySourceListChangeReEvaluatesWhetherTheNavEntryShows()
    {
        var code = ReadRepoFile(SettingsRss);

        Assert.Equal(3, Count(code, "ViewModel.RefreshRssEnabled()"));   // 添加 / 删除 / 逐源开关
        Assert.Contains("using StarMark.UI.Helpers", code);
    }

    // ────────── 导航栏那一端 ──────────

    [Fact]
    public void NavEntryExistsAndIsWiredAtBothEnds()
    {
        var xaml = ReadRepoFile(MainWindowXaml);
        Assert.Contains("x:Name=\"NavRssItem\"", xaml);
        Assert.Contains("Tag=\"rss\"", xaml);

        var cs = ReadRepoFile(MainWindowCs);
        Assert.Contains("\"rss\" => typeof(RssPage)", cs);
        Assert.Contains("public void ApplyRssNavVisibility(bool enabled)", cs);
        Assert.Contains("ApplyRssNavVisibility(_settings.LoadRssEnabled());", MethodBody(cs, "public MainWindow"));
    }

    /// <summary>关掉开关必须<b>即时</b>收掉导航项，且正停在那一页时要退回文件夹页。</summary>
    [Fact]
    public void TogglingTheSwitchMovesTheNavEntryAndEscapesThePage()
    {
        var changed = MethodBody(ReadRepoPartials(SettingsVm), "partial void OnRssEnabledChanged(bool value)");
        Assert.Contains("_settings.SaveRssEnabled(value);", changed);
        Assert.Contains("App.MainWindow?.ApplyRssNavVisibility(value);", changed);

        var apply = MethodBody(ReadRepoFile(MainWindowCs), "public void ApplyRssNavVisibility(bool enabled)");
        Assert.Contains("NavRssItem.Visibility", apply);
        Assert.Contains("ViewModel.CurrentPageTag == \"rss\"", apply);
        Assert.Contains("NavigateTo(\"tree\")", apply);
    }

    // ────────── 卡片动作位：两处消费必须读同一个判据 ──────────

    /// <summary>「预览」与「收藏」各有菜单项与按钮<b>两处</b>，必须绑同一个判据；
    /// 收藏那一处若退回 <c>IsTrendingRepo</c>，RSS 行就没有收藏了（＝这一批的主功能消失而构建全绿）。</summary>
    [Fact]
    public void PreviewAndCollectSlotsBothReadTheSharedFlags()
    {
        var xaml = Markup(ReadRepoFile(ItemCardXaml));
        Assert.Equal(2, Count(xaml, "ViewModel.ShowsPreview"));
        Assert.Equal(2, Count(xaml, "ViewModel.ShowsCollect"));

        var strays = xaml.Split('\n')
            .Where(l => l.Contains("Collect", StringComparison.Ordinal)
                        && l.Contains("IsTrendingRepo", StringComparison.Ordinal))
            .Select(l => l.Trim())
            .ToList();
        Assert.Empty(strays);
    }

    /// <summary>收藏动作只有一个分流口。绕开它直接调热榜那份，RSS 行点收藏就会去写 GitHub 书签。</summary>
    [Fact]
    public void CollectHasExactlyOneRoutingDoor()
    {
        var root = RepoRoot();
        var hits = Directory
            .EnumerateFiles(Path.Combine(root, "src", "StarMark.UI"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Select(f => (Name: Path.GetFileName(f), Text: File.ReadAllText(f)))
            .Where(x => x.Text.Contains("TrendingItemActions.ToggleCollectAsync(", StringComparison.Ordinal))
            .Select(x => x.Name)
            .ToList();

        Assert.Equal(new[] { "RssItemActions.cs" }, hits);  // 只有 ItemCollectActions 那一个分流口能调它
        // 卡片上那颗按钮与它的菜单项共用同一个处理器 ⇒ 全仓只有这一处调用
        Assert.Equal(1, Count(ReadRepoFile("src/StarMark.UI/Controls/ItemCard.xaml.cs"), "ItemCollectActions.ToggleAsync"));
        Assert.Contains("ItemCollectActions.ToggleAsync", ReadRepoFile("src/StarMark.UI/Helpers/ItemContextMenu.cs"));
    }

    // ────────── RSS 页本身 ──────────

    /// <summary>用户裁决："该页内容不提供搜索/排序等功能"。写成守门而不是记在注释里，
    /// 因为下一次"顺手加个筛选框"看起来像增强，实际把这一页变成了另一个主窗。
    /// <para>另一半是这一批的真机反馈：<b>文件夹必须能展开/收起</b>——上一版一次性平铺所有源的全部条目，
    /// 用户的评价是"文件夹只是个摆设"。所以这里同时钉"默认收起 + 展开状态记得住 + 卡片惰性建"。</para></summary>
    [Fact]
    public void RssPageIsACollapsibleAccordionWithNoSearchNoSort()
    {
        var xaml = Markup(ReadRepoFile(RssPageXaml));
        var code = ReadRepoFile("src/StarMark.UI/Views/RssPage.xaml.cs");
        foreach (var gone in new[] { "<TextBox", "<ComboBox", "<AutoSuggestBox", "排序" })
            Assert.False(xaml.Contains(gone, StringComparison.Ordinal));

        Assert.Contains("SuppressTags = true", code);   // 候选行不给标签入口（＋标签会按需登记写库）
        Assert.Contains("OpenRequested +=", code);      // 点标题直接跳文章
        Assert.Contains("Visibility.Collapsed", code);  // 体默认收起
        Assert.Contains("_expanded", code);             // 展开状态跨刷新记住
        Assert.Contains("DispatcherQueue.TryEnqueue(BuildOnce)", code);   // 卡片惰性建，点一下不卡
        Assert.Contains("StructureChanged += RebuildSources", code);
        Assert.Contains("StructureChanged -= RebuildSources", code);      // 成对退订（死页不该继续收重建事件）
    }

    /// <summary>点条目跳文章：<b>打不开必须说出原因</b>；静默失败正是这一栏最早那条"点了没反应"的形状。</summary>
    [Fact]
    public void OpeningARowReportsWhyItFailed()
    {
        var open = MethodBody(ReadRepoFile(RssPageVm), "public async Task OpenAsync(ItemCardViewModel vm)");
        Assert.Contains("LauncherEx.TryOpenAsync(vm.Uri)", open);
        Assert.Contains("没能打开：", open);
        Assert.DoesNotContain("LauncherEx.OpenAsync", open);
    }

    /// <summary>一轮抓取的时限、停止出口都在页 VM 上（设置页那份已删净，见上面的守门）。</summary>
    [Fact]
    public void TheRoundKeepsItsBudgetAndStopDoorOnThePage()
    {
        var vm = ReadRepoFile(RssPageVm);
        Assert.Contains("RoundBudget = TimeSpan.FromSeconds(60)", vm);
        Assert.Contains("RssSourceStatus.Describe(outcome", vm);
        Assert.Contains("public void CancelLoading()", vm);
        Assert.Contains("ViewModel.CancelLoading();", ReadRepoFile(RssPageCode));
        // 状态行那句"共 N 条"必须数页面上真的摆出来的行，而不是聚合器那份带 200 条上限的摊平清单
        Assert.Contains("Sections.Sum(s => s.Rows.Count)", vm);
        Assert.DoesNotContain("共 {run.Entries.Count}", vm);
    }

    // ────────── 批次 RF-3：缓存 + 每天一次 + 增量 ──────────

    /// <summary><b>进页面不许无脑全抓</b>（用户裁决"每天仅刷新一次"）。
    /// 这条守门看着普通，失效方式却很难看：把它改回"每次进页面都跑一轮"，构建全绿、纯函数全绿，
    /// 只有用户每次都慢——所以只能钉结构。</summary>
    [Fact]
    public void EnteringThePageOnlyFetchesSourcesThatAreDue()
    {
        var vm = ReadRepoFile(RssPageVm);
        var page = ReadRepoFile(RssPageCode);

        Assert.Contains("_ = ViewModel.PrimeAsync();", MethodBody(page, "protected override void OnNavigatedTo(NavigationEventArgs e)"));
        Assert.Equal(0, Count(page, "ViewModel.ReloadSources()"));      // 只从 PrimeAsync 走一遍，别在页面里再抓一次

        var prime = MethodBody(vm, "public async Task PrimeAsync()");
        Assert.Contains("StartRoundAsync(auto: true)", prime);
        Assert.Contains("|| IsBusy) return;", prime);                  // 上一轮还在收尾时不许再开一轮

        var start = MethodBody(vm, "private async Task StartRoundAsync(bool auto)");
        Assert.Contains("IsBusy = true;", start);                       // 闸门在任何 await 之前就合上
        Assert.Contains("RunRoundAsync(auto, cts.Token)", start);

        var round = MethodBody(vm, "private async Task RunRoundAsync(bool auto, CancellationToken ct)");
        Assert.Contains("(!auto || RssFeedCache.IsDue(", round);        // 到期判断只作用在自动那一轮
        Assert.DoesNotContain("if (!auto) return;", round);             // 手动那一轮不许顺手把自动的活也推掉
    }

    /// <summary>一轮对缓存档<b>只读一次、只写一次</b>（批次 PA 的口径：逐源读写随源数线性放大，
    /// 而且中途崩溃会留下半新半旧的一档）。</summary>
    [Fact]
    public void ARoundTouchesTheCacheFileAtMostOnce()
    {
        var vm = ReadRepoFile(RssPageVm);
        var round = MethodBody(vm, "private async Task RunRoundAsync(bool auto,");

        Assert.Equal(1, Count(round, "_cache.Save(_file)"));
        Assert.Equal(0, Count(round, "_cache.Load()"));                 // 读发生在 ReloadSources，不在轮里
        Assert.Equal(1, Count(vm, "_cache.Load()"));                    // 全仓也就这一次
        Assert.Equal(2, Count(vm, "_cache.Save(_file)"));               // 一处是轮末，一处是删源之后，没有第三处
    }

    /// <summary>校验符（ETag / Last-Modified）<b>必须活过渡</b>：它原先待在 VM 的一个内存字典里，
    /// 于是每次启动的第一轮必定是全量下载——那正是"每次刷新极为缓慢"的另一半。</summary>
    [Fact]
    public void ValidatorsArePersistedInsteadOfLivingInAMemoryDictionary()
    {
        var vm = ReadRepoFile(RssPageVm);
        var round = MethodBody(vm, "private async Task RunRoundAsync(bool auto,");

        Assert.Equal(0, Count(vm, "_validators"));                      // 那份内存字典删净了，不许回来
        Assert.Contains("cached.Etag = outcome.Etag ?? cached.Etag;", round);   // 源没重发就沿用旧的
        Assert.Contains("known?.Etag, known?.LastModified", round);
        Assert.Contains("RssCacheStore", ReadRepoFile("src/StarMark.UI/App.xaml.cs"));
    }

    /// <summary>抓取那一路是并发的 ⇒ <b>缓存的写入必须留在轮末的串行段</b>，
    /// 而界面上那一列要取自合并后的缓存（取自本轮条目就是把增量刷新白做了一次）。</summary>
    [Fact]
    public void RowsComeFromTheMergedCacheNotFromThisRound()
    {
        var round = MethodBody(ReadRepoFile(RssPageVm), "private async Task RunRoundAsync(bool auto,");

        Assert.Contains("RssFeedCache.ToEntries(cached, outcome.Source)", round);
        Assert.DoesNotContain("outcome.Entries.Select(e => RowFor", round);
        Assert.DoesNotContain("_file.Sources.Add", MethodBody(ReadRepoFile(RssPageVm), "public void ReloadSources()"));
        // 进页面那一段不许凭空长出缓存项（只有轮末真的抓到东西才建）；"删源之后清档"是另一件事
    }

    /// <summary>缓存的判据不许在 UI 层另写一套（批次 NF 同一课：<c>SettingsStore</c> 在 UI，一条都断言不到）：
    /// 页 VM 只许"问"Core，不许自己算一天、自己加时间戳。</summary>
    [Fact]
    public void TheDailyGateIsAskedAboutNotReimplemented()
    {
        var vm = ReadRepoFile(RssPageVm);
        Assert.Contains("RssFeedCache.IsDue(", vm);
        Assert.Contains("RssFeedCache.Merge(", vm);
        Assert.DoesNotContain("TimeSpan.FromHours(24)", vm);
        Assert.DoesNotContain("FetchedAtUnix +", vm);
        Assert.DoesNotContain("CacheVersion", vm);                      // 格式版本只有一处在说话
    }

    /// <summary><b>缓存档必须进 DI</b>：没注册的话构造页 VM 时当场抛，表现是"点 RSS 这一页没反应"
    /// （SettingsStore 未入 DI 那次踩过同一处，批次 H1）。</summary>
    [Fact]
    public void TheCacheStoreIsRegisteredBeforeThePageViewModelNeedsIt()
    {
        var app = ReadRepoFile("src/StarMark.UI/App.xaml.cs");
        Assert.Contains("services.AddSingleton<StarMark.Core.Feed.RssCacheStore>();", app);
        Assert.True(app.IndexOf("RssCacheStore", StringComparison.Ordinal) <
                    app.IndexOf("services.AddSingleton<RssPageViewModel>", StringComparison.Ordinal));
    }

    /// <summary>页面顶部那句说明必须与"什么时候真的会联网"一致。
    /// 上一版写的是"只有按「刷新」时才发请求"——每天自动补抓落地之后那句话就成了假话，
    /// 而用户是照着它判断"我看到的是不是最新的"。</summary>
    [Fact]
    public void ThePageTellsTheTruthAboutWhenItGoesOnline()
    {
        var xaml = Markup(ReadRepoFile(RssPageXaml));
        Assert.DoesNotContain("只有按「刷新」时才发请求", xaml);       // 这句在每天一次之后就是假话
        Assert.Contains("rss-cache.json", xaml);                        // 说的是本机缓存，就得让人找得到那份文件
    }

    /// <summary>"隔多久自动补抓一次"这个节奏只许有一处在说话（<c>RssFeedCache.AutoRefreshGap</c>）。
    /// <para>原先"一天"这个词写在标记与状态行里共四处：把常数改成 6 小时，编译器不响、闸门不红，
    /// 只有用户看到的句子还在说"一天"——那是比少一个功能更难受的一种假口径。现在句子只从 GapText 取，
    /// 标记里连这个词都不许出现。</para></summary>
    [Fact]
    public void TheCadenceIsStatedInExactlyOnePlace()
    {
        var vm = ReadRepoFile(RssPageVm);
        Assert.True(Count(vm, "RssFeedCache.GapText") >= 4,
            "界面那句节奏必须四处都从 RssFeedCache.GapText 取（改了 AutoRefreshGap 才不会留下四句假话）");

        var xaml = Markup(ReadRepoFile(RssPageXaml));
        foreach (var stray in new[] { "一天", "每天", "24 小时" })
            Assert.DoesNotContain(stray, xaml);
    }

    /// <summary>「已收藏」的读法不许带行数窗口：窗口外的收藏会被显示成"没收藏"，
    /// 而那是少报——用户看不出任何异常（批次 PA 的同一教训）。</summary>
    [Fact]
    public void CollectedLookupIsAPrefixQueryWithoutARowWindow()
    {
        // 仓储按访问面拆成了多个 partial 文件（批次 WF-2），所以这条守门读"整个类的全部文件"，
        // 而不是钉某一个文件名——方法搬到哪一段都还拦得住。
        var body = MethodBody(ReadRepoPartials("src/StarMark.Data/ItemRepository.cs"),
            "public async Task<IReadOnlyList<string>> GetCollectedRssLinksAsync");
        Assert.DoesNotContain("LIMIT", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RssEntryIdentity.BookmarkSourcePrefix", body);   // 前缀只有一处定义，这里不抄字面量
        Assert.Contains("GetCollectedRssLinksAsync", ReadRepoFile(RssPageVm));
    }
}
