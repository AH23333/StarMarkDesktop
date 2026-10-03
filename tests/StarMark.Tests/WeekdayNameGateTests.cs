#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「星期几」那张中文表只许有一处（批次 SF，P-128）。
/// <para>
/// 判据在 <c>Abstractions</c> 的那部分由 <see cref="WeekdayNameTests"/> <b>真测</b>；这里守的是判据<b>够不着的两件事</b>：
/// ① 全仓不许再长出第二张表（含 <c>StarMark.UI</c>——测试工程不引用它，#184：那一侧既出不了行为测，
/// 也<b>曾经连"有几处"都数不到</b>，RY 那句"全称只有一颗"就是这么错的）；
/// ② 已改道的宿主必须<b>真的</b>从判据取名（线断了我得知道）。
/// </para>
/// <para>
/// 按 #195：全部读<b>抹掉注释后的代码</b>（<see cref="FormatScanner.Scan"/>）。这批最想在注释里写
/// "旧写法是 <c>DayOfWeek.Monday => &quot;周一&quot;</c>"，读原文就会把自己的解释弄红。
/// </para>
/// <para>
/// 按 #196：所有"X 必须调用判据"的接线断言都钉在<b>方法体／字段初始化区</b>里，不用整文件 <c>Contains</c>——
/// 整文件数会被同文件另一处合法用法顶住（台架 SE5 就是这么绿的）。
/// </para>
/// </summary>
public sealed class WeekdayNameGateTests
{
    private const string JudgeFile = "src/StarMark.Abstractions/DateTimeText.cs";
    private const string MonthGridFile = "src/StarMark.Core/Widgets/MonthGrid.cs";
    private const string AlarmPolicyFile = "src/StarMark.Core/Widgets/AlarmPolicy.cs";
    private const string LocalItemStateFile = "src/StarMark.Abstractions/LocalItemState.cs";
    /// <summary>批次 VO 之后"逐天那一排"住在两个入口共用的动作表里，不再住在宿主菜单那半文件。</summary>
    private const string AlarmTableFile = "src/StarMark.UI/Views/AlarmMenu.cs";
    private const string GlanceUiFile = "src/StarMark.UI/Views/GlanceWidget.xaml.cs";

    /// <summary>
    /// 一张"星期名表"的形状：<b>整枚</b>两字短名／三字全称／七字词根串。
    /// 只认完整字面量，所以 <c>"工作日（周一到周五）</c>、<c>"周末"</c>、<c>"周"</c>（单位）都不算违规——
    /// 那是给人看的短语，不是表。
    /// </summary>
    private static readonly Regex WeekdayTableLiteral = new(
        "\"(?:周[日一二三四五六]|星期[日一二三四五六]|日一二三四五六)\"",
        RegexOptions.Compiled);

    // ────────── ① 判据之外不许有第二张表 ──────────

    [Fact]
    public void NoWeekdayTableLandsOutsideTheJudge()
    {
        var offenders = FormatScanner.SourcesUnder("src", "DateTimeText.cs")
            .SelectMany(f => WeekdayTableLiteral.Matches(f.Code).Cast<Match>()
                .Select(m => $"{f.Path}: {m.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "又有人自己写了一张星期表（该走 DateTimeText.Weekday／WeekdayShort／WeekdayStem）：\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// 正面凭据（#161）：①是禁项，"零命中"本身不说明扫描器看得见中文——先证明<b>这段代码里它认得出违规</b>，
    /// 并且注释里出现同样的写法不算违规（否则没人敢在注释里解释旧写法，规则就会被当噪声放宽）。
    /// </summary>
    [Fact]
    public void TheScannerSeesTheBadShape_andIgnoresComments()
    {
        const string sample = "// 旧写法是 DayOfWeek.Monday => \"周一\"，还有 \"日一二三四五六\" 这种切片\n"
            + "var bad = DayOfWeek.Sunday => \"周日\";\n";
        var code = FormatScanner.Scan(sample).Code;

        // 读原文会被注释里那两处一起弄红 ⇒ 没人敢解释旧写法，规则迟早被当噪声放宽（#190/#195）。
        Assert.Equal(3, WeekdayTableLiteral.Matches(sample).Count);
        Assert.Single(WeekdayTableLiteral.Matches(code));
        Assert.Equal("\"周日\"", WeekdayTableLiteral.Match(code).Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// 三枚"看着像表、其实不是表"的合法写法：短语、单位、"周末"。
    /// 禁项按<b>整枚字面量</b>匹配，所以它们都不许被误伤——否则下一次加一句人话就得先放宽规则。
    /// </summary>
    [Theory]
    [InlineData("\"工作日（周一到周五）\"")]
    [InlineData("\"周末（周六周日）\"")]
    [InlineData("\"周\"")]
    [InlineData("\"周末\"")]
    public void ProseThatMentionsWeekdaysIsNotATable(string literal)
        => Assert.DoesNotMatch(WeekdayTableLiteral, literal);

    // ────────── ② 判据不许退成空壳 ──────────

    [Fact]
    public void TheJudgeHoldsSevenDistinctStems()
    {
        var body = MethodBody(ReadRepoFile(JudgeFile), "public static string WeekdayStem(");
        var stems = Regex.Matches(body, @"""(.)""").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(7, stems.Distinct().Count());   // 七格互不相同，且都在
    }

    /// <summary>全称与短名都必须<b>由词根拼</b>——否则①那条禁项一红，修法就变成"再补一张表"。</summary>
    [Fact]
    public void BothSpellingsAreBuiltFromTheStem()
    {
        var judge = CodeOf(JudgeFile);
        Assert.Equal(1, Count(judge, "\"星期\" + WeekdayStem(day)"));
        Assert.Equal(1, Count(judge, "\"周\" + WeekdayStem(day)"));
    }

    // ────────── ③ 四个真读者都得真的接在判据上 ──────────

    [Fact]
    public void MonthGridTakesItsHeaderWordsFromTheJudge()
    {
        // 字段初始化区，不是方法体：钉"从 WeekHeaders 到 ];"这一段"，整文件 Contains 会被别的调用顶住（#196）。
        var region = Between(CodeOf(MonthGridFile), "WeekHeaders", "];");
        Assert.True(Count(region, "DateTimeText.WeekdayStem(") >= MonthGridColumns,
            $"月历表头里只有 {Count(region, "DateTimeText.WeekdayStem(")} 处取自判据（应 ≥{MonthGridColumns}）——它又开始自己写字了");
        Assert.Empty(WeekdayTableLiteral.Matches(region));
    }

    /// <summary>钉"七列"这个数不写死：读 <c>MonthGrid.Columns</c> 本身。</summary>
    private static int MonthGridColumns
        => int.Parse(Regex.Match(CodeOf(MonthGridFile), @"const int Columns = (\d+)").Groups[1].Value);

    [Fact]
    public void AlarmPolicyTakesItsDayWordsFromTheJudge()
    {
        var body = MethodBody(ReadRepoFile(AlarmPolicyFile), "public static string DaysLabel(");
        Assert.Contains("DateTimeText.WeekdayShort(", body, StringComparison.Ordinal);
        Assert.Empty(WeekdayTableLiteral.Matches(body));
        // 它仍拥有"周一在前"这个次序（那是习惯，不是第二真值）——少了这句就说明次序被并掉了。
        Assert.Contains("DayOfWeek.Monday", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TodoDueTextTakesItsDayWordsFromTheJudge()
    {
        var body = MethodBody(ReadRepoFile(LocalItemStateFile), "public static string DescribeDue(");
        Assert.Contains("DateTimeText.WeekdayShort(", body, StringComparison.Ordinal);
        Assert.Empty(WeekdayTableLiteral.Matches(body));
    }

    // ────────── ④ UI 那一侧只能靠形状（#184）──────────

    /// <summary>
    /// UI 工程<b>零星期名表</b>：这一侧的行为测不出来，所以形状禁项要更严——整个工程里一枚都不许出现。
    /// </summary>
    [Fact]
    public void UiProjectHoldsNoWeekdayTableAtAll()
    {
        var offenders = FormatScanner.SourcesUnder("src/StarMark.UI")
            .SelectMany(f => WeekdayTableLiteral.Matches(f.Code).Cast<Match>()
                .Select(m => $"{f.Path}: {m.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "UI 层又自己写了一遍星期名（测试工程够不着那一侧，只能靠这条拦）：\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// 闹钟那一排的逐天标签、速览那一行的全称：两处都必须现取判据。
    /// <para>批次 VO 之后逐天那一排搬进了共用的动作表（表面上那一行与右键菜单同一份），
    /// 锚点跟着搬到 <c>AlarmMenu.RowItems</c>——<b>不是改判据，是改落点</b>：那颗点还是只能问 DateTimeText。</para>
    /// </summary>
    [Fact]
    public void TheTwoUiScreensForwardToTheJudge()
    {
        var alarmRow = MethodBody(ReadRepoFile(AlarmTableFile), "public static IEnumerable<MenuFlyoutItemBase> RowItems(");
        Assert.Contains("DateTimeText.WeekdayShort(day)", alarmRow, StringComparison.Ordinal);

        var refresh = MethodBody(ReadRepoFile(GlanceUiFile), "private void RefreshDate()");
        Assert.Contains("DateTimeText.Weekday(today.DayOfWeek)", refresh, StringComparison.Ordinal);
    }

    /// <summary>
    /// 反空转（#161）：上面那些"接线"锚点今天必须真的存在，且数量不许掉——
    /// 否则禁项会因为"根本没人接判据"而冒充绿灯。
    /// </summary>
    [Fact]
    public void TheJudgeHasAtLeastFourRealReaders()
    {
        var readers = FormatScanner.SourcesUnder("src", "DateTimeText.cs")
            .Where(f => Count(f.Code, "DateTimeText.WeekdayStem(") + Count(f.Code, "DateTimeText.WeekdayShort(")
                      + Count(f.Code, "DateTimeText.Weekday(") > 0)
            .Select(f => f.Path)
            .ToList();

        Assert.True(readers.Count >= 4,
            $"只有 {readers.Count} 个文件从判据取星期名（今天是 4）——③④那几条快成空闸门了：\n"
            + string.Join("\n", readers));
    }

    private static string CodeOf(string relativePath) => FormatScanner.Scan(ReadRepoFile(relativePath)).Code;
}
