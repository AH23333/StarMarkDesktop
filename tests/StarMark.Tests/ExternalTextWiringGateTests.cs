#nullable enable
using System;
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// P-132 那颗判据的<b>接线与形状</b>闸门（批次 SR）。
/// <para>
/// 行为测钉的是"这次按哪个码页读的"，这里钉的是<b>结构别被悄悄改回去</b>：① 参数被加回默认值
/// （下一个调用方忘记交解码器时就静默退回 UTF-8 假定，同 #209 在剪贴板那一支的证明）；
/// ② 某一处又自己手写 <c>Encoding.UTF8.GetString</c>；③ "认不得的编码名"被兜底成某个具体码页
/// （那是把"我们其实没读懂"从日志里抹掉）；④ 兜底那一臂被写死成 936（换台机器就错，而本机永远量不出）。
/// </para>
/// </summary>
public sealed class ExternalTextWiringGateTests
{
    private const string Predicate = "src/StarMark.Abstractions/Text/ExternalText.cs";
    private const string Rss = "src/StarMark.Integrations/Feed/RssClient.cs";
    private const string Preview = "src/StarMark.UI/Controls/PreviewHost.xaml.cs";
    private const string Ansi = "src/StarMark.Integrations/Clipboard/AnsiText.cs";

    [Fact]
    public void ThePredicateTakesNoDefaultDecoderAndNoDefaultCodePage()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(Predicate));

        Assert.Contains("Func<byte[], int, int, uint, string> decodeByCodePage,", code, StringComparison.Ordinal);
        Assert.Contains("uint systemAnsiCodePage)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("decodeByCodePage =", code, StringComparison.Ordinal);
        Assert.DoesNotContain("systemAnsiCodePage =", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DllImport", code, StringComparison.Ordinal);   // 决定"该按哪个码页"是纯函数，翻字节交出去
    }

    [Fact]
    public void ByteOrderMarkIsNotHandedDownToTheDecoder()
    {
        var body = SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(Predicate),
            "public static ExternalTextDecoded Decode"));

        // 交 0 就把 BOM 留在文本开头：预览与标题那一格会显出一个看不见的字符。
        Assert.Contains("decodeByCodePage(data, bomLength, length - bomLength, bomCodePage)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownEncodingNamesFallToTheProbeNotToATableDefault()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(Predicate));

        Assert.Contains("CodePagesByLabel.TryGetValue(Canonicalize(label), out var codePage) ? codePage : null", code, StringComparison.Ordinal);
        // 认不得就交 null，由探测继续判；"未知编码就当 GBK"在别的机器上是错的。
        Assert.DoesNotContain("? codePage : 936", code, StringComparison.Ordinal);
        Assert.DoesNotContain("?? 936", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAnsiArmUsesTheCodePageItWasGivenNotALiteral()
    {
        var body = SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(Predicate),
            "public static ExternalTextDecoded Decode"));

        Assert.Contains("decodeByCodePage(data, 0, length, systemAnsiCodePage)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("936", body, StringComparison.Ordinal);        // 本机 ACP 恰好是 936，写死了也没人会发现
    }

    [Fact]
    public void DamageAfterRealCharactersDoesNotSwitchCodePage()
    {
        var body = SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(Predicate),
            "public static ExternalTextDecoded Decode"));

        // 判据的方向：先"这份 UTF-8 有伤"（留 U+FFFD），才"它不是 UTF-8"（换 ANSI）。反过来的话整篇换字。
        Assert.Contains("if (decodedRealCharacters)", body, StringComparison.Ordinal);
        Assert.True(body.IndexOf("ExternalTextBasis.DamagedUtf8", StringComparison.Ordinal)
            < body.IndexOf("ExternalTextBasis.SystemAnsi", StringComparison.Ordinal));
    }

    [Fact]
    public void RssBodyNoLongerAssumesUtf8AndReadsBothDeclarationSources()
    {
        var body = SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(Rss),
            "internal static string Decode(byte[] bytes, string? httpCharset)"));

        Assert.DoesNotContain("Encoding.UTF8.GetString", body, StringComparison.Ordinal);
        Assert.Contains("ExternalText.PrologLabel(bytes, bytes.Length) ?? httpCharset", body, StringComparison.Ordinal);
        Assert.Contains("ExternalText.Decode(bytes, bytes.Length, declared, AnsiText.Decode, AnsiText.SystemAnsiCodePage)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void RssCallSiteHandsTheHttpCharsetDown()
    {
        var fetch = SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(Rss),
            "public async Task<RssFetchResult> FetchAsync"));

        // 声明有两个来源，其中一个（HTTP charset）只有这里能拿到；漏掉就等于没读声明。
        Assert.Contains("Decode(bytes, response.Content.Headers.ContentType?.CharSet)", fetch, StringComparison.Ordinal);
        Assert.DoesNotContain("Decode(bytes)", fetch, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewTextFileHandsOverTheSharedPredicate()
    {
        var body = SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(Preview),
            "private async System.Threading.Tasks.Task ShowTextFileAsync"));

        Assert.DoesNotContain("Encoding.UTF8.GetString", body, StringComparison.Ordinal);
        // 磁盘上的文件没有声明层，所以 declaredLabel 交 null——但码页仍然必须问系统要，不许在 UI 里写死。
        Assert.Contains("ExternalText.Decode(bytes, read, null, AnsiText.Decode, AnsiText.SystemAnsiCodePage)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Utf16CodePagesArePairedBeforeTheyReachMultiByteToWideChar()
    {
        var body = SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(Ansi),
            "public static string Decode(byte[] data, int start, int length, uint codePage)"));

        Assert.Contains("if (codePage == ExternalText.Utf16LeCodePage || codePage == ExternalText.Utf16BeCodePage)", body, StringComparison.Ordinal);
        // 顺序也是判据：<c>MultiByteToWideChar</c> 对 1200/1201 返回 0，先交给它就走宽化兜底，
        // 症状是"一个字符节变成两个字符"——而那正是这批撞上的第二格。
        Assert.True(body.IndexOf("FromUtf16(data, start, length", StringComparison.Ordinal)
            < body.IndexOf("MultiByteToWideChar(codePage", StringComparison.Ordinal));
    }

    [Fact]
    public void ExternalByteProducersStillDoNotGetTheirOwnCopyOfTheProbe()
    {
        // 收成一颗的意思＝全仓只有一处 UTF-8 自检。有人再写一份，红在这里，而不是分岔两年后各自漂移。
        var offenders = SourceGate.ReadRepoUnder("src")
            .Where(f => SourceGate.Code(f.Text).Contains("sawRealCharacter", StringComparison.Ordinal)
                     || SourceGate.Code(f.Text).Contains("DecodedRealCharacters", StringComparison.Ordinal))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .ToList();

        Assert.Equal(new[] { Predicate }, offenders);
    }
}
