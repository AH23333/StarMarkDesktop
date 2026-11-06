#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 活档里指向<b>本仓那颗会改的源档</b>的行号指针闸门（账本 P-256・Phase C 片86）。
/// <para>
/// <b>为什么单独立一把</b>：<see cref="LedgerPointerGateTests"/>（UK）故意⛔ 查行号——它的理由写得很清楚，
/// 行号随任何一次合法编辑漂移，罚它＝每批回来改文档；<see cref="FrozenTreePointerGateTests"/>（片85）只量
/// <b>随包那颗冻结树</b>那一族，因为它的字节由清单钉死、号本来⛔ 会漂。本条量的是中间那一族——
/// <b>会随任何一次合法编辑挪号、而正文却照旧写着号</b>的那 85 处：片85 结案时数出"活档之外还有 130 颗待核"，
/// 本批逐颗定性（当前锚／历史证据／⛔ 归属的裸号）并把能定性的 78 处改口成可搜的锚。
/// </para>
/// <para>
/// <b>口径（本批定死，P-256 ③）</b>：活档正文要指<b>会改的那一档</b>里的某一发，写<b>档名＋那颗逐字锚</b>，
/// 行号只留在 <c>bin/**</c> 的正本里。锚⛔ 许是转述——它必须是<b>那颗档里读得到的字面串</b>，
/// 所以本批把它钉在"两边逐字相等"上：正文念它，源档里也有它。
/// </para>
/// <para>
/// <b>⚠ 三张名单都是那颗 Python 尺现读生成的</b>（正本 <c>bin/WI99/pointer86_r1/gate86.json</c>・
/// 生成腿 <c>emit_gate_r1.py</c>）。⛔ 要手加手删：下一手改了源码那一发，<see
/// cref="EveryRewrittenAnchorIsStillVerbatimOnBothSides"/> 会红——那是<b>要的</b>（"改那一发必红一次"），
/// 修法是回来重跑那颗尺、把新锚一并落回正文，而⛔ 是把名单里那一行删掉。
/// </para>
/// </summary>
public sealed class LivingSourcePointerGateTests
{
    /// <summary>射程＝"下一次动手要照着做"的那两面活档（⛔ 报告／坑表：那是历史快照，UK 卷首同一口径）。</summary>
    /// <remarks>
    /// <b>那颗射程缺口已由片88 配上独立一维（账本 P-256 ④）</b>：<see cref="Resolve"/> 认不到「带目录前缀而那颗前缀
    /// 与仓里那颗路径⛔ 相等」的简写（例：仓里住 <c>src/StarMark.Core/…</c> 而正文写 <c>Core/…</c>），
    /// 而 <c>FallenPointers</c> 只数「认得到档名<b>且</b>端点全落空」⇒那一族<b>既不叫落空也⛔ 叫在</b>。
    /// 片88 落盘前的现读总数<b>548＝落空 35＋盲区 8＋认不到 3＋在 502</b>（正本 <c>bin/WI99/p843_r1/census843-读数.txt</c>・
    /// 与片86 那颗尺三数全等＝成对判据过了）；落盘后同尺重测<b>540＝落空 35＋盲区 0＋认不到 3＋在 502</b>
    /// （正本 <c>bin/WI99/p843_r1/census88-post.txt</c>）。今天那一族由 <see cref="UnresolvedPrefixPointers"/>、
    /// <see cref="AmbiguousPrefixPointers"/>、<see cref="PrefixedAbbreviationShapes"/> 三把各数各的钉住。
    /// ⛔ 把那颗基线 35 读成「活档行号指针的全数」——548 里另有 513 颗⛔ 在落空那一族。
    /// </remarks>
    private static readonly string[] Living =
    {
        "docs/待决策事项.md",
        "docs/excalidraw整合可行性深析.md",
    };

    /// <summary>本批点名的那颗会改的源档那一族：只认 .cs／.xaml（随包冻结树归片85 那把，⛔ 归本条）。</summary>
    private static readonly string[] SourceRoots = { "src", "tests", "tools", "scripts", "build" };

    private static readonly HashSet<string> SkipDirs =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "TestResults", ".vs", ".git", "node_modules" };

    /// <summary><c>档名＋冒号＋行号[＋行号]</c>——只认 .cs/.xaml，且保留目录前缀那层约束。</summary>
    private static readonly Regex Pointer = new(
        @"([\w./-]*\.(?:cs|xaml))\s*[:：]\s*([0-9]+)(?:\s*[-–—~～]\s*([0-9]+))?",
        RegexOptions.Compiled);

    /// <summary>本批改口掉的「档名＋号」：那颗号⛔ 许再被写回活档（写回＝有人把已定的引用退回成会漂的号）。</summary>
    private static readonly (string Name, int First, int Last)[] BannedPointers =
    {
            (@"AiClassifyService.cs", 186, 186),
            (@"BoardWindow.cs", 935, 935),
            (@"CanvasService.cs", 194, 194),
            (@"DiagnosticsService.cs", 131, 131),
            (@"FolderTreePageViewModel.cs", 204, 204),
            (@"GitHubClient.cs", 56, 56),
            (@"GitHubClient.cs", 113, 127),
            (@"GitHubSource.cs", 113, 113),
            (@"GlanceWidget.xaml.cs", 18, 18),
            (@"IItemSource.cs", 35, 35),
            (@"InsightsService.cs", 81, 81),
            (@"MainWindow.Search.cs", 151, 159),
            (@"MainWindow.Search.cs", 212, 212),
            (@"MainWindow.xaml.cs", 500, 503),
            (@"NoticeCardWindow.cs", 90, 90),
            (@"SearchService.cs", 68, 79),
            (@"SearchService.cs", 86, 129),
            (@"SettingsPage.Ai.cs", 54, 54),
            (@"SettingsPageViewModel.cs", 271, 279),
            (@"SyncCoordinator.cs", 36, 50),
            (@"ToolBar.cs", 136, 136),
            (@"WidgetContentFactory.cs", 38, 38),
            (@"src/StarMark.Core/Sync/SyncCoordinator.cs", 36, 124),
            (@"src/StarMark.Core/Widgets/WeatherCode.cs", 31, 31),
            (@"src/StarMark.Core/Widgets/WidgetStorage.cs", 481, 496),
            (@"src/StarMark.Integrations/Feed/RssClient.cs", 152, 152),
            (@"src/StarMark.UI/Boards/BoardWindow.cs", 471, 471),
            (@"src/StarMark.UI/ViewModels/FolderTreePageViewModel.cs", 204, 204),
            (@"src/StarMark.UI/Views/GlanceWidget.xaml.cs", 123, 123),
            (@"src/StarMark.UI/Views/ItemGridWidget.xaml", 83, 83),
            (@"tests/StarMark.Tests/CultureInvariantNamingTests.cs", 11, 15),
            // ↓ 片88 那颗 P-256 ④：八颗「带目录前缀的简写」换锚之后，那颗号⛔ 许再被写回活档。
            // ⚠ 这里只登记七颗、⛔ 八颗：WidgetStorage.cs 那三处号还活在账本 ✅P-10 那条盘点记下的<b>历史证据</b>里，
            //   按「⛔ 用语法形状代替谁有资格说」（名册 347／426）的口径那颗号⛔ 禁；
            //   它的位置交给 PrefixedAbbreviationShapes 按<b>形状</b>管＝全路径照旧合法，只有「带⛔ 等前缀」那一写法会红。
            (@"AiClassify.cs", 104, 107),
            (@"WorldClockViewModel.cs", 122, 122),
            (@"CountdownViewModel.cs", 70, 70),
            (@"SnapshotPage.xaml.cs", 86, 86),
            (@"BuildInfo.cs", 22, 22),
            (@"PerformanceSettingsPolicy.cs", 18, 24),
            (@"ItemGridWidget.xaml", 66, 66),
    };

    /// <summary>改口写进正文的那 65 颗锚：每颗必须<b>正文念得到、它指向的那颗档也读得到</b>。</summary>
    private static readonly (string Path, string Anchor)[] RewrittenAnchors =
    {
            (@"src/StarMark.Abstractions/IItemSource.cs", @"public string? ContinuationToken { get; init; }"),
            (@"src/StarMark.Core/Diagnostics/DiagnosticsService.cs", @"private static string ShortenEtag(string value)"),
            (@"src/StarMark.Core/Insights/InsightsService.cs", @"var stale = items.Count(i => nowSec - i.UpdatedAt > StaleSeconds);"),
            (@"src/StarMark.Core/Search/SearchService.cs", @"public async Task<SearchResult> SearchAsync(string keyword, SearchFilter filter, CancellationToken ct)"),
            (@"src/StarMark.Core/Search/SearchService.cs", @"var ftsResult = await SafeAwait(ftsTask, ct);"),
            (@"src/StarMark.Core/Search/SearchService.cs", @"var realTimeTasks = usable"),
            (@"src/StarMark.Core/Sync/SyncCoordinator.cs", @"PulledCount = items.Count,"),
            (@"src/StarMark.Core/Sync/SyncCoordinator.cs", @"catch (Exception ex)"),
            (@"src/StarMark.Core/Sync/SyncCoordinator.cs", @"if (!source.IsAvailable)"),
            (@"src/StarMark.Core/Sync/SyncCoordinator.cs", @"public async Task<SyncSummary> SyncAllAsync(CancellationToken ct)"),
            (@"src/StarMark.Core/Widgets/WeatherCode.cs", @"public static LocMessage DescribeMessage(int code)"),
            (@"src/StarMark.Core/Widgets/WidgetStorage.cs", @"public WidgetStoreData Load()"),
            (@"src/StarMark.Core/Widgets/WidgetStorage.cs", @"public bool Save(WidgetStoreData data)"),
            (@"src/StarMark.Core/Widgets/WidgetStorage.cs", @"public static WidgetStoreData Normalize(WidgetStoreData? data, bool firstRun = false)"),
            (@"src/StarMark.Data/ItemRepository.Activity.cs", @"public async Task LogActivityAsync(ActivityKind kind, string? itemKey, string title, string? uri, CancellationToken ct)"),
            (@"src/StarMark.Data/ItemRepository.Items.cs", @"public async Task<IReadOnlyList<Item>> GetAllAsync(BrowseFilter filter, CancellationToken ct)"),
            (@"src/StarMark.Data/ItemRepository.Local.cs", @"public async Task UpsertLocalItemAsync(Item item, CancellationToken ct = default, ActivityKind? activity = null)"),
            (@"src/StarMark.Data/ItemRepository.Local.cs", @"public async Task<IReadOnlyList<Item>> GetBySourceAsync(string source, ItemType? type = null, int limit = 1000, CancellationToken ct = default)"),
            (@"src/StarMark.Data/ItemRepository.Search.cs", @"public async Task<SearchResult> SearchAsync(string keyword, SearchFilter filter, CancellationToken ct)"),
            (@"src/StarMark.Data/ItemRepository.Tags.cs", @"public async Task<bool> SetPinnedAsync(long itemId, bool pinned, CancellationToken ct)"),
            (@"src/StarMark.Data/ItemRepository.cs", @"private static async Task UpsertOne(SqliteConnection conn, Item item, CancellationToken ct)"),
            (@"src/StarMark.Integrations/Feed/RssClient.cs", @"internal static string Decode(byte[] bytes, string? httpCharset)"),
            (@"src/StarMark.Integrations/GitHub/GitHubClient.cs", @"application/vnd.github+json"),
            (@"src/StarMark.Integrations/GitHub/GitHubClient.cs", @"public async Task<IReadOnlyList<GitHubStarApiModel>> GetAllStarredAsync(CancellationToken ct)"),
            (@"src/StarMark.Integrations/GitHub/GitHubClient.cs", @"starred_at 真值来自"),
            (@"src/StarMark.Integrations/GitHub/GitHubClient.cs", @"application/vnd.github.star+json"),
            (@"src/StarMark.Integrations/GitHub/GitHubSource.cs", @"ExtraJson = JsonSerializer.Serialize(meta),"),
            (@"src/StarMark.UI/Boards/BoardWindow.cs", @"private void RequestSceneSnapshot(BoardPolicy.SaveBeat beat)"),
            (@"src/StarMark.UI/Helpers/RelativeTimeHelper.cs", @"public static string Format(long unixSeconds)"),
            (@"src/StarMark.UI/MainWindow.Search.cs", @"private const string LanguageAllValue = """";"),
            (@"src/StarMark.UI/MainWindow.Search.cs", @"private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)"),
            (@"src/StarMark.UI/MainWindow.Search.cs", @"var items = new List<LocOption> { LocOption.T(""searchLang.placeholder"", LanguageAllValue) };"),
            (@"src/StarMark.UI/MainWindow.Search.cs", @"var timer = _debounceTimer ??= BuildDebounceTimer();"),
            (@"src/StarMark.UI/MainWindow.Status.cs", @"SyncButton.IsEnabled = false;"),
            (@"src/StarMark.UI/MainWindow.Status.cs", @"private void SetSourceButtonsHighlight(string source)"),
            (@"src/StarMark.UI/MainWindow.Status.cs", @"private void SyncButton_Click(object sender, RoutedEventArgs e) => _ = DoSyncAsync();"),
            (@"src/StarMark.UI/MainWindow.Status.cs", @"var summary = await syncCoordinator.SyncAllAsync(cts.Token);"),
            (@"src/StarMark.UI/Services/CanvasService.cs", @"private static void Report(LocMessage title, LocMessage message)"),
            (@"src/StarMark.UI/Services/WidgetContentFactory.cs", @"string title = WidgetStorage.KindTitle(kind);"),
            (@"src/StarMark.UI/Services/WidgetManager.QuickLaunch.cs", @"public async Task<bool> AddLinkAsync(string instanceId, string title, string uri)"),
            (@"src/StarMark.UI/ViewModels/ActivityItemViewModel.cs", @"public string AtText => RelativeTimeHelper.Format(At);"),
            (@"src/StarMark.UI/ViewModels/ClockWidgetViewModel.cs", @"private string _dateText = DateTimeText.DayWithWeekday(DateTime.Now);"),
            (@"src/StarMark.UI/ViewModels/ClockWidgetViewModel.cs", @"private void RefreshRest(DateTime now)"),
            (@"src/StarMark.UI/ViewModels/ClockWidgetViewModel.cs", @"public int Update()"),
            (@"src/StarMark.UI/ViewModels/FolderTreePageViewModel.cs", @"? (HasTagFilter ? LocMessage.Of(""page.folder.tagEmpty"") : LocMessage.Of(""page.folder.noRows""))"),
            (@"src/StarMark.UI/ViewModels/FolderTreePageViewModel.cs", @"LocMessage.Of(""page.folder.noRows"")"),
            (@"src/StarMark.UI/ViewModels/SettingsPageViewModel.cs", @"private static T Safe<T>(Func<T> read, T fallback, string what)"),
            (@"src/StarMark.UI/ViewModels/SettingsPageViewModel.cs", @"public string WhenText => DateTimeText.Minute(File.ModifiedUtc.ToLocalTime());"),
            (@"src/StarMark.UI/Views/CaptureOverlayWindow.ToolBar.cs", @"capture.bar.through"),
            (@"src/StarMark.UI/Views/GlanceWidget.xaml.cs", @"widget.glance.nextFestival"),
            (@"src/StarMark.UI/Views/GlanceWidget.xaml.cs", @"日期语义 + 推荐入口"),
            (@"src/StarMark.UI/Views/ItemGridWidget.xaml", @"x:Name=""EmptyHint"""),
            (@"src/StarMark.UI/Views/ItemGridWidget.xaml.cs", @"if (empty) LocText.SetMessage(EmptyHint, ViewModel.EmptyState);"),
            (@"src/StarMark.UI/Views/NoticeCardWindow.cs", @"LocText.SetWindowTitle(this, ""card.windowTitle"");"),
            (@"src/StarMark.UI/Views/SettingsPage.Ai.cs", @"AiKeyBox.Password = stored.ApiKey ?? string.Empty;"),
            (@"src/StarMark.UI/Views/SettingsPage.Ai.cs", @"ApiKey: AiKeyBox.Password,"),
            (@"tests/StarMark.Tests/CultureInvariantNamingTests.cs", @"文件名里的日期段必须锁定公历（InvariantCulture）"),
            // ↓ 片88 那颗 P-256 ④：八颗「带目录前缀的简写」换的那八颗锚（锚⛔ 许住注释＝优先挑声明那一行）。
            (@"src/StarMark.Abstractions/Ai/AiClassify.cs", @"public const int MaxItemsPerBatch = 80;"),
            (@"src/StarMark.UI/ViewModels/WorldClockViewModel.cs", @"public void ReresolveNames()"),
            (@"src/StarMark.UI/ViewModels/CountdownViewModel.cs", @"public void Load(IEnumerable<CountdownItem>? items)"),
            (@"src/StarMark.UI/Views/SnapshotPage.xaml.cs", @"private void BuildRows()"),
            (@"src/StarMark.Abstractions/BuildInfo.cs", @"public static string Display => LocalTime is { } time"),
            (@"src/StarMark.Core/Performance/PerformanceSettingsPolicy.cs", @"public const int ResourceSaverMaxCacheCount = 128;"),
            (@"src/StarMark.UI/Views/ItemGridWidget.xaml", @"<DataTemplate x:DataType=""vm:ActivityItemViewModel"">"),
            (@"src/StarMark.Core/Widgets/WidgetStorage.cs", @"public static WidgetStoreData Normalize(WidgetStoreData? data, bool firstRun = false)"),
    };

    /// <summary>片86 收口时现读的落空残留基线：改前 75 颗 → 改后这颗数。⛔ 是"归零"，是"还剩这一族"的<b>现读证</b>。
    /// 片91 又修掉一颗（那颗「剪贴板文案写死三颗数」的登记行把落点写成 <c>…Clipboard.cs:90-91</c>，
    /// 而批 SE 之后那两行已⛔ 是那句话＝那颗号既漂了又指错了地方，换成正文锚之后这颗数从 35 落到 34）。
    /// <para><b>片115 再落一颗（34 → 33）・而那一颗⛔ 是本批"修好"的</b>：账本 ✅P-14 那行原本在档名后面带了一对行号・
    /// 那颗号<b>从来没对着</b>句里点名的 <c>GetQingmingDay</c>（那颗方法住在文件后段）・只是过去区间撞上一颗空行・
    /// 被 <see cref="Falls"/> 数成落空。本批在那颗档头部加了几行 ⇒ 同一对号恰好压在非空正文上＝从「落空」<b>撞成</b>「非空」。
    /// 所以这颗基线是<b>先把那颗号删掉留符号锚</b>（就地补〔片115 更正〕）之后才改小的・⛔ 让一次巧合冒充一次修复（坑表 <b>#1020</b>）。</para>
    /// <para><b>片131 再落一颗（33 → 32）・而这一发的两颗各有来历</b>：本批把日志目录的取值改道（并让启动错误框不再自己重算那条路径）
    /// ⇒ 那两档的行号整体平移：账本里三串指向那颗启动装配档的号<b>当场落空</b>（闸门自己报现读 36↔33）・
    /// 另一串指向那颗日志门面的旧区间<b>反过来</b>压在非空正文上＝上一段 #1020 那句「巧合」的另一副脸（⛔ 算成一次修复）。
    /// 处置按 P-256 ③ 口径＝<b>删号留锚</b>：那四处换成正文里读得到的那句注册逐字句・那颗反过来撞对的旧号⛔ 动（它现在量得到＝照旧留在这一族里）。
    /// 两颗数各由一把只读复称尺对 HEAD 的 blob 与盘上各跑一遍现读（33↔32・差集恰那颗反过来撞对的），判据仍是这颗尺本身・⛔ 拿颗数当判据。</para>
    /// <para><b>片145 再落一颗（32 → 31）・来历写清楚，⛔ 让一次巧合冒充一次修复（#1020 第三次）</b>：
    /// 本批把设置页那颗隐式 <c>TabViewItem</c> 样式搬到应用级字典（<c>Themes/SettingsTabStyle.xaml</c>）
    /// ⇒ <c>SettingsPage.xaml</c> 29 行以下整体平移一行，账本里那串 <c>SettingsPage.xaml:458/459</c>
    /// 恰好从"压在空行＝落空"变成"压在注释行＝非空"。它<b>本来就没对着正文</b>（那两颗绑定今天住在 492/493），
    /// 所以这一颗不是被修好的——处置按 P-256 ③＝<b>删号留锚</b>（账本里换成正文读得到的那两句逐字），
    /// 那颗数随之从 32 变 31。同批另有一串"挂键行 154 → 196"是<b>历史计数</b>（WI-2h 当时数的），
    /// 那把尺的形状认不到它（号与档名隔着一串字），照旧留在正文里。</para>
    /// <para><b>片162 再落一颗（31 → 30）・来历＝Ditto 修复腿平移行号（#1020 第四次・⛔ 读成"这一族又修好了"）</b>：
    /// 本批把 Ditto 库候选表领头改装机现形（P-192 结案入口①）⇒ DittoSource 与 DittoDatabaseReader
    /// 两档行号整体平移：账本里三串指向那两档的号当场落空（闸门报「现读 33↔基线 31」），
    /// 处置按 P-256 ③＝<b>删号留锚</b>（<c>case "CF_TEXT"</c> 与 <c>ClipboardPayload.DecodeText</c> 两锚照旧在档）；
    /// 另一串 CS 复验里的 FillData 旧区间反撞成非空（现读整颗含着 FillData）＝#1020 那句「巧合」的
    /// 另一副脸（⛔ 算成修复・⛔ 动它）。基线按闸门交回的那颗读数改小・⛔ 手打（正本 D:\StarMarkTmp\p162\diffcensus162.txt）。</para>
    /// <para><b>片187 再落两颗（30 → 28）・来历＝语言比较改道平移行号（#1020 第五次・⛔ 读成"这一族又修好了"）</b>：
    /// 本批把搜索/浏览两腿的语言比较改 trim+NOCASE（P-8 (A) 结案）⇒ 那颗 partial 档行号整体平移：
    /// 账本里两串 P-33 时代指向它的旧号（66-67）恰好从「压在空行＝落空」变成「压在谓词行＝非空」，
    /// 而它们<b>本来就没对着</b>正文所句的那处截断点（那处今天住在 innerTail 那一行）。
    /// 处置按 P-256 ③＝<b>删号留锚</b>（两处旧号删掉・锚改念那一行读得到的 <c>var innerTail = relevanceFirst</c>）。
    /// 差集由一把只读复称尺对 HEAD 的 blob 与盘上各跑一遍点到名（正本 D:\StarMarkTmp\p164\diffcensus187.txt）。
    /// 基线按闸门交回的那颗读数改小・⛔ 手打。</para>
    /// <para><b>片191 再落一颗（28 → 27）・来历＝同步重入闸门平移行号（#1020 第六次・⛔ 读成"这一族又修好了"）</b>：
    /// 本批给那颗同步协调器装排队闸门并抽出体（P-31 (C)）⇒ 那颗协调器档行号整体平移。
    /// 差集由一把只读复称尺对 HEAD 的 blob 与盘上各跑一遍点到名（正本 D:\StarMarkTmp\p164\diffcensus191.txt）：
    /// HEAD 态 28 颗・盘上态 28 颗＝一落一愈恰好相抵・别无增减。
    /// 三族处置：正文被本批挤歪的号按 +29 就地改对（不进净账）；对账表快照整片冻结 ⛔ 动；
    /// 那颗旧区间本来压着落空・被本批新加的门口检查顶上后反撞非空＝#1020 那句「巧合」的第六副脸
    /// （⛔ 算成修复・⛔ 动它）⇒ 净颗数 27。基线按闸门交回的那颗读数改小・⛔ 手打。</para>
    /// <para><b>片192 再落一颗（27 → 26）・来历＝对账刷新轮真修（⛔ 归进 #1020 那族「巧合」）</b>：
    /// 六刀真码修批收口后按真身刷新对账表那片冻结快照：33 行同轮逐行重定位（只读探针先对拍旧文正本
    /// 并逐行读码核对 33 行新引用全落所句处才落笔）。差集由一把只读复称尺对改前备份与改后盘上各跑一遍
    /// 点到名（正本 D:\StarMarkTmp\p164\diffcensus192.txt）：改前 27 颗・改后 26 颗・唯一差额＝对账表
    /// P-57 行那颗旧引指向空白段、整行改写后落进该落的段落＝本刀真修（本轮码零改动，两读数间唯一变量＝
    /// 对账表本身）。基线按那把尺交回的读数改小・⛔ 手打。</para>
    /// <para><b>片194 再落两颗（26 → 24）・来历＝Ditto 取消守卫平移行号（#1020 第七次・⛔ 读成"这一族又修好了"）</b>：
    /// 本批给 <c>DittoDatabaseReader.ReadRecent</c>／<c>Search</c> 各加两道循环守卫与一道"掐中上抛"、
    /// 并给 <c>DittoSource</c> 两颗公共出口统一成 <c>Task.Run(…, ct)</c>（P-32 站点②）⇒ 那两档行号整体平移。
    /// 差集由那把只读复称尺分两步点到名（正本 <c>D:\StarMarkTmp\p194\diffcensus194.txt</c>）：
    /// 码两档落位（账本未动）现读 25 颗：① P-26 那串指向 <c>ReadRecent</c> 尾部兜错的旧号（<c>:91-95</c>）
    /// 被平移<b>压进空行＝真落空</b>，本批按口径<b>删号留锚</b>（锚念"两颗查询各自尾部那道 <c>catch { result.Clear(); }</c>"）
    /// ＝自造自收（净±0）；② 同一族里 <c>ParseDropFiles</c> 时代那两串旧区间（P-20 注记与对账表那格・<c>211-219</c>）
    /// 被加行<b>反撞非空</b>＝第六副脸的同一形状，本批没把它算成修复，而是照 ① 同一口径把号删掉
    /// （⛔ 让一颗压在无关正文上的号冒充活着）⇒ 净账少两颗。基线按闸门交回的那颗读数改小・⛔ 手打。</para>
    /// <para><b>片200 再落三颗（24 → 21）・来历＝信封上浮平移行号（#1020 第八次・⛔ 读成"这一族又修好了"）</b>：
    /// 本批把 <c>FetchAsync</c> 的返回改成 <c>Truncated</c> 信封（P-58 落码）⇒ <c>GitHubSource</c>／<c>GitHubClient</c>／
    /// <c>UiStrings</c> 三档行号整体平移。差集由那把只读复称尺对拍 199 快照与改后各跑一遍点到名
    /// （正本 <c>D:\StarMarkTmp\p164\diffcensus200.txt</c>・改前 25 颗・改后 21 颗）：① 四串旧号被平移压进空行＝真落空
    /// （304 空页臂那一串・canvas 热键表那一串・P-6／P-34 两行念 <c>PushedAt</c> 代理的那两串），本批按口径换号重指真身；
    /// ② 另三串旧号恰随平移落回正文＝自然愈合而非修，本批没把它算成修复，只在 P-256 报一句 ⇒ 净账少三颗。
    /// 基线按闸门交回的那颗读数改小・⛔ 手打。</para>
    /// <para><b>片212 再落一颗（21 → 20）・来历＝C 组性能批平移行号（#1020 第九次・⛔ 读成"这一族又修好了"）</b>：
    /// 本批 P-47 把「可用」一句改三态（<c>SourceAvailabilityText</c> 重写・四源 <c>AvailabilityKind</c> 覆写）＋
    /// P-23 重写 <c>GitHubClient</c> 构造区 ⇒ 相关行号平移。差集由那把只读复称尺对拍 HEAD 快照与改后点到名：
    /// ① 切片普查那一行念 <c>SourceAvailabilityText.cs</c> 的旧号被重写压进空行＝真落空（旧「可用」单键臂随键退役），
    /// 本批按口径<b>删号留锚</b>；② Ditto 那行念 <c>DittoSource.cs</c> 的旧区间被覆写压进空行＝真落空，
    /// 本批按口径<b>换号重指</b>（<c>CandidateDbPaths</c> 声明现读 89 起・同行那颗裸号一并重指 127）；
    /// ③ P-23 条目自己那串念 <c>GitHubClient.cs</c> 的旧号恰随重写落回正文＝自然愈合而非修，
    /// 本批没把它算成修复，只在 P-256 报一句 ⇒ 净账少一颗（22 现读 − 修复 2 ＝ 20）。
    /// 基线按闸门交回的那颗读数改小・⛔ 手打。</para>
    /// <para><b>片213 再落一颗（20 → 19）・来历＝Ditto 吞空改抛与通知带 kind 平移行号（#1020 第十次・⛔ 读成“这一族又修好了”）</b>：
    /// 本批把 <c>DittoDatabaseReader</c> 两颗查询的吞空 catch 改抛 <c>SourceReadException</c>（P-28B′ 落地）＋
    /// <c>DataChangeHub.Notify</c> 带 kind 重写与 <c>GlanceWidget</c> 订阅行加 interests（P-44 落地）⇒ 三档行号平移。
    /// 差集由那把只读复称尺对拍 HEAD 快照与改后各跑一遍点到名（正本 <c>D:\StarMarkTmp\p213\diffcensus213.txt</c>・
    /// HEAD 态 20 颗・改后现读 21 颗＝一愈两落）：① P-44 定位行念 <c>DataChangeHub.cs</c> 旧 Notify 区的号与
    /// SX 嫌疑清单行念 <c>GlanceWidget.xaml.cs</c> 农历段的号双双被平移压进空行＝真落空，本批按口径<b>换号重指</b>
    /// （各指改后新区间）；② 另一串念 <c>DittoDatabaseReader.cs</c> 旧区间的号恰随改码落回正文＝自然愈合而非修，
    /// 本批没把它算成修复，只在 P-256 报一句 ⇒ 净账少一颗（21 现读 − 换号 2 ＝ 19）。
    /// 基线按闸门交回的那颗读数改小・⛔ 手打。</para>
    private const int FallenPointerBaseline = 19;

    /// <summary>片88 第二颗棘轮（P-256 ④）：那颗 <see cref="Resolve"/> 对它交回空的「带目录前缀的简写」那一族。
    /// 换锚前现读 8 颗（正本 <c>bin/WI99/p843_r1/census843-读数.txt</c>）→ 本批逐颗换成正文锚之后这颗数。
    /// ⚠ 这颗数与那颗 35 <b>各数各的</b>：35 只含「认得到档名而端点落空」，这一族既⛔ 落空也⛔ 在场＝两份判据⛔ 能合并成一个数。
    /// <b>片92 把那颗零基线的射程说准了（P-256 ⑤ 第一笔结案）</b>：另造的那把 Python 尺<b>逐字搬进本类的四步 <see cref="Resolve"/>
    /// 与 <see cref="Falls"/></b> 之后现读「后缀接得上 23・带前缀而全接不上 0・全接不上 3・号落空 34」——后三颗与本档三颗基线<b>一颗不差</b>
    /// （那颗「号落空 34」是<b>片92 那一跑的读数</b>・片115 删掉一颗撞对的号之后本档基线已是 33・另两颗照旧不差
    /// （正本 <c>bin/WI99/p992_r1/census92-读数.txt</c>；对齐之前那把尺自造判据，数出 21↔旧正本 24 的<b>假差</b>＝坑表 #972）。
    /// ⇒ 那 23 颗<b>当场复称得到</b>（号落空 0 颗）＝判据下合法、⛔ 一律换全路径；而"那颗档被改名或挪走"那一退化方向会把它们推进
    /// 本基线这一堆（0）⇒ <b>这一维早有尺守着＝本档那颗零基线的射程兑现</b>，⛔ 读成"闸门漏了"。</summary>
    private const int PrefixBlindPointerBaseline = 0;

    /// <summary>同一族的<b>歧义</b>那一维：那颗末段在仓里认得到两颗以上＝⛔ 许替正文挑一颗（名册 716），要单列一维钉住。</summary>
    private const int AmbiguousPrefixPointerBaseline = 0;

    /// <summary>散文那一族「第 N 行」（账本 P-256 ⑤・片90）：上面那颗 <see cref="Pointer"/> 要「档名＋冒号＋号」，
    /// 所以 <c>账本第 2533 行</c>／<c>那颗声明从第 25 行起</c> 这种<b>散文写法</b>整族⛔ 进得了射程——
    /// 而它指的那一档照样随任何一次合法编辑挪号。本批现读活档 18 颗，逐颗定性成 改口 9・历史 1・结构序号 8。</summary>
    private static readonly Regex Prose = new(
        @"第\s*([0-9]+)(?:\s*[-–—~～]\s*[0-9]+)?\s*行", RegexOptions.Compiled);

    /// <summary>那一族里<b>留下来</b>的那三副样子，各由一颗同行念得到的指称解释：那颗小节号（结构）、
    /// 那句「那一版把出处写成」（历史证据・⛔ 删）、那张表的名字（正文自己说出它指哪张表）。
    /// ⚠ 判据⛔ 是"认出形状就放过"，而是「那颗号所在那一行必须念得到这里其中一颗」——要新写一颗序号，先把它的指称写出来。</summary>
    private static readonly string[] ProseMarkers =
    {
        "§21.0",
        "那一版把出处写成",
        "由这颗顺序得出的判断",
    };

    /// <summary>本批改口掉的八串旧写法（其中一颗一次抹掉两颗号＝九颗）：⛔ 许再被写回活档。</summary>
    private static readonly string[] BannedProsePointers =
    {
        "批次 WI-2e 的覆盘照片那一节（本文件第 840 行附近）",
        "⛔ 用那把尺自带的 --expect：它第 68 行还钉着 ✅65a 那年的 168／18",
        "那颗声明从第 25 行起、第 30 行正是 <c>}) =&gt; Promise&lt;SVGSVGElement&gt;;</c>",
        "① 账本第 2533 行原写着",
        "而<b>同一档第 393 行</b>与那颗常数自己的注释",
        "② 第 2709 行整行住在删除线里（已作废的 384/512/160/128 对比）",
        "那颗在 <c>fonts/ExcalidrawFontFace.ts</c> 的<b>第 11 行</b>（<c>ASSETS_FALLBACK_URL</c>）",
        "第 2 行的<b>归因</b>（⛔ 停在「compile 了两次」）",
    };

    /// <summary>片90 那颗棘轮（P-256 ⑤）：改口前现读 18 颗 → 落盘后这颗数。⚠ 与 <see cref="FallenPointerBaseline"/>
    /// 那颗 35 <b>各数各的</b>：那一族要「档名＋冒号」，这一族压根⛔ 带档名，两份判据⛔ 能合并成一个数。</summary>
    private const int ProsePointerBaseline = 10;

    /// <summary>片99 那份第三档名单（P-256 ⑪①・账本 ⑰）：报告／坑表（活档・每批追加 §／坑）＋双库一致性审计（纯 LF・冻结）。
    /// ⚠ 与 <see cref="Living"/> 那面 <b>各数各的</b>：这面只数第三档散文「第 N 行」那一族的<b>总颗</b>（颗口径・LF 口径），
    /// ⛔ 问指称——41 颗全是历史叙述（日志／仪器产物的行引句）且全部未带指称，镜像活档那颗「未带指称 0」＝清零要改写历史证据（⛔）。
    /// ⛔ 与登记读数 35（probe93b 行颗口径・含散文的行数・一行两颗算一）混账——闸门是颗口径（一行两颗算二），两口径⛔ 许互相拿去对账（账本 ⑪③・⑰）。</summary>
    private static readonly string[] ThirdTier =
    {
        "docs/开发进度报告.md",
        "docs/踩坑记录.md",
        "docs/双库一致性审计.md",
    };

    /// <summary>片99 收口基线（用户裁决＝只钉总颗）：报告 17・坑表 24・审计 0（颗口径・LF 口径〔名册 407〕）。
    /// 正本 <c>bin/WI99/p999_r1/probe99-读数.txt</c>（五段：正本判据现读・daaec63 历史 blob 移植自证对不上 0 格・今日现读・真行切分十格全零差・41 颗逐颗现场）。</summary>
    private static readonly int[] ThirdTierProseBaseline = { 17, 24, 0 };

    /// <summary>片94 那把尺（P-256 ⑪③）：正文里那一族 <c>&lt;c&gt;:N［～M］&lt;/c&gt; 裸简写——
    /// 形状逐字搬 <c>bin/WI99/p994_r1/census94_r2.py</c>（那颗 Python 普查尺，又逐字沿片85 的
    /// <c>bin/WI99/bare85.py</c>）：A＝带冒号那副；B＝区间那副；裸一颗 <c>&lt;c&gt;4194304&lt;/c&gt;
    /// 体积读数⛔ 收（⛔ 是行号）。片94 把 82 颗全数定性（继承复称 34・桥侧考古 6・片51 时点现读与深析 :82 各 1・
    /// §28 过期 5・上游族 27・元语言 6・端点空行 2・账本 :514＝82）之后全数合法留下，只把 §28 那 5 颗过期
    /// 「现在时」凭据删号留锚（<c>bin/WI99/p994_r1/apply94.py</c>・二进制读写・行尾三族改前后逐项相等）。</summary>
    private static readonly Regex BareShorthand = new(
        @"<c>\s*[:：]\s*[0-9]+(?:～[0-9]+)?\s*</c>|<c>\s*[0-9]+(?:～[0-9]+)+\s*</c>",
        RegexOptions.Compiled);

    /// <summary>片94 收口基线（账本那面）：35 颗全数＝历史登记（片56/57 时点现读，账本 21110 那句
    /// 「行号只在那个读数时刻有效」罩着）⇒ ⛔ 强改，只钉颗数。正本 <c>bin/WI99/p994_r1/census94-读数-r2.txt</c>。</summary>
    private const int LedgerBareShorthandBaseline = 35;

    /// <summary>片94 收口基线（深析那面）：改前 21・§28 五颗删号留锚后 16。正本同上。</summary>
    private const int DeepDiveBareShorthandBaseline = 16;

    /// <summary>逐行数那族裸简写（与那颗 Python 尺同一副真行基：孤立 CR 也是一行）。
    /// ⛔ 在整段正文上数——正则里 <c>\s</c> 认得换行，整段数会把跨行的形状也咬进来，
    /// 两把尺就各说各话（名册 461 那一族）。</summary>
    private static int BareShorthandCount(string text)
    {
        var count = 0;
        foreach (var line in text.Replace("\r", "\n").Split('\n'))
            count += BareShorthand.Matches(line).Count;
        return count;
    }

    private static string RepoRoot()
    {
        var dir = new FileInfo(typeof(LivingSourcePointerGateTests).Assembly.Location).Directory!;
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarMark.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    private static string Read(string rel)
    {
        var path = Path.Combine(RepoRoot(), rel.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), "闸门射程里那颗档读不到＝先修这颗尺，⛔ 让缺席变成恒绿：" + path);
        return File.ReadAllText(path).Replace("\r\n", "\n");   // ⛔ 并孤立 CR：行号口径按 LF（名册 407）
    }

    private static List<string> SourceFiles()
    {
        var list = new List<string>();
        void Walk(string dir, string rel)
        {
            foreach (var f in Directory.EnumerateFiles(dir))
                if (f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                    list.Add(rel + "/" + Path.GetFileName(f));
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (!SkipDirs.Contains(Path.GetFileName(sub)))
                    Walk(sub, rel + "/" + Path.GetFileName(sub));
        }
        foreach (var r in SourceRoots)
        {
            var full = Path.Combine(RepoRoot(), r);
            if (Directory.Exists(full)) Walk(full, r);
        }
        return list;
    }

    /// <summary>尾段解析只给<b>裸档名</b>；写了目录前缀就认前缀（partial 简写 <c>Search.cs</c> 那族要认）。</summary>
    private static List<string> Resolve(string pointer, List<string> files)
    {
        pointer = pointer.Replace('\\', '/');
        var low = pointer.ToLowerInvariant();
        var exact = files.Where(f => f.ToLowerInvariant() == low).ToList();
        if (exact.Count > 0) return exact;
        if (low.Contains('/'))
            return files.Where(f => f.ToLowerInvariant().EndsWith("/" + low)).ToList();
        var named = files.Where(f => f.ToLowerInvariant().Split('/')[^1] == low).ToList();
        if (named.Count > 0) return named;
        return files.Where(f => f.ToLowerInvariant().Split('/')[^1].EndsWith("." + low)).ToList();
    }

    private static bool Falls(string rel, int first, int last)
    {
        var lines = Read(rel).Split('\n');
        var count = lines.Length - (lines.Length > 0 && lines[^1].Length == 0 ? 1 : 0);
        if (first < 1 || last < first || last > count) return true;
        return lines[first - 1].Trim().Length == 0 || lines[last - 1].Trim().Length == 0;
    }

    /// <summary>那颗档名<b>只认末段</b>＋首号：正文写全路径还是简写都算同一颗号（与 <c>bin/WI99/pointer86_r1</c>
    /// 那颗 Python 尺同一条判据——两把尺各认各的形状就会各执一词，名册 461 那一族）。</summary>
    private static bool SameFile(string matched, string wanted)
        => string.Equals(matched.Split('/')[^1], wanted.Split('/')[^1], StringComparison.OrdinalIgnoreCase);

    /// <summary>现读"指向本仓活源档、且端点落空"的行号指针颗数（同一把判据的第二副眼睛，与那颗 Python 尺成对）。</summary>
    private static List<string> FallenPointers()
    {
        var files = SourceFiles();
        var found = new List<string>();
        foreach (var rel in Living)
            foreach (Match m in Pointer.Matches(Read(rel)))
            {
                var name = m.Groups[1].Value;
                var first = int.Parse(m.Groups[2].Value);
                var last = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : first;
                var targets = Resolve(name, files);
                if (targets.Count > 0 && targets.All(t => Falls(t, first, last)))
                    found.Add(rel + ":" + name + ":" + first);
            }
        return found;
    }

    /// <summary>按<b>末段</b>认（尾段全等 → 尾段后缀那一步兜底），交回仓里所有对得上的那颗——这一把只数候选，⛔ 挑一颗。</summary>
    private static List<string> ByTail(string pointer, List<string> files)
    {
        var tail = pointer.Replace('\\', '/').ToLowerInvariant().Split('/')[^1];
        var named = files.Where(f => f.ToLowerInvariant().Split('/')[^1] == tail).ToList();
        return named.Count > 0
            ? named
            : files.Where(f => f.ToLowerInvariant().Split('/')[^1].EndsWith("." + tail)).ToList();
    }

    /// <summary>那一族的<b>唯一一把形状尺</b>：写了目录前缀、而那颗前缀既⛔ 等仓里那颗整串路径、连"带斜杠的后缀"也⛔ 中——
    /// 正是那颗 <see cref="Resolve"/> 交回空、两把棘轮都⛔ 看得见的一族。
    /// <para>它⛔ 调 <see cref="Resolve"/>，而是把它那两条分支<b>就地重走一遍</b>：那是刻意的——把 <see cref="Resolve"/>
    /// 放宽成"末段全认"确实能让盲区里那几颗"看起来认得到了"，而⛔ 等于那一族消失（名册 348「⛔ 让强者退到弱者口径」）。
    /// 那颗放宽另由 <see cref="TheRulerItselfBitesAndTheScopeIsReal"/> 钉 <see cref="Resolve"/> 现行为挡下。</para>
    /// <para>⚠ 口径边界（本批现读）：<c>ViewModels/ItemCardViewModel.cs</c> 那种<b>后缀认得到</b>的前缀⛔ 归本维——
    /// 它在片86 的口径下合法（那颗尺认得到、号也量得到），那一族那颗数现读见正本
    /// <c>bin/WI99/p843_r1/shapes88.txt</c>，已交账本 P-256 ⑤ 登记一维问句（"要不要一律全路径"），
    /// ⛔ 在本批顺手把它们改掉＝越权扩大射程。</para></summary>
    private static List<(string Doc, string Name, int First, int TailCandidates)> PrefixShapes(string text, string rel, List<string> files)
    {
        var rows = new List<(string, string, int, int)>();
        foreach (Match m in Pointer.Matches(text))
        {
            var name = m.Groups[1].Value.Replace('\\', '/').ToLowerInvariant();
            if (!name.Contains('/')) continue;                                   // 裸档名那一族归 FallenPointers 问
            if (files.Any(f => f.ToLowerInvariant() == name)) continue;          // 全路径＝口径要的样子
            if (files.Any(f => f.ToLowerInvariant().EndsWith("/" + name))) continue;   // 后缀认得到＝上面那段边界
            var tail = ByTail(name, files).Count;
            if (tail == 0) continue;                                             // 末段也认不到＝跨仓／已删那一族（现读 3 颗）
            rows.Add((rel, name, int.Parse(m.Groups[2].Value), tail));
        }
        return rows;
    }

    private static List<(string Doc, string Name, int First, int TailCandidates)> AllPrefixShapes()
    {
        var files = SourceFiles();
        var all = new List<(string, string, int, int)>();
        foreach (var rel in Living)
            all.AddRange(PrefixShapes(Read(rel), rel, files));
        return all;
    }

    /// <summary>盲区那一族＝整串认不到、而末段<b>恰一颗</b>（那颗 <see cref="Resolve"/> 今天对它交回空＝35 那颗基线上的一片盲区）。</summary>
    private static List<string> UnresolvedPrefixPointers()
        => AllPrefixShapes().Where(r => r.TailCandidates == 1)
            .Select(r => r.Doc + " 第" + r.Name + "那一串").ToList();

    /// <summary>歧义那一族＝整串认不到、末段认得到<b>两颗以上</b>＝单列一维（⛔ 取排序后第一颗＝把我的选择当成正文的错，名册 716）。</summary>
    private static List<string> AmbiguousPrefixPointers()
        => AllPrefixShapes().Where(r => r.TailCandidates >= 2)
            .Select(r => r.Doc + " 第" + r.Name + "那一串").ToList();

    /// <summary>那一族<b>整串形状</b>的颗数（盲区＋歧义＋末段也认不到的带前缀串）＝那颗新格的判据。</summary>
    private static List<string> PrefixedAbbreviationShapes()
        => AllPrefixShapes().Select(r => r.Doc + " 第" + r.Name + "那一串").ToList();

    [Fact]
    public void BannedLiveSourceLineNumberPointersStayOutOfTheLivingDocs()
    {
        foreach (var (name, first, last) in BannedPointers)
            foreach (var rel in Living)
            {
                var hits = Pointer.Matches(Read(rel)).Cast<Match>()
                    .Where(m => SameFile(m.Groups[1].Value, name) && int.Parse(m.Groups[2].Value) == first).ToList();
                Assert.True(hits.Count == 0,
                    rel + " 又把已定性的那颗号写回来了：" + name + ":" + first
                    + (last != first ? "～" + last : "") + " ⇒ 那正是片86 逐颗定性掉的号，换成正文里那颗逐字锚");
            }
        Assert.True(BannedPointers.Length == 38 && RewrittenAnchors.Length == 65,
            "两张名单颗数变了（禁项 " + BannedPointers.Length + "・锚 " + RewrittenAnchors.Length
            + "）＝射程或口径动过，先回来重跑那颗尺（bin/WI99/pointer86_r1・bin/WI99/p843_r1）再改这里");
    }

    [Fact]
    public void EveryRewrittenAnchorIsStillVerbatimOnBothSides()
    {
        var body = string.Concat(Living.Select(Read));
        foreach (var (path, anchor) in RewrittenAnchors)
        {
            Assert.True(Read(path).Contains(anchor, StringComparison.Ordinal),
                "那颗锚「" + anchor + "」在 " + path + " 里读不到了＝那一发被改了/删了，正文的引用当场过期");
            Assert.True(body.Contains(anchor, StringComparison.Ordinal),
                "那颗锚「" + anchor + "」在 " + path + " 里读得到，可活档一处也没念＝改口没落到正文上（这颗尺的两半要同时在场）");
        }
    }

    [Fact]
    public void TheRulerItselfBitesAndTheScopeIsReal()
    {
        Assert.Single(Pointer.Matches("依据见 `ItemRepository.Search.cs:622`。").Cast<Match>(),
                   m => m.Groups[1].Value == "ItemRepository.Search.cs" && m.Groups[2].Value == "622");
        Assert.Single(Pointer.Matches("<c>src/StarMark.Core/A.cs：41～57</c>").Cast<Match>(),
                   m => m.Groups[3].Value == "57");                 // 全角冒号＋尾号都认（⛔ 只认第一颗＝端点⛔ 查）
        Assert.Empty(Pointer.Matches("那颗桥 board-bridge.js:207").Cast<Match>());   // 冻结树归片85 那把
        Assert.Empty(Pointer.Matches("A.csproj:1 那一发").Cast<Match>());             // ⛔ 是源档指针
        foreach (var rel in Living)
            Assert.True(Read(rel).Length > 100000, rel + " 现读⛔ 像一份真档＝那颗「0 颗」的读数⛔ 可信");
        var files = SourceFiles();
        Assert.True(files.Count > 300, "现索引的源档只有 " + files.Count + " 颗＝那颗 Walk 坏了，0 颗的读数⛔ 可信");
        Assert.Contains("src/StarMark.Data/ItemRepository.Search.cs", files);          // partial 那族要扫得到
        // ⛔ 许把「盲区」修成「看不见」：那颗 Resolve 对带目录前缀的简写<b>就是</b>交回空——这一句钉住它今天的形状，
        // 谁把它放宽成「末段全认」，那颗 35 的棘轮会跟着变脸，这一句先红（名册 348「⛔ 让强者退到弱者口径」）。
        Assert.Empty(Resolve("Core/Widgets/WidgetStorage.cs", files));
        Assert.NotEmpty(Resolve("WidgetStorage.cs", files));                            // 末段那一族认得到＝盲区⛔ 是因为档名压根不存在
        Assert.NotEmpty(FallenPointers());                                             // 尺⛔ 许恒绿：这一族真的还剩
    }

    [Fact]
    public void ThePrefixBlindPointerCensusIsTheOneThisBatchReconciled()
    {
        var blind = UnresolvedPrefixPointers();
        var ambiguous = AmbiguousPrefixPointers();
        Assert.True(blind.Count == PrefixBlindPointerBaseline,
            "活档里「带目录前缀而那颗前缀⛔ 等仓里那颗路径、末段又恰一颗」的行号指针现读 " + blind.Count
            + " 颗↔片88 收口基线 " + PrefixBlindPointerBaseline + " 颗（换锚前那颗 Python 尺现读 8 颗）。"
            + "<b>多了</b>＝正文又写了那种简写，按 P-256 ③ 的口径换成「全路径 的 逐字锚」；"
            + "<b>⛔ 许</b>把这一族并进那颗 " + FallenPointerBaseline + " 的棘轮：两颗数各问各的一维，"
            + "合起来就⛔ 知道坏在哪一边。");
        Assert.True(ambiguous.Count == AmbiguousPrefixPointerBaseline,
            "同一族里「末段认得到两颗以上」的歧义那颗现读 " + ambiguous.Count + " 颗↔基线 "
            + AmbiguousPrefixPointerBaseline + " 颗。那一族⛔ 许靠「排序后挑第一颗」糊过去（名册 716＝把我的选择当成正文的错），"
            + "真要修就把它写成那颗档里读得到的<b>全路径＋逐字锚</b>。");
    }

    /// <summary>那颗新格：把 8 颗的<b>形状</b>禁掉（⛔ 禁那颗号——账本 ✅P-10 那条盘点还合法地写着全路径的那三处号）。</summary>
    [Fact]
    public void PrefixedAbbreviationsStayOutOfTheLivingDocBodies()
    {
        var files = SourceFiles();
        // 动作尺⛔ 正件（名册 425／461）：同一颗档四种形状各钉一颗＝全路径⛔ 红・带⛔ 等前缀红・裸名交回另一维・后缀认得到的那族交回口径边界。
        Assert.Empty(PrefixShapes("见 `src/StarMark.Core/Widgets/WidgetStorage.cs:647`。", "probe", files));
        Assert.Single(PrefixShapes("见 `Core/Widgets/WidgetStorage.cs:647`。", "probe", files));
        Assert.Empty(PrefixShapes("那颗档住 `WidgetStorage.cs` 而没写号。", "probe", files));
        Assert.Empty(PrefixShapes("见 `ViewModels/ItemCardViewModel.cs:40`。", "probe", files));   // 后缀认得到＝⛔ 本维（上面那段边界有正件）
        var now = PrefixedAbbreviationShapes();
        Assert.True(now.Count == 0,
            "活档正文里现读 " + now.Count + " 颗「带目录前缀而那颗前缀与仓里那颗路径⛔ 相等」的行号指针："
            + string.Join("・", now.Take(8))
            + " ⇒ 那种写法让 <see cref=\"Resolve\"/> 交回空、两把棘轮都⛔ 看得见（片86 数出的那片盲区），"
            + "按 P-256 ③ 的口径改成「全路径 的 逐字锚」");
    }

    [Fact]
    public void TheFallenPointerCensusIsTheOneThisBatchReconciled()
    {
        var now = FallenPointers();
        Assert.True(now.Count == FallenPointerBaseline,
            "活档里指向本仓活源档、端点落空的行号指针现读 " + now.Count + " 颗↔片86 收口基线 " + FallenPointerBaseline
            + " 颗（改前 75）。<b>多了</b>＝新写了会漂的号，按口径换成锚；<b>少了</b>＝这一族又修掉了几颗，"
            + "回来把这颗数改小并同批在账本 P-256 报一句——那颗数⛔ 是判据，它只许在真动过时逼我重新对一次账");
    }

    /// <summary>那一族在活档里的逐颗现场：哪一档・所在那一行・正文念的那一串・那一行有没有带指称。</summary>
    private static List<(string Doc, int Line, string Spoken, bool Marked)> ProseOccurrences(string text, string rel)
    {
        var rows = new List<(string, int, string, bool)>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            foreach (Match m in Prose.Matches(lines[i]))
                rows.Add((rel, i + 1, m.Value,
                    ProseMarkers.Any(mk => lines[i].Contains(mk, StringComparison.Ordinal))));
        return rows;
    }

    private static List<(string Doc, int Line, string Spoken, bool Marked)> AllProseOccurrences()
    {
        var all = new List<(string, int, string, bool)>();
        foreach (var rel in Living)
            all.AddRange(ProseOccurrences(Read(rel), rel));
        return all;
    }

    /// <summary>那颗禁项判据本体：交回一段正文里在场的禁项串。名单格与「尺自己咬得动」那一格共用同一副眼睛，
    /// ⛔ 两把各写一遍判据（写两遍就会漂成两种口径，名册 461 那一族）。</summary>
    private static List<string> BannedProseHits(string text)
        => BannedProsePointers.Where(old => text.Contains(old, StringComparison.Ordinal)).ToList();

    /// <summary>片90 新格一：九颗已定性的散文行号指针⛔ 许写回（形状与 <see cref="BannedPointers"/> 同一口径：
    /// 禁<b>那一串写法</b>，⛔ 禁那颗数——历史证据那一族照旧留在正文里）。</summary>
    [Fact]
    public void BannedProseLinePointersStayOutOfTheLivingDocs()
    {
        foreach (var rel in Living)
        {
            var hits = BannedProseHits(Read(rel));
            Assert.True(hits.Count == 0,
                rel + " 又把已改口的那颗散文行号指针写回来了：" + string.Join("・", hits)
                + " ⇒ 那一族的号随任何一次合法编辑挪号，而 <see cref=\"Pointer\"/> 那把尺⛔ 看得见它（P-256 ⑤）");
        }
        Assert.True(BannedProsePointers.Length == 8 && ProseMarkers.Length == 3,
            "那一族的两张名单颗数变了（禁项 " + BannedProsePointers.Length + "・指称 " + ProseMarkers.Length
            + "）＝射程或口径动过，先回来重跑那颗尺（bin/WI99/p990_r1）再改这里");
    }

    /// <summary>片90 新格二：留下来的每一颗「第 N 行」必须由<b>同一行</b>念得到的一颗指称解释得了
    /// （小节号／历史登记／那张表的名字）。新写一颗而不说它指哪一节，这一格当场红。</summary>
    [Fact]
    public void EveryProseLinePointerNamesItsReferent()
    {
        var unmarked = AllProseOccurrences().Where(r => !r.Marked).ToList();
        Assert.True(unmarked.Count == 0,
            "活档正文里现读 " + unmarked.Count + " 颗「第 N 行」而那一行⛔ 带任何指称："
            + string.Join("・", unmarked.Select(r => r.Doc + ":" + r.Line + r.Spoken).Take(8))
            + " ⇒ 那种写法要么换成「档名＋逐字锚」（P-256 ③ 那颗口径），要么把它的指称写出来（§x.y／那张表／那句历史登记）");
    }

    /// <summary>片90 新格三：那把尺自己咬得动，且⛔ 聋——个位那颗号要认得到、带档名那颗号⛔ 重复计、
    /// 三副正件各放过一颗（同 <see cref="TheRulerItselfBitesAndTheScopeIsReal"/> 的形状）。</summary>
    [Fact]
    public void TheProseRulerItselfBites()
    {
        Assert.Single(ProseOccurrences("账本第 999 行原写着那句。", "probe"), r => r.Spoken == "第 999 行");
        Assert.Single(ProseOccurrences("那颗 <c>export.d.ts</c> 第 30 行正是那句。", "probe"),
                     r => !r.Marked);                                     // 带档名也照抓＝与 <see cref="Pointer"/> 两族⛔ 重叠
        Assert.Single(ProseOccurrences("第 2 行的归因。", "probe"), r => !r.Marked);   // 个位那颗要认得到（⛔ 只量两位数＝尺聋）
        Assert.Single(ProseOccurrences("§21.0 第 7 行还专门写那句。", "probe"), r => r.Marked);
        Assert.Single(ProseOccurrences("上面那张「由这颗顺序得出的判断」表里第 2 行那颗归因。", "probe"), r => r.Marked);
        Assert.Empty(ProseOccurrences("那一节里第 3 条与第 5 项各说过一次。", "probe"));        // ⛔ 是「行」的就⛔ 抓（正则放宽会红在这里）
        Assert.Single(BannedProseHits("旧写法照抄一遍：① 账本第 2533 行原写着……"),
                      old => old.StartsWith("① 账本第 2533"));                                  // 禁项判据本体咬得动
        Assert.Empty(BannedProseHits("新写法：① 账本 P-116 那条的「依据」行原写着……"));          // 正件＝改口后的那句
        var now = AllProseOccurrences();
        Assert.True(now.Count > 0 && now.Any(r => r.Marked),
            "那一族现读 " + now.Count + " 颗＝那颗尺在活档上恒绿⛔ 可信（名册 485：数东西的尺要配「>0」断言）");
    }

    /// <summary>片90 新格四：那一族的颗数棘轮（改口前 18 → 落盘后本读数）。</summary>
    [Fact]
    public void TheProsePointerCensusIsTheOneThisBatchReconciled()
    {
        var now = AllProseOccurrences();
        Assert.True(now.Count == ProsePointerBaseline,
            "活档里「第 N 行」那一族现读 " + now.Count + " 颗↔片90 收口基线 " + ProsePointerBaseline
            + " 颗（改口前 18 颗）。<b>多了</b>＝又写了新的行号式指针；<b>少了</b>＝这一族又修掉了几颗，"
            + "回来把这颗数改小并同批在账本 P-256 ⑤ 报一句。两颗数各问各的一维，⛔ 与那颗 "
            + FallenPointerBaseline + " 合并");
    }

    /// <summary>片99 新格：第三档散文「第 N 行」颗数棘轮（P-256 ⑪①・账本 ⑰・用户裁决＝只钉总颗）。
    /// 报告／坑表虽是活档，这一族却全是历史叙述（41 颗全部未带指称），故只钉总颗、
    /// ⛔ 镜像活档那颗「未带指称 0」＝清零要改写历史证据。先决「真行切分」已实测消解：
    /// LF 与真行两口径两族读数十格全零差（正本 <c>bin/WI99/p999_r1/probe99-读数.txt</c>④段）。</summary>
    [Fact]
    public void TheThirdTierProseCensusStaysPinned()
    {
        for (var i = 0; i < ThirdTier.Length; i++)
        {
            var rows = ProseOccurrences(Read(ThirdTier[i]), ThirdTier[i]);
            Assert.True(rows.Count == ThirdTierProseBaseline[i],
                ThirdTier[i] + " 里「第 N 行」那一族现读 " + rows.Count + " 颗↔片99 收口基线 "
                + ThirdTierProseBaseline[i] + " 颗（颗口径・LF 口径〔名册 407〕）。<b>多了</b>＝又写了新的散文行号——"
                + "先把它的指称写出来（或换成「档名＋逐字锚」）并同批在账本 P-256 ⑰ 报一句；"
                + "<b>少了</b>＝这一族又修掉了几颗，回来把这颗数改小并同批报一句");
        }
        Assert.True(ThirdTier.Length == 3 && ThirdTierProseBaseline.Length == 3,
            "第三档名单或基线那两行的颗数变了（名单 " + ThirdTier.Length + "・基线 " + ThirdTierProseBaseline.Length
            + "）＝射程动过，先回来重跑那颗尺（正本 bin/WI99/p999_r1）再改这里");
    }

    /// <summary>片94 新格一：裸简写颗数棘轮（P-256 ⑪③・账本 ⑫）——只钉两面活档
    /// （报告／坑表按既有射程⛔ 归本条，与 <see cref="Living"/> 同一口径）。
    /// 82 颗全数定性之后全数合法留下：账本那 35＝历史登记（片56/57 时点现读）；深析那 16 里 §28 那
    /// 5 颗过期「现在时」凭据已删号留锚。这两颗数只逼对账：多了＝新写了会漂的号；少了＝这一族又修掉几颗。</summary>
    [Fact]
    public void TheBareShorthandCensusIsTheOneThisBatchReconciled()
    {
        var ledger = BareShorthandCount(Read(Living[0]));
        var deep = BareShorthandCount(Read(Living[1]));
        Assert.True(ledger == LedgerBareShorthandBaseline,
            "账本正文里那族裸简写现读 " + ledger + " 颗↔片94 收口基线 " + LedgerBareShorthandBaseline
            + " 颗（82 颗全定性・全数合法留下・正本 bin/WI99/p994_r1/census94-读数-r2.txt）。"
            + "<b>多了</b>＝又写了会随合法编辑漂的号，按 P-256 ③ 的口径换成「档名＋逐字锚」，"
            + "历史登记那一族照旧合法（账本 21110 那句注解罩着）；<b>少了</b>＝这一族又修掉了几颗，"
            + "回来把这颗数改小并同批在账本 P-256 ⑫ 报一句——那颗数⛔ 是判据，它只许在真动过时逼我重新对一次账");
        Assert.True(deep == DeepDiveBareShorthandBaseline,
            "深析正文里那族裸简写现读 " + deep + " 颗↔片94 收口基线 " + DeepDiveBareShorthandBaseline
            + " 颗（改前 21・§28 五颗过期「现在时」凭据删号留锚后 16・正本同上）。"
            + "<b>多了</b>＝又写了会漂的号，换成「档名＋逐字锚」；<b>少了</b>＝回来把这颗数改小并同批报一句。"
            + "两颗数各问各的一面，⛔ 并成一颗总数——并了就⛔ 知道坏在哪一边");
    }

    /// <summary>片94 新格二：那把尺自己咬得动——A／B／复合三副要咬，体积读数、裸号、无包裹三副⛔ 咬
    /// （与 <c>bin/WI99/p994_r1/census94_r2.py</c> 的自证同一副判据）。
    /// ⚠ 正件串在源码里<b>断成两截拼出来</b>：那颗 Python 普查尺扫 .cs 时连字符串一起咬
    /// （代码注释那一族现读 4 颗），整副写出来＝自己给自己添一颗假账。</summary>
    [Fact]
    public void TheBareShorthandRulerItselfBites()
    {
        Assert.Single(BareShorthand.Matches("<c" + ">:131</c>").Cast<Match>());
        Assert.Single(BareShorthand.Matches("<c" + ">228～233</c>").Cast<Match>());      // 区间那副
        Assert.Single(BareShorthand.Matches("<c" + ">:4872～4888</c>").Cast<Match>());  // 冒号＋区间复合
        Assert.Empty(BareShorthand.Matches("<c>4194304</c>").Cast<Match>());             // 体积读数⛔ 收
        Assert.Empty(BareShorthand.Matches("<c>300</c>").Cast<Match>());                 // 裸号（无冒号无区间）⛔ 收
        Assert.Empty(BareShorthand.Matches("那颗 :131 就在这。").Cast<Match>());          // 无包裹⛔ 收
        Assert.Equal(2, BareShorthandCount("<c" + ">:1</c>\r\n<c" + ">:2</c>"));         // 逐行基：两行各一颗
        Assert.Equal(2, BareShorthandCount("同一行两颗 <c" + ">:3</c> 与 <c" + ">:4</c>"));  // 同行两颗各计
        var ledger = BareShorthandCount(Read(Living[0]));
        var deep = BareShorthandCount(Read(Living[1]));
        Assert.True(ledger > 0 && deep > 0,
            "两面现读 " + ledger + "／" + deep + " 颗＝那颗尺在活档上恒绿⛔ 可信（名册 485：数东西的尺要配「>0」断言）");
    }
}
