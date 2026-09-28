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
    private readonly List<string> _siblingDirs = new();

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
        foreach (var dir in _siblingDirs)
            try { Directory.Delete(dir, recursive: true); } catch { }
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

    /// <summary>
    /// "没改过设置"那一档的间隔。取自 <see cref="AutoBackupPolicy.DefaultIntervalHours"/> 本身
    /// ——生产代码的脏值回落用的就是这同一个常量，所以这里不可能与它对不上。
    /// </summary>
    private static readonly TimeSpan DefaultGap = TimeSpan.FromHours(AutoBackupPolicy.DefaultIntervalHours);

    private static bool DueDefault(DateTimeOffset now, DateTimeOffset? newestAutoUtc)
        => AutoBackupPolicy.ShouldRun(now, newestAutoUtc, enabled: true, AutoBackupPolicy.DefaultIntervalHours);

    /// <summary>按"没改过设置"跑一次（开 + 默认档）。间隔没有默认参数：谁排程谁就说清是哪个档。</summary>
    private static Task<string?> RunDefaultAsync(BackupService svc)
        => svc.RunAutoBackupAsync(enabled: true, intervalHours: AutoBackupPolicy.DefaultIntervalHours);

    // ==================== ShouldRun：间隔与时钟 ====================

    [Fact]
    public void ShouldRun_NoPreviousBackup_Runs()
        => Assert.True(DueDefault(DateTimeOffset.UtcNow, null));

    [Fact]
    public void ShouldRun_ExactlyAtGap_Runs_ButOneSecondYoungerDoesNot()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(DueDefault(now, now - DefaultGap));
        Assert.False(DueDefault(now, now - DefaultGap + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ShouldRun_FutureTimestampBeyondGap_StillRuns_ButSmallDriftDoesNot()
    {
        // 最新一份的时间戳在未来（用户改系统时钟、机器时钟先快后准、目录被云盘从别的机器同步进来）：
        // 带符号差值恒为负 ⇒ 写成 now-newest>=gap 会让备份一直停摆到真实时间追平那个未来时刻。
        var now = DateTimeOffset.UtcNow;
        Assert.True(DueDefault(now, now.AddDays(3)));
        Assert.True(DueDefault(now, now + DefaultGap));
        // 小于一个间隔的向前漂移仍视作"刚备份过"，否则每次唤起都会重写一份、保留窗口被刷掉
        Assert.False(DueDefault(now, now + DefaultGap - TimeSpan.FromSeconds(1)));
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
        Assert.Null(await RunDefaultAsync(NewBackup()));
        Assert.Empty(AutoFiles());
    }

    [Fact]
    public async Task RunAutoBackup_WritesRestorableFile_ThenSecondRunSkips()
    {
        await SeedItemAsync();
        var svc = NewBackup();

        var path = await RunDefaultAsync(svc);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.StartsWith(AutoBackupPolicy.Prefix, Path.GetFileName(path!), StringComparison.Ordinal);
        Assert.Single(AutoFiles());

        // 刚落盘就再跑一次：间隔未到 ⇒ 跳过（否则每次唤起都会刷掉保留窗口）
        Assert.Null(await RunDefaultAsync(svc));
        Assert.Single(AutoFiles());

        // 落盘的那份必须是可导入的合法备份（校验和/版本闸门全过），否则自动件只是假安全感
        var env = await BackupService.ReadAsync(path!);
        Assert.Contains(env.Payload.Items, i => i.SourceId == "auto-1");
    }

    [Fact]
    public async Task RunAutoBackup_AfterGap_WritesAndPrunesToKeep_Newest()
    {
        await SeedItemAsync();

        // 9 份旧的自动件（都比默认间隔老，故本次会落第 10 份）+ 2 份非自动件
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

        var written = await RunDefaultAsync(NewBackup());
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
        var path = await RunDefaultAsync(NewBackup());
        Assert.NotNull(path);

        var stamp = Path.GetFileNameWithoutExtension(path!)["auto-".Length..];
        var parsed = DateTimeOffset.ParseExact(stamp, "yyyyMMdd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.True((DateTimeOffset.UtcNow - parsed).Duration() < TimeSpan.FromMinutes(5),
            $"文件名时间戳解析出 {parsed:O}，与当前 UTC 相差 {DateTimeOffset.UtcNow - parsed}");
    }

    // ==================== 批次 BK：间隔变成用户可调 ====================

    /// <summary>默认档必须在档位表里：否则设置页回灌时"选不中当前值"（下拉空着）。</summary>
    [Fact]
    public void IntervalOptions_ContainTheDefault()
        => Assert.Contains(AutoBackupPolicy.DefaultIntervalHours, AutoBackupPolicy.IntervalOptions);

    /// <summary>
    /// 文案与数值同处生成、同序：下拉里出现一个 <c>ClampInterval</c> 不认的档，
    /// 用户选了它之后落盘的是另一个间隔（护眼那边同样的分岔已经钉过一次）。
    /// </summary>
    [Fact]
    public void IntervalLabels_AreOnePerChoice_AndNameTheirNumber()
    {
        Assert.Equal(AutoBackupPolicy.IntervalOptions.Length, AutoBackupPolicy.IntervalLabels.Count);
        for (var i = 0; i < AutoBackupPolicy.IntervalOptions.Length; i++)
        {
            var hours = AutoBackupPolicy.IntervalOptions[i];
            var label = AutoBackupPolicy.IntervalLabels[i];
            Assert.StartsWith("每 ", label);
            Assert.Contains((hours < 24 ? hours : hours / 24).ToString(), label);
        }
    }

    [Theory]
    [InlineData(6)] [InlineData(24)] [InlineData(72)] [InlineData(168)]
    public void ClampInterval_KeepsEveryRealChoice(int hours)
        => Assert.Equal(hours, AutoBackupPolicy.ClampInterval(hours));

    [Theory]
    [InlineData(0)] [InlineData(-24)] [InlineData(1)] [InlineData(12)] [InlineData(25)] [InlineData(100000)]
    public void ClampInterval_SendsAnythingElseToTheDefault(int hours)
        // 夹到边界＝"存档坏了就每 6 小时写一份"或"每 7 天才一份"，两种都比回到大家认识的那一档更糟。
        => Assert.Equal(AutoBackupPolicy.DefaultIntervalHours, AutoBackupPolicy.ClampInterval(hours));

    [Theory]
    [InlineData(-1)] [InlineData(99)]
    public void IntervalAt_OutOfRangeIndexGivesTheDefaultNotTheNearestEdge(int index)
        // WinUI 的 SelectedIndex 在"还没选中"时就是 -1：夹到端点等于把一次误触发的保存变成 6 小时。
        => Assert.Equal(AutoBackupPolicy.DefaultIntervalHours, AutoBackupPolicy.IntervalAt(index));

    [Fact]
    public void IntervalRoundTrips_EveryChoiceThroughItsIndex()
    {
        for (var i = 0; i < AutoBackupPolicy.IntervalOptions.Length; i++)
        {
            Assert.Equal(i, AutoBackupPolicy.IntervalIndexOf(AutoBackupPolicy.IntervalOptions[i]));
            Assert.Equal(AutoBackupPolicy.IntervalOptions[i], AutoBackupPolicy.IntervalAt(i));
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(-5)] [InlineData(13)]
    public void IntervalIndexOf_DirtyValueLandsOnTheDefaultChoice(int hours)
    {
        var at = AutoBackupPolicy.IntervalIndexOf(hours);
        Assert.Equal(AutoBackupPolicy.DefaultIntervalHours, AutoBackupPolicy.IntervalAt(at));
    }

    /// <summary>
    /// 「保留 7 份」写死而间隔可调 ⇒ 能回溯多久必须跟着算出来给用户看（设置页那句账就取自这里）。
    /// 向上取整是"最长"，报短了会让用户以为旧件比实际更少。
    /// </summary>
    [Theory]
    [InlineData(6, 2)]       // 7×6=42h ≈ 1.75 天
    [InlineData(24, 7)]
    [InlineData(72, 21)]
    [InlineData(168, 49)]    // 7×7 天
    public void MaxLookbackDays_IsKeptCountTimesTheInterval(int hours, int expectedDays)
        => Assert.Equal(expectedDays, AutoBackupPolicy.MaxLookbackDays(hours));

    [Fact]
    public void ShouldRun_Disabled_NeverRuns_EvenWithNoPreviousBackup()
    {
        // 关掉之后"还偷偷落盘"是最难发现的一类不诚实：界面上明明写着已关闭。
        Assert.False(AutoBackupPolicy.ShouldRun(DateTimeOffset.UtcNow, null, enabled: false, intervalHours: 6));
        Assert.False(AutoBackupPolicy.ShouldRun(DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow - TimeSpan.FromDays(400), enabled: false, intervalHours: 6));
    }

    [Theory]
    [InlineData(6, 6, true)]       // 正好一个间隔：到点
    [InlineData(5, 6, false)]      // 差一小时：未到
    [InlineData(24, 168, false)]   // 选了每周档 ⇒ 一天前那份还算"新鲜"
    [InlineData(168, 168, true)]   // 正好一周：到点
    [InlineData(720, 168, true)]   // 三十天没落过盘：按每周档当然该补
    public void ShouldRun_UsesTheUsersInterval(int hoursOld, int intervalHours, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(expected, AutoBackupPolicy.ShouldRun(now, now - TimeSpan.FromHours(hoursOld), true, intervalHours));
    }

    [Fact]
    public void ShouldRun_DirtyIntervalFallsBackBeforeComparing()
    {
        var now = DateTimeOffset.UtcNow;
        // 0/负数不会让"每次都跑"（那等于把一处数据损坏放大成每次唤起都写盘），而是回默认 24h。
        Assert.False(AutoBackupPolicy.ShouldRun(now, now - TimeSpan.FromHours(12), true, 0));
        Assert.True(AutoBackupPolicy.ShouldRun(now, now - TimeSpan.FromDays(2), true, -7));
    }

    [Fact]
    public async Task RunAutoBackup_Disabled_WritesNothingEvenWithContent()
    {
        await SeedItemAsync();                       // 有内容 + 一份自动件都没有＝按判据本该落盘
        Assert.Null(await NewBackup().RunAutoBackupAsync(enabled: false, intervalHours: 6));
        Assert.Empty(AutoFiles());
    }

    [Fact]
    public async Task RunAutoBackup_HonoursTheUsersInterval()
    {
        await SeedItemAsync();
        WriteFake("auto-old-3-days.json", TimeSpan.FromDays(3));

        Assert.Null(await NewBackup().RunAutoBackupAsync(true, 168));    // 每周档：3 天前的还算新鲜
        Assert.Single(AutoFiles());

        var written = await NewBackup().RunAutoBackupAsync(true, 24);    // 每天档：该补一份
        Assert.NotNull(written);
        Assert.True(File.Exists(written));
        Assert.Equal(2, AutoFiles().Length);
    }

    // ==================== 批次 BK：删掉某一份（先过目录/类型那道闸） ====================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeleteRefusal_RejectsEmptyPath(string? path)
        => Assert.NotNull(AutoBackupPolicy.DeleteRefusal(path, _snapshotDir));

    [Theory]
    [InlineData("auto-x.json")]
    [InlineData("pre-restore-x.json")]
    [InlineData("starmark-backup-x.json")]
    [InlineData("starmark-backup-x.zip")]
    public void DeleteRefusal_AllowsBackupFilesInsideTheDirectory(string name)
        => Assert.Null(AutoBackupPolicy.DeleteRefusal(Path.Combine(_snapshotDir, name), _snapshotDir));

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("index.html")]
    [InlineData("no-extension")]
    public void DeleteRefusal_RejectsNonBackupFilesEvenInsideTheDirectory(string name)
        => Assert.Contains("不是备份件", AutoBackupPolicy.DeleteRefusal(Path.Combine(_snapshotDir, name), _snapshotDir));

    [Fact]
    public void DeleteRefusal_RejectsSiblingDirectoryWithTheSamePrefix()
    {
        // 这条钉的是"用 StartsWith 判目录"的那个写法：backups-evil 的前缀与 backups 完全一样，
        // 一旦判据退化成前缀比较，落在隔壁目录的文件就成了"程序可代删"的东西。
        var inside = Path.Combine(_snapshotDir + "-evil", "x.json");
        var reason = AutoBackupPolicy.DeleteRefusal(inside, _snapshotDir);
        Assert.NotNull(reason);
        Assert.Contains("不在备份目录里", reason);
    }

    [Fact]
    public void DeleteRefusal_RejectsParentDirectoryEscape()
    {
        var escaped = Path.Combine(_snapshotDir, "..", "x.json");
        Assert.Contains("不在备份目录里", AutoBackupPolicy.DeleteRefusal(escaped, _snapshotDir));
    }

    [Fact]
    public void DeleteRefusal_IsCaseInsensitiveAboutTheDirectory()
    {
        // 同一目录换个大小写（用户从别处拷路径、或云盘改过 casing）不该被当成越界。
        var flipped = _snapshotDir.ToUpperInvariant();
        Assert.Null(AutoBackupPolicy.DeleteRefusal(Path.Combine(flipped, "auto-x.json"), _snapshotDir));
    }

    [Fact]
    public void DeleteBackup_RemovesABackupFileAndReturnsAReceipt()
    {
        var path = WriteFake("auto-todelete.json", TimeSpan.FromHours(1));
        var receipt = BackupService.DeleteBackup(path);
        Assert.Contains("已删除", receipt);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeleteBackup_RefusesAndLeavesTheFileAlone()
    {
        // 拒绝时必须真的没删——回执说了"不删"而文件已经没了，是最坏的一种回答。
        var path = WriteFake("readme.txt", TimeSpan.FromHours(1));
        Assert.Contains("不是备份件", BackupService.DeleteBackup(path));
        Assert.True(File.Exists(path));

        var evil = EnsureSiblingDirWithTheSamePrefix();
        var outside = Path.Combine(evil, "important.json");
        File.WriteAllText(outside, "{}");
        Assert.Contains("不在备份目录里", BackupService.DeleteBackup(outside));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void DeleteBackup_EmptyPathRefusesWithoutTouchingTheDisk()
    {
        var keep = WriteFake("auto-keepme.json", TimeSpan.FromHours(1));
        Assert.Contains("没指定", BackupService.DeleteBackup(null));
        Assert.Contains("没指定", BackupService.DeleteBackup("   "));
        Assert.True(File.Exists(keep));
    }

    /// <summary>造一个名字以备份目录打头的隔壁目录（Dispose 收掉，别让单测在 %TEMP% 里留垃圾）。</summary>
    private string EnsureSiblingDirWithTheSamePrefix()
    {
        var evil = _snapshotDir + "-evil";
        _siblingDirs.Add(evil);
        Directory.CreateDirectory(evil);
        return evil;
    }
}
