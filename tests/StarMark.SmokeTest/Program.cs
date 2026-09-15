#nullable enable
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
