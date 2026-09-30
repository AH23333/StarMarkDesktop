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
}
