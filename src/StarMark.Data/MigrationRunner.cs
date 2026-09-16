#nullable enable
using System.IO;
using System.Reflection;

namespace StarMark.Data;

/// <summary>
/// 数据库迁移器。从嵌入资源加载 Schema.sql 并幂等执行。
/// 对应技术文档 §4 数据模型。
/// </summary>
public sealed class MigrationRunner
{
    private readonly DbConnectionFactory _factory;

    public MigrationRunner(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>初始化或升级 schema。应用启动时调用。</summary>
    public void EnsureSchema()
    {
        var sql = LoadEmbeddedSchema();
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        // Schema.sql 中所有 CREATE 语句均为 IF NOT EXISTS，可幂等执行
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();

        // Schema.sql 只保证"表存在"，列级演进靠版本化迁移（v1 建库，v2 起 ALTER）。
        var version = ReadSchemaVersion(conn);
        if (version < 2) MigrateV2(conn);
        if (version < 3) MigrateV3(conn);
        if (version < 4) MigrateV4(conn);

        WriteSchemaVersion(conn, CurrentVersion);
    }

    public const int CurrentVersion = 4;

    private static int ReadSchemaVersion(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM sync_state WHERE key = 'schema_version';";
        var obj = cmd.ExecuteScalar();
        return int.TryParse(obj?.ToString(), out var v) ? v : 1;
    }

    /// <summary>v2：items.pinned（用户置顶）。ALTER 仅在列缺失时执行，幂等。</summary>
    private static void MigrateV2(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        if (ColumnExists(conn, "items", "pinned")) return;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "ALTER TABLE items ADD COLUMN pinned INTEGER NOT NULL DEFAULT 0;";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// v3：重算 items.search_text 并重建 FTS 索引。
    /// </summary>
    /// <remarks>
    /// 存量行的 search_text 未经 CJK 展开，中文子串查询会全部落空（unicode61 把连续
    /// 中文当作单个 token）。此处按与 UpsertAsync 相同的口径（title + description +
    /// notes + 标签名）重算，再执行 FTS5 的 'rebuild' 从外部内容表重新灌索引。
    /// 幂等：重复执行结果一致。
    /// </remarks>
    private static void MigrateV3(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        var rows = new List<(long Id, string Text)>();
        using (var sel = conn.CreateCommand())
        {
            sel.CommandText = @"
                SELECT i.id,
                       COALESCE(i.title, '') || ' ' ||
                       COALESCE(i.description, '') || ' ' ||
                       COALESCE(i.notes, '') || ' ' ||
                       COALESCE((SELECT GROUP_CONCAT(t.name, ' ') FROM item_tags it
                                 JOIN tags t ON t.id = it.tag_id
                                 WHERE it.item_id = i.id), '')
                FROM items i;";
            using var reader = sel.ExecuteReader();
            while (reader.Read())
            {
                string raw = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                rows.Add((reader.GetInt64(0), StarMark.Abstractions.Text.CjkTokenizer.ExpandForIndex(raw)));
            }
        }

        using var tx = conn.BeginTransaction();
        using (var upd = conn.CreateCommand())
        {
            upd.CommandText = "UPDATE items SET search_text = @t WHERE id = @id;";
            var pId = upd.Parameters.Add("@id", Microsoft.Data.Sqlite.SqliteType.Integer);
            var pText = upd.Parameters.Add("@t", Microsoft.Data.Sqlite.SqliteType.Text);
            foreach (var (id, text) in rows)
            {
                pId.Value = id;
                pText.Value = text;
                upd.ExecuteNonQuery();
            }
        }

        // 外部内容表（content='items'）必须从源表重灌索引
        using (var rebuild = conn.CreateCommand())
        {
            rebuild.CommandText = "INSERT INTO items_fts(items_fts) VALUES('rebuild');";
            rebuild.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// v4：按归一化 <c>source_id</c> 合并重复条目（扩展对比方案 P1-5）。
    /// 同一资源的不同 URL 变体（尾斜杠 / utm 参数 / 默认端口 / GitHub <c>tab=</c> 查询）原会被
    /// 判为多条，导致标签与笔记分裂。此处按 (source, 归一化 key) 分组，每组保留最早
    /// <c>created_at</c> 的条目，合并其标签与笔记，删除其余。幂等：重复执行结果一致。
    /// </summary>
    private static void MigrateV4(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        var rows = new List<(long Id, string Source, string SourceId, long CreatedAt, string? Notes)>();
        using (var sel = conn.CreateCommand())
        {
            sel.CommandText = "SELECT id, source, source_id, created_at, notes FROM items WHERE source_id IS NOT NULL;";
            using var reader = sel.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        // 仅对出现重复的分组合并（非 http(s) 源的 key 等于原 source_id，本就是去重）
        var groups = rows
            .GroupBy(r => (r.Source, StarMark.Abstractions.UriNormalizer.Normalize(r.SourceId)))
            .Where(g => g.Count() > 1)
            .ToList();
        if (groups.Count == 0) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var tx = conn.BeginTransaction();
        foreach (var g in groups)
        {
            // keeper = 最早 created_at（并列取最小 id），其余为待删除的重复项
            var keeper = g.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).First();
            var normKey = StarMark.Abstractions.UriNormalizer.Normalize(keeper.SourceId);

            foreach (var dup in g.Where(r => r.Id != keeper.Id))
            {
                // 合并标签：把 dup 的标签关联到 keeper（INSERT OR IGNORE 防重复）
                using (var move = conn.CreateCommand())
                {
                    move.CommandText = @"
                        INSERT OR IGNORE INTO item_tags(item_id, tag_id, created_at)
                        SELECT @keeper, it.tag_id, @now
                        FROM item_tags it
                        WHERE it.item_id = @dup;";
                    move.Parameters.AddWithValue("@keeper", keeper.Id);
                    move.Parameters.AddWithValue("@dup", dup.Id);
                    move.Parameters.AddWithValue("@now", now);
                    move.ExecuteNonQuery();
                }

                // 合并笔记：keeper 为空，或 dup 的笔记更长时采用 dup 的笔记
                // （非空冲突保留较长者，见扩展对比方案 P1-5）
                bool keeperEmpty = string.IsNullOrEmpty(keeper.Notes);
                bool dupLonger = !string.IsNullOrEmpty(dup.Notes)
                    && dup.Notes!.Length > (keeper.Notes?.Length ?? 0);
                if (keeperEmpty || dupLonger)
                {
                    using (var upd = conn.CreateCommand())
                    {
                        upd.CommandText = "UPDATE items SET notes = @n WHERE id = @id;";
                        // dup.Notes 可能为 null：Microsoft.Data.Sqlite 对 Value=null 的参数是
                        // 抛 "Value must be set."，必须显式传 DBNull.Value 表示 SQL NULL。
                        upd.Parameters.AddWithValue("@n", (object?)dup.Notes ?? DBNull.Value);
                        upd.Parameters.AddWithValue("@id", keeper.Id);
                        upd.ExecuteNonQuery();
                        keeper.Notes = dup.Notes;
                    }
                }

                // 删除 dup：ON DELETE CASCADE 清 item_tags，FTS 触发器维护索引
                using (var del = conn.CreateCommand())
                {
                    del.CommandText = "DELETE FROM items WHERE id = @id;";
                    del.Parameters.AddWithValue("@id", dup.Id);
                    del.ExecuteNonQuery();
                }
            }

            // keeper 的 source_id 归一到标准键，使后续同步直接合并而非再分裂
            using (var upd = conn.CreateCommand())
            {
                upd.CommandText = "UPDATE items SET source_id = @k WHERE id = @id;";
                upd.Parameters.AddWithValue("@k", normKey);
                upd.Parameters.AddWithValue("@id", keeper.Id);
                upd.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    private static bool ColumnExists(Microsoft.Data.Sqlite.SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = @col;";
        cmd.Parameters.AddWithValue("@col", column);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static string LoadEmbeddedSchema()
    {
        var asm = Assembly.GetExecutingAssembly();
        // 嵌入资源名：命名空间.文件名
        var resourceName = "StarMark.Data.Schema.sql";
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"嵌入资源 {resourceName} 未找到");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void WriteSchemaVersion(Microsoft.Data.Sqlite.SqliteConnection conn, int version)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sync_state(key, value) VALUES('schema_version', @v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        cmd.Parameters.AddWithValue("@v", version.ToString());
        cmd.ExecuteNonQuery();
    }
}
