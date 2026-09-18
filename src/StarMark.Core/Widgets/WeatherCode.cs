#nullable enable
namespace StarMark.Core.Widgets;

/// <summary>
/// WMO 天气解释码（Weather interpretation codes）→ 中文描述 / Segoe Fluent 字形 / emoji 的映射。
/// <para>
/// 码表来源：Open-Meteo 文档（https://open-meteo.com/en/docs），与 DeskBox 的
/// <c>Helpers/WeatherCodeMapper.cs</c> 同源。DeskBox 那份还有 MSN 描述文本反查与 11 种语言，
/// StarMark 只用中文、且数据源固定为 Open-Meteo（WMO 原生码），故只保留正向映射。
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

    /// <summary>WMO 码 → 中文描述。</summary>
    public static string Describe(int code) => code switch
    {
        0 => "晴",
        1 => "晴间多云",
        2 => "多云",
        3 => "阴",
        45 => "雾",
        48 => "冻雾",
        51 => "毛毛雨",
        53 => "小雨",
        55 => "中雨",
        56 => "冻毛毛雨",
        57 => "冻雨",
        61 => "小雨",
        63 => "中雨",
        65 => "大雨",
        66 => "冻雨",
        67 => "强冻雨",
        71 => "小雪",
        73 => "中雪",
        75 => "大雪",
        77 => "米雪",
        80 => "阵雨",
        81 => "强阵雨",
        82 => "暴雨",
        85 => "阵雪",
        86 => "强阵雪",
        95 => "雷阵雨",
        96 => "雷阵雨伴冰雹",
        99 => "雷阵雨伴大冰雹",
        _ => "未知",
    };

    /// <summary>
    /// WMO 码 → Segoe Fluent Icons 字形。
    /// 选字形的原则是小尺寸（16–20px）下仍清晰、且各 Windows 版本都能渲染——
    /// 这也是 DeskBox 保留这套映射的原因（emoji 在部分环境会缺字或走不出彩色字形）。
    /// </summary>
    public static string Glyph(int code, bool isDay = true) => code switch
    {
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
        _ => "\uE706",
    };

    /// <summary>WMO 码 → emoji（组件头部大图标用；未识别时回落太阳）。</summary>
    public static string Emoji(int code, bool isDay = true) => code switch
    {
        0 or 1 => isDay ? "\u2600\uFE0F" : "\U0001F319",
        2 => isDay ? "\u26C5" : "\U0001F319",
        3 => "\U0001F325\uFE0F",
        45 or 48 => "\u2601\uFE0F",           // 刻意用云不用 🌫️，后者在部分字体下会缺字
        51 or 53 or 55 or 56 or 57 => "\U0001F326\uFE0F",
        >= 61 and <= 67 => "\U0001F327\uFE0F",
        80 or 81 => "\U0001F327\uFE0F",
        82 => "\U0001F327\uFE0F",
        >= 71 and <= 77 or 85 or 86 => "\U0001F328\uFE0F",
        95 => "\U0001F329\uFE0F",
        96 or 99 => "\u26C8\uFE0F",
        _ => "\u2600\uFE0F",
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
