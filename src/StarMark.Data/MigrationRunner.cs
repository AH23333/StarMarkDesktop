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

        WriteSchemaVersion(conn, CurrentVersion);
    }

    public const int CurrentVersion = 2;

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
