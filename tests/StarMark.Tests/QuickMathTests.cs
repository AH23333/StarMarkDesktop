#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;
using StarMark.Core.Calc;

namespace StarMark.Tests;

/// <summary>
/// 计算器「常用」页的算法测试。这些算式的价值全在"给出的数正好是用户心里那个问号的答案"，
/// 所以断言钉的是<b>显示出来的那串字符</b>，不是内部中间量——四舍五入、正号、单位后缀都在这里被检查。
/// </summary>
public sealed class QuickMathTests
{
    private static readonly DateTime Today = new DateTime(2026, 5, 5);

    private static string Line(QuickResult r, string label)
    {
        Assert.True(r.Ok, $"应当成功却失败：{r.Error}");
        var line = r.Lines.FirstOrDefault(l => l.Label == label);
        Assert.NotNull(line);
        return line!.Value;
    }

    private static string AssertFailed(QuickResult r)
    {
        Assert.False(r.Ok, $"应当失败却成功：{r.Text}");
        Assert.Empty(r.Lines);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
        return r.Error!;
    }

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
        Assert.Equal("75.00（7.5 折）", Line(r, "折后"));
        Assert.Equal("75.00", Line(r, "到手"));
        Assert.Equal("25.00", Line(r, "省"));
        Assert.Equal("75%", Line(r, "相当于"));
        // 没有满减就不该出现满减那一行（多一行"减 0 次"是噪声）
        Assert.DoesNotContain(r.Lines, l => l.Label == "满减");
    }

    [Fact]
    public void Bargain_DiscountAboveTen_IsReadAsPercent()
    {
        var r = QuickMath.Bargain(100m, 85m, 0m, 0m);
        Assert.Equal("85.00", Line(r, "折后"));
        Assert.Equal("85.00", Line(r, "到手"));
        Assert.Equal("15.00", Line(r, "省"));
        Assert.DoesNotContain("折", Line(r, "折后"));
    }

    [Fact]
    public void Bargain_TenIsFullPrice_SlashHundredIsNotASecondMeaning()
    {
        // 7.5–10 与 75–100 之间不能有两套语义打架：10 是"不打折"，100 也是"不打折"
        Assert.Equal("100.00", Line(QuickMath.Bargain(100m, 10m, 0m, 0m), "到手"));
        Assert.Equal("100.00", Line(QuickMath.Bargain(100m, 100m, 0m, 0m), "到手"));
    }

    [Fact]
    public void Bargain_FullReduction_CountsOnDiscountedPriceAndFloors()
    {
        // 800 打 9 折 = 720；每满 300 减 50 ⇒ 2 次共 100 ⇒ 到手 620（按比例减会得到 120，是错的）
        var r = QuickMath.Bargain(800m, 9m, 300m, 50m);
        Assert.Equal("720.00（9 折）", Line(r, "折后"));
        Assert.Equal("620.00", Line(r, "到手"));
        Assert.Equal("180.00", Line(r, "省"));
        Assert.Equal("77.5%", Line(r, "相当于"));
        Assert.Equal("减 2 次共 100.00", Line(r, "满减"));
    }

    [Fact]
    public void Bargain_OneStepBelowThreshold_GetsNoReduction()
    {
        var r = QuickMath.Bargain(290m, 10m, 300m, 50m);
        Assert.Equal("290.00", Line(r, "到手"));
        Assert.DoesNotContain(r.Lines, l => l.Label == "满减");
    }

    [Fact]
    public void Bargain_ReductionNeverDrivesFinalToZero()
    {
        // 数学上到手价恒 > 0（每次减的 < 门槛，次数向下取整），这里用极端组合压一遍，
        // 免得哪天改成"封顶减到 0"却没人发现文案还写着"到手"。
        foreach (var (original, discount, everyFull, reduceEach) in new[]
        {
            (100m, 1m, 1m, 0.99m), (100m, 9m, 11m, 10.99m), (1m, 1m, 0.01m, 0.009m),
        })
        {
            var r = QuickMath.Bargain(original, discount, everyFull, reduceEach);
            Assert.True(r.Ok, $"{original}/{discount}/{everyFull}/{reduceEach} ⇒ {r.Error}");
            Assert.DoesNotContain("0.00", Line(r, "到手"));
        }
    }

    [Theory]
    [InlineData(0, 7.5, 0, 0, "原价")]
    [InlineData(-1, 7.5, 0, 0, "原价")]
    [InlineData(100, 0, 0, 0, "折扣")]
    [InlineData(100, -5, 0, 0, "折扣")]
    [InlineData(100, 700, 0, 0, "折扣")]
    [InlineData(100, 7.5, -300, 50, "负数")]
    [InlineData(100, 7.5, 300, -50, "负数")]
    [InlineData(100, 7.5, 300, 0, "减多少")]
    [InlineData(100, 7.5, 0, 50, "每满")]
    [InlineData(100, 7.5, 50, 50, "门槛")]
    [InlineData(100, 7.5, 50, 80, "门槛")]
    public void Bargain_RejectsBadCombinations_WithTheOffendingName(
        double original, double discount, double everyFull, double reduceEach, string keyword)
    {
        var error = AssertFailed(QuickMath.Bargain(
            (decimal)original, (decimal)discount, (decimal)everyFull, (decimal)reduceEach));
        Assert.Contains(keyword, error);
    }

    // ────────── 涨跌 / 回本 ──────────

    [Fact]
    public void Change_LossNeedsABiggerGainToBreakEven()
    {
        var r = QuickMath.Change(100m, 80m);
        Assert.Equal("-20.00", Line(r, "差额"));
        Assert.Equal("-20%", Line(r, "涨跌"));
        // 这一行就是整个功能的理由：跌 20% 要涨 25% 才回本
        Assert.Equal("需涨 25%", Line(r, "回到 100"));
    }

    [Fact]
    public void Change_HalfLossNeedsDoubleGain()
    {
        Assert.Equal("需涨 100%", Line(QuickMath.Change(100m, 50m), "回到 100"));
    }

    [Fact]
    public void Change_GainNeedsSmallerDrop_NoDoubleNegatives()
    {
        var r = QuickMath.Change(100m, 120m);
        Assert.Equal("+20.00", Line(r, "差额"));
        Assert.Equal("+20%", Line(r, "涨跌"));
        var back = Line(r, "回到 100");
        Assert.Equal("需跌 16.67%", back);
        Assert.DoesNotContain("-", back);   // "需跌 -16.67%" 是否定之否定，读的人要翻一次
    }

    [Fact]
    public void Change_FlatGetsNoPlusSign()
    {
        var r = QuickMath.Change(50m, 50m);
        Assert.Equal("0.00", Line(r, "差额"));
        Assert.Equal("0%", Line(r, "涨跌"));
        Assert.Equal("已经是这个值", Line(r, "回到 50"));
    }

    [Fact]
    public void Change_ZeroedValue_SaysItCannotComeBack()
    {
        var r = QuickMath.Change(100m, 0m);
        Assert.Equal("-100%", Line(r, "涨跌"));
        Assert.Contains("归零", Line(r, "回本"));
        Assert.DoesNotContain(r.Lines, l => l.Label.StartsWith("回到", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 5, "起始值")]
    [InlineData(-5, 10, "起始值")]
    [InlineData(100, -1, "负数")]
    public void Change_RejectsValuesWithoutPercentMeaning(double from, double to, string keyword)
        => Assert.Contains(keyword, AssertFailed(QuickMath.Change((decimal)from, (decimal)to)));

    // ────────── 单价比较 ──────────

    [Fact]
    public void UnitPriceCompare_PicksTheCheaperAndStatesTheGap()
    {
        var r = QuickMath.UnitPriceCompare(29.9m, 500m, 39.9m, 800m);
        Assert.Equal("0.0598", Line(r, "A 单价"));
        Assert.Equal("0.0499", Line(r, "B 单价"));
        Assert.Equal("B 更划算，比 A 便宜 19.84%", Line(r, "结论"));
    }

    [Fact]
    public void UnitPriceCompare_FourDigitRuleSeparatesPricesOneDigitWouldTie()
    {
        // 12.345 与 12.344：按 2 位（甚至 3 位）显示会判成"一样"，比较必须走到第 4 位
        var r = QuickMath.UnitPriceCompare(12.345m, 1m, 12.344m, 1m);
        Assert.NotEqual(Line(r, "A 单价"), Line(r, "B 单价"));
        Assert.StartsWith("B 更划算", Line(r, "结论"));
    }

    [Fact]
    public void UnitPriceCompare_EqualUnits_SaySoInsteadOfPickingASide()
    {
        var r = QuickMath.UnitPriceCompare(10m, 2m, 15m, 3m);
        Assert.Equal("5.0000", Line(r, "A 单价"));
        Assert.Equal("5.0000", Line(r, "B 单价"));
        Assert.Equal("两者单价相同", Line(r, "结论"));
    }

    [Theory]
    [InlineData(0, 500, 10, 500, "价格")]
    [InlineData(10, 500, 0, 500, "价格")]
    [InlineData(10, 0, 10, 500, "数量")]
    [InlineData(10, 500, 10, 0, "数量")]
    public void UnitPriceCompare_RejectsZeroPriceOrQuantity(
        double priceA, double qtyA, double priceB, double qtyB, string keyword)
        => Assert.Contains(keyword, AssertFailed(QuickMath.UnitPriceCompare(
            (decimal)priceA, (decimal)qtyA, (decimal)priceB, (decimal)qtyB)));

    // ────────── 间隔天数 ──────────

    [Fact]
    public void DateSpan_TenDaysIsOneWeekThreeDays_SevenWorkdays()
    {
        // 2026-10-01 是星期四：10/1–10/11 共 11 个自然日、7 个工作日
        var r = QuickMath.DateSpan(new DateTime(2026, 10, 1), new DateTime(2026, 10, 11));
        Assert.Equal("10 天", Line(r, "相差"));
        Assert.Equal("1 周 3 天", Line(r, "折合"));
        Assert.Equal("7", Line(r, "工作日(含首尾)"));
        Assert.Equal("已含结束日", Line(r, "说明"));
    }

    [Fact]
    public void DateSpan_BackwardCarriesTheMinusAndSaysWhy()
    {
        var r = QuickMath.DateSpan(new DateTime(2026, 10, 11), new DateTime(2026, 10, 1));
        Assert.Equal("-10 天", Line(r, "相差"));
        Assert.Equal("结束日早于开始日", Line(r, "说明"));
        Assert.Equal("7", Line(r, "工作日(含首尾)"));   // 工作日数与方向无关
    }

    [Fact]
    public void DateSpan_SameDay_SaysSo()
    {
        var r = QuickMath.DateSpan(new DateTime(2026, 10, 1), new DateTime(2026, 10, 1));
        Assert.Equal("0 天", Line(r, "相差"));
        Assert.Equal("0 周 0 天", Line(r, "折合"));
        Assert.Equal("同一天", Line(r, "提示"));
        Assert.Equal("1", Line(r, "工作日(含首尾)"));   // 10/1 是星期四
    }

    [Fact]
    public void DateSpan_TimesAreIgnored()
    {
        var r = QuickMath.DateSpan(new DateTime(2026, 10, 1, 23, 59, 0), new DateTime(2026, 10, 2, 0, 1, 0));
        Assert.Equal("1 天", Line(r, "相差"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]   // 反方向也走一遍（"已过 / 还有"两种读法都要带说明）
    public void DateSpan_AlwaysCarriesTheHolidayCaveat(int which)
    {
        var a = new DateTime(2026, 10, 1);
        var b = new DateTime(2026, 10, 11);
        var r = which == 1 ? QuickMath.DateSpan(a, b) : QuickMath.DateSpan(b, a);
        Assert.Equal("工作日不含法定节假日与调休", Line(r, "注意"));
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
    public void QuickResult_TextAndMultiLine_CarryEveryLabel_OrTheError()
    {
        var ok = QuickMath.Bargain(100m, 7.5m, 0m, 0m);
        Assert.Equal(4, ok.Lines.Count);
        Assert.Equal(4, ok.MultiLine.Split('\n').Length);
        foreach (var line in ok.Lines)
        {
            Assert.Contains(line.Label, ok.Text, StringComparison.Ordinal);
            Assert.Contains(line.Value, ok.Text, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("\n", ok.Text, StringComparison.Ordinal);

        var bad = QuickMath.Change(0m, 1m);
        Assert.Equal(bad.Error, bad.Text);
        Assert.Equal(bad.Error, bad.MultiLine);
        Assert.False(bad.Ok);
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
        Assert.Equal(QuickCalculator.Modes.Count, QuickCalculator.Modes.Select(m => m.Title).Distinct().Count());
        Assert.All(QuickCalculator.Modes, m => Assert.False(string.IsNullOrWhiteSpace(m.Hint)));
    }

    [Fact]
    public void EveryMode_HasBetweenTwoAndMaxFields_WithDistinctLabels()
    {
        foreach (var mode in QuickCalculator.Modes)
        {
            Assert.InRange(mode.Fields.Count, 2, QuickCalculator.MaxFields);
            Assert.Equal(mode.Fields.Count, mode.Fields.Select(f => f.Label).Distinct().Count());
            Assert.All(mode.Fields, f => Assert.False(string.IsNullOrWhiteSpace(f.Label)));
            Assert.All(mode.Fields, f => Assert.False(string.IsNullOrWhiteSpace(f.Placeholder)));
            Assert.Contains(mode.Fields, f => f.Required);   // 全可空的模式永远只输出"没填"
        }
    }

    [Fact]
    public void FieldPlaceholders_MatchTheirKind()
    {
        // 占位文本是用户会照着抄的第一个例子：整个模式都按占位文本填一遍必须算得出来，
        // 否则就是"照提示填却报错"。可空槽一律留空（它们抄的是"300（可空）"这类说明）。
        var requiredSlots = 0;
        for (var i = 0; i < QuickCalculator.Modes.Count; i++)
        {
            var mode = QuickCalculator.Modes[i];
            var texts = mode.Fields.Select(f =>
            {
                if (!f.Required) return string.Empty;
                requiredSlots++;
                return f.Placeholder;
            }).ToList();
            var r = QuickCalculator.Evaluate(i, texts, Today);
            Assert.True(r.Ok, $"{mode.Title} 全按占位文本填应当算得出，实际：{r.Error}");
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
        Assert.True(r.Ok, $"模式 {index} 的样例输入失败了：{r.Error}");
        Assert.NotEmpty(r.Lines);
        Assert.All(r.Lines, l => Assert.False(string.IsNullOrWhiteSpace(l.Value)));
    }

    [Fact]
    public void SampleInputs_ReachTheIntendedAnswer()
    {
        Assert.Equal("620.00", QuickCalculator.Evaluate(0, Samples[0], Today).Lines.First(l => l.Label == "到手").Value);
        Assert.Equal("需涨 25%", QuickCalculator.Evaluate(1, Samples[1], Today).Lines.First(l => l.Label == "回到 100").Value);
        Assert.Equal("B 更划算，比 A 便宜 19.84%", QuickCalculator.Evaluate(2, Samples[2], Today).Lines.First(l => l.Label == "结论").Value);
        Assert.Equal("10 天", QuickCalculator.Evaluate(3, Samples[3], Today).Lines.First(l => l.Label == "相差").Value);
    }

    [Fact]
    public void OptionalNumberSlots_BlankMeansNoneNotZeroShown()
    {
        // 满减两栏留空 = 不参加满减，结果里不能冒出"减 0 次"
        var r = QuickCalculator.Evaluate(0, new[] { "100", "7.5", "", null }, Today);
        Assert.True(r.Ok, r.Error);
        Assert.DoesNotContain(r.Lines, l => l.Label == "满减");
        Assert.Equal("75.00", r.Lines.First(l => l.Label == "到手").Value);
    }

    [Fact]
    public void OptionalDateSlot_BlankMeansToday_RequiredOneStaysRequired()
    {
        // 结束日期是"目标日"，只有它有默认值（今天）才有意义；两头都空就退化成"今天到今天"
        var single = QuickCalculator.Evaluate(3, new[] { "", "12-31" }, Today);
        Assert.True(single.Ok, single.Error);
        Assert.Equal("240 天", single.Lines.First(l => l.Label == "相差").Value);

        var noTarget = QuickCalculator.Evaluate(3, new[] { "2026-01-01", "" }, Today);
        Assert.False(noTarget.Ok);
        Assert.Contains("结束日期", noTarget.Error!);
    }

    [Fact]
    public void MissingRequiredInput_NamesTheField()
    {
        Assert.Contains("原价", QuickCalculator.Evaluate(0, new[] { "", "7.5" }, Today).Error!);
        Assert.Contains("结束日期", QuickCalculator.Evaluate(3, new[] { "2026-01-01" }, Today).Error!);
        // 第三个槽没给文本 = 与给空串同一处理（界面可能只放了两栏的长度）
        Assert.Contains("价格 B", QuickCalculator.Evaluate(2, new[] { "10", "2" }, Today).Error!);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("7.5 折")]
    [InlineData("１２３")]
    public void NonNumericInput_QuotesTheFieldAndTheOffendingText(string junk)
    {
        var error = QuickCalculator.Evaluate(1, new[] { junk, "80" }, Today).Error!;
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Contains("起始值", error);
        Assert.Contains(junk, error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnparseableDate_SaysWhichFieldAndHowToWriteIt()
    {
        var error = QuickCalculator.Evaluate(3, new[] { "下周三", "2026-12-31" }, Today).Error!;
        Assert.Contains("开始日期", error);
        Assert.Contains("2026-12-31", error);   // 给出正确写法，别让用户猜格式
    }

    [Fact]
    public void ThousandsSeparators_AreAcceptedInMoneySlots()
    {
        var r = QuickCalculator.Evaluate(1, new[] { "1,000", "800" }, Today);
        Assert.True(r.Ok, r.Error);
        Assert.Equal("-20%", r.Lines.First(l => l.Label == "涨跌").Value);
    }

    [Fact]
    public void UnknownModeIndex_IsRefusedNotCrashed()
    {
        foreach (var index in new[] { -1, QuickCalculator.Modes.Count, 99 })
        {
            var r = QuickCalculator.Evaluate(index, Array.Empty<string?>(), Today);
            Assert.False(r.Ok);
            Assert.Contains(index.ToString(CultureInfo.InvariantCulture), r.Error!);
        }
    }
}
