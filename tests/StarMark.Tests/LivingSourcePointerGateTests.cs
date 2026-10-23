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
    /// ⚠ <b>已知的一颗射程缺口（片86 现读，交账本 P-256 ④）</b>：<see cref="Resolve"/> 认不到「带目录前缀而那颗前缀
    /// 与仓里那颗路径⛔ 相等」的简写（例：<c>Core/Widgets/WidgetStorage.cs</c>／<c>UI/Views/SnapshotPage.xaml.cs</c>），
    /// 而 <c>FallenPointers</c> 只数「认得到档名<b>且</b>端点全落空」⇒那一族 8 颗<b>既不叫落空也⛔ 叫在</b>，
    /// 那颗基线 35<b>⛔ 是活档行号指针的全数</b>（同形状命中现读 526＋22 颗，另有 3 颗是跨仓／读不到的号）。
    /// 读到这颗基线的人要连着读这句，⛔ 拿它当「活档里再没坏号」。
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
    };

    /// <summary>改口写进正文的那 57 颗锚：每颗必须<b>正文念得到、它指向的那颗档也读得到</b>。</summary>
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
    };

    /// <summary>片86 收口时现读的落空残留基线：改前 75 颗 → 改后这颗数。⛔ 是"归零"，是"还剩这一族"的<b>现读证</b>。</summary>
    private const int FallenPointerBaseline = 35;

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
        Assert.True(BannedPointers.Length == 31 && RewrittenAnchors.Length == 57,
            "两张名单颗数变了（禁项 " + BannedPointers.Length + "・锚 " + RewrittenAnchors.Length
            + "）＝射程或口径动过，先回来重跑那颗尺（bin/WI99/pointer86_r1）再改这里");
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
        Assert.NotEmpty(FallenPointers());                                             // 尺⛔ 许恒绿：这一族真的还剩
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
