#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using StarMark.Abstractions.Text;
using StarMark.Integrations.Clipboard;
using StarMark.Integrations.Feed;

namespace StarMark.Tests;

/// <summary>
/// "外部字节该按哪个码页读"那颗判据的行为测（P-132，批次 SR）。
/// <para>
/// 断言尽量<b>不碰跑测试那台机器的配置</b>：假解码器把 (start,length,codePage) 原样记下来，
/// 需要真翻字节时也只交<b>明写</b>的码页（936 / 1200），而不是"本机 ANSI"——否则同一份代码在
/// 英文机（ACP=1252）上会红，而那是正确行为。
/// </para>
/// </summary>
public sealed class ExternalTextDecodeTests
{
    private const uint Acp936 = 936;
    private const uint FakeSystemAcp = 936;

    /// <summary>GBK("剪贴板")。测试里硬编码字节：<c>.NET Core</c> 取 936 需要额外的编码提供程序。</summary>
    private static readonly byte[] GbkClipboard = { 0xBC, 0xF4, 0xCC, 0xF9, 0xB0, 0xE5 };

    private sealed class Recorder
    {
        public List<(int Start, int Length, uint CodePage)> Calls { get; } = new();

        public string Decode(byte[] data, int start, int length, uint codePage)
        {
            Calls.Add((start, length, codePage));
            return $"{codePage}:{start}:{length}";
        }
    }

    private static ExternalTextDecoded Run(byte[]? data, string? label, Recorder rec, uint systemAcp = FakeSystemAcp)
        => ExternalText.Decode(data, data?.Length ?? 0, label, rec.Decode, systemAcp);

    [Fact]
    public void NoBytesDecodesToEmptyAndNeverTouchesTheDecoder()
    {
        var rec = new Recorder();

        Assert.Equal(string.Empty, Run(Array.Empty<byte>(), null, rec).Text);
        Assert.Equal(ExternalTextBasis.Empty, Run(null, null, rec).Basis);
        Assert.Empty(rec.Calls);            // "没有载荷"不该被送去任何一种码页
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 }, 65001u, 3, 1)]     // UTF-8 BOM
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41, 0x00 }, 1200u, 2, 2)]      // UTF-16 小端
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, 0x41 }, 1201u, 2, 2)]      // UTF-16 大端
    public void ByteOrderMarkWinsAndIsNotHandedToTheDecoder(byte[] data, uint wantCodePage, int wantStart, int wantLength)
    {
        var rec = new Recorder();

        var decoded = Run(data, "gbk", rec);          // 连声明都压得住：BOM 是载荷自己写在头上的

        Assert.Equal(wantCodePage, decoded.CodePage);
        Assert.Equal(ExternalTextBasis.ByteOrderMark, decoded.Basis);
        Assert.Single(rec.Calls);
        Assert.Equal(wantStart, rec.Calls[0].Start);  // BOM 本身不交下去，否则文本开头多一个隐形字符
        Assert.Equal(wantLength, rec.Calls[0].Length);
        Assert.Equal(wantCodePage, rec.Calls[0].CodePage);
    }

    [Fact]
    public void DeclarationBeatsTheProbeEvenWhenTheBytesLookLikeUtf8()
    {
        var rec = new Recorder();
        var utf8 = Encoding.UTF8.GetBytes("标题");      // 既是合法 UTF-8，也可能被别的码页解成另一回事

        var decoded = Run(utf8, "GBK", rec);

        Assert.Equal(ExternalTextBasis.DeclaredLabel, decoded.Basis);
        Assert.Single(rec.Calls);
        Assert.Equal(Acp936, rec.Calls[0].CodePage);
        // 不自作聪明："源说了 GBK，但这串字节按 UTF-8 也解得通，就按 UTF-8 读"一旦开一次头，
        // 下一次就没人再去看声明了。
    }

    [Fact]
    public void UnknownDeclarationIsTreatedAsNoDeclarationNotAsAGuess()
    {
        var rec = new Recorder();
        var utf8 = Encoding.UTF8.GetBytes("标题");

        Assert.Equal(ExternalTextBasis.Utf8Probe, Run(utf8, "x-user-defined", rec).Basis);
        Assert.Equal(65001u, rec.Calls[0].CodePage);
    }

    [Fact]
    public void AsciiOnlyProbesAsUtf8()
    {
        var rec = new Recorder();
        Assert.Equal(ExternalTextBasis.Utf8Probe, Run(Encoding.ASCII.GetBytes("<rss>plain</rss>"), null, rec).Basis);
    }

    [Fact]
    public void RealUtf8ChineseProbesAsUtf8()
    {
        var rec = new Recorder();
        Run(Encoding.UTF8.GetBytes("订阅正文的中文"), null, rec);

        Assert.Equal(65001u, rec.Calls[0].CodePage);
    }

    [Fact]
    public void ForeignBytesWithoutDeclarationFollowTheSystemAnsiCodePage()
    {
        var rec = new Recorder();

        var decoded = Run(GbkClipboard, null, rec, systemAcp: 1252);   // 交一个"这台机器是英文码页"的假 ACP

        Assert.Equal(ExternalTextBasis.SystemAnsi, decoded.Basis);
        Assert.Equal(1252u, rec.Calls[0].CodePage);   // 这一格钉的是"兜底听系统的"，不是"兜底是 936"
    }

    [Fact]
    public void DamageAfterRealCharactersStaysUtf8InsteadOfSwitchingCodePage()
    {
        var rec = new Recorder();
        var damaged = new List<byte>(Encoding.UTF8.GetBytes("中文正文")) { 0xC3, 0x28 };   // 解出过真字之后才坏

        Assert.Equal(ExternalTextBasis.DamagedUtf8, Run(damaged.ToArray(), null, rec).Basis);
        Assert.Equal(65001u, rec.Calls[0].CodePage);   // 换 ANSI 会把本来只错一两处的整篇变成另一种语言的字
    }

    [Fact]
    public void TruncatedTailIsDamageNotAForeignCodePage()
    {
        var rec = new Recorder();
        var truncated = new List<byte>(Encoding.UTF8.GetBytes("正文")) { 0xE4, 0xB8 };     // 尾部半个字（源被掐断）

        Assert.Equal(ExternalTextBasis.DamagedUtf8, Run(truncated.ToArray(), null, rec).Basis);
    }

    [Theory]
    [InlineData(new byte[] { 0xC0, 0x80 }, ExternalTextBasis.SystemAnsi)]                  // 过长编码
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 }, ExternalTextBasis.SystemAnsi)]            // 代理区不许用 UTF-8 编码
    [InlineData(new byte[] { 0xF4, 0x90, 0x80, 0x80 }, ExternalTextBasis.SystemAnsi)]      // 超出 U+10FFFF
    [InlineData(new byte[] { 0x80, 0x41 }, ExternalTextBasis.SystemAnsi)]                  // 落单的变化字节
    [InlineData(new byte[] { 0x41, 0xFF }, ExternalTextBasis.SystemAnsi)]                  // 不存在的首字节
    public void InvalidUtf8ShapesAreRejectedByTheProbe(byte[] data, ExternalTextBasis want)
    {
        var rec = new Recorder();
        Assert.Equal(want, Run(data, null, rec).Basis);
    }

    [Fact]
    public void OnlyTheDeclaredLengthIsJudgedWhenTheBufferIsBiggerThanThePayload()
    {
        var rec = new Recorder();
        var used = Encoding.UTF8.GetByteCount("正文");
        // 预览只读文件前一段：数组比载荷长，尾部那几个 0 不属于这份文本（NUL 是合法 ASCII，混进去就悄悄多解出空字符）。
        var buffer = new byte[used + 4];
        Encoding.UTF8.GetBytes("正文").CopyTo(buffer, 0);

        var decoded = ExternalText.Decode(buffer, used, null, rec.Decode, FakeSystemAcp);

        Assert.Equal(ExternalTextBasis.Utf8Probe, decoded.Basis);
        Assert.Single(rec.Calls);
        Assert.Equal(0, rec.Calls[0].Start);
        Assert.Equal(used, rec.Calls[0].Length);
    }

    [Theory]
    [InlineData("gbk", 936)]
    [InlineData("GBK", 936)]
    [InlineData("GB2312", 936)]
    [InlineData("gb_2312-80", 936)]
    [InlineData("x-gbk", 936)]
    [InlineData("GB18030", 54936)]
    [InlineData("Big5", 950)]
    [InlineData("shift-jis", 932)]
    [InlineData("EUC-KR", 949)]
    [InlineData("ks_c_5601-1987", 949)]
    [InlineData("ISO-8859-1", 28591)]
    [InlineData("windows-1252", 1252)]
    [InlineData("utf-8", 65001)]
    [InlineData("UTF8", 65001)]
    [InlineData("utf-16", 1200)]
    public void KnownEncodingNamesMapToWin32CodePages(string label, uint want)
        => Assert.Equal(want, ExternalText.CodePageForLabel(label));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x-user-defined")]
    [InlineData("utf-32")]                       // 我们不接 UTF-32：认不得比解错好
    [InlineData("microsoft-turkish")]
    public void UnknownOrBlankEncodingNamesMapToNothing(string? label)
        => Assert.Null(ExternalText.CodePageForLabel(label));

    [Fact]
    public void A64PlusCharLabelIsRefusedRatherThanHashed()
        => Assert.Null(ExternalText.CodePageForLabel(new string('a', 80)));   // 名字来自对方的头，先在边界上夹住

    [Theory]
    [InlineData("<?xml version=\"1.0\" encoding=\"GBK\"?><rss/>", "GBK")]
    [InlineData("<?xml version=\"1.0\" encoding='gb2312'?><rss/>", "gb2312")]
    [InlineData("<?xml version=\"1.0\"?>\n<rss encoding=\"iso-8859-1\"/>", null)]   // 写在 ?&gt; 之后＝不是声明
    [InlineData("<rss><title>x</title></rss>", null)]                                // 压根没有前奏
    [InlineData("<?xml version=\"1.0\"?><rss/>", null)]
    public void PrologLabelReadsOnlyTheXmlDeclaration(string xml, string? want)
    {
        var bytes = Encoding.ASCII.GetBytes(xml);
        Assert.Equal(want, ExternalText.PrologLabel(bytes, bytes.Length));
    }

    [Fact]
    public void PrologLabelSurvivesAByteOrderMarkedByNotBelievingIt()
    {
        // BOM 在前就认不得前奏了——但那条路径由 BOM 自己那一步接住，这里钉的是"不去乱猜"。
        var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
        bytes.AddRange(Encoding.ASCII.GetBytes("<?xml version=\"1.0\" encoding=\"GBK\"?>"));
        Assert.Null(ExternalText.PrologLabel(bytes.ToArray(), bytes.Count));
    }

    [Fact]
    public void Utf16BigEndianFileWithBomReadsAsItself()
    {
        var bytes = new List<byte> { 0xFE, 0xFF, 0x68, 0x07, 0x98, 0x98 };   // "标题"，大端

        var decoded = ExternalText.Decode(bytes.ToArray(), bytes.Count, null, AnsiText.Decode, Acp936);

        Assert.Equal("标题", decoded.Text);
        Assert.Equal(ExternalTextBasis.ByteOrderMark, decoded.Basis);
    }

    [Fact]
    public void HalfACharacterAtTheEndOfAnUtf16FileIsDroppedNotGuessed()
    {
        // 截断/坏档的 UTF-16：凑不成一对的那一个字节不许猜成半个字符。
        var bytes = new List<byte> { 0xFF, 0xFE, 0x07, 0x68, 0x98 };

        Assert.Equal("标", ExternalText.Decode(bytes.ToArray(), bytes.Count, null, AnsiText.Decode, Acp936).Text);
    }

    [Fact]
    public void ValidUtf8ChineseSurvivesTheRealDecoder()
    {
        // 这一格钉的是"表里那个 UTF-8 码页号是真的能用的码页号"：假解码器只会把数字原样带回来，
        // 抄错常量（65000 是 UTF-7）时它量不出来，只有交给系统翻字节才红。
        var utf8 = Encoding.UTF8.GetBytes("订阅正文的中文");

        var decoded = ExternalText.Decode(utf8, utf8.Length, null, AnsiText.Decode, Acp936);

        Assert.Equal("订阅正文的中文", decoded.Text);
        Assert.Equal(ExternalTextBasis.Utf8Probe, decoded.Basis);
    }

    [Fact]
    public void DeclaredGbkBytesReachChineseThroughTheRealDecoder()
    {
        var decoded = ExternalText.Decode(GbkClipboard, GbkClipboard.Length, "GBK", AnsiText.Decode, 1252);

        Assert.Equal("剪贴板", decoded.Text);      // 系统翻的，不是我们自己表里抄的
        Assert.Equal(ExternalTextBasis.DeclaredLabel, decoded.Basis);
    }

    [Fact]
    public void UndeclaredGbkBytesReachChineseThroughTheGivenSystemCodePage()
    {
        var decoded = ExternalText.Decode(GbkClipboard, GbkClipboard.Length, null, AnsiText.Decode, Acp936);

        Assert.Equal("剪贴板", decoded.Text);
        Assert.Equal(ExternalTextBasis.SystemAnsi, decoded.Basis);
    }

    [Fact]
    public void Utf16LittleEndianFileWithBomNowReadsAsItself()
    {
        var bytes = new List<byte> { 0xFF, 0xFE };
        bytes.AddRange(Encoding.Unicode.GetBytes("标题"));

        var decoded = ExternalText.Decode(bytes.ToArray(), bytes.Count, null, AnsiText.Decode, Acp936);

        Assert.Equal("标题", decoded.Text);        // 改前：按 UTF-8 读 UTF-16，这里是一串怪字
        Assert.Equal(ExternalTextBasis.ByteOrderMark, decoded.Basis);
    }

    [Fact]
    public void PureAsciiIsIdenticalOnEitherCodePage()
    {
        // 账本承诺的"影响面"那一格：纯 ASCII 载荷一字不变（正向对照）。
        var ascii = Encoding.ASCII.GetBytes("<title>StarMark feeds</title>");

        Assert.Equal(Encoding.ASCII.GetString(ascii), ExternalText.Decode(ascii, ascii.Length, null, AnsiText.Decode, Acp936).Text);
        Assert.Equal(ExternalTextBasis.Utf8Probe, ExternalText.Decode(ascii, ascii.Length, null, AnsiText.Decode, Acp936).Basis);
    }
}

/// <summary>
/// RSS 那一处的<b>行为</b>测（同批）：本批把 <c>Encoding.UTF8.GetString</c> 换掉之后，
/// "声明了 GBK 的源"与"什么都没声明的 GBK 源"要能当场量出中文，而 UTF-8 源不许被换坏。
/// </summary>
public sealed class RssBodyDecodeTests
{
    /// <summary>前奏声明 GBK 的源：字节是 GBK，UTF-8 读法会把整源变成怪字。</summary>
    [Fact]
    public void FeedThatDeclaresGbkIsReadAsGbk()
    {
        var body = new List<byte>();
        body.AddRange(Encoding.ASCII.GetBytes("<?xml version=\"1.0\" encoding=\"GBK\"?><rss><title>"));
        body.AddRange(new byte[] { 0xBC, 0xF4, 0xCC, 0xF9, 0xB0, 0xE5 });        // GBK("剪贴板")
        body.AddRange(Encoding.ASCII.GetBytes("</title></rss>"));

        var text = RssClient.Decode(body.ToArray(), null);

        Assert.Contains("剪贴板", text, StringComparison.Ordinal);
        Assert.DoesNotContain("�", text, StringComparison.Ordinal);   // 旧读法在这一步必红：整源都是替换符
    }

    [Fact]
    public void FeedThatOnlySaysUtf8IsUnchanged()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><rss><title>订阅中文</title></rss>";
        var bytes = Encoding.UTF8.GetBytes(xml);

        Assert.Equal(xml, RssClient.Decode(bytes, null));      // 正向对照：绝大多数源今天就是这一格
    }

    /// <summary>前奏没写、HTTP 头写了 charset：外层那句也要接住，否则等于没读声明。</summary>
    [Fact]
    public void HttpCharsetCountsAsADeclaration()
    {
        var body = new List<byte>();
        body.AddRange(Encoding.ASCII.GetBytes("<?xml version=\"1.0\"?><rss><title>"));
        body.AddRange(new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 });                    // GBK("中文")
        body.AddRange(Encoding.ASCII.GetBytes("</title></rss>"));

        Assert.Contains("中文", RssClient.Decode(body.ToArray(), "gbk"), StringComparison.Ordinal);
    }

    [Fact]
    public void ByteOrderMarkFeedKeepsItsTitleAndLosesTheBomChar()
    {
        var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
        bytes.AddRange(Encoding.UTF8.GetBytes("<rss><title>标题</title></rss>"));

        Assert.StartsWith("<rss>", RssClient.Decode(bytes.ToArray(), null), StringComparison.Ordinal);
    }
}
