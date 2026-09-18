#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Core.Performance;
using StarMark.Integrations.Weather;

namespace StarMark.UI.Helpers;

/// <summary>主题偏好。</summary>
public enum ThemePreference
{
    Default = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>
/// 用户设置持久化（目前仅主题偏好）。
/// 存储位置：STARMARK_SETTINGS_PATH > 数据库同目录(开发期跟随 STARMARK_DB_PATH) > %APPDATA%\StarMark\settings.json。
/// </summary>
public sealed class SettingsStore : IPerformanceSettingsSource
{
    private readonly string _path;
    private sealed class SettingsData
    {
        public int Theme { get; set; }
        public bool? EnableTray { get; set; }
        public bool? EnableGlobalHotKey { get; set; }
        public bool? MinimizeToTray { get; set; }
        /// <summary>本地文件索引根目录（P0-1b）。null/空表示使用默认（桌面/下载/文档）。</summary>
        public List<string>? FileIndexRoots { get; set; }
        /// <summary>每目录索引数量上限（P0-1b）。≤0 表示使用默认 5000。</summary>
        public int? MaxFileIndexCount { get; set; }
        /// <summary>快捷键绑定（动作 id → 手势）的 JSON。缺省时使用 <see cref="DefaultHotkeyBindings"/>。</summary>
        public string? HotkeyBindingsJson { get; set; }
        /// <summary>组件拖动 / 缩放时的边缘磁吸总开关（默认开启）。关闭后用户可自由摆位。</summary>
        public bool? WidgetSnapEnabled { get; set; }
        /// <summary>半透明材质：0=亚克力 1=云母 2=不透明（默认 0）。</summary>
        public int? WidgetBackdrop { get; set; }
        /// <summary>组件背景不透明度 0.3–1.0（默认 0.72），配合半透明材质使用。</summary>
        public double? WidgetOpacity { get; set; }
        /// <summary>毛玻璃材质浓度 0–1（默认 0.65，DeskBox 的 WidgetMaterialIntensity）。</summary>
        public double? WidgetMaterialIntensity { get; set; }
        /// <summary>主窗口是否也使用同一套半透明材质（默认开启）。</summary>
        public bool? MainWindowTranslucent { get; set; }
        /// <summary>性能模式：0=均衡（默认）1=省资源 2=自定义。常驻应用的内存/缓存预算开关。</summary>
        public int? PerformanceMode { get; set; }
        /// <summary>自定义性能模式下的进程工作集预算（MB，默认 200）。超预算时 MemoryReclaimer 触发回收。</summary>
        public double? CacheBudgetMb { get; set; }
        /// <summary>自定义性能模式下有界缓存的最大条目数（默认 256）。</summary>
        public int? MaxImageCacheCount { get; set; }
        /// <summary>天气组件所选城市（JSON 序列化的 WeatherCity）。null 表示未选，组件会提示先选城市。</summary>
        public string? WeatherCityJson { get; set; }
    }

    public SettingsStore(string? path = null) => _path = path ?? ResolveSettingsPath();

    private SettingsData? Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(_path));
        }
        catch { }
        return null;
    }

    private void Save(SettingsData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(data));
        }
        catch { }
    }

    public ThemePreference LoadTheme() => Load() is { } d && Enum.IsDefined(typeof(ThemePreference), d.Theme)
        ? (ThemePreference)d.Theme
        : ThemePreference.Default;

    public void SaveTheme(ThemePreference pref)
    {
        var d = Load() ?? new SettingsData();
        d.Theme = (int)pref;
        Save(d);
    }

    /// <summary>托盘常驻总开关（默认开启）。</summary>
    public bool LoadEnableTray() => Load() is { } d ? d.EnableTray ?? true : true;

    public void SaveEnableTray(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.EnableTray = enabled;
        Save(d);
    }

    /// <summary>全局呼出热键开关（默认开启，Ctrl+Alt+Space）。</summary>
    public bool LoadEnableGlobalHotKey() => Load() is { } d ? d.EnableGlobalHotKey ?? true : true;

    public void SaveEnableGlobalHotKey(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.EnableGlobalHotKey = enabled;
        Save(d);
    }

    /// <summary>关闭按钮最小化到托盘（默认开启）。</summary>
    public bool LoadMinimizeToTray() => Load() is { } d ? d.MinimizeToTray ?? true : true;

    public void SaveMinimizeToTray(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.MinimizeToTray = enabled;
        Save(d);
    }

    /// <summary>组件边缘磁吸总开关（默认开启）。关闭后拖动/缩放都不再自动贴合。</summary>
    public bool LoadWidgetSnapEnabled() => Load() is { } d ? d.WidgetSnapEnabled ?? true : true;

    public void SaveWidgetSnapEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetSnapEnabled = enabled;
        Save(d);
    }

    /// <summary>
    /// 组件窗口背景材质（默认亚克力）。
    /// 注意：旧版 settings.json 没有该字段（WidgetBackdrop 为 null），
    /// 此处必须先取值的空合并结果再校验枚举，绝不能直接对可空值取 .Value
    /// （历史 bug：先 `Enum.IsDefined(typeof(...), d.WidgetBackdrop ?? 0)` 通过后再 `d.WidgetBackdrop!.Value`
    /// 会对 null 解包，抛 "Nullable object must have a value"，导致设置页导航与组件显示直接崩溃）。
    /// </summary>
    public WidgetBackdropKind LoadWidgetBackdrop()
    {
        if (Load() is { } d && d.WidgetBackdrop is { } raw
            && Enum.IsDefined(typeof(WidgetBackdropKind), raw))
            return (WidgetBackdropKind)raw;
        return WidgetBackdropKind.Acrylic;
    }

    public void SaveWidgetBackdrop(WidgetBackdropKind kind)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetBackdrop = (int)kind;
        Save(d);
    }

    /// <summary>组件背景不透明度（默认 0.72）。越接近 1 越不透明。</summary>
    public double LoadWidgetOpacity()
    {
        if (Load() is { } d && d.WidgetOpacity is > 0)
            return Math.Clamp(d.WidgetOpacity.Value, 0.3, 1.0);
        return WidgetAppearance.DefaultOpacity;
    }

    public void SaveWidgetOpacity(double opacity)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetOpacity = Math.Clamp(opacity, 0.3, 1.0);
        Save(d);
    }

    /// <summary>毛玻璃材质浓度（默认 0.65）：控制亚克力的染色/光亮度，值越大越「实」。</summary>
    public double LoadWidgetMaterialIntensity()
    {
        if (Load() is { } d && d.WidgetMaterialIntensity is >= 0)
            return Math.Clamp(d.WidgetMaterialIntensity.Value, 0.0, 1.0);
        return 0.65;
    }

    public void SaveWidgetMaterialIntensity(double intensity)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetMaterialIntensity = Math.Clamp(intensity, 0.0, 1.0);
        Save(d);
    }

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

    /// <summary>主窗口是否跟随同样的半透明材质（默认开启）。</summary>
    public bool LoadMainWindowTranslucent() => Load() is { } d ? d.MainWindowTranslucent ?? true : true;

    public void SaveMainWindowTranslucent(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.MainWindowTranslucent = enabled;
        Save(d);
    }

    /// <summary>性能模式（默认均衡）。旧版 settings.json 无该字段时回退均衡。</summary>
    public PerformanceMode LoadPerformanceMode()
    {
        if (Load() is { } d && d.PerformanceMode is { } raw && Enum.IsDefined(typeof(PerformanceMode), raw))
            return (PerformanceMode)raw;
        return PerformanceMode.Balanced;
    }

    public void SavePerformanceMode(PerformanceMode mode)
    {
        var d = Load() ?? new SettingsData();
        d.PerformanceMode = (int)mode;
        Save(d);
    }

    /// <summary>自定义模式下的进程工作集预算（MB，默认 200）。</summary>
    public double LoadCacheBudgetMb()
    {
        if (Load() is { } d && d.CacheBudgetMb is > 0)
            return Math.Clamp(d.CacheBudgetMb.Value, 32.0, 4096.0);
        return 200.0;
    }

    public void SaveCacheBudgetMb(double mb)
    {
        var d = Load() ?? new SettingsData();
        d.CacheBudgetMb = Math.Clamp(mb, 32.0, 4096.0);
        Save(d);
    }

    /// <summary>自定义模式下的有界缓存最大条目数（默认 256）。</summary>
    public int LoadMaxImageCacheCount()
    {
        if (Load() is { } d && d.MaxImageCacheCount is > 0)
            return Math.Clamp(d.MaxImageCacheCount.Value, 16, 4096);
        return 256;
    }

    public void SaveMaxImageCacheCount(int count)
    {
        var d = Load() ?? new SettingsData();
        d.MaxImageCacheCount = Math.Clamp(count, 16, 4096);
        Save(d);
    }

    /// <summary>本地文件索引根目录（P0-1b）。未配置时返回默认（桌面/下载/文档中存在的目录）。</summary>
    public IReadOnlyList<string> LoadFileIndexRoots()
    {
        var d = Load();
        if (d?.FileIndexRoots is { Count: > 0 } list)
            return list.Where(Directory.Exists).ToList();
        return DefaultFileIndexRoots();
    }

    public void SaveFileIndexRoots(IReadOnlyList<string> roots)
    {
        var d = Load() ?? new SettingsData();
        d.FileIndexRoots = roots.Where(Directory.Exists).Distinct().ToList();
        Save(d);
    }

    /// <summary>每目录索引数量上限（P0-1b）。未配置或非法时返回默认 5000。</summary>
    public int LoadMaxFileIndexCount()
        => Load() is { } d && d.MaxFileIndexCount is > 0 ? d.MaxFileIndexCount.Value : 5000;

    public void SaveMaxFileIndexCount(int count)
    {
        var d = Load() ?? new SettingsData();
        d.MaxFileIndexCount = count > 0 ? count : 5000;
        Save(d);
    }

    private static List<string> DefaultFileIndexRoots()
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        return candidates.Where(Directory.Exists).Distinct().ToList();
    }

    /// <summary>快捷键绑定：默认 + 已保存合并。未配置动作回退到默认手势（缺省为无）。</summary>
    public IReadOnlyDictionary<string, HotkeyGesture> GetHotkeyBindings()
    {
        var merged = DefaultHotkeyBindings();
        var d = Load();
        if (d?.HotkeyBindingsJson is { } json)
        {
            try
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, HotkeyGesture>>(json);
                if (saved is not null) foreach (var kv in saved) merged[kv.Key] = kv.Value;
            }
            catch { }
        }
        return merged;
    }

    /// <summary>保存快捷键绑定（动作 id → 手势）。</summary>
    public void SaveHotkeyBindings(IReadOnlyDictionary<string, HotkeyGesture> bindings)
    {
        var d = Load() ?? new SettingsData();
        d.HotkeyBindingsJson = JsonSerializer.Serialize(bindings);
        Save(d);
    }

    /// <summary>默认快捷键：主界面呼出/关闭 = Ctrl+Alt+Space（沿用原有全局热键）。</summary>
    public static Dictionary<string, HotkeyGesture> DefaultHotkeyBindings()
    {
        var m = new Dictionary<string, HotkeyGesture>
        {
            [HotkeyActions.MainToggle] = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, (uint)Windows.System.VirtualKey.Space),
        };
        return m;
    }

    public static string ResolveSettingsPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("STARMARK_SETTINGS_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        // 开发期跟随数据库路径所在目录，避免沙盒拒绝 %APPDATA% 写入
        var dbPath = Environment.GetEnvironmentVariable("STARMARK_DB_PATH");
        if (!string.IsNullOrWhiteSpace(dbPath))
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dir)) return Path.Combine(dir, "settings.json");
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, StarMark.Abstractions.AppConstants.AppName, "settings.json");
    }
}