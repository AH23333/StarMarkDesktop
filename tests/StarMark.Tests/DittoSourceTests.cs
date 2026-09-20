#nullable enable
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;
using StarMark.Abstractions;
using StarMark.Data;
using StarMark.Integrations.Ditto;

namespace StarMark.Tests;

/// <summary>
/// Ditto 剪贴板源集成测试：合成 DittoDB（对照源码 schema）验证
/// 文本 / 文件（CF_HDROP）/ 分组过滤 / 实时检索 / URI 构建。
/// </summary>
public sealed class DittoSourceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"starmark-ditto-test-{Guid.NewGuid():N}.db");

    public DittoSourceTests() => CreateSyntheticDb();

    public void Dispose()
    {
        for (int i = 0; i < 5; i++)
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); return; }
            catch { Thread.Sleep(100); }
        }
    }

    [Fact]
    public async Task FetchAsync_ReturnsOnlyNonGroupClips()
    {
        var source = new DittoSource(_dbPath);
        Assert.True(source.IsAvailable);

        var items = await source.FetchAsync(new SyncContext(), CancellationToken.None);

        // ditto:1(0) / ditto:2(0) / ditto:4(NULL) / ditto:5(0) 为非分组，ditto:3(1) 为分组被排除
        Assert.Equal(4, items.Count);
        Assert.DoesNotContain(items, i => i.SourceId == "ditto:3");
        Assert.Contains(items, i => i.SourceId == "ditto:4"); // bIsGroup IS NULL 的行必须召回（S13-1 回归）

        var file = Assert.Single(items, i => i.SourceId == "ditto:2");
        Assert.Equal("file:///C:/Data/demo/读我.txt", file.Uri);
        Assert.Equal(ItemType.Clipboard, file.Type);
    }

    [Fact]
    public async Task Map_TextClip_PutsFullBodyInDescriptionNotJustTitle()
    {
        // 回归 AJ-2：多行正文的第 2 行关键词必须在 Description（可入库索引字段），而非仅落在被忽略的 SearchText。
        var source = new DittoSource(_dbPath);
        var items = await source.FetchAsync(new SyncContext(), CancellationToken.None);

        var multi = Assert.Single(items, i => i.SourceId == "ditto:5");
        Assert.Equal("剪贴板标题行", multi.Title);                       // Title 仅首行
        Assert.NotNull(multi.Description);
        Assert.Contains("ZEBRA", multi.Description);                     // 正文整段落 Description（含第 2 行）
    }

    [Fact]
    public async Task Persist_TextClip_BodyOnLaterLine_IsFtsSearchable()
    {
        // 端到端钉死：把 Ditto 条目经 ItemRepository.UpsertAsync 落库后，用 SQLite/FTS 搜第 2 行的关键词应命中。
        // 旧实现 Description=null → UpsertOne 重算 search_text 只含首行 Title → 搜 "ZEBRA" 得 0（缺陷）。
        var dbPath = Path.Combine(Path.GetTempPath(), $"starmark-ditto-persist-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new DbConnectionFactory(dbPath);
            new MigrationRunner(factory).EnsureSchema();
            var repo = new ItemRepository(factory);

            var source = new DittoSource(_dbPath);
            var items = await source.FetchAsync(new SyncContext(), CancellationToken.None);
            await repo.UpsertAsync(items, CancellationToken.None);

            var hits = await repo.SearchAsync("ZEBRA", new SearchFilter { MaxResults = 10 }, CancellationToken.None);
            Assert.Contains(hits.Items, i => i.SourceId == "ditto:5");
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task FetchAsync_TextClipTitleMatchesUnicodeText()
    {
        var source = new DittoSource(_dbPath);
        var items = await source.FetchAsync(new SyncContext(), CancellationToken.None);

        var text = Assert.Single(items, i => i.SourceId == "ditto:1");
        Assert.Equal("Hello StarMark 剪贴板集成测试", text.Title);
    }

    [Fact]
    public async Task SearchAsync_MatchesClipboardText()
    {
        var source = new DittoSource(_dbPath);
        var hits = await source.SearchAsync("StarMark", new SearchFilter { MaxResults = 10 }, CancellationToken.None);

        Assert.Contains(hits, h => h.SourceId == "ditto:1");
    }

    [Fact]
    public void IsAvailable_False_WhenDbMissing()
    {
        var source = new DittoSource(Path.Combine(Path.GetTempPath(), "nope-ditto.db"));
        Assert.False(source.IsAvailable);
    }

    private void CreateSyntheticDb()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
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
        InsertMain(1, now, "Hello StarMark 剪贴板集成测试", 0);
        InsertMain(2, now - 10, "C:\\Data\\demo\\读我.txt", 0);
        InsertMain(3, now - 20, "分组: 常用", 1);
        // Ditto 对普通（非分组）剪贴板常把 bIsGroup 留 NULL——回归 S13-1：这类行不得被 SQL 三值逻辑漏掉
        InsertMainNullGroup(4, now - 30, "NULL 分组的历史剪贴板");
        // 多行正文（关键词在第二行）——回归 AJ-2：正文须进入 FTS 索引（经 Description），首行外的词才可搜。
        InsertMain(5, now - 40, "剪贴板标题行\n第二行包含机密词 ZEBRA", 0);

        InsertData(1, "CF_UNICODETEXT", Encoding.Unicode.GetBytes("Hello StarMark 剪贴板集成测试"));
        InsertData(2, "CF_HDROP", BuildDropFiles("C:\\Data\\demo\\读我.txt"));
        InsertData(4, "CF_UNICODETEXT", Encoding.Unicode.GetBytes("NULL 分组的历史剪贴板"));
        InsertData(5, "CF_UNICODETEXT", Encoding.Unicode.GetBytes("剪贴板标题行\n第二行包含机密词 ZEBRA"));
    }

    private void InsertMain(long id, long date, string text, int isGroup)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Main (lID, lDate, mText, lShortCut, lDontAutoDelete, CRC, bIsGroup, lParentID, QuickPasteText,
                              clipOrder, clipGroupOrder, globalShortCut, lastPasteDate)
            VALUES ($id, $date, $text, 0, 0, 0, $isGroup, 0, '', 0, 0, 0, $date)
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$date", date);
        cmd.Parameters.AddWithValue("$text", text);
        cmd.Parameters.AddWithValue("$isGroup", isGroup);
        cmd.ExecuteNonQuery();
    }

    private void InsertMainNullGroup(long id, long date, string text)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Main (lID, lDate, mText, bIsGroup)
            VALUES ($id, $date, $text, NULL)
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$date", date);
        cmd.Parameters.AddWithValue("$text", text);
        cmd.ExecuteNonQuery();
    }

    private void InsertData(long parentId, string format, byte[] blob)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Data (lParentID, strClipBoardFormat, ooData) VALUES ($p, $f, $b)";
        cmd.Parameters.AddWithValue("$p", parentId);
        cmd.Parameters.AddWithValue("$f", format);
        cmd.Parameters.AddWithValue("$b", blob);
        cmd.ExecuteNonQuery();
    }

    private static byte[] BuildDropFiles(string path)
    {
        var body = Encoding.Unicode.GetBytes(path + "\0");
        var buffer = new byte[20 + body.Length + 2];
        Array.Copy(body, 0, buffer, 20, body.Length);
        BitConverter.GetBytes(20u).CopyTo(buffer, 0);
        BitConverter.GetBytes(1u).CopyTo(buffer, 16);
        return buffer;
    }
}