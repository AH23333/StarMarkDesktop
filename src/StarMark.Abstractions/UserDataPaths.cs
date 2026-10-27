#nullable enable
using System;
using System.IO;

namespace StarMark.Abstractions;

/// <summary>
/// 用户数据档落在哪：整仓只有这一处约定（批次 SS；片23 起画板那一族的子目录也从这里出）。
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

    /// <summary>
    /// 用户数据根之下的<b>一颗子目录</b>（画板那一族的档住 <c>boards\</c>，导出物住在它下面）。
    /// <para>为什么一颗目录出口也要走这里：档名归 <c>BoardPolicy</c>、落点归 UI（深析 §21.3 那句"路径⛔ 是判据"），
    /// 但"<b>根在哪</b>"这件事早就有主人了。UI 侧自己拼一份 <c>%APPDATA%\StarMark</c>，上面那条优先规则
    /// 对它照样无效——沙盒改道带不走画板档，一次取证就把他的真目录写脏（批次 SS 撞过的正是这一族）。</para>
    /// </summary>
    public static string Subdirectory(string name) => Path.Combine(RootDirectory(), name);

    /// <summary>
    /// 用户数据那棵根<b>有没有</b>被环境变量改道；没改道就交 null（片39）。
    /// <para>为什么要单独问这一格：仓里另有一族产物（画板导出与那颗救回来的副本）平时<b>⛔ 住在这棵根下面</b>，
    /// 而一旦改道，它必须跟着走——否则一次沙盒取证就往用户真目录里写东西（批次 SS 撞过的同一族）。
    /// 让它自己读那两颗环境变量＝第二份改道规则；所以这里把<b>同一颗规则</b>的答案递出去。
    /// ⚠ 没改道时交 null 而⛔ 交 <c>%APPDATA%\StarMark</c>：那一棵是"程序内部档"的根，
    /// 产物族需要的是「有没有被改道」这一颗<b>信号</b>，⛔ 是"默认落哪一棵"（那一句归 <c>ProductsDirectory</c> 裁）。</para>
    /// </summary>
    public static string? RedirectedDataRoot()
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

        return null;
    }

    /// <summary>
    /// 用户的<b>产物</b>（截图、画板导出与那颗救回来的副本）落进哪一颗目录——✅P-259 (B) 的落地腿。
    /// <para>三件事挤在同一颗判据里：<b>①根被改道时跟着走</b>（沙盒取证、单测改道，⛔ 往用户真目录里写——
    /// 批次 SS 撞过的那一族；截图那一支从前只拼 <c>图片\StarMark 截图</c>，那颗信号<b>⛔ 参与</b>，
    /// 一次沙盒取证就往他真图片目录里写一颗 PNG）。<b>②旧的名册在就续用</b>：已经在「StarMark 截图」／
    /// 「StarMark 画板」里的档是用户的东西，目录一换名字＝已有档当场孤儿化。<b>③没有旧名册时落
    /// <paramref name="newName"/></b>：他裁的 (B)——新落点 ASCII 中性名（目录名是盘上的<b>身份</b>而⛔ 措辞，
    /// 一句上屏话换三种语言＝三颗目录；日文／英文用户拿不到他那门话念不出来的目录名）。</para>
    /// <para>名字由各家递进来（截图那对住 <c>ScreenshotService</c>、画板那对住 <c>BoardPolicy</c>），
    /// 而根的改道信号在这里问——「拿哪一棵、怎么拼」只此一处：截图与画板各走一头＝同一颗根被拼两遍。</para>
    /// </summary>
    public static string ProductsDirectory(
        string picturesRoot, string? redirectedDataRoot, string legacyName, string newName)
    {
        var root = redirectedDataRoot ?? picturesRoot;
        var legacy = Path.Combine(root, legacyName);
        return Directory.Exists(legacy) ? legacy : Path.Combine(root, newName);
    }

    private static string InRoot(string fileName) => Path.Combine(RootDirectory(), fileName);

    private static string RootDirectory() => RedirectedDataRoot() ?? AppDataDirectory();

    private static string AppDataDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppConstants.AppName);
}
