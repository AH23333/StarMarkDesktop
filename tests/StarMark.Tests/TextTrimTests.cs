#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Core.Feed;
using StarMark.Core.Ocr;
using StarMark.Core.Widgets;

namespace StarMark.Tests;

/// <summary>
/// 批次 SC：全仓"截断＋省略号"合流到 <see cref="TextTrim"/>（P-130）。
/// <para>这一颗只管<b>怎么切</b>，宽度由各宿主传（40／160／24／400／200…）——所以这里既测判据本身，
/// 也测<b>每一个改道过的宿主仍然逐字不变</b>；<c>OcrText.Preview</c> 那把是"累加式"，
/// 形状闸门看不见它（#193），只有一枚这样的行为测能兜住。</para>
/// </summary>
public sealed class TextTrimTests
{
    private const string Cat = "🐱";      // U+1F431：两枚 UTF-16 单元

    private static string A(int n) => new string('a', n);

    private static int Unpaired(string s)
    {
        var bad = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))) bad++;
            else if (char.IsLowSurrogate(s[i]) && (i == 0 || !char.IsHighSurrogate(s[i - 1]))) bad++;
        }
        return bad;
    }

    // ────────── 判据本身 ──────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void Ellipsize_WithinBudget_ReturnsTheWholeString(int length)
        => Assert.Equal(A(length), TextTrim.Ellipsize(A(length), length));

    [Theory]
    [InlineData(10, 11)]
    [InlineData(10, 400)]
    public void Ellipsize_CutsAtBudgetAndAddsOneEllipsis(int max, int length)
    {
        var result = TextTrim.Ellipsize(A(length), max);
        Assert.Equal(A(max) + "…", result);
    }

    [Fact]
    public void Cut_NeverSplitsASurrogatePair_AtEveryPossibleBudget()
    {
        // 一枚 emoji 在串里滑动位置，预算从 1 到 12 全试一遍：总有一种预算会正好压在代理对上
        for (var offset = 0; offset <= 8; offset++)
            for (var budget = 1; budget <= 12; budget++)
            {
                var text = A(offset) + Cat + A(12 - offset);
                Assert.Equal(0, Unpaired(TextTrim.Cut(text, budget)));
                Assert.Equal(0, Unpaired(TextTrim.Ellipsize(text, budget)));
                Assert.True(TextTrim.Ellipsize(text, budget).Length <= budget + 1);
            }
    }

    [Fact]
    public void BackingOffOnlyHappensWhenThePairStraddlesTheBoundary()
    {
        var text = A(4) + Cat + "xyz";        // 索引 4＝高代理、5＝低代理
        Assert.Equal(A(4) + "…", TextTrim.Ellipsize(text, 4));    // 整枚都在边界外 ⇒ 逐字照旧（连半个都不留）
        Assert.Equal(A(4) + "…", TextTrim.Ellipsize(text, 5));    // 边界压在代理对中间 ⇒ 退一格
        Assert.Equal(A(4) + Cat + "…", TextTrim.Ellipsize(text, 6)); // 整枚在边界内 ⇒ 逐字照旧
    }

    [Fact]
    public void BudgetOfOneBeforeAPairYieldsJustTheEllipsis()
    {
        var result = TextTrim.Ellipsize(Cat + "abc", 1);
        Assert.Equal("…", result);                    // 退到底就是空串＋省略号，不是一枚半代理
        Assert.Equal(0, Unpaired(result));
    }

    [Theory]
    [InlineData("标")]
    [InlineData("b")]
    [InlineData("★")]
    [InlineData("é")]   // BMP 内组合字符：两法逐字相同（本批有意不动字素切分）
    public void BmpUnits_ReadExactlyLikeAPlainSlice(string unit)
    {
        var text = string.Concat(Enumerable.Repeat(unit, 30));
        Assert.Equal(text[..12] + "…", TextTrim.Ellipsize(text, 12));
    }

    // ────────── 每个改道过的宿主：读数不变，而且不切半个字 ──────────

    [Fact]
    public void AlarmLabel_StillUsesItsOwnBudget()
    {
        Assert.Equal(24, AlarmPolicy.MaxLabelLength);
        Assert.Equal(A(24) + "…", AlarmPolicy.TrimLabel(A(30)));           // 普通形状：与旧写法逐字相同
        var straddle = A(23) + Cat + "xyz";                                  // 高代理正好落在第 24 枚上
        Assert.Equal(A(23) + "…", AlarmPolicy.TrimLabel(straddle));
        Assert.Equal(0, Unpaired(AlarmPolicy.TrimLabel(straddle)));
        Assert.Equal("喝水", AlarmPolicy.TrimLabel("  喝水  "));             // 只截不换行，兜底不报错
    }

    [Fact]
    public void ClipboardTitle_KeepsRoomForItsSecondEllipsis()
    {
        Assert.Equal(160, ClipboardPolicy.MaxTitleChars);
        var longOneLine = A(400);
        var title = ClipboardPolicy.BuildTitle(longOneLine);
        Assert.True(title.Length <= ClipboardPolicy.MaxTitleChars, "标题不许超出自己声明的上限");
        Assert.EndsWith("…", title, StringComparison.Ordinal);
        Assert.Equal(0, Unpaired(title));
    }

    [Fact]
    public void AvailabilityHint_StaysUnderItsTwoHundredUnits()
    {
        var text = A(199) + Cat + A(50);
        var line = SourceAvailabilityText.Of(false, text);
        Assert.StartsWith("没有这项数据：", line, StringComparison.Ordinal);
        Assert.Equal(0, Unpaired(line));
        Assert.True(line.Length <= "没有这项数据：".Length + 200 + 1);
    }

    [Fact]
    public void RssSummary_StillTrimsTrailingSpacesBeforeTheEllipsis()
    {
        var html = A(398) + "  " + Cat + A(20);
        var summary = RssParser.Clean(html);
        Assert.EndsWith("…", summary, StringComparison.Ordinal);
        Assert.False(summary.TrimEnd('…').EndsWith(" ", StringComparison.Ordinal), "尾巴上的空白该被 TrimEnd 掉（SC 之前也是这个读数）");
        Assert.Equal(0, Unpaired(summary));
    }

    /// <summary>
    /// 形状闸门看不见"逐枚累加到上限就停"（#193），所以这一枚只能靠行为测兜：<c>OcrText.Preview</c> 的边界。
    /// <para><b>边界要真压到那一枚上</b>：输入里 emoji 的高代理必须正好是第 <c>maxChars</c> 枚被追加的字符
    /// （所以是 199 枚 ASCII 在前，不是 200）——SC7 的台架就是把这条撞成空转之后才改成这样的。</para>
    /// </summary>
    [Fact]
    public void OcrPreview_ShapeTheGateCannotSee_IsStillCoveredByBehaviour()
    {
        var straddle = A(199) + Cat + A(50);      // 第 200 枚被追加的就是高代理
        Assert.True(char.IsHighSurrogate(straddle[199]));
        var preview = OcrText.Preview(straddle, 200);
        Assert.Equal(0, Unpaired(preview));
        Assert.True(preview.Length <= 201);
        var shortOne = "识别到的文字";
        Assert.Equal(shortOne, OcrText.Preview(shortOne, 200));   // 没超长 ⇒ 不许凭空长出省略号
    }

    /// <summary>标题那颗已经退成纯转发：宽度还住自己这儿，切法不许回来。</summary>
    [Fact]
    public void TitleJudge_ForwardsAndKeepsOnlyTheWidth()
        => Assert.Equal(TextTrim.Ellipsize(A(50), 40), ItemCardPolicy.TruncatedTitle(A(50)));
}
