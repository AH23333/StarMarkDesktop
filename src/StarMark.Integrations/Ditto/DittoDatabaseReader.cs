#nullable enable
using Microsoft.Data.Sqlite;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Integrations.Clipboard;

namespace StarMark.Integrations.Ditto;

/// <summary>
/// 一条 Ditto 剪贴板记录（读自 Ditto 库的 Main 表）。
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
/// 直读 Ditto 的剪贴板库（只读；路径由 DittoSource 的候选表探测——装机版 3.25 实测落 %APPDATA%\Ditto\Ditto.db）。
/// 已对照 Ditto 源码（src/DatabaseUtilities.cpp、
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
            // ✅P-28B′：Ditto 正在写库的那一瞬，读侧会撞 SQLITE_BUSY。给 5 秒等锁窗口
            //（与主库 DbConnectionFactory 同一口径）：一瞬撞锁不再被读成"这一轮读失败"，
            // 真等不到锁才轮到下面两颗读法去抛 SourceReadException。
            using (var pragma = _connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA busy_timeout=5000;";
                pragma.ExecuteNonQuery();
            }
        }
        catch
        {
            // 这一站保持宽松（P-28B′ 的分工）：打不开（不存在/损坏/被独占）视作
            // "本机没有可读的 Ditto 库"，IsOpen=false 让调用方走"未配置"那条路，
            // ⛔ 把装机差异读成同步故障。
            _connection?.Dispose();
            _connection = null;
        }
    }

    public bool IsOpen => _connection != null;

    /// <summary>
    /// 读取最近若干条非分组剪贴板记录（按复制时间倒序）。
    /// <para><paramref name="ct"/> 掐在两颗循环的边界上（逐行取回、逐颗补附件），掐中<b>上抛</b>——
    /// 被取消的一次读若降级成空表，在调用方看来自洽，而 <c>SyncCoordinator</c> 会把它记成
    /// <c>Success=true, PulledCount=0</c>（P-28 点名的正是这一族）。</para>
    /// <para>✅P-28B′：其余读取失败（等锁超时、schema 异变）同样<b>上抛</b>——包成
    /// <see cref="SourceReadException"/> 交给 <c>SyncCoordinator</c> 记「这一源读取失败」，
    /// ⛔ 再吞成空表冒充"读到了零条"。</para>
    /// </summary>
    public List<DittoClip> ReadRecent(int limit, CancellationToken ct = default)
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
                    ct.ThrowIfCancellationRequested();
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

            foreach (var clip in result) { ct.ThrowIfCancellationRequested(); FillData(clip); }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new SourceReadException("读取 Ditto 剪贴板库失败（ReadRecent）", ex);
        }
        return result;
    }

    /// <summary>
    /// 在剪贴板文本/标题上做 LIKE 检索（不区分大小写）。
    /// <para>这一颗的<b>逐行取回就是 Ditto 库上的全表 <c>LIKE '%…%'</c> 扫描</b>（无索引可依），而实时搜索
    /// 逐按键发一次、上一发会被取消（<c>SearchService</c> 交 <c>Task.Run(…, ct)</c> 并 <c>WaitAsync(ct)</c>）——
    /// 令牌不接进这颗循环时，"取消"只解了调用方的等，那轮扫描仍会在池线程上跑到完：每次键入都留下一条
    /// 在跑的整表扫描。同 <see cref="ReadRecent"/>：<b>掐中上抛</b>。</para>
    /// <para>✅P-28B′：其余读取失败同样上抛（<see cref="SourceReadException"/>），⛔ 吞成空表——
    /// 搜索腿的调用方（<c>SearchService</c>）对每条腿各有容错，真失败记降级日志而不是装作"没搜到"。</para>
    /// </summary>
    public List<DittoClip> Search(string query, int limit, CancellationToken ct = default)
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
                ct.ThrowIfCancellationRequested();
                result.Add(new DittoClip
                {
                    Id = reader.GetInt64(0),
                    Date = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                    Text = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    IsGroup = !reader.IsDBNull(3) && reader.GetInt64(3) != 0,
                });
            }

            foreach (var clip in result) { ct.ThrowIfCancellationRequested(); FillData(clip); }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new SourceReadException("检索 Ditto 剪贴板库失败（Search）", ex);
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