#nullable enable
using System;
using System.IO;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// DbConnectionFactory 的连接串由 <c>DbPath</c> 构造。若用裸字符串拼接，
/// Windows 文件名里合法的 ';' / '=' 会被 SQLite 连接串解析器当作关键字分隔符，令 Open() 抛异常、
/// 应用在含这些字符的 %APPDATA%（如用户名带 ';'）下完全打不开自己的库。
/// </summary>
public sealed class DbConnectionFactoryTests
{
    [Theory]
    [InlineData("we;ird.db")]   // ';' 是连接串关键字分隔符
    [InlineData("ba=lance.db")] // '=' 会被当作 key/value 分隔，剩余部分成非法关键字
    public void Open_PathWithConnectionStringSpecialChars_Succeeds(string fileName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "starmark-dbfactory-" + Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(dir, fileName);
        try
        {
            Directory.CreateDirectory(dir);
            var factory = new DbConnectionFactory(dbPath);
            using var conn = factory.Open();

            Assert.True(File.Exists(dbPath));
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", cmd.ExecuteScalar()?.ToString());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 临时目录清理失败不影响断言 */ }
        }
    }
}
