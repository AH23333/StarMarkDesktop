#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StarMark.Abstractions.I18n;
using StarMark.Core.Calc;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 计算器「常用」页的算法测试。这些算式的价值全在"给出的数正好是用户心里那个问号的答案"，
/// 所以断言钉的是那颗键 + 那几颗实参：数字格式（千分位、两位小数、正号、取整位数）全在实参里，
/// 而屏幕上那句话的措辞住在表里。
/// <para>⚠ 与 ✅WI-53 那颗 <c>WeatherTests</c> 的 Theory 同一颗改口（批次 WI-55b／P-182b）：早先这里钉的是
/// 渲染出来的中文字符串，于是只改措辞也红，而红得说不出是数字错了还是字改了（台架规则 #130）。</para>
/// </summary>
public sealed class QuickMathTests
{
    /// <summary>行标签那颗键（表里 <c>calc.quick.line.*</c> 那一族；本档只点名用得到的）。</summary>
    internal static class K
    {
        public const string Final = "calc.quick.line.final";
        public const string Saved = "calc.quick.line.saved";
        public const string Ratio = "calc.quick.line.ratio";
        public const string AfterDiscount = "calc.quick.line.afterDiscount";
        public const string SpendSave = "calc.quick.line.spendSave";
        public const string Delta = "calc.quick.line.delta";
        public const string Change = "calc.quick.line.change";
        public const string BackEven = "calc.quick.line.backEven";
        public const string BackTo = "calc.quick.line.backTo";
        public const string UnitPrice = "calc.quick.line.unitPrice";
        public const string Verdict = "calc.quick.line.verdict";
        public const string DaysBetween = "calc.quick.line.daysBetween";
        public const string Weeks = "calc.quick.line.weeks";
        public const string Workdays = "calc.quick.line.workdays";
        public const string Hint = "calc.quick.line.hint";
        public const string Explain = "calc.quick.line.explain";
        public const string Caution = "calc.quick.line.caution";
    }

    private static readonly DateTime Today = new DateTime(2026, 5, 5);

    // ────────── 数字显示 ──────────

    [Fact]
    public void Money_IsTwoDecimalsWithGroupingAndAwayFromZero()
    {
        // InvariantCulture 写死：千分位必须是逗号、小数点必须是点，否则复制回算式栏就断在半路
        Assert.Equal("1,234.57", QuickMath.Money(1234.565m));
        Assert.Equal("0.01", QuickMath.Money(0.005m));
        Assert.Equal("-100.00", QuickMath.Money(-100m));
        Assert.Equal("0.00", QuickMath.Money(0m));
    }

    [Fact]
    public void Number_TrimsTrailingZerosButKeepsTwoDecimals()
    {
        Assert.Equal("25", QuickMath.Number(25.00m));
        Assert.Equal("25.5", QuickMath.Number(25.5m));
        Assert.Equal("1234.57", QuickMath.Number(1234.567m));
        Assert.Equal("0", QuickMath.Number(0m));
        Assert.Equal("-20", QuickMath.Number(-20m));
    }

    // ────────── 打折 / 满减 ──────────

    [Fact]
    public void Bargain_DiscountUpToTen_IsReadAsChineseZhe()
    {
        var r = QuickMath.Bargain(100m, 7.5m, 0m, 0m);
        // ≤10 那一档要把折数一起念出来：两颗实参的顺序＝"钱在前、折数在后"
        QuickAssert.Value(r, K.AfterDiscount, "calc.quick.value.discountTenths", "75.00", "7.5");
        QuickAssert.Value(r, K.Final, "calc.num", "75.00");
        QuickAssert.Value(r, K.Saved, "calc.num", "25.00");
        QuickAssert.Value(r, K.Ratio, "calc.num.percent", "75");
        // 没有满减就不该出现满减那一行（多一行"减 0 次"是噪声）
        Assert.DoesNotContain(r.Lines, l => l.Label.Key == K.SpendSave);
    }

    [Fact]
    public void Bargain_DiscountAboveTen_IsReadAsPercent()
    {
        var r = QuickMath.Bargain(100m, 85m, 0m, 0m);
        // 按百分比收的那一档⛔ 再套一层"几折"：折数那颗槽根本没有值可填
        QuickAssert.Value(r, K.AfterDiscount, "calc.num", "85.00");
        QuickAssert.Value(r, K.Final, "calc.num", "85.00");
        QuickAssert.Value(r, K.Saved, "calc.num", "15.00");
    }

    [Fact]
    public void Bargain_TenIsFullPrice_SlashHundredIsNotASecondMeaning()
    {
        // 7.5–10 与 75–100 之间不能有两套语义打架：10 是"不打折"，100 也是"不打折"
        QuickAssert.Value(QuickMath.Bargain(100m, 10m, 0m, 0m), K.Final, "calc.num", "100.00");
        QuickAssert.Value(QuickMath.Bargain(100m, 100m, 0m, 0m), K.Final, "calc.num", "100.00");
    }

    [Fact]
    public void Bargain_FullReduction_CountsOnDiscountedPriceAndFloors()
    {
        // 800 打 9 折 = 720；每满 300 减 50 ⇒ 2 次共 100 ⇒ 到手 620（按比例减会得到 120，是错的）
        var r = QuickMath.Bargain(800m, 9m, 300m, 50m);
        QuickAssert.Value(r, K.AfterDiscount, "calc.quick.value.discountTenths", "720.00", "9");
        QuickAssert.Value(r, K.Final, "calc.num", "620.00");
        QuickAssert.Value(r, K.Saved, "calc.num", "180.00");
        QuickAssert.Value(r, K.Ratio, "calc.num.percent", "77.5");
        QuickAssert.Value(r, K.SpendSave, "calc.quick.value.times", "2", "100.00");
    }

    [Fact]
    public void Bargain_OneStepBelowThreshold_GetsNoReduction()
    {
        var r = QuickMath.Bargain(290m, 10m, 300m, 50m);
        QuickAssert.Value(r, K.Final, "calc.num", "290.00");
        Assert.DoesNotContain(r.Lines, l => l.Label.Key == K.SpendSave);
    }

    [Fact]
    public void Bargain_ReductionNeverDrivesFinalToZero()
    {
        // 数学上到手价恒 > 0（每次减的 < 门槛，次数向下取整），这里用极端组合压一遍，
        // 免得哪天改成"封顶减到 0"却没人发现那一行还叫"到手"。
        foreach (var (original, discount, everyFull, reduceEach) in new[]
        {
            (100m, 1m, 1m, 0.99m), (100m, 9m, 11m, 10.99m), (1m, 1m, 0.01m, 0.009m),
        })
        {
            var r = QuickMath.Bargain(original, discount, everyFull, reduceEach);
            Assert.True(r.Ok, $"{original}/{discount}/{everyFull}/{reduceEach} ⇒ {r.Error?.Key}");
            Assert.DoesNotContain("0.00", QuickAssert.Args(r, K.Final));
        }
    }

    [Theory]
    [InlineData(0, 7.5, 0, 0, "calc.quick.rej.original", "")]
    [InlineData(-1, 7.5, 0, 0, "calc.quick.rej.original", "")]
    [InlineData(100, 0, 0, 0, "calc.quick.rej.discount", "")]
    [InlineData(100, -5, 0, 0, "calc.quick.rej.discount", "")]
    [InlineData(100, 700, 0, 0, "calc.quick.rej.discount", "")]
    [InlineData(100, 7.5, -300, 50, "calc.quick.rej.negative", "")]
    [InlineData(100, 7.5, 300, -50, "calc.quick.rej.negative", "")]
    // 「每满／减多少」那两种配错⛔ 进这张单列表：那颗句同时点两个字段名（名单见下面那颗单独的 Fact）
    [InlineData(100, 7.5, 50, 50, "calc.quick.rej.reduceTooBig", QuickCalculator.Field.EveryFull)]
    [InlineData(100, 7.5, 50, 80, "calc.quick.rej.reduceTooBig", QuickCalculator.Field.EveryFull)]
    public void Bargain_RejectsBadCombinations_NamingTheOffendingRow(
        double original, double discount, double everyFull, double reduceEach, string errorKey, string namedKey)
        => QuickAssert.Reject(
            QuickMath.Bargain((decimal)original, (decimal)discount, (decimal)everyFull, (decimal)reduceEach),
            errorKey, namedKey.Length == 0 ? Array.Empty<string>() : new[] { namedKey });

    [Fact]
    public void Bargain_PairReject_NamesTheMissingPartnerSecond()
    {
        // 「填了 A 就要填 B」那一句是同一颗键的两个方向：⛔ 抄屏幕上那两行标签，实参交的是字段那颗键。
        // 两颗字段名都要钉（只点第一颗＝把"每满没填"与"减多少没填"读成同一句话，那是两个不同的错）。
        QuickAssert.Reject(QuickMath.Bargain(100m, 7.5m, 300m, 0m), "calc.quick.rej.pair",
            QuickCalculator.Field.EveryFull, QuickCalculator.Field.Reduce);
        QuickAssert.Reject(QuickMath.Bargain(100m, 7.5m, 0m, 50m), "calc.quick.rej.pair",
            QuickCalculator.Field.Reduce, QuickCalculator.Field.EveryFull);
    }

    // ────────── 涨跌 / 回本 ──────────

    [Fact]
    public void Change_LossNeedsABiggerGainToBreakEven()
    {
        var r = QuickMath.Change(100m, 80m);
        QuickAssert.Value(r, K.Delta, "calc.num", "-20.00");
        QuickAssert.Value(r, K.Change, "calc.num.percent", "-20");
        // 这一行就是整个功能的理由：跌 20% 要涨 25% 才回本
        QuickAssert.Value(r, K.BackTo, "calc.quick.value.needUp", "25");
    }

    [Fact]
    public void Change_HalfLossNeedsDoubleGain()
        => QuickAssert.Value(QuickMath.Change(100m, 50m), K.BackTo, "calc.quick.value.needUp", "100");

    [Fact]
    public void Change_GainNeedsSmallerDrop_NoDoubleNegatives()
    {
        var r = QuickMath.Change(100m, 120m);
        QuickAssert.Value(r, K.Delta, "calc.num", "+20.00");
        QuickAssert.Value(r, K.Change, "calc.num.percent", "+20");
        QuickAssert.Value(r, K.BackTo, "calc.quick.value.needDown", "16.67");
        // "需跌 -16.67%" 是否定之否定，读的人要再翻一次：方向交给那颗键，实参只给绝对值
        Assert.DoesNotContain("-", QuickAssert.Args(r, K.BackTo)[0]);
    }

    [Fact]
    public void Change_FlatGetsNoPlusSign()
    {
        var r = QuickMath.Change(50m, 50m);
        QuickAssert.Value(r, K.Delta, "calc.num", "0.00");
        QuickAssert.Value(r, K.Change, "calc.num.percent", "0");
        QuickAssert.Value(r, K.BackTo, "calc.quick.value.already");
    }

    [Fact]
    public void Change_ZeroedValue_SaysItCannotComeBack()
    {
        var r = QuickMath.Change(100m, 0m);
        QuickAssert.Value(r, K.Change, "calc.num.percent", "-100");
        QuickAssert.Value(r, K.BackEven, "calc.quick.value.zeroed");
        // 值已经归零：那一句"回到 X"没有意义，⛔ 还挂着那一行
        Assert.DoesNotContain(r.Lines, l => l.Label.Key == K.BackTo);
    }

    [Theory]
    [InlineData(0, 5, "calc.quick.rej.startBase")]
    [InlineData(-5, 10, "calc.quick.rej.startBase")]
    [InlineData(100, -1, "calc.quick.rej.currentNegative")]
    public void Change_RejectsValuesWithoutPercentMeaning(double from, double to, string errorKey)
        => QuickAssert.Reject(QuickMath.Change((decimal)from, (decimal)to), errorKey);

    // ────────── 单价比较 ──────────

    [Fact]
    public void UnitPriceCompare_PicksTheCheaperAndStatesTheGap()
    {
        var r = QuickMath.UnitPriceCompare(29.9m, 500m, 39.9m, 800m);
        QuickAssert.ValueArg(r, K.UnitPrice, "A", "calc.num", "0.0598");
        QuickAssert.ValueArg(r, K.UnitPrice, "B", "calc.num", "0.0499");
        QuickAssert.Value(r, K.Verdict, "calc.quick.value.cheaper", "B", "A", "19.84");
    }

    [Fact]
    public void UnitPriceCompare_FourDigitRuleSeparatesPricesOneDigitWouldTie()
    {
        // 12.345 与 12.344：按 2 位（甚至 3 位）显示会判成"一样"，比较必须走到第 4 位
        var r = QuickMath.UnitPriceCompare(12.345m, 1m, 12.344m, 1m);
        Assert.NotEqual(QuickAssert.Args(r, K.UnitPrice, "A")[0], QuickAssert.Args(r, K.UnitPrice, "B")[0]);
        // 差 0.0010 / 较小单价 12.3440 × 100 = 0.0081% ⇒ 两位读数进成 0.01：这一格钉的是"第 4 位真的参与了比较"，⛔ 钉成看起来像样的数
        QuickAssert.Value(r, K.Verdict, "calc.quick.value.cheaper", "B", "A", "0.01");
    }

    [Fact]
    public void UnitPriceCompare_EqualUnits_SaySoInsteadOfPickingASide()
    {
        var r = QuickMath.UnitPriceCompare(10m, 2m, 15m, 3m);
        QuickAssert.ValueArg(r, K.UnitPrice, "A", "calc.num", "5.0000");
        QuickAssert.ValueArg(r, K.UnitPrice, "B", "calc.num", "5.0000");
        QuickAssert.Value(r, K.Verdict, "calc.quick.value.sameUnit");
    }

    [Theory]
    [InlineData(0, 500, 10, 500, "calc.quick.rej.prices")]
    [InlineData(10, 500, 0, 500, "calc.quick.rej.prices")]
    [InlineData(10, 0, 10, 500, "calc.quick.rej.quantities")]
    [InlineData(10, 500, 10, 0, "calc.quick.rej.quantities")]
    public void UnitPriceCompare_RejectsZeroPriceOrQuantity(
        double priceA, double qtyA, double priceB, double qtyB, string errorKey)
        => QuickAssert.Reject(QuickMath.UnitPriceCompare(
            (decimal)priceA, (decimal)qtyA, (decimal)priceB, (decimal)qtyB), errorKey);

    // ────────── 间隔天数 ──────────

    [Fact]
    public void DateSpan_TenDaysIsOneWeekThreeDays_SevenWorkdays()
    {
        // 2026-10-01 是星期四：10/1–10/11 共 11 个自然日、7 个工作日
        var r = QuickMath.DateSpan(new DateTime(2026, 10, 1), new DateTime(2026, 10, 11));
        // "N 天"这颗与倒计时／RSS 同一行（time.unit.days），⛔ 为速算再开一颗同义行
        QuickAssert.Value(r, K.DaysBetween, "time.unit.days", "10");
        QuickAssert.Value(r, K.Weeks, "calc.quick.value.weeksDays", "1", "3");
        QuickAssert.Value(r, K.Workdays, "calc.num", "7");
        QuickAssert.Value(r, K.Explain, "calc.quick.value.inclusiveEnd");
    }

    [Fact]
    public void DateSpan_BackwardCarriesTheMinusAndSaysWhy()
    {
        var r = QuickMath.DateSpan(new DateTime(2026, 10, 11), new DateTime(2026, 10, 1));
        QuickAssert.Value(r, K.DaysBetween, "time.unit.days", "-10");
        QuickAssert.Value(r, K.Explain, "calc.quick.value.endBeforeStart");
        QuickAssert.Value(r, K.Workdays, "calc.num", "7");   // 工作日数与方向无关
    }

    [Fact]
    public void DateSpan_SameDay_SaysSo()
    {
        var r = QuickMath.DateSpan(new DateTime(2026, 10, 1), new DateTime(2026, 10, 1));
        QuickAssert.Value(r, K.DaysBetween, "time.unit.days", "0");
        QuickAssert.Value(r, K.Weeks, "calc.quick.value.weeksDays", "0", "0");
        QuickAssert.Value(r, K.Hint, "calc.quick.value.sameDay");
        QuickAssert.Value(r, K.Workdays, "calc.num", "1");   // 10/1 是星期四
    }

    [Fact]
    public void DateSpan_TimesAreIgnored()
    {
        var r = QuickMath.DateSpan(new DateTime(2026, 10, 1, 23, 59, 0), new DateTime(2026, 10, 2, 0, 1, 0));
        QuickAssert.Value(r, K.DaysBetween, "time.unit.days", "1");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]   // 反方向也走一遍（"还有 / 已过"两种读法都要带说明）
    public void DateSpan_AlwaysCarriesTheHolidayCaveat(int which)
    {
        var a = new DateTime(2026, 10, 1);
        var b = new DateTime(2026, 10, 11);
        var r = which == 1 ? QuickMath.DateSpan(a, b) : QuickMath.DateSpan(b, a);
        QuickAssert.Value(r, K.Caution, "calc.quick.value.workdayNote");
    }

    [Theory]
    [InlineData(2026, 10, 3, 2026, 10, 4, 0)]    // 周六 → 周日
    [InlineData(2026, 10, 5, 2026, 10, 9, 5)]    // 周一 → 周五
    [InlineData(2026, 10, 3, 2026, 10, 3, 0)]    // 单个周六
    [InlineData(2026, 10, 5, 2026, 10, 5, 1)]    // 单个周一
    [InlineData(2026, 10, 1, 2026, 10, 11, 7)]
    public void CountWorkdays_CoversWeekendOnly_WeekOnly_And_SingleDays(
        int y1, int m1, int d1, int y2, int m2, int d2, int expected)
    {
        var a = new DateTime(y1, m1, d1);
        var b = new DateTime(y2, m2, d2);
        Assert.Equal(expected, QuickMath.CountWorkdays(a, b));
        Assert.Equal(expected, QuickMath.CountWorkdays(b, a));   // 与两端先后无关
    }

    // ────────── 日期解析 ──────────

    [Theory]
    [InlineData("2026-12-31")]
    [InlineData("2026/12/31")]
    [InlineData("2026.12.31")]
    [InlineData("2026年12月31日")]
    [InlineData(" 2026-12-31 ")]
    [InlineData("2026-12-31 08:30")]
    public void ParseDate_CommonFormats_AllLandOnTheSameDay(string text)
        => Assert.Equal(new DateTime(2026, 12, 31), QuickMath.ParseDate(text, Today));

    [Fact]
    public void ParseDate_MonthDayOnly_TakesYearFromToday()
    {
        Assert.Equal(new DateTime(2026, 12, 31), QuickMath.ParseDate("12-31", Today));
        Assert.Equal(new DateTime(2026, 1, 5), QuickMath.ParseDate("1/5", Today));
        // 补年份这件事依赖 today 的年份，不能悄悄写成今年之后的某年
        Assert.Equal(new DateTime(2031, 3, 4), QuickMath.ParseDate("3-4", new DateTime(2031, 1, 1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("昨天下午")]
    [InlineData("2026-13-45")]
    [InlineData("13-45")]
    [InlineData("2026-2-30")]
    public void ParseDate_Garbage_ReturnsNull(string text)
        => Assert.Null(QuickMath.ParseDate(text, Today));

    [Fact]
    public void ParseDate_Null_IsNotAnException()
        => Assert.Null(QuickMath.ParseDate(null, Today));

    // ────────── 结果文本 ──────────

    [Fact]
    public void QuickResult_MultiLine_CarriesEveryRow_OrTheError()
    {
        var ok = QuickMath.Bargain(100m, 7.5m, 0m, 0m);
        Assert.Equal(4, ok.Lines.Count);
        Assert.Equal(4, ok.MultiLine.Split('\n').Length);
        foreach (var line in ok.Lines)
        {
            // 比的是同一个渲染出口的两次结果，⛔ 抄表里那一行：这一格钉的是那颗宿主自己拼装那一行
            Assert.Contains(line.Label.Render(), ok.MultiLine, StringComparison.Ordinal);
            Assert.Contains(line.Value.Render(), ok.MultiLine, StringComparison.Ordinal);
        }

        var bad = QuickMath.Change(0m, 1m);
        Assert.False(bad.Ok);
        Assert.Equal(bad.Error!.Render(), bad.MultiLine);
        Assert.DoesNotContain("\n", bad.MultiLine, StringComparison.Ordinal);
        // 那颗单行版的 Text 本批删了：全仓没有一处消费者，而两颗 join 各自漂下去迟早念出两种分隔
    }
}

/// <summary>
/// 「常用」页的模式目录测试：页面上能不能算，取决于目录与算法是否逐项对得上。
/// 这里刻意不测某个模式的具体数字（那在 <see cref="QuickMathTests"/>），只测"目录本身是否自洽"。
/// </summary>
public sealed class QuickCalculatorTests
{
    private static readonly DateTime Today = new DateTime(2026, 5, 5);

    private static readonly Dictionary<int, string[]> Samples = new()
    {
        [0] = new[] { "800", "9", "300", "50" },
        [1] = new[] { "100", "80" },
        [2] = new[] { "29.9", "500", "39.9", "800" },
        [3] = new[] { "2026-10-01", "2026-10-11" },
    };

    [Fact]
    public void Modes_AreFour_AndDistinguishable()
    {
        Assert.Equal(4, QuickCalculator.Modes.Count);
        Assert.Equal(QuickCalculator.Modes.Count, QuickCalculator.Modes.Select(m => m.TitleKey).Distinct().Count());
        foreach (var mode in QuickCalculator.Modes)
        {
            // 那颗"念法"交的必须是表里真有的行（⛔ 存成品字，也⛔ 挂一颗没登记的键＝屏幕上 ⟨key⟩）
            Assert.True(UiStrings.TryGet(mode.TitleKey, out _), $"标题那颗键没进表：{mode.TitleKey}");
            Assert.True(UiStrings.TryGet(mode.HintKey, out var hint), $"说明那颗键没进表：{mode.HintKey}");
            Assert.False(string.IsNullOrWhiteSpace(hint.Zh));
        }
    }

    [Fact]
    public void EveryMode_HasBetweenTwoAndMaxFields_WithDistinctLabels()
    {
        foreach (var mode in QuickCalculator.Modes)
        {
            Assert.InRange(mode.Fields.Count, 2, QuickCalculator.MaxFields);
            Assert.Equal(mode.Fields.Count, mode.Fields.Select(f => f.LabelKey).Distinct().Count());
            Assert.All(mode.Fields, f => Assert.True(UiStrings.TryGet(f.LabelKey, out _), $"槽名没进表：{f.LabelKey}"));
            // 占位符两种形状恰好一种：走表的那颗是真话（"留空＝今天"），剩下的是不用翻的例子（199、2026-12-31）
            Assert.All(mode.Fields, f => Assert.True(f.HasOnePlaceholderShape, "占位符的两种形状⛔ 同时给或都不给"));
            Assert.All(mode.Fields, f => Assert.False(string.IsNullOrWhiteSpace(f.PlaceholderKey ?? f.Placeholder)));
            Assert.Contains(mode.Fields, f => f.Required);   // 全可空的模式永远只输出"没填"
        }
    }

    [Fact]
    public void FieldPlaceholders_MatchTheirKind()
    {
        // 占位文本是用户会照着抄的第一个例子：整个模式都按占位文本填一遍必须算得出来，
        // 否则就是"照提示填却报错"。可空槽一律留空（"可空"那两个字在标签那句里，⛔ 抄进例子）。
        var requiredSlots = 0;
        for (var i = 0; i < QuickCalculator.Modes.Count; i++)
        {
            var mode = QuickCalculator.Modes[i];
            var texts = mode.Fields.Select(f =>
            {
                if (!f.Required) return string.Empty;
                requiredSlots++;
                return f.Placeholder!;
            }).ToList();
            var r = QuickCalculator.Evaluate(i, texts, Today);
            Assert.True(r.Ok, $"{mode.TitleKey} 全按占位文本填应当算得出，实际：{r.Error?.Key}");
        }
        Assert.InRange(requiredSlots, 8, 20);   // 守门测试不许空转：真的扫到这么多必填槽
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryMode_RunsEndToEnd(int index)
    {
        var r = QuickCalculator.Evaluate(index, Samples[index], Today);
        Assert.True(r.Ok, $"模式 {index} 的样例输入失败了：{r.Error?.Key}");
        Assert.NotEmpty(r.Lines);
        Assert.All(r.Lines, l =>
        {
            Assert.True(UiStrings.TryGet(l.Label.Key, out _), $"这一行的标签没进表：{l.Label.Key}");
            Assert.True(UiStrings.TryGet(l.Value.Key, out _), $"这一行的读数没进表：{l.Value.Key}");
            Assert.False(string.IsNullOrWhiteSpace(l.Value.Render()));
        });
    }

    [Fact]
    public void SampleInputs_ReachTheIntendedAnswer()
    {
        QuickAssert.Value(QuickCalculator.Evaluate(0, Samples[0], Today), QuickMathTests.K.Final, "calc.num", "620.00");
        QuickAssert.Value(QuickCalculator.Evaluate(1, Samples[1], Today), QuickMathTests.K.BackTo,
            "calc.quick.value.needUp", "25");
        QuickAssert.Value(QuickCalculator.Evaluate(2, Samples[2], Today), QuickMathTests.K.Verdict,
            "calc.quick.value.cheaper", "B", "A", "19.84");
        QuickAssert.Value(QuickCalculator.Evaluate(3, Samples[3], Today), QuickMathTests.K.DaysBetween,
            "time.unit.days", "10");
    }

    [Fact]
    public void OptionalNumberSlots_BlankMeansNoneNotZeroShown()
    {
        // 满减两栏留空 = 不参加满减，结果里不能冒出"减 0 次"
        var r = QuickCalculator.Evaluate(0, new[] { "100", "7.5", "", null }, Today);
        Assert.True(r.Ok, r.Error?.Key);
        Assert.DoesNotContain(r.Lines, l => l.Label.Key == QuickMathTests.K.SpendSave);
        QuickAssert.Value(r, QuickMathTests.K.Final, "calc.num", "75.00");
    }

    [Fact]
    public void OptionalDateSlot_BlankMeansToday_RequiredOneStaysRequired()
    {
        // 结束日期是"目标日"，只有它有默认值（今天）才有意义；两头都空就退化成"今天到今天"
        var single = QuickCalculator.Evaluate(3, new[] { "", "12-31" }, Today);
        Assert.True(single.Ok, single.Error?.Key);
        QuickAssert.Value(single, QuickMathTests.K.DaysBetween, "time.unit.days", "240");

        QuickAssert.Reject(QuickCalculator.Evaluate(3, new[] { "2026-01-01", "" }, Today),
            "calc.quick.missingDate", QuickCalculator.Field.EndDate);
    }

    [Fact]
    public void MissingRequiredInput_NamesTheField()
    {
        QuickAssert.Reject(QuickCalculator.Evaluate(0, new[] { "", "7.5" }, Today),
            "calc.quick.missingNumber", QuickCalculator.Field.Original);
        QuickAssert.Reject(QuickCalculator.Evaluate(3, new[] { "2026-01-01" }, Today),
            "calc.quick.missingDate", QuickCalculator.Field.EndDate);
        // 第三个槽没给文本 = 与给空串同一处理（界面可能只放了两栏的长度）
        QuickAssert.Reject(QuickCalculator.Evaluate(2, new[] { "10", "2" }, Today),
            "calc.quick.missingNumber", QuickCalculator.Field.PriceB);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("7.5 折")]
    [InlineData("１２３")]
    public void NonNumericInput_QuotesTheFieldAndTheOffendingText(string junk)
    {
        var error = QuickAssert.Reject(QuickCalculator.Evaluate(1, new[] { junk, "80" }, Today),
            "calc.quick.badNumber", QuickCalculator.Field.Start);
        // 那句还要把用户打的那串原样带回来（只说"没认出数字"＝让人猜哪一格）
        Assert.Equal(junk, error.Args![1]?.ToString());
    }

    [Fact]
    public void UnparseableDate_SaysWhichFieldAndHowToWriteIt()
    {
        QuickAssert.Reject(QuickCalculator.Evaluate(3, new[] { "下周三", "2026-12-31" }, Today),
            "calc.quick.badDate", QuickCalculator.Field.StartDate);
        // "给出正确写法，别让用户猜格式"这句搬进表里之后，那颗例子必须由三列都带着：
        // 少一列就是那一门语言的用户拿到一句没有例子的说明（⛔ 只在中文列留例子）
        Assert.True(UiStrings.TryGet("calc.quick.badDate", out var row));
        Assert.Contains("2026-12-31", row.Zh, StringComparison.Ordinal);
        Assert.Contains("2026-12-31", row.En, StringComparison.Ordinal);
        Assert.Contains("2026-12-31", row.Ja, StringComparison.Ordinal);
    }

    [Fact]
    public void ThousandsSeparators_AreAcceptedInMoneySlots()
    {
        var r = QuickCalculator.Evaluate(1, new[] { "1,000", "800" }, Today);
        Assert.True(r.Ok, r.Error?.Key);
        QuickAssert.Value(r, QuickMathTests.K.Change, "calc.num.percent", "-20");
    }

    [Fact]
    public void UnknownModeIndex_IsRefusedNotCrashed()
    {
        foreach (var index in new[] { -1, QuickCalculator.Modes.Count, 99 })
        {
            var error = QuickAssert.Reject(QuickCalculator.Evaluate(index, Array.Empty<string?>(), Today),
                "calc.quick.noMode");
            Assert.Equal(index.ToString(CultureInfo.InvariantCulture), error.Args![0]?.ToString());
        }
    }
}

/// <summary>
/// 两个测试类共用的判据（⛔ 各写一份：同一件事两份实现会漂，✅WI-46c 那颗「组 N（M 张）」的教训）。
/// </summary>
internal static class QuickAssert
{
    /// <summary>那一行的读数（<c>labelArg</c> 用来分开同一颗标签的不同行，如"A 单价"与"B 单价"）。</summary>
    internal static LocMessage Line(QuickResult r, string labelKey, string? labelArg = null)
    {
        Assert.True(r.Ok, $"应当成功却失败：{r.Error?.Key}");
        var line = r.Lines.FirstOrDefault(l => l.Label.Key == labelKey
                                              && (labelArg is null || Equals(l.Label.Args?[0]?.ToString(), labelArg)));
        Assert.NotNull(line);
        return line!.Value;
    }

    /// <summary>那一行读数的实参（逐颗转成字符串）：本档钉的是数字，不是屏幕上那句话。</summary>
    internal static string[] Args(QuickResult r, string labelKey, string? labelArg = null)
        => (Line(r, labelKey, labelArg).Args ?? Array.Empty<object?>())
            .Select(a => a?.ToString() ?? string.Empty).ToArray();

    /// <summary>那一行＝那颗键 ＋ 这几颗实参（措辞改了这里⛔ 红，那颗键或那些数错了才红，台架规则 #130）。</summary>
    /// <para>⚠ 带标签实参的那一颗⛔ 做成同名重载：四参的调用会被解析到"多一个 labelArg"的那一版上（本批第一颗红就是它），所以它叫 ValueArg。</para>
    internal static void Value(QuickResult r, string labelKey, string valueKey, params string[] args)
        => ValueArg(r, labelKey, null, valueKey, args);

    internal static void ValueArg(QuickResult r, string labelKey, string? labelArg, string valueKey, params string[] args)
    {
        Assert.Equal(valueKey, Line(r, labelKey, labelArg).Key);
        Assert.Equal(args, Args(r, labelKey, labelArg));
    }

    /// <summary>失败那句：钉那颗键与它点名的那几颗字段键（⛔ 抄屏幕上的中文——字段改说法时该跟着表走）。</summary>
    internal static LocMessage Reject(QuickResult r, string errorKey, params string[] namedFields)
    {
        Assert.False(r.Ok, $"应当失败却成功：{r.Lines.Count} 行");
        Assert.Empty(r.Lines);
        var error = r.Error;
        Assert.NotNull(error);
        Assert.Equal(errorKey, error!.Key);
        Assert.Equal(namedFields, (error.Args ?? Array.Empty<object?>())
            .OfType<LocMessage>().Select(m => m.Key).ToArray());
        return error;
    }
}
