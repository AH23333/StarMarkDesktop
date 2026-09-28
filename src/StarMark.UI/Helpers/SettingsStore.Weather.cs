#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.Feed;
using StarMark.Core.Feed;
using StarMark.Abstractions.Trending;
using StarMark.Core.Hotkeys;
using StarMark.Core.Performance;
using StarMark.Integrations.Weather;
using StarMark.UI.Services;   // 热键注册投影要问"此刻的会话态"（架构方案 §6.1，投影只有这一处）

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——天气组件那组：城市、单位、视图。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsStore
{

    /// <summary>
    /// 天气组件所选城市。未选过（或 JSON 损坏 / 旧版无此字段）时返回 null，
    /// 组件据此显示「点此选择城市」。解析失败一律兜底为 null —— 设置文件是用户可手改的，
    /// 坏数据绝不能冒异常到 UI 线程。
    /// </summary>
    public WeatherCity? LoadWeatherCity()
    {
        var json = Load()?.WeatherCityJson;
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<WeatherCity>(json);
        }
        catch
        {
            return null;
        }
    }

    public void SaveWeatherCity(WeatherCity? city)
    {
        var d = Load() ?? new SettingsData();
        try
        {
            d.WeatherCityJson = city is null ? null : System.Text.Json.JsonSerializer.Serialize(city);
        }
        catch
        {
            d.WeatherCityJson = null;
        }
        Save(d);
    }

    /// <summary>天气温度单位（默认摄氏）。非法值一律回落摄氏。</summary>
    public WeatherUnit LoadWeatherUnit() => WeatherUnits.Parse(Load()?.WeatherUnit);

    public void SaveWeatherUnit(WeatherUnit unit)
    {
        var d = Load() ?? new SettingsData();
        d.WeatherUnit = (int)unit;
        Save(d);
    }

    /// <summary>
    /// 天气视图（默认未来三天）。非法值回落为 0。
    /// 注意「先 is 判断再取值」：旧版 settings.json 没有这个字段时为 null，
    /// 直接写 <c>d.WeatherView ?? 0</c> 再 <c>.Value</c> 会对 null 取值抛 InvalidOperationException。
    /// </summary>
    public WeatherForecastView LoadWeatherView()
    {
        if (Load()?.WeatherView is { } raw &&
            Enum.IsDefined(typeof(WeatherForecastView), raw))
            return (WeatherForecastView)raw;
        return WeatherForecastView.Daily;
    }

    public void SaveWeatherView(WeatherForecastView view)
    {
        var d = Load() ?? new SettingsData();
        d.WeatherView = (int)view;
        Save(d);
    }
}
