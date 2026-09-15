#nullable enable
using System.IO;
using Microsoft.Data.Sqlite;

namespace StarMark.Data;

/// <summary>
/// SQLite 连接工厂。统一管理连接字符串与 PRAGMA 设置。
/// 数据库文件位于 %APPDATA%\StarMark\starmark.db。
/// </summary>
public sealed class DbConnectionFactory
{
    private readonly string _connectionString;

    public string DbPath { get; }

    public DbConnectionFactory(string? dbPath = null)
    {
        // 开发期允许通过 STARMARK_DB_PATH 环境变量覆盖默认路径
        // （避免沙盒限制 %APPDATA% 写入；生产环境走 %APPDATA%\StarMark\starmark.db）
        DbPath = dbPath ?? Environment.GetEnvironmentVariable("STARMARK_DB_PATH") ?? DefaultDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        // SQLite 数据库文件位于 %APPDATA%\StarMark\starmark.db
        _connectionString = $"Data Source={DbPath}";
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // 每次连接时设置 PRAGMA（SQLite 每连接独立）
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA synchronous=NORMAL;";
            cmd.ExecuteNonQuery();
        }
        return conn;
    }

    public static string DefaultDbPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "StarMark", "starmark.db");
    }
}
