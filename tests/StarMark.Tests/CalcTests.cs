#nullable enable
using System.Globalization;
using Xunit;
using StarMark.Core.Calc;

namespace StarMark.Tests;

/// <summary>
/// 计算器求值器测试：优先级、结合性、百分号语义、输入鲁棒性（不抛异常是硬契约）、显示格式。
/// </summary>
public sealed class ExpressionEvaluatorTests
{
    private static double Ok(string text)
    {
        var r = ExpressionEvaluator.Evaluate(text);
        Assert.True(r.Ok, $"应当成功却失败：{text} ⇒ {r.Error}");
        return r.Value;
    }

    private static string Fail(string text)
    {
        var r = ExpressionEvaluator.Evaluate(text);
        Assert.False(r.Ok, $"应当失败却成功：{text} ⇒ {r.Value}");
        return r.Error!;
    }

    // ────────── 优先级与结合性 ──────────

    [Fact]
    public void Multiplication_BindsTighterThanAddition() => Assert.Equal(7, Ok("1+2*3"));

    [Fact]
    public void Parentheses_OverridePrecedence() => Assert.Equal(9, Ok("(1+2)*3"));

    [Fact]
    public void Power_IsRightAssociative() => Assert.Equal(512, Ok("2^3^2"));

    [Fact]
    public void UnaryMinus_BindsLooserThanPower() => Assert.Equal(-4, Ok("-2^2"));

    [Fact]
    public void Exponent_CanCarryItsOwnSign() => Assert.Equal(0.5, Ok("2^-1"));

    [Fact]
    public void Division_IsLeftAssociative() => Assert.Equal(8, Ok("64/4/2"));

    [Fact]
    public void Subtraction_IsLeftAssociative() => Assert.Equal(20, Ok("100-50-30"));

    [Theory]
    [InlineData("2*(3+4)^2", 98)]
    [InlineData("(2+3)*(4-1)", 15)]
    [InlineData("10/4", 2.5)]
    [InlineData("  7 - 2 ", 5)]
    public void CommonExpressions(string text, double expected) => Assert.Equal(expected, Ok(text), 10);

    // ────────── 常量与全角 ──────────

    [Fact]
    public void Constants_PiAndE()
    {
        Assert.Equal(Math.PI, Ok("pi"), 10);
        Assert.Equal(Math.PI, Ok("π"), 10);
        Assert.Equal(Math.PI, Ok("PI"), 10);
        Assert.Equal(Math.E, Ok("e"), 10);
        Assert.Equal(Math.PI * 2, Ok("pi*2"), 10);
    }

    [Theory]
    [InlineData("2×3", 6)]
    [InlineData("6÷2", 3)]
    [InlineData("（1＋2）×3", 9)]
    [InlineData("5－2", 3)]
    public void FullWidthAndImeOperators_AreAccepted(string text, double expected)
        => Assert.Equal(expected, Ok(text), 10);

    [Fact]
    public void ScientificNotation_WhenExponentActuallyFollows() => Assert.Equal(2000, Ok("2e3"));

    [Fact]
    public void LetterE_AfterNumberWithoutDigits_IsNotAnExponent()
    {
        // "2e" 里的 e 是常量 e ⇒ 裸的 "2e" 是"多余的内容"，不能被当成 2 静默算掉
        Assert.Contains("多余", Fail("2e"));
        Assert.Equal(2 * Math.E, Ok("2*e"), 10);
    }

    // ────────── 千分位 ──────────

    [Theory]
    [InlineData("1,234+1", 1235)]
    [InlineData("1,234,567-1", 1234566)]
    public void ThousandsSeparators_AreAccepted(string text, double expected) => Assert.Equal(expected, Ok(text), 6);

    [Theory]
    [InlineData("1,23")]
    [InlineData("1,2345")]
    [InlineData("1.2.3")]
    [InlineData("1.")]
    public void MalformedNumbers_AreRejectedNotSilentlyRounded(string text)
        => Assert.Contains("数字", Fail(text));

    [Fact]
    public void LeadingComma_IsReportedAsUnsupportedCharacter()
    {
        // 逗号只在数字内部有意义；开头一个逗号是"没见过的符号"，说"数字写法无法识别"反而误导
        Assert.Contains("不支持的字符", Fail(",5"));
    }

    // ────────── 百分号语义（计算器惯例，非纯数学） ──────────

    [Theory]
    [InlineData("200+10%", 220)]
    [InlineData("200-10%", 180)]
    [InlineData("200*10%", 20)]
    [InlineData("200/50%", 400)]
    [InlineData("50%", 0.5)]
    public void Percent_FollowsCalculatorConvention(string text, double expected)
        => Assert.Equal(expected, Ok(text), 10);

    [Fact]
    public void Percent_ChainedAddition_UsesEachIntermediateTotal()
    {
        // 逐次累加：200 → +10% ⇒ 220 → +10% ⇒ 242（与桌面计算器一致）
        Assert.Equal(242, Ok("200+10%+10%"), 10);
    }

    [Fact]
    public void Percent_BoundByMultiplicationPrecedence()
    {
        // "200+10%*2" 的右操作数是 (10%)*2 = 0.2 ⇒ 200.2，而不是 200+20*2
        Assert.Equal(200.2, Ok("200+10%*2"), 10);
    }

    // ────────── 错误路径：只给原因，绝不抛 ──────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2+")]
    [InlineData("*3")]
    [InlineData("(1+2")]
    [InlineData("1+2)")]
    [InlineData("2 3")]
    [InlineData("@")]
    [InlineData("%")]
    public void InvalidInput_ReturnsReason(string text) => Assert.False(string.IsNullOrWhiteSpace(Fail(text)));

    [Fact]
    public void EmptyInput_ExplainsItself() => Assert.Equal("没有可计算的内容", Fail(""));

    [Fact]
    public void MissingCloseParen_SaysSo() => Assert.Equal("缺少右括号", Fail("(1+2"));

    [Fact]
    public void TrailingOperator_SaysWhatIsMissing() => Assert.Contains("缺少数字", Fail("2+"));

    [Fact]
    public void DivideByZero_IsNamedNotNaN()
    {
        Assert.Equal("除数为 0", Fail("1/0"));
        Assert.Equal("除数为 0", Fail("1/(3-3)"));
        // 0÷0 同样报"除数为 0"而不是"无定义"：用户按下去的动机是"除以了 0"，这条信息更可行动
        Assert.Equal("除数为 0", Fail("0/0"));
    }

    [Fact]
    public void ZeroToNegativePower_IsAlsoDivideByZero()
    {
        // 0^-1 数学上就是 1÷0，报"除数为 0"比"结果超出可表示范围"更可行动
        Assert.Equal("除数为 0", Fail("0^-1"));
    }

    [Fact]
    public void Overflow_IsReportedAsRange()
    {
        Assert.Equal("结果超出可表示范围", Fail("1e308*10"));
        Assert.Equal("结果超出可表示范围", Fail("10^400"));
    }

    [Fact]
    public void TooLong_IsBoundedAndNamed()
    {
        var error = Fail(new string('1', ExpressionEvaluator.MaxLength + 1));
        Assert.Contains("过长", error);
    }

    [Fact]
    public void DeepNesting_IsStoppedAtTheDepthLimit()
    {
        // 递归下降的递归深度＝括号层数；不设上限时这一串会把进程直接崩掉（栈溢出不可捕获）
        var inside = string.Concat(Enumerable.Repeat("(", ExpressionEvaluator.MaxDepth + 5)) + "1"
                   + string.Concat(Enumerable.Repeat(")", ExpressionEvaluator.MaxDepth + 5));
        Assert.True(inside.Length <= ExpressionEvaluator.MaxLength, "用例本身要落在长度上限内，否则测不到深度闸门");
        Assert.Contains("嵌套过深", Fail(inside));
    }

    [Fact]
    public void AnyGarbageInput_NeverThrows()
    {
        // 逐键求值路径：一次抛出＝一次崩溃弹窗。这里穷举单字符与短串，确保全部走"返回原因"。
        var samples = new[] { "(", ")", "%", "^", "*", "/", "+", "-", ".", ",", "e", "π", "２", "０^０", "1^", "()", "%%" };
        foreach (var s in samples)
        {
            var r = ExpressionEvaluator.Evaluate(s);
            Assert.False(r.Ok && double.IsNaN(r.Value), $"「{s}」返回了 NaN 却标记成功");
            Assert.False(r.Ok && double.IsInfinity(r.Value), $"「{s}」返回了无穷却标记成功");
            if (!r.Ok) Assert.False(string.IsNullOrWhiteSpace(r.Error), $"「{s}」失败却没给原因");
        }
    }

    // ────────── 显示格式 ──────────

    [Fact]
    public void Format_TrimsBinaryFloatingNoise()
    {
        // 0.1+0.2 的二值和是 0.30000000000000004；G15 把它收成 0.3
        Assert.Equal("0.3", Ok("0.1+0.2").ToString("G15", CultureInfo.InvariantCulture));
        Assert.Equal("0.3", ExpressionEvaluator.Format(Ok("0.1+0.2")));
    }

    [Theory]
    [InlineData("1000000", "1,000,000")]
    [InlineData("1234567.89", "1,234,567.89")]
    [InlineData("-4500", "-4,500")]
    [InlineData("2.5", "2.5")]
    [InlineData("1e30", "1E+30")]
    public void Format_GroupsThousandsAndRoundTrips(string text, string expected)
    {
        var shown = ExpressionEvaluator.Evaluate(text).Display;
        Assert.Equal(expected, shown);
        // 分节号能被自己的解析器吃回去 ⇒ "复制结果 → 粘回输入框"不断链
        Assert.True(ExpressionEvaluator.Evaluate(shown).Ok, $"显示串应可再解析：{shown}");
    }

    [Fact]
    public void Format_DropsTrailingZeroOfWholeNumbers()
    {
        Assert.Equal("5", ExpressionEvaluator.Format(5d));
        Assert.Equal("5", ExpressionEvaluator.Format(5.0d));
        Assert.Equal("0", ExpressionEvaluator.Format(0d));
        Assert.Equal("0", ExpressionEvaluator.Format(-0d));
    }
}

/// <summary>单位换算静态表测试：已知换算率、往返一致性、温度仿射、错误路径。</summary>
public sealed class UnitTablesTests
{
    private static decimal Convert(string category, string from, string to, decimal value)
    {
        Assert.True(UnitTables.TryConvert(category, from, to, value, out var result, out var error),
            $"换算应当成功：{value}{from}→{to} ⇒ {error}");
        return result;
    }

    [Fact]
    public void KnownLengthRates()
    {
        Assert.Equal(1609.344m, Convert(UnitTables.LengthKey, "mi", "m", 1m), 6);
        Assert.Equal(0.0254m, Convert(UnitTables.LengthKey, "in", "m", 1m), 8);
        Assert.Equal(500m, Convert(UnitTables.LengthKey, "li", "m", 1m), 6);
        Assert.Equal(2000m, Convert(UnitTables.LengthKey, "km", "m", 2m), 6);
        // 反向（大单位→小单位之外）也要成立：米→千米要缩
        Assert.Equal(2m, Convert(UnitTables.LengthKey, "m", "km", 2000m), 6);
    }

    [Fact]
    public void KnownMassRates()
    {
        Assert.Equal(0.45359237m, Convert(UnitTables.MassKey, "lb", "kg", 1m), 8);
        Assert.Equal(100m, Convert(UnitTables.MassKey, "jin", "liang", 10m), 6);   // 1 斤 = 10 两
        Assert.Equal(1000m, Convert(UnitTables.MassKey, "kg", "g", 1m), 6);
    }

    [Fact]
    public void KnownAreaRates()
    {
        Assert.Equal(10000m, Convert(UnitTables.AreaKey, "ha", "m2", 1m), 6);
        // 1 亩 = 2000/3 平方米；1 公顷 = 15 亩
        Assert.Equal(15m, Convert(UnitTables.AreaKey, "ha", "mu", 1m), 4);
    }

    [Fact]
    public void KnownVolumeAndSpeedRates()
    {
        Assert.Equal(3.785411784m, Convert(UnitTables.VolumeKey, "galus", "l", 1m), 8);
        Assert.Equal(1m / 3.6m, Convert(UnitTables.SpeedKey, "kmh", "mps", 1m), 6);
        Assert.Equal(0.5144444444m, Convert(UnitTables.SpeedKey, "kn", "mps", 1m), 8);
    }

    [Fact]
    public void Storage_IsBinaryBased()
    {
        Assert.Equal(1024m, Convert(UnitTables.StorageKey, "kb", "b", 1m), 6);
        Assert.Equal(1073741824m, Convert(UnitTables.StorageKey, "gb", "b", 1m), 6);
        Assert.Equal(8m, Convert(UnitTables.StorageKey, "b", "bit", 1m), 6);
    }

    [Theory]
    [InlineData(0d, 32d)]      // 冰点
    [InlineData(100d, 212d)]   // 沸点
    [InlineData(-40d, -40d)]   // 两标度唯一交点：只有仿射定义正确时才成立
    [InlineData(37d, 98.6d)]
    public void Temperature_IsAffineNotLinear(double celsius, double fahrenheit)
    {
        var got = Convert(UnitTables.TemperatureKey, "c", "f", (decimal)celsius);
        Assert.Equal(fahrenheit, (double)got, 4);
    }

    [Fact]
    public void Temperature_KelvinRoundTrip()
    {
        var k = Convert(UnitTables.TemperatureKey, "c", "k", 25m);
        Assert.Equal(298.15m, k, 6);
        Assert.Equal(25m, Convert(UnitTables.TemperatureKey, "k", "c", k), 4);
    }

    [Fact]
    public void EveryUnit_RoundTripsToItself()
    {
        foreach (var cat in UnitTables.Categories)
        foreach (var unit in cat.Units)
        {
            var back = Convert(cat.Key, unit.Key, unit.Key, 123.456m);
            Assert.Equal(123.456m, decimal.Round(back, 3));   // 同单位往返必须保持不变
        }
    }

    [Fact]
    public void EveryUnit_RoundTripsThroughBase()
    {
        foreach (var cat in UnitTables.Categories)
        foreach (var unit in cat.Units)
        {
            var there = Convert(cat.Key, unit.Key, cat.Units[0].Key, 7m);
            var back = Convert(cat.Key, cat.Units[0].Key, unit.Key, there);
            Assert.Equal(7m, decimal.Round(back, 4));   // 经基准量往返必须回到原值
        }
    }

    [Fact]
    public void Category_KeysAreUniqueAndUnitsAreNamed()
    {
        var keys = UnitTables.Categories.Select(c => c.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        foreach (var cat in UnitTables.Categories)
        {
            Assert.False(string.IsNullOrWhiteSpace(cat.Label));
            Assert.True(cat.Units.Count >= 2, $"{cat.Key} 至少要有两个单位才谈得上换算");
            var unitKeys = cat.Units.Select(u => u.Key).ToList();
            Assert.Equal(unitKeys.Count, unitKeys.Distinct().Count());
        }
    }

    [Fact]
    public void ExactlyOneBaseUnitPerCategory()
    {
        foreach (var cat in UnitTables.Categories)
        {
            var bases = cat.Units.Count(u => u.ToBase == 1m && u.BaseOffset == 0m);
            Assert.Equal(1, bases);
        }
    }

    [Fact]
    public void UnknownInputs_ReturnReasonNotException()
    {
        Assert.False(UnitTables.TryConvert("nope", "m", "km", 1m, out _, out var e1));
        Assert.Equal("不认识这一类单位", e1);
        Assert.False(UnitTables.TryConvert(UnitTables.LengthKey, "zz", "km", 1m, out _, out var e2));
        Assert.Equal("起始或目标单位无效，请重新选择", e2);
        Assert.False(UnitTables.TryConvert(null, "m", "km", 1m, out _, out _));
    }

    [Fact]
    public void Format_TrimsDecimalTailFromFractionalFactors()
    {
        // 表里 5/9 这类分数在 decimal 下有 28 位尾巴；显示必须收干净
        // （100℃ 转 ℉ 是 212，不是 212.0000000000000000000000000）
        Assert.Equal("212", UnitTables.Format(Convert(UnitTables.TemperatureKey, "c", "f", 100m)));
        Assert.StartsWith("0.310685", UnitTables.Format(Convert(UnitTables.LengthKey, "li", "mi", 1m)));
        Assert.Equal("1609.344", UnitTables.Format(Convert(UnitTables.LengthKey, "mi", "m", 1m)));
    }
}

/// <summary>时间戳换算测试：位数判精度、范围闸门、时区显式化、格式化。</summary>
public sealed class TimestampConverterTests
{
    private static readonly TimeZoneInfo Utc8 = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    private static DateTimeOffset Stamp(string text)
    {
        Assert.True(TimestampConverter.TryParseStamp(text, Utc8, out var at, out _, out var error), $"应成功：{error}");
        return at;
    }

    [Fact]
    public void TenDigits_AreSeconds()
    {
        var at = Stamp("1727000000");
        Assert.Equal(2024, at.Year);
        Assert.Equal(new TimeSpan(8, 0, 0), at.Offset);   // 显式按传入时区给偏移，不跟本机时区走
    }

    [Fact]
    public void ThirteenDigits_AreMilliseconds()
    {
        var at = Stamp("1727000000123");
        Assert.Equal(2024, at.Year);
        Assert.Equal(123, at.Millisecond);
    }

    [Fact]
    public void SixteenDigits_AreMicroseconds()
    {
        var at = Stamp("1727000000123456");
        Assert.Equal(2024, at.Year);
        Assert.Equal(123, at.Millisecond);
    }

    [Fact]
    public void SameInstant_IndependentOfZone()
    {
        Assert.True(TimestampConverter.TryParseStamp("1727000000", Utc8, out var local, out _, out _));
        Assert.True(TimestampConverter.TryParseStamp("1727000000", TimeZoneInfo.Utc, out var utc, out _, out _));
        Assert.Equal(local.ToUniversalTime(), utc.ToUniversalTime());   // 同一时刻，只是展示偏移不同
        Assert.NotEqual(local.Offset, utc.Offset);
    }

    [Fact]
    public void DetectUnit_Thresholds()
    {
        Assert.Equal(UnixStampUnit.Seconds, TimestampConverter.DetectUnit(99_999_999_999));
        Assert.Equal(UnixStampUnit.Milliseconds, TimestampConverter.DetectUnit(100_000_000_000));
        Assert.Equal(UnixStampUnit.Microseconds, TimestampConverter.DetectUnit(100_000_000_000_000));
        // 负数（1970 之前）与 long.MinValue（取绝对值会溢出）都不能翻脸
        Assert.Equal(UnixStampUnit.Seconds, TimestampConverter.DetectUnit(-1_000_000));
        Assert.Equal(UnixStampUnit.Microseconds, TimestampConverter.DetectUnit(long.MinValue));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12.5")]
    [InlineData("1e9")]
    public void GarbageStamps_ReturnReason(string text)
    {
        Assert.False(TimestampConverter.TryParseStamp(text, Utc8, out _, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void OutOfRangeStamps_AreRejectedBeforeFrameworkThrows()
    {
        // 1e16 微秒 = 1e10 秒 = 公元 2286 年以内，但 9e18 秒直接超出 DateTimeOffset 上限：
        // 不挡就会从 UI 线程抛出 ArgumentOutOfRangeException
        Assert.False(TimestampConverter.TryParseStamp("9223372036854775807", Utc8, out _, out _, out var error));
        Assert.Contains("超出", error);
        Assert.True(TimestampConverter.TryParseStamp("-62135596800", Utc8, out _, out _, out _));
    }

    [Fact]
    public void Describe_ShowsLocalAndUtcWithWeekday()
    {
        var text = TimestampConverter.Describe(1727000000, Utc8);
        Assert.Contains("UTC+08:00", text);
        Assert.Contains("UTC", text);
        Assert.Contains("星期", text);
    }

    [Fact]
    public void ClockToStamp_UsesGivenZoneWhenTextHasNoOffset()
    {
        Assert.True(TimestampConverter.TryParseClock("2026-01-01 00:00:00", Utc8, out var at, out _));
        Assert.Equal(new TimeSpan(8, 0, 0), at.Offset);
        Assert.Equal(at.ToUnixTimeSeconds(), TimestampConverter.ToUnix(at, UnixStampUnit.Seconds));
        Assert.Equal(at.ToUnixTimeMilliseconds(), TimestampConverter.ToUnix(at, UnixStampUnit.Milliseconds));
    }

    [Fact]
    public void ClockWithExplicitOffset_TrustsTheText()
    {
        Assert.True(TimestampConverter.TryParseClock("2026-01-01T00:00:00Z", Utc8, out var at, out _));
        Assert.Equal(TimeSpan.Zero, at.Offset);
    }

    [Theory]
    [InlineData("2026-09-23")]
    [InlineData("2026-09-23 20:48")]
    [InlineData("2026-09-23 20:48:12")]
    [InlineData("2026/9/23 8:05")]
    public void CommonClockFormats_AllParse(string text)
    {
        Assert.True(TimestampConverter.TryParseClock(text, TimeZoneInfo.Utc, out var at, out var error),
            $"{text} 应可解析：{error}");
        Assert.Equal(2026, at.Year);
        Assert.Equal(9, at.Month);
        Assert.Equal(23, at.Day);
    }

    [Fact]
    public void IncompleteClock_IsRejected()
    {
        Assert.False(TimestampConverter.TryParseClock("", Utc8, out _, out _));
        Assert.False(TimestampConverter.TryParseClock("昨天下午", Utc8, out _, out var error));
        Assert.Contains("无法识别", error);
    }

    [Fact]
    public void PreEpochClocks_RoundTripThroughNegativeStamps()
    {
        // 1970 之前的时刻折成秒/毫秒是**负的**，框架不抛（我原先假设它会抛 ArgumentOutOfRangeException，
        // 实测 1500 年正常给出负值）⇒ 这里钉的是"负值原样往返"，不是"拒绝"。
        var early = new DateTimeOffset(1500, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var seconds = TimestampConverter.ToUnix(early, UnixStampUnit.Seconds);
        Assert.True(seconds < 0);
        Assert.True(TimestampConverter.TryParseStamp(seconds.ToString(CultureInfo.InvariantCulture),
            TimeZoneInfo.Utc, out var back, out var unit, out _));
        Assert.Equal(UnixStampUnit.Seconds, unit);
        Assert.Equal(early, back);
    }

    [Fact]
    public void MicrosAtFarFuture_AreExactMultiplesOfMillisAndFitInLong()
    {
        // 9999-12-31 是 DateTimeOffset 的上限，它的微秒值仍要装得进 long——这是 ToUnix 全函数的依据
        var far = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var micros = TimestampConverter.ToUnix(far, UnixStampUnit.Microseconds);
        Assert.Equal(far.ToUnixTimeMilliseconds() * 1000, micros);
        Assert.True(micros < long.MaxValue);
    }

    [Fact]
    public void WeekdayNames_CoverAllSevenDays()
    {
        var names = Enumerable.Range(0, 7).Select(i =>
            TimestampConverter.WeekName((DayOfWeek)i)).ToList();
        Assert.Equal(7, names.Distinct().Count());
        Assert.Equal("星期日", TimestampConverter.WeekName(DayOfWeek.Sunday));
    }
}
