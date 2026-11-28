#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Abstractions.I18n;
using StarMark.Data;
using StarMark.Integrations.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// clip-drops 的<b>清理孤儿</b>那一条路（片292・§3-Q6 第二类的出口，drops 版）：
/// "只数不删"里的那半句"数出来的看得见"，以及决议明列的那颗出口（"给『清理孤儿』入口"，
/// 但<b>不静默删用户目录</b>）。
/// <para>这里全部走真仓储 + 真临时目录：清理是一段会碰文件系统的代码，
/// 把它写成"返回该删哪些名字"再在内存里断言，最容易坏的三段（拼路径、名字名册、删前重扫）就全留在没测的地方。</para>
/// <para>与图片那一族（<see cref="ClipboardOrphanCleanupTests"/>）的判据差异：drops 的名字必须过
/// <see cref="ClipDropAssets.IsSafeFileName"/> 名册（前 13 位＝12 位小写哈希＋下划线），
/// 所以这里孤儿/临时件全用名册形状的名字——不这样的话删那一侧会把它们当"不认识的名字"拒掉。</para>
/// </summary>
[Collection("LocaleGlobal")]    // 下面那几格渲染中文那一屏，⛔ 与换语言的证人并发跑
public sealed class ClipboardDropOrphanCleanupTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_droporph_{Guid.NewGuid():N}.db");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"starmark_droporph_{Guid.NewGuid():N}");
    private readonly ItemRepository _repo;

    public ClipboardDropOrphanCleanupTests()
    {
        var factory = new DbConnectionFactory(_db);
        new MigrationRunner(factory).EnsureSchema();
        _repo = new ItemRepository(factory);
        Directory.CreateDirectory(_dir);
        // ⛔ 不改道＝删到用户真实 clip-drops 目录里去（与 ClipAssets.FolderOverride 同一条纪律）。
        ClipDropAssets.FolderOverride = _dir;
    }

    public void Dispose()
    {
        ClipDropAssets.FolderOverride = null;
        foreach (var p in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(p); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static DateTimeOffset At(int seconds)
        => new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.FromHours(8)).AddSeconds(seconds);

    /// <summary>名册形状的一颗名字（12 位哈希＋下划线＋原始名）——删那一侧只认这个形状。</summary>
    private static string DropName(string tag)
        => ClipDropAssets.FileNameOf(ClipDropAssets.HashOf(Encoding.UTF8.GetBytes(tag)), tag + ".pdf");

    private async Task<Item> RecDropRowAsync(string tag, int at, params string[] names)
    {
        var draft = ClipboardEntry.Build($"C:\\件\\{tag}.txt", "Code", ClipboardEntry.FormatFiles, At(at));
        if (names.Length > 0)
            draft.ExtraJson = ClipboardEntry.WithDropFilesOf(draft.ExtraJson, names);
        return await _repo.RecordClipboardAsync(draft, CancellationToken.None);
    }

    /// <summary>在 clip-drops 目录里摆一个文件，返回它的字节数（体积那句要看的就是这个数）。</summary>
    private long Touch(string name, int size = 1024)
    {
        var path = ClipDropAssets.FullPathOf(name);
        Assert.NotNull(path);
        File.WriteAllBytes(path!, new byte[size]);
        return size;
    }

    // ────────── 扫描：三分类仍然只有一份出处 ──────────

    [Fact]
    public async Task ScanSeparatesOrphansFromClaimedAndTemp()
    {
        var claimed = DropName("有主的那件");
        await RecDropRowAsync("甲", 0, claimed);
        Touch(claimed);
        Touch(DropName("用户自己拷进来的"), 2048);
        Touch(ClipDropAssets.TempNameOf(DropName("残货")), 64);

        var scan = await ClipboardDropAudit.ScanAsync(_repo);
        Assert.NotNull(scan);
        var r = scan!.Value.Result;
        Assert.Equal(new[] { DropName("用户自己拷进来的") }, r.OrphanNames);
        Assert.Equal(new[] { ClipDropAssets.TempNameOf(DropName("残货")) }, r.TempNames);
        Assert.Empty(r.MissingNames);
        var (orphanBytes, tempBytes) = ClipboardDropAudit.BytesOf(scan.Value);
        Assert.Equal(2048, orphanBytes);
        Assert.Equal(64, tempBytes);
    }

    [Fact]
    public async Task ADirectoryWithNoRowsIsNotAnError()
    {
        // 从没拖放过：Empty 而不是 null——把"没跑成"与"没事发生"混成一个，
        // 界面上就会出现"对账失败了"的假警报。
        var scan = await ClipboardDropAudit.ScanAsync(_repo);
        Assert.NotNull(scan);
        Assert.True(scan!.Value.Result.NothingToDo);
    }

    [Fact]
    public async Task OneRowClaimingManyFilesHasEachFileJudgedOnItsOwn()
    {
        // "一行认 N 件"在扫描这一层也要成立：缺一件只报那一件，其余的在就继续有主。
        var a = DropName("件甲");
        var b = DropName("件乙");
        await RecDropRowAsync("多认领", 0, a, b);
        Touch(a);                                     // 乙不在磁盘上

        var scan = await ClipboardDropAudit.ScanAsync(_repo);
        var r = scan!.Value.Result;
        Assert.Equal(new[] { b }, r.MissingNames);
        Assert.Empty(r.OrphanNames);                  // 甲被认领着，不冒充孤儿
    }

    // ────────── 清理：删什么、不删什么、删不动怎么报 ──────────

    [Fact]
    public async Task CleanRemovesOrphansAndTempsButNeverClaimedFiles()
    {
        var claimed = DropName("有主的那件");
        await RecDropRowAsync("乙", 0, claimed);
        Touch(claimed);
        Touch(DropName("孤儿一"), 1000);
        Touch(DropName("孤儿二"), 2000);
        Touch(ClipDropAssets.TempNameOf(DropName("残货")), 50);

        var outcome = await ClipboardDropAudit.CleanAsync(_repo);
        Assert.NotNull(outcome);
        Assert.Equal(2, outcome!.Value.Deleted);
        Assert.Equal(3000, outcome.Value.DeletedBytes);
        Assert.Equal(1, outcome.Value.TempDeleted);
        Assert.Equal(50, outcome.Value.TempDeletedBytes);
        Assert.Equal(0, outcome.Value.Failed + outcome.Value.TempFailed);
        Assert.True(File.Exists(ClipDropAssets.FullPathOf(claimed)));
        Assert.False(File.Exists(ClipDropAssets.FullPathOf(DropName("孤儿一"))));
        Assert.False(File.Exists(ClipDropAssets.FullPathOf(DropName("孤儿二"))));
        Assert.False(File.Exists(ClipDropAssets.FullPathOf(ClipDropAssets.TempNameOf(DropName("残货")))));
    }

    [Fact]
    public async Task CleanRescansSoAFileClaimedInBetweenSurvives()
    {
        // 这一条钉的是"清理不接界面那份旧名单"。中间一次"同一件再拖"或备份恢复把名字认领回去，
        // 按旧名单删＝删掉一件正在被历史用的文件。
        var late = DropName("后来被认领的那件");
        Touch(late, 700);
        Assert.Contains(late, (await ClipboardDropAudit.ScanAsync(_repo))!.Value.Result.OrphanNames);

        await RecDropRowAsync("后来的行", 30, late);        // 预览之后、删除之前：它有主了

        var outcome = await ClipboardDropAudit.CleanAsync(_repo);
        Assert.Equal(0, outcome!.Value.Deleted);
        Assert.True(File.Exists(ClipDropAssets.FullPathOf(late)));
    }

    [Fact]
    public async Task ALockedFileIsReportedAsFailedAndStaysOnDisk()
    {
        // 删不动是常态（杀毒软件、预览器正开着那一件）。坏法不是抛，是"报成功却还留着"。
        var busy = DropName("被占用的那件");
        Touch(busy, 512);
        using (new FileStream(ClipDropAssets.FullPathOf(busy)!, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = await ClipboardDropAudit.CleanAsync(_repo);
            Assert.Equal(0, outcome!.Value.Deleted);
            Assert.Equal(1, outcome.Value.Failed);
        }
        Assert.True(File.Exists(ClipDropAssets.FullPathOf(busy)));
    }

    [Fact]
    public async Task NothingToCleanIsAZerosOutcomeNotAFailure()
    {
        var claimed = DropName("唯一有主的那件");
        await RecDropRowAsync("丙", 0, claimed);
        Touch(claimed);
        var outcome = await ClipboardDropAudit.CleanAsync(_repo);
        Assert.NotNull(outcome);                                       // null＝没跑成，这里明明跑成了
        Assert.Equal(0, outcome!.Value.Deleted + outcome.Value.Failed
                       + outcome.Value.TempDeleted + outcome.Value.TempFailed);
    }

    [Fact]
    public async Task CleanIsIdempotentSoASecondClickSaysSo()
    {
        Touch(DropName("一次就够的那件"), 300);
        var first = await ClipboardDropAudit.CleanAsync(_repo);
        Assert.Equal(1, first!.Value.Deleted);
        var second = await ClipboardDropAudit.CleanAsync(_repo);
        Assert.Equal(0, second!.Value.Deleted + second.Value.TempDeleted);   // 界面上据此说"现在没有需要清理的"
    }

    // ────────── 占用那句话：一族一种件（纯函数，逐值钉） ──────────

    [Fact]
    public void SummarizeSplitsTemporariesFromFormalFiles()
    {
        var files = new (string Name, long Bytes)[]
        {
            (DropName("正式件"), 100),
            (ClipDropAssets.TempNameOf(DropName("残货一")), 40),
            (ClipDropAssets.TempNameOf(DropName("残货二")), 60),
        };
        var footprint = ClipDropAssets.Summarize(files, out var tempBytes, out var tempCount);
        Assert.Equal(1, footprint.FileCount);                          // 临时件不算正式件
        Assert.Equal(100, footprint.FileBytes);
        Assert.Equal(100, tempBytes);                                  // 件数与字节分开交：两个量纲
        Assert.Equal(2, tempCount);
    }

    [Fact]
    public void UsageSentenceSaysEmptyOnlyWhenThereIsNothingAtAll()
    {
        var empty = ClipDropAssets.DescribeUsage(new ClipDropAssets.DropFootprint(0, 0), 0, 0);
        Assert.Equal("drop.usage.empty", LocAssert.KeyOf(empty));
        // 有临时件（哪怕正式件一个都没有）就不是"空的"：那是"有一次没写完"，事实不同话不同。
        var tempsOnly = ClipDropAssets.DescribeUsage(new ClipDropAssets.DropFootprint(0, 0), 64, 1);
        Assert.Equal("drop.usage.full", LocAssert.KeyOf(tempsOnly));
    }

    [Fact]
    public void UsageSentenceCarriesCountBytesAndTheTempTailSeparately()
    {
        var msg = ClipDropAssets.DescribeUsage(new ClipDropAssets.DropFootprint(2, 5000), 300, 1);
        Assert.Equal("drop.usage.full", LocAssert.KeyOf(msg));
        var head = LocAssert.Arm(msg, 0);
        Assert.Equal("drop.usage.files", LocAssert.KeyOf(head));
        Assert.Equal(2, LocAssert.Arg(head, 0));
        Assert.Equal(ClipAssets.DescribeBytes(5000), LocAssert.Arg(head, 1));
        var temps = LocAssert.Arm(msg, 1);
        Assert.Equal("drop.usage.temps", LocAssert.KeyOf(temps));
        Assert.Equal(1, LocAssert.Arg(temps, 0));
        Assert.Equal(ClipAssets.DescribeBytes(300), LocAssert.Arg(temps, 1));

        var noTemps = ClipDropAssets.DescribeUsage(new ClipDropAssets.DropFootprint(2, 5000), 0, 0);
        LocAssert.Silent(noTemps, 1);                                  // 没有临时件：那一档不许说话
    }

    [Fact]
    public void OrphanSentenceIsAbsentAtZeroAndStatesCountAndSizeOtherwise()
    {
        // ✅WI-64：0 颗交的是 <b>null</b> 而⛔ 空串——宿主那一侧用 null 回答"有没有这句话"。
        Assert.Null(ClipDropAssets.DescribeOrphans(0, 0));
        var some = ClipDropAssets.DescribeOrphans(7, 20 * 1024 * 1024);
        Assert.Equal("drop.orphans.some", LocAssert.KeyOf(some));
        Assert.Equal(new object?[] { 7, ClipAssets.DescribeBytes(20 * 1024 * 1024) }, some!.Args);
    }
}
