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
        // SQLite 数据库文件位于 %APPDATA%\StarMark\starmark.db。
        // 用 builder 而非裸拼接：路径里的 ';' / '=' 是连接串保留字符（Windows 文件名合法，如用户名含 ';'），
        // 裸拼会被解析成关键字分隔符而抛 ArgumentException，导致应用在自己的库上都打不开。builder 会自动加引号转义。
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DbPath }.ConnectionString;
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // 每次连接时设置 PRAGMA（SQLite 每连接独立）
        using (var cmd = conn.CreateCommand())
        {
            // busy_timeout：WAL 下写写仍互斥，应用有约 30 处各自 Open 的短连接 + 后台同步/组件写入并发。
            // 不设时默认 0ms，任一次瞬时锁争用即抛 SQLITE_BUSY（表现为同步/迁移/组件保存偶发失败）。
            // 给 5s 让 SQLite 自行重试排队，消除这类伪失败。
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
        }
        return conn;
    }

    public static string DefaultDbPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, StarMark.Abstractions.AppConstants.AppName, "starmark.db");
    }
}
