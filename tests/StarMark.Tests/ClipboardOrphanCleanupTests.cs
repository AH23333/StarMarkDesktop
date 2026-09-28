#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Data;
using StarMark.Integrations.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// ClipIMG-3a：<b>§3-Q6 的第二类（图有行无）</b>——"只数不删"里的那半句"数出来的看得见"，
/// 以及决议明列的那颗出口（"给『清理孤儿』入口"，但<b>不静默删用户目录</b>）。
/// <para>
/// 这里全部走真仓储 + 真临时目录：清理是一段会碰文件系统的代码，
/// 把它写成"返回该删哪些名字"再在内存里断言，最容易坏的三段（拼路径、名字名册、删前重扫）就全留在没测的地方。
/// </para>
/// </summary>
public sealed class ClipboardOrphanCleanupTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_orphan_{Guid.NewGuid():N}.db");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"starmark_orphan_{Guid.NewGuid():N}");
    private readonly ItemRepository _repo;

    public ClipboardOrphanCleanupTests()
    {
        var factory = new DbConnectionFactory(_db);
        new MigrationRunner(factory).EnsureSchema();
        _repo = new ItemRepository(factory);
        Directory.CreateDirectory(_dir);
        ClipAssets.FolderOverride = _dir;
    }

    public void Dispose()
    {
        ClipAssets.FolderOverride = null;
        foreach (var p in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(p); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static DateTimeOffset At(int seconds)
        => new DateTimeOffset(2026, 9, 28, 9, 15, 0, TimeSpan.FromHours(8)).AddSeconds(seconds);

    private static string Sid(string tag) => ClipboardPolicy.BuildImageSourceId(Encoding.UTF8.GetBytes(tag));

    /// <summary>在 clip 目录里摆一个文件，返回它的字节数（体积那句要看的就是这个数）。</summary>
    private long Touch(string name, int size = 1024)
    {
        var path = ClipAssets.FullPathOf(name);
        Assert.NotNull(path);
        File.WriteAllBytes(path!, new byte[size]);
        return size;
    }

    /// <summary>记一条图片历史，并把行里那两个名字落到磁盘上（＝"行有图也有"，既不缺失也不算孤儿）。</summary>
    private async Task<Item> LiveRowAsync(string tag, int at = 0)
    {
        var sid = Sid(tag);
        var when = At(at);
        var meta = new ClipboardEntry.ImageMeta(
            ClipAssets.MainNameOf(sid, when), ClipAssets.ThumbNameOf(sid, when), 800, 600, 4321);
        var item = await _repo.RecordClipboardAsync(
            ClipboardEntry.BuildImage(sid, meta, "Code", when), CancellationToken.None);
        Touch(ClipboardEntry.FileName(item)!);
        Touch(ClipboardEntry.ThumbFileName(item)!);
        return item;
    }

    // ────────── 扫描：三分类仍然只有一份出处 ──────────

    [Fact]
    public async Task ScanSeparatesOrphansFromClaimedAndTemp()
    {
        var live = await LiveRowAsync("live", at: 0);
        var main = ClipboardEntry.FileName(live)!;
        var thumb = ClipboardEntry.ThumbFileName(live)!;
        Touch("someone_elses.png", 2048);                       // 用户自己拷进来的
        Touch(ClipAssets.TempNameOf(main), 64);                 // 我们自己的半件

        var scan = await ClipboardAssetAudit.ScanAsync(_repo);
        Assert.NotNull(scan);
        var r = scan!.Value.Result;
        Assert.Equal(new[] { "someone_elses.png" }, r.OrphanNames);   // 已落盘的 main/thumb 都被行认领着
        Assert.Equal(new[] { ClipAssets.TempNameOf(main) }, r.TempNames);
        Assert.Empty(r.MissingRowIds);                                 // 行有图也在：不该被标缺失
        Assert.Equal(2048, ClipboardAssetAudit.BytesOf(scan!.Value).OrphanBytes);
    }

    [Fact]
    public async Task ADirectoryWithNoRowsIsNotAnError()
    {
        // 从没开过图片采集：Empty 而不是 null——把"没跑成"与"没事发生"混成一个，
        // 界面上就会出现"对账失败了"的假警报，用户去查一个不存在的故障。
        var scan = await ClipboardAssetAudit.ScanAsync(_repo);
        Assert.NotNull(scan);
        Assert.True(scan!.Value.Result.NothingToDo);
    }

    // ────────── 清理：删什么、不删什么、删不动怎么报 ──────────

    [Fact]
    public async Task CleanRemovesOrphansAndTempsButNeverClaimedFiles()
    {
        var live = await LiveRowAsync("keep", at: 0);
        var main = ClipboardEntry.FileName(live)!;
        var thumb = ClipboardEntry.ThumbFileName(live)!;
        Touch("orphan_one.png", 1000);
        Touch("orphan_two.png", 2000);
        Touch(ClipAssets.TempNameOf("ghost.png"), 50);

        var outcome = await ClipboardAssetAudit.CleanAsync(_repo);
        Assert.NotNull(outcome);
        Assert.Equal(2, outcome!.Value.Deleted);
        Assert.Equal(1, outcome.Value.TempDeleted);
        Assert.Equal(3000, outcome.Value.DeletedBytes);
        Assert.Equal(0, outcome.Value.Failed);
        Assert.True(File.Exists(ClipAssets.FullPathOf(main)));
        Assert.True(File.Exists(ClipAssets.FullPathOf(thumb)));       // 缩略图也被行认领着，一个都不许碰
        Assert.False(File.Exists(ClipAssets.FullPathOf("orphan_one.png")));
        Assert.False(File.Exists(ClipAssets.FullPathOf("orphan_two.png")));
        Assert.False(File.Exists(ClipAssets.FullPathOf(ClipAssets.TempNameOf("ghost.png"))));
    }

    [Fact]
    public async Task CleanRescansSoAFileClaimedInBetweenSurvives()
    {
        // 这一条钉的是"清理不接界面那份旧名单"。界面上的名单是用户读它那一刻的事实；
        // 中间一次"同图再复制"或备份恢复把名字认领回去，按旧名单删＝删掉一张正在被历史用的图。
        Touch("late.png", 700);
        Assert.Empty((await ClipboardAssetAudit.ScanAsync(_repo))!.Value.Result.MissingRowIds);

        // 现在"来了一条历史行用它"（名字与行里的完全一致，就是那份被认领的文件）。
        // ImageMeta 的缩略图位不给 null：这个类型里"没有单独缩略图"的写法就是重复主图名（与读侧的 ?? main 同一口径）。
        var item = await _repo.RecordClipboardAsync(ClipboardEntry.BuildImage(
            Sid("late"), new ClipboardEntry.ImageMeta("late.png", "late.png", 8, 8, 700), "Code", At(30)),
            CancellationToken.None);
        Assert.Equal("late.png", ClipboardEntry.FileName(item));

        var outcome = await ClipboardAssetAudit.CleanAsync(_repo);
        Assert.Equal(0, outcome!.Value.Deleted);
        Assert.True(File.Exists(ClipAssets.FullPathOf("late.png")));   // 活着的东西一个都没掉
    }

    [Fact]
    public async Task ALockedFileIsReportedAsFailedAndStaysOnDisk()
    {
        // 删不动是常态（杀毒软件、图片预览器正开着那一张）。坏法不是抛，是"报成功却还留着"。
        Touch("busy.png", 512);
        using (var hold = new FileStream(ClipAssets.FullPathOf("busy.png")!,
                   FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = await ClipboardAssetAudit.CleanAsync(_repo);
            Assert.Equal(0, outcome!.Value.Deleted);
            Assert.Equal(1, outcome.Value.Failed);
        }
        Assert.True(File.Exists(ClipAssets.FullPathOf("busy.png")));
    }

    [Fact]
    public async Task NothingToCleanIsAZerosOutcomeNotAFailure()
    {
        await LiveRowAsync("only", at: 0);
        var outcome = await ClipboardAssetAudit.CleanAsync(_repo);
        Assert.NotNull(outcome);                                       // null＝没跑成，这里明明跑成了
        Assert.Equal(0, outcome!.Value.Deleted + outcome.Value.Failed
                       + outcome.Value.TempDeleted + outcome.Value.TempFailed);
    }

    [Fact]
    public async Task CleanIsIdempotentSoASecondClickSaysSo()
    {
        Touch("gone.png", 300);
        var first = await ClipboardAssetAudit.CleanAsync(_repo);
        Assert.Equal(1, first!.Value.Deleted);
        var second = await ClipboardAssetAudit.CleanAsync(_repo);
        Assert.Equal(0, second!.Value.Deleted + second.Value.TempDeleted);   // 界面上据此说"现在没有需要清理的"
    }

    // ────────── 那两句要念给用户听的话（纯函数，逐值钉） ──────────

    [Theory]
    [InlineData(0, 0, "")]                                   // 没有孤儿就不占行：那不是需要报的事实
    [InlineData(1, 2048, "1 个文件不在历史里")]
    [InlineData(7, 20 * 1024 * 1024, "7 个文件不在历史里")]
    public void OrphanSentenceStatesTheCountAndTheSize(int count, long bytes, string expected)
    {
        var text = ClipAssets.DescribeOrphans(count, bytes);
        if (expected.Length == 0) { Assert.Equal(string.Empty, text); return; }
        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains(ClipAssets.DescribeBytes(bytes), text, StringComparison.Ordinal);
        // "可能是残留，也可能是你自己拷进来的"两句都要在：只说其一会让用户以为程序在指控他。
        Assert.Contains("残留", text, StringComparison.Ordinal);
        Assert.Contains("拷进来", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmBodyListsNamesSaysHistoryUntouchedAndCountsWhatItOmitted()
    {
        var many = Enumerable.Range(0, ClipAssets.CleanupPreviewLimit + 3)
            .Select(i => $"2026-09-28_0915_{i:x8}.png").ToList();
        var body = ClipAssets.CleanupConfirmBody(many, 9000, tempCount: 0, tempBytes: 0, folder: @"C:\x\clip");

        Assert.Contains(@"C:\x\clip", body, StringComparison.Ordinal);        // 哪个目录：可见性决议要求原话写出来
        Assert.Contains("不会动任何一条历史条目", body, StringComparison.Ordinal);
        Assert.Contains("这份名单之外", body, StringComparison.Ordinal);
        Assert.Contains($"其余 3 个没有列出", body, StringComparison.Ordinal);  // 截断必须承认截断
        // 列出的条数正好等于上限，一条都不许多（多了那份"清单"就没人读了）。
        Assert.Equal(ClipAssets.CleanupPreviewLimit,
            body.Split('\n').Count(l => l.StartsWith("· ", StringComparison.Ordinal)));
        Assert.DoesNotContain("临时件", body, StringComparison.Ordinal);       // tempCount=0 时不提它
    }

    [Fact]
    public void ConfirmBodyCoversTheFourShapesTheButtonCanSee()
    {
        var one = new[] { "a.png" };
        // ① 只有孤儿：列名字，不提临时件。
        var onlyOrphan = ClipAssets.CleanupConfirmBody(one, 10, 0, 0, "D:\\clip");
        Assert.Contains("a.png", onlyOrphan, StringComparison.Ordinal);
        Assert.DoesNotContain("临时件", onlyOrphan, StringComparison.Ordinal);
        // ② 只有临时件：不编一份空名单，也不能一句"没有可清"。
        var onlyTemp = ClipAssets.CleanupConfirmBody(Array.Empty<string>(), 0, 2, 64, "D:\\clip");
        Assert.Contains("2 个没写完的临时件", onlyTemp, StringComparison.Ordinal);
        Assert.DoesNotContain("· ", onlyTemp, StringComparison.Ordinal);
        // ③ 两种都有：各自成句，且临时件要说清"那是我们自己的半件，不可能是你的文件"。
        var both = ClipAssets.CleanupConfirmBody(one, 10, 2, 64, "D:\\clip");
        Assert.Contains("· a.png", both, StringComparison.Ordinal);
        Assert.Contains("不可能是你的文件", both, StringComparison.Ordinal);
        // ④ 一句都不许出现"请稍后/请确认/重启"式的把责任交回用户的话（P-54 同口径）。
        Assert.DoesNotContain("请", both, StringComparison.Ordinal);
    }
}
