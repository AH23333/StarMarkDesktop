#nullable enable
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarMark.Integrations.Weather;

/// <summary>地理编码命中的城市（Open-Meteo geocoding 结果）。</summary>
public sealed class WeatherCity
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Country { get; set; } = string.Empty;
    /// <summary>一级行政区（中国的省/直辖市）。可能为空。</summary>
    public string Admin1 { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;

    /// <summary>展示名：同名城市补上行政区，避免「北京」和别处北京分不清。</summary>
    public string Display =>
        string.IsNullOrWhiteSpace(Admin1) || Admin1 == Name ? Name : $"{Name} · {Admin1}";
}

/// <summary>当前天气实况。</summary>
public sealed class WeatherNow
{
    public double TemperatureC { get; set; }
    public double FeelsLikeC { get; set; }
    public int Humidity { get; set; }
    public double WindSpeedKmh { get; set; }
    /// <summary>WMO 天气解释码（Open-Meteo 原生，可直接喂给 WeatherCode）。</summary>
    public int Code { get; set; }
    /// <summary>是否白天。需要区分日/夜图标（太阳 vs 月亮）。</summary>
    public bool IsDay { get; set; }
}

/// <summary>单日预报。</summary>
public sealed class WeatherDay
{
    public DateOnly Date { get; set; }
    public int Code { get; set; }
    public double MaxC { get; set; }
    public double MinC { get; set; }
}

/// <summary>逐小时预报的一个点。</summary>
public sealed class WeatherHour
{
    public DateTimeOffset Time { get; set; }
    public double TemperatureC { get; set; }
    /// <summary>WMO 天气码。逐时也带code，"今日逐时"视图据此显示图标。</summary>
    public int Code { get; set; }
}

/// <summary>
/// 温度/风速的展示单位。设置里持久化成 int（0=摄氏 1=华氏），枚举只是为了代码可读。
/// <para>华氏模式下风速一并换成 mph —— 华氏用户期望的是整套英制，只换温度会很怪。</para>
/// </summary>
public enum WeatherUnit
{
    Celsius = 0,
    Fahrenheit = 1,
}

/// <summary>预报的两种视图：多日概览 / 今日逐时。</summary>
public enum WeatherForecastView
{
    Daily = 0,
    Hourly = 1,
}

/// <summary>
/// 单位换算与格式化。全部是纯函数、无关网络和外设，因此可脱离 UI 单测。
/// </summary>
public static class WeatherUnits
{
    public static double ToFahrenheit(double celsius) => celsius * 9.0 / 5.0 + 32.0;

    public static double ToMilesPerHour(double kmPerHour) => kmPerHour / 1.609344;

    /// <summary>显示用整数温度字符串，已含单位符号（23° / 73°）。</summary>
    public static string TemperatureText(double celsius, WeatherUnit unit) =>
        $"{TemperatureValue(celsius, unit)}°";

    /// <summary>显示用整数温度数值（不带符号），给需要自己排版的地方用。</summary>
    public static long TemperatureValue(double celsius, WeatherUnit unit) =>
        (long)Math.Round(unit == WeatherUnit.Fahrenheit ? ToFahrenheit(celsius) : celsius);

    /// <summary>单位后缀（°C / °F），给"23° / 14°"这种区间排版配文字说明时用。</summary>
    public static string UnitSuffix(WeatherUnit unit) => unit == WeatherUnit.Fahrenheit ? "°F" : "°C";

    /// <summary>风速串。摄氏=km/h，华氏=mph。</summary>
    public static string WindText(double kmPerHour, WeatherUnit unit) =>
        unit == WeatherUnit.Fahrenheit
            ? $"{Math.Round(ToMilesPerHour(kmPerHour))} mph"
            : $"{Math.Round(kmPerHour)} km/h";

    /// <summary>解析持久化的单位，非法值一律回落摄氏（参考 SettingsStore 可空字段坑：先 is 判断再取值）。</summary>
    public static WeatherUnit Parse(int? raw) =>
        raw is { } v && Enum.IsDefined(typeof(WeatherUnit), v) ? (WeatherUnit)v : WeatherUnit.Celsius;
}

/// <summary>一次完整的天气查询结果（实况 + 多日预报）。</summary>
public sealed class WeatherReport
{
    public WeatherCity City { get; set; } = new();
    public WeatherNow Now { get; set; } = new();
    public List<WeatherDay> Days { get; set; } = [];
    /// <summary>逐小时预报（按时间升序）。"今日逐时"视图用它。</summary>
    public List<WeatherHour> Hours { get; set; } = [];
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>
/// Open-Meteo 天气客户端。
/// <para>
/// 选它的理由：forecast 与 geocoding 两个端点都<b>免费、无需 API Key、无需注册</b>，
/// 对桌面小组件这种低频请求最省心。DeskBox 默认走的是 MSN Weather（内嵌一个私有 key
/// 并回落到 Open-Meteo），那种做法把第三方凭证硬编码进客户端，StarMark 不照搬——
/// 只用 Open-Meteo 一条链路。
/// </para>
/// </summary>
public sealed class OpenMeteoClient : IDisposable
{
    private const string ForecastUrl = "https://api.open-meteo.com/v1/forecast";
    private const string GeocodingUrl = "https://geocoding-api.open-meteo.com/v1/search";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private bool _disposed;

    public OpenMeteoClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    /// <summary>
    /// 构造地理查询 URL。抽出来是为了能脱离网络单测（只校验拼串，不真的发请求）。
    /// </summary>
    public static string BuildGeocodingUrl(string query, int count, string language) =>
        $"{GeocodingUrl}?name={Uri.EscapeDataString(query)}&count={count}&language={Uri.EscapeDataString(language)}&format=json";

    /// <summary>构造预报 URL。<paramref name="forecastDays"/> 含今天。</summary>
    public static string BuildForecastUrl(double latitude, double longitude, int forecastDays)
    {
        var lat = latitude.ToString("G", CultureInfo.InvariantCulture);
        var lon = longitude.ToString("G", CultureInfo.InvariantCulture);
        // current 只取展示需要的字段，少传一点省带宽也省解析
        const string current = "temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m";
        const string daily = "weather_code,temperature_2m_max,temperature_2m_min";
        // 逐时只取温度与天气码：够画"今日逐时"的图标+温度，不多传一列省解析
        const string hourly = "temperature_2m,weather_code";
        return $"{ForecastUrl}?latitude={lat}&longitude={lon}" +
               $"&current={Uri.EscapeDataString(current)}&daily={Uri.EscapeDataString(daily)}" +
               $"&hourly={Uri.EscapeDataString(hourly)}" +
               $"&timezone=auto&forecast_days={forecastDays}";
    }

    /// <summary>按城市名查询（中文可直接查）。失败时返回空列表，绝不把异常冒到 UI 线程。</summary>
    public async Task<List<WeatherCity>> SearchCityAsync(
        string query, int count = 8, string language = "zh", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        try
        {
            var json = await _http.GetStringAsync(BuildGeocodingUrl(query, count, language), ct);
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var arr) ||
                arr.ValueKind != JsonValueKind.Array) return [];

            var list = new List<WeatherCity>();
            foreach (var e in arr.EnumerateArray())
            {
                list.Add(new WeatherCity
                {
                    Id = GetLong(e, "id"),
                    Name = GetString(e, "name"),
                    Latitude = GetDouble(e, "latitude"),
                    Longitude = GetDouble(e, "longitude"),
                    Country = GetString(e, "country"),
                    Admin1 = GetString(e, "admin1"),
                    Timezone = GetString(e, "timezone"),
                });
            }
            return list;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>拉取指定城市的实况 + 多日预报。失败返回 null（调用方据此显示「暂时无法获取」）。</summary>
    public async Task<WeatherReport?> GetReportAsync(
        WeatherCity city, int forecastDays = 4, CancellationToken ct = default)
    {
        if (city is null) return null;
        try
        {
            var url = BuildForecastUrl(city.Latitude, city.Longitude, forecastDays);
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var now = new WeatherNow();
            if (root.TryGetProperty("current", out var cur))
            {
                now.TemperatureC = GetDouble(cur, "temperature_2m");
                now.FeelsLikeC = GetDouble(cur, "apparent_temperature");
                now.Humidity = (int)GetDouble(cur, "relative_humidity_2m");
                now.WindSpeedKmh = GetDouble(cur, "wind_speed_10m");
                now.Code = (int)GetDouble(cur, "weather_code");
                now.IsDay = GetDouble(cur, "is_day") >= 1;
            }

            var days = new List<WeatherDay>();
            if (root.TryGetProperty("daily", out var daily))
            {
                var times = daily.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.Array
                    ? t.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
                    : [];
                var codes = ReadNumberArray(daily, "weather_code");
                var maxs = ReadNumberArray(daily, "temperature_2m_max");
                var mins = ReadNumberArray(daily, "temperature_2m_min");

                for (var i = 0; i < times.Count; i++)
                {
                    // 数组长度理论上一致，但任一缺失都按 0/未知兜底，绝不因越界崩 UI
                    days.Add(new WeatherDay
                    {
                        Date = DateOnly.TryParse(times[i], out var d) ? d : default,
                        Code = i < codes.Count ? (int)codes[i] : 0,
                        MaxC = i < maxs.Count ? maxs[i] : 0,
                        MinC = i < mins.Count ? mins[i] : 0,
                    });
                }
            }

            var hours = new List<WeatherHour>();
            if (root.TryGetProperty("hourly", out var hourly))
            {
                var stamps = hourly.TryGetProperty("time", out var ht) && ht.ValueKind == JsonValueKind.Array
                    ? ht.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
                    : [];
                var hourTemps = ReadNumberArray(hourly, "temperature_2m");
                var hourCodes = ReadNumberArray(hourly, "weather_code");

                for (var i = 0; i < stamps.Count; i++)
                {
                    if (!DateTimeOffset.TryParse(stamps[i], CultureInfo.InvariantCulture, out var when)) continue;
                    hours.Add(new WeatherHour
                    {
                        Time = when,
                        TemperatureC = i < hourTemps.Count ? hourTemps[i] : 0,
                        Code = i < hourCodes.Count ? (int)hourCodes[i] : 0,
                    });
                }
            }
            hours.Sort((a, b) => a.Time.CompareTo(b.Time));

            return new WeatherReport
            {
                City = city,
                Now = now,
                Days = days,
                Hours = hours,
                FetchedAt = DateTimeOffset.Now,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static List<double> ReadNumberArray(JsonElement parent, string name)
    {
        var list = new List<double>();
        if (!parent.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in arr.EnumerateArray())
        {
            // 缺测值 Open-Meteo 会给 null，用 0 兜底（展示层再判空意义不大，0℃ 也不误导）
            list.Add(e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0);
        }
        return list;
    }

    private static string GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static double GetDouble(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static long GetLong(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
