#nullable enable
using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Abstractions.Language;
using ActivityKind = StarMark.Abstractions.ActivityKind;

namespace StarMark.Data;

/// <summary>
/// 活动流与同步状态检查点。 <b>与同目录其余 ItemRepository.*.cs 是同一个类</b>
/// （partial）——按 SQLite 访问面分文件，不是一个新抽象层。
/// </summary>
public sealed partial class ItemRepository
{
    // ===== 活动流（扩展对比方案 P1-3）=====

    /// <summary>写入一条活动事件。插入后裁剪环形缓冲，仅保留最近 500 条（>500 删最旧）。</summary>
    public async Task LogActivityAsync(ActivityKind kind, string? itemKey, string title, string? uri, CancellationToken ct)
    {
        using var conn = _factory.Open();
        await LogActivityOnConnection(conn, kind, itemKey, title, uri, ct);
        DataChangeHub.Notify();   // 活动事件本身也是「数据」：让常驻的最近活动格即时跟上（与业务写各自的 Notify 叠加，重载有去抖）
    }

    /// <summary>在给定连接上写入活动事件（供 <see cref="UpsertOne"/> 在已有事务内复用连接）。</summary>
    private static async Task LogActivityOnConnection(SqliteConnection conn, ActivityKind kind, string? itemKey, string title, string? uri, CancellationToken ct)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO activity(at, kind, item_key, title, uri)
                VALUES(@at, @kind, @item_key, @title, @uri);";
            cmd.Parameters.AddWithValue("@at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("@kind", kind.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@item_key", (object?)itemKey ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@title", title);
            cmd.Parameters.AddWithValue("@uri", (object?)uri ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 环形缓冲：仅保留最近 500 条
        using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) - 500 FROM activity;";
            var overflow = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));
            if (overflow > 0)
            {
                using var prune = conn.CreateCommand();
                prune.CommandText = @"
                    DELETE FROM activity
                    WHERE id IN (
                        SELECT id FROM activity ORDER BY at ASC, id ASC LIMIT @n
                    );";
                prune.Parameters.AddWithValue("@n", overflow);
                await prune.ExecuteNonQueryAsync(ct);
            }
        }
    }

    /// <summary>读取最近的活动事件，按时间倒序。</summary>
    public async Task<IReadOnlyList<ActivityRecord>> GetActivityAsync(int limit, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, at, kind, item_key, title, uri
            FROM activity
            ORDER BY at DESC, id DESC
            LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);
        var list = new List<ActivityRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ActivityRecord
            {
                Id = reader.GetInt64(0),
                At = reader.GetInt64(1),
                Kind = Enum.TryParse<ActivityKind>(reader.GetString(2), ignoreCase: true, out var k)
                    ? k : ActivityKind.ItemDelete,
                ItemKey = reader.IsDBNull(3) ? null : reader.GetString(3),
                Title = reader.GetString(4),
                Uri = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }
        return list;
    }

    // ===== 同步状态（扩展对比方案 P1-4）=====

    /// <summary>读取 sync_state 键值；不存在返回 null。</summary>
    public async Task<string?> GetSyncStateAsync(string key, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM sync_state WHERE key = @key;";
        cmd.Parameters.AddWithValue("@key", key);
        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj == null || obj == DBNull.Value ? null : (string?)obj;
    }

    /// <summary>幂等写入 sync_state 键值（用于 ETag / last_synced_at 等检查点）。</summary>
    public async Task SetSyncStateAsync(string key, string value, CancellationToken ct)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sync_state(key, value) VALUES(@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertTagLink(SqliteConnection conn, long itemId, string tagName, CancellationToken ct)
    {
        long tagId;
        using (var findTag = conn.CreateCommand())
        {
            findTag.CommandText = "SELECT id FROM tags WHERE name = @name COLLATE NOCASE";
            findTag.Parameters.AddWithValue("@name", tagName);
            var existing = await findTag.ExecuteScalarAsync(ct);
            if (existing is long id)
            {
                tagId = id;
            }
            else
            {
                using var createTag = conn.CreateCommand();
                createTag.CommandText = "INSERT INTO tags(name, created_at) VALUES(@name, @now); SELECT last_insert_rowid();";
                createTag.Parameters.AddWithValue("@name", tagName);
                createTag.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                tagId = (long)(await createTag.ExecuteScalarAsync(ct))!;
            }
        }

        using var link = conn.CreateCommand();
        link.CommandText = "INSERT OR IGNORE INTO item_tags(item_id, tag_id, created_at) VALUES(@item, @tag, @now)";
        link.Parameters.AddWithValue("@item", itemId);
        link.Parameters.AddWithValue("@tag", tagId);
        link.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await link.ExecuteNonQueryAsync(ct);
    }

    private static Item MapItem(SqliteDataReader reader)
    {
        var item = new Item
        {
            Id = reader.GetInt64(0),
            // 与 BackupRepository.MapItem 同口径用 TryParse 兜底：items.type 无 CHECK 约束，
            // 降级/备份恢复/手工修正可留 Enum.Parse 抛 ArgumentException 的未知值；而 MapItem 是所有读路径
            // （Search/GetAll/GetPinned/GetHidden/…）的水合入口，一行坏值即整页查询抛异常（非只坏那一行）。
            Type = Enum.TryParse<ItemType>(reader.GetString(1), ignoreCase: true, out var itemType) ? itemType : ItemType.Bookmark,
            Source = reader.GetString(2),
            SourceId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            Title = reader.GetString(4),
            Subtitle = reader.GetString(5),
            Uri = reader.GetString(6),
            Description = reader.IsDBNull(7) ? null : reader.GetString(7),
            StarsCount = reader.IsDBNull(8) ? null : reader.GetInt64(8),
            FileSize = reader.IsDBNull(9) ? null : reader.GetInt64(9),
            CreatedAt = reader.GetInt64(10),
            UpdatedAt = reader.GetInt64(11),
            SyncedAt = reader.IsDBNull(12) ? null : reader.GetInt64(12),
            ExtraJson = reader.IsDBNull(13) ? null : reader.GetString(13),
            Hidden = reader.GetInt32(14) != 0,
            // SELECT 列序：14 = hidden，15 = pinned，16 = notes，17 = tag_names
            Pinned = reader.FieldCount > 15 && !reader.IsDBNull(15) && reader.GetInt64(15) != 0,
            Notes = reader.FieldCount > 16 && !reader.IsDBNull(16) ? reader.GetString(16) : null,
        };

        // 标签：char(31)（单元分隔符，正常标签名不含）分隔的字符串 → List<string>
        if (reader.FieldCount > 17 && !reader.IsDBNull(17))
        {
            var tagStr = reader.GetString(17);
            if (!string.IsNullOrEmpty(tagStr))
            {
                item.Tags = tagStr.Split(new[] { (char)31 }, StringSplitOptions.RemoveEmptyEntries).ToList();
            }
        }
        return item;
    }

    /// <summary>
    /// 构建 FTS5 查询表达式。空格分隔的多个词 → AND 匹配。
    /// 词中含非字母数字 → 加引号作为短语。
    /// </summary>
    private static string BuildFtsQuery(string keyword)
    {
        var tokens = StarMark.Abstractions.Text.CjkTokenizer.SplitForQuery(keyword);
        if (tokens.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var t in tokens)
        {
            if (sb.Length > 0) sb.Append(' ');   // 空格 = FTS5 隐式 AND

            if (StarMark.Abstractions.Text.CjkTokenizer.ContainsCjk(t))
            {
                // CJK 词元已是完整的二元组，加 '*' 会造成过度匹配；且不含 FTS5 特殊字符
                sb.Append(t);
            }
            else if (t.Any(c => !char.IsLetterOrDigit(c)))
            {
                // 简单转义：含特殊字符的词加引号
                sb.Append('"').Append(t.Replace("\"", "\"\"")).Append('"');
            }
            else
            {
                // AND/OR/NOT/NEAR 是 FTS5 大写布尔算符：裸 "AND*" 会被语法解析器当算符、
                // 对尾随 '*' 报 "fts5: syntax error"（语句级、整条 MATCH 崩）。本项目的检索语义
                // 是"空格=全部 AND"、不支持布尔算符，故把这几个词降级为普通检索词（小写化去掉
                // 算符身份），仍走前缀匹配——既不崩、又能按字面召回含该词的条目。
                var term = IsFts5Operator(t) ? t.ToLowerInvariant() : t;
                sb.Append(term).Append('*');  // 前缀匹配
            }
        }
        return sb.ToString();
    }

    private static bool IsFts5Operator(string token) =>
        token.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("OR", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("NOT", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("NEAR", StringComparison.OrdinalIgnoreCase);
}
