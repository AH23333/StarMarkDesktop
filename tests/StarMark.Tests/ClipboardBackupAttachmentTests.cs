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
using StarMark.Core.Backup;
using StarMark.Data;
using StarMark.Integrations.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// ClipIMG-P3-a（备份附件打包，§3-Q2 的 a 期）的行为面。
/// <para>
/// 这一批的东西<b>几乎全都必须碰真文件系统才验得出来</b>：包里到底有几张、清单字节有没有变、
/// 换机之后文件到底落没落盘、本机已有的那张有没有被覆盖——这些都是"磁盘上的事实"。
/// 把它改写成"返回一份名单"再在内存里断言，等于把最容易坏的那一段（拼路径、开流、改名）留在没测的地方
/// （同 1c 的口径）。
/// </para>
/// <para>要钉住的四条裁决：① zip(store) 不压缩；② 清单字节与老的 <c>.json</c> 逐字节相同（老备份照常能读）；
/// ③ 恢复只写<b>这一份备份的条目认领的名字</b>，且不覆盖本机已有；④ 带没带图片本体是三种不同的事实，话要分开说。</para>
/// </summary>
public sealed class ClipboardBackupAttachmentTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_clipbk_{Guid.NewGuid():N}.db");
    private readonly string _clip = Path.Combine(Path.GetTempPath(), $"starmark_clipbk_clip_{Guid.NewGuid():N}");
    private readonly string _backupDir = Path.Combine(Path.GetTempPath(), $"starmark_clipbk_bk_{Guid.NewGuid():N}");
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;
    private readonly BackupService _backup;

    /// <summary>本例真写出去的导出文件（Dispose 时清掉，含改名前的 .tmp）。</summary>
    private readonly System.Collections.Generic.List<string> _exports = new();

    public ClipboardBackupAttachmentTests()
    {
        Directory.CreateDirectory(_clip);
        Directory.CreateDirectory(_backupDir);
        ClipAssets.FolderOverride = _clip;
        BackupService.SnapshotDirectoryOverride = _backupDir;
        _factory = new DbConnectionFactory(_db);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
        _backup = new BackupService(new BackupRepository(_factory));
    }

    public void Dispose()
    {
        ClipAssets.FolderOverride = null;
        BackupService.SnapshotDirectoryOverride = null;
        foreach (var p in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(p); } catch { }
        foreach (var d in new[] { _clip, _backupDir })
            try { Directory.Delete(d, true); } catch { }
        foreach (var f in _exports)
            try { if (File.Exists(f)) File.Delete(f); } catch { }
    }

    // ────────── 助手 ──────────

    private static DateTimeOffset At(int seconds)
        => new DateTimeOffset(2026, 9, 28, 9, 15, 0, TimeSpan.FromHours(8)).AddSeconds(seconds);

    private static string Sid(string tag) => ClipboardPolicy.BuildImageSourceId(Encoding.UTF8.GetBytes(tag));

    /// <summary>记一条图片历史，并把两张文件真写到 clip 目录（内容带标记，覆盖与否才看得出来）。</summary>
    private async Task<Item> RecImage(string tag, int at = 0)
    {
        var sid = Sid(tag);
        var when = At(at);
        var meta = new ClipboardEntry.ImageMeta(
            ClipAssets.MainNameOf(sid, when), ClipAssets.ThumbNameOf(sid, when), 800, 600, 4321);
        var item = await _repo.RecordClipboardAsync(
            ClipboardEntry.BuildImage(sid, meta, "Code", when), CancellationToken.None);
        Write(ClipboardEntry.FileName(item), $"像素[{tag}]");
        Write(ClipboardEntry.ThumbFileName(item), $"缩略[{tag}]");
        return item;
    }

    private static void Write(string? name, string content)
        => File.WriteAllText(Path.Combine(ClipAssets.Folder, name!), content);

    private static string Read(string name) => File.ReadAllText(Path.Combine(ClipAssets.Folder, name));

    private void DropLocalImages()
    {
        foreach (var f in Directory.GetFiles(_clip)) File.Delete(f);
    }

    /// <summary>要一张 .json 名字的目标路径（带图导出会把它换成同名 .zip，返回的那才是实际写的）。</summary>
    private string Target(bool container = false)
    {
        var p = Path.Combine(_backupDir, $"out_{Guid.NewGuid():N}{(container ? ".zip" : ".json")}");
        _exports.Add(p);
        _exports.Add(BackupContainer.SwapExtension(p));
        return p;
    }

    /// <summary>包里的 <c>clip/</c> 成员：名字与各自字节数（顺手验"没有零字节成员冒充一张图"）。</summary>
    private static (string[] Names, long[] Sizes, long[] Compressed) Package(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var clip = zip.Entries
            .Where(e => e.FullName.StartsWith(BackupContainer.ClipPrefix, StringComparison.Ordinal))
            .ToList();
        return (clip.Select(e => e.FullName[BackupContainer.ClipPrefix.Length..]).ToArray(),
                clip.Select(e => e.Length).ToArray(),
                clip.Select(e => e.CompressedLength).ToArray());
    }

    private static string ManifestOf(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(zip.GetEntry(BackupContainer.ManifestEntryName)!.Open());
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 整条替换包里的清单（模拟"被人改过/被工具截断"的那一份）。
    /// 用 <c>entry.Open()</c> 直接写不行：Update 模式下那不是截断写，会把旧字节留在后面拼成一份四不像。
    /// </summary>
    private static void ReplaceManifest(string zipPath, string json)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        zip.GetEntry(BackupContainer.ManifestEntryName)?.Delete();
        using var w = new StreamWriter(
            zip.CreateEntry(BackupContainer.ManifestEntryName, CompressionLevel.NoCompression).Open(),
            new UTF8Encoding(false));
        w.Write(json);
    }

    // ==================== 导出侧 ====================

    [Fact]
    public async Task TwoImageRows_MakeOnePackageCarryingAllFourFiles()
    {
        var a = await RecImage("A");
        var b = await RecImage("B", 60);
        var export = await _backup.ExportWithClipImagesAsync(Target());

        // 用户要拷走的是"一个文件"，不是一份清单加一个目录——封装完整性就是这条裁决的全部价值。
        Assert.True(BackupContainer.IsContainer(export.Path), "带图片的导出必须写成 .zip");
        Assert.Equal(2, export.ItemCount);
        Assert.Equal(4, export.ClipImages);
        Assert.Equal(0, export.MissingImages);
        Assert.Equal(0, export.FailedImages);
        var (names, sizes, _) = Package(export.Path);
        Assert.Equal(
            new[] { ClipboardEntry.FileName(a), ClipboardEntry.ThumbFileName(a),
                    ClipboardEntry.FileName(b), ClipboardEntry.ThumbFileName(b) }
                .Select(n => n!).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            names.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.All(sizes, s => Assert.True(s > 0, "包里不许有零字节成员冒充一张图"));
        Assert.Equal(4, BackupContainer.CountClipEntries(export.Path));
    }

    [Fact]
    public async Task NothingToCarry_StillWritesJsonEvenWhenTheNameSaysZip()
    {
        // 库里没有图片行（或采集一直关着）→ 包一张都没有 → 那份文件仍是纯清单。
        // 留一个叫 .zip 的 JSON 是最坏的结局：下一个读它的人（正是本程序的导入路径）报"备份包打不开"。
        var asked = Target(container: true);
        var export = await _backup.ExportWithClipImagesAsync(asked);

        Assert.Equal(BackupContainer.ManifestExtension, Path.GetExtension(export.Path));
        Assert.NotEqual(asked, export.Path);
        Assert.Equal(0, export.ClipImages);
        Assert.Equal("{", File.ReadAllText(export.Path)[..1]);
        var env = await BackupService.ReadAsync(export.Path);
        Assert.Empty(env.Payload.Items);
    }

    [Fact]
    public async Task ADataOnlyExportAskedForZipNameAlsoDropsThatName()
    {
        // 开关关着的那条路（UI 调 ExportToFileAsync）同样不许留 .zip：内容决定名字，两个方向都算。
        var asked = Target(container: true);
        var written = await _backup.ExportToFileAsync(asked);

        Assert.NotEqual(asked, written);
        Assert.Equal(BackupContainer.ManifestExtension, Path.GetExtension(written));
        Assert.True(File.Exists(written));
        Assert.False(File.Exists(asked));
        await BackupService.ReadAsync(written);       // 读得开＝它确实是清单，不是"名字像压缩包"
    }

    [Fact]
    public async Task ManifestInsideThePackageIsByteIdenticalToAPlainExport()
    {
        // §3-Q2 的字面要求：清单不变、只加一层封装。校验和、版本判定、导入路径都不必知道附件存在。
        // 两份导出之间必须字节相同——所以这里先把库备成同一个样子，再各导一次。
        await RecImage("A");
        var plain = await _backup.ExportToFileAsync(Target());
        var export = await _backup.ExportWithClipImagesAsync(Target());

        var manifest = ManifestOf(export.Path);
        Assert.Equal(File.ReadAllText(plain), manifest);
        Assert.Equal(BackupService.ComputeChecksum(
            (await BackupService.ReadAsync(plain)).Payload),
            (await BackupService.ReadAsync(export.Path)).Checksum);
        Assert.Equal(2, BackupContainer.CountClipEntries(export.Path));      // 一行两张：主图 + 缩略图
    }

    [Fact]
    public async Task ChecksumStillGuardsTheManifest_WhenOneByteOfThePackageChanges()
    {
        await RecImage("A");
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var original = ManifestOf(export.Path);

        // 只改清单里 payload 的一个字符（条目标题）：JSON 仍然合法，所以挡它的只能是校验和。
        // 这条必须成立——附件的存在不能让"清单被改过"这件事变得看不见。
        ReplaceManifest(export.Path, original.Replace("800×600", "800×601", StringComparison.Ordinal));

        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupService.ReadAsync(export.Path));
        Assert.Contains("校验和不匹配", ex.Message);
    }

    [Fact]
    public async Task AManifestThatIsNoLongerJsonIsReportedAsADamagedBackupNotADeserializerError()
    {
        // 半份包（导出途中断电/磁盘满）与手改坏的清单都落到这里。
        // 递给用户 .NET 那句 "The JSON value could not be converted to …" 回答不了他真正要决定的事
        // （这份能不能用），所以它必须转成格式错误。
        await RecImage("A");
        var export = await _backup.ExportWithClipImagesAsync(Target());
        ReplaceManifest(export.Path, "{\"app\":\"starmark-desk");     // 截断在字符串中间

        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => BackupService.ReadAsync(export.Path));
        Assert.Contains("读不懂", ex.Message);
        Assert.DoesNotContain("converted", ex.Message);
    }

    [Fact]
    public async Task ARowWhoseFilesAreAlreadyGoneIsCountedMissingAndStaysOutOfThePackage()
    {
        var kept = await RecImage("A");
        var lost = await RecImage("B", 60);
        File.Delete(Path.Combine(_clip, ClipboardEntry.FileName(lost)!));
        File.Delete(Path.Combine(_clip, ClipboardEntry.ThumbFileName(lost)!));

        var export = await _backup.ExportWithClipImagesAsync(Target());

        Assert.Equal(2, export.MissingImages);                    // 那行的两张都不在磁盘上
        Assert.Equal(2, export.ClipImages);                        // 包里只有另一行的两张
        Assert.Equal(0, export.FailedImages);
        Assert.DoesNotContain(ClipboardEntry.FileName(lost)!, Package(export.Path).Names);
        Assert.Equal(2, (await BackupService.ReadAsync(export.Path)).Payload.Items.Count);   // 行本体照旧导出
        Assert.True(kept.Id > 0);
    }

    [Fact]
    public async Task AFileAnotherProcessHoldsOpenLeavesNoEmptySlotInThePackage()
    {
        await RecImage("A");
        var blocked = await RecImage("B", 60);
        var blockedName = ClipboardEntry.FileName(blocked)!;
        using (File.Open(Path.Combine(_clip, blockedName), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var export = await _backup.ExportWithClipImagesAsync(Target());

            var (names, sizes, _) = Package(export.Path);
            Assert.DoesNotContain(blockedName, names);             // 没有零字节成员冒充它
            Assert.Equal(3, export.ClipImages);                    // 另外三张照常带上
            Assert.Equal(1, export.FailedImages);                  // 这一张要说"没进包"，不许静默吞掉
            Assert.All(sizes, s => Assert.True(s > 0));
        }
    }

    [Fact]
    public async Task TheSameNameIsNeverCarriedTwice()
    {
        // 旧行的缩略图位回落到主图名（读侧那句 ?? main）：不去重就会在包里放两份同一个文件。
        var sid = Sid("A");
        var when = At(0);
        var main = ClipAssets.MainNameOf(sid, when);
        var row = await _repo.RecordClipboardAsync(ClipboardEntry.BuildImage(
            sid, new ClipboardEntry.ImageMeta(main, main, 8, 8, 12), "Code", when), CancellationToken.None);
        Write(ClipboardEntry.FileName(row), "只有一份");

        var export = await _backup.ExportWithClipImagesAsync(Target());

        Assert.Equal(1, export.ClipImages);
        Assert.Equal(new[] { main }, Package(export.Path).Names);
    }

    [Fact]
    public async Task AttachmentsAreStoredNotDeflated()
    {
        // 决议写的是 zip(store)：PNG 与 JPEG q60 已经压过，再 deflate 平均省不到 5%，
        // 还把导出变成一场 CPU 活。这里用"压缩后字节＝原文件字节"验那颗开关真的是 store。
        var a = await RecImage("A");
        var main = ClipboardEntry.FileName(a)!;
        var raw = new FileInfo(Path.Combine(_clip, main)).Length;

        var export = await _backup.ExportWithClipImagesAsync(Target());

        var (names, sizes, compressed) = Package(export.Path);
        var i = Array.IndexOf(names, main);
        Assert.True(i >= 0);
        Assert.Equal(raw, sizes[i]);
        Assert.Equal(sizes[i], compressed[i]);
    }

    // ==================== 载体识别与盘点 ====================

    [Fact]
    public async Task BothCarriersAreReadAndBothAreListed()
    {
        await RecImage("A");
        var zip = await _backup.ExportWithClipImagesAsync(Target());
        var plain = await _backup.ExportToFileAsync(Target());

        var fromZip = await BackupService.ReadAsync(zip.Path);
        var fromJson = await BackupService.ReadAsync(plain);
        Assert.Equal(fromJson.Payload.Items.Count, fromZip.Payload.Items.Count);

        // 列表少认一种，症状就是"我导出过两份，列表只剩一份"，而少掉那份恰恰是唯一带图的。
        File.Copy(zip.Path, Path.Combine(_backupDir, "kept.zip"), overwrite: true);
        File.Copy(plain, Path.Combine(_backupDir, "kept.json"), overwrite: true);
        var listed = BackupService.EnumerateBackups(_backupDir).Select(b => b.FileName).ToList();
        Assert.Contains("kept.zip", listed);
        Assert.Contains("kept.json", listed);
    }

    [Fact]
    public void PathPolicyAcceptsBothCarriersAndRejectsOthers()
    {
        var (p1, e1) = BackupPathPolicy.ForExport("abc.zip", _backupDir);
        Assert.Null(e1);
        Assert.Equal(Path.Combine(_backupDir, "abc.zip"), p1);

        var (p2, e2) = BackupPathPolicy.ForExport("abc", _backupDir);        // 不写扩展名 → 补清单那个
        Assert.Null(e2);
        Assert.Equal(Path.Combine(_backupDir, "abc" + BackupContainer.ManifestExtension), p2);

        var (_, e3) = BackupPathPolicy.ForExport("abc.txt", _backupDir);
        Assert.Contains(".zip", e3!);

        var (_, e4) = BackupPathPolicy.ForImport(Path.Combine(_backupDir, "nope.zip"));
        Assert.Contains("文件不存在", e4!);

        var (_, e5) = BackupPathPolicy.ForImport(Path.Combine(_backupDir, "nope.rar"));
        Assert.Contains(".json 或 .zip", e5!);
    }

    // ==================== 恢复侧 ====================

    [Fact]
    public async Task RestoringOntoAFreshMachineWritesEveryClaimedFileBack()
    {
        await RecImage("A");
        await RecImage("B", 60);
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var env = await BackupService.ReadAsync(export.Path);
        DropLocalImages();                      // 换机 / 目录被清：条目回来了，图片一张都没有

        var result = await _backup.RestoreAsync(env, RestoreMode.Replace, null, CancellationToken.None, export.Path);

        Assert.True(result.Success);
        Assert.Equal(4, result.ClipImagesRestored);
        Assert.Equal(0, result.ClipImagesSkipped);
        Assert.Equal(0, result.ClipImagesFailed);
        Assert.Contains("写回 4 个", result.Message);
        Assert.Equal("像素[A]", Read(ClipAssets.MainNameOf(Sid("A"), At(0))));
        Assert.Equal("缩略[B]", Read(ClipAssets.ThumbNameOf(Sid("B"), At(60))));
        Assert.Equal(4, Directory.GetFiles(_clip).Length);
    }

    [Fact]
    public async Task RestoringNeverOverwritesAFileThisMachineAlreadyHas()
    {
        await RecImage("A");
        await RecImage("B", 60);
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var env = await BackupService.ReadAsync(export.Path);

        // 本机这一张是用户自己放回来的（§3-Q6 第三类反向）或这一轮新复制的：拿旧备份换掉它是净损失。
        // 其余三张当作本机没有（换机），才看得出"写回 3 / 跳过 1"这两个数各说各的事。
        DropLocalImages();
        var mine = ClipAssets.MainNameOf(Sid("A"), At(0));
        Write(mine, "我自己那份更新的内容");

        var result = await _backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None, export.Path);

        Assert.Equal("我自己那份更新的内容", Read(mine));
        Assert.Equal(3, result.ClipImagesRestored);
        Assert.Equal(1, result.ClipImagesSkipped);
        Assert.Contains("跳过 1 个", result.Message);
    }

    [Fact]
    public async Task EntriesThatNoRowClaimsAreRefusedAndNothingLandsOutsideTheFolder()
    {
        await RecImage("A");
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var env = await BackupService.ReadAsync(export.Path);

        // 手工往包里塞三条来路不明的成员：名字合法但没条目认领、带 ..（Zip Slip）、带盘符形状。
        using (var zip = ZipFile.Open(export.Path, ZipArchiveMode.Update))
        {
            foreach (var name in new[] { "clip/unclaimed.png", "clip/../escaped.png", "clip/C:\\windows.png" })
                using (var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.NoCompression).Open()))
                    w.Write("不该落盘");
        }
        var parent = Directory.GetParent(_clip)!.FullName;
        DropLocalImages();          // 先当作"换机"：本机一张都没有，才看得出这三条越权成员到底落没落盘

        var result = await _backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None, export.Path);

        Assert.False(File.Exists(Path.Combine(_clip, "unclaimed.png")));
        Assert.False(File.Exists(Path.Combine(parent, "escaped.png")));
        Assert.False(File.Exists(Path.Combine(_clip, "C:\\windows.png")));
        // 越权的那几条算"跳过"（不是写失败），合法那两张仍要写回——三个箱子不许混成一个数。
        Assert.Equal(3, result.ClipImagesSkipped);
        Assert.Equal(2, result.ClipImagesRestored);
        Assert.Equal(0, result.ClipImagesFailed);
        Assert.Equal(new[] { ClipAssets.MainNameOf(Sid("A"), At(0)), ClipAssets.ThumbNameOf(Sid("A"), At(0)) }
            .OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            Directory.GetFiles(_clip).Select(f => Path.GetFileName(f)!).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task AHalfWrittenAttachmentIsCountedFailedNotSilentlySkipped()
    {
        await RecImage("A");
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var env = await BackupService.ReadAsync(export.Path);

        var main = ClipAssets.MainNameOf(Sid("A"), At(0));
        DropLocalImages();
        // 用"临时名的位置上站着一个目录"制造一次写不进去：File.Create 必然失败，而失败必须被数出来。
        Directory.CreateDirectory(Path.Combine(_clip, ClipAssets.TempNameOf(main)));

        var result = await _backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None, export.Path);

        Assert.Equal(1, result.ClipImagesRestored);        // 缩略图那张仍然回来了
        Assert.Equal(1, result.ClipImagesFailed);          // 主图这张要说"没能写入"，不许并进"跳过"
        Assert.False(File.Exists(Path.Combine(_clip, main)));
        Assert.Contains("没能写入", result.Message);
    }

    [Fact]
    public async Task DataOnlyBackupsKeepTellingTheTruthAboutMissingPictures()
    {
        // 开关关着导出的那一份、以及 a 期之前的老备份：条目在、文件名在、图不在。
        // 这句原话不许因为 a 期上线就撤——对这些备份它仍是事实。
        await RecImage("A");
        var plain = await _backup.ExportToFileAsync(Target());
        var env = await BackupService.ReadAsync(plain);
        DropLocalImages();

        var result = await _backup.RestoreAsync(env, RestoreMode.Replace, null, CancellationToken.None, plain);

        Assert.Equal(0, result.ClipImagesRestored);
        Assert.Contains("不带图片本体", result.Message);
        Assert.DoesNotContain("写回", result.Message);
    }

    [Fact]
    public async Task RowsThePackageHasNoFileForAreReportedSeparatelyFromFilesThatFailed()
    {
        // 一份包里三种事实同时在场：A 行两张都带着、B 行导出时本就没文件。
        // 上一版这里用"图片行数 − 文件个数"做减法，量纲不同（一行占两张）得出"缺 0 条"而实际缺一条——
        // 现在按"这一行认领的名字在不在包里"取交集，所以缺的就是缺。
        await RecImage("A");
        var noFile = await RecImage("B", 60);
        File.Delete(Path.Combine(_clip, ClipboardEntry.FileName(noFile)!));
        File.Delete(Path.Combine(_clip, ClipboardEntry.ThumbFileName(noFile)!));
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var env = await BackupService.ReadAsync(export.Path);
        DropLocalImages();

        var result = await _backup.RestoreAsync(env, RestoreMode.Replace, null, CancellationToken.None, export.Path);

        Assert.Equal(2, result.ClipImagesRestored);
        Assert.Contains("这份包里带着 2 个图片文件，写回 2 个", result.Message);
        Assert.Contains("还有 1 条图片历史在这份包里就没有对应的文件", result.Message);
    }

    [Fact]
    public async Task RestoreWithoutASourcePathNeverTouchesTheClipFolder()
    {
        // 快照与每日自动件都是 .json：把它们的路径接进附件逻辑＝每次回滚都往图片目录写文件。
        await RecImage("A");
        var export = await _backup.ExportWithClipImagesAsync(Target());
        var env = await BackupService.ReadAsync(export.Path);
        DropLocalImages();

        var result = await _backup.RestoreAsync(env, RestoreMode.Replace);

        Assert.Equal(0, result.ClipImagesRestored);
        Assert.Empty(Directory.GetFiles(_clip));
        // 没递路径＝没人打开过那个包，所以这句不许写成"这份备份不带图片本体"（那是对文件的断言）。
        Assert.Contains("这次没有从备份包里解出任何图片文件", result.Message);
    }
}
