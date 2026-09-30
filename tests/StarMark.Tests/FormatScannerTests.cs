#nullable enable
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <see cref="FormatScanner"/> 自己的自检（批次 RY 立，RZ 抽出共用时补全）。
/// <para>
/// 为什么单独一份：日期闸门与数字闸门现在<b>共用同一个扫描器</b>——扫描器的语法面只要有洞，
/// 两道闸门会同时假绿。所以它的自检也只许有一份，写在判据旁边而不是某一枚闸门里。
/// 这一族用例全部是 #190 里真撞出来的：每一种"我以为扫得到"的写法，都对应过一次假绿灯。
/// </para>
/// </summary>
public sealed class FormatScannerTests
{
    /// <summary>五种字符串写法都得看得见里面的格式串。</summary>
    [Theory]
    [InlineData(@"var a = ""yyyy-MM-dd"";")]                          // 普通串
    [InlineData(@"var a = $""{x:yyyy-MM-dd}"";")]                      // 内插孔里的格式
    [InlineData(@"var a = $""下一次 {due.ToString(""HH:mm"")}。"";")]  // 内插孔里再套字符串（RY 的 RI4/RI5 就是这么漏的）
    [InlineData(@"var a = @""h\:mm\:ss"";")]                           // 逐字串 + TimeSpan 的转义冒号
    [InlineData(@"var a = """"""yyyy-MM-dd"""""";")]                   // 原始串 """…"""
    public void SeesThroughEveryStringSyntax(string snippet)
        => Assert.Contains(FormatScanner.Scan(snippet).Literals, FormatScanner.IsDateFormatLiteral);

    /// <summary>
    /// 注释里举的格式例子<b>不该</b>算违规（那是解释不是行为），而同一行<b>注释/URL 之后</b>的真代码仍要扫得到——
    /// 按行切 <c>//</c> 的老写法做不到后半句（RY 的 RI9）。
    /// </summary>
    [Fact]
    public void CommentsAreSkippedWithoutEatingTheRestOfTheLine()
    {
        const string snippet = """
            // 旧口径长这样：var legacy = "yyyy-MM-dd"; 如今归判据
            var url = "https://example.com/a"; var t = now.ToString("HH:mm");
            /* 也举一次 "dd-MM-yyyy" */ var m = "MM-dd";
            """;
        var (code, literals, _) = FormatScanner.Scan(snippet);

        Assert.DoesNotContain("yyyy-MM-dd", literals);
        Assert.DoesNotContain("dd-MM-yyyy", literals);
        Assert.Contains("HH:mm", literals);                       // URL 的双斜杠不是注释：同行后半段还在
        Assert.Contains("MM-dd", literals);                       // 块注释之后同一行也还在
        Assert.Contains(@"ToString(""HH:mm"")", code, System.StringComparison.Ordinal);   // 字符串原样留着给实参表扫描
        Assert.DoesNotContain("var legacy", code, System.StringComparison.Ordinal);        // 注释本体确实被抹平
    }

    /// <summary>字符字面量 <c>'"'</c> 里那半个引号不许冒充串起点（否则后面整段都被当成字面量吞掉）。</summary>
    [Fact]
    public void CharLiteralHoldingAQuoteDoesNotDerailTheScan()
    {
        const string snippet = "var sep = '\"'; var t = now.ToString(\"HH:mm\", CultureInfo.InvariantCulture);";
        var literals = FormatScanner.Scan(snippet).Literals;

        Assert.Contains("HH:mm", literals);
        Assert.DoesNotContain(literals, lit => lit.Contains("var t = now.ToString", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// 内插孔里的格式按"<b>第一枚冒号之后全是格式</b>"取。
    /// 贪心前缀（<c>\{[^{}]*:…\}</c>）会把 <c>{due:HH:mm}</c> 读成"表达式 <c>due:HH</c> ＋格式 <c>mm</c>"，
    /// 于是两道闸门对全仓最常见的两枚冒号格式当场失明（RY 的 RI12）。
    /// </summary>
    [Theory]
    [InlineData(@"$""{due:HH:mm}""", "{due:HH:mm}", "HH:mm")]
    [InlineData(@"$""{x,5:MM-dd HH:mm}""", "{x,5:MM-dd HH:mm}", "MM-dd HH:mm")]
    [InlineData(@"$""{v:N0}""", "{v:N0}", "N0")]
    public void HoleFormatTakesEverythingAfterTheFirstColon(string snippet, string hole, string format)
    {
        var found = FormatScanner.HoleFormats(FormatScanner.Scan(snippet).Literals.First()).ToList();

        Assert.Contains((hole, format), found);
        Assert.True(FormatScanner.IsDateFormatLiteral(format) || FormatScanner.IsNumberFormatLiteral(format),
            $"孔里这段既不算日期也不算数字格式，等于两种闸门都不管：{format}");
    }
}
