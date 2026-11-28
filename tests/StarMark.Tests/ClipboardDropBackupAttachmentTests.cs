#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Abstractions.Clipboard;
using StarMark.Abstractions.I18n;
using StarMark.Core.Backup;
using StarMark.Data;
using StarMark.Integrations.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 拖入文件那一族的<b>备份附件打包</b>（片292，§3-Q2 的 drops 版）的行为面。
/// <para>
/// 这一批的东西<b>几乎全都必须碰真文件系统才验得出来</b>：包里到底有几件、清单字节有没有变、
/// 换机之后文件到底落没落盘、本机已有的那件有没有被覆盖——这些都是"磁盘上的事实"。
/// </para>
/// <para>要钉住的裁决：① drops 那一族跟着同一份包走（<c>drops/</c> 前缀，store 不压缩）；
/// ② 行认了、件不在 ⇒ 计"缺"且不进包；③ 恢复只写<b>这一份备份的条目认领的名字</b>（白名单），
/// 且不覆盖本机已有；④ 那句回执按"这份包里到底带没带"分三档：带本体（四数分开报）／
/// 只带认领（dropManifestOnly）／包没开成（一句不提——没看过就没资格说）。</para>
/// </summary>
public sealed class ClipboardDropBackupAttachmentTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_dropbk_{Guid.NewGuid():N}.db");
    private readonly string _drops = Path.Combine(Path.GetTempPath(), $"starmark_dropbk_dir_{Guid.NewGuid():N}");
    private readonly string _backupDir = Path.Combine(Path.GetTempPath(), $"starmark_dropbk_bk_{Guid.NewGuid():N}");
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;
    private readonly BackupService _backup;

    /// <summary>本例真写出去的导出文件（Dispose 时清掉，含换名后的另一半）。</summary>
    private readonly System.Collections.Generic.List<string> _exports = new();

    public ClipboardDropBackupAttachmentTests()
    {
        Directory.CreateDirectory(_drops);
        Directory.CreateDirectory(_backupDir);
        // ⛔ 不改道＝往用户真实 clip-drops 目录里写测试件/删测试件（与 ClipAssets.FolderOverride 同一条纪律）。
        ClipDropAssets.FolderOverride = _drops;
        BackupService.SnapshotDirectoryOverride = _backupDir;
        _factory = new DbConnectionFactory(_db);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
        _backup = new BackupService(new BackupRepository(_factory));
    }

    public void Dispose()
    {
        ClipDropAssets.FolderOverride = null;
        BackupService.SnapshotDirectoryOverride = null;
        foreach (var p in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(p); } catch { }
        foreach (var d in new[] { _drops, _backupDir })
            try { Directory.Delete(d, true); } catch { }
        foreach (var f in _exports)
            try { if (File.Exists(f)) File.Delete(f); } catch { }
    }

    // ────────── 助手 ──────────

    private static DateTimeOffset At(int seconds)
        => new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.FromHours(8)).AddSeconds(seconds);

    /// <summary>名册形状的一颗名字（12 位哈希＋下划线＋原始名）——白名单与名册两道闸都认这个形状。</summary>
    private static string DropName(string tag)
        => ClipDropAssets.FileNameOf(ClipDropAssets.HashOf(Encoding.UTF8.GetBytes(tag)), tag + ".pdf");

    private async Task<Item> RecDropRowAsync(string tag, int at, params string[] names)
    {
        var draft = ClipboardEntry.Build($"C:\\件\\{tag}.txt", "Code", ClipboardEntry.FormatFiles, At(at));
        if (names.Length > 0)
            draft.ExtraJson = ClipboardEntry.WithDropFilesOf(draft.ExtraJson, names);
        return await _repo.RecordClipboardAsync(draft, CancellationToken.None);
    }

    private static void WriteDrop(string name, string content)
        => File.WriteAllText(Path.Combine(ClipDropAssets.Folder, name), content);

    private static string ReadDrop(string name) => File.ReadAllText(Path.Combine(ClipDropAssets.Folder, name));

    private void DropLocalFiles()
    {
        foreach (var f in Directory.GetFiles(_drops)) File.Delete(f);
    }

    /// <summary>要一份 .json 名字的目标路径（带附件导出会换成同名 .zip，返回的那才是实际写的）。</summary>
    private string Target(bool container = false)
    {
        var p = Path.Combine(_backupDir, $"out_{Guid.NewGuid():N}{(container ? ".zip" : ".json")}");
        _exports.Add(p);
        _exports.Add(BackupContainer.SwapExtension(p));
        return p;
    }

    /// <summary>包里的 <c>drops/</c> 成员：名字与各自字节数（顺手验"没有零字节成员冒充一件"）。</summary>
    private static (string[] Names, long[] Sizes, long[] Compressed) DropPackage(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var drops = zip.Entries
            .Where(e => e.FullName.StartsWith(BackupContainer.DropPrefix, StringComparison.Ordinal))
            .ToList();
        return (drops.Select(e => e.FullName[BackupContainer.DropPrefix.Length..]).ToArray(),
                drops.Select(e => e.Length).ToArray(),
                drops.Select(e => e.CompressedLength).ToArray());
    }

    // ==================== 导出侧 ====================

    [Fact]
    public async Task TwoRowsWithFourClaimedFiles_MakeOnePackageCarryingAllOfThem()
    {
        var a1 = DropName("甲一");
        var a2 = DropName("甲二");
        var b1 = DropName("乙一");
        var b2 = DropName("乙二");
        await RecDropRowAsync("甲", 0, a1, a2);
        await RecDropRowAsync("乙", 30, b1, b2);
        WriteDrop(a1, "甲一的本体");
        WriteDrop(a2, "甲二的本体");
        WriteDrop(b1, "乙一的本体");
        WriteDrop(b2, "乙二的本体");

        var export = await _backup.ExportWithClipImagesAsync(Target());

        // 用户要拷走的是"一个文件"：条目、认领名单与文件本体在同一份包里。
        Assert.True(BackupContainer.IsContainer(export.Path), "带拖入文件的导出必须写成 .zip");
        Assert.Equal(2, export.ItemCount);
        Assert.Equal(4, export.DropFiles);
        Assert.Equal(0, export.MissingDrops);
        Assert.Equal(0, export.FailedDrops);
        Assert.True(export.DropFileBytes > 0);
        var (names, sizes, compressed) = DropPackage(export.Path);
        Assert.Equal(new[] { a1, a2, b1, b2 }.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            names.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.All(sizes, s => Assert.True(s > 0, "包里不许有零字节成员冒充一件"));
        Assert.Equal(sizes, compressed);                        // zip(store)：件已压过或本就是二进制，再 deflate 白费 CPU
        Assert.Equal(4, BackupContainer.CountDropEntries(export.Path));
    }

    [Fact]
    public async Task ARowWhoseFilesAreAlreadyGone_IsCountedMissingAndStaysOutOfThePackage()
    {
        var kept = DropName("还在的那件");
        var gone = DropName("被删的那件");
        await RecDropRowAsync("甲", 0, kept, gone);
        WriteDrop(kept, "本体");
        // gone 那件被用户手删了：包里不该出现它的名字，但"行认了、件不在"这件事必须报出来。

        var export = await _backup.ExportWithClipImagesAsync(Target());

        Assert.Equal(1, export.MissingDrops);
        Assert.Equal(1, export.DropFiles);
        Assert.Equal(0, export.FailedDrops);
        Assert.Equal(new[] { kept }, DropPackage(export.Path).Names);
        // 行本体照旧导出
        Assert.Single((await BackupService.ReadAsync(export.Path)).Payload.Items);
    }

    // ==================== 恢复侧 ====================

    [Fact]
    public async Task RestoringOntoAFreshMachine_WritesEveryClaimedFileBackByteIdentical()
    {
        var a = DropName("件甲");
        var b = DropName("件乙");
        await RecDropRowAsync("甲", 0, a, b);
        WriteDrop(a, "甲的本体，带着一些字节");
        WriteDrop(b, "乙的本体，另一段字节");
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var env = await BackupService.ReadAsync(export.Path);
        DropLocalFiles();                          // 换机：条目回来了，件一个都没有

        var result = await _backup.RestoreAsync(env, RestoreMode.Replace, null, CancellationToken.None, export.Path);

        Assert.True(result.Success);
        Assert.Equal(2, result.DropFilesRestored);
        Assert.Equal(0, result.DropFilesSkipped);
        Assert.Equal(0, result.DropFilesFailed);
        Assert.Equal("甲的本体，带着一些字节", ReadDrop(a));     // 字节一致，不是"写了个空壳占名"
        Assert.Equal("乙的本体，另一段字节", ReadDrop(b));
        var carried = LocAssert.Arm(result.Message, 4);         // drops 那一句是恢复回执的第五档：{0}条数{1}组件{2}图片{3}画板{4}拖入文件
        Assert.Equal("settings.data.backup.dropCarried", LocAssert.KeyOf(carried));
        Assert.Equal(2, LocAssert.Arg(carried, 0));             // 包里带着 2 件
        Assert.Equal(2, LocAssert.Arg(carried, 1));             // 写回 2 件
        LocAssert.Silent(carried, 2);                           // 没跳过：那一档不许说话
        LocAssert.Silent(carried, 3);                           // 没失败
        LocAssert.Silent(carried, 4);                           // 没有行认领着包外的件
    }

    [Fact]
    public async Task RestoringOntoAMachineWithoutTheDropFolderStillWritesTheFilesBack()
    {
        // 换机演练的第一步在单测里就是"整目录不存在"：clip-drops 目录是第一次拖放时才建的
        // （TryWriteAsync 里那句 CreateDirectory），一台从没拖放过的机器上它根本不存在。
        var a = DropName("件甲");
        await RecDropRowAsync("甲", 0, a);
        WriteDrop(a, "本体");
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var env = await BackupService.ReadAsync(export.Path);

        Directory.Delete(_drops, true);
        Assert.False(Directory.Exists(_drops));

        var result = await _backup.RestoreAsync(env, RestoreMode.Replace, null, CancellationToken.None, export.Path);

        Assert.True(Directory.Exists(_drops));
        Assert.Equal(1, result.DropFilesRestored);
        Assert.Equal(0, result.DropFilesFailed);
        Assert.Equal("本体", ReadDrop(a));
    }

    [Fact]
    public async Task RestoringNeverOverwritesADropFileThisMachineAlreadyHas()
    {
        var a = DropName("件甲");
        var b = DropName("件乙");
        await RecDropRowAsync("甲", 0, a, b);
        WriteDrop(a, "包里那份旧内容");
        WriteDrop(b, "乙的本体");
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var env = await BackupService.ReadAsync(export.Path);

        // 本机这一件是用户自己放回来的（§3-Q6 第三类反向）：拿旧备份换掉它是净损失。
        DropLocalFiles();
        WriteDrop(a, "我自己那份更新的内容");

        var result = await _backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None, export.Path);

        Assert.Equal("我自己那份更新的内容", ReadDrop(a));
        Assert.Equal(1, result.DropFilesRestored);
        Assert.Equal(1, result.DropFilesSkipped);
        var skipped = LocAssert.Arm(LocAssert.Arm(result.Message, 4), 2);
        Assert.Equal("settings.data.backup.dropSkipped", LocAssert.KeyOf(skipped));
        Assert.Equal(1, LocAssert.Arg(skipped, 0));
    }

    [Fact]
    public async Task EntriesThatNoRowClaimsAreRefusedAndNothingLandsOutsideTheFolder()
    {
        var a = DropName("件甲");
        var b = DropName("件乙");
        await RecDropRowAsync("甲", 0, a, b);
        WriteDrop(a, "本体一");
        WriteDrop(b, "本体二");
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var env = await BackupService.ReadAsync(export.Path);

        // 手工往包里塞三条来路不明的成员：名册形状但没行认领、带 ..（Zip Slip）、带盘符形状。
        var unclaimed = DropName("没行认领的");
        using (var zip = ZipFile.Open(export.Path, ZipArchiveMode.Update))
        {
            foreach (var name in new[] { "drops/" + unclaimed, "drops/../escaped.pdf", "drops/C:\\evil.pdf" })
                using (var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.NoCompression).Open()))
                    w.Write("不该落盘");
        }
        var parent = Directory.GetParent(_drops)!.FullName;
        DropLocalFiles();          // 先当作"换机"：本机一件都没有，才看得出这三条越权成员到底落没落盘

        var result = await _backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None, export.Path);

        Assert.False(File.Exists(Path.Combine(_drops, unclaimed)));
        Assert.False(File.Exists(Path.Combine(parent, "escaped.pdf")));
        Assert.False(File.Exists(Path.Combine(_drops, "C:\\evil.pdf")));
        // 越权的那三条算"跳过"（不是写失败），合法那两件仍要写回——三个箱子不许混成一个数。
        Assert.Equal(2, result.DropFilesRestored);
        Assert.Equal(3, result.DropFilesSkipped);
        Assert.Equal(0, result.DropFilesFailed);
        Assert.Equal(new[] { a, b }.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            Directory.GetFiles(_drops).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task AHalfWrittenAttachmentIsCountedFailedNotSilentlySkipped()
    {
        var a = DropName("件甲");
        var b = DropName("件乙");
        await RecDropRowAsync("甲", 0, a, b);
        WriteDrop(a, "本体一");
        WriteDrop(b, "本体二");
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var env = await BackupService.ReadAsync(export.Path);

        DropLocalFiles();
        // 用"临时名的位置上站着一个目录"制造一次写不进去：File.Create 必然失败，而失败必须被数出来。
        Directory.CreateDirectory(Path.Combine(_drops, ClipDropAssets.TempNameOf(a)));

        var result = await _backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None, export.Path);

        Assert.Equal(1, result.DropFilesRestored);        // 件乙仍然回来了
        Assert.Equal(1, result.DropFilesFailed);          // 件甲要说"没能写入"，不许并进"跳过"
        Assert.False(File.Exists(Path.Combine(_drops, a)));
        var failed = LocAssert.Arm(LocAssert.Arm(result.Message, 4), 3);
        Assert.Equal("settings.data.backup.dropFailed", LocAssert.KeyOf(failed));
        Assert.Equal(1, LocAssert.Arg(failed, 0));
    }

    [Fact]
    public async Task DataOnlyBackupsTellDropRowsTheManifestOnlyTruth()
    {
        // 开关关着导出的那一份（ExportToFileAsync 只带清单）：条目在、认领名单在、本体不在。
        // 那句要说清"这份没带、认领还在"——那是"清单在、本体不在"，删行解不了，得重导带附件的那份。
        var a = DropName("件甲");
        await RecDropRowAsync("甲", 0, a);
        WriteDrop(a, "本体");
        var plain = await _backup.ExportToFileAsync(Target());
        var env = await BackupService.ReadAsync(plain);
        DropLocalFiles();

        var result = await _backup.RestoreAsync(env, RestoreMode.Replace, null, CancellationToken.None, plain);

        Assert.Equal(0, result.DropFilesRestored);
        var manifestOnly = LocAssert.Arm(result.Message, 4);
        Assert.Equal("settings.data.backup.dropManifestOnly", LocAssert.KeyOf(manifestOnly));
        Assert.Equal(1, LocAssert.Arg(manifestOnly, 0));
    }

    [Fact]
    public async Task APackageThatCannotBeOpenedStaysSilentAboutDrops()
    {
        // 清单已经读进来了，之后附件包才打不开（被云盘抽成占位件、正被别的程序独占、或刚被删掉）。
        // 这时"这份备份不带拖入文件本体"是没资格说的——我们连里面有什么都没看见（图片那句
        // clipNotOpened 的同一条理由，drops 这边按分叉三档走 silent）。
        var a = DropName("件甲");
        await RecDropRowAsync("甲", 0, a);
        WriteDrop(a, "本体");
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var env = await BackupService.ReadAsync(export.Path);
        DropLocalFiles();
        File.Delete(export.Path);

        var result = await _backup.RestoreAsync(env, RestoreMode.Replace, null, CancellationToken.None, export.Path);

        Assert.True(result.Success);                      // 条目本体照常被恢复，附件这一步失败不该连带
        Assert.Equal(0, result.DropFilesRestored);
        Assert.Equal(0, result.DropFilesSkipped);
        Assert.Equal(0, result.DropFilesFailed);
        LocAssert.Silent(result.Message, 4);              // 包没打开＝没资格说，一句不提
    }

    [Fact]
    public async Task RestoreWithoutASourcePathNeverTouchesTheDropFolder()
    {
        // 快照与每日自动件都是 .json：把它们的路径接进附件逻辑＝每次回滚都往 clip-drops 目录写文件。
        var a = DropName("件甲");
        await RecDropRowAsync("甲", 0, a);
        WriteDrop(a, "本体");
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var env = await BackupService.ReadAsync(export.Path);
        DropLocalFiles();

        var result = await _backup.RestoreAsync(env, RestoreMode.Replace);

        Assert.Equal(0, result.DropFilesRestored);
        Assert.Empty(Directory.GetFiles(_drops));
        LocAssert.Silent(result.Message, 4);              // 没人递路径＝那一档不许说话
    }

    // ==================== 闸门：单条上限那条线画在哪 ====================

    [Fact]
    public void TheSizeCeilingSharesTheBudgetTheDroppingSideUses()
    {
        // 2 GB 那颗线与拖放侧的兜底预算同值（TodoVirtualFilePolicy.FallbackBudgetBytes）而⛔ 照抄图片那 20 MB：
        // 一件拖入文件本来就可以远超一张截图，照抄图片的上限就会把"当初带得走的件"拒之门外。
        // 2 GB 的条目造不出来（单测里写一颗 2 GB 的临时文件不叫测试），所以"超线拒收"这一档
        // 按源码守：判据那一行真的在，且读的就是这颗常量。
        Assert.Equal(StarMark.Core.Widgets.TodoVirtualFilePolicy.FallbackBudgetBytes,
            BackupContainer.MaxDropEntryBytes);
        var extract = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.Core/Backup/BackupContainer.cs"),
            "ExtractDropsAsync(string path, HashSet<string> claimedNames, CancellationToken ct)");
        Assert.Contains("entry.Length > MaxDropEntryBytes", extract);
    }
}
