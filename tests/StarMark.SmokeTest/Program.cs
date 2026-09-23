#nullable enable
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using StarMark.Abstractions;
using StarMark.Core.Search;
using StarMark.Core.Sync;
using StarMark.Data;
using StarMark.Integrations.Everything;
using StarMark.SmokeTest.Mocks;

// 用法：
//   无参数            -> 全新临时 DB，跑 schema + seed + 搜索（自检）
//   query <dbPath> [kw] -> 打开已存在的 DB 只读查询（不写入）
//   sync              -> 用 MockGitHubSource 验证 SyncCoordinator 全流程
if (args.Length >= 1 && args[0] == "sync")
{
    await SyncModeAsync();
    return;
}
if (args.Length >= 2 && args[0] == "query")
{
    await QueryModeAsync(args[1], args.Length >= 3 ? args[2] : "rag");
    return;
}
if (args.Length >= 2 && args[0] == "bookmarks")
{
    await BookmarkSyncCheckAsync(args[1]);
    return;
}
if (args.Length >= 1 && args[0] == "everything")
{
    await EverythingModeAsync(args.Length >= 2 ? args[1] : "*.txt");
    return;
}
if (args.Length >= 1 && args[0] == "tray")
{
    TraySmokeCheck();
    return;
}
if (args.Length >= 1 && args[0] == "ditto")
{
    await DittoCheckAsync();
    return;
}
if (args.Length >= 1 && args[0] == "log")
{
    LogCheck();
    return;
}
if (args.Length >= 1 && args[0] == "widgets")
{
    WidgetsCheck();
    return;
}
if (args.Length >= 1 && args[0] == "capture")
{
    await CaptureCheckAsync();
    return;
}
if (args.Length >= 1 && args[0] == "ocr")
{
    await OcrCheckAsync();
    return;
}

await SmokeModeAsync();

// ===== 全新 DB 自检 =====
static async Task SmokeModeAsync()
{
    BookmarkParseSelfCheck();
    EverythingCsvParseSelfCheck();

    var dbPath = Path.Combine(Path.GetTempPath(), "starmark_smoke.db");
    foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
        if (File.Exists(p)) File.Delete(p);
    Console.WriteLine($"DB: {dbPath}");

    var (sp, _) = BuildServices(dbPath);
    sp.GetRequiredService<MigrationRunner>().EnsureSchema();
    Console.WriteLine("Schema: ok");

    var repo = sp.GetRequiredService<IItemRepository>();
    await SeedAsync(repo);
    Console.WriteLine("Seed: ok");

    await PrintStateAsync(sp);
    Console.WriteLine("DONE");
}

// ===== Everything CSV 解析自检（不依赖 Everything 运行） =====
static void EverythingCsvParseSelfCheck()
{
    var flags = StarMark.Integrations.Everything.EverythingInterop.RequestFlags.FileName
              | StarMark.Integrations.Everything.EverythingInterop.RequestFlags.Path;

    var line = "report.pdf,D:\\Docs,102400,2024-01-15 10:30:00,2024-01-14 09:00:00";
    var item = StarMark.Integrations.Everything.EverythingInterop.ParseCsvLine(line, flags);

    Console.WriteLine($"Everything CSV parse: {(item != null ? $"{item.Title} [{item.Subtitle}]" : "FAILED")}");
    if (item == null) throw new Exception("Everything CSV 解析失败");
    if (item.Title != "report.pdf" || item.Subtitle != @"D:\Docs") throw new Exception("CSV 字段错位");
    if (item.Uri != "file://D:/Docs/report.pdf") throw new Exception("file:// URI 拼接错误");

    // 含引号的路径也要能解析
    var quoted = "\"my file.txt\",\"C:\\My Files\",2048,2024-02-01 08:00:00,2024-01-31 07:00:00";
    var quotedItem = StarMark.Integrations.Everything.EverythingInterop.ParseCsvLine(quoted, flags);
    Console.WriteLine($"Everything CSV quoted: {(quotedItem != null ? $"{quotedItem.Title} [{quotedItem.Subtitle}]" : "FAILED")}");
    if (quotedItem?.Title != "my file.txt" || quotedItem.Subtitle != @"C:\My Files") throw new Exception("引号字段解析错误");
    Console.WriteLine("Everything CSV parse: ok");
}

// ===== 浏览器书签解析自检 =====
static void BookmarkParseSelfCheck()
{
    const string sample = """
    {
      "roots": {
        "bookmark_bar": {
          "type": "folder", "name": "Bookmarks bar",
          "children": [
            { "type": "url", "name": "GitHub", "url": "https://github.com", "date_added": "13170155701705361" },
            { "type": "folder", "name": "AI",
              "children": [
                { "type": "url", "name": "Ollama", "url": "https://ollama.com", "date_added": "13170160000000000" }
              ] }
          ]
        },
        "other": { "type": "folder", "name": "Other bookmarks", "children": [] },
        "synced": { "type": "folder", "name": "Mobile bookmarks", "children": [] }
      }
    }
    """;

    var entries = StarMark.Integrations.Bookmarks.BookmarksFileParser.ParseJson(sample);
    Console.WriteLine($"Bookmark parse: total={entries.Count}");
    foreach (var e in entries)
    {
        var tag = e.FolderPaths.Count > 0 ? e.FolderPaths[^1] : "<none>";
        Console.WriteLine($"  - {e.Title,-10} {e.Url,-28} folder=[{string.Join("/", e.FolderPaths)}] tag={tag} added={e.BookmarkedAt}");
    }
    if (entries.Count != 2) throw new Exception($"书签解析数量不符: {entries.Count}, 期望 2");
    if (entries[0].FolderPaths.Count != 0) throw new Exception("根级书签不应有文件夹标签");
    if (entries[1].FolderPaths.Count != 1 || entries[1].FolderPaths[0] != "AI") throw new Exception("嵌套书签文件夹路径错误");
    if (entries[0].BookmarkedAt <= 0) throw new Exception("date_added 转换失败");
    Console.WriteLine("Bookmark parse: ok");
}

// ===== 统一日志（log） =====
static void LogCheck()
{
    StarMark.Abstractions.StarLog.Info("smoke-log-write");
    StarMark.Abstractions.StarLog.Warn("smoke-log-warn");
    var file = StarMark.Abstractions.StarLog.CurrentLogFile;
    Console.WriteLine($"Log file: {file}");
    if (!System.IO.File.Exists(file)) throw new Exception("日志文件未创建");
    var content = System.IO.File.ReadAllText(file);
    if (!content.Contains("smoke-log-write")) throw new Exception("日志未写入 INFO 行");
    if (!content.Contains("smoke-log-warn")) throw new Exception("日志未写入 WARN 行");
    Console.WriteLine($"Log size: {content.Length} bytes");
    Console.WriteLine("Log: ok");
    Console.WriteLine("DONE");
}

// ===== Ditto 剪贴板源（ditto） =====
static async Task DittoCheckAsync()
{
    var dbPath = Path.Combine(Path.GetTempPath(), "starmark-ditto-test.db");
    if (File.Exists(dbPath)) File.Delete(dbPath);

    // 1. 构造合成 DittoDB（对照 Ditto 源码 schema）
    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
    {
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE Main(
                  lID INTEGER PRIMARY KEY AUTOINCREMENT,
                  lDate INTEGER, mText TEXT, lShortCut INTEGER, lDontAutoDelete INTEGER,
                  CRC INTEGER, bIsGroup INTEGER, lParentID INTEGER, QuickPasteText TEXT,
                  clipOrder REAL, clipGroupOrder REAL, globalShortCut INTEGER,
                  lastPasteDate INTEGER, stickyClipOrder REAL, stickyClipGroupOrder REAL,
                  MoveToGroupShortCut INTEGER, GlobalMoveToGroupShortCut INTEGER);
                CREATE TABLE Data(
                  lID INTEGER PRIMARY KEY AUTOINCREMENT,
                  lParentID INTEGER, strClipBoardFormat TEXT, ooData BLOB);
                """;
            cmd.ExecuteNonQuery();
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        InsertClip(conn, lID: 1, date: now, text: "Hello StarMark 剪贴板集成测试", isGroup: 0);
        InsertClip(conn, lID: 2, date: now - 10, text: "C:\\Data\\demo\\读我.txt", isGroup: 0);

        // 文本格式原始数据（UTF-16LE）
        using (var d1 = conn.CreateCommand())
        {
            d1.CommandText = "INSERT INTO Data (lParentID, strClipBoardFormat, ooData) VALUES (1, 'CF_UNICODETEXT', $b)";
            d1.Parameters.AddWithValue("$b", System.Text.Encoding.Unicode.GetBytes("Hello StarMark 剪贴板集成测试"));
            d1.ExecuteNonQuery();
        }

        // 文件格式 DROPFILES（宽字符）
        using (var d2 = conn.CreateCommand())
        {
            var hdrop = BuildDropFiles("C:\\Data\\demo\\读我.txt");
            d2.CommandText = "INSERT INTO Data (lParentID, strClipBoardFormat, ooData) VALUES (2, 'CF_HDROP', $b)";
            d2.Parameters.AddWithValue("$b", hdrop);
            d2.ExecuteNonQuery();
        }

        // 分组节点（bIsGroup=1）应被跳过
        InsertClip(conn, lID: 3, date: now - 20, text: "分组: 常用", isGroup: 1);
    }

    // 2. FetchAsync 全量拉取
    var source = new StarMark.Integrations.Ditto.DittoSource(dbPath);
    Console.WriteLine($"Ditto IsAvailable: {source.IsAvailable}");
    var items = await source.FetchAsync(new StarMark.Abstractions.SyncContext(), CancellationToken.None);
    foreach (var it in items)
        Console.WriteLine($"  [{it.Type}] {it.Title} | {it.Subtitle} | uri={it.Uri}");
    if (items.Count != 2) throw new Exception($"Ditto 应返回 2 条非分组记录，实际 {items.Count}");
    if (items.Any(i => i.SourceId == "ditto:3")) throw new Exception("分组节点不应被导入");

    var textItem = items.First(i => i.SourceId == "ditto:1");
    Console.WriteLine($"  Fetch textItem.Title = {textItem.Title}");
    if (textItem.Title != "Hello StarMark 剪贴板集成测试") throw new Exception("文本剪贴板标题解析错误");

    var fileItem = items.First(i => i.SourceId == "ditto:2");
    Console.WriteLine($"  Fetch fileItem.Title = {fileItem.Title}");
    if (fileItem.Uri != "file:///C:/Data/demo/读我.txt") throw new Exception($"CF_HDROP 文件路径解析错误: {fileItem.Uri}");

    // 3. SearchAsync 实时检索
    var hits = await source.SearchAsync("StarMark", new StarMark.Abstractions.SearchFilter { MaxResults = 10 }, CancellationToken.None);
    Console.WriteLine($"  Search 'StarMark': {hits.Count} 条");
    if (hits.All(h => h.SourceId != "ditto:1")) throw new Exception("实时检索未命中剪贴板文本");

    for (int i = 0; i < 5; i++)
    {
        try { if (File.Exists(dbPath)) File.Delete(dbPath); break; }
        catch { System.Threading.Thread.Sleep(150); }
    }
    Console.WriteLine("Ditto: ok");
    Console.WriteLine("DONE");
}

static void InsertClip(Microsoft.Data.Sqlite.SqliteConnection conn, long lID, long date, string text, int isGroup)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        INSERT INTO Main (lID, lDate, mText, lShortCut, lDontAutoDelete, CRC, bIsGroup, lParentID, QuickPasteText,
                          clipOrder, clipGroupOrder, globalShortCut, lastPasteDate)
        VALUES ($id, $date, $text, 0, 0, 0, $isGroup, 0, '', 0, 0, 0, $date)
        """;
    cmd.Parameters.AddWithValue("$id", lID);
    cmd.Parameters.AddWithValue("$date", date);
    cmd.Parameters.AddWithValue("$text", text);
    cmd.Parameters.AddWithValue("$isGroup", isGroup);
    cmd.ExecuteNonQuery();
}

static byte[] BuildDropFiles(string path)
{
    var body = System.Text.Encoding.Unicode.GetBytes(path + "\0");
    var buffer = new byte[20 + body.Length + 2]; // 头部 + 路径 + 结束双 NUL
    Array.Copy(body, 0, buffer, 20, body.Length);
    BitConverter.GetBytes(20u).CopyTo(buffer, 0);      // pFiles
    BitConverter.GetBytes(1u).CopyTo(buffer, 16);      // fWide = true
    return buffer;
}

// ===== 托盘宿主（tray） =====
static void TraySmokeCheck()
{
    Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
    using var host = new StarMark.Integrations.SystemTray.TrayHost();
    var ok = host.Initialize();
    Console.WriteLine($"Tray host available: {ok}");
    if (!ok) throw new Exception("托盘宿主初始化失败");

    // 全局热键不在这里验：批次 JB 起 TrayHost 不再注册热键（它那份同名方法早已是只打日志的空壳），
    // 注册统一由 StarMark.UI 的 HotkeyService 挂在主窗口句柄上——冒烟程序没有那个窗口，验不了。
    Console.WriteLine("Hotkey registration owned by HotkeyService (not covered by this smoke check)");
    host.ShowNotification("SmokeTest", "托盘冒烟自检");
    Console.WriteLine("Tray balloon modify: ok");
    Console.WriteLine("DONE");
}

// ===== Everything 真实查询（everything [query]） =====
static async Task EverythingModeAsync(string query)
{
    // 探测 Everything 安装位置（复用适配器逻辑，仅报告）
    var available = ProbeEverything();
    Console.WriteLine($"Everything executable: {available ?? "<not found>"}");

    var services = new ServiceCollection();
    services.AddSingleton<StarMark.Integrations.Everything.EverythingQueryQueue>();
    services.AddSingleton<StarMark.Integrations.Everything.EverythingSource>();
    services.AddSingleton<IItemSource>(sp => sp.GetRequiredService<StarMark.Integrations.Everything.EverythingSource>());
    services.AddSingleton<SearchService>();
    var sp = services.BuildServiceProvider();

    var source = sp.GetRequiredService<StarMark.Integrations.Everything.EverythingSource>();
    Console.WriteLine($"Everything running: {source.IsAvailable}");

    if (!source.IsAvailable && available == null)
    {
        Console.WriteLine("Everything 未安装或未运行，跳过真实查询。");
        return;
    }

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var items = await source.SearchAsync(query, new SearchFilter { MaxResults = 20, IncludeSize = true }, CancellationToken.None);
    sw.Stop();

    Console.WriteLine($"Query '{query}': {items.Count} 条 · {sw.ElapsedMilliseconds}ms");
    foreach (var item in items.Take(10))
        Console.WriteLine($"  - {item.Title}  [{item.Subtitle}]  size={item.FileSize?.ToString() ?? "-"}");
    if (items.Count > 10) Console.WriteLine($"  ... 共 {items.Count} 条");
    Console.WriteLine("DONE");
}

static string? ProbeEverything()
{
    var candidates = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Everything", "Everything.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Everything", "Everything.exe"),
    };
    return candidates.FirstOrDefault(File.Exists);
}

// ===== 真实书签文件检查（bookmarks <path>） =====
static async Task BookmarkSyncCheckAsync(string filePath)
{
    if (!File.Exists(filePath)) { Console.WriteLine($"Bookmarks file not found: {filePath}"); return; }
    Console.WriteLine($"Bookmarks file: {filePath} ({new FileInfo(filePath).Length} bytes)");

    var entries = StarMark.Integrations.Bookmarks.BookmarksFileParser.ParseFile(filePath);
    Console.WriteLine($"Bookmark file parse: total={entries.Count}");
    foreach (var e in entries.Take(5))
    {
        var tag = e.FolderPaths.Count > 0 ? e.FolderPaths[^1] : "<none>";
        Console.WriteLine($"  - {e.Title,-24} {e.Url,-36} folder=[{string.Join("/", e.FolderPaths)}] tag={tag}");
    }
    if (entries.Count > 5) Console.WriteLine($"  ... 共 {entries.Count} 条");

    // E2E：把 Edge 书签同步进临时 DB 并搜索
    var dbPath = Path.Combine(Path.GetTempPath(), "starmark_bookmarks.db");
    foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
        if (File.Exists(p)) File.Delete(p);

    var services = new ServiceCollection();
    services.AddSingleton(new DbConnectionFactory(dbPath));
    services.AddSingleton(sp => new MigrationRunner(sp.GetRequiredService<DbConnectionFactory>()));
    services.AddSingleton<IItemRepository, ItemRepository>();
    services.AddSingleton(new StarMark.Integrations.Bookmarks.EdgeBookmarksSource(filePath));
    services.AddSingleton<IItemSource>(sp => sp.GetRequiredService<StarMark.Integrations.Bookmarks.EdgeBookmarksSource>());
    services.AddSingleton<SearchService>();
    services.AddSingleton<SyncCoordinator>();
    var sp = services.BuildServiceProvider();

    sp.GetRequiredService<MigrationRunner>().EnsureSchema();
    var coord = sp.GetRequiredService<SyncCoordinator>();
    var summary = await coord.SyncAllAsync(CancellationToken.None);
    Console.WriteLine($"Sync: {summary.FormatText()}");

    var counts = await sp.GetRequiredService<IItemRepository>().GetCountsByTypeAsync(CancellationToken.None);
    Console.WriteLine("Counts: " + string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}")));

    var search = sp.GetRequiredService<SearchService>();
    foreach (var q in new[] { "zotero", "github" })
    {
        var r = await search.SearchAsync(q, new SearchFilter { MaxResults = 5 }, CancellationToken.None);
        var first = r.Items.FirstOrDefault();
        Console.WriteLine($"  q='{q}' total={r.Total} first={first?.Title ?? "<none>"}");
        foreach (var item in r.Items.Take(3))
            Console.WriteLine($"    [{item.Type}] {item.Title}  url={item.Uri}  tags=[{string.Join(",", item.Tags)}]");
    }
    Console.WriteLine("DONE");
}

// ===== 查询已有 DB（不写） =====
static async Task QueryModeAsync(string dbPath, string keyword)
{
    if (!File.Exists(dbPath)) { Console.WriteLine($"DB not found: {dbPath}"); return; }
    Console.WriteLine($"DB: {dbPath} ({new FileInfo(dbPath).Length} bytes)");
    var walPath = dbPath + "-wal";
    if (File.Exists(walPath)) Console.WriteLine($"WAL: {walPath} ({new FileInfo(walPath).Length} bytes)");

    var (sp, _) = BuildServices(dbPath);
    await PrintStateAsync(sp);
}

// ===== 公共：DI 容器构建 =====
static (IServiceProvider, IServiceProvider) BuildServices(string dbPath)
{
    var services = new ServiceCollection();
    services.AddSingleton(new DbConnectionFactory(dbPath));
    services.AddSingleton(sp => new MigrationRunner(sp.GetRequiredService<DbConnectionFactory>()));
    services.AddSingleton<IItemRepository, ItemRepository>();
    services.AddSingleton<EverythingQueryQueue>();
    services.AddSingleton<EverythingSource>();
    services.AddSingleton<IItemSource>(sp => sp.GetRequiredService<EverythingSource>());
    services.AddSingleton<SearchService>();
    services.AddSingleton<SyncCoordinator>();
    return (services.BuildServiceProvider(), services.BuildServiceProvider());
}

// ===== Sync 模式：用 Mock 验证 SyncCoordinator 全链路 =====
static async Task SyncModeAsync()
{
    var dbPath = Path.Combine(Path.GetTempPath(), "starmark_sync.db");
    foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
        if (File.Exists(p)) File.Delete(p);
    Console.WriteLine($"DB: {dbPath}");

    var services = new ServiceCollection();
    services.AddSingleton(new DbConnectionFactory(dbPath));
    services.AddSingleton(sp => new MigrationRunner(sp.GetRequiredService<DbConnectionFactory>()));
    services.AddSingleton<IItemRepository, ItemRepository>();
    services.AddSingleton<MockGitHubSource>();
    services.AddSingleton<IItemSource>(sp => sp.GetRequiredService<MockGitHubSource>());
    services.AddSingleton<SearchService>();
    services.AddSingleton<SyncCoordinator>();
    var sp = services.BuildServiceProvider();

    sp.GetRequiredService<MigrationRunner>().EnsureSchema();
    Console.WriteLine("Schema: ok");

    // 用 mock GitHub source 同步
    var coord = sp.GetRequiredService<SyncCoordinator>();
    Console.WriteLine("Triggering sync...");
    var summary = await coord.SyncAllAsync(CancellationToken.None);
    Console.WriteLine($"Sync: {summary.FormatText()}");
    foreach (var s in summary.Sources)
    {
        var status = s.Success ? "OK" : "FAIL";
        Console.WriteLine($"  [{status}] {s.DisplayName}: {s.PulledCount} 条{(s.Error != null ? " err=" + s.Error : "")}");
    }

    // 验证同步后 DB 状态
    await PrintStateAsync(sp);

    // 验证同步进来的 repo 可被搜索
    var search = sp.GetRequiredService<SearchService>();
    foreach (var q in new[] { "testrepo", "rust", "csharp" })
    {
        var r = await search.SearchAsync(q, new SearchFilter { MaxResults = 10 }, CancellationToken.None);
        var first = r.Items.FirstOrDefault();
        Console.WriteLine($"  q='{q}' total={r.Total} first={first?.Title ?? "<none>"}");
    }

    Console.WriteLine("DONE");
}

// ===== 公共：打印状态 =====
static async Task PrintStateAsync(IServiceProvider sp)
{
    var repo = sp.GetRequiredService<IItemRepository>();
    var counts = await repo.GetCountsByTypeAsync(CancellationToken.None);
    Console.WriteLine("Counts: " + string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}")));

    var tags = await repo.GetAllTagsAsync(CancellationToken.None);
    Console.WriteLine("Tags: " + string.Join(", ", tags.Select(t => $"{t.Name}({t.Count})")));

    var search = sp.GetRequiredService<SearchService>();
    foreach (var q in new[] { "rag", "ollama", "ai", "技术文档" })
    {
        var r = await search.SearchAsync(q, new SearchFilter { MaxResults = 10 }, CancellationToken.None);
        var first = r.Items.FirstOrDefault();
        Console.WriteLine($"  q='{q}' total={r.Total} elapsed={r.ElapsedMs}ms first={first?.Title ?? "<none>"}");
        foreach (var item in r.Items.Take(3))
            Console.WriteLine($"    [{item.Type}] {item.Title}  tags=[{string.Join(",", item.Tags)}]");
    }
}

// ===== 公共：种子写入 =====
static async Task SeedAsync(IItemRepository repo)
{
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var items = new List<Item>
    {
        new()
        {
            Type = ItemType.GitHubStar,
            Source = ItemSources.GitHub,
            SourceId = "repo-ragflow",
            Title = "RAGFlow - RAG 引擎",
            Subtitle = "infiniflow/ragflow",
            Uri = "https://github.com/infiniflow/ragflow",
            Description = "基于深度文档理解的 RAG 引擎。",
            StarsCount = 30000,
            CreatedAt = now,
            UpdatedAt = now,
            Tags = new() { "rag", "ai", "llm" },
        },
        new()
        {
            Type = ItemType.GitHubStar,
            Source = ItemSources.GitHub,
            SourceId = "repo-ollama",
            Title = "Ollama - 本地 LLM 运行时",
            Subtitle = "ollama/ollama",
            Uri = "https://github.com/ollama/ollama",
            Description = "在本地运行 Llama 3, Mistral 等大模型。",
            StarsCount = 100000,
            CreatedAt = now,
            UpdatedAt = now,
            Tags = new() { "llm", "ai", "local" },
        },
        new()
        {
            Type = ItemType.Bookmark,
            Source = ItemSources.Chrome,
            SourceId = "bm-docs",
            Title = "StarMark 浏览器扩展文档",
            Subtitle = "github.com",
            Uri = "https://github.com/anthropics/anthropic-cookbook",
            Description = "Anthropic API 使用手册。",
            CreatedAt = now,
            UpdatedAt = now,
            Tags = new() { "ai", "docs" },
        },
        new()
        {
            Type = ItemType.File,
            Source = ItemSources.FileSystem,
            SourceId = "file-test-1",
            Title = "技术文档.md",
            Subtitle = @"D:\Visual Studio Code\Something\StarMarkDesktop\docs",
            Uri = "file:///D:/test.md",
            Description = "StarMark 桌面端项目开发技术文档。",
            FileSize = 15000,
            CreatedAt = now,
            UpdatedAt = now,
        },
    };
    await repo.UpsertAsync(items, CancellationToken.None);
    if (items[0].Id > 0) await repo.AddTagAsync(items[0].Id, "favorite", CancellationToken.None);
}

// ===== 桌面组件存储与吸附无头验证 =====
// 检查点：默认空启用 / 启用集合与窗口配置持久化 / 待办·随记·入口规范化 / v1 迁移 / 损坏容错 / 吸附
static void WidgetsCheck()
{
    var path = Path.Combine(Path.GetTempPath(), $"starmark_widgets_{Guid.NewGuid():N}.json");
    try
    {
        var store = new StarMark.Core.Widgets.WidgetStorage(path);

        // 1. 缺失文件 → 全新安装默认：v3、无实例、默认尺寸正确
        var d = store.Load();
        var defaultCfg = store.GetConfig(d, StarMark.Core.Widgets.WidgetKind.QuickLaunch, 0);
        Check(d.Version == 3 && d.Instances.Count == 0 && defaultCfg.Width == 320, "缺失文件回退默认配置");

        // 2. v2 文件 → v3 迁移：Enabled + WindowConfigs + 全局 Todos/Notes/Links 归并为实例
        // （storage 中枚举序列化为数字：QuickLaunch=0 / Todo=1 / Clock=3，无 JsonStringEnumConverter）
        File.WriteAllText(path, """
            {"Version":2,"Enabled":[3,0,1],
             "WindowConfigs":{"Clock":{"X":300,"Y":200,"Width":260,"Height":180,"Topmost":true},
                              "Todo":{"X":10,"Y":10,"Width":300,"Height":400}},
             "Todos":[{"Id":1,"Text":"写周报","Done":false,"CreatedAt":100},
                      {"Id":2,"Text":"已完成的","Done":true,"CreatedAt":200}],
             "Links":[{"Id":9,"Title":"GitHub","Uri":"https://github.com","CreatedAt":300}],
             "Notes":[]}
            """);
        var m = store.Load();
        var clock = m.Instances.FirstOrDefault(x => x.Kind == StarMark.Core.Widgets.WidgetKind.Clock);
        var ql = m.Instances.FirstOrDefault(x => x.Kind == StarMark.Core.Widgets.WidgetKind.QuickLaunch);
        var todo = m.Instances.FirstOrDefault(x => x.Kind == StarMark.Core.Widgets.WidgetKind.Todo);
        Check(m.Version == 3 && m.Instances.Count == 3, "v2 迁移为 3 个实例");
        Check(clock is { X: 300, Topmost: true }, "窗口位置与置顶迁移");
        Check(todo is { Todos.Count: 2 } && todo.Todos[0].Text == "写周报", "待办内容归并到 Todo 实例");
        Check(ql is { Links.Count: 1 } && ql.Links[0].Title == "GitHub", "快捷入口归并到 QuickLaunch 实例");
        Check(m.Enabled == null && m.WindowConfigs == null && m.Todos == null, "v2 遗留字段迁移后清空");

        // 3. 未完成待办排前 + 空文本剔除 + 往返持久化
        todo!.Todos.Add(new StarMark.Core.Widgets.TodoItem { Text = "   " });
        store.Save(m);
        var r1 = store.Load();
        var todo2 = r1.Instances.FirstOrDefault(x => x.Kind == StarMark.Core.Widgets.WidgetKind.Todo);
        Check(todo2 is { Todos.Count: 2 } && todo2.Todos[0].Text == "写周报" && !todo2.Todos[0].Done, "未完成待办排前且空文本被剔除");

        // 4. 布局方案持久化（v3）
        r1.Layouts = new List<StarMark.Core.Widgets.WidgetLayout> { new() { Id = "L1", Name = "工作台" } };
        r1.DefaultLayoutId = "L1";
        store.Save(r1);
        var r2 = store.Load();
        Check(r2.DefaultLayoutId == "L1" && r2.Layouts is { Count: 1 } && r2.Layouts[0].Name == "工作台", "布局方案持久化");

        // 5. 损坏 JSON → 回退默认且不抛异常
        File.WriteAllText(path, "{corrupted!!!");
        var r3 = store.Load();
        Check(r3.Version == 3 && r3.Instances.Count == 0 && store.GetConfig(r3, StarMark.Core.Widgets.WidgetKind.QuickLaunch, 0).Width == 320, "损坏文件回退默认值");


        // 7. 吸附（DeskBox WidgetSnapCalculator 语义）
        var work = new Windows.Graphics.RectInt32(0, 0, 1920, 1040);
        var target = new Windows.Graphics.RectInt32(300, 100, 200, 300);
        // 目标右缘 500 → 贴合位 508；候选左缘 514（偏差 6）
        var snapped = StarMark.Core.Widgets.WidgetSnapCalculator.SnapMove(
            new Windows.Graphics.RectInt32(514, 120, 200, 200), new[] { target }, work, 8, 24);
        Check(snapped.X == 508, "边缘贴合吸附");
        // 偏差 52 > 阈值 24 → 不吸附
        var far = StarMark.Core.Widgets.WidgetSnapCalculator.SnapMove(
            new Windows.Graphics.RectInt32(560, 120, 200, 200), new[] { target }, work, 8, 24);
        Check(far.X == 560, "阈值外不吸附");
        // 屏幕边缘：计算器零间隙吸附，留白由 InsetWorkArea 提供
        var edge = StarMark.Core.Widgets.WidgetSnapCalculator.SnapMove(
            new Windows.Graphics.RectInt32(4, 100, 200, 200),
            System.Array.Empty<Windows.Graphics.RectInt32>(),
            StarMark.Core.Widgets.WidgetSnapCalculator.InsetWorkArea(work, 8), 8, 24);
        Check(edge.X == 8, "屏幕边缘留白吸附");

        Console.WriteLine("Widgets: ok");
    }
    finally
    {
        try { File.Delete(path); } catch { }
    }

    static void Check(bool cond, string name)
    {
        if (!cond) throw new InvalidOperationException($"WidgetsCheck FAIL: {name}");
        Console.WriteLine($"  ok - {name}");
    }
}
// ===== 真实截屏自检（capture）=====
// GDI 抓屏与 PNG 编码没法在 xUnit 里跑（要真桌面），按可行性分析 §三-3 的约定走冒烟：
// 截一帧 → 裁一小块 → 编 PNG → 再解码回来对尺寸与 alpha。任何一步不对都抛非零退出码。
static async Task CaptureCheckAsync()
{
    // 与 StarMark.UI 的 app.manifest 同一档 DPI 感知：不设的话系统会把坐标虚拟化，
    // 冒烟看到的尺寸就不再等价于应用里的真实尺寸
    // 冒烟必须按应用本体的 DPI 形态跑（app.manifest 里是 PerMonitorV2），
    // 否则这里看到的桌面尺寸与真机不是一回事，测出来的"通过"不作数
    var dpiAware = CaptureSmokeNative.SetProcessDpiAwarenessContext(CaptureSmokeNative.PerMonitorV2);
    Console.WriteLine($"DpiAwareness PMv2 set={dpiAware}");

    var started = Stopwatch.GetTimestamp();
    var result = StarMark.Integrations.Capture.GdiScreenCapture.CaptureVirtualScreen();
    var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    Console.WriteLine($"Capture ok={result.Ok} elapsed={elapsedMs:F0}ms err={result.Error ?? "-"}");
    if (!result.Ok || result.Frame is not { } frame) throw new Exception("截屏失败：" + (result.Error ?? "无帧"));
    Console.WriteLine($"Frame bounds={frame.Bounds.X},{frame.Bounds.Y} {frame.Bounds.Width}x{frame.Bounds.Height} bytes={frame.Bgra.Length}");
    if (frame.Bgra.Length != (long)frame.Width * frame.Height * 4) throw new Exception("像素缓冲长度与宽高不符");

    var (distinct, brightest) = SurveyPixels(frame.Bgra);
    Console.WriteLine($"Pixels distinct={distinct} brightest={brightest}");
    if (brightest == 0) throw new Exception("整帧全黑——多半是独占全屏应用或桌面合成被关");
    if (distinct < 2) throw new Exception("整帧只有一个颜色值——像素读数可疑（先怀疑位深或行序）");

    // 中心 200×120 一块，走完整的"判定 → 偏移 → 裁剪 → 编码 → 解码"链
    var selection = new StarMark.Abstractions.Capture.IntRect(
        frame.Bounds.X + frame.Width / 2 - 100, frame.Bounds.Y + frame.Height / 2 - 60, 200, 120);
    if (StarMark.Core.Capture.CaptureGeometry.CropProblem(selection, frame.Bounds) is { } problem)
        throw new Exception("裁剪判定拒绝：" + problem);
    var (offsetX, offsetY) = StarMark.Core.Capture.CaptureGeometry.CropOffset(selection, frame.Bounds);
    if (offsetX < 0 || offsetY < 0) throw new Exception($"裁剪偏移为负（{offsetX},{offsetY}）——原点折算错了");

    var crop = StarMark.Integrations.Capture.GdiScreenCapture.Crop(
        new StarMark.Integrations.Capture.FrameCopyRequest(frame, offsetX, offsetY, selection.Width, selection.Height));
    if (crop.Length != selection.Width * selection.Height * 4) throw new Exception("裁剪缓冲尺寸不符");

    var path = Path.Combine(Path.GetTempPath(), $"starmark-smoke-{Guid.NewGuid():N}.png");
    try
    {
        if (!await StarMark.Integrations.Capture.GdiScreenCapture.SavePngAsync(path, crop, selection.Width, selection.Height))
            throw new Exception("PNG 落盘失败");
        var info = await ProbePngAsync(path);
        Console.WriteLine($"Png {path} decoded={info.Width}x{info.Height} alpha={info.Alpha} sizeOnDisk={new FileInfo(path).Length}");
        if (info.Width != selection.Width || info.Height != selection.Height) throw new Exception("PNG 解码尺寸与选区不符");
        // alpha 必须是 255：GDI 不写 alpha，没做"补不透明"这一步的话存出来是一张全透明图
        if (info.Alpha != 255) throw new Exception($"PNG alpha={info.Alpha}，不是 255——存出来会是透明图");
    }
    finally { try { File.Delete(path); } catch { } }
    Console.WriteLine("DONE");
}

/// <summary>粗采样整帧：不同颜色个数与最亮通道值（够判"全黑 / 只有一个值"，不为精确）。</summary>
static (int Distinct, int Brightest) SurveyPixels(byte[] bgra)
{
    var seen = new HashSet<uint>();
    var brightest = 0;
    for (var i = 0; i + 3 < bgra.Length; i += 4 * 997)
    {
        var packed = (uint)(bgra[i] | (bgra[i + 1] << 8) | (bgra[i + 2] << 16));
        seen.Add(packed);
        brightest = Math.Max(brightest, Math.Max(bgra[i], Math.Max(bgra[i + 1], bgra[i + 2])));
        if (seen.Count > 4096) break;
    }
    return (seen.Count, brightest);
}

static async Task<(int Width, int Height, int Alpha)> ProbePngAsync(string path)
{
    using var file = File.OpenRead(path);
    using var stream = file.AsRandomAccessStream();
    var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
    // 用无参重载：拿"解码器自己认定的格式"，不拿我们期望的格式去问它——
    // 传格式参数就会把不匹配的期望当成事实读，正是这类自检最容易被糊过去的地方
    using var software = await decoder.GetSoftwareBitmapAsync();
    var data = (await decoder.GetPixelDataAsync()).DetachPixelData();
    Console.WriteLine($"Png format={software.BitmapPixelFormat}/{software.BitmapAlphaMode} bytes={data.Length}");
    return (software.PixelWidth, software.PixelHeight, data.Length > 3 ? data[3] : -1);
}

// ===== OCR 能力探针（ocr）=====
// Windows.Media.Ocr 在这台机器上到底能不能用、给的是哪种语言、
// 以及**它把一行中文切成什么样的词**——这些都不是读码能定的（语言包按机器装、切词按引擎版本变），
// 而"把词拼回一段可复制文字"的规则必须照着它实际给的形状写，不然就是凭想象。
static async Task OcrCheckAsync()
{
    CaptureSmokeNative.SetProcessDpiAwarenessContext(CaptureSmokeNative.PerMonitorV2);
    var result = StarMark.Integrations.Capture.GdiScreenCapture.CaptureVirtualScreen();
    if (!result.Ok || result.Frame is not { } frame) throw new Exception("截屏失败：" + (result.Error ?? "无帧"));
    Console.WriteLine($"Frame {frame.Width}x{frame.Height}");

    Console.WriteLine("AvailableLanguages: " + string.Join(", ",
        Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag)));
    var supported = Windows.Media.Ocr.OcrEngine.IsLanguageSupported(new Windows.Globalization.Language("zh-Hans-CN"));
    Console.WriteLine($"IsLanguageSupported(zh-Hans-CN)={supported}");

    var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
    Console.WriteLine($"TryCreateFromUserProfileLanguages => {(engine is null ? "null（系统没有可用的 OCR 语言包）" : engine.RecognizerLanguage?.LanguageTag ?? "?")}");
    if (engine is null)
    {
        Console.WriteLine("SKIP-NO-ENGINE");
        return;
    }

    // 整帧：机器上刚好拍到什么不可预测，中央一块往往是壁纸（引擎会从纯图形里"读"出噪声，本次探针就见到了）
    var w = frame.Width;
    var h = frame.Height;
    var crop = StarMark.Integrations.Capture.GdiScreenCapture.Crop(
        new StarMark.Integrations.Capture.FrameCopyRequest(frame, 0, 0, w, h));
    // 用 DataWriter 而不是 Buffer + AsStream：后者要 System.Runtime.InteropServices.WindowsRuntime
    // 那个拓展方法，而本文件是 top-level program，using 只能写在所有语句之前，不如直接用 WinRT 自己的写法。
    var writer = new Windows.Storage.Streams.DataWriter();
    writer.WriteBytes(crop);
    var native = writer.DetachBuffer();
    var bitmap = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
        native, Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, w, h, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);

    var started = Stopwatch.GetTimestamp();
    var ocr = await engine.RecognizeAsync(bitmap);
    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    // 结果写 UTF-8 文件而不是 stdout：控制台是 cp936，中文在那里只会看见乱码，
    // 而本探针要判的恰恰是"引擎把中文切成了什么"
    var report = Path.Combine(Path.GetTempPath(), "starmark-ocr-smoke.txt");
    var lines = new List<string>
    {
        $"Recognize {w}x{h} elapsed={elapsed:F0}ms lines={ocr.Lines.Count} words={ocr.Lines.Sum(l => l.Words.Count)}",
        $"engineLang={engine.RecognizerLanguage?.LanguageTag ?? "-"}",
    };
    foreach (var line in ocr.Lines)
    {
        var words = line.Words.Select(word => word.Text).ToList();
        lines.Add($"[{string.Join("|", words)}]  Text=<{line.Text}>  spaceJoined=<{string.Join(" ", words)}>  textIsSpaceJoin={line.Text == string.Join(" ", words)}");
    }
    // 再走一遍产品真正用的那条链（Integrations 的封装 + Core 的拼回），
    // 而不是只验引擎本身：探针能认出词、产品却拼不出可读文字，也是常见的分岔
    var outcome = await StarMark.Integrations.Ocr.ScreenOcrReader.RecognizeAsync(crop, w, h);
    var assembled = StarMark.Core.Ocr.OcrText.Assemble(outcome.Lines);
    lines.Add($"Reader ok={outcome.Ok} engine={outcome.EngineLanguage ?? "-"} err={outcome.Error ?? "-"} "
              + $"assembledChars={StarMark.Core.Ocr.OcrText.CountMeaningful(assembled)} lines={outcome.Lines.Count}");
    lines.Add("ASSEMBLED <" + assembled.Replace(((char)10).ToString(), " | ") + ">");
    if (!outcome.Ok) throw new Exception("ScreenOcrReader 没能识别：" + (outcome.Error ?? "无原因"));
    if (outcome.Lines.Count > 0 && assembled.Length == 0) throw new Exception("引擎给了行，拼回来却是空的（拼接判据错了）");
    File.WriteAllLines(report, lines, new System.Text.UTF8Encoding(false));
    Console.WriteLine($"Recognize {w}x{h} elapsed={elapsed:F0}ms lines={ocr.Lines.Count} report={report}");
    Console.WriteLine("DONE");
}

/// <summary>冒烟进程要自己声明 DPI 感知，否则与 PerMonitorV2 的应用本体看到的桌面尺寸不是一回事。</summary>
internal static class CaptureSmokeNative{
    public static readonly IntPtr PerMonitorV2 = new(-4);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
