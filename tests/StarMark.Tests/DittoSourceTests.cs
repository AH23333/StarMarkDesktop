#nullable enable
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;
using StarMark.Abstractions;
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

        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, i => i.SourceId == "ditto:3");

        var file = Assert.Single(items, i => i.SourceId == "ditto:2");
        Assert.Equal("file:///C:/Data/demo/读我.txt", file.Uri);
        Assert.Equal(ItemType.Clipboard, file.Type);
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

        InsertData(1, "CF_UNICODETEXT", Encoding.Unicode.GetBytes("Hello StarMark 剪贴板集成测试"));
        InsertData(2, "CF_HDROP", BuildDropFiles("C:\\Data\\demo\\读我.txt"));
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