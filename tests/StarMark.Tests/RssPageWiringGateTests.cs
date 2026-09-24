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
        var xaml = Markup(Between(ReadRepoFile(SettingsXaml), "网址来源（RSS / Atom）", "AI 助手"));

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
        var xaml = Between(ReadRepoFile(SettingsXaml), "网址来源（RSS / Atom）", "AI 助手");

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
        var changed = MethodBody(ReadRepoFile(SettingsVm), "partial void OnRssEnabledChanged(bool value)");
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
    /// 因为下一次"顺手加个筛选框"看起来像增强，实际把这一页变成了另一个主窗。</summary>
    [Fact]
    public void RssPageOffersNoSearchNoSortAndHidesTheTagChips()
    {
        var xaml = Markup(ReadRepoFile(RssPageXaml));
        foreach (var gone in new[] { "<TextBox", "<ComboBox", "<AutoSuggestBox", "排序" })
            Assert.False(xaml.Contains(gone, StringComparison.Ordinal));

        Assert.Contains("SuppressTags=\"True\"", xaml);   // 候选行不给标签入口（＋标签会按需登记写库）
        Assert.Contains("OpenRequested=\"Card_OpenRequested\"", xaml);
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

    /// <summary>一轮抓取的时限、校验符、停止出口都在页 VM 上（设置页那份已删净，见上一条守门）。</summary>
    [Fact]
    public void TheRoundKeepsItsBudgetValidatorsAndStopDoorOnThePage()
    {
        var vm = ReadRepoFile(RssPageVm);
        Assert.Contains("RoundBudget = TimeSpan.FromSeconds(60)", vm);
        Assert.Contains("_validators", vm);
        Assert.Contains("RssSourceStatus.Describe(outcome)", vm);
        Assert.Contains("public void CancelLoading()", vm);
        Assert.Contains("ViewModel.CancelLoading();", ReadRepoFile("src/StarMark.UI/Views/RssPage.xaml.cs"));
    }

    /// <summary>「已收藏」的读法不许带行数窗口：窗口外的收藏会被显示成"没收藏"，
    /// 而那是少报——用户看不出任何异常（批次 PA 的同一教训）。</summary>
    [Fact]
    public void CollectedLookupIsAPrefixQueryWithoutARowWindow()
    {
        var body = MethodBody(ReadRepoFile("src/StarMark.Data/ItemRepository.cs"),
            "public async Task<IReadOnlyList<string>> GetCollectedRssLinksAsync");
        Assert.DoesNotContain("LIMIT", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RssEntryIdentity.BookmarkSourcePrefix", body);   // 前缀只有一处定义，这里不抄字面量
        Assert.Contains("GetCollectedRssLinksAsync", ReadRepoFile(RssPageVm));
    }
}
