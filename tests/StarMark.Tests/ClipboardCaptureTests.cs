#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Data;
using StarMark.Integrations.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 采集裁决链（批次 ID）：<c>去重 → 策略 → 建条目 → 落库</c>。
/// <para>
/// Win32 那半（读 HGLOBAL、消息窗口）在这台机器上没法可信地测，所以整条判定被刻意做成
/// <see cref="ClipboardCapture.CaptureAsync"/> 一个静态方法，输入全是普通参数（含时间戳与前台进程名）。
/// 本文件因此能在单机上把"什么会被记进明文库"这件事钉死，而不必真去操作用户的剪贴板。
/// </para>
/// </summary>
public sealed class ClipboardCaptureTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_cap_{Guid.NewGuid():N}.db");
    private readonly ItemRepository _repo;

    public ClipboardCaptureTests()
    {
        var factory = new DbConnectionFactory(_db);
        new MigrationRunner(factory).EnsureSchema();
        _repo = new ItemRepository(factory);
    }

    public void Dispose()
    {
        foreach (var p in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(p); } catch { }
    }

    private static long Ms(long offset = 0) => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + offset;

    private Task<Item?> Cap(ClipboardDedupe d, string? raw, string? app = "chrome",
        string format = ClipboardEntry.FormatText, long? now = null)
        => ClipboardCapture.CaptureAsync(_repo, d, raw, format, app, now ?? Ms());

    private async Task<int> Count() => (await _repo.GetBySourceAsync(ItemSources.Clipboard, ItemType.Clipboard)).Count;

    [Fact]
    public async Task NormalCopy_IsRecordedWithAppAndCountOne()
    {
        var item = await Cap(new ClipboardDedupe(), "第一段正常内容", app: "Code");

        Assert.NotNull(item);
        Assert.Equal(1, ClipboardEntry.CopyCount(item!));
        Assert.Equal("Code", ClipboardEntry.App(item!));
        Assert.Equal(1, await Count());
    }

    [Fact]
    public async Task PasswordManagerSource_IsNeverStored()
    {
        // 这条断言是整个功能的价值所在：内容完全正常，只因为来源是密码管理器就不许落盘。
        Assert.Null(await Cap(new ClipboardDedupe(), "看起来正常的一段文字", app: "KeePass"));
        Assert.Equal(0, await Count());
    }

    [Fact]
    public async Task SecretShapes_AreNeverStored_EvenFromAllowedApp()
    {
        var d = new ClipboardDedupe();
        Assert.Null(await Cap(d, "-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA", now: Ms(0)));
        Assert.Null(await Cap(d, "4111 1111 1111 1111", now: Ms(5_000)));
        Assert.Null(await Cap(d, "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w", now: Ms(10_000)));
        Assert.Equal(0, await Count());
    }

    [Fact]
    public async Task BurstNotificationsFromOneCopy_RecordOnceButCountedAgainAfterWindow()
    {
        var d = new ClipboardDedupe();
        var t = Ms();

        Assert.NotNull(await Cap(d, "Office 连发同一条", now: t));
        Assert.Null(await Cap(d, "Office 连发同一条", now: t + 50));      // 同一次复制的重复通知
        Assert.Null(await Cap(d, "Office 连发同一条", now: t + 600));

        var later = await Cap(d, "Office 连发同一条", now: t + 60_000);   // 出窗口 ⇒ 真又复制了一次
        Assert.NotNull(later);
        Assert.Equal(2, ClipboardEntry.CopyCount(later!));
        Assert.Equal(1, await Count());                                   // 仍然是同一条历史
    }

    [Fact]
    public async Task StarMarkOwnCopy_IsNotReRecorded_ButLaterRealCopyStillIs()
    {
        var d = new ClipboardDedupe();
        var t0 = Ms();
        Assert.NotNull(await Cap(d, "用户从历史页复制的文本", now: t0));

        d.NoteOwnWrite("用户从历史页复制的文本", t0);
        // 取 t0+1 s（已超出 700 ms 连发窗）：吞掉它的只可能是回声登记，而不是去抖——
        // 两个机制都在这个类里，测试必须说清是哪一道在起作用。
        Assert.Null(await Cap(d, "用户从历史页复制的文本", now: t0 + 1_000));

        var again = await Cap(d, "用户从历史页复制的文本", now: t0 + 60_000);   // 之后用户真的又抄了一次
        Assert.NotNull(again);
        Assert.Equal(2, ClipboardEntry.CopyCount(again!));
    }

    [Fact]
    public async Task EmptyAndShortAndBusyFrames_SilentlyProduceNothing()
    {
        var d = new ClipboardDedupe();
        Assert.Null(await Cap(d, null));
        Assert.Null(await Cap(d, ""));
        Assert.Null(await Cap(d, "x", now: Ms(1)));       // 单字符
        Assert.Equal(0, await Count());
    }

    [Fact]
    public async Task FileListCopy_StoresPathsAsFilesFormat()
    {
        var item = await Cap(new ClipboardDedupe(), "C:\\a.txt\nD:\\b.docx", app: "explorer",
            format: ClipboardEntry.FormatFiles);

        Assert.NotNull(item);
        Assert.Equal(ClipboardEntry.FormatFiles, ClipboardEntry.Format(item!));
        Assert.Equal("C:\\a.txt…", item!.Title);           // 标题取首行，正文保留全部路径
        Assert.Equal("C:\\a.txt\nD:\\b.docx", item.Description);
    }

    [Fact]
    public async Task UnknownForegroundApp_IsAllowedButLabeled()
    {
        // 取不到前台进程名不能成为"整条历史不记"的理由——但也不能显示成"来源可信"。
        // ✅P-199：那颗「未知来源」不再写进 items.subtitle（库里只存中性形状），
        // 显示侧对空副标题当场念（clip.sub.unknown，三语读数在 ClipboardStoredShapeGateTests）；
        // 本格只钉"记了＋库里零词"。
        var item = await Cap(new ClipboardDedupe(), "来源不明的一段内容", app: null);

        Assert.NotNull(item);
        Assert.Equal(string.Empty, item!.Subtitle);
    }

    [Fact]
    public async Task Capture_NullRepoOrDedupe_ThrowsInsteadOfSwallowing()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => ClipboardCapture.CaptureAsync(null!, new ClipboardDedupe(), "x", ClipboardEntry.FormatText, "chrome", Ms()));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => ClipboardCapture.CaptureAsync(_repo, null!, "x", ClipboardEntry.FormatText, "chrome", Ms()));
    }

    // ── ✅P-306：拖进剪贴板格的两口。显式动作：不走去抖/回声（那是"系统广播"那一路的防重），
    // 只过同一道策略门（过短、敏感形状）；前台排除清单不适用（拖进来是用户亲手指定要记的那一下）。──

    private Task<Item?> ImportText(string? raw)
        => ClipboardCapture.ImportDroppedTextAsync(_repo, raw);

    private Task<Item?> ImportFiles(params string[] paths)
        => ClipboardCapture.ImportDroppedFilesAsync(_repo, paths);

    [Fact]
    public async Task DroppedText_IsRecordedWithoutDedupeOrEcho()
    {
        var first = await ImportText("拖进剪贴板格的文本");
        Assert.NotNull(first);
        Assert.Equal(ClipboardEntry.FormatText, ClipboardEntry.Format(first!));
        Assert.Equal(1, await Count());

        // 再拖同一段＝显式再来一次：不去重，copy count 累加、仍然只有一条历史
        // （与采集"出连发窗后再复制"同口径——upsert by (source, source_id)，不是又一行）。
        var again = await ImportText("拖进剪贴板格的文本");
        Assert.NotNull(again);
        Assert.Equal(2, ClipboardEntry.CopyCount(again!));
        Assert.Equal(1, await Count());
    }

    [Fact]
    public async Task DroppedText_EmptyShortOrSensitiveShapes_Rejected()
    {
        Assert.Null(await ImportText(null));
        Assert.Null(await ImportText(""));
        Assert.Null(await ImportText("x"));
        Assert.Null(await ImportText("-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA"));
        Assert.Equal(0, await Count());
    }

    [Fact]
    public async Task DroppedFiles_JoinPathsAsTheSameShapeAsACapture()
    {
        var item = await ImportFiles("C:\\a.txt", "D:\\b.docx");

        Assert.NotNull(item);
        Assert.Equal(ClipboardEntry.FormatFiles, ClipboardEntry.Format(item!));
        Assert.Equal("C:\\a.txt…", item!.Title);           // 与真实 CF_HDROP 采集同一形状：标题＝首行，正文＝全部路径
        Assert.Equal("C:\\a.txt\nD:\\b.docx", item.Description);
    }

    [Fact]
    public async Task DroppedFiles_WithoutUsablePaths_Rejected()
    {
        Assert.Null(await ImportFiles());
        Assert.Null(await ImportFiles("", "  "));
        Assert.Equal(0, await Count());
    }

    [Fact]
    public async Task Import_NullRepoOrPaths_ThrowsInsteadOfSwallowing()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ClipboardCapture.ImportDroppedTextAsync(null!, "x"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ClipboardCapture.ImportDroppedFilesAsync(null!, new[] { "C:\\a" }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ClipboardCapture.ImportDroppedFilesAsync(_repo, null!));
    }

    // ────────── ✅P-307 第二刀・乙：认领名单跟着行走 ──────────

    [Fact]
    public async Task DroppedFiles_ClaimedDropNamesLandInTheRecordedRow()
    {
        var item = await ClipboardCapture.ImportDroppedFilesAsync(_repo, new[] { "C:\\报告 v1.pdf" },
            claimedDropNames: new[] { "9f2a3b8c4d5e_报告 v1.pdf" });

        Assert.NotNull(item);
        Assert.Equal(ClipboardEntry.FormatFiles, ClipboardEntry.Format(item!));
        // 名单带在草稿上（不是落库后补写）：行落库那一刻起件就有主了——
        // 补写要在合并过的行上再动一次 extra，多一次读写就多一处"件在、名字不在"的孤儿窗口。
        Assert.Equal(new[] { "9f2a3b8c4d5e_报告 v1.pdf" }, ClipboardEntry.DropFileNamesOf(item!.ExtraJson));
    }

    [Fact]
    public async Task DroppedFiles_SameSourceAgain_UnionsOldAndNewClaims()
    {
        var first = await ClipboardCapture.ImportDroppedFilesAsync(_repo, new[] { "C:\\同一批.pdf" },
            claimedDropNames: new[] { "9f2a3b8c4d5e_第一件.pdf" });
        var second = await ClipboardCapture.ImportDroppedFilesAsync(_repo, new[] { "C:\\同一批.pdf" },
            claimedDropNames: new[] { "0123456789ab_第二件.docx" });

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);                                   // 同源合并成同一行
        Assert.Equal(2, ClipboardEntry.CopyCount(second));
        // 旧认领没丢、新认领并进来：丢任何一边，物化出去的件当场变孤儿。
        Assert.Equal(new[] { "9f2a3b8c4d5e_第一件.pdf", "0123456789ab_第二件.docx" },
            ClipboardEntry.DropFileNamesOf(second!.ExtraJson));
    }

    [Fact]
    public async Task DroppedFiles_WithoutClaims_LeavesNoClaimKeyBehind()
    {
        var item = await ClipboardCapture.ImportDroppedFilesAsync(_repo, new[] { "C:\\普通的本机件.txt" });

        Assert.NotNull(item);
        Assert.Empty(ClipboardEntry.DropFileNamesOf(item!.ExtraJson));        // 本机件没有物化，就没有认领
    }
}
