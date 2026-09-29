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

    private void Exec(string sql)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private T Scalar<T>(string sql)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var obj = cmd.ExecuteScalar();
        return (T)Convert.ChangeType(obj!, typeof(T));
    }

    /// <summary>
    /// 契约护栏：MigrateV4 按归一化 source_id 合并重复条目时，必须把整组的用户状态
    /// （hidden / pinned）与更长笔记、标签关联，全部 OR/合并回「最早 created_at」的 keeper，
    /// 只删 URL 变体不丢用户意图。此路径每次从 &lt;v4 升级都会跑，且回归会静默删除用户数据——
    /// 此前完全无测。构造两条归一后同键的 github_star（尾斜杠 vs tab= 查询）验证：
    /// keeper 保留、dup 删除、hidden/pinned OR 落到 keeper、空笔记被 dup 长笔记填充、source_id 归一。
    /// </summary>
    [Fact]
    public void EnsureSchema_MigrateV4_MergesDuplicatesPreservingUserState()
    {
        new MigrationRunner(_factory).EnsureSchema(); // 建表

        // keeper：URL 带尾斜杠，created_at 更早(100)，用户未隐藏/未置顶、无笔记
        Exec(@"INSERT INTO items(id,type,source,source_id,title,search_text,created_at,updated_at,hidden,pinned,notes)
               VALUES(1,'github_star','github','https://github.com/foo/bar/','A','A',100,100,0,0,'');");
        // dup：tab= 查询变体（归一后与 keeper 同键），created_at 更晚(200)，但用户把隐藏/置顶/笔记都留在了这条上
        Exec(@"INSERT INTO items(id,type,source,source_id,title,search_text,created_at,updated_at,hidden,pinned,notes)
               VALUES(2,'github_star','github','https://github.com/foo/bar?tab=issues','B','B',200,200,1,1,'这是较长的一条笔记');");
        // 标签挂在 dup 上
        Exec("INSERT INTO tags(id,name,created_at) VALUES(1,'work',1);");
        Exec("INSERT INTO item_tags(item_id,tag_id,created_at) VALUES(2,1,1);");

        // 回退到 v3 触发 v4 合并
        WriteVersion(3);
        new MigrationRunner(_factory).EnsureSchema();

        // 只剩 keeper（id=1），dup 被删
        Assert.Equal(1, Scalar<long>("SELECT COUNT(*) FROM items;"));
        Assert.Equal(1, Scalar<long>("SELECT id FROM items;"));
        // 用户状态整组 OR 归并到 keeper（原本只在 dup 上的 hidden/pinned 未丢）
        Assert.Equal(1, Scalar<long>("SELECT hidden FROM items WHERE id=1;"));
        Assert.Equal(1, Scalar<long>("SELECT pinned FROM items WHERE id=1;"));
        // keeper 空笔记被 dup 的长笔记填充
        Assert.Equal("这是较长的一条笔记",
            Scalar<string>("SELECT notes FROM items WHERE id=1;"));
        // keeper 的 source_id 归一到标准键（尾斜杠与 tab= 均被剥除）
        Assert.Equal("https://github.com/foo/bar",
            Scalar<string>("SELECT source_id FROM items WHERE id=1;"));
        // 标签关联随合并搬到 keeper
        Assert.Equal(1, Scalar<long>("SELECT COUNT(*) FROM item_tags WHERE item_id=1 AND tag_id=1;"));
    }

    /// <summary>
    /// 批次 RW（P-118）v6 要补的那一类：<b>键变了、但没有双胞胎的行</b>。
    /// <para>
    /// <c>ItemRepository.UpsertOne</c> 找旧行只看 <c>(source, source_id)</c>，而它写库前会先把
    /// source_id 过一遍 <c>Normalize</c>。RW 之后规范形变成 <c>https://github.com/foo/bar</c>，
    /// 但库里那行存量 <c>https://www.github.com/Foo/Bar</c> 没人改 ⇒ 下一次同步按新键<b>另起一行</b>，
    /// 旧行连同它的标签/笔记/置顶被永久孤立（v4 只合并"归一后撞上"的组，恰好漏掉这种单行组）。
    /// </para>
    /// 这条不测"合并"，只测"改键且不丢用户状态"——id 不变、标签还在、笔记还在。
    /// </summary>
    [Fact]
    public void EnsureSchema_MigrateV6_RenamesOrphanedKeyWithoutLosingUserState()
    {
        new MigrationRunner(_factory).EnsureSchema();

        Exec(@"INSERT INTO items(id,type,source,source_id,title,search_text,created_at,updated_at,hidden,pinned,notes)
               VALUES(7,'github_star','github','https://www.github.com/Foo/Bar','A','A',100,100,1,0,'留在旧键上的笔记');");
        Exec("INSERT INTO tags(id,name,created_at) VALUES(3,'keepme',1);");
        Exec("INSERT INTO item_tags(item_id,tag_id,created_at) VALUES(7,3,1);");

        WriteVersion(5);                                   // 只触发 v6
        new MigrationRunner(_factory).EnsureSchema();

        Assert.Equal("https://github.com/foo/bar",
            Scalar<string>("SELECT source_id FROM items WHERE id=7;"));
        Assert.Equal(1, Scalar<long>("SELECT COUNT(*) FROM items;"));
        Assert.Equal(1, Scalar<long>("SELECT hidden FROM items WHERE id=7;"));
        Assert.Equal("留在旧键上的笔记", Scalar<string>("SELECT notes FROM items WHERE id=7;"));
        Assert.Equal(1, Scalar<long>("SELECT COUNT(*) FROM item_tags WHERE item_id=7 AND tag_id=3;"));
    }

    /// <summary>
    /// v6 的另一半：RW 之后 <c>http://</c> 与 <c>https://</c> 两种写法<b>撞成同键</b>了，
    /// 必须按 v4 那套合并口径收（keeper＝最早 created_at、标签并、笔记取长、hidden/pinned 整组 OR），
    /// 而不是留下两行让"疑似重复"继续误报。
    /// </summary>
    [Fact]
    public void EnsureSchema_MigrateV6_MergesVariantsThatNewlyCollide()
    {
        new MigrationRunner(_factory).EnsureSchema();

        Exec(@"INSERT INTO items(id,type,source,source_id,title,search_text,created_at,updated_at,hidden,pinned,notes)
               VALUES(1,'github_star','github','http://github.com/foo/bar','A','A',100,100,0,0,'');");
        Exec(@"INSERT INTO items(id,type,source,source_id,title,search_text,created_at,updated_at,hidden,pinned,notes)
               VALUES(2,'github_star','github','https://github.com/Foo/Bar','B','B',200,200,0,1,'较长的一条笔记');");
        Exec("INSERT INTO tags(id,name,created_at) VALUES(1,'work',1);");
        Exec("INSERT INTO item_tags(item_id,tag_id,created_at) VALUES(2,1,1);");

        WriteVersion(5);
        new MigrationRunner(_factory).EnsureSchema();

        Assert.Equal(1, Scalar<long>("SELECT COUNT(*) FROM items;"));
        Assert.Equal(1, Scalar<long>("SELECT id FROM items;"));                    // 留最早那条
        Assert.Equal("https://github.com/foo/bar",
            Scalar<string>("SELECT source_id FROM items WHERE id=1;"));
        Assert.Equal(1, Scalar<long>("SELECT pinned FROM items WHERE id=1;"));      // 整组 OR
        Assert.Equal("较长的一条笔记", Scalar<string>("SELECT notes FROM items WHERE id=1;"));
        Assert.Equal(1, Scalar<long>("SELECT COUNT(*) FROM item_tags WHERE item_id=1 AND tag_id=1;"));
    }

    /// <summary>
    /// v6 幂等：所有行已在规范形时，第二次跑不得再改任何东西
    /// （"改键数为 0 ⇒ 连索引重建都不触发"这条也顺带钉住——它防的是每次启动都全表重算）。
    /// </summary>
    [Fact]
    public void EnsureSchema_MigrateV6_IsIdempotent()
    {
        new MigrationRunner(_factory).EnsureSchema();
        Exec(@"INSERT INTO items(id,type,source,source_id,title,search_text,created_at,updated_at,hidden,pinned,notes)
               VALUES(1,'github_star','github','https://www.github.com/Foo/Bar','A','A',100,100,0,0,'');");

        WriteVersion(5);
        new MigrationRunner(_factory).EnsureSchema();
        var first = Scalar<string>("SELECT source_id FROM items WHERE id=1;");

        WriteVersion(5);                                            // 再逼 v6 跑一遍
        new MigrationRunner(_factory).EnsureSchema();

        Assert.Equal("https://github.com/foo/bar", first);
        Assert.Equal(first, Scalar<string>("SELECT source_id FROM items WHERE id=1;"));
        Assert.Equal(1, Scalar<long>("SELECT COUNT(*) FROM items;"));
    }

    /// <summary>
    /// v6 的边界：<b>非 http(s) 的键一个都不许动</b>。
    /// <c>trending-bookmark:{owner/repo}</c> 是热榜"收进收藏"的业务键，删除按
    /// <c>(source, source_id)</c> 精确匹配（<c>TrendingItemDraft</c> 的注释就是为此写的）；
    /// 本地文件键是 <c>LocalFileIdentity</c> 的 16 位十六进制串。两者一旦被顺手折成小写，
    /// 下一次"取消收藏"就匹配不到行——<b>条目再也删不掉</b>，比多出一行严重得多。
    /// </summary>
    [Fact]
    public void EnsureSchema_MigrateV6_LeavesNonHttpSourceIdsAlone()
    {
        new MigrationRunner(_factory).EnsureSchema();
        var fileKey = StarMark.Abstractions.LocalFileIdentity.SourceIdForPath(@"C:\Users\me\报告.txt");

        Exec(@"INSERT INTO items(id,type,source,source_id,title,search_text,created_at,updated_at,hidden,pinned,notes)
               VALUES(1,'bookmark','trending-bookmark:PowerShell/PowerShell','PowerShell/PowerShell','A','A',100,100,0,0,'');");
        Exec(@"INSERT INTO items(id,type,source,source_id,title,search_text,created_at,updated_at,hidden,pinned,notes)
               VALUES(2,'file','local',@k,'B','B',100,100,0,0,'');"
             .Replace("@k", "'" + fileKey + "'"));

        WriteVersion(5);
        new MigrationRunner(_factory).EnsureSchema();

        Assert.Equal("PowerShell/PowerShell", Scalar<string>("SELECT source_id FROM items WHERE id=1;"));
        Assert.Equal(fileKey, Scalar<string>("SELECT source_id FROM items WHERE id=2;"));
        Assert.Equal(2, Scalar<long>("SELECT COUNT(*) FROM items;"));   // 没被错并
    }
}
