#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Core.Backup;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 每日自动备份（P-51）的策略与落地契约。
/// <para>
/// 设置页原文案「请定期导出备份」把"别丢数据"托付给人的记性；本批改成程序按天自己落盘。
/// 自动落盘带<b>删除</b>语义，风险高于它保护的数据，故这里钉死三件事：
/// ① 间隔判定（含时钟被往回调/未来时间戳不能让备份永久停摆）；
/// ② <b>删除候选只有 <c>auto-</c> 前缀</b>——手动导出件与 <c>pre-restore-</c> 快照永不被程序删；
/// ③ 空库不落盘（否则首次运行的空件会把下次备份推后 24h，真正的内容反倒没备份）。
/// </para>
/// </summary>
public sealed class AutoBackupPolicyTests : IDisposable
{
    private readonly string _snapshotDir;
    private readonly string _db;

    public AutoBackupPolicyTests()
    {
        _snapshotDir = Path.Combine(Path.GetTempPath(), $"starmark_autobk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_snapshotDir);
        _db = Path.Combine(Path.GetTempPath(), $"starmark_autobk_{Guid.NewGuid():N}.db");
        BackupService.SnapshotDirectoryOverride = _snapshotDir;
    }

    public void Dispose()
    {
        BackupService.SnapshotDirectoryOverride = null;
        try { Directory.Delete(_snapshotDir, recursive: true); } catch { /* 临时目录清理失败不影响断言 */ }
        foreach (var p in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(p); } catch { }
    }

    private BackupService NewBackup()
    {
        var factory = new DbConnectionFactory(_db);
        new MigrationRunner(factory).EnsureSchema();
        return new BackupService(new BackupRepository(factory));
    }

    private async Task SeedItemAsync()
    {
        var factory = new DbConnectionFactory(_db);
        new MigrationRunner(factory).EnsureSchema();
        await new ItemRepository(factory).UpsertAsync(new[]
        {
            new Item
            {
                Type = ItemType.Bookmark,
                Source = "test",
                SourceId = "auto-1",
                Title = "不可重建的手写笔记",
                Uri = "https://example.com/auto-1",
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            },
        }, CancellationToken.None);
    }

    private string[] AutoFiles()
        => Directory.GetFiles(_snapshotDir, AutoBackupPolicy.Prefix + "*.json");

    /// <summary>造一份自动件并把写盘时间推到 <paramref name="age"/> 之前（模拟"上次备份已在 N 天前"）。</summary>
    private string WriteFake(string name, TimeSpan age, string? content = null)
    {
        var path = Path.Combine(_snapshotDir, name);
        File.WriteAllText(path, content ?? "{}");
        File.SetLastWriteTimeUtc(path, (DateTimeOffset.UtcNow - age).UtcDateTime);
        return path;
    }

    // ==================== ShouldRun：间隔与时钟 ====================

    [Fact]
    public void ShouldRun_NoPreviousBackup_Runs()
        => Assert.True(AutoBackupPolicy.ShouldRun(DateTimeOffset.UtcNow, null));

    [Fact]
    public void ShouldRun_ExactlyAtGap_Runs_ButOneSecondYoungerDoesNot()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(AutoBackupPolicy.ShouldRun(now, now - AutoBackupPolicy.MinGap));
        Assert.False(AutoBackupPolicy.ShouldRun(now, now - AutoBackupPolicy.MinGap + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ShouldRun_FutureTimestampBeyondGap_StillRuns_ButSmallDriftDoesNot()
    {
        // 最新一份的时间戳在未来（用户改系统时钟、机器时钟先快后准、目录被云盘从别的机器同步进来）：
        // 带符号差值恒为负 ⇒ 写成 now-newest>=MinGap 会让备份一直停摆到真实时间追平那个未来时刻。
        var now = DateTimeOffset.UtcNow;
        Assert.True(AutoBackupPolicy.ShouldRun(now, now.AddDays(3)));
        Assert.True(AutoBackupPolicy.ShouldRun(now, now + AutoBackupPolicy.MinGap));
        // 小于一个间隔的向前漂移仍视作"刚备份过"，否则每次唤起都会重写一份、保留窗口被刷掉
        Assert.False(AutoBackupPolicy.ShouldRun(now, now + AutoBackupPolicy.MinGap - TimeSpan.FromSeconds(1)));
    }

    // ==================== IsAuto / PrunePlan：删除边界 ====================

    [Theory]
    [InlineData("auto-20260922-010203.json", true)]
    [InlineData("AUTO-20260922-010203.json", true)]          // 前缀判定不看大小写（Windows 文件名本就大小写不敏感）
    [InlineData(@"C:\Users\x\backups\auto-20260922-010203.json", true)]
    [InlineData("pre-restore-20260922-010203.json", false)]  // 恢复前快照：绝不能进删除候选
    [InlineData("starmark-backup-20260922.json", false)]     // 手动导出件：同上
    [InlineData("20260922-auto.json", false)]                // 前缀不在开头＝不是自动件
    public void IsAuto_OnlyAutoPrefixQualifies(string path, bool expected)
        => Assert.Equal(expected, AutoBackupPolicy.IsAuto(path));

    [Fact]
    public void PrunePlan_KeepsNewestDropsOlderAutos_IgnoringNonAutoEvenIfOldest()
    {
        var now = DateTimeOffset.UtcNow;
        (string Path, DateTimeOffset ModifiedUtc) F(string name, int daysAgo)
            => (name, now.AddDays(-daysAgo));

        var files = new List<(string, DateTimeOffset)>
        {
            F("auto-0.json", 0), F("auto-1.json", 1), F("auto-2.json", 2), F("auto-3.json", 3),
            F("auto-4.json", 4), F("auto-5.json", 5), F("auto-6.json", 6), F("auto-7.json", 7),
            F("auto-8.json", 8),
            F("pre-restore-9.json", 9),      // 比最旧的自动件还旧，但非 auto- 前缀 ⇒ 永不候选
            F("manual-export-10.json", 10),
        };

        var doomed = AutoBackupPolicy.PrunePlan(files);

        // 9 份自动件保留最新 7 份 ⇒ 只有最旧的两份 auto- 出局；两份更旧的非自动件必须原样留下
        Assert.Equal(new[] { "auto-7.json", "auto-8.json" }.OrderBy(x => x), doomed.OrderBy(x => x));
        Assert.DoesNotContain(doomed, p => !AutoBackupPolicy.IsAuto(p));
    }

    [Fact]
    public void PrunePlan_OrdersByModifiedTimeNotFileNameAndHandlesEmpty()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Empty(AutoBackupPolicy.PrunePlan(Array.Empty<(string, DateTimeOffset)>()));

        // 名字序与时间序相反：唯一能证明排序取的是 ModifiedUtc 而非文件名的形状
        var files = new[]
        {
            ("auto-z.json", now.AddDays(-1)),
            ("auto-a.json", now.AddDays(-9)),
            ("auto-m.json", now.AddDays(-5)),
        };
        Assert.Equal(new[] { "auto-a.json" }, AutoBackupPolicy.PrunePlan(files, keep: 2));
    }

    [Fact]
    public void PrunePlan_KeepZero_ClearsEveryAutoAndStillSparesOthers()
    {
        var files = new[]
        {
            ("auto-1.json", DateTimeOffset.UtcNow),
            ("auto-2.json", DateTimeOffset.UtcNow),
            ("pre-restore-1.json", DateTimeOffset.UtcNow),
        };
        Assert.Equal(
            new[] { "auto-1.json", "auto-2.json" }.OrderBy(x => x),
            AutoBackupPolicy.PrunePlan(files, keep: 0).OrderBy(x => x));
    }

    // ==================== RunAutoBackupAsync：端到端落地 ====================

    [Fact]
    public async Task RunAutoBackup_EmptyDatabase_WritesNothing()
    {
        // 空库落一份"空备份"不只是噪声：它会把"最近一份"的时钟推后 24h，
        // 于是用户第一次真正写入数据后的当天反而没有备份。
        Assert.Null(await NewBackup().RunAutoBackupAsync());
        Assert.Empty(AutoFiles());
    }

    [Fact]
    public async Task RunAutoBackup_WritesRestorableFile_ThenSecondRunSkips()
    {
        await SeedItemAsync();
        var svc = NewBackup();

        var path = await svc.RunAutoBackupAsync();
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.StartsWith(AutoBackupPolicy.Prefix, Path.GetFileName(path!), StringComparison.Ordinal);
        Assert.Single(AutoFiles());

        // 刚落盘就再跑一次：间隔未到 ⇒ 跳过（否则每次唤起都会刷掉保留窗口）
        Assert.Null(await svc.RunAutoBackupAsync());
        Assert.Single(AutoFiles());

        // 落盘的那份必须是可导入的合法备份（校验和/版本闸门全过），否则自动件只是假安全感
        var env = await BackupService.ReadAsync(path!);
        Assert.Contains(env.Payload.Items, i => i.SourceId == "auto-1");
    }

    [Fact]
    public async Task RunAutoBackup_AfterGap_WritesAndPrunesToKeep_Newest()
    {
        await SeedItemAsync();

        // 9 份旧的自动件（都比 MinGap 老，故本次会落第 10 份）+ 2 份非自动件
        var oldest = WriteFake("auto-old1.json", TimeSpan.FromDays(20));
        WriteFake("auto-old2.json", TimeSpan.FromDays(19));
        WriteFake("auto-old3.json", TimeSpan.FromDays(18));
        WriteFake("auto-old4.json", TimeSpan.FromDays(17));
        WriteFake("auto-old5.json", TimeSpan.FromDays(16));
        WriteFake("auto-old6.json", TimeSpan.FromDays(15));
        WriteFake("auto-old7.json", TimeSpan.FromDays(14));
        WriteFake("auto-old8.json", TimeSpan.FromDays(13));
        WriteFake("auto-old9.json", TimeSpan.FromDays(12));
        var preRestore = WriteFake("pre-restore-20200101-000000.json", TimeSpan.FromDays(400));
        var manual = WriteFake("starmark-backup-20200101.json", TimeSpan.FromDays(400));

        var written = await NewBackup().RunAutoBackupAsync();
        Assert.NotNull(written);

        var autos = AutoFiles();
        Assert.Equal(AutoBackupPolicy.Keep, autos.Length);
        Assert.Contains(written!, autos);                                    // 最新一份必在保留集里
        Assert.DoesNotContain(oldest, autos);                                 // 最旧的被裁掉
        Assert.True(File.Exists(preRestore));                                 // 恢复前快照不受自动清理影响
        Assert.True(File.Exists(manual));                                     // 手动导出的备份不受自动清理影响
    }

    [Fact]
    public async Task RunAutoBackup_FileNameStampIsInvariantGregorianAndUtc()
    {
        // 佛历（th-TH）/希吉来历（ar-SA）区域设置下不锁 InvariantCulture 会得到错误年份（2569 / 1447），
        // 回滚点按名排序的时间序随之失效；不锁 UTC 则名字与真实时刻差出一个时区。
        // 名字里只有 yyyyMMdd-HHmmss 一串，两个错都只能靠"解析回来对不对"钉住——与 WriteSnapshotAsync 同口径。
        await SeedItemAsync();
        var path = await NewBackup().RunAutoBackupAsync();
        Assert.NotNull(path);

        var stamp = Path.GetFileNameWithoutExtension(path!)["auto-".Length..];
        var parsed = DateTimeOffset.ParseExact(stamp, "yyyyMMdd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.True((DateTimeOffset.UtcNow - parsed).Duration() < TimeSpan.FromMinutes(5),
            $"文件名时间戳解析出 {parsed:O}，与当前 UTC 相差 {DateTimeOffset.UtcNow - parsed}");
    }
}
