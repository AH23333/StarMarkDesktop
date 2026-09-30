#nullable enable
using System;
using System.IO;
using System.Runtime.CompilerServices;
using StarMark.Abstractions;

namespace StarMark.Tests;

/// <summary>
/// 单测的两条改道：日志与用户数据档都不许落在他的真实目录里。
/// <para>
/// 日志那条（批次 PL）：不指走的话 <c>dotnet test</c> 每跑一遍就往
/// <c>%LOCALAPPDATA%\StarMark\logs</c> 追加几百行。那份文件是按"真机发生过什么"来读的
/// （判断"有会话开始、无进程退出"＝异常终止就靠它），混进测试事件后每次排查都得先分辨一遍。
/// </para>
/// <para>
/// 数据那条（批次 SS，实测撞出来的）：<c>WidgetStorage.DefaultPath()</c> /
/// <c>SettingsStore</c> / <c>RssCacheStore</c> / <c>GitHubOptions</c> 都按
/// <c>STARMARK_SETTINGS_PATH &gt; STARMARK_DB_PATH 同目录 &gt; %APPDATA%\StarMark</c> 解路径，
/// 而备份还原在 <c>widgetsTargetPath</c> 传 null 时写的就是这个默认路径。
/// 于是 <c>dotnet test</c> 每跑一遍都把他真实的 <c>widgets.json</c> 整档覆盖一次
/// （连带 <c>.bak</c>、<c>settings.json</c>、<c>github.json</c>）。今天内容恰好是他自己那份的往返，
/// 看不出破坏；换一台机器／换一份载荷就是"跑一次测试，桌面组件全变样"。
/// 更要紧的是<b>读</b>的那一半：导出会把真档读进载荷，测试的结论于是取决于他桌面上摆着什么。
/// </para>
/// <para>指到 <c>%TEMP%\StarMark.Tests\</c> 之后，测试对档内容的断言一字不改，真目录一个字节不动。</para>
/// </summary>
internal static class TestSandboxRedirect
{
    /// <summary>单测的沙盒根目录；测试用它断言"默认路径没指回真实用户目录"。</summary>
    public static string Root => Path.Combine(Path.GetTempPath(), "StarMark.Tests");

    [ModuleInitializer]
    internal static void Redirect()
    {
        StarLog.DirectoryOverride = Path.Combine(Root, "logs");
        // 这一处同时带走 starmark.db / settings.json / widgets.json / rss-cache.json / github.json
        // （那几个 DefaultPath 全都回落到"数据库同目录"）。
        Environment.SetEnvironmentVariable("STARMARK_DB_PATH", Path.Combine(Root, "starmark.db"));
    }
}
