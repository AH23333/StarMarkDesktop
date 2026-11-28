#nullable enable
using System;
using System.Text;
using Xunit;
using StarMark.Core.Widgets;

namespace StarMark.Tests;

/// <summary>
/// 虚拟文件物化的<b>纯判据</b>（片290 / P-307）：<see cref="TodoVirtualFilePolicy"/> 三件套——
/// 暂存预算、魔数→扩展名、MOTW 正文——全是纯函数，这里全走无夹具单测。
/// <para>
/// 预算的边界值（512MB／64MB／2GB）与十二组魔数签名是行为规格（v6 参数表・学思路不搬码，
/// ✅P-301 红线口径）：预算错一格就是「写穿宿主盘」或「该拒收的没拒」两种事故；
/// 正文少剥一个换行就是 ADS 内容注入。
/// </para>
/// </summary>
public sealed class TodoVirtualFilePolicyTests
{
    private static readonly long Mb = 1024L * 1024;
    private static readonly long Gb = 1024L * 1024 * 1024;

    // ── ResolveStagingBudgetBytes：盘感知暂存预算 ──

    [Fact]
    public void Budget_UnknownDrive_FallsBackTo2Gb()
        => Assert.Equal(2 * Gb, TodoVirtualFilePolicy.ResolveStagingBudgetBytes(null));

    [Theory]
    [InlineData(0L)]                       // 盘满
    [InlineData(-1L)]                      // 防御：负值不当「充裕」算
    [InlineData(64L * 1024 * 1024)]        // 恰好见底：拒收，不贴着零写盘
    public void Budget_AtOrBelowCritical_Zero(long free)
        => Assert.Equal(0, TodoVirtualFilePolicy.ResolveStagingBudgetBytes(free));

    [Fact]
    public void Budget_JustAboveCritical_CriticalReserveKicksIn()
        => Assert.Equal(1, TodoVirtualFilePolicy.ResolveStagingBudgetBytes(64L * 1024 * 1024 + 1));

    [Fact]
    public void Budget_AtSafetyReserve_CriticalFloorApplies()
        => Assert.Equal(512 * Mb - 64 * Mb, TodoVirtualFilePolicy.ResolveStagingBudgetBytes(512 * Mb));

    [Fact]
    public void Budget_JustAboveSafetyReserve_OneByteLeft()
        => Assert.Equal(1, TodoVirtualFilePolicy.ResolveStagingBudgetBytes(512 * Mb + 1));

    [Fact]
    public void Budget_PlentifulDrive_SafetyReserveApplies()
        => Assert.Equal(100 * Gb - 512 * Mb, TodoVirtualFilePolicy.ResolveStagingBudgetBytes(100 * Gb));

    // ── TryInferExtension：魔数→扩展名（十二组签名） ──

    /// <summary>把若干 ASCII 段按偏移拼进一颗头（RIFF/ftyp 族的结构偏移签名用）。</summary>
    private static byte[] Boxed(params (int Offset, string Ascii)[] parts)
    {
        var bytes = new byte[64];
        foreach (var (offset, ascii) in parts)
            Encoding.ASCII.GetBytes(ascii, 0, ascii.Length, bytes, offset);
        return bytes;
    }

    [Fact]
    public void Infer_PngSignature_Png()
        => Assert.Equal(".png", TodoVirtualFilePolicy.TryInferExtension(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 }));

    [Fact]
    public void Infer_JpegSoi_Jpg()
        => Assert.Equal(".jpg", TodoVirtualFilePolicy.TryInferExtension(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));

    [Theory]
    [InlineData("GIF87a")]
    [InlineData("GIF89a")]
    public void Infer_GifBothVersions_Gif(string magic)
        => Assert.Equal(".gif", TodoVirtualFilePolicy.TryInferExtension(
            Boxed((0, magic), (6, "7a2b0d00"))));

    [Fact]
    public void Infer_Bmp_Bmp()
        => Assert.Equal(".bmp", TodoVirtualFilePolicy.TryInferExtension(new byte[] { 0x42, 0x4D, 0x00, 0x00 }));

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0x00 })]   // 图标组
    [InlineData(new byte[] { 0x00, 0x00, 0x02, 0x00 })]   // 光标组
    public void Infer_IcoIconAndCursorGroups_Ico(byte[] header)
        => Assert.Equal(".ico", TodoVirtualFilePolicy.TryInferExtension(header));

    [Theory]
    [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00 })]   // 小端
    [InlineData(new byte[] { 0x4D, 0x4D, 0x00, 0x2A })]   // 大端
    public void Infer_TiffBothByteOrders_Tif(byte[] header)
        => Assert.Equal(".tif", TodoVirtualFilePolicy.TryInferExtension(header));

    [Fact]
    public void Infer_RiffWebp_Webp()
        => Assert.Equal(".webp", TodoVirtualFilePolicy.TryInferExtension(
            Boxed((0, "RIFF"), (8, "WEBP"))));

    [Theory]
    [InlineData("avif")]
    [InlineData("avis")]
    public void Infer_FtypAvifBrands_Avif(string brand)
        => Assert.Equal(".avif", TodoVirtualFilePolicy.TryInferExtension(
            Boxed((4, "ftyp"), (8, brand))));

    [Theory]
    [InlineData("heic")]
    [InlineData("heix")]
    [InlineData("hevc")]
    [InlineData("hevx")]
    [InlineData("mif1")]
    [InlineData("msf1")]
    public void Infer_FtypHeicFamilyBrands_Heic(string brand)
        => Assert.Equal(".heic", TodoVirtualFilePolicy.TryInferExtension(
            Boxed((4, "ftyp"), (8, brand))));

    [Fact]
    public void Infer_PdfHeader_Pdf()
        => Assert.Equal(".pdf", TodoVirtualFilePolicy.TryInferExtension(Encoding.ASCII.GetBytes("%PDF-1.7")));

    [Fact]
    public void Infer_UnknownContent_Null()
        // 认不出的内容＝null：保留原名，不瞎猜（未知内容硬配一个扩展名＝打开必错）
        => Assert.Null(TodoVirtualFilePolicy.TryInferExtension(Encoding.ASCII.GetBytes("hello world, plainly text")));

    [Fact]
    public void Infer_EmptyHeader_Null()
        => Assert.Null(TodoVirtualFilePolicy.TryInferExtension(Array.Empty<byte>()));

    [Fact]
    public void Infer_TruncatedRiff_Null()
        // RIFF 只到 8 字节（WEBP 标记还没开始）：结构签名不够长就不认
        => Assert.Null(TodoVirtualFilePolicy.TryInferExtension(
            Boxed((0, "RIFF")).AsSpan(0, 8).ToArray()));

    [Fact]
    public void Infer_TruncatedFtyp_Null()
        // ftyp 盒只到 11 字节（品牌第 4 字节缺）：不够长就不认
        => Assert.Null(TodoVirtualFilePolicy.TryInferExtension(
            Boxed((4, "ftyp"), (8, "avi")).AsSpan(0, 11).ToArray()));

    // ── BuildZoneIdentifierContent：MOTW 正文 ──

    [Fact]
    public void Zone_NoHostUrl_GenericMarkerAndExactShape()
        => Assert.Equal("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=starmark://drag-drop/virtual\r\n",
            TodoVirtualFilePolicy.BuildZoneIdentifierContent(null));

    [Fact]
    public void Zone_WithHostUrl_KeepsItOnTheHostUrlLine()
    {
        var content = TodoVirtualFilePolicy.BuildZoneIdentifierContent("https://example.com/a.png");
        Assert.Contains("ZoneId=3", content);
        Assert.Contains("HostUrl=https://example.com/a.png", content);
    }

    [Fact]
    public void Zone_CrLfInjection_CannotForgeAFourthLine()
    {
        // 恶意 HostUrl 里的换行必须被剥掉：多出来的第四行（ReferrerUrl 之类）＝ADS 正文注入
        var content = TodoVirtualFilePolicy.BuildZoneIdentifierContent("https://x/\r\nReferrerUrl=https://evil");
        var lines = content.Split("\r\n");
        Assert.Equal(4, lines.Length);                       // 恰三行＋CRLF 收尾的空尾巴：注入活不进第四行
        Assert.Equal("[ZoneTransfer]", lines[0]);
        Assert.Equal("ZoneId=3", lines[1]);
        Assert.StartsWith("HostUrl=", lines[2]);
        Assert.Equal(string.Empty, lines[3]);
    }

    [Fact]
    public void Zone_WhitespaceHostUrl_FallsBackToGenericMarker()
        => Assert.Contains("starmark://drag-drop/virtual",
            TodoVirtualFilePolicy.BuildZoneIdentifierContent("  \r\n "));
}
