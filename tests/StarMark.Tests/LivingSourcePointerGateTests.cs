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
            (@"src/StarMark.Integrations/GitHub/GitHubClient.cs", @"starred_at 由 Link header 不可用"),
            (@"src/StarMark.Integrations/GitHub/GitHubClient.cs", @"改为从 pushed_at 推断"),
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

    /// <summary>片86 收口时现读的落空残留基线：改前 75 颗 → 改后这颗数。⛔ 是"归零"，是"还剩这一族"的<b>现读证</b>。</summary>
    private const int FallenPointerBaseline = 35;

    /// <summary>片88 第二颗棘轮（P-256 ④）：那颗 <see cref="Resolve"/> 对它交回空的「带目录前缀的简写」那一族。
    /// 换锚前现读 8 颗（正本 <c>bin/WI99/p843_r1/census843-读数.txt</c>）→ 本批逐颗换成正文锚之后这颗数。
    /// ⚠ 这颗数与那颗 35 <b>各数各的</b>：35 只含「认得到档名而端点落空」，这一族既⛔ 落空也⛔ 在场＝两份判据⛔ 能合并成一个数。</summary>
    private const int PrefixBlindPointerBaseline = 0;

    /// <summary>同一族的<b>歧义</b>那一维：那颗末段在仓里认得到两颗以上＝⛔ 许替正文挑一颗（名册 716），要单列一维钉住。</summary>
    private const int AmbiguousPrefixPointerBaseline = 0;

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
}
