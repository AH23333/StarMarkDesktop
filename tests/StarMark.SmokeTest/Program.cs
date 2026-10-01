#nullable enable
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Abstractions.Clipboard;
using StarMark.Core.Backup;
using StarMark.Core.Search;
using StarMark.Core.Sync;
using StarMark.Data;
using StarMark.Integrations.Everything;
using StarMark.SmokeTest.Mocks;
using IntRect = StarMark.Abstractions.Capture.IntRect;
using PixelPoint = StarMark.Abstractions.Capture.PixelPoint;
using CaptureGeometry = StarMark.Core.Capture.CaptureGeometry;
using AnnotationPainter = StarMark.Core.Capture.AnnotationPainter;
using Mark = StarMark.Core.Capture.Annotation;
using Tool = StarMark.Core.Capture.AnnotationTool;
using GdiScreenCapture = StarMark.Integrations.Capture.GdiScreenCapture;
using FrameCopyRequest = StarMark.Integrations.Capture.FrameCopyRequest;

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
if (args.Length >= 1 && args[0] == "annotate")
{
    await AnnotateCheckAsync();
    return;
}

if (args.Length >= 1 && args[0] == "ocr")
{
    await OcrCheckAsync();
    return;
}

if (args.Length >= 1 && args[0] == "clipimg")
{
    // clipimg            -> 用这台机器上真实的图片历史跑一遍"导出 + 换机恢复"
    // clipimg <N>        -> 再灌 N 条合成历史，量出"几百上千张"这一档的耗时与体积
    // 两种模式都只在 %TEMP% 里写，用户的库与 clip 目录一个字节都不动（详见函数注释）。
    var n = args.Length >= 2 && int.TryParse(args[1], out var parsed) ? parsed : 0;
    await ClipBackupAuditAsync(n);
    return;
}
if (args.Length >= 1 && args[0] == "searchsort")
{
    // searchsort          -> 用合成库量"搜索两条腿"各自的耗时（P-33 的代价，不靠猜）
    // searchsort <最大规模> -> 把最大那一档换成指定行数（默认 30000，逐档翻倍到它）
    // 只在 %TEMP% 里建临时库，用户的库一个字节都不动。
    await SearchSortAuditAsync(args.Length >= 2 && int.TryParse(args[1], out var rows) ? rows : 30_000);
    return;
}
if (args.Length >= 1 && args[0] == "aiusage")
{
    // aiusage            -> 读这台机器真库的 ai_usage 表，把 §19.6 那四条"什么时候重开 O3"的阈值逐条报数（默认 30 天窗口）
    // aiusage <天>       -> 换窗口长度；<天> 之后再跟一个路径就是指定库
    // 只读取证：连 -wal/-shm 一起复制到 %TEMP% 再打开，用户的库一个字节都不动（同 clipimg 那档的做法）。
    // 空表/没这张表都不算失败——"没有账"与"读不到账"分开说，见函数注释。
    await AiUsageAuditAsync(args);
    return;
}

if (args.Length >= 1 && args[0] == "srcprobe")
{
    // srcprobe      -> 量"这台机器上 Ditto 那笔探测税是多少毫秒"（P-135 的前后数字；默认 5 轮）
    // srcprobe <N>  -> 换轮数
    // 只读：全程只碰文件系统与进程表，不写任何库、不碰用户档。输出刻意用 ASCII——这台机器的控制台码页会把中文打成乱码。
    SrcProbeAudit(args.Length >= 2 && int.TryParse(args[1], out var rounds) && rounds > 0 ? rounds : 5);
    return;
}

await SmokeModeAsync();

// ===== 构造期探测税（P-135）=====
// 把"没装 Ditto 的机器上白付的那笔税"拆成两段量出来：枚举进程（CandidateDbPaths）与逐条探盘（FindDbPath）。
// 批次 SU 之前这两段都在构造里跑，而 DI 在主窗构造时就把五颗源全建出来；改后构造 ≈0 ms，
// 代价挪到"第一次问可用性"那一次。第二次问必须是 0 毫秒且不涨 ProbeCount——那是"只探一次"的证据。
static void SrcProbeAudit(int rounds)
{
    for (var i = 0; i < rounds; i++)
    {
        var ctorSw = Stopwatch.StartNew();
        var source = new StarMark.Integrations.Ditto.DittoSource();
        ctorSw.Stop();
        var ctorProbes = source.ProbeCount;

        var firstSw = Stopwatch.StartNew();
        var available = source.IsAvailable;
        firstSw.Stop();

        var againSw = Stopwatch.StartNew();
        _ = source.IsAvailable;
        againSw.Stop();

        // 旧构造体里干的就是这两件事，分开量才知道钱在"枚举进程"还是"探盘"上。
        var enumSw = Stopwatch.StartNew();
        var candidates = StarMark.Integrations.Ditto.DittoSource.CandidateDbPaths();
        enumSw.Stop();
        var findSw = Stopwatch.StartNew();
        var chosen = StarMark.Integrations.Ditto.DittoSource.FindDbPath(candidates);
        findSw.Stop();

        Console.WriteLine($"round {i}: ctor={ctorSw.Elapsed.TotalMilliseconds:F3}ms ctorProbes={ctorProbes} "
                          + $"firstAsk={firstSw.Elapsed.TotalMilliseconds:F3}ms secondAsk={againSw.Elapsed.TotalMilliseconds:F3}ms "
                          + $"probes={source.ProbeCount} | enumPaths={enumSw.Elapsed.TotalMilliseconds:F3}ms "
                          + $"findDb={findSw.Elapsed.TotalMilliseconds:F3}ms candidates={candidates.Count} "
                          + $"available={available} chosen={chosen}");
    }
}

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

// ===== AI 用量取证档（批次 R5）：把 §19.6 那四条"什么时候重开 O3"变成一条命令能复跑的数 =====
/// <summary>
/// 读一台机器上的 <c>ai_usage</c> 账本，按 <c>docs/组件功能扩展实现详解.md</c> §19.6 的四条重开阈值逐条报数。
/// <para><b>只读</b>：真库连 <c>-wal</c>／<c>-shm</c> 一起复制到 %TEMP% 再打开，用户的库一个字节都不动——
/// <see cref="DbConnectionFactory.Open"/> 每次都要跑一遍 <c>PRAGMA journal_mode=WAL</c>，
/// 那是对文件的写，直接拿真库路径就等于"取证工具改了被取证的东西"。</para>
/// <para><b>"没有账"与"读不到账"是两句话</b>：表空是正常状态（这台机器还没真跑过一轮 AI 整理），
/// 表不存在／库打不开／没读权限才是失败，两者都要说清是哪一种，且都不许以异常栈收尾。</para>
/// </summary>
static async Task AiUsageAuditAsync(string[] args)
{
    // §19.6 的四条阈值（那张表是唯一出处，代码不是——改了文档这里要跟着改，两边都指向同一节）。
    const long InputPerRoundThreshold = 20_000;   // 单轮 sum(input_tokens) > 20K
    const int EstimatedRatioPercent = 50;          // 一轮里 estimated=1 的行占比 > 50%
    const long EstimatedTotalThreshold = 10_000;   //   且 sum > 10K（两条同时成立才算）
    const int UntaggedThreshold = 2_000;           // 未打标条目数 > 2000（⇒ 25 批以上）

    Console.WriteLine("=== AI 用量取证（§19.6 重开阈值的出口，批次 R5）===");
    var days = 30;
    string? dbArg = null;
    if (args.Length >= 2 && int.TryParse(args[1], out var parsedDays)) days = parsedDays;
    else if (args.Length >= 2) dbArg = args[1];
    if (args.Length >= 3) dbArg = args[2];
    if (days is < 1 or > 3650)
    {
        Console.WriteLine($"⚠ 窗口 {days} 天不在 1–3650 之间，按 30 天报（坏值回默认，不夹到边上）。");
        days = 30;
    }

    var realDb = dbArg ?? DbConnectionFactory.DefaultDbPath();
    Console.WriteLine($"库：{realDb}");
    if (!File.Exists(realDb))
    {
        Console.WriteLine($"✗ 这个路径上没有库文件（{realDb}）。");
        Console.WriteLine("  要么这台机器还没用过 StarMark，要么库在别处——把路径作为第二个参数再跑一次：");
        Console.WriteLine("    dotnet run --project tests/StarMark.SmokeTest -- aiusage 30 <starmark.db 的完整路径>");
        return;
    }

    var work = Path.Combine(Path.GetTempPath(), $"starmark-aiusage-{Guid.NewGuid():N}");
    Directory.CreateDirectory(work);
    var copy = Path.Combine(work, "starmark-copy.db");
    try
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(realDb + suffix)) File.Copy(realDb + suffix, copy + suffix, true);
        Console.WriteLine($"读法：副本 {copy}（{new FileInfo(copy).Length:N0} 字节），真库只被复制、没被打开写入。");

        var factory = new DbConnectionFactory(copy);
        using (var probe = factory.Open())
        {
            using var cmd = probe.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ai_usage';";
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            {
                Console.WriteLine("✗ 这份库里没有 ai_usage 表（schema 早于 §20.1 的计量表）。");
                Console.WriteLine("  这不是「没花过 token」，是「这台机器的库还没有账本这一张表」——两码事。");
                return;
            }
        }

        var usage = new AiUsageRepository(factory);
        var maxAt = await usage.MaxRecordedAtAsync();
        Console.WriteLine(maxAt is null
            ? "账本状态：ai_usage 存在但一行都没有 ⇒ 这台机器还没真跑过一轮 AI 整理（不是失败，下面四条全按「没有数」报）。"
            : $"账本状态：最后一条账 {DateTimeOffset.FromUnixTimeSeconds(maxAt.Value).LocalDateTime:yyyy-MM-dd HH:mm:ss}"
              + $"（窗口锚在这条往回 {days} 天，同 §20.5 的口径——不看墙钟）");

        var from = maxAt is null ? 0L : maxAt.Value - (long)days * 86_400;
        var totals = await usage.TotalsSinceAsync(from);
        var byFeature = await usage.ByFeatureSinceAsync(from);
        var byModel = await usage.ByModelSinceAsync(from);

        Console.WriteLine();
        Console.WriteLine($"窗口内合计：{totals.Calls} 次调用，input {totals.InputTokens:N0} / output {totals.OutputTokens:N0}"
            + $"，合计 {totals.TotalTokens:N0} token；其中估算 {totals.EstimatedCalls} 次"
            + (totals.Calls > 0 ? $"（{100.0 * totals.EstimatedCalls / totals.Calls:F1}%）" : "（占比没法算：没有行）"));
        Console.WriteLine("按功能：" + (byFeature.Count == 0 ? "（窗口内没有行）"
            : string.Join("；", byFeature.Select(f => $"{(string.IsNullOrWhiteSpace(f.Name) ? "(空)" : f.Name)} {f.Calls} 次 / {f.TotalTokens:N0} token{(f.EstimatedOnly ? "，全是估算" : "")}"))));
        Console.WriteLine("按模型：" + (byModel.Count == 0 ? "（窗口内没有行）"
            : string.Join("；", byModel.Select(m => $"{(string.IsNullOrWhiteSpace(m.Name) ? "(没记模型名)" : m.Name)} {m.Calls} 次 / {m.TotalTokens:N0} token"))));
        var classify = byFeature.FirstOrDefault(f => f.Name == "classify");
        var classifyInput = totals.InputTokens;   // 见下面第 1 条的说明：没有轮次 id，只能给窗口合计这个上界

        Console.WriteLine();
        Console.WriteLine("--- §19.6 四条重开阈值，逐条 ---");
        Console.WriteLine($"1) 单轮 sum(input_tokens) > {InputPerRoundThreshold:N0}"
            + $"　⇒ 现在：窗口内合计 {classifyInput:N0}（classify 一档 {classify?.Calls ?? 0} 次调用）"
            + $"　判定：{(classifyInput > InputPerRoundThreshold ? "越线，重开 §19.6" : "没越线")}");
        Console.WriteLine("    口径边界：ai_usage 没有「轮」这个字段，所以这条只能按窗口合计给上界——"
            + "上界都没到就一定没到；到了才需要按 at 时间戳分段复核（那是改表的事，本档不改）。");
        var ratio = totals.Calls > 0 ? 100.0 * totals.EstimatedCalls / totals.Calls : 0.0;
        var hit2 = ratio > EstimatedRatioPercent && totals.TotalTokens > EstimatedTotalThreshold;
        Console.WriteLine($"2) estimated=1 的行占比 > {EstimatedRatioPercent}% 且 sum > {EstimatedTotalThreshold:N0}"
            + $"　⇒ 现在：{ratio:F1}% 且 {totals.TotalTokens:N0} token　判定：{(hit2 ? "越线，先修计量再谈优化" : "没越线")}");

        var repo = new ItemRepository(factory);
        var cap = UntaggedThreshold + 1;      // 只数到"比阈值多一条"就停：阈值问的是"过没过"，不是精确总数
        var untagged = await repo.GetUntaggedAsync(StarMark.Core.Ai.ClassifyRules.InScope, cap, CancellationToken.None);
        Console.WriteLine($"3) 未打标条目数 > {UntaggedThreshold}"
            + $"　⇒ 现在：{(untagged.Count >= cap ? $"≥{cap}（到上限就停，没继续数）" : untagged.Count.ToString())} 条"
            + $"（类型范围读 ClassifyRules.InScope，与批量整理选候选同一份）　判定：{(untagged.Count > UntaggedThreshold ? "越线，重开 §19.6" : "没越线")}");
        Console.WriteLine($"4) 一轮产出的新标签数 > 80　⇒ 本档测不到：ai_usage 只有 token 列，不记每轮新标签数。");
        Console.WriteLine("    要测这条得让 ClassifyRunner 把 newTags 落一行账——那是改代码，不属于取证工具，登记见 R5 文档。");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ 读这份库没成功：{ex.Message}");
        Console.WriteLine("  这是「读不到账」，不是「没有账」。常见原因是权限或文件被占用；换一条路径或关掉正在跑的实例再试。");
    }
    finally
    {
        // 必须先清连接池再删：Microsoft.Data.Sqlite 默认按连接串池化连接，连接即便 Dispose 掉，
        // 池里的实体仍占着副本文件的句柄 ⇒ "删掉临时副本"这一步会稳定失败（AiUsageTests.cs:149 同一手法）。
        // 本工具的纪律是**不给用户留要手动删的东西**，所以这里失败也只报告位置，不写"请手动清理"。
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(work, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"（临时副本还留在 {work}——{ex.Message}；下一次跑会另建一份，不影响读数。）");
        }
    }
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
    // 通知也不在这里验：批次 RV 起托盘不再代发通知。系统在 Windows 11 上对那一发返回 TRUE 而屏幕上
    // 什么都不显示，所以"冒烟打印 ok"从来不是证据；提醒改由 UI 侧那张右下角提示卡承担，
    // 判据是窗口的实际矩形（冒烟程序没有 UI 线程与窗口，验不了，也不该假装验了）。
    Console.WriteLine("Notification exit owned by NoticeCard in StarMark.UI (not covered by this smoke check)");
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
// 这里要量的不是"引擎能不能跑"，而是**认错字这件事改善了没有**。而"认错"必须有标准答案可言：
// 当场拍到的屏幕没人知道正确文本，多认出来的字既可能是修好也可能是幻觉。
// 所以探针自己用 GDI 把已知文字按桌面字号画成图——与产品截到的完全同类
// （同一个字体渲染器、同一套抗锯齿、alpha 同样是 0 的 BGRA、同样的内存位图）。
// 然后同一条链跑两遍：原样交引擎＝改善前，走 Core.Ocr.OcrPipeline＝改善后。
static async Task OcrCheckAsync()
{
    CaptureSmokeNative.SetProcessDpiAwarenessContext(CaptureSmokeNative.PerMonitorV2);
    var report = Path.Combine(Path.GetTempPath(), "starmark-ocr-smoke.txt");
    var lines = new List<string>();

    lines.Add("AvailableLanguages: " + string.Join(", ",
        Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag)));
    lines.Add("IsLanguageSupported(zh-Hans-CN)="
        + Windows.Media.Ocr.OcrEngine.IsLanguageSupported(new Windows.Globalization.Language("zh-Hans-CN")));
    var profileEngine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
    lines.Add($"TryCreateFromUserProfileLanguages => {(profileEngine is null ? "null（系统没有可用的 OCR 语言包）" : profileEngine.RecognizerLanguage?.LanguageTag ?? "?")}");
    if (profileEngine is null)
    {
        // 没有引擎就没得量：直说，别让下面的表格以 0% 冒充"识别很差"
        lines.Add("SKIP-NO-ENGINE");
        File.WriteAllLines(report, lines, new System.Text.UTF8Encoding(false));
        Console.WriteLine("SKIP-NO-ENGINE report=" + report);
        return;
    }

    // ────────── 一、有标准答案的对照 ──────────
    // 挑的五条覆盖桌面上最常见的四种形状：纯中文菜单、中英数混排、带全角括号的版本号、纯快捷键。
    // 字号取 14/18 物理像素：14 是 Win11 正文的下限，18 是常见放大/高分屏下的实际高度。
    var texts = new[]
    {
        "设置与时间和语言选项",
        "今日天气晴 24 摄氏度",
        "StarMark 已复制到剪贴板 12 项",
        "版本 1.4.11（2026 年发布）",
        "按住 Ctrl 加 Alt 加 Space 呼出主界面",
    };
    lines.Add(string.Empty);
    lines.Add("=== 改善前（原样交引擎） vs 改善后（OcrPipeline：放大 + 对比拉伸 + 弱结果换语言） ===");
    lines.Add("字号  基线%   产品%   基线字 产品字  放大  换语言  产品实际认出");
    double baseSum = 0, prodSum = 0;
    var counted = 0;
    var better = 0;
    var worse = 0;
    foreach (var px in new[] { 14, 18 })
    {
        foreach (var truthText in texts)
        {
            var (bgra, w, h) = OcrSmokeNative.RenderTextStrip(truthText, px);
            var snapshot = (byte[])bgra.Clone();

            var raw = await StarMark.Integrations.Ocr.ScreenOcrReader.RecognizeAsync(bgra, w, h);
            if (!raw.Ok) throw new Exception("基线识别失败：" + raw.Error);
            var rawText = StarMark.Core.Ocr.OcrText.Assemble(raw.Lines);

            var read = await StarMark.Core.Ocr.OcrPipeline.ReadAsync(bgra, w, h);
            if (!read.Ok) throw new Exception("产品链识别失败：" + read.Error);
            // 产品的预处理绝不能改到调用方给的缓冲：贴图窗交出来的就是它正在显示的那份像素，
            // 就地转灰度＝"点一下识字，贴图变灰图"。这条断言量的就是那个反直觉的边界。
            if (!bgra.AsSpan().SequenceEqual(snapshot))
                throw new Exception("OcrPipeline 就地改了传入的像素（预处理必须自己复制）");

            var score0 = Accuracy(truthText, rawText);
            var score1 = Accuracy(truthText, read.Text);
            baseSum += score0;
            prodSum += score1;
            counted++;
            if (score1 > score0) better++;
            else if (score1 < score0) worse++;
            lines.Add($"{px,3}px {score0,6:F1} {score1,7:F1} {StarMark.Core.Ocr.OcrText.CountMeaningful(rawText),6} {read.Chars,5} {read.Upscale,4}× {(read.SwappedLanguage ? " 是" : " 否"),4}  "
                + "<" + StarMark.Core.Ocr.OcrText.Preview(read.Text, 26) + ">");
        }
    }
    lines.Add($"合计：基线 {baseSum / counted:F1}% → 产品 {prodSum / counted:F1}%（变好 {better} 例、变差 {worse} 例、共 {counted} 例）");
    Console.WriteLine($"Accuracy baseline={baseSum / counted:F1}% product={prodSum / counted:F1}% cases={counted}");

    // ────────── 二、真屏整帧（无标准答案，只看链路跑不跑得通） ──────────
    // 这一段量的是"拍到真实桌面时那条链还成不成立"：基线与产品各认一遍，只比计数与耗时。
    lines.Add(string.Empty);
    lines.Add("=== 真屏整帧（当前桌面拍到什么算什么，无标准答案） ===");
    var captured = StarMark.Integrations.Capture.GdiScreenCapture.CaptureVirtualScreen();
    if (!captured.Ok || captured.Frame is not { } frame)
    {
        lines.Add("截屏失败：" + (captured.Error ?? "没有帧"));
    }
    else
    {
        var fw = frame.Width;
        var fh = frame.Height;
        var crop = StarMark.Integrations.Capture.GdiScreenCapture.Crop(
            new StarMark.Integrations.Capture.FrameCopyRequest(frame, 0, 0, fw, fh));
        var rawFrame = await StarMark.Integrations.Ocr.ScreenOcrReader.RecognizeAsync(crop, fw, fh);
        if (!rawFrame.Ok) throw new Exception("ScreenOcrReader 没能识别：" + (rawFrame.Error ?? "无原因"));
        var rawAssembled = StarMark.Core.Ocr.OcrText.Assemble(rawFrame.Lines);
        if (rawFrame.Lines.Count > 0 && rawAssembled.Length == 0)
            throw new Exception("引擎给了行，拼回来却是空的（拼接判据错了）");
        var started = Stopwatch.GetTimestamp();
        var readFrame = await StarMark.Core.Ocr.OcrPipeline.ReadAsync(crop, fw, fh);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        lines.Add($"Frame {fw}x{fh} 基线 lines={rawFrame.Lines.Count} chars={StarMark.Core.Ocr.OcrText.CountMeaningful(rawAssembled)} "
            + $"→ 产品 chars={readFrame.Chars} how={readFrame.How} 产品链耗时={elapsed:F0}ms");
        // 刻意不把屏幕上的文字抄进临时文件：整屏 OCR 出来的内容里可能有密码、令牌或私人信息，
        // 而这一段要证的只是"真屏那条链跑得通、耗时多少"，计数就够了。
        // （MX 那次量到的"引擎用空格切词"形状已记进审查报告，不必再往磁盘上留一份明文。）
    }

    File.WriteAllLines(report, lines, new System.Text.UTF8Encoding(false));
    Console.WriteLine("report=" + report);
    Console.WriteLine("DONE");
}

/// <summary>
/// 对着标准答案算准确率：两边的空白一律去掉（引擎按词给、拼接规则另说，这里只问"字对不对"），
/// 再按编辑距离折算。<b>可能超过 100% 的多认不算惩罚</b>：幻觉会让分母外的字符变多，
/// 所以同时把字数打印出来，别只留一个百分比。
/// </summary>
static double Accuracy(string truth, string got)
{
    var a = new string(truth.Where(c => !char.IsWhiteSpace(c)).ToArray());
    var b = new string(got.Where(c => !char.IsWhiteSpace(c)).ToArray());
    if (a.Length == 0) return b.Length == 0 ? 100d : 0d;
    var distance = EditDistance(a, b);
    return Math.Max(0d, 100d * (1d - (double)distance / a.Length));
}

static int EditDistance(string a, string b)
{
    var prev = new int[b.Length + 1];
    var cur = new int[b.Length + 1];
    for (var j = 0; j <= b.Length; j++) prev[j] = j;
    for (var i = 1; i <= a.Length; i++)
    {
        cur[0] = i;
        for (var j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
        }
        (prev, cur) = (cur, prev);
    }
    return prev[b.Length];
}


// ===== 标注合成探针（annotate）=====
// 量的是"画完标注之后真正会交出去的那张图"这一整条链：真屏裁一块 → AnnotationPainter 合成 →
// PNG 落盘 → 再解码读回来验像素。BGRA 步长、alpha、编码器、贴图那份像素，哪一环错了这里都会红，
// 而且最后留下一张可以用眼睛看的图——标注这种东西"读码自洽"不算证据。
static async Task AnnotateCheckAsync()
{
    CaptureSmokeNative.SetProcessDpiAwarenessContext(CaptureSmokeNative.PerMonitorV2);
    var captured = StarMark.Integrations.Capture.GdiScreenCapture.CaptureVirtualScreen();
    if (!captured.Ok || captured.Frame is not { } frame) throw new Exception("截屏失败：" + (captured.Error ?? "无帧"));

    var width = Math.Min(700, frame.Bounds.Width - 120);
    var height = Math.Min(430, frame.Bounds.Height - 120);
    var box = new IntRect(frame.Bounds.X + 60, frame.Bounds.Y + 60, width, height);
    var (ox, oy) = CaptureGeometry.CropOffset(box, frame.Bounds);
    var basePixels = GdiScreenCapture.Crop(new FrameCopyRequest(frame, ox, oy, box.Width, box.Height));

    var red = Mark.Opaque(0x23, 0x11, 0xE8);
    var blue = Mark.Opaque(0xD4, 0x78, 0x00);
    PixelPoint P(int x, int y) => new(x, y);
    var marks = new[]
    {
        new Mark(Tool.Rectangle, new[] { P(20, 20), P(220, 120) }, red, 4),
        new Mark(Tool.Ellipse, new[] { P(260, 20), P(430, 140) }, blue, 6),
        new Mark(Tool.Arrow, new[] { P(30, 320), P(210, 180) }, red, 4),
        new Mark(Tool.Line, new[] { P(250, 320), P(520, 320) }, blue, 8),
        new Mark(Tool.Pen, new[] { P(300, 200), P(330, 240), P(360, 190), P(400, 250), P(440, 195), P(480, 245) }, red, 3),
        new Mark(Tool.Highlighter, new[] { P(250, 70), P(520, 70) }, red, 22),
        new Mark(Tool.Mosaic, new[] { P(540, 330), P(600, 350), P(660, 380) }, red, 36),
        new Mark(Tool.Text, new[] { P(250, 350) }, blue, 4) { Text = "这是标注文字 1.4.11", FontHeight = 30 },
    };

    var composed = AnnotationPainter.Render(basePixels, box.Width, box.Height, marks);
    var changed = 0;
    for (var i = 0; i < composed.Length; i += 4)
        if (composed[i] != basePixels[i] || composed[i + 1] != basePixels[i + 1] || composed[i + 2] != basePixels[i + 2])
            changed++;
    for (var p = 3; p < composed.Length; p += 4)
        if (composed[p] != 255) throw new Exception($"合成结果第 {p / 4} 个像素 alpha={composed[p]}，交出去会变成透明洞");

    // 没被任何标注扫到的地方必须逐字节不变：这一条挡的是"合成顺手把别处也改脏了"
    AssertSame(basePixels, composed, box.Width, 2, 2, "左上角");
    AssertSame(basePixels, composed, box.Width, box.Width - 3, 2, "右上角");
    // 边线中点必须是那支笔的颜色（顺带验 BGRA 位序：红蓝写反这里就对不上）
    AssertColor(composed, box.Width, 20, 70, red, "矩形左边线");
    AssertColor(composed, box.Width, 345, 20, blue, "椭圆上边线");

    var path = Path.Combine(Path.GetTempPath(), "starmark-annotate.png");
    if (!await GdiScreenCapture.SavePngAsync(path, composed, box.Width, box.Height))
        throw new Exception("标注后的画面没能写成 PNG");
    var (pngWidth, pngHeight, pngAlpha) = await ProbePngAsync(path);
    if (pngWidth != box.Width || pngHeight != box.Height)
        throw new Exception($"PNG 尺寸对不上：{pngWidth}×{pngHeight} vs {box.Width}×{box.Height}");
    if (pngAlpha != 255) throw new Exception("存出来的 PNG 第一像素是半透明的");

    Console.WriteLine($"Annotate {box.Width}x{box.Height} marks={marks.Length} changedPixels={changed} "
        + $"png={pngWidth}x{pngHeight} file={path}");
    Console.WriteLine("DONE");
}

static void AssertSame(byte[] a, byte[] b, int width, int x, int y, string what)
{
    var p = (y * width + x) * 4;
    if (a[p] != b[p] || a[p + 1] != b[p + 1] || a[p + 2] != b[p + 2])
        throw new Exception($"{what}（{x},{y}）本该逐字节不变，却从 {a[p]},{a[p + 1]},{a[p + 2]} 变成 {b[p]},{b[p + 1]},{b[p + 2]}");
}

static void AssertColor(byte[] pixels, int width, int x, int y, int bgra, string what)
{
    var p = (y * width + x) * 4;
    if (pixels[p] != (byte)bgra || pixels[p + 1] != (byte)(bgra >> 8) || pixels[p + 2] != (byte)(bgra >> 16))
        throw new Exception($"{what}（{x},{y}）期望 {bgra & 0xFF},{bgra >> 8 & 0xFF},{bgra >> 16 & 0xFF}，"
            + $"实际 {pixels[p]},{pixels[p + 1]},{pixels[p + 2]}");
}

/// <summary>

/// <summary>
/// ClipIMG-P3 的<b>取证</b>：把收官文档 §6 与 ⑥ 里几条"只能真机量"的说法变成数字——
/// 包体积、导出耗时、解回耗时、逐张字节是否一致，以及 3c 修的那个洞所在的场景
/// （<b>新机器上 clip 目录整个不存在</b>时能不能写回）。
/// <para>三条硬性安全线，动这条链之前先看清：</para>
/// <list type="number">
/// <item><description>库走 <c>%TEMP%</c> 里的<b>副本</b>：导出虽是只读，但连"在真库上开连接"这一步都不做。</description></item>
/// <item><description><c>widgetsTargetPath</c> 必须给临时路径——传 null 会走 <c>WidgetStorage.DefaultPath()</c>，
/// 那就是拿一份旧备份去覆盖用户<b>真机上的组件数据</b>（待办与随记不可重建，等于造成损失）。</description></item>
/// <item><description>用户的 clip 目录<b>只读</b>：合成模式另建临时目录并把 <c>ClipAssets.FolderOverride</c> 指过去，
/// 一个写操作都不落在真目录。</description></item>
/// </list>
/// </summary>
static async Task SearchSortAuditAsync(int maxSize)
{
    // P-33 的代价要有数，不能只说"会慢一点"。修完之后非默认排序那条腿不再让相关度先吃掉名额，
    // 代价是它要把全部 FTS 命中 join + 分组一遍。这里在 %TEMP% 造合成库逐档量，
    // 报的是"同一句关键词、同一页大小，相关度腿 vs 其它腿"的毫秒差。
    // 用户机上的库到底多大没人知道，所以逐档报（2000 起、每次 ×5，直到 maxSize）。
    Console.WriteLine("== 搜索两条腿耗时取证（合成库，只写 %TEMP%，用户的库一个字节都不动）==");
    const int pageSize = 200;                     // 与搜索页一页的量级对齐（MaxResults）
    const string keyword = "rag";

    for (var size = 2_000; ; size *= 5)
    {
        var rows = Math.Min(size, maxSize);
        var path = Path.Combine(Path.GetTempPath(), $"starmark-searchsort-{rows}-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new DbConnectionFactory(path);
            new MigrationRunner(factory).EnsureSchema();
            var repo = new ItemRepository(factory);

            var swSeed = Stopwatch.StartNew();
            await SeedSearchCorpusAsync(repo, rows);
            swSeed.Stop();
            Console.WriteLine($"  {rows} 条全部命中 \"{keyword}\"，一页取 {pageSize}（灌库 {swSeed.ElapsedMilliseconds} ms）");

            foreach (var (label, sort) in new[] { ("相关度腿", "relevance"), ("名字序腿", "name"), ("最近腿", "recent") })
            {
                var samples = new List<long>();
                int returned = 0, total = 0;
                for (var round = 0; round < 5; round++)
                {
                    var sw = Stopwatch.StartNew();
                    var res = await repo.SearchAsync(keyword,
                        new SearchFilter { Sort = sort, MaxResults = pageSize }, default);
                    sw.Stop();
                    samples.Add(sw.ElapsedMilliseconds);
                    returned = res.Items.Count;
                    total = res.Total;
                }
                samples.Sort();
                Console.WriteLine($"    {label}：返回 {returned} 条／FTS 命中 {total}，"
                    + $"中位 {samples[samples.Count / 2]} ms（最快 {samples[0]}／最慢 {samples[^1]}）");
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + suffix); } catch { /* 临时库删不掉不影响读数 */ }
        }

        if (rows >= maxSize) break;
    }
}

/// <summary>
/// 合成语料：词频、字母序、时间序三者<b>刻意互相错开</b>。
/// 这样"相关度前排的那批"与"名字最靠前/最新的那批"不是同一批，两条腿的工作量才真的不同
/// ——否则量出来的差值只反映了一个碰巧同序的夹具。
/// </summary>
static async Task SeedSearchCorpusAsync(ItemRepository repo, int rows)
{
    const long BaseTime = 1_700_000_000L;
    var batch = new List<Item>(2_000);
    for (var i = 0; i < rows; i++)
    {
        var repeats = 1 + i % 7;
        batch.Add(new Item
        {
            Type = ItemType.Bookmark,
            Source = "synth",
            SourceId = $"s{i}",
            Title = $"doc{i:X5} " + string.Join(' ', Enumerable.Repeat("rag", repeats)),
            UpdatedAt = BaseTime + (rows - i),
            StarsCount = i % 500,
        });
        if (batch.Count >= 2_000) { await repo.UpsertAsync(batch, default); batch.Clear(); }
    }
    if (batch.Count > 0) await repo.UpsertAsync(batch, default);
}

static async Task ClipBackupAuditAsync(int synthRows)
{
    var work = Path.Combine(Path.GetTempPath(), $"starmark-clipimg-{Guid.NewGuid():N}");
    Directory.CreateDirectory(work);
    var realClip = ClipAssets.Folder;
    var realDb = DbConnectionFactory.DefaultDbPath();
    Console.WriteLine("== ClipIMG 备份链取证（全部落在 %TEMP%，用户的一个字节都不动）==");
    Console.WriteLine($"  工作目录：{work}");
    Console.WriteLine($"  真实 clip 目录（只读）：{realClip}");
    if (!File.Exists(realDb)) { Console.WriteLine($"  [跳过] 找不到库文件：{realDb}"); return; }

    // ---- 1. 真库的副本 + 真目录的占用画像 ----
    foreach (var suffix in new[] { "", "-wal", "-shm" })
        if (File.Exists(realDb + suffix)) File.Copy(realDb + suffix, Path.Combine(work, "starmark-copy.db" + suffix), true);
    var copyFactory = new DbConnectionFactory(Path.Combine(work, "starmark-copy.db"));
    ClipAssets.FolderOverride = null;                       // 导出侧读真目录
    var realFiles = Directory.Exists(realClip)
        ? new DirectoryInfo(realClip).EnumerateFiles().Select(f => (f.Name, f.Length)).ToList()
        : new List<(string Name, long Bytes)>();
    var fp = ClipAssets.Summarize(realFiles, out var realTemp);
    Console.WriteLine($"  真实目录画像：{ClipAssets.DescribeUsage(fp, realTemp)}");

    var srcBackup = new BackupService(new BackupRepository(copyFactory));
    var env0 = await srcBackup.ExportAsync();
    var imageRows = env0.Payload.Items.Count(i =>
        i.Source == ItemSources.Clipboard && ClipboardEntry.IsImageOf(i.ExtraJson));
    Console.WriteLine($"  库里的图片历史：{imageRows} 条（条目总数 {env0.Payload.Items.Count}）");

    // ---- 2. 导出：两份的建议名都故意写 .json，看"带图"那一份会不会自己换成 .zip ----
    var sw = Stopwatch.StartNew();
    var export = await srcBackup.ExportWithClipImagesAsync(Path.Combine(work, "out.json"));
    sw.Stop();
    var exportMs = sw.ElapsedMilliseconds;
    var pkgBytes = File.Exists(export.Path) ? new FileInfo(export.Path).Length : 0;
    var plainPath = await srcBackup.ExportToFileAsync(Path.Combine(work, "plain.json"));
    var plainBytes = new FileInfo(plainPath).Length;
    Console.WriteLine($"  导出：请求 out.json → 实际写出 {Path.GetFileName(export.Path)}（{exportMs} ms，"
        + $"包体积 {pkgBytes / 1024.0 / 1024.0:0.##} MB）");
    Console.WriteLine($"        只带条目那份：{Path.GetFileName(plainPath)}（{plainBytes / 1024.0:0.#} KB）"
        + $" ⇒ 附件净增 {(pkgBytes - plainBytes) / 1024.0 / 1024.0:0.##} MB");
    Console.WriteLine($"        条目 {export.ItemCount} · 带上 {export.ClipImages} 张（{export.ClipImageBytes / 1024.0 / 1024.0:0.##} MB）"
        + $" · 本就没文件 {export.MissingImages} · 没进包 {export.FailedImages}");

    // ---- 3. 换机恢复：目标 clip 目录刻意不存在，widgets/快照都改道到 %TEMP% ----
    var newMachine = Path.Combine(work, "clip-new-machine");
    BackupService.SnapshotDirectoryOverride = Path.Combine(work, "snapshots");
    ClipAssets.FolderOverride = newMachine;
    var freshFactory = new DbConnectionFactory(Path.Combine(work, "fresh.db"));
    new MigrationRunner(freshFactory).EnsureSchema();
    var env = await BackupService.ReadAsync(export.Path);
    sw.Restart();
    var rr = await new BackupService(new BackupRepository(freshFactory)).RestoreAsync(
        env, RestoreMode.Merge, Path.Combine(work, "widgets.json"), CancellationToken.None, export.Path);
    sw.Stop();
    Console.WriteLine($"  恢复：耗时 {sw.ElapsedMilliseconds} ms，结果 {(rr.Success ? "成功" : "失败：" + rr.Message)}");
    Console.WriteLine($"        写回 {rr.ClipImagesRestored} · 跳过 {rr.ClipImagesSkipped} · 没能写入 {rr.ClipImagesFailed}");
    Console.WriteLine($"        新机器上 clip 目录被自动建出来：{Directory.Exists(newMachine)}；"
        + $"落盘文件 {(Directory.Exists(newMachine) ? Directory.GetFiles(newMachine).Length : 0)} 个");
    Console.WriteLine($"        状态栏那句话：{rr.Message}");

    // ---- 4. 逐张验字节：包里的每一条与写回的那份必须逐字节相同（store 不改字节） ----
    int compared = 0, differs = 0, missing = 0;
    if (rr.Success && BackupContainer.IsContainer(export.Path))
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(export.Path);
        foreach (var e in zip.Entries)
        {
            if (!e.FullName.StartsWith(BackupContainer.ClipPrefix, StringComparison.Ordinal)) continue;
            var name = e.FullName[BackupContainer.ClipPrefix.Length..];
            var onDisk = ClipAssets.FullPathOf(name);
            if (onDisk is null || !File.Exists(onDisk)) { missing++; continue; }
            using var es = e.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            if (!ms.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(onDisk))) differs++;
            compared++;
        }
        Console.WriteLine($"  逐张比对：{compared} 张全等（不一致 {differs}、没落盘 {missing}）"
            + $"；store 的直接证据：压缩后字节＝原始字节 {(zipEntriesStore(export.Path) ? "成立" : "不成立")}");
    }

    // ---- 5. 合成放大：把"几百上千张"这一档也量出来（只写 %TEMP%） ----
    if (synthRows > 0)
    {
        var synthClip = Path.Combine(work, "clip-synth");
        Directory.CreateDirectory(synthClip);
        ClipAssets.FolderOverride = synthClip;
        var bigFactory = new DbConnectionFactory(Path.Combine(work, "big.db"));
        new MigrationRunner(bigFactory).EnsureSchema();
        var bigRepo = new ItemRepository(bigFactory);
        var seeds = realFiles.Where(f => f.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
        var filler = new byte[200 * 1024];                 // 真目录空得没法取材时的替身（备份不看内容）
        sw.Restart();
        for (var i = 0; i < synthRows; i++)
        {
            var when = new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.FromHours(8)).AddMinutes(i);
            var sid = ClipboardPolicy.BuildImageSourceId(Encoding.UTF8.GetBytes($"synth-{i}"));
            var main = ClipAssets.MainNameOf(sid, when);
            var thumb = ClipAssets.ThumbNameOf(sid, when);
            byte[] mainBytes = seeds.Count > 0
                ? File.ReadAllBytes(Path.Combine(realClip, seeds[i % seeds.Count].Name))
                : filler;
            File.WriteAllBytes(Path.Combine(synthClip, main), mainBytes);
            File.WriteAllText(Path.Combine(synthClip, thumb), "thumb");
            await bigRepo.RecordClipboardAsync(ClipboardEntry.BuildImage(
                sid, new ClipboardEntry.ImageMeta(main, thumb, 3840, 2160, mainBytes.Length), "SmokeAudit", when),
                CancellationToken.None, imageMaxEntries: synthRows + 64);
        }
        var seedMs = sw.ElapsedMilliseconds;
        sw.Stop();
        var bigBackup = new BackupService(new BackupRepository(bigFactory));
        var planned = await bigBackup.ExportAsync();
        var withImages = planned.Payload.Items.Count(i => ClipboardEntry.IsImageOf(i.ExtraJson));
        sw.Restart();
        var bigExport = await bigBackup.ExportWithClipImagesAsync(Path.Combine(work, "big.json"));
        var bigExportMs = sw.ElapsedMilliseconds;
        sw.Stop();
        var bigPkg = new FileInfo(bigExport.Path).Length;
        Console.WriteLine($"  [合成] 灌 {synthRows} 条（库里图片行 {withImages}）用时 {seedMs} ms，"
            + $"临时目录落盘 {Directory.GetFiles(synthClip).Length} 个文件、{Directory.GetFiles(synthClip).Sum(f => new FileInfo(f).Length) / 1024.0 / 1024.0:0.##} MB");
        Console.WriteLine($"        请求 big.json → 实际写出 {Path.GetFileName(bigExport.Path)}："
            + $"{bigExport.ClipImages} 张、{bigExport.ClipImageBytes / 1024.0 / 1024.0:0.##} MB，导出耗时 {bigExportMs} ms"
            + $"（本就没文件 {bigExport.MissingImages}、没进包 {bigExport.FailedImages}）");
        var env2 = await BackupService.ReadAsync(bigExport.Path);
        var newMachine2 = Path.Combine(work, "clip-new-machine-2");
        ClipAssets.FolderOverride = newMachine2;                 // 又一次"新机器"：目录整个不存在
        var fresh2 = new DbConnectionFactory(Path.Combine(work, "fresh2.db"));
        new MigrationRunner(fresh2).EnsureSchema();
        sw.Restart();
        var rr2 = await new BackupService(new BackupRepository(fresh2)).RestoreAsync(
            env2, RestoreMode.Merge, Path.Combine(work, "widgets2.json"), CancellationToken.None, bigExport.Path);
        var restoreMs = sw.ElapsedMilliseconds;
        sw.Stop();
        Console.WriteLine($"  [合成] 包体积 {bigPkg / 1024.0 / 1024.0:0.##} MB，平均单张 "
            + $"{ClipAssets.DescribeBytes(bigPkg / Math.Max(1, bigExport.ClipImages))}");
        Console.WriteLine($"  [合成] 解回 {rr2.ClipImagesRestored} 张（跳过 {rr2.ClipImagesSkipped}、失败 {rr2.ClipImagesFailed}）"
            + $"用时 {restoreMs} ms ⇒ {rr2.ClipImagesRestored * 1000.0 / Math.Max(1, restoreMs):0.#} 张/秒；"
            + $"目录自动建出：{Directory.Exists(newMachine2)}，落盘 {(Directory.Exists(newMachine2) ? Directory.GetFiles(newMachine2).Length : 0)} 个文件");
        Console.WriteLine($"  [合成] 这一份包里附件是否全为 store：{(zipEntriesStore(bigExport.Path) ? "成立" : "不成立")}；"
            + $"状态栏那句话：{rr2.Message}");

        // UI 线程上那句"这份还带 N 张图"的代价必须当场量：代码注释写的是"几毫秒"，
        // 3000 条目要是慢，就得跟着注释一起改（把数挪进 Task.Run），不能留着假话。
        sw.Restart();
        var peeked = BackupContainer.CountClipEntries(bigExport.Path);
        var peekMs = sw.ElapsedMilliseconds;
        sw.Stop();
        Console.WriteLine($"  [合成] UI 线程上数附件（确认框那句『带 N 张』）：{peeked} 张，用时 {peekMs} ms");
    }

    Console.WriteLine($"  结论只覆盖\"备份链本身\"：真换机、各家粘贴、以及\"图看着对不对\"仍要在真机上看。");
    Console.WriteLine($"  取证产物保留在：{work}");
}

/// <summary>包里所有附件都是 store（压缩后＝原始）吗——zip(store) 这条裁决的直接证据。</summary>
static bool zipEntriesStore(string zipPath)
{
    using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
    return zip.Entries.Where(e => e.FullName.StartsWith(BackupContainer.ClipPrefix, StringComparison.Ordinal))
        .All(e => e.CompressedLength == e.Length);
}

/// <summary>冒烟进程要自己声明 DPI 感知，否则与 PerMonitorV2 的应用本体看到的桌面尺寸不是一回事。</summary>
internal static class CaptureSmokeNative{
    public static readonly IntPtr PerMonitorV2 = new(-4);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}

/// 用 GDI 把一行已知文字画进内存位图，读出 BGRA——探针由此得到"知道正确答案的输入"。
/// <para>
/// 刻意走与产品同一条取像素的路（<c>GetDIBits</c> + 自上而下 + 先摘位图再读 + 手工写 BITMAPINFOHEADER），
/// 因为照抄产品验证过的写法才不会有第三种意外。
/// </para>
/// </summary>
internal static class OcrSmokeNative
{
    /// <summary>桌面常见的浅底深字（Win11 亮色窗体），留 6px 空白边距，与用户随手框选的形状一致。</summary>
    public static (byte[] Bgra, int Width, int Height) RenderTextStrip(string text, int pixelHeight, string face = "Microsoft YaHei UI")
    {
        const int Margin = 6;
        const int HeaderSize = 40;                       // BITMAPINFOHEADER 的 biSize 恒为 40
        const uint Back = 0x00F3F3F3;                    // COLORREF 是 0x00BBGGRR
        const uint Fore = 0x001A1A1A;

        var screen = GetDC(IntPtr.Zero);
        var dc = CreateCompatibleDC(screen);
        var logFont = new LogFont
        {
            Height = -pixelHeight,                       // 负值＝按 em 高（设备像素）给，不含内部留白
            Weight = 400,                                // FW_NORMAL
            CharSet = 1,                                 // DEFAULT_CHARSET：中文必须靠它挑到支持 CJK 的字面
            Quality = 5,                                 // CLEARTYPE_QUALITY，与真实桌面一致
            FaceName = face,
        };
        var font = CreateFontIndirectW(ref logFont);
        if (font == IntPtr.Zero)
            throw new Exception($"GDI 没能创建字体 {face}（这台机器上没有该字体，探针无法造标准答案）");
        IntPtr oldFont = IntPtr.Zero, oldBitmap = IntPtr.Zero, bitmap = IntPtr.Zero, info = IntPtr.Zero;
        try
        {
            oldFont = SelectObject(dc, font);
            if (!GetTextExtentPoint32W(dc, text, text.Length, out var extent))
                throw new Exception("量不出文字尺寸（GetTextExtentPoint32 失败）");
            var width = extent.cx + Margin * 2;
            var height = extent.cy + Margin * 2;

            bitmap = CreateCompatibleBitmap(screen, width, height);
            if (bitmap == IntPtr.Zero) throw new Exception("创建内存位图失败");
            oldBitmap = SelectObject(dc, bitmap);

            var brush = CreateSolidBrush(Back);
            var rect = new Rect { Left = 0, Top = 0, Right = width, Bottom = height };
            FillRect(dc, ref rect, brush);
            DeleteObject(brush);
            SetTextColor(dc, Fore);
            SetBkMode(dc, 1);                            // TRANSPARENT：底色已由 FillRect 铺好
            if (!TextOutW(dc, Margin, Margin, text, text.Length))
                throw new Exception("TextOutW 失败（Win32 " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + "）");

            info = System.Runtime.InteropServices.Marshal.AllocHGlobal(HeaderSize + 1024);
            for (var i = 0; i < HeaderSize; i++) System.Runtime.InteropServices.Marshal.WriteByte(info, i, 0);
            System.Runtime.InteropServices.Marshal.WriteInt32(info, 0, HeaderSize);
            System.Runtime.InteropServices.Marshal.WriteInt32(info, 4, width);
            System.Runtime.InteropServices.Marshal.WriteInt32(info, 8, -height);   // 负＝自上而下行序
            System.Runtime.InteropServices.Marshal.WriteInt16(info, 12, (short)1);
            System.Runtime.InteropServices.Marshal.WriteInt16(info, 14, (short)32);

            var pixels = new byte[width * height * 4];
            // 读像素前把位图从 DC 上摘下来：仍被选中时 GetDIBits 在部分驱动上返回 0 行且不带错误码
            SelectObject(dc, oldBitmap);
            oldBitmap = IntPtr.Zero;
            var got = GetDIBits(dc, bitmap, 0, (uint)height, pixels, info, 0);
            if (got == 0) throw new Exception("GetDIBits 返回 0 行，标准答案画不出来");
            // 与产品一致地把 alpha 转不透明：GDI 写的是 0，而引擎按 Bgra8/Premultiplied 收
            for (var p = 3; p < pixels.Length; p += 4) pixels[p] = 255;
            return (pixels, width, height);
        }
        finally
        {
            if (info != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(info);
            if (oldBitmap != IntPtr.Zero) SelectObject(dc, oldBitmap);
            if (oldFont != IntPtr.Zero) SelectObject(dc, oldFont);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            DeleteObject(font);
            DeleteDC(dc);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct LogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, CharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct TextExtent
    {
        public int cx;
        public int cy;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref Rect rect, IntPtr brush);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, uint color);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr CreateFontIndirectW(ref LogFont logFont);

    [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool GetTextExtentPoint32W(IntPtr hdc, string lpString, int count, out TextExtent size);

    [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool TextOutW(IntPtr hdc, int x, int y, string lpString, int count);

    [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hBitmap, uint start, uint lines,
        byte[] bits, IntPtr bmi, uint usage);
}

