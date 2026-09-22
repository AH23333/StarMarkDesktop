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
        Assert.NotNull(await Cap(d, "用户从历史页复制的文本", now: Ms()));

        d.NoteOwnWrite("用户从历史页复制的文本");
        Assert.Null(await Cap(d, "用户从历史页复制的文本", now: Ms(5_000)));   // 回声：不记、不加次数

        var again = await Cap(d, "用户从历史页复制的文本", now: Ms(60_000));   // 之后用户真的又抄了一次
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
        // 取不到前台进程名不能成为"整条历史不记"的理由——但也不能显示成"来源可信"，故标"未知来源"。
        var item = await Cap(new ClipboardDedupe(), "来源不明的一段内容", app: null);

        Assert.NotNull(item);
        Assert.Contains("未知来源", item!.Subtitle);
    }

    [Fact]
    public async Task Capture_NullRepoOrDedupe_ThrowsInsteadOfSwallowing()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => ClipboardCapture.CaptureAsync(null!, new ClipboardDedupe(), "x", ClipboardEntry.FormatText, "chrome", Ms()));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => ClipboardCapture.CaptureAsync(_repo, null!, "x", ClipboardEntry.FormatText, "chrome", Ms()));
    }
}
