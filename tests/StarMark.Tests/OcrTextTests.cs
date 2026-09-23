#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using StarMark.Abstractions.Ocr;
using StarMark.Core.Ocr;

namespace StarMark.Tests;

/// <summary>
/// OCR 拼回文本的测试。<b>用例里的词表全部来自本机 Windows.Media.Ocr 的真实输出</b>
/// （冒烟 <c>StarMark.SmokeTest ocr</c>，引擎 zh-Hans-CN，30/30 行满足
/// <c>line.Text == string.Join(" ", words)</c>），不是我编的形状。
/// 引擎怎么切词这件事改不了，能改的只有"我们怎么拼回去"，所以要把它给的样子钉住。
/// </summary>
public sealed class OcrTextTests
{
    private static OcrLine Line(params string[] words) => new(words);

    [Fact]
    public void JoinWords_GluesChineseAndFullWidthPunctuation()
    {
        // 真实一行（月历/组件标题区）：本|场|据|：|R|2|¥|0|0
        var joined = OcrText.JoinWords(new[] { "本", "场", "据", "：", "R", "2", "¥", "0", "0" });
        Assert.Equal("本场据：R 2 ¥ 0 0", joined);
        // 全角冒号后面跟拉丁不该被加空格（原文是"：R"）
        Assert.Contains("：R", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void JoinWords_KeepsASpaceBetweenChineseAndLatin()
    {
        // 真实一行：单|个|占|用|最|大|的|添|是|p5 —— 只有末尾"是|p5"两侧不同类，故留一个空格
        Assert.Equal("单个占用最大的添是 p5",
            OcrText.JoinWords(new[] { "单", "个", "占", "用", "最", "大", "的", "添", "是", "p5" }));
    }

    [Fact]
    public void JoinWords_GluesFullWidthBracketPairs()
    {
        // 真实一行开头：（|）|安|好
        Assert.Equal("（）安好", OcrText.JoinWords(new[] { "（", "）", "安", "好" }));
    }

    [Fact]
    public void JoinWords_LeavesLatinAndSymbolsSpacedApart()
    {
        // 真实一行：#|||=|左|2|+|A|+|B —— 拉丁/符号之间一律留空格，这一行拼出来与引擎自己的 Text 相同
        Assert.Equal("# | = 左 2 + A + B",
            OcrText.JoinWords(new[] { "#", "|", "=", "左", "2", "+", "A", "+", "B" }));
    }

    [Fact]
    public void JoinWords_DropsEmptyAndWhitespaceTokens()
    {
        Assert.Equal("a b", OcrText.JoinWords(new[] { "a", "", "   ", "b" }));
        Assert.Equal(string.Empty, OcrText.JoinWords(new[] { "", " " }));
        Assert.Equal(string.Empty, OcrText.JoinWords(Array.Empty<string>()));
        Assert.Equal(string.Empty, OcrText.JoinWords(null));
    }

    [Fact]
    public void JoinWords_LooksOnlyAtTheTwoBoundaryChars()
    {
        // 词内部什么都不参与决定：接缝两侧的字符才说了算
        Assert.Equal("A本场", OcrText.JoinWords(new[] { "A本", "场" }));      // 接缝：本|场 ⇒ 黏
        Assert.Equal("本A 场", OcrText.JoinWords(new[] { "本A", "场" }));     // 接缝：A|场 ⇒ 加空格
        Assert.Equal("AH: 球", OcrText.JoinWords(new[] { "AH:", "球" }));     // 半角冒号不是全角标点 ⇒ 不触发"标点后不空格"那条
    }

    [Fact]
    public void Assemble_SkipsBlankLinesAndKeepsOrder()
    {
        var text = OcrText.Assemble(new[]
        {
            Line("你", "好"),
            Line(),
            Line("   "),
            Line("第", "二", "行"),
        });
        Assert.Equal("你好\n第二行", text);
    }

    [Fact]
    public void Assemble_EmptyOrNull_GivesEmptyString()
    {
        Assert.Equal(string.Empty, OcrText.Assemble(Array.Empty<OcrLine>()));
        Assert.Equal(string.Empty, OcrText.Assemble(null));
    }

    [Fact]
    public void CountMeaningful_CountsCharactersNotSpaceSeparatedWords()
    {
        // 中文没有空格：按"分词数"报会永远得到 1，那是个假数字
        Assert.Equal(5, OcrText.CountMeaningful("本场据：R"));
        Assert.Equal(4, OcrText.CountMeaningful("你 好\n1 2"));
        Assert.Equal(0, OcrText.CountMeaningful("　 　"));   // 全角/半角空格都不算内容
        Assert.Equal(0, OcrText.CountMeaningful(null));
    }

    [Fact]
    public void Preview_CollapsesWhitespaceAndMarksTruncation()
    {
        Assert.Equal("aa b…", OcrText.Preview("aa\n\nbb", 3));
        Assert.Equal("abcd", OcrText.Preview("abcd", 4));      // 正好到头 ⇒ 不加省略号
        Assert.Equal("abcd…", OcrText.Preview("abcde", 4));
        Assert.Equal(string.Empty, OcrText.Preview(null));
        Assert.Equal(string.Empty, OcrText.Preview("   \n "));
    }

    [Fact]
    public void ResultProblem_EmptyCaseNamesTheLimits()
    {
        var problem = OcrText.ResultProblem(string.Empty)!;
        Assert.Contains("没有认出文字", problem);
        Assert.Contains("图形", problem);      // 要说清哪种情况注定读不出来，别让人反复试
        Assert.Null(OcrText.ResultProblem("有字"));
    }

    [Theory]
    [InlineData('本', true)]     // CJK 基本区
    [InlineData('あ', true)]     // 平假名
    [InlineData('한', true)]     // 韩文音节
    [InlineData('ｱ', true)]     // 半角片假名（IsCjk 覆盖了它）
    [InlineData('。', true)]     // 全角标点
    [InlineData('　', true)]     // 全角空格也算黏字符
    [InlineData('A', false)]
    [InlineData('1', false)]
    [InlineData('¥', false)]    // 半角货币符号：与真机输出里那个 ¥ 同一类，不该黏
    public void IsGlue_ClassifiesBoundaryChars(char c, bool expected)
        => Assert.Equal(expected, OcrText.IsGlue(c));

    [Theory]
    [InlineData('　')]  // U+3000
    [InlineData('〿')]  // U+303F 上界
    [InlineData('！')]  // U+FF01
    [InlineData('｠')]  // U+FF60 上界
    [InlineData('￠')]  // U+FFE0
    [InlineData('￦')]  // U+FFE6 上界
    public void IsFullWidthPunctuation_CoversAllThreeRanges(char c)
        => Assert.True(OcrText.IsFullWidthPunctuation(c));

    [Theory]
    [InlineData('¥')]   // U+00A5
    [InlineData('~')]
    [InlineData('中')]
    public void IsFullWidthPunctuation_RejectsEverythingOutsideThoseRanges(char c)
        => Assert.False(OcrText.IsFullWidthPunctuation(c));

    /// <summary>
    /// 整次识别的端到端拼接：拿真机那几行原样喂进来，钉住"最后能粘出去的东西"。
    /// 这是本文件唯一一条"看起来像快照"的用例——它有意为之：拼法一改就会红，
    /// 而拼法只有照着引擎实际给的东西改才有意义。
    /// </summary>
    [Fact]
    public void Assemble_RealEngineOutput_ProducesReadableChinese()
    {
        var observed = new[]
        {
            new[] { "11", "14", "7", "@@@@", "10", "@016" },
            new[] { "B", "=", "5" },
            new[] { "#", "|", "=", "左", "2", "+", "A", "+", "B" },
            new[] { "本", "场", "据", "：", "R", "2", "¥", "0", "0" },
            new[] { "（", "）", "安", "好", "AH:", "？", "乓", "球" },
            new[] { "-", "一", "木", "瓜", "秧", "秧", "：", "两", "个", "盘" },
        };
        var text = OcrText.Assemble(observed.Select(words => new OcrLine(words)).ToList());
        var lines = text.Split('\n');

        Assert.Equal(6, lines.Length);
        Assert.Equal("11 14 7 @@@@ 10 @016", lines[0]);
        Assert.Equal("本场据：R 2 ¥ 0 0", lines[3]);
        Assert.Equal("（）安好 AH: ？乓球", lines[4]);
        Assert.Equal("- 一木瓜秧秧：两个盘", lines[5]);
        // 拼完不该出现"字 字"这种中文被空格凿开的样子
        Assert.DoesNotContain(lines, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"[一-鿿] [一-鿿]"));
    }
}
