#nullable enable
using System;
using System.IO;

namespace StarMark.Abstractions;

/// <summary>
/// 用户数据档落在哪：整仓只有这一处约定（批次 SS）。
/// <para>
/// 为什么要有它：<c>starmark.db</c> / <c>settings.json</c> / <c>widgets.json</c> /
/// <c>rss-cache.json</c> / <c>github.json</c> 这五份档，此前各有各的一套"读环境变量、否则 %APPDATA%"，
/// 而五份抄本里有一份（<c>GitHubOptions.DefaultConfigPath</c>）漏掉了环境变量那一档。漏掉的那一份不会报错，
/// 它只是<b>永远写到真目录去</b>：单测改道、沙盒取证、以及任何"把用户档整体搬走"的做法对它都无效
/// ——而这套约定存在的理由恰好就是那两个用途。
/// </para>
/// <para>
/// 优先级：<c>STARMARK_SETTINGS_PATH</c>（它的值是 settings.json 的<b>整条路径</b>，其余各档取它所在目录）
/// ＞ <c>STARMARK_DB_PATH</c>（取它所在目录）＞ <c>%APPDATA%\StarMark</c>。
/// 数据库档自己只认 <see cref="DatabaseVariable"/>：拿 settings 的路径顶掉它，等于让一份别的库上的档
/// 把主库位置改掉。
/// </para>
/// </summary>
public static class UserDataPaths
{
    /// <summary>把整包用户数据指到别处（沙盒取证与单测改道用的就是它）。</summary>
    public const string DatabaseVariable = "STARMARK_DB_PATH";

    /// <summary>只把设置档指走；它的值是 settings.json 的完整路径，其余各档跟着它的目录走。</summary>
    public const string SettingsVariable = "STARMARK_SETTINGS_PATH";

    /// <summary>数据库档本身：只认 <see cref="DatabaseVariable"/>，不被 settings 的路径顶掉。</summary>
    public static string Database()
        => Environment.GetEnvironmentVariable(DatabaseVariable)
            ?? Path.Combine(AppDataDirectory(), "starmark.db");

    /// <summary>settings.json：环境变量给整条路径时原样用它（历史约定，改成取目录会顶掉用户自设的设置档路径）。</summary>
    public static string Settings()
    {
        var settingsOverride = Environment.GetEnvironmentVariable(SettingsVariable);
        return string.IsNullOrWhiteSpace(settingsOverride) ? InRoot("settings.json") : settingsOverride!;
    }

    /// <summary>与数据库同目录的其它档（widgets.json / rss-cache.json / github.json 都走这里）。</summary>
    public static string Sibling(string fileName) => InRoot(fileName);

    private static string InRoot(string fileName) => Path.Combine(RootDirectory(), fileName);

    private static string RootDirectory()
    {
        var settingsOverride = Environment.GetEnvironmentVariable(SettingsVariable);
        if (!string.IsNullOrWhiteSpace(settingsOverride))
            return Path.GetDirectoryName(settingsOverride) ?? ".";

        var dbOverride = Environment.GetEnvironmentVariable(DatabaseVariable);
        if (!string.IsNullOrWhiteSpace(dbOverride))
        {
            var dir = Path.GetDirectoryName(dbOverride);
            if (!string.IsNullOrWhiteSpace(dir)) return dir;
        }

        return AppDataDirectory();
    }

    private static string AppDataDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppConstants.AppName);
}
