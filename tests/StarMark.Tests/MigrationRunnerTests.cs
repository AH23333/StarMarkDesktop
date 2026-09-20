#nullable enable
using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;
using StarMark.Data;

namespace StarMark.Tests;

/// <summary>
/// MigrationRunner 的版本闸门行为：EnsureSchema 幂等建库，且绝不把 schema_version 倒拨。
/// </summary>
public sealed class MigrationRunnerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;

    public MigrationRunnerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_mig_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private int ReadVersion()
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM sync_state WHERE key = 'schema_version';";
        var obj = cmd.ExecuteScalar();
        return int.TryParse(obj?.ToString(), out var v) ? v : -1;
    }

    private void WriteVersion(int version)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sync_state(key, value) VALUES('schema_version', @v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        cmd.Parameters.AddWithValue("@v", version.ToString());
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void EnsureSchema_FreshDb_WritesCurrentVersion()
    {
        new MigrationRunner(_factory).EnsureSchema();

        Assert.Equal(MigrationRunner.CurrentVersion, ReadVersion());
    }

    /// <summary>
    /// AM-1 回归：应用降级（新版建库、旧二进制打开）时，库里存的 schema_version 会高于
    /// 本二进制的 CurrentVersion。旧实现无条件写回 CurrentVersion，把版本号倒拨，
    /// 掩盖"库结构比二进制更新"的事实，并可能在下次升级误重跑已执行过的迁移。
    /// 修复后：version ≥ CurrentVersion 时不动 schema_version。
    /// </summary>
    [Fact]
    public void EnsureSchema_WhenStoredVersionNewer_IsNotRewound()
    {
        // 先建库（写入 schema_version=CurrentVersion），再人为拔高到"未来版本"
        new MigrationRunner(_factory).EnsureSchema();
        const int future = MigrationRunner.CurrentVersion + 95; // 99
        WriteVersion(future);
        Assert.Equal(future, ReadVersion());

        new MigrationRunner(_factory).EnsureSchema();

        Assert.Equal(future, ReadVersion()); // 不得被倒拨回 CurrentVersion
    }
}
