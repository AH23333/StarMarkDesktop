#nullable enable
using System.IO;
using System.Text.Json;

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
public sealed class SettingsStore
{
    private readonly string _path;
    private sealed class SettingsData
    {
        public int Theme { get; set; }
        public bool? EnableTray { get; set; }
        public bool? EnableGlobalHotKey { get; set; }
        public bool? MinimizeToTray { get; set; }
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
        return Path.Combine(appData, "StarMark", "settings.json");
    }
}