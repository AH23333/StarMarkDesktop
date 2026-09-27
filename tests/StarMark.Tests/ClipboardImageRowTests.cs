#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
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
/// 一条剪贴板<b>图片</b>历史对外暴露的三件事（ClipIMG-2a）：
/// <b>地址</b>（预览与"打开位置"问的是 <c>Item.Uri</c>）、<b>回放之后地址仍然指得到文件</b>、
/// 以及<b>写回剪贴板时认得出"这是我们自己刚写的那一张"</b>。
/// <para>
/// 三条的共同点：它们都不在采集链上，出错时不会崩，只会<b>安静地不好用</b>——
/// "预览打不开""同一张图越点越多""这一格是空的但没人说为什么"。所以每条都在这里钉死，
/// 而不是等真机验收时靠眼睛发现。
/// </para>
/// </summary>
public sealed class ClipboardImageRowTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_cliprow_{Guid.NewGuid():N}.db");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"starmark_cliprow_{Guid.NewGuid():N}");
    private readonly ItemRepository _repo;

    public ClipboardImageRowTests()
    {
        var factory = new DbConnectionFactory(_db);
        new MigrationRunner(factory).EnsureSchema();
        _repo = new ItemRepository(factory);
        Directory.CreateDirectory(_dir);
        ClipAssets.FolderOverride = _dir;        // 写侧/删侧/读侧共用的那一个改道（见 ClipAssets 注释）
    }

    public void Dispose()
    {
        ClipAssets.FolderOverride = null;
        foreach (var p in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(p); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static DateTimeOffset At(int minutes)
        => new DateTimeOffset(2026, 9, 28, 9, 5, 0, TimeSpan.FromHours(8)).AddMinutes(minutes);

    private static Item Draft(string tag, int minutes)
    {
        var sid = ClipboardPolicy.BuildImageSourceId(Encoding.UTF8.GetBytes(tag));
        var when = At(minutes);
        return ClipboardEntry.BuildImage(sid, new ClipboardEntry.ImageMeta(
            ClipAssets.MainNameOf(sid, when), ClipAssets.ThumbNameOf(sid, when), 800, 600, 4321), "Code", when);
    }

    // ────────── 地址：图片行的"可打开的东西" ──────────

    [Theory]
    [InlineData(null)]                 // 文本行根本没有文件名
    [InlineData("")]
    [InlineData("../outside.png")]     // 备份文件是用户可以手改的文本
    [InlineData("sub/inside.png")]
    [InlineData("C:\\windows\\x.png")]
    [InlineData("wrong-extension.png.txt")]
    [InlineData("trailing-dot.png.")]
    public void UnsafeOrAbsentNamesNeverBecomeAnAddress(string? name)
        => Assert.Equal(string.Empty, ClipboardEntry.UriOf(name));

    [Fact]
    public void SafeNameBecomesAFileUrlUnderTheClipFolder()
    {
        var uri = ClipboardEntry.UriOf("2026-09-28_0905_9f2a3b8c.png");
        Assert.StartsWith("file:///", uri, StringComparison.Ordinal);     // 三斜杠：本地绝对路径的规范形式
        Assert.EndsWith("/2026-09-28_0905_9f2a3b8c.png", uri, StringComparison.Ordinal);
        Assert.Equal(new Uri(ClipAssets.FullPathOf("2026-09-28_0905_9f2a3b8c.png")!).AbsoluteUri, uri);
    }

    [Fact]
    public async Task ReplayPointsTheAddressAtTheNameThatSurvived()
    {
        // 同图再复制：MergeForReplay 保住<b>早上</b>那个名字（否则旧文件变孤儿），
        // 而 draft.Uri 是按<b>这次</b>算出的新名字拼的。两处不同源 ⇒ 预览指到一个不存在的文件，
        // 而这一条在界面上看起来完全正常（有图、有尺寸、有来源）。
        var morning = await _repo.RecordClipboardAsync(Draft("same", 0), CancellationToken.None);
        var afternoon = await _repo.RecordClipboardAsync(Draft("same", 300), CancellationToken.None);

        Assert.Equal(ClipboardEntry.FileName(morning), ClipboardEntry.FileName(afternoon));
        Assert.NotEqual(ClipAssets.MainNameOf(SidOf(afternoon), At(300)), ClipboardEntry.FileName(afternoon)!);
        Assert.EndsWith("/" + ClipboardEntry.FileName(afternoon), afternoon.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoredColumnAndReturnedRowAgreeOnTheAddress()
    {
        // 库里那一列与交回内存的那一行必须是同一个地址：UPSERT 的 UPDATE 集刻意不含 uri
        // （回放不许改标题以外的既有事实），所以这里钉的是"两条路都指向幸存的那个名字"。
        await _repo.RecordClipboardAsync(Draft("same", 0), CancellationToken.None);
        var returned = await _repo.RecordClipboardAsync(Draft("same", 300), CancellationToken.None);
        var stored = (await _repo.GetBySourceAsync(ItemSources.Clipboard, ItemType.Clipboard))
            .Single(i => i.Id == returned.Id);
        Assert.Equal(returned.Uri, stored.Uri);
        Assert.Equal(ClipboardEntry.UriOfMerged(returned.ExtraJson), stored.Uri);
    }

    [Fact]
    public async Task TextRowsStillCarryNoAddress()
    {
        // P1 的验收级前提：这一路加了"按合并后的名字校正 Uri"之后，文本行仍须一个字符不变。
        var first = await _repo.RecordClipboardAsync(
            ClipboardEntry.Build("同一段文字", "Code", ClipboardEntry.FormatText, At(0)), CancellationToken.None);
        var again = await _repo.RecordClipboardAsync(
            ClipboardEntry.Build("同一段文字", "Code", ClipboardEntry.FormatText, At(300)), CancellationToken.None);
        Assert.Equal(string.Empty, first.Uri);
        Assert.Equal(string.Empty, again.Uri);
        Assert.Equal(2, ClipboardEntry.CopyCountOf(again.ExtraJson));   // 幂等回放本身没被改动碰坏
    }

    // ────────── 写回：交出去的是数据，不是路径；像素身份要活过一次 PNG 往返 ──────────

    /// <summary>一条图片历史，文件名由测试给（安全不安全都照原样进 extra——校验归入口那一处）。</summary>
    private static Item ImageRowNamed(string mainName) => ClipboardEntry.BuildImage(
        ClipboardPolicy.BuildImageSourceId(Encoding.UTF8.GetBytes("row")),
        new ClipboardEntry.ImageMeta(mainName, mainName + ".thumb.jpg", 8, 6, 2048), "Code", At(0));

    [Fact]
    public async Task WriteBackHandsOverBytesNotAFilePath()
    {
        // 真机坏法复盘：把 file:// URI 交给 SetBitmap，系统按"复制了一个文件"呈现 ⇒
        // 目标程序粘出来是一串路径，而采集侧把那行路径又记成一条新历史（每点一次多一条）。
        // 所以入口必须一次给齐"字节 + 像素"：字节是要上交剪贴板的那份数据，像素是回声登记的原料。
        var bgra = Screenshot(8, 6);
        var png = await ClipboardImageStore.EncodePngAsync(bgra, 8, 6, CancellationToken.None);
        Assert.NotNull(png);
        File.WriteAllBytes(ClipAssets.FullPathOf("round_trip.png")!, png!);

        Assert.True(ClipboardImageStore.TryReadEntryImage(ImageRowNamed("round_trip.png"),
            out var bytes, out var back, out var why), why ?? "读不出帧");
        Assert.Equal(png, bytes);                                   // 交出去的那份就是文件里的 PNG 字节
        Assert.Equal((8, 6), (back.Width, back.Height));
        Assert.Equal(bgra, back.Bgra);
        Assert.Equal(ClipboardPolicy.BuildImageSourceId(bgra), ClipboardPolicy.BuildImageSourceId(back.Bgra));
    }

    [Fact]
    public void StreamOfCarriesTheSameBytesOutOfProcess()
    {
        // "数据不是路径"落在 WinRT 上就是这一颗内存流：字节进出必须一致，且位置回到 0
        //（流停在末尾的话，目标程序读到的是一段空数据 ⇒ "复制成功、粘出来什么都没有"）。
        var blob = new byte[] { 1, 2, 3, 250, 251, 252 };
        var stream = ClipboardImageStore.StreamOf(blob);
        Assert.Equal(0L, (long)stream.Position);
        using var inner = stream.AsStreamForRead();
        var read = new byte[blob.Length];
        inner.ReadExactly(read);
        Assert.Equal(blob, read);
        Assert.Equal(blob, read);
    }

    [Fact]
    public async Task PixelEchoTokenFromTheRoundTripActuallyBlocksTheFrame()
    {
        // 上一条只证"哈希相同"，这一条证"哈希相同真的被用上了"：登记往返后的像素，
        // 采集侧拿同一份像素去问 ShouldSkip 必须回 true。两边各自正确但键的算法不同，是这一处最难发现的坏法。
        var bgra = Screenshot(4, 4);
        var png = await ClipboardImageStore.EncodePngAsync(bgra, 4, 4, CancellationToken.None);
        File.WriteAllBytes(ClipAssets.FullPathOf("echo_round.png")!, png!);
        Assert.True(ClipboardImageStore.TryReadEntryFrame(ImageRowNamed("echo_round.png"), out var back, out _));

        var dedupe = new ClipboardDedupe();
        dedupe.NoteOwnWrite(back.Bgra, 1_000);
        Assert.True(dedupe.ShouldSkip(bgra, 1_001));
    }

    [Fact]
    public void EntriesWithoutAReadableFileAreRefusedInTheirOwnWords()
    {
        // 这一句也是 P3 贴图要用的入口：它给不出帧时必须说得出为什么，否则"贴不出去"是一句猜谜。
        Assert.False(ClipboardImageStore.TryReadEntryFrame(ImageRowNamed("../escape.png"), out _, out var unsafeName));
        Assert.Contains("不安全", unsafeName, StringComparison.Ordinal);
        Assert.False(ClipboardImageStore.TryReadEntryFrame(
            ClipboardEntry.Build("一段文字", "Code", ClipboardEntry.FormatText, At(0)), out _, out var none));
        Assert.Contains("没有文件", none, StringComparison.Ordinal);
        Assert.False(ClipboardImageStore.TryReadEntryFrame(ImageRowNamed("2026-09-28_0915_00000000.png"),
            out _, out var gone));                                  // 合法名字但文件不在：也不是异常，是一句原话
        Assert.Contains("不在本机", gone, StringComparison.Ordinal);
        // 先 stat 再读：漏那一步，这里交出去的是"文件读不出来（FileNotFoundException）"——
        // _exception 名对用户不是一句原话，而"已经不在这台机器上"是（2d 起两个入口共用同一句）。
        Assert.DoesNotContain("Exception", gone, StringComparison.Ordinal);
    }

    /// <summary>一块"像截图"的像素：逐像素变化的 BGR，alpha 恒 255（剪贴板图没有半透明语义，见 ClipAssets 的 DIB 口径）。</summary>
    private static byte[] Screenshot(int w, int h)
    {
        var p = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            p[i * 4] = (byte)(i * 7);
            p[i * 4 + 1] = (byte)(i * 13);
            p[i * 4 + 2] = (byte)(i * 29);
            p[i * 4 + 3] = 255;
        }
        return p;
    }

    private static string SidOf(Item item) => item.SourceId;
}
