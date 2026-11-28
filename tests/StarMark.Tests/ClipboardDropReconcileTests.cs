#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Data;
using StarMark.Integrations.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 拖入文件那一族的启动对账（片292・§3-Q6 三分类，drops 版）。分两层写：
/// <see cref="ClipDropAssets.Reconcile"/> 是纯函数，逐格钉判据；
/// 另几条走真 <see cref="ItemRepository"/> + 真目录，钉的是 drops 版的分叉一甲——
/// <b>对账纯读：一件文件都不许删、一行库也不许写</b>（没有"打标记"那一类），
/// 这两条都是只有碰真盘才测得出来的。
/// <para>与图片那一族（<see cref="ClipboardReconcileTests"/>）的判据差异：drops 的认领是
/// "一行认 N 件"，所以这里要额外钉"一行名单里的每一件各自受审"与"多行认同一件只算一件"。</para>
/// </summary>
public sealed class ClipboardDropReconcileTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_droprecon_{Guid.NewGuid():N}.db");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"starmark_droprecon_{Guid.NewGuid():N}");
    private readonly ItemRepository _repo;

    public ClipboardDropReconcileTests()
    {
        var factory = new DbConnectionFactory(_db);
        new MigrationRunner(factory).EnsureSchema();
        _repo = new ItemRepository(factory);
        Directory.CreateDirectory(_dir);
        // ⛔ 不改道＝往用户真实 clip-drops 目录里扫测试件（与 ClipAssets.FolderOverride 同一条纪律）。
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

    /// <summary>过得了 <see cref="ClipDropAssets.IsSafeFileName"/> 名册的一颗名字（对账本身不问名册，但真盘上的件都得长这样）。</summary>
    private static string DropName(string tag)
        => ClipDropAssets.FileNameOf(ClipDropAssets.HashOf(Encoding.UTF8.GetBytes(tag)), tag + ".pdf");

    /// <summary>记一条认领着 <paramref name="names"/> 的历史行（走 Build+WithDropFilesOf 而不是拖放链：
    /// 这里钉的是对账读不读得对，不是拖放那一侧的接线）。</summary>
    private async Task<Item> RecDropRowAsync(string tag, int at, params string[] names)
    {
        var draft = ClipboardEntry.Build($"C:\\件\\{tag}.txt", "Code", ClipboardEntry.FormatFiles, At(at));
        if (names.Length > 0)
            draft.ExtraJson = ClipboardEntry.WithDropFilesOf(draft.ExtraJson, names);
        return await _repo.RecordClipboardAsync(draft, CancellationToken.None);
    }

    // ────────── 纯判据：三类文件 + 一行认 N 件 ──────────

    [Fact]
    public void AClaimedFileThatIsNotOnDiskIsMissing()
    {
        var name = DropName("被手删的那件");
        var r = ClipDropAssets.Reconcile(new[] { new[] { name } }, Array.Empty<string>());
        Assert.Equal(new[] { name }, r.MissingNames);
        Assert.Empty(r.OrphanNames);
        Assert.Empty(r.TempNames);
    }

    [Fact]
    public void AFileNoRowClaimsIsAnOrphan()
    {
        var name = DropName("没人认领的那件");
        var r = ClipDropAssets.Reconcile(Array.Empty<string[]>(), new[] { name });
        Assert.Equal(new[] { name }, r.OrphanNames);
        Assert.Empty(r.MissingNames);
    }

    [Fact]
    public void TempFilesAreTheirOwnClassNotOrphans()
    {
        var r = ClipDropAssets.Reconcile(Array.Empty<string[]>(),
            new[] { ClipDropAssets.TempNameOf(DropName("残货")), DropName("真件") });
        Assert.Equal(new[] { DropName("真件") }, r.OrphanNames);    // 只有真落过盘的件才算孤儿
        Assert.Single(r.TempNames);
    }

    [Fact]
    public void ComparisonIgnoresCaseBecauseNtfsDoes()
    {
        // 名字是我们写出去的（哈希段小写），但目录里回来的大小写不由我们保证；
        // 按序数敏感比就会把"其实在"的件判成缺失——用户看到的是"我的附件打不开了"。
        var lower = DropName("大小写");
        var upper = lower.ToUpperInvariant();
        var r = ClipDropAssets.Reconcile(new[] { new[] { lower } }, new[] { upper });
        Assert.Empty(r.MissingNames);
        Assert.Empty(r.OrphanNames);
    }

    [Fact]
    public void TwoRowsClaimingTheSameFileCountItOnceAndLeaveNoOrphan()
    {
        // 多行认同一件是 drops 的常态（同一件拖进过两格）；"认领了但不在"只报一件，
        // 对账数的是磁盘上有几个名字对不上，不是名单里出现几次。
        var name = DropName("共享的那件");
        var r = ClipDropAssets.Reconcile(new[] { new[] { name }, new[] { name } }, Array.Empty<string>());
        Assert.Equal(new[] { name }, r.MissingNames);               // 只算一件
        Assert.Empty(r.OrphanNames);
    }

    [Fact]
    public void OneRowClaimsManyFilesAndEachIsJudgedOnItsOwn()
    {
        // "一行认 N 件"是 drops 独有的形状：一行三件缺一件，缺的就是那一件——
        // 行级缺失标记答不了这个问题，这正是对账选"纯读不标"的那条理由（分叉一甲）。
        var a = DropName("件甲");
        var b = DropName("件乙");
        var c = DropName("件丙");
        var r = ClipDropAssets.Reconcile(new[] { new[] { a, b, c } }, new[] { a, c });
        Assert.Equal(new[] { b }, r.MissingNames);
        Assert.Empty(r.OrphanNames);
    }

    [Fact]
    public void BlankEntriesAndEmptyClaimsAreIgnoredInsteadOfCounting()
    {
        var real = DropName("真件");
        var r = ClipDropAssets.Reconcile(
            new[]
            {
                Array.Empty<string>(),                       // 没认领任何件的行：常态，不是数据坏
                new[] { "", "   ", null! },
            },
            new[] { real });
        Assert.Empty(r.MissingNames);                         // 空白名字不算一件缺失
        Assert.Equal(new[] { real }, r.OrphanNames);
    }

    // ────────── 走真盘：对账之后磁盘与库里剩什么 ──────────

    [Fact]
    public async Task HandDeletedFileIsCountedMissingAndTheRowSurvives()
    {
        var name = DropName("被手删的那件");
        await RecDropRowAsync("甲", 0, name);
        File.WriteAllText(Path.Combine(_dir, name), "本体");
        File.Delete(Path.Combine(_dir, name));

        var r = await Audit();

        Assert.Equal(new[] { name }, r.MissingNames);
        var row = Assert.Single(await _repo.GetBySourceAsync(ItemSources.Clipboard, ItemType.Clipboard));
        Assert.Equal(new[] { name }, ClipboardEntry.DropFileNamesOf(row.ExtraJson));   // 行与认领名单一个字符都不动
    }

    [Fact]
    public async Task PuttingTheFileBackClearsTheMissingCountWithoutAnyFlagToReset()
    {
        // 无状态＝天然幂等（分叉一甲的那句"第三类反向免费得到"）：件放回来，"缺失"自动归零——
        // 没有标记要清，也就没有"标记忘了清"这一族坏法。
        var name = DropName("会放回来的那件");
        await RecDropRowAsync("乙", 0, name);
        File.WriteAllText(Path.Combine(_dir, name), "本体");
        File.Delete(Path.Combine(_dir, name));
        Assert.Single((await Audit()).MissingNames);

        File.WriteAllText(Path.Combine(_dir, name), "用户放回来的");

        var r = await Audit();
        Assert.Empty(r.MissingNames);
        Assert.Empty(r.OrphanNames);
        Assert.True(r.NothingToDo);
    }

    [Fact]
    public async Task OrphanFileIsCountedButSurvives()
    {
        var name = DropName("孤儿那件");
        File.WriteAllText(Path.Combine(_dir, name), "用户自己拷进来的");

        var r = await Audit();

        Assert.Equal(new[] { name }, r.OrphanNames);
        Assert.True(File.Exists(Path.Combine(_dir, name)));   // ★ 启动那一条只数不删：清理得用户点（出口见清理那一档）
    }

    [Fact]
    public async Task SecondRunCountsExactlyTheSame()
    {
        // 图片那一族要专门验"不重复标"，因为标记是状态；drops 没有标记，
        // 所以这里验的是它真的没攒下任何状态：两次读数逐项相等。
        var name = DropName("幂等的那件");
        await RecDropRowAsync("丙", 0, name);
        File.WriteAllText(Path.Combine(_dir, name), "本体");
        File.Delete(Path.Combine(_dir, name));
        File.WriteAllText(Path.Combine(_dir, ClipDropAssets.TempNameOf(DropName("残货"))), "半件");

        var first = await Audit();
        var second = await Audit();

        Assert.Equal(first.MissingNames, second.MissingNames);
        Assert.Equal(first.OrphanNames, second.OrphanNames);
        Assert.Equal(first.TempNames, second.TempNames);
    }

    [Fact]
    public async Task RowsWithoutAnyClaimsDoNotHideTheOrphans()
    {
        // 认领名单按行读，行可能一条都不认领（普通文本/图片行）——它们不该把"谁认领了"这件事搅浑。
        var orphan = DropName("孤儿那件");
        File.WriteAllText(Path.Combine(_dir, orphan), "没人要的");
        await RecDropRowAsync("无认领的文本行", 0);
        await RecDropRowAsync("另一条", 30);

        var r = await Audit();

        Assert.Equal(new[] { orphan }, r.OrphanNames);
    }

    /// <summary>跑一次对账。<b>null＝没跑成，测试要在这里就炸得可见</b>，而不是在下一行断言里报个莫名 NullReference。</summary>
    private async Task<ClipDropAssets.ReconcileDropResult> Audit()
        => await ClipboardDropAudit.RunAsync(_repo, CancellationToken.None)
           ?? throw new InvalidOperationException("拖入文件对账没跑成（返回 null＝出错），见日志");

    // ────────── 闸门：几条"只有源码能证"的规矩 ──────────

    [Fact]
    public void TheClaimsQueryHasNoRowWindow()
    {
        // 借一个带 limit 的查询来对账，窗口外的行会被当成"没人认领的文件"⇒ 完好的目录被报成一堆孤儿
        // （与图片那一族同一条教训，见 ClipboardReconcileTests）。
        var body = MethodBodyOf("public async Task<IReadOnlyList<IReadOnlyList<string>>> GetClipboardDropClaimsAsync");
        Assert.DoesNotContain("LIMIT", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheStartupPathDeletesNothingAndWritesNothing()
    {
        var run = MethodBodyOf("public static async Task<ClipDropAssets.ReconcileDropResult?> RunAsync(");
        foreach (var banned in new[] { "File.Delete", "TryDelete", "Directory.Delete", "CleanAsync(", "SetClipboardMissingFlags" })
            Assert.DoesNotContain(banned, run);
        Assert.Contains("启动只数不删", run);
        // 而"三类怎么分"这件事仍只有一份出处：孤儿口径不许在别处再算一遍。
        var audit = ReadRepoFile("src/StarMark.Integrations/Clipboard/ClipboardDropAudit.cs");
        Assert.Equal(1, SourceGate.Count(audit, "ClipDropAssets.Reconcile(claims"));
    }

    [Fact]
    public void AuditRunsAtStartupBesideTheImageAuditAndNotBehindASwitch()
    {
        // §4 同一条：安全护栏默认开、不设关——留一个"可以关掉不看"的口子就没护栏了。
        // 且与图片对账同一拍顺跑（一族对不上不该连累另一族的节奏）。
        var app = SourceGate.Squash(ReadRepoFile("src/StarMark.UI/App.xaml.cs"));
        Assert.Contains("ClipboardAssetAudit", app);
        Assert.Contains("ClipboardDropAudit .RunAsync(", app);
        Assert.DoesNotContain("DropReconcileEnabled", app);
    }

    private static string MethodBodyOf(string fragment)
    {
        foreach (var file in new[]
        {
            "src/StarMark.Data/ItemRepository.Local.cs",
            "src/StarMark.Integrations/Clipboard/ClipboardDropAudit.cs",
        })
        {
            var source = ReadRepoFile(file);
            if (!source.Contains(fragment, StringComparison.Ordinal)) continue;
            return SourceGate.MethodBody(source, fragment);
        }
        throw new InvalidOperationException($"闸门锚点没命中任何文件：{fragment}");
    }

    private static string ReadRepoFile(string relative) => SourceGate.ReadRepoFile(relative);
}
