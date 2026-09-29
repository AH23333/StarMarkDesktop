#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 SB 的形状闸门：预览窗标题的 40 字截断只许住在一颗判据里（P-123 清单第二条）。
/// <para>规则①是禁项，但它<b>今天零命中</b>——零命中和"没扫到"在绿灯上长得一样（#161），
/// 所以反空转由两处一起顶：② 钉"确实有两个读者、而且实参槽位里传的就是那一行的标题"，
/// ⑤ 用合成串自证扫描器认得出违规写法。④ 把"UI 里还剩下的省略号欠账"钉成<b>有名字的 1 处</b>
/// （待办撤销提示，预算 12，走 P-130 那批一起收），加一处就红。</para>
/// </summary>
public sealed class TitleTruncationGateTests
{
    private const string JudgeFile = "src/StarMark.Abstractions/ItemCardPolicy.cs";
    private const string CardFile = "src/StarMark.UI/Controls/ItemCard.xaml.cs";
    private const string MenuFile = "src/StarMark.UI/Helpers/ItemContextMenu.cs";
    private const string Call = "ItemCardPolicy.TruncatedTitle(";

    /// <summary>判据外不许再出现的两种"当场切标题"写法（内插／切片都算，注释已被抹平）。</summary>
    private static readonly Regex[] TitleSlicers =
    {
        new(@"\.Title\.Length\s*[<>]=?\s*\d", RegexOptions.Compiled),
        new(@"\.Title\[\s*\.\.", RegexOptions.Compiled),
        new(@"\.Title\.Substring\(\s*0\s*,", RegexOptions.Compiled),
    };

    /// <summary>"切片 + 省略号"的形状：字面量宽度或变量宽度都算（④ 用）。</summary>
    private static readonly Regex EllipsisCut = new(
        @"(\[\s*\.\.\s*[\w]+\s*\]|AsSpan\(\s*0\s*,\s*[\w]+\s*\)|Substring\(\s*0\s*,\s*[\w]+\s*\))[^\n""]*""\s*…",
        RegexOptions.Compiled);

    // ────────── ① 判据外不许再切标题 ──────────

    [Fact]
    public void TitleSlicingExistsNowhereButTheJudge()
    {
        var offenders = new List<string>();
        foreach (var (path, code, _) in FormatScanner.SourcesUnder("src", JudgeFile))
        {
            foreach (var line in SplitLines(code))
                if (TitleSlicers.Any(rx => rx.IsMatch(line)))
                    offenders.Add($"{path}: {line.Trim()}");
        }

        Assert.True(offenders.Count == 0,
            "标题截断又长出第二份了（该调 ItemCardPolicy.TruncatedTitle）：\n" + string.Join("\n", offenders));
    }

    // ────────── ② 两个读者，实参槽位钉死 ──────────

    [Fact]
    public void BothPreviewPathsFeedTheRowsTitleIntoTheJudge()
    {
        foreach (var file in new[] { CardFile, MenuFile })
        {
            var code = FormatScanner.Scan(ReadRepoFile(file)).Code;
            var calls = Count(code, Call);
            Assert.Equal(1, calls);                       // 每条路各一处，不多不少
            var arg = FirstArgumentAfter(code, Call);
            Assert.Equal("vm.Title", arg);                // 传的是那一行的标题；换个实参就等于悄悄开了第二条判据
        }

        var readers = FormatScanner.SourcesUnder("src")
            .Where(f => f.Code.Contains(Call, StringComparison.Ordinal))
            .Select(f => f.Path)
            .ToList();
        Assert.Equal(2, readers.Count);                   // 读者只有这两家；多出来的一律先问"是不是第二条真值"
    }

    // ────────── ③ 宽度与省略号住在判据里，且不自带内联数字 ──────────

    [Fact]
    public void TheJudgeHoldsTheBudgetAndTheEllipsisOnce()
    {
        var code = FormatScanner.Scan(ReadRepoFile(JudgeFile)).Code;

        Assert.Equal(1, Count(code, "TitleDisplayChars = 40"));
        Assert.Equal(1, Count(code, "+ \"…\""));      // 尾巴只许有一处出处
        Assert.Contains("title.Length <= TitleDisplayChars", code, StringComparison.Ordinal);
        Assert.Contains("var cut = TitleDisplayChars", code, StringComparison.Ordinal);
        Assert.Equal(0, Count(code, "title[..40"));  // 判据自己也不许把宽度内联进切片
    }

    // ────────── ④ 剩下的省略号欠账：有名字、封顶一处 ──────────

    [Fact]
    public void RemainingEllipsisDebtInUiIsOneNamedSite()
    {
        var debt = FormatScanner.SourcesUnder("src/StarMark.UI")
            .SelectMany(f => SplitLines(f.Code).Where(l => EllipsisCut.IsMatch(l)).Select(l => $"{f.Path}: {l.Trim()}"))
            .ToList();

        Assert.True(debt.Count == 1,
            $"UI 工程里\"切片＋省略号\"的写法今天恰好 1 处（待办撤销提示，见 P-130），实际 {debt.Count} 处：\n"
            + string.Join("\n", debt));
        Assert.Contains("TodoWidgetViewModel.cs", debt[0], StringComparison.Ordinal);
    }

    // ────────── ⑤ 扫描器自证（正反两面）──────────

    [Theory]
    [InlineData("var t = vm.Title.Length <= 40 ? vm.Title : vm.Title[..40] + \"…\";")]        // 三元
    [InlineData("title = vm.Title[..40] + \"…\";")]                                            // 语句片段
    [InlineData("return vm.Title.Substring(0, 40) + \"…\";")]                                    // 老写法
    [InlineData("var x = vm.Title.Length<=40 ? a : b;")]                                        // 没空格也要认
    public void ScannerSeesEveryWayToSliceATitle(string line)
        => Assert.True(TitleSlicers.Any(rx => rx.IsMatch(line)), $"漏了这个写法：{line}");

    [Theory]
    [InlineData("var firstLine = normalizedText[..nl];")]           // 切片但不是标题、也没省略号
    [InlineData("return text[..MaxSummaryChars].TrimEnd();")]        // Core 里的摘要截断，不属本条禁项
    [InlineData("Title = text.Trim(),")]                              // 赋值标题，不切
    public void ScannerDoesNotFlagUnrelatedSlices(string line)
        => Assert.False(TitleSlicers.Any(rx => rx.IsMatch(line)), $"误伤了这个写法：{line}");

    [Theory]
    [InlineData("line = line[..maxLen] + \"…\";", true)]              // 变量宽度也算
    [InlineData("s.Length <= 12 ? s : string.Concat(s.AsSpan(0, 12), \"…\"),", true)]
    [InlineData("sb.Append(\"标题很长的时候用省略号…表示\")", false)]  // 只有省略号、没有切片
    public void EllipsisCutSeesTheShapeNotTheWord(string line, bool expected)
        => Assert.Equal(expected, EllipsisCut.IsMatch(line));

    // ────────── 手边的小工具 ──────────

    private static IEnumerable<string> SplitLines(string code)
        => code.Split('\n').Select(l => l.TrimEnd('\r'));

    /// <summary>取 <paramref name="call"/> 之后<b>第一个顶层实参</b>（跟着嵌套括号与字符串，遇顶层逗号／右括号收尾）。</summary>
    private static string FirstArgumentAfter(string code, string call)
    {
        var start = code.IndexOf(call, StringComparison.Ordinal);
        Assert.True(start >= 0, $"找不到调用：{call}");
        var i = start + call.Length;                 // 已吃掉左括号
        var depth = 0;
        var begin = i;
        while (i < code.Length)
        {
            var c = code[i];
            if (c == '"' || c == '\'') { i = SkipLiteral(code, i, c); continue; }
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}')
            {
                if (depth == 0) break;
                depth--;
            }
            else if (c == ',' && depth == 0) break;
            i++;
        }
        return code[begin..i].Trim();
    }

    private static int SkipLiteral(string s, int at, char quote)
    {
        for (var j = at + 1; j < s.Length; j++)
        {
            if (s[j] == '\\') { j++; continue; }
            if (s[j] == quote) return j + 1;
        }
        return s.Length;
    }
}
