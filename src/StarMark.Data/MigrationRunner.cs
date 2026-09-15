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
        WriteSchemaVersion(conn, 1);
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
