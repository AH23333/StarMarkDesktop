#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Core.Backup;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 备份盘点契约（批次 IM）。
/// <para>
/// 设置卡片一直写着"应用会自己备份""任何恢复都可回滚"，但界面上看不到"有哪些份"，
/// 恢复指定某一份得自己敲文件名。这里钉住盘点本身的四条性质：
/// 类别判据与写入侧同源、时间倒序且平局稳定、只看 .json、上限生效且坏目录不抛。
/// </para>
/// </summary>
public sealed class BackupListingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"starmark_bk_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* 临时目录清不掉不影响断言 */ }
    }

    private string Touch(string name, int minutesAgo, long bytes = 1024)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, new string('x', (int)bytes));
        var stamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo);
        File.SetLastWriteTimeUtc(path, stamp);
        return path;
    }

    // ==================== 类别判据 ====================

    [Theory]
    [InlineData("auto-20260101-120000.json", AutoBackupPolicy.BackupKind.Auto)]
    [InlineData("pre-restore-20260101-120000.json", AutoBackupPolicy.BackupKind.PreRestore)]
    [InlineData("starmark-backup-20260101-120000.json", AutoBackupPolicy.BackupKind.Manual)]
    [InlineData("随手改的名.json", AutoBackupPolicy.BackupKind.Manual)]
    [InlineData("AUTO-20260101-120000.json", AutoBackupPolicy.BackupKind.Auto)]           // 大写仍算自动件（与 IsAuto 同口径）
    [InlineData("PRE-Restore-x.json", AutoBackupPolicy.BackupKind.PreRestore)]
    [InlineData(@"C:\somewhere\deep\auto-x.json", AutoBackupPolicy.BackupKind.Auto)]      // 传整路径也按文件名判
    public void Classify_FollowsPrefix(string name, AutoBackupPolicy.BackupKind expected)
        => Assert.Equal(expected, AutoBackupPolicy.Classify(name));

    [Fact]
    public void Classify_AutoPrefixDoesNotSwallowSnapshotName()
    {
        // 「恢复前快照」是唯一"用户点它来撤销上一步"的一类，判错方向的代价最大 ⇒ 它必须赢过 auto-。
        Assert.Equal(AutoBackupPolicy.BackupKind.PreRestore, AutoBackupPolicy.Classify("pre-restore-a.json"));
        Assert.False(AutoBackupPolicy.IsAuto("pre-restore-a.json"));   // 回滚点永不在自动清理候选里
    }

    [Fact]
    public async Task WriterAndClassifierShareOnePrefix()
    {
        // 落件侧与分类侧必须引用同一个常量。分叉了的后果不是"显示不对"这么轻：
        // 回滚点会被当成"手动导出"，而自动清理的豁免是按前缀判的 ⇒ 名字一错就可能被删。
        var snapDir = Path.Combine(_dir, "snapshots");
        var db = Path.Combine(_dir, "writer.db");
        BackupService.SnapshotDirectoryOverride = snapDir;
        try
        {
            var factory = new DbConnectionFactory(db);
            new MigrationRunner(factory).EnsureSchema();
            var svc = new BackupService(new BackupRepository(factory));

            var path = await svc.WriteSnapshotAsync(CancellationToken.None);

            var row = Assert.Single(BackupService.EnumerateBackups(snapDir));
            Assert.Equal(AutoBackupPolicy.BackupKind.PreRestore, row.Kind);
            Assert.Equal(Path.GetFileName(path), row.FileName);
            Assert.True(row.LengthBytes > 0);
        }
        finally
        {
            BackupService.SnapshotDirectoryOverride = null;
            foreach (var p in new[] { db, db + "-wal", db + "-shm" })
                try { File.Delete(p); } catch { /* 临时库清不掉不影响断言 */ }
        }
    }

    // ==================== 盘点本身 ====================

    [Fact]
    public void EnumerateBackups_Empty_ReturnsNothingWhenDirectoryMissing()
    {
        Assert.Empty(BackupService.EnumerateBackups(Path.Combine(_dir, "never-created")));
    }

    [Fact]
    public void EnumerateBackups_NewestFirst_AndSizeAndKindCarried()
    {
        Touch("auto-old.json", minutesAgo: 600);
        var mid = Touch("auto-mid.json", minutesAgo: 300, bytes: 2048);
        Touch("pre-restore-new.json", minutesAgo: 60);

        var list = BackupService.EnumerateBackups(_dir);

        Assert.Equal(3, list.Count);
        Assert.Equal("pre-restore-new.json", list[0].FileName);
        Assert.Equal(AutoBackupPolicy.BackupKind.PreRestore, list[0].Kind);
        Assert.Equal("auto-mid.json", list[1].FileName);
        Assert.Equal(2048, list[1].LengthBytes);
        Assert.Equal(mid, list[1].Path);
        Assert.Equal("auto-old.json", list[2].FileName);
    }

    [Fact]
    public void EnumerateBackups_SameTimestamp_TieBreakIsStableByName()
    {
        // 同一秒内落两份是常态（连续两次导入）。没有稳定次级序的话，每次刷新上下抖，
        // 用户会以为列表在乱动，也就没法按位置点"恢复"。
        Touch("auto-a.json", minutesAgo: 10);
        Touch("auto-b.json", minutesAgo: 10);
        Touch("auto-c.json", minutesAgo: 10);

        var first = BackupService.EnumerateBackups(_dir).Select(b => b.FileName).ToList();
        var second = BackupService.EnumerateBackups(_dir).Select(b => b.FileName).ToList();

        Assert.Equal(first, second);
        Assert.Equal(new[] { "auto-c.json", "auto-b.json", "auto-a.json" }, first);
    }

    [Fact]
    public void EnumerateBackups_IgnoresNonJsonFiles()
    {
        Touch("auto-x.json", minutesAgo: 5);
        var stray = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(stray, "不是备份");

        Assert.Equal(new[] { "auto-x.json" }, BackupService.EnumerateBackups(_dir).Select(b => b.FileName));
    }

    [Fact]
    public void EnumerateBackups_RespectsMax()
    {
        for (var i = 0; i < 12; i++) Touch($"auto-{i:00}.json", minutesAgo: i);

        Assert.Equal(5, BackupService.EnumerateBackups(_dir, max: 5).Count);
        Assert.Equal(12, BackupService.EnumerateBackups(_dir, max: 50).Count);
        Assert.Empty(BackupService.EnumerateBackups(_dir, max: 0));
    }

    [Fact]
    public void EnumerateBackups_TimestampIsUtcNotLocalWallClock()
    {
        // 显式回归：LastWriteTimeUtc 当 DateTimeOffset 隐式用会按本地时区解释 ⇒ 列表上差出一个时区。
        var path = Touch("auto-tz.json", minutesAgo: 0);
        var stamp = new DateTimeOffset(new FileInfo(path).LastWriteTimeUtc, TimeSpan.Zero);

        var row = Assert.Single(BackupService.EnumerateBackups(_dir));
        Assert.Equal(stamp.ToUniversalTime(), row.ModifiedUtc.ToUniversalTime());
        Assert.Equal(TimeSpan.Zero, row.ModifiedUtc.Offset);
    }
}
