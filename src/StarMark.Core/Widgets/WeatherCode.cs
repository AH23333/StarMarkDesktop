#nullable enable
using StarMark.Abstractions.I18n;

namespace StarMark.Core.Widgets;

/// <summary>
/// WMO 天气解释码（Weather interpretation codes）→ 那句天气话的<b>键</b> / Segoe Fluent 字形 / emoji 的映射。
/// <para>
/// 码表来源：Open-Meteo 文档（https://open-meteo.com/en/docs），与 DeskBox 的
/// <c>Helpers/WeatherCodeMapper.cs</c> 同源。DeskBox 那份还有 MSN 描述文本反查与 11 种语言，
/// StarMark 一份表配三列（见 <c>UiStrings</c>），故这里只留正向映射，而词交在表里。
/// </para>
/// </summary>
public static class WeatherCode
{
    /// <summary>天气大类，供组件按类选配色/动效。</summary>
    public enum Condition
    {
        Clear,
        Cloudy,
        Fog,
        Drizzle,
        Rain,
        Snow,
        Thunderstorm,
        Unknown,
    }

    /// <summary>未知/缺失码。Open-Meteo 不会返回，用于本地「暂无数据」占位。</summary>
    public const int Unknown = int.MinValue;

    /// <summary>
    /// WMO 码 → 那句天气话的<b>键</b>（批次 WI-53＝P-182a）。
    /// <para>⛔ 在这里返回已经取好词的那句：这里每秒/每次刷新都会被问一遍，而语言是<b>用户随时改的</b>；
    /// 交字符串就等于把语言冻在算出它的那一刻（✅WI-37-C 那颗闹钟同一条口径）。
    /// 键按<b>词</b>开而⛔ 按码开：53 与 61 都说"小雨"、57 与 66 都说"冻雨"，
    /// 一颗词配一颗键才能被证人问"这句有没有第二个主人"。</para>
    /// </summary>
    public static LocMessage DescribeMessage(int code) => code switch
    {
        0 => LocMessage.Of("weather.desc.clear"),
        1 => LocMessage.Of("weather.desc.mainlyClear"),
        2 => LocMessage.Of("weather.desc.cloudy"),
        3 => LocMessage.Of("weather.desc.overcast"),
        45 => LocMessage.Of("weather.desc.fog"),
        48 => LocMessage.Of("weather.desc.freezingFog"),
        51 => LocMessage.Of("weather.desc.drizzle"),
        53 => LocMessage.Of("weather.desc.lightRain"),
        55 => LocMessage.Of("weather.desc.moderateRain"),
        56 => LocMessage.Of("weather.desc.freezingDrizzle"),
        57 => LocMessage.Of("weather.desc.freezingRain"),
        61 => LocMessage.Of("weather.desc.lightRain"),
        63 => LocMessage.Of("weather.desc.moderateRain"),
        65 => LocMessage.Of("weather.desc.heavyRain"),
        66 => LocMessage.Of("weather.desc.freezingRain"),
        67 => LocMessage.Of("weather.desc.heavyFreezingRain"),
        71 => LocMessage.Of("weather.desc.lightSnow"),
        73 => LocMessage.Of("weather.desc.moderateSnow"),
        75 => LocMessage.Of("weather.desc.heavySnow"),
        77 => LocMessage.Of("weather.desc.snowGrains"),
        80 => LocMessage.Of("weather.desc.showers"),
        81 => LocMessage.Of("weather.desc.heavyShowers"),
        82 => LocMessage.Of("weather.desc.violentRain"),
        85 => LocMessage.Of("weather.desc.snowShowers"),
        86 => LocMessage.Of("weather.desc.heavySnowShowers"),
        95 => LocMessage.Of("weather.desc.thunderstorm"),
        96 => LocMessage.Of("weather.desc.thunderstormHail"),
        99 => LocMessage.Of("weather.desc.thunderstormHeavyHail"),
        _ => LocMessage.Of("weather.desc.unknown"),
    };

    /// <summary>
    /// WMO 码 → Segoe Fluent Icons 字形。
    /// 选字形的原则是小尺寸（16–20px）下仍清晰、且各 Windows 版本都能渲染——
    /// 这也是 DeskBox 保留这套映射的原因（emoji 在部分环境会缺字或走不出彩色字形）。
    /// </summary>
    public static string Glyph(int code, bool isDay = true) => code switch
    {
        Unknown => "\uE9CB",                  // 未知：不给"晴"（太阳），用本表已有的中性字形，避免与「未知」文字自相矛盾
        0 => isDay ? "\uE706" : "\uE708",   // 太阳 / 月亮
        1 => isDay ? "\uE706" : "\uE708",   // 大部晴朗
        2 => isDay ? "\uE9D2" : "\uE708",   // 多云 / 夜
        3 => "\uE9D2",                       // 阴
        45 => "\uE9CB",                      // 雾
        48 => "\uE9CB",                      // 冻雾
        >= 51 and <= 57 => "\uE755",         // 毛毛雨 / 冻雨
        >= 61 and <= 67 => "\uE755",         // 雨
        >= 71 and <= 77 => "\uE703",         // 雪
        >= 80 and <= 82 => "\uE755",         // 阵雨
        >= 85 and <= 86 => "\uE703",         // 阵雪
        >= 95 and <= 99 => "\uE756",         // 雷
        _ => "\uE9CB",                       // 表外码：与 Unknown 同处理（中性字形），绝不回落太阳，避免「☀️ 未知」自相矛盾
    };

    /// <summary>WMO 码 → emoji（组件头部大图标用；未知/缺失码显式给 ❓，不回落成太阳）。</summary>
    public static string Emoji(int code, bool isDay = true) => code switch
    {
        Unknown => "❓",
        0 or 1 => isDay ? "☀️" : "\U0001F319",
        2 => isDay ? "⛅" : "\U0001F319",
        3 => "\U0001F325\uFE0F",
        45 or 48 => "☁️",           // 刻意用云不用 🌫️，后者在部分字体下会缺字
        51 or 53 or 55 or 56 or 57 => "\U0001F326\uFE0F",
        >= 61 and <= 67 => "\U0001F327\uFE0F",
        80 or 81 => "\U0001F327\uFE0F",
        82 => "\U0001F327\uFE0F",
        >= 71 and <= 77 or 85 or 86 => "\U0001F328\uFE0F",
        95 => "\U0001F329\uFE0F",
        96 or 99 => "⛈️",
        _ => "❓",                            // 表外码：与 Unknown 同处理，绝不回落太阳（本方法 doc 承诺）
    };

    /// <summary>WMO 码 → 天气大类。</summary>
    public static Condition Classify(int code) => code switch
    {
        0 or 1 => Condition.Clear,
        2 or 3 => Condition.Cloudy,
        45 or 48 => Condition.Fog,
        >= 51 and <= 57 => Condition.Drizzle,
        >= 61 and <= 67 or >= 80 and <= 82 => Condition.Rain,
        >= 71 and <= 77 or 85 or 86 => Condition.Snow,
        >= 95 and <= 99 => Condition.Thunderstorm,
        _ => Condition.Unknown,
    };

    /// <summary>是否算降水（雨/雪/雷都算，雾不算）。</summary>
    public static bool IsPrecipitation(int code) => Classify(code) is
        Condition.Drizzle or Condition.Rain or Condition.Snow or Condition.Thunderstorm;
}
