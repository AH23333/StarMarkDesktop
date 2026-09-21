#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
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

    [Fact]
    public void Open_EnablesForeignKeys_OnEveryConnection()
    {
        // EI：Open:39 每连接下发 `PRAGMA foreign_keys=ON`。SQLite 该开关默认【关】、且是 per-connection
        // 设置——只在建库时设一次无效，故每个 factory.Open() 都必须重下。它是 items/item_tags/activity 上
        // `ON DELETE CASCADE`（Schema:89/90/104）真正生效的唯一前提。静默后果：漏设→删条目/重复清理时
        // item_tags 关联行残留成孤儿→幽灵标签计数、已删条目链接复现，且不抛任何异常。
        // 既有本文件测只断 journal_mode=wal，从不看 foreign_keys→这条配置此前完全无护栏。
        var dir = Path.Combine(Path.GetTempPath(), "starmark-fk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var factory = new DbConnectionFactory(Path.Combine(dir, "t.db"));
            using var conn = factory.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys;";
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));   // 1=ON；若漏设→0，级联静默失效
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task FkEnforcementActive_DeleteItem_CascadesItemTagLink()
    {
        // EI②：把 EI① 的 PRAGMA 落到功能面——FK 开 → items 删除确实 cascade 清 item_tags。
        // 走真实仓储链路（非手搓裸 SQL，免测到 SQLite 本身）：建库→upsert 条目→AddTagAsync 造关联→
        // DeleteBySourceIdAsync 删条目→关联行须随 cascade 归零。
        // 直面一处既有盲区：MigrationRunner v4 合并测只断"保留者的标签挪到了 item_id=1"，
        // 从不验证"被删副本 item_id=2 的旧关联行已清空"→即便 FK 关、级联失效、关联残留也照样通过。
        var dir = Path.Combine(Path.GetTempPath(), "starmark-fkcascade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "t.db");
        try
        {
            var factory = new DbConnectionFactory(dbPath);
            new MigrationRunner(factory).EnsureSchema();
            var repo = new ItemRepository(factory);
            var item = new Item
            {
                Type = ItemType.Bookmark,
                Source = "test",
                SourceId = "cx1",
                Title = "带标签条目",
                Uri = "https://example.com/cx1",
            };
            await repo.UpsertAsync(new[] { item }, CancellationToken.None);
            await repo.AddTagAsync(item.Id, "标签X", CancellationToken.None);
            Assert.Equal(1L, CountItemTagRows(factory));   // 前置：关联已建

            await repo.DeleteBySourceIdAsync("test", "cx1", CancellationToken.None);
            Assert.Equal(0L, CountItemTagRows(factory));   // FK=ON → item_tags 级联清空（否则孤儿残留）
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private static long CountItemTagRows(DbConnectionFactory factory)
    {
        using var conn = factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM item_tags;";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}
