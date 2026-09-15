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
    private sealed class SettingsData { public int Theme { get; set; } }

    public SettingsStore(string? path = null) => _path = path ?? ResolveSettingsPath();

    public ThemePreference LoadTheme()
    {
        try
        {
            if (File.Exists(_path))
            {
                var data = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(_path));
                if (data != null && Enum.IsDefined(typeof(ThemePreference), data.Theme))
                    return (ThemePreference)data.Theme;
            }
        }
        catch { }
        return ThemePreference.Default;
    }

    public void SaveTheme(ThemePreference pref)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new SettingsData { Theme = (int)pref }));
        }
        catch { }
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