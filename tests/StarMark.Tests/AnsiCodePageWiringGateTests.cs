#nullable enable
using System;
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 码页解码的<b>落点与接线</b>闸门（P-35，批次 SP）。
/// <para>
/// 这批的风险不是"翻错一个字节"（行为测已经钉住），而是<b>结构被悄悄改回去</b>：
/// ① 有人为了省事把 Win32 调用搬进 <c>Abstractions</c>（那一层写明只放纯函数，共用它的实时采集与第三方库
/// 读取都指望它不碰平台）；② 有人给 <c>decodeAnsi</c> 加回默认值（下一个调用方忘记交解码器时就静默退回乱码）；
/// ③ 有人在某一处重新手写 <c>(char)byte</c>。三条各由下面一格守着。
/// </para>
/// </summary>
public sealed class AnsiCodePageWiringGateTests
{
    private const string Payload = "src/StarMark.Abstractions/Clipboard/ClipboardPayload.cs";
    private const string AbstractionsRoot = "src/StarMark.Abstractions";

    [Fact]
    public void AbstractionsLayerStillContainsNoWin32()
    {
        var offenders = SourceGate.ReadRepoUnder(AbstractionsRoot)
            .Where(f => SourceGate.Code(f.Text).Contains("DllImport", StringComparison.Ordinal))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void DropFilesParserNoLongerWidensBytesItself()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(Payload));

        Assert.DoesNotContain("(char)data[", code, StringComparison.Ordinal);   // 旧的逐字节宽化＝Latin-1 假定
        Assert.Contains("decodeAnsi(data, start", code, StringComparison.Ordinal); // 非宽分支整段交给交进来的解码器
    }

    [Fact]
    public void TheDecoderParameterHasNoDefaultSoCallersMustHandOneOver()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(Payload));

        Assert.Contains("Func<byte[], int, int, string> decodeAnsi)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("decodeAnsi =", code, StringComparison.Ordinal);   // 一旦有默认值，"忘记交"就变成静默退回乱码
    }

    [Fact]
    public void BothProducersOfDropListBytesHandOverTheSystemCodePageDecoder()
    {
        // 两个生产者：系统剪贴板的实时采集 + Ditto 库的 BLOB。**钉在方法体里**而不是"全仓数一次"
        // （#196：整文件 Contains 会被同文件另一处合法用法顶住；而全局计数会把"抽个局部别名"
        // 这种无害重构判成违规——真要那么改，改的是这道门并且写下理由，不是放宽它）。
        var native = SourceGate.Code(SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.Integrations/Clipboard/ClipboardNative.cs"),
            "private static System.Collections.Generic.List<string> ReadFiles()"));
        var ditto = SourceGate.Code(SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.Integrations/Ditto/DittoDatabaseReader.cs"),
            "private void FillData(DittoClip clip)"));

        Assert.Contains("AnsiText.DecodeSystemAnsi", native, StringComparison.Ordinal);
        Assert.Contains("AnsiText.DecodeSystemAnsi", ditto, StringComparison.Ordinal);
    }

    // ===== 文本那一半（P-18，批次 SQ）：同一族，所以闸门也在同一处 =====

    private const string DittoReader = "src/StarMark.Integrations/Ditto/DittoDatabaseReader.cs";
    private const string Native = "src/StarMark.Integrations/Clipboard/ClipboardNative.cs";

    [Fact]
    public void TheAnsiTextBranchNoLongerAssumesUtf8AndTakesNoDefaultDecoder()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(Payload));
        var body = SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(Payload),
            "public static string DecodeAnsiText(byte[]? data, Func<byte[], int, int, string> decodeAnsi)"));

        // 那个 `bool ansi` 把"单字节"与"UTF-8"混为一谈——旧缺陷就是这么写进签名的。
        Assert.DoesNotContain("DecodeText(byte[]? data, bool", code, StringComparison.Ordinal);
        // CF_TEXT 那一支不许自己选定编码：整段交给交进来的解码器。
        Assert.DoesNotContain("Encoding.UTF8", body, StringComparison.Ordinal);
        // #209：留默认值＝给未来的调用方留一条静默乱码的路。
        Assert.Contains("Func<byte[], int, int, string> decodeAnsi)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EachProducerPicksItsOwnEntryPoint()
    {
        var native = SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(Native),
            "private static string? ReadText()"));
        var ditto = SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(DittoReader),
            "private void FillData(DittoClip clip)"));

        // 实时采集只读 CF_UNICODETEXT ⇒ 它不该问码页要任何东西。
        Assert.Contains("DecodeUnicodeText(payload)", native, StringComparison.Ordinal);
        // Ditto 两种文本格式各走各的入口，且 CF_TEXT 交的是 **OEM** 码页解码器（Win32 对该格式的约定）。
        Assert.Contains("ClipboardPayload.DecodeUnicodeText(data)", ditto, StringComparison.Ordinal);
        Assert.Contains("ClipboardPayload.DecodeAnsiText(data, AnsiText.DecodeSystemOemText)", ditto, StringComparison.Ordinal);
    }

    [Fact]
    public void TextPrecedenceIsDecidedOutsideTheRowLoop()
    {
        var ditto = SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(DittoReader),
            "private void FillData(DittoClip clip)"));

        // 那句 SELECT 没有 ORDER BY ⇒ "谁后到谁赢"就是行序在替我们做决定（P-18 的第二半）。
        Assert.DoesNotContain("clip.Format = fmt.ToUpperInvariant();", ditto, StringComparison.Ordinal);
        Assert.Contains("if (wideText is not null)", ditto, StringComparison.Ordinal);
        Assert.Contains("else if (ansiText is not null)", ditto, StringComparison.Ordinal);
    }

    [Fact]
    public void OemAndAnsiCodePagesAreTwoNamedEntryPointsNotOneGuess()
    {
        var ansi = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Integrations/Clipboard/AnsiText.cs"));

        // 两个格式两种约定：CF_HDROP＝ANSI、CF_TEXT＝OEM。合成一个"反正中文机器上都是 936"就没人会去查别的机器。
        Assert.Contains("private static extern uint GetACP();", ansi, StringComparison.Ordinal);
        Assert.Contains("private static extern uint GetOEMCP();", ansi, StringComparison.Ordinal);
        Assert.Contains("public static uint SystemOemCodePage => GetOEMCP();", ansi, StringComparison.Ordinal);
        Assert.Contains("Decode(data, start, length, SystemOemCodePage)", ansi, StringComparison.Ordinal);
    }
}
