#nullable enable
using Microsoft.Data.Sqlite;
using StarMark.Abstractions.Clipboard;
using StarMark.Integrations.Clipboard;

namespace StarMark.Integrations.Ditto;

/// <summary>
/// 一条 Ditto 剪贴板记录（读自 DittoDB.db 的 Main 表）。
/// </summary>
public sealed class DittoClip
{
    public long Id { get; init; }
    public long Date { get; init; }
    public string Text { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new();
    public string Format { get; set; } = string.Empty;
    public bool IsGroup { get; init; }
}

/// <summary>
/// Ditto 剪贴板数据库读取器。
///
/// 直读 %APPDATA%\Ditto\DB\DittoDB.db（只读）。已对照 Ditto 源码（src/DatabaseUtilities.cpp、
/// src/Clip.cpp）确认 schema：
///   Main(lID INTEGER PK, lDate INTEGER, mText TEXT, bIsGroup INTEGER, lParentID INTEGER, ..., lastPasteDate INTEGER)
///   Data(lID PK, lParentID INTEGER, strClipBoardFormat TEXT, ooData BLOB)
/// Main.mText = 剪贴板文本（UTF-16/ANSI）；Data.ooData = 各剪贴板格式原始字节（CF_UNICODETEXT、
/// CF_HDROP 为 DROPFILES 结构）。lDate 为 Unix 秒。
/// </summary>
public sealed class DittoDatabaseReader : IDisposable
{
    private readonly SqliteConnection? _connection;

    public DittoDatabaseReader(string dbPath)
    {
        if (!File.Exists(dbPath)) return;
        try
        {
            _connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Cache=Shared");
            _connection.Open();
        }
        catch
        {
            // Ditto 可能正占用独占锁，视为不可读
            _connection = null;
        }
    }

    public bool IsOpen => _connection != null;

    /// <summary>读取最近若干条非分组剪贴板记录（按复制时间倒序）。</summary>
    public List<DittoClip> ReadRecent(int limit)
    {
        var result = new List<DittoClip>();
        if (_connection == null) return result;

        try
        {
            using var cmd = _connection.CreateCommand();
            // bIsGroup 常为 NULL（未分组剪贴板）。SQL 三值逻辑下 `bIsGroup IN (0, NULL)`
            // 里的 NULL 永不匹配，会漏掉全部 NULL 行，必须显式 `OR bIsGroup IS NULL`。
            cmd.CommandText = """
                SELECT lID, lDate, mText, bIsGroup
                FROM Main
                WHERE lDate IS NOT NULL AND (bIsGroup = 0 OR bIsGroup IS NULL)
                ORDER BY lDate DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$limit", Math.Max(0, Math.Min(limit, 2000)));

            var ids = new List<long>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var clip = new DittoClip
                    {
                        Id = reader.GetInt64(0),
                        Date = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                        Text = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        IsGroup = !reader.IsDBNull(3) && reader.GetInt64(3) != 0,
                    };
                    ids.Add(clip.Id);
                    result.Add(clip);
                }
            }

            foreach (var clip in result)
                FillData(clip);
        }
        catch
        {
            result.Clear();
        }
        return result;
    }

    /// <summary>在剪贴板文本/标题上做 LIKE 检索（不区分大小写）。</summary>
    public List<DittoClip> Search(string query, int limit)
    {
        var result = new List<DittoClip>();
        if (_connection == null) return result;

        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT lID, lDate, mText, bIsGroup
                FROM Main
                WHERE lDate IS NOT NULL AND (bIsGroup = 0 OR bIsGroup IS NULL)
                  AND mText LIKE $pattern ESCAPE '\'
                ORDER BY lDate DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$pattern", "%" + EscapeLike(query) + "%");
            cmd.Parameters.AddWithValue("$limit", Math.Max(0, Math.Min(limit, 500)));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new DittoClip
                {
                    Id = reader.GetInt64(0),
                    Date = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                    Text = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    IsGroup = !reader.IsDBNull(3) && reader.GetInt64(3) != 0,
                });
            }

            foreach (var clip in result)
                FillData(clip);
        }
        catch
        {
            result.Clear();
        }
        return result;
    }

    private void FillData(DittoClip clip)
    {
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT strClipBoardFormat, ooData FROM Data WHERE lParentID = $id";
            cmd.Parameters.AddWithValue("$id", clip.Id);

            byte[]? hdrop = null;
            string? wideText = null;
            string? ansiText = null;
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var fmt = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    if (reader.IsDBNull(1)) continue;
                    var data = (byte[])reader.GetValue(1);

                    switch (fmt.ToUpperInvariant())
                    {
                        case "CF_UNICODETEXT":
                            var w = ClipboardPayload.DecodeUnicodeText(data);
                            if (!string.IsNullOrWhiteSpace(w)) wideText = w;
                            break;
                        case "CF_TEXT":
                            // P-18：CF_TEXT 是码页文本，不是 UTF-8（按 Win32 约定是 OEM 码页）。
                            var a = ClipboardPayload.DecodeAnsiText(data, AnsiText.DecodeSystemOemText);
                            if (!string.IsNullOrWhiteSpace(a)) ansiText = a;
                            break;
                        case "CF_HDROP":
                            hdrop = data;
                            break;
                    }
                }
            }

            // P-18 的另一半：那句 SELECT 没有 ORDER BY ⇒ 谁后到谁赢是**行序**在替我们做决定。
            // 两种文本格式各存各的，出了循环再按"宽字符优先"定夺 ⇒ 同一 clip 的读数与行序无关。
            // （同一格式内仍是后到的赢，与改前一致；这里改的是跨格式的裁决，不是新增防御分支。）
            if (wideText is not null)
            {
                clip.Text = wideText;
                clip.Format = ClipboardPayload.FormatUnicodeText;
            }
            else if (ansiText is not null)
            {
                clip.Text = ansiText;
                clip.Format = ClipboardPayload.FormatAnsiText;
            }

            if (hdrop != null)
            {
                clip.Files = ClipboardPayload.ParseDropFiles(hdrop, AnsiText.DecodeSystemAnsi);
                if (clip.Files.Count > 0)
                    clip.Format = ClipboardPayload.FormatHDrop;
            }
        }
        catch { }
    }

    private static string EscapeLike(string s) => s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    public void Dispose() => _connection?.Dispose();
}