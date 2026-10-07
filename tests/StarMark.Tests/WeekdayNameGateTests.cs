#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
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

    /// <summary>
    /// 批次 WI-37-C 起，<b>那张表（<c>I18n/UiStrings.cs</c>）就是星期名的主人</b>，所以扫描面要把它一起跳过——
    /// 它与 <c>DateTimeText.cs</c> 同一种豁免理由：那一行一行本身就是字，把它算进"别人自己写的表"
    /// 等于要求主人不带字（而"表里不许有第二套"那一族由 <c>I18nWiringGateTests</c> 那几颗管）。
    /// ⛔ 因此这一格仍然成立：除这两处之外，任何文件里再出现一枚 <c>"周一"</c> 都要红。
    /// </summary>
    [Fact]
    public void NoWeekdayTableLandsOutsideTheJudge()
    {
        var offenders = FormatScanner.SourcesUnder("src", "DateTimeText.cs")
            .Where(f => !f.Path.EndsWith("I18n/UiStrings.cs", StringComparison.Ordinal))
            .SelectMany(f => WeekdayTableLiteral.Matches(f.Code).Cast<Match>()
                .Select(m => $"{f.Path}: {m.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "又有人自己写了一张星期表（该走 DateTimeText.Weekday／WeekdayShort／WeekdayStem，或它们的 *Message 版）：\n"
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

    // ────────── ② 主人（那张表）不许退成空壳，也不许两副面孔各说各的 ──────────

    /// <summary>
    /// 每一副面孔七格齐全、三列都有字、且<b>同一副面孔内部互不相同</b>（周日周日不分＝那一格在表里根本不存在）。
    /// <para>原来这一格数的是 <c>DateTimeText</c> 里那七枚字面量。字搬进表之后，"数得到七格"必须在表那一侧量——
    /// ⛔ 让判据退成"只会拼键名、里面一颗字都没有"而没人核对那 21 行齐不齐（键名对了但表里少一行，
    /// 屏幕上就是 ⟨time.weekday.short.3⟩ 那种形状，而全绿）。</para>
    /// </summary>
    [Theory]
    [InlineData("full")]
    [InlineData("short")]
    [InlineData("stem")]
    public void EachSpellingFamilyHoldsSevenDistinctNames(string which)
    {
        var rows = Enumerable.Range(0, 7)
            .Select(i => UiStrings.All.FirstOrDefault(e => e.Key == $"time.weekday.{which}.{i}"))
            .ToList();
        Assert.All(rows, r => Assert.NotNull(r));
        Assert.Equal(7, rows.Select(r => r!.Zh).Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, r => Assert.False(
            string.IsNullOrWhiteSpace(r!.En) || string.IsNullOrWhiteSpace(r.Ja),
            $"{which} 那一族有一行的英文或日文列是空的（键在、字没填＝屏幕上那一格是 ⟨键⟩ 或空白）"));
    }

    /// <summary>
    /// 中文那两副面孔仍是"前缀＋词根"：<c>星期日＝星期＋日</c>、<c>周日＝周＋日</c>。
    /// <para>这条从前钉在代码里（<c>"星期" + WeekdayStem(day)</c> 出现一次），现在钉<b>表的数据</b>：
    /// 代码里再拼一次就是第二份真值（切语言时一半跟着翻、一半停在旧语言＝坑表 #344），
    /// 而"表里那两列会不会哪天自己漂开"这件事⛔ 只能这样当场对账。</para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void EachChineseSpellingIsItsStemWithItsPrefix(int index)
    {
        var stem = Zh($"time.weekday.stem.{index}");
        Assert.Equal("星期" + stem, Zh($"time.weekday.full.{index}"));
        Assert.Equal("周" + stem, Zh($"time.weekday.short.{index}"));
    }

    private static string Zh(string key)
        => UiStrings.All.First(e => string.Equals(e.Key, key, StringComparison.Ordinal)).Zh;

    /// <summary>
    /// 判据那一侧⛔ 再留一张中文表、也⛔ 再自己现拼一次键名（那两处各是一份可以独立漂移的真值，上面那颗数据格就是防它）。
    /// <para>它现在只许拥有一件事：<b>把"第几天"换成"哪一颗键"</b>，而且三副面孔共用同一条路（数一次 <c>Slot(day)</c> 的定义、
    /// 三处使用）。原来这一格钉的是"两副面孔都由词根拼"——那是中文唯一形状时代的规矩；字进了表之后，
    /// 那条规矩的正确后继就是上面 <see cref="EachChineseSpellingIsItsStemWithItsPrefix"/> 那颗数据对账。</para>
    /// <para>⚠ 键名不许写成 <c>$"time.weekday.full.{n}"</c> 现拼（<see cref="EachSpellingFamilyHoldsSevenDistinctNames"/>
    /// 与 <see cref="TheJudgeKeepsNoWordsAndBuildsAllThreeKeysOneWay"/> 是同一件事的两面）：拼出来的键在源码里不成一整串，
    /// 那颗"表里没有一行没人问"的闸门就看不见它，21 行星期名于是变成删不掉的死行。</para>
    /// </summary>
    [Fact]
    public void TheJudgeKeepsNoWordsAndBuildsAllThreeKeysOneWay()
    {
        var judge = CodeOf(JudgeFile);
        Assert.DoesNotContain("\"星期\"", judge, StringComparison.Ordinal);
        Assert.DoesNotContain("\"周\" +", judge, StringComparison.Ordinal);
        Assert.Empty(WeekdayTableLiteral.Matches(judge));
        Assert.DoesNotContain("$\"time.weekday", judge, StringComparison.Ordinal);   // 现拼＝对死键闸门隐身
        // full／short／stem 三副面孔共用同一条取键路：三份实现＝三处可以各自漂的地方。
        Assert.Equal(3, Count(judge, "Keys[Slot(day)]"));
        Assert.Equal(1, Count(judge, "private static int Slot("));
        // 每条路各有一张自己的键表（三张、每张七行）：只有一张表时另一族就会读到邻族的字。
        Assert.Equal(3, Count(judge, "private static readonly string[] "));
        Assert.Equal(21, Regex.Matches(judge, "\"time\\.weekday\\.(?:full|short|stem)\\.[0-6]\"").Count);
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

    /// <summary>
    /// 星期档那一句的名字来自判据，而<b>次序（周一在前）才是 AlarmPolicy 拥有的事</b>。
    /// <para>批次 WI-37-C 起这一族交的是<b>还没取词的</b> <c>LocMessage</c>（<c>DaysMessage</c>），
    /// <c>DaysLabel</c> 只剩一句 <c>.Render()</c>——所以这一格改钉 <c>DaysMessage</c> 那颗方法体：
    /// ⛔ 继续钉 <c>DaysLabel</c> 的方法体会变成"名字搬进了 <c>DaysMessage</c>、这里数不到"的假红（#196 同族：接线断言要钉在真正干活的那颗体上）。</para>
    /// </summary>
    [Fact]
    public void AlarmPolicyTakesItsDayWordsFromTheJudge()
    {
        var body = MethodBody(ReadRepoFile(AlarmPolicyFile), "public static LocMessage DaysMessage(");
        Assert.Contains("DateTimeText.WeekdayShortMessage(", body, StringComparison.Ordinal);
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
