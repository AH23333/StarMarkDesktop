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
}
