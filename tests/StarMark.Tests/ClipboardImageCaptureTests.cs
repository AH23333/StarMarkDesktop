#nullable enable
using System;
using System.IO;
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
/// 剪贴板图片历史的<b>裁决层</b>（批次 ClipIMG-P1-1b）：三层门禁、image 条目构造、
/// 以及"去重 → 门禁 → 落库 → 才写文件"这条端到端顺序。
/// <para>
/// 端到端这几条刻意用<b>真仓储 + 真临时目录</b>跑：图片这条链最容易出错的地方恰好是"行与文件对不上"
/// （写了行没写文件＝点开是空的；写了文件没写行＝用户目录里多个没人认领的孤儿），
/// 这两件事都只有把两边都真做一遍才看得见。<see cref="ClipboardImageStore.FolderOverride"/>
/// 就是为这几条存在的——与 <c>StarLog.DirectoryOverride</c> 同一课：单测不许往用户目录里写东西。
/// </para>
/// </summary>
public sealed class ClipboardImageCaptureTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_clipimg_{Guid.NewGuid():N}.db");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"starmark_clipimg_{Guid.NewGuid():N}");
    private readonly ItemRepository _repo;

    public ClipboardImageCaptureTests()
    {
        var factory = new DbConnectionFactory(_db);
        new MigrationRunner(factory).EnsureSchema();
        _repo = new ItemRepository(factory);
        ClipAssets.FolderOverride = _dir;
    }

    public void Dispose()
    {
        ClipAssets.FolderOverride = null;
        foreach (var p in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(p); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ────────── 造一帧图片（与字节层测试同一套头部写法）──────────

    private static ClipboardNative.ImageRead DibFrame(int width, int height)
    {
        var bytes = new System.Collections.Generic.List<byte>();
        void U32(uint v) => bytes.AddRange(new[] { (byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24) });
        void U16(ushort v) => bytes.AddRange(new[] { (byte)v, (byte)(v >> 8) });
        U32(40); U32((uint)width); U32(unchecked((uint)-height));   // 负高＝自上而下
        U16(1); U16(32); U32(0); U32(0); U32(0); U32(0); U32(0); U32(0);
        for (var i = 0; i < width * height; i++)
            bytes.AddRange(new byte[] { (byte)(i * 7 % 251), (byte)(i * 13 % 251), (byte)(i * 29 % 251), 0 });
        return new ClipboardNative.ImageRead(
            new ClipboardPayload.ImageFrame(width, height, bytes.ToArray()), null, width, height, ClipboardPayload.FormatDib);
    }

    private static DateTimeOffset When() => new(2026, 9, 27, 14, 32, 0, TimeSpan.FromHours(8));

    private Task<Item?> Cap(ClipboardDedupe d, ClipboardNative.ImageRead frame, string? app = "Code")
        => ClipboardCapture.CaptureImageAsync(_repo, d, frame, app, When(), ct: CancellationToken.None);

    private async Task<int> Rows() => (await _repo.GetBySourceAsync(ItemSources.Clipboard, ItemType.Clipboard)).Count;

    // ────────── 三层门禁（纯函数，逐臂）──────────

    [Theory]
    [InlineData(ClipboardPolicy.MaxImageBytes, true)]              // 正好 20MB 收
    [InlineData(ClipboardPolicy.MaxImageBytes + 1, false)]         // 超一字节就拒
    [InlineData(3_000_000, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void ByteCeilingIsTheFirstLayer(long bytes, bool expected)
    {
        var ok = ClipboardPolicy.ShouldRecordImage(400, 300, bytes, "Code", out var reason);
        Assert.Equal(expected, ok);
        if (!ok) Assert.False(string.IsNullOrWhiteSpace(reason));   // 拒收必须给得出原因（哑丢弃是缺陷）
    }

    [Theory]
    [InlineData(16, 16, true)]
    [InlineData(15, 400, false)]
    [InlineData(400, 15, false)]
    [InlineData(1, 1, false)]
    public void TinyImagesAreNotHistoryWorthy(int w, int h, bool expected)
        => Assert.Equal(expected, ClipboardPolicy.ShouldRecordImage(w, h, 4096, "Code", out _));

    [Theory]
    [InlineData("KeePass", false)]
    [InlineData("1Password-8", false)]
    [InlineData("Code", true)]
    [InlineData(null, true)]            // 取不到来源＝按"未知"处理，不因此丢历史
    public void TheSameSourceListGuardsPicturesAsText(string? app, bool expected)
        => Assert.Equal(expected, ClipboardPolicy.ShouldRecordImage(200, 100, 4096, app, out _));

    [Fact]
    public void RejectionReasonsSayWhichLayerRefused()
    {
        Assert.Contains("20 MB", Ask(ClipboardPolicy.MaxImageBytes * 3L));
        Assert.Contains("小于", Ask(9));
        return;
        static string Ask(long bytes)
        {
            ClipboardPolicy.ShouldRecordImage(bytes > ClipboardPolicy.MaxImageBytes ? 400 : 4,
                bytes > ClipboardPolicy.MaxImageBytes ? 300 : 4, bytes, "Code", out var why);
            return why ?? "";
        }
    }

    [Theory]
    [InlineData(0, ClipboardPolicy.DefaultImageMaxEntries)]      // 坏值（0/负）回默认，而不是"记一条删一条"
    [InlineData(-5, ClipboardPolicy.DefaultImageMaxEntries)]
    [InlineData(9, ClipboardPolicy.MinEntries)]                  // 低于下界夹到 10
    [InlineData(200, 200)]
    [InlineData(2001, ClipboardPolicy.ImageMaxEntriesCeil)]      // 高于上界夹住
    public void ImageCapSettingHasExactlyOneRoundingRule(int stored, int expected)
        => Assert.Equal(expected, ClipboardPolicy.ClampImageMaxEntries(stored));

    [Theory]
    [InlineData(0, ClipboardPolicy.MaxEntries)]
    [InlineData(500, 500)]                                        // 默认值＝现行为，只是从常量变成可见
    [InlineData(999999, ClipboardPolicy.TextMaxEntriesCeil)]
    public void TextCapKeepsTheOldBehaviourAndOnlyBecomesVisible(int stored, int expected)
        => Assert.Equal(expected, ClipboardPolicy.ClampTextMaxEntries(stored));

    // ────────── image 条目构造 ──────────

    [Fact]
    public void ImageRowCarriesGeometryAndFileNames_AndNoBody()
    {
        var meta = new ClipboardEntry.ImageMeta("a.png", "a_thumb.jpg", 3840, 2160, 4_194_304);
        var item = ClipboardEntry.BuildImage("c-9f2a3b8c1d2e3f405162738495a6b7c8", meta, "Code", When());

        Assert.Equal(ItemType.Clipboard, item.Type);
        Assert.Equal(ItemSources.Clipboard, item.Source);
        // 图片行的"可打开的东西"就是那张 PNG（ClipIMG-2a）：预览与"打开位置"都从 Uri 走。
        // 期望值在测试里独立拼一遍路径，这样"ThreeSlashes/正斜杠"这类 URI 形式写坏了才看得见。
        Assert.Equal(new Uri(Path.Combine(ClipAssets.Folder, "a.png")).AbsoluteUri, item.Uri);
        Assert.Equal(string.Empty, item.Description);            // 正文在文件里，库里不存第二份
        Assert.Equal("3840×2160", item.Title);
        Assert.Contains("4 MB", item.Subtitle);
        Assert.Equal(ClipboardEntry.FormatImage, ClipboardEntry.Format(item));
        Assert.Equal((3840, 2160), ClipboardEntry.ImageSize(item));
        Assert.Equal(4_194_304, ClipboardEntry.ImageBytes(item));
        Assert.Equal("a.png", ClipboardEntry.FileName(item));
        Assert.Equal("a_thumb.jpg", ClipboardEntry.ThumbFileName(item));
        Assert.False(ClipboardEntry.IsMissing(item));
    }

    [Fact]
    public void TextRowsDoNotPretendToHaveGeometry()
    {
        var text = ClipboardEntry.Build("一段普通的文字", "Code", ClipboardEntry.FormatText, When());
        Assert.Null(ClipboardEntry.FileName(text));
        Assert.Equal((0, 0), ClipboardEntry.ImageSize(text));
        Assert.Equal(0, ClipboardEntry.ImageBytes(text));
        Assert.False(ClipboardEntry.IsMissing(text));
    }

    [Fact]
    public void ReplayKeepsTheStoredFileNamesAndClearsMissing()
    {
        // 旧行写的是 morning 的名字；下午再复制同一张图 ⇒ 名字必须是旧的那个，否则旧文件变孤儿。
        var morning = new DateTimeOffset(2026, 9, 27, 9, 5, 0, TimeSpan.FromHours(8));
        var existing = ClipboardEntry.BuildImage("c-abc", new ClipboardEntry.ImageMeta(
            "2026-09-27_0905_abc.png", "2026-09-27_0905_abc_thumb.jpg", 800, 600, 90_000), "Code", morning);
        ClipboardEntry.WithMissing(existing, true);

        var afternoon = new DateTimeOffset(2026, 9, 27, 14, 32, 0, TimeSpan.FromHours(8));
        var draft = ClipboardEntry.BuildImage("c-abc", new ClipboardEntry.ImageMeta(
            "2026-09-27_1432_abc.png", "2026-09-27_1432_abc_thumb.jpg", 800, 600, 91_000), "Code", afternoon);

        var merged = ClipboardEntry.MergeForReplay(existing.ExtraJson, draft, 2);
        var row = new Item { ExtraJson = merged };

        Assert.Equal("2026-09-27_0905_abc.png", ClipboardEntry.FileName(row));
        Assert.Equal("2026-09-27_0905_abc_thumb.jpg", ClipboardEntry.ThumbFileName(row));
        Assert.Equal(91_000, ClipboardEntry.ImageBytes(row));            // 字节数按最新一次写
        Assert.False(ClipboardEntry.IsMissing(row));                     // 正要往这个名字重写 ⇒ 缺失标记清掉
        Assert.Equal(2, ClipboardEntry.CopyCount(row));
    }

    [Fact]
    public void MissingFlagRoundTrips_AndSurvivesGarbageJson()
    {
        var item = ClipboardEntry.BuildImage("c-abc", new ClipboardEntry.ImageMeta("a.png", "a_thumb.jpg", 40, 40, 10), null, When());
        ClipboardEntry.WithMissing(item, true);
        Assert.True(ClipboardEntry.IsMissing(item));
        ClipboardEntry.WithMissing(item, false);
        Assert.False(ClipboardEntry.IsMissing(item));

        var broken = new Item { ExtraJson = "{ 这不是 JSON" };
        Assert.False(ClipboardEntry.IsMissing(broken));
        Assert.Equal((0, 0), ClipboardEntry.ImageSize(broken));
        Assert.Null(ClipboardEntry.FileName(broken));
    }

    // ────────── 端到端：门禁 → 落库 → 才写文件 ──────────

    [Fact]
    public async Task ImageCopy_WritesOnePngOneThumb_AndNoTempLeftBehind()
    {
        var item = await Cap(new ClipboardDedupe(), DibFrame(200, 120));
        Assert.NotNull(item);
        Assert.True(ClipboardPayload.TryReadPngSize(File.ReadAllBytes(MainPath(item!)), out var w, out var h));
        Assert.Equal((200, 120), (w, h));                                  // 库里那份真是这张图
        Assert.True(File.Exists(ThumbPath(item!)));
        Assert.Equal(2, Files().Length);                              // 主图 + 缩略图，没有第三件：原子改名不留残件（§3-Q6 第三类由构造消灭）
        Assert.Equal(1, await Rows());
    }

    [Fact]
    public async Task SameImageTwice_ReusesTheRowAndLeavesNoOrphanFile()
    {
        var first = await Cap(new ClipboardDedupe(), DibFrame(180, 90));
        Assert.NotNull(first);
        var files = Files().Length;

        // 隔出突发窗口再来一次：同图 ⇒ 命中同一行，名字不许换（换了旧文件就是孤儿）。
        var item = await Cap(new ClipboardDedupe(), DibFrame(180, 90));
        Assert.NotNull(item);
        Assert.Equal(1, await Rows());
        Assert.Equal(2, ClipboardEntry.CopyCount(item!));
        Assert.Equal(first!.Id, item!.Id);
        Assert.Equal(files, Files().Length);
    }

    [Fact]
    public async Task OwnEcho_IsNotRecorded()
    {
        var frame = DibFrame(120, 60);
        var d = new ClipboardDedupe();
        d.NoteOwnWrite(frame.Dib!.Value.Bgra, When().ToUnixTimeMilliseconds());     // 用户刚点了"复制这张图"

        Assert.Null(await Cap(d, frame));
        Assert.Equal(0, await Rows());
        Assert.Empty(Files());
    }

    [Fact]
    public async Task PasswordManagerSource_LeavesNeitherRowNorFile()
    {
        Assert.Null(await Cap(new ClipboardDedupe(), DibFrame(300, 200), app: "KeePass"));
        Assert.Equal(0, await Rows());
        Assert.Empty(Files());
    }

    [Fact]
    public async Task PngFrame_IsStoredByteForByte()
    {
        var first = await Cap(new ClipboardDedupe(), DibFrame(160, 80));
        Assert.NotNull(first);
        var png = File.ReadAllBytes(MainPath(first!));

        // 应用直接给 CF_PNG：库里那份必须就是它，不重新编码（§2）。重编码一次就多一次质量与尺寸的机会。
        var again = await Cap(new ClipboardDedupe(), new ClipboardNative.ImageRead(
            null, png, 160, 80, ClipboardPayload.FormatPng));
        Assert.NotNull(again);
        var saved = File.ReadAllBytes(MainPath(again!));
        Assert.Equal(png.LongLength, saved.LongLength);
        Assert.True(png.AsSpan().SequenceEqual(saved));
    }

    /// <summary>目录里现有的文件。<b>目录不存在＝空</b>，不是异常："什么都没写出来"常常正因为目录还没建。</summary>
    private string[] Files() => Directory.Exists(_dir) ? Directory.GetFiles(_dir) : Array.Empty<string>();

    private static string MainPath(Item item) => Path.Combine(ClipboardImageStore.Folder, ClipboardEntry.FileName(item)!);

    // ────────── 闸门：两条只有源码能证的纪律 ──────────

    [Fact]
    public void FilesAreWrittenOnlyAfterTheRowKnowsItsName()
    {
        // 先落库再写盘：仓储会告诉我们这一条其实早就在历史里、该沿用哪个文件名。
        // 反过来（先写盘）每次回放都落一个新名字，旧文件当场变孤儿——而"用户目录里不许攒没人认领的东西"
        // 正是 §3-Q6 把存储做成可见之后必须一起兑现的那一半。
        var cap = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.Integrations/Clipboard/ClipboardWatcher.cs"),
            "internal static async Task<Item?> CaptureImageAsync");
        Assert.True(cap.IndexOf("RecordClipboardAsync", StringComparison.Ordinal)
                    < cap.IndexOf("WritePairAsync", StringComparison.Ordinal),
            "顺序倒了：写盘在落库之前，回放就会留下孤儿文件");
    }

    [Fact]
    public void PngEncodingStillHasExactlyOneHome()
    {
        // 全仓唯一一处 PNG 编码器在截图抓屏那边（它自己的注释立着这条纪律）。剪贴板只调它。
        var store = SourceGate.ReadRepoFile("src/StarMark.Integrations/Clipboard/ClipboardImageStore.cs");
        Assert.Contains("GdiScreenCapture.EncodePngAsync(bgra, width, height, ct)", store);
        Assert.DoesNotContain("PngEncoderId", store);
        Assert.DoesNotContain("System.Drawing", store);          // 也不许借 GDI+ 另开一条编码路
    }


    private static string ThumbPath(Item item) => Path.Combine(ClipboardImageStore.Folder, ClipboardEntry.ThumbFileName(item)!);
}
