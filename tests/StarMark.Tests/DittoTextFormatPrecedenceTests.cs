#nullable enable
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;
using StarMark.Abstractions.Clipboard;
using StarMark.Integrations.Ditto;

namespace StarMark.Tests;

/// <summary>
/// 一条 Ditto clip 同时带 <c>CF_UNICODETEXT</c> 与 <c>CF_TEXT</c> 时，读到的必须是**宽字符**那一份（P-18）。
/// <para>
/// 旧写法两条格式走同一个赋值分支、且循环里无条件后写覆盖，而那句 <c>SELECT … FROM Data</c> 没有
/// <c>ORDER BY</c> ⇒ "读到哪一份"其实由行序决定。这里把两种插入次序各造一遍，钉的是**我们的代码不再看行序**
/// （不宣称 Ditto 真会同时落两种格式——那是第三方数据的事实；行序裁决在我们这一侧，就该由我们定）。
/// </para>
/// <para>
/// 刻意自建一份小库，而不是往 <c>DittoSourceTests</c> 的共享夹具里加行：那条夹具的计数读数
/// （"非分组 4 条"）会被每一行新增牵动，一改就要连带重述全部读数（#160）。
/// 这里只需要 reader 实际用到的那几列。
/// </para>
/// </summary>
public sealed class DittoTextFormatPrecedenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"starmark-ditto-order-{Guid.NewGuid():N}.db");

    public DittoTextFormatPrecedenceTests()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Main(lID INTEGER PRIMARY KEY, lDate INTEGER, mText TEXT, bIsGroup INTEGER);
            CREATE TABLE Data(lID INTEGER PRIMARY KEY AUTOINCREMENT,
                              lParentID INTEGER, strClipBoardFormat TEXT, ooData BLOB);
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        for (var i = 0; i < 5; i++)
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); return; }
            catch { Thread.Sleep(100); }
        }
    }

    [Theory]
    [InlineData(true)]                      // 宽字符行先落库（rowid 小 ⇒ 扫描先读到它）
    [InlineData(false)]                     // 单字节行先落库
    public void ReadRecent_WideTextWins_RegardlessOfRowOrder(bool wideFirst)
    {
        InsertMain(10, "mText 首行");
        if (wideFirst)
        {
            InsertUnicode(10, "宽字符正文");
            InsertAnsi(10, "单字节正文");
        }
        else
        {
            InsertAnsi(10, "单字节正文");
            InsertUnicode(10, "宽字符正文");
        }

        var clip = Assert.Single(Read(10));

        // 两种次序必须给同一个答案，而且答案不是 Main 那句 mText（证明 Data 行确实被用上）。
        Assert.Equal("宽字符正文", clip.Text);
        Assert.Equal(ClipboardPayload.FormatUnicodeText, clip.Format);
    }

    [Fact]
    public void ReadRecent_AnsiOnlyClip_IsReadAndLabelledCfText()
    {
        // 只有 CF_TEXT 的那一条不许变成空。正文刻意只用 ASCII：内容层的确定断言（GBK 解出中文）
        // 在 ClipboardPayloadTests 里显式传码页做，这里不许依赖跑测试那台机器的 OEM 码页。
        InsertMain(11, "只有单字节");
        InsertAnsi(11, "CF-TEXT-ONLY");

        var clip = Assert.Single(Read(11));

        Assert.Equal("CF-TEXT-ONLY", clip.Text);
        Assert.Equal(ClipboardPayload.FormatAnsiText, clip.Format);
    }

    [Fact]
    public void ReadRecent_BlankWideRowDoesNotHideTheAnsiRow()
    {
        // 宽字符行是"只有空白"的脏数据 ⇒ 不许让它顶着 CF_UNICODETEXT 的名义把真正的正文盖掉，
        // 也不许把空白本身写进 clip.Text（那样标题那一格会变成一片空白）。
        // 正文用 ASCII：这条钉的是"空白不许吞正文"，内容层的中文解码在 ClipboardPayloadTests 显式传 936 那两条。
        InsertMain(12, "标题");
        InsertUnicode(12, "   ");
        InsertAnsi(12, "REAL-TEXT");

        var clip = Assert.Single(Read(12));

        Assert.Equal("REAL-TEXT", clip.Text);
        Assert.Equal(ClipboardPayload.FormatAnsiText, clip.Format);
    }

    [Fact]
    public void ReadRecent_NoTextRowAtAll_KeepsTheMainRowText()
    {
        // 正向对照：没有文本行时，Main.mText 仍是兜底（这条 clip 是 CF_HDROP 那种）。
        InsertMain(13, "只有 mText");

        var clip = Assert.Single(Read(13));

        Assert.Equal("只有 mText", clip.Text);
    }

    private List<DittoClip> Read(long clipId)
    {
        using var reader = new DittoDatabaseReader(_dbPath);
        Assert.True(reader.IsOpen);
        return reader.ReadRecent(50).Where(c => c.Id == clipId).ToList();
    }

    private void InsertMain(long id, string text)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Main (lID, lDate, mText, bIsGroup) VALUES ($id, $date, $text, 0)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$text", text);
        cmd.ExecuteNonQuery();
    }

    private void InsertUnicode(long parentId, string text)
        => InsertData(parentId, "CF_UNICODETEXT", Encoding.Unicode.GetBytes(text + "\0"));

    private void InsertAnsi(long parentId, string text)
        => InsertData(parentId, "CF_TEXT", Encoding.ASCII.GetBytes(text + "\0"));

    private void InsertData(long parentId, string format, byte[] payload)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Data (lParentID, strClipBoardFormat, ooData) VALUES ($p, $f, $d)";
        cmd.Parameters.AddWithValue("$p", parentId);
        cmd.Parameters.AddWithValue("$f", format);
        cmd.Parameters.AddWithValue("$d", payload);
        cmd.ExecuteNonQuery();
    }
}
