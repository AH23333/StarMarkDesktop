using System.Linq;
using StarMark.Core.Text;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 搜索高亮切分。对应浏览器扩展 highlight() 的行为：
/// 只高亮首个匹配片段，且大小写不敏感。
/// </summary>
public class HighlighterTests
{
    private static string Render(string? text, string? query)
        => string.Concat(Highlighter.Split(text, query).Select(s => s.ToString()));

    [Fact]
    public void EmptyQuery_ReturnsWholeTextAsOnePlainSegment()
    {
        var segs = Highlighter.Split("搜索笔记工具", "");
        Assert.Single(segs);
        Assert.False(segs[0].IsMatch);
        Assert.Equal("搜索笔记工具", segs[0].Text);
    }

    [Fact]
    public void NullOrEmptyText_ReturnsEmpty()
    {
        Assert.Empty(Highlighter.Split(null, "abc"));
        Assert.Empty(Highlighter.Split("", "abc"));
    }

    [Fact]
    public void MatchInMiddle_SplitsIntoThreeSegments()
    {
        var segs = Highlighter.Split("搜索笔记工具", "笔记");
        Assert.Equal(new[] { "搜索", "[笔记]", "工具" }, segs.Select(s => s.ToString()));
    }

    [Fact]
    public void MatchAtStart_SplitsIntoTwoSegments()
    {
        var segs = Highlighter.Split("搜索笔记", "搜索");
        Assert.Equal(new[] { "[搜索]", "笔记" }, segs.Select(s => s.ToString()));
    }

    [Fact]
    public void MatchIsCaseInsensitive_ButPreservesOriginalCasing()
    {
        var segs = Highlighter.Split("GitHub StarMark", "starmark");
        Assert.Equal(new[] { "GitHub ", "[StarMark]" }, segs.Select(s => s.ToString()));
    }

    [Fact]
    public void NoMatch_ReturnsWholeTextUnhighlighted()
    {
        var segs = Highlighter.Split("abcdef", "xyz");
        Assert.Single(segs);
        Assert.False(segs[0].IsMatch);
    }

    [Fact]
    public void MultiWordQuery_FallsBackToLongestMatchingToken()
    {
        // 整串 "rust async" 不在标题里，退化后应命中更长的那个词
        var segs = Highlighter.Split("learning async rust", "rust async");
        var matched = segs.Where(s => s.IsMatch).Select(s => s.Text).ToArray();
        Assert.Equal(new[] { "async" }, matched);
    }

    [Fact]
    public void OnlyFirstOccurrenceIsHighlighted()
    {
        var segs = Highlighter.Split("aa-bb-aa", "aa");
        Assert.Single(segs, s => s.IsMatch);
        Assert.Equal("[aa]-bb-aa", Render("aa-bb-aa", "aa"));
    }

    [Fact]
    public void QueryLongerThanText_DoesNotOverflow()
    {
        var segs = Highlighter.Split("ab", "abcdef");
        Assert.NotEmpty(segs);
    }

    /// <summary>
    /// 跨输入不变式（AV）：Split 必须**无损**——各片段原文按序拼接恒等于原串。
    /// 现有逐例断言只钉死特定切点；若 :62-65 的 prefix/match/tail 切片将来出现 off-by-one，
    /// 这些逐例可能仍通过却静默丢字/重字，本不变式可兜住。
    /// </summary>
    [Theory]
    [InlineData("搜索笔记工具", "笔记")]          // CJK 中段命中，前中后三段
    [InlineData("GitHub StarMark", "starmark")]   // 末尾命中，无尾段
    [InlineData("aa-bb-aa", "aa")]                // 多次出现，仅高亮首个
    [InlineData("learning async rust", "rust async")] // 整串未命中→退化命中较长词
    [InlineData("abc", "")]                       // 空查询
    [InlineData("abc", "   ")]                    // 纯空白查询
    [InlineData("ab", "abcdef")]                  // 查询比正文长且无任一 token 命中
    [InlineData("x", "x")]                        // 单字符全等
    public void Split_IsLossless_ConcatSegmentsEqualsOriginal(string text, string query)
        => Assert.Equal(text, string.Concat(Highlighter.Split(text, query).Select(s => s.Text)));

    [Fact]
    public void Split_AtMostOneMatchSegment_PreservesOriginalCasing()
    {
        var segs = Highlighter.Split("aa-bb-aa", "AA");
        var match = Assert.Single(segs, s => s.IsMatch);
        Assert.Equal("aa", match.Text); // 高亮片段保留原文大小写、取首个出现
    }
}
