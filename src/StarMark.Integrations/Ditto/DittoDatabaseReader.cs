#nullable enable
using System.Text;
using Microsoft.Data.Sqlite;

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
            cmd.CommandText = """
                SELECT lID, lDate, mText, bIsGroup
                FROM Main
                WHERE lDate IS NOT NULL AND bIsGroup IN (0, NULL)
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
                WHERE lDate IS NOT NULL AND bIsGroup IN (0, NULL)
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
                        case "CF_TEXT":
                            var text = DecodeText(fmt.ToUpperInvariant() == "CF_TEXT", data);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                clip.Text = text;
                                clip.Format = fmt.ToUpperInvariant();
                            }
                            break;
                        case "CF_HDROP":
                            hdrop = data;
                            break;
                    }
                }
            }

            if (hdrop != null)
            {
                clip.Files = ParseDropFiles(hdrop);
                if (clip.Files.Count > 0)
                    clip.Format = "CF_HDROP";
            }
        }
        catch { }
    }

    private static string DecodeText(bool ansi, byte[] data)
    {
        // 剪贴板文本可能带结尾 NUL
        var end = data.Length;
        while (end > 0 && data[end - 1] == 0) end--;

        try
        {
            if (ansi)
                return Encoding.UTF8.GetString(data, 0, end);
            return Encoding.Unicode.GetString(data, 0, end & ~1);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>解析 CF_HDROP 的 DROPFILES 结构：头部 20 字节 + 双 NUL 结束的文件路径列表。</summary>
    private static List<string> ParseDropFiles(byte[] data)
    {
        var files = new List<string>();
        if (data.Length < 20) return files;

        uint pFiles = BitConverter.ToUInt32(data, 0);
        bool wide = BitConverter.ToUInt32(data, 16) != 0;
        var offset = Math.Min((int)pFiles, data.Length);

        if (wide)
        {
            var sb = new StringBuilder();
            var i = offset;
            while (i + 1 < data.Length)
            {
                var ch = (char)BitConverter.ToUInt16(data, i);
                i += 2;
                if (ch == 0)
                {
                    if (sb.Length > 0)
                    {
                        files.Add(sb.ToString());
                        sb.Clear();
                    }
                    // 连续两个 NUL 表示列表结束
                    if (i + 1 < data.Length && BitConverter.ToUInt16(data, i) == 0) break;
                    continue;
                }
                sb.Append(ch);
            }
        }
        else
        {
            var sb = new StringBuilder();
            var i = offset;
            while (i < data.Length)
            {
                var ch = (char)data[i++];
                if (ch == 0)
                {
                    if (sb.Length > 0)
                    {
                        files.Add(sb.ToString());
                        sb.Clear();
                    }
                    if (i < data.Length && data[i] == 0) break;
                    continue;
                }
                sb.Append(ch);
            }
        }
        return files;
    }

    private static string EscapeLike(string s) => s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    public void Dispose() => _connection?.Dispose();
}