#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// <b>裸内插孔</b>会不会打出小数点——把 P-129 记的那条"闸门能力边界"从一句话变成一条守门（批次 SG）。
/// <para>
/// RZ 那批六条规则全靠"扫到一枚会随文化变的<b>格式串</b>"来判；而默认 <c>ToString()</c> 一个格式串都没有：
/// <c>$"{1234.5}"</c> 在 <c>de-DE</c> 得到 <c>1234,5</c>、在阿语区得到 <c>1234٫5</c>——<b>写了 <c>:0.#</c> 会红，
/// 什么都不写反而全绿</b>。这是格式串闸门的盲区，不是漏实现。
/// </para>
/// <para>
/// <b>这条守得到哪、守不到哪（写清楚，别让人以为边界已经封住）</b>：
/// ① 守得到——孔里出现<b>算得出非整值</b>的形状（小数面量参与运算、<c>Math.Round</c> 带位数、开方/三角/对数、
///   <c>TimeSpan.Total*</c>、显式 <c>(double)</c>）却没被一颗判据包着、也没先夹成整数；
/// ② 守不到两类，都不能当成"已经安全"：<b>a)</b> <c>$"{someDoubleVariable}"</c>——孔里只有一个变量名，
///   <b>类型信息不在文本里</b>；<b>b)</b> 返回 <c>double</c> 却不在①那张表上的调用（如 <c>Math.Clamp(气压, 0, 1100)</c>）。
///   要堵 a) 得上 Roslyn/编译器级检查，账本 P-129 的默认处置是<b>不付这个成本</b>（为一条今天实测为零的面
///   引入编译器级依赖，属于"给未发生的输入加防御"，与 CE/BZ/CT 拒登那几条同口径）。
///   ⇒ 所以这条闸门的价值不是"封住"，是<b>让新写浮点读数的人必须做一个看得见的决定</b>（走判据、或明写夹成整数）。
/// </para>
/// <para>
/// <b>今天扫到的规模</b>：全仓 <c>src/</c> 里裸内插孔 <b>1287</b> 枚、判违规 <b>0</b> 枚——
/// 下面那条地板断言钉的就是这个规模（扫描器坏掉时不许靠"零命中"冒充绿灯，#161）。
/// </para>
/// </summary>
public sealed class FloatHoleGateTests
{
    /// <summary>
    /// 会<b>真的打出小数点</b>的形状。这份表是<b>被误伤教出来的</b>：第一版按"孔里有 <c>Math.</c>"与
    /// "<c>.TotalXxx</c>"来判，一次报出 7 处，逐条打开全是安全的——
    /// <c>r.TotalCount</c>/<c>node.TotalCount</c> 是 <c>int</c>（只有 <c>TimeSpan</c> 那五个 <c>Total*</c> 是 <c>double</c>）、
    /// <c>Palette[Math.Clamp(i,0,n)].Name</c> 里 <c>Math.</c> 只是取下标、整个孔是<b>字符串</b>、
    /// <c>Math.Round(v, MidpointRounding.AwayFromZero)</c> 与 <c>Math.Round(v)</c> 的结果是<b>整数值的 double</b>，
    /// 而 .NET 对整数值不写小数点 ⇒ 七种文化逐字相同。
    /// ⇒ 只有"算得出<b>非整</b>值"的才收：<c>Math.Round</c> 带<b>位数</b>那一支、开方/三角/对数这类必出小数的、
    /// 小数面量参与运算、<c>TimeSpan.Total*</c>、显式 <c>(double)</c> 转换。
    /// 判据同 #144：<b>天天误伤的闸门最后会被人加白名单绕过去</b>，宁可窄到要配合下面的边界说明。
    /// </summary>
    private static readonly Regex FractionShape = new(
        @"\d+\.\d+"
        + @"|\bMath\.Round\s*\([^()]*,\s*\d+\s*\)"
        + @"|\bMath\.(Sqrt|Cbrt|Pow|Exp|Log|Log2|Log10|Sin|Cos|Tan|Asin|Acos|Atan|Atan2)\b"
        + @"|\.Total(Days|Hours|Minutes|Seconds|Milliseconds)\b"
        + @"|\(\s*double\s*\)",
        RegexOptions.Compiled);

    /// <summary>
    /// 已经<b>处理过</b>的三种形状：走显示侧判据、显式夹成整数、或自己把小数位交给格式化
    /// （第三种由 RZ 的格式串闸门管，见 <see cref="FormattedHolesAreLeftToTheOtherGate"/>）。
    /// </summary>
    private static readonly Regex HandledShape = new(
        @"NumberText\.|FileSizeText\.|DateTimeText\.|\(\s*int\s*\)|\(\s*long\s*\)|\.ToString\(",
        RegexOptions.Compiled);

    /// <summary>这条闸门唯一的判据：孔里既算得出非整值、又没被处理过。</summary>
    private static bool IsFlagged(string hole)
    {
        var body = hole.Trim();
        return body.Length > 0 && FractionShape.IsMatch(body) && !HandledShape.IsMatch(body);
    }

    [Fact]
    public void NoFractionShapedHoleGoesUnformatted()
    {
        var offenders = new List<string>();
        var seen = 0;
        foreach (var (path, _, _, interpolated) in FormatScanner.SourcesUnder("src"))
            foreach (var literal in interpolated)
                foreach (var hole in FormatScanner.BareHoles(literal))
                {
                    if (hole.Trim().Length == 0) continue;
                    seen++;
                    // 只在"孔里算得出非整值"时管——纯整数与纯字符串走的是另一条口径（七种文化逐字相同）
                    if (IsFlagged(hole)) offenders.Add($"{path}: {{{hole.Trim()}}}");
                }

        // 反空转（#161）：先证明扫描器真看得见内插孔，否则下面的零命中只是因为什么都没扫到
        Assert.True(seen >= 800, $"整仓只认出 {seen} 个裸内插孔（SF/SG 收官当天实测 1287）——提取器或扫描路径失效了，先修闸门再说结论");
        Assert.True(offenders.Count == 0,
            "内插孔里算出了小数，却没走判据也没夹成整数（非公历文化会把小数点打成逗号）：\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// 把"误伤面"钉成断言，防止下一次有人把形状表重新放宽（放宽的代价就是 #144 那条：闸门天天红 ⇒ 被白名单绕过）。
    /// 左边这五枚都是<b>今天仓里真实存在</b>且七种文化逐字相同的读数。
    /// </summary>
    [Theory]
    [InlineData("r.TotalCount")]                                            // int 属性，不是 TimeSpan.Total*
    [InlineData("node.TotalCount")]
    [InlineData("Annotation.Palette[Math.Clamp(_colourIndex, 0, 5)].Name")] // Math. 只用于取下标，孔是字符串
    [InlineData("Math.Round(kmPerHour, MidpointRounding.AwayFromZero)")]     // 结果是整值 double，默认格式不写小数点
    [InlineData("Math.Round(report.Now.PressureHpa)")]
    public void IntegralReadingsAreNotFlagged(string hole)
        => Assert.False(IsFlagged(hole), $"这枚不该被判成违规（会把人逼去加白名单）：{hole}");

    [Theory]
    [InlineData("bytes / 1024.0")]                        // 去掉 NumberText 包裹就是当年那条病
    [InlineData("value / (1024.0 * 1024)")]
    [InlineData("Math.Round(kmPerHour, 1)")]              // 带位数 ⇒ 真会打出小数点
    [InlineData("Math.Sqrt(area)")]
    [InlineData("span.TotalHours")]
    [InlineData("(double)exact.Count / total")]
    public void FractionReadingsAreFlagged(string hole)
        => Assert.True(IsFlagged(hole), $"这枚会打出随文化变的小数点，必须红：{hole}");

    /// <summary>
    /// 提取器自证（#195：机制本身要钉成一条测试，否则"我用的是内插那一份"只是口头承诺）。
    /// 三种内插写法都要认得，普通串与注释里的例子都不许算。
    /// </summary>
    [Fact]
    public void TheExtractorSeparatesInterpolationFromPlainStrings()
    {
        const string snippet = """
            var a = $"用了 {value / 2.0} 这么算";
            var b = @$"逐字 {value / 2.0} 这么算";
            var c = $@"倒过来 {value / 2.0} 也这么算";
            var plain = "这里有个 {value / 2.0} 但它不是内插";
            // 注释里再写一次 $"{value / 2.0}" 不算行为
            """;
        var (_, _, interpolated) = FormatScanner.Scan(snippet);

        Assert.Equal(3, interpolated.Count);
        Assert.All(interpolated, lit => Assert.Contains("value / 2.0", lit, StringComparison.Ordinal));
        Assert.DoesNotContain(interpolated, lit => lit.Contains("不是内插", StringComparison.Ordinal));
    }

    /// <summary>带格式说明符的孔<b>不</b>归这条管（那是 RZ 的格式串闸门），钉住这条分工不被写歪。</summary>
    [Fact]
    public void FormattedHolesAreLeftToTheOtherGate()
    {
        var holes = FormatScanner.BareHoles(@"体积 {bytes / 1024.0:0.#} KB，比率 {ratio:P0}，条件 {ok ? 1 : 0}")
            .ToList();
        Assert.DoesNotContain(holes, h => h.Contains("bytes", StringComparison.Ordinal));
        Assert.DoesNotContain(holes, h => h.Contains("ratio", StringComparison.Ordinal));
    }
}
