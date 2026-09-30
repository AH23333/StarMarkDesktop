#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 SC 的形状闸门：全仓"切片＋省略号"只许住在 <see cref="StarMark.Abstractions.TextTrim"/>，
/// 剩下两处是<b>点名的有意分岔</b>（P-130）。
/// <para>③ 是正向凭据：改道过的八家宿主每家都必须写着 <c>TextTrim.</c> 且不再匹配该形状——
/// 少了这一条，①的"只剩两处"就可能只是"扫描器没扫到"（#161）。
/// ④ 明写这条正则<b>看不见</b>哪种写法：累加式（<c>OcrText.Preview</c>）由行为测兜（#193）。</para>
/// </summary>
public sealed class EllipsisFamilyGateTests
{
    private const string JudgeFile = "src/StarMark.Abstractions/TextTrim.cs";

    /// <summary>两处有意保留的"自己切"：各自的理由写在被点名的文件里。</summary>
    private static readonly string[] KnownExceptions =
    {
        "src/StarMark.Abstractions/Ai/AiClassify.cs",        // 输出喂模型，不是给人看的读数
        "src/StarMark.Core/Diagnostics/DiagnosticsService.cs", // 中间省略（头…尾），与尾部截断不是一种语义；输入是 ASCII ETag
    };

    /// <summary>本批改道过的宿主（少一处或退回自己切，③ 会红）。</summary>
    private static readonly string[] ConvertedHosts =
    {
        "src/StarMark.Abstractions/ItemCardPolicy.cs",
        "src/StarMark.Abstractions/SourceAvailabilityText.cs",
        "src/StarMark.Abstractions/Ai/AiFailures.cs",
        "src/StarMark.Abstractions/Clipboard/ClipboardPolicy.cs",
        "src/StarMark.Core/Feed/RssParser.cs",
        "src/StarMark.Core/Widgets/AlarmPolicy.cs",
        "src/StarMark.Integrations/Ditto/DittoSource.cs",
        "src/StarMark.UI/ViewModels/TodoWidgetViewModel.cs",
    };

    [Fact]
    public void HandRolledEllipsisSurvivesOnlyInTheNamedExceptions()
    {
        var hits = new List<string>();
        foreach (var (path, code, _, _) in FormatScanner.SourcesUnder("src", JudgeFile))
        {
            if (KnownExceptions.Any(e => path.EndsWith(e, StringComparison.Ordinal))) continue;
            hits.AddRange(SplitLines(code).Where(l => FormatScanner.HandRolledEllipsis.IsMatch(l)).Select(l => $"{path}: {l.Trim()}"));
        }

        Assert.True(hits.Count == 0, "又有地方自己\"切片＋省略号\"了（该调 TextTrim.Ellipsize / Cut）：\n" + string.Join("\n", hits));

        // 那两处必须还在、且只有这两处：有人顺手把它们也改了，就该回来更新本条（而不是让闸门悄悄变宽）
        var exceptions = FormatScanner.SourcesUnder("src", JudgeFile)
            .Where(f => KnownExceptions.Any(e => f.Path.EndsWith(e, StringComparison.Ordinal)))
            .SelectMany(f => SplitLines(f.Code).Where(l => FormatScanner.HandRolledEllipsis.IsMatch(l)).Select(l => f.Path))
            .Distinct()
            .ToList();
        Assert.Equal(KnownExceptions.Length, exceptions.Count);
    }

    [Fact]
    public void TheJudgeHoldsTheCutAndIsNoEmptyShell()
    {
        var code = FormatScanner.Scan(ReadRepoFile(JudgeFile)).Code;

        Assert.Equal(2, Count(code, "public static string "));      // Cut ＋ Ellipsize
        Assert.Equal(1, Count(code, "char.IsHighSurrogate"));       // 那"退一格"就是这颗判据的全部理由
        Assert.Equal(1, Count(code, "\"…\""));
        Assert.Contains("text[..cut]", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryConvertedHostStillRoutesThroughTheJudge()
    {
        var missing = new List<string>();
        foreach (var (path, code, _, _) in FormatScanner.SourcesUnder("src"))
        {
            var file = ConvertedHosts.FirstOrDefault(h => path.EndsWith(h, StringComparison.Ordinal));
            if (file is null) continue;
            if (!code.Contains("TextTrim.", StringComparison.Ordinal)) missing.Add($"{file}：没找到 TextTrim. 的调用");
            if (SplitLines(code).Any(l => FormatScanner.HandRolledEllipsis.IsMatch(l))) missing.Add($"{file}：还在自己切片补省略号");
        }

        Assert.Equal(8, ConvertedHosts.Length);                     // 名单不许悄悄变短（改了名/删了文件都会红在这里）
        Assert.True(missing.Count == 0, "改道名单里有宿主不走了：\n" + string.Join("\n", missing));
    }

    /// <summary>自证：正则认得出三种写法，且<b>明说</b>它认不出累加式（那种由 <c>TextTrimTests</c> 的行为测兜）。</summary>
    [Theory]
    [InlineData("line = line[..maxLen] + \"…\";", true)]
    [InlineData("s.Length <= 12 ? s : string.Concat(s.AsSpan(0, 12), \"…\"),", true)]
    [InlineData("return text.Substring(0, 160) + \"…\";", true)]
    [InlineData("if (sb.Length >= maxChars) break;   // 累加式：形状看不见", false)]
    [InlineData("var line = idx >= 0 ? text[..idx] : text;", false)]
    public void SharedRegexCoversTheSlicesAndAdmitsTheAccumulateBlindSpot(string line, bool expected)
        => Assert.Equal(expected, FormatScanner.HandRolledEllipsis.IsMatch(line));

    private static IEnumerable<string> SplitLines(string code) => code.Split('\n').Select(l => l.TrimEnd('\r'));
}
