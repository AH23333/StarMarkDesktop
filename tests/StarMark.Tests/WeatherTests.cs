#nullable enable
using System.Globalization;
using StarMark.Core.Widgets;
using StarMark.Integrations.Weather;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 天气组件的可测部分：WMO 码映射（纯逻辑）与 Open-Meteo 的 URL 构造（拼串，不发请求）。
/// 真正的网络请求不进单测——离线/限流时测试不该红。
/// </summary>
public class WeatherTests
{
    // ── WMO 天气码映射 ──

    [Theory]
    [InlineData(0, "晴")]
    [InlineData(2, "多云")]
    [InlineData(3, "阴")]
    [InlineData(45, "雾")]
    [InlineData(63, "中雨")]
    [InlineData(75, "大雪")]
    [InlineData(95, "雷阵雨")]
    [InlineData(99, "雷阵雨伴大冰雹")]
    public void Describe_KnownCodes_ReturnsChinese(int code, string expected)
        => Assert.Equal(expected, WeatherCode.Describe(code));

    [Fact]
    public void Describe_UnknownCode_FallsBackToUnknown()
        => Assert.Equal("未知", WeatherCode.Describe(12345));

    [Fact]
    public void Glyph_DistinguishesDayAndNightForClearSky()
    {
        // 晴天的日/夜图标必须不同（太阳 vs 月亮），否则夜里显示太阳很出戏
        Assert.Equal("\uE706", WeatherCode.Glyph(0, isDay: true));
        Assert.Equal("\uE708", WeatherCode.Glyph(0, isDay: false));
    }

    [Theory]
    [InlineData(0, WeatherCode.Condition.Clear)]
    [InlineData(3, WeatherCode.Condition.Cloudy)]
    [InlineData(45, WeatherCode.Condition.Fog)]
    [InlineData(53, WeatherCode.Condition.Drizzle)]
    [InlineData(63, WeatherCode.Condition.Rain)]
    [InlineData(75, WeatherCode.Condition.Snow)]
    [InlineData(95, WeatherCode.Condition.Thunderstorm)]
    [InlineData(-1, WeatherCode.Condition.Unknown)]
    public void Classify_MapsToCondition(int code, WeatherCode.Condition expected)
        => Assert.Equal(expected, WeatherCode.Classify(code));

    [Fact]
    public void IsPrecipitation_ExcludesFogAndClear()
    {
        Assert.False(WeatherCode.IsPrecipitation(0));    // 晴
        Assert.False(WeatherCode.IsPrecipitation(45));   // 雾不算降水
        Assert.True(WeatherCode.IsPrecipitation(63));    // 雨
        Assert.True(WeatherCode.IsPrecipitation(75));    // 雪
        Assert.True(WeatherCode.IsPrecipitation(95));    // 雷
    }

    [Fact]
    public void Emoji_NeverEmptyForKnownCodes()
    {
        foreach (var code in new[] { 0, 1, 2, 3, 45, 48, 51, 61, 71, 80, 95, 99 })
        {
            Assert.False(string.IsNullOrEmpty(WeatherCode.Emoji(code)));
        }
    }

    // ── Open-Meteo URL 构造 ──

    [Fact]
    public void BuildGeocodingUrl_EscapesCityName()
    {
        var url = OpenMeteoClient.BuildGeocodingUrl("北京 朝阳", 8, "zh");
        Assert.StartsWith("https://geocoding-api.open-meteo.com/v1/search?", url);
        Assert.Contains("name=%E5%8C%97%E4%BA%AC", url);   // 中文必须被转义
        Assert.Contains("count=8", url);
        Assert.Contains("language=zh", url);
        Assert.Contains("format=json", url);
    }

    [Fact]
    public void BuildForecastUrl_RequestsCurrentAndDailyFields()
    {
        var url = OpenMeteoClient.BuildForecastUrl(39.9042, 116.4074, 4);
        Assert.StartsWith("https://api.open-meteo.com/v1/forecast?", url);
        Assert.Contains("latitude=39.9042", url);
        Assert.Contains("longitude=116.4074", url);
        Assert.Contains("current=temperature_2m", url);
        Assert.Contains("weather_code", url);
        Assert.Contains("timezone=auto", url);
        Assert.Contains("forecast_days=4", url);
    }

    /// <summary>
    /// 区域文化坑：在德语/法语等区域下 double.ToString() 会把小数点写成逗号（39,9042），
    /// 拼进 URL 就变成 latitude=39,9042 —— 服务端解析失败。必须用 InvariantCulture。
    /// 这里临时切到 de-DE 验证拼出来的仍是点号。
    /// </summary>
    [Fact]
    public void BuildForecastUrl_UsesInvariantCultureUnderCommaDecimalLocale()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var url = OpenMeteoClient.BuildForecastUrl(39.9042, 116.4074, 4);
            Assert.Contains("latitude=39.9042", url);
            Assert.DoesNotContain("latitude=39,9042", url);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ── 城市展示名 ──

    [Fact]
    public void CityDisplay_AppendsAdmin1OnlyWhenDifferent()
    {
        var same = new WeatherCity { Name = "北京", Admin1 = "北京" };
        Assert.Equal("北京", same.Display);

        var diff = new WeatherCity { Name = "朝阳", Admin1 = "辽宁" };
        Assert.Equal("朝阳 · 辽宁", diff.Display);

        var bare = new WeatherCity { Name = "上海" };
        Assert.Equal("上海", bare.Display);
    }

    // ── 温度/风速单位 ──

    [Fact]
    public void BuildForecastUrl_AlsoRequestsHourlyFields()
    {
        // 逐时=预报 today hourly：有了它"今日逐时"视图才有数据可用
        var url = OpenMeteoClient.BuildForecastUrl(39.9042, 116.4074, 4);
        Assert.Contains("hourly=", url);
        Assert.Contains("temperature_2m", url);
    }

    [Fact]
    public void ToFahrenheit_UsesStandardConversion()
    {
        Assert.Equal(32, WeatherUnits.ToFahrenheit(0));
        Assert.Equal(212, WeatherUnits.ToFahrenheit(100));
        Assert.Equal(73.4, Math.Round(WeatherUnits.ToFahrenheit(23), 1));
    }

    [Theory]
    [InlineData(23, WeatherUnit.Celsius, "23°")]
    [InlineData(23, WeatherUnit.Fahrenheit, "73°")]
    [InlineData(0, WeatherUnit.Fahrenheit, "32°")]
    public void TemperatureText_AppliesSelectedUnit(double celsius, WeatherUnit unit, string expected)
        => Assert.Equal(expected, WeatherUnits.TemperatureText(celsius, unit));

    [Fact]
    public void WindText_UsesMphUnderFahrenheit()
    {
        // 华氏=整套英制；只换温度不换风速会变成"73° + 12 km/h"这种混搭
        Assert.Equal("12 km/h", WeatherUnits.WindText(12, WeatherUnit.Celsius));
        Assert.Equal("7 mph", WeatherUnits.WindText(12, WeatherUnit.Fahrenheit));
    }

    [Fact]
    public void UnitSuffix_MatchesSelectedUnit()
    {
        Assert.Equal("°C", WeatherUnits.UnitSuffix(WeatherUnit.Celsius));
        Assert.Equal("°F", WeatherUnits.UnitSuffix(WeatherUnit.Fahrenheit));
    }

    [Theory]
    [InlineData(null, WeatherUnit.Celsius)]   // 旧 settings.json 没有这个字段时为 null，必须回落而不是抛
    [InlineData(0, WeatherUnit.Celsius)]
    [InlineData(1, WeatherUnit.Fahrenheit)]
    [InlineData(9, WeatherUnit.Celsius)]      // 手改坏了的非法值也回落
    [InlineData(-1, WeatherUnit.Celsius)]
    public void Parse_InvalidOrMissingFallsBackToCelsius(int? raw, WeatherUnit expected)
        => Assert.Equal(expected, WeatherUnits.Parse(raw));

    // ── 尺寸档位（含滞回）──

    [Theory]
    [InlineData(120, 100, WeatherLayoutLevel.Mini)]      // 很小：无条件 Mini
    [InlineData(400, 400, WeatherLayoutLevel.Expanded)]  // 很大
    public void Determine_LargeAndSmall_AreStable(double w, double h, WeatherLayoutLevel expected)
    {
        // 从中档出发也要落到同一结论，说明结论不依赖起始档位
        Assert.Equal(expected, WeatherLayoutMath.Determine(w, h, WeatherLayoutLevel.Compact));
        Assert.Equal(expected, WeatherLayoutMath.Determine(w, h, WeatherLayoutLevel.Mini));
        Assert.Equal(expected, WeatherLayoutMath.Determine(w, h, WeatherLayoutLevel.Expanded));
    }

    [Fact]
    public void Determine_HysteresisPreventsFlickerAtThreshold()
    {
        // 关键回归：升档阈值 300×260，降档阈值 280×240。
        // 291 落在缓冲区里——已经是 Expanded 就保持，已经是 Compact 也不升级，
        // 否则用户拖边缘经过这一点时界面会疯狂跳档。
        const double w = 291, h = 250;

        Assert.Equal(WeatherLayoutLevel.Expanded, WeatherLayoutMath.Determine(w, h, WeatherLayoutLevel.Expanded));
        Assert.Equal(WeatherLayoutLevel.Compact, WeatherLayoutMath.Determine(w, h, WeatherLayoutLevel.Compact));
    }

    [Fact]
    public void Determine_UpgradesOnlyAfterPassingUpgradeThreshold()
    {
        // 从 Mini 出发：185×140 落在「已过降级线、未到升级线」的缓冲区里，保持不动；
        // 195×150 过了升级线才升档。
        Assert.Equal(WeatherLayoutLevel.Mini, WeatherLayoutMath.Determine(185, 140, WeatherLayoutLevel.Mini));
        Assert.Equal(WeatherLayoutLevel.Compact, WeatherLayoutMath.Determine(195, 150, WeatherLayoutLevel.Mini));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-10)]
    public void Determine_InvalidSize_KeepsCurrentLevel(double bad)
    {
        // 布局还没起来时 SizeChanged 会带着 0 或非法值进来，此时不能乱跳档
        Assert.Equal(WeatherLayoutLevel.Compact, WeatherLayoutMath.Determine(bad, 300, WeatherLayoutLevel.Compact));
        Assert.Equal(WeatherLayoutLevel.Compact, WeatherLayoutMath.Determine(300, bad, WeatherLayoutLevel.Compact));
    }

    [Fact]
    public void Determine_LargerTypographyDowngradesEarlier()
    {
        // 文本放大后同样尺寸要落到更低密度档，否则字挤爆
        Assert.Equal(WeatherLayoutLevel.Expanded, WeatherLayoutMath.Determine(300, 260, WeatherLayoutLevel.Mini, 1.0));
        Assert.NotEqual(WeatherLayoutLevel.Expanded, WeatherLayoutMath.Determine(300, 260, WeatherLayoutLevel.Mini, 1.6));
    }

    [Theory]
    [InlineData(WeatherLayoutLevel.Mini, false)]
    [InlineData(WeatherLayoutLevel.Compact, true)]
    [InlineData(WeatherLayoutLevel.Expanded, true)]
    public void ShowForecast_MatchesLevel(WeatherLayoutLevel level, bool expected)
        => Assert.Equal(expected, WeatherLayoutMath.ShowForecast(level));

    [Theory]
    [InlineData(WeatherLayoutLevel.Mini, false)]
    [InlineData(WeatherLayoutLevel.Compact, false)]
    [InlineData(WeatherLayoutLevel.Expanded, true)]
    public void ShowExtraMetrics_OnlyWhenExpanded(WeatherLayoutLevel level, bool expected)
        => Assert.Equal(expected, WeatherLayoutMath.ShowExtraMetrics(level));

    [Fact]
    public void TemperatureFontSize_ShrinksAsLevelDrops()
    {
        Assert.True(WeatherLayoutMath.TemperatureFontSize(WeatherLayoutLevel.Mini) <
                    WeatherLayoutMath.TemperatureFontSize(WeatherLayoutLevel.Compact));
        Assert.True(WeatherLayoutMath.TemperatureFontSize(WeatherLayoutLevel.Compact) <
                    WeatherLayoutMath.TemperatureFontSize(WeatherLayoutLevel.Expanded));
    }
}
