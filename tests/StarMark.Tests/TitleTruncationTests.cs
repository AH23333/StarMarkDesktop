#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using StarMark.Abstractions;

namespace StarMark.Tests;

/// <summary>
/// 批次 SB：预览窗标题的截断收成一颗 <see cref="ItemCardPolicy.TruncatedTitle"/>（P-123 清单第二条）。
/// <para>三件事分开钉：① 边界三态；② **不许把一枚 emoji 切成半个**（这批唯一真正的行为变化）；
/// ③ 反面前提——收口前那两处写法在哪种形状下会切坏、在哪种形状下与新判据逐字相同。
/// ③ 是永久前提测，不是临时探针：哪天有人把"退一格"当成冗余优化掉，会先在这里红。</para>
/// <para>宽度一律用 <c>new string('a', n)</c> 构造而不是数引号里的 a——数错了一个字，测的就不是那条边界。</para>
/// </summary>
public sealed class TitleTruncationTests
{
    /// <summary>一枚 emoji：U+1F431，在 UTF-16 里占<b>两枚</b>单元。</summary>
    private const string Cat = "🐱";

    private const char Ellipsis = '…';

    private static string A(int n) => new string('a', n);

    /// <summary>收口前两处逐字相同的写法（只为对照而留，不参与生产代码）。</summary>
    private static string NaiveBeforeFix(string title)
        => title.Length <= 40 ? title : title[..40] + "…";

    /// <summary>串里有没有"半个代理"（未配对的高／低位）——那就是界面上那个方块。</summary>
    private static int UnpairedSurrogates(string s)
    {
        var bad = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))) bad++;
            else if (char.IsLowSurrogate(s[i]) && (i == 0 || !char.IsHighSurrogate(s[i - 1]))) bad++;
        }
        return bad;
    }

    // ────────── ① 边界三态 ──────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(39)]
    [InlineData(40)]   // 闭区间：正好一行那么宽，不许长出省略号
    public void WithinBudget_ComesOutUnchanged(int length)
    {
        var title = A(length);
        Assert.Equal(title, ItemCardPolicy.TruncatedTitle(title));
    }

    [Theory]
    [InlineData(41)]
    [InlineData(60)]
    [InlineData(4000)]
    public void OverBudget_CutsAtTheBudgetAndAppendsExactlyOneEllipsis(int length)
    {
        var kept = ItemCardPolicy.TitleDisplayChars;
        var result = ItemCardPolicy.TruncatedTitle(A(length));

        Assert.Equal(A(kept) + "…", result);
        Assert.Equal(Ellipsis, result[^1]);
        Assert.Equal(kept + 1, result.Length);          // 尾巴只有一枚字符，不是 "..."
    }

    // ────────── ② 不许切半个 emoji ──────────

    [Fact]
    public void BoundaryInsideASurrogatePair_BackOffsOneUnitInsteadOfLeavingHalfAGlyph()
    {
        // 39 枚 'a' 之后紧跟 emoji ⇒ 第 40 枚单元是<b>高位代理</b>，照原写法切就留下半个 🐱
        var title = A(39) + Cat + "tail";
        Assert.Equal(45, title.Length);

        var result = ItemCardPolicy.TruncatedTitle(title);

        Assert.Equal(0, UnpairedSurrogates(result));
        Assert.Equal(A(39) + "…", result);
    }

    [Fact]
    public void PairNotOnTheBoundary_ReadsExactlyLikeBeforeFix()
    {
        var after = A(40) + Cat;              // 整枚 emoji 在边界之后 ⇒ 切掉的就是 emoji
        var before = A(38) + Cat + "xyzw";    // 整枚 emoji 在边界之内

        Assert.Equal(NaiveBeforeFix(after), ItemCardPolicy.TruncatedTitle(after));
        Assert.Equal(NaiveBeforeFix(before), ItemCardPolicy.TruncatedTitle(before));
        Assert.Equal(A(38) + Cat + "…", ItemCardPolicy.TruncatedTitle(before));
        Assert.All(new[] { after, before }, t => Assert.Equal(0, UnpairedSurrogates(ItemCardPolicy.TruncatedTitle(t))));
    }

    /// <summary>
    /// 永久前提测：<b>收口前那个写法确实会切坏</b>，而且只坏在这一种形状上。
    /// 这条不是冗余——它把"为什么要退那一格"从注释变成会红的断言（#161：净要带凭据）。
    /// </summary>
    [Fact]
    public void BeforeTheFix_TheNaiveSliceLeftHalfAnEmoji_OnExactlyOneShape()
    {
        Assert.Equal(1, UnpairedSurrogates(NaiveBeforeFix(A(39) + Cat + "tail")));
        Assert.Equal(0, UnpairedSurrogates(NaiveBeforeFix(A(40) + Cat)));
        Assert.Equal(0, UnpairedSurrogates(NaiveBeforeFix(A(38) + Cat + "xyzw")));
    }

    /// <summary>
    /// 逐字不变清单：中文／ASCII／BMP 符号／BMP 内组合字符一律与收口前一致。
    /// <para>组合字符（<c>e</c> + 附加符号）在代理对之外<b>仍会被切开</b>——有意不动它：
    /// 本批只消灭"半枚 emoji＝一个方块"，不顺手改字素切分（那要重量所有形状，登记在 P-130）。</para>
    /// </summary>
    [Theory]
    [InlineData("标")]
    [InlineData("b")]
    [InlineData("★")]
    [InlineData("é")]   // e + U+0301：两枚 BMP 单元＝一个显示字位
    public void BmpShapes_ReadWordForWordLikeBeforeFix(string unit)
    {
        var title = string.Concat(Enumerable.Repeat(unit, 50));
        Assert.Equal(NaiveBeforeFix(title), ItemCardPolicy.TruncatedTitle(title));
        Assert.Equal(A(40) + "…", ItemCardPolicy.TruncatedTitle(new string('a', 40) + title));
    }

    /// <summary>全覆盖那一面：任意"若干 ASCII ＋一枚 emoji ＋若干 ASCII"的形状都不许留下未配对代理。</summary>
    [Fact]
    public void NoLeadCountEverProducesAnUnpairedSurrogate()
    {
        var samples = new List<string>();
        for (var lead = 0; lead <= 44; lead++)
            samples.Add(A(lead) + Cat + new string('z', 44 - lead));
        samples.Add(Cat + A(60));
        samples.Add(A(20) + Cat + Cat + Cat + A(20));
        samples.Add("🦄🌈📚 项目 资料 归档 2026 第四季度 最终版 副本(3).pptx");

        foreach (var s in samples)
        {
            var result = ItemCardPolicy.TruncatedTitle(s);
            Assert.Equal(0, UnpairedSurrogates(result));
            Assert.True(result.Length <= ItemCardPolicy.TitleDisplayChars + 1);
        }
    }
}
