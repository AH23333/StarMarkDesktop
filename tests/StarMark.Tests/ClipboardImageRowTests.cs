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

    // ────────── 写回：像素身份要活过一次 PNG 往返 ──────────

    [Fact]
    public async Task WriteBackIdentitySurvivesThePngRoundTrip()
    {
        // "再复制一张图"能不能挡下自家回声，前提是我们写出去的 PNG 再解回来与当初<b>逐字节相等</b>：
        // 采集侧登记的是"归一后 BGRA"的哈希，而不是文件字节（系统会把 PNG 重排成 CF_DIB 再广播回来）。
        // 这一步不等，症状就是"每点一次重复制，历史多一条"——而且每次都多，不是偶发。
        // （系统重排那一段仍只能真机验，见 §6；这里只保证我们自己那一半不出错。）
        var bgra = Screenshot(8, 6);
        var png = await ClipboardImageStore.EncodePngAsync(bgra, 8, 6, CancellationToken.None);
        Assert.NotNull(png);

        // 走"那条读路"本身：把 PNG 落到 clip 目录，再用条目侧唯一的解码入口读回来。
        File.WriteAllBytes(ClipAssets.FullPathOf("round_trip.png")!, png!);
        Assert.True(ClipboardImageStore.TryReadPngFrame("round_trip.png", out var back, out var why), why ?? "读不出帧");
        Assert.Equal((8, 6), (back.Width, back.Height));
        Assert.Equal(bgra, back.Bgra);
        Assert.Equal(ClipboardPolicy.BuildImageSourceId(bgra), ClipboardPolicy.BuildImageSourceId(back.Bgra));
    }

    [Fact]
    public async Task PixelEchoTokenFromTheRoundTripActuallyBlocksTheFrame()
    {
        // 上一条只证"哈希相同"，这一条证"哈希相同真的被用上了"：登记往返后的像素，
        // 采集侧拿同一份像素去问 ShouldSkip 必须回 true。两边各自正确但键的算法不同，是这一处最难发现的坏法。
        var bgra = Screenshot(4, 4);
        var png = await ClipboardImageStore.EncodePngAsync(bgra, 4, 4, CancellationToken.None);
        File.WriteAllBytes(ClipAssets.FullPathOf("echo_round.png")!, png!);
        Assert.True(ClipboardImageStore.TryReadPngFrame("echo_round.png", out var back, out _));

        var dedupe = new ClipboardDedupe();
        dedupe.NoteOwnWrite(back.Bgra, 1_000);
        Assert.True(dedupe.ShouldSkip(bgra, 1_001));
    }

    [Fact]
    public void UnsafeOrMissingNameIsRefusedInItsOwnWords()
    {
        // 这一句也是 P3 贴图要用的入口：它给不出帧时必须说得出为什么，否则"贴不出去"是一句猜谜。
        Assert.False(ClipboardImageStore.TryReadPngFrame("../escape.png", out _, out var unsafeName));
        Assert.Contains("不安全", unsafeName, StringComparison.Ordinal);
        Assert.False(ClipboardImageStore.TryReadPngFrame(null, out _, out var none));
        Assert.Contains("没有文件", none, StringComparison.Ordinal);
        Assert.False(ClipboardImageStore.TryReadPngFrame("2026-09-28_0915_00000000.png", out _, out var gone));
        Assert.Contains("读不出来", gone, StringComparison.Ordinal);      // 合法名字但文件不在：也不是异常，是一句原话
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
