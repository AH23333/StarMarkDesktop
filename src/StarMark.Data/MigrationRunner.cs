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

        WriteSchemaVersion(conn, CurrentVersion);
    }

    public const int CurrentVersion = 3;

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
