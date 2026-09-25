#nullable enable
using System.IO;
using System.Runtime.CompilerServices;
using StarMark.Abstractions;

namespace StarMark.Tests;

/// <summary>
/// 单测的日志改道：不指走的话，<c>dotnet test</c> 每跑一遍就往用户真实的
/// <c>%LOCALAPPDATA%\StarMark\logs</c> 里追加几百行——那份文件是按"真机发生过什么"来读的
/// （判断"有会话开始、无进程退出"＝异常终止就靠它），混进测试事件后每次排查都得先分辨一遍。
/// 指到临时目录后真机日志只剩真机的事，测试里对日志内容的断言一字不改。
/// </summary>
internal static class TestLogRedirect
{
    [ModuleInitializer]
    internal static void Redirect()
        => StarLog.DirectoryOverride = Path.Combine(Path.GetTempPath(), "StarMark.Tests", "logs");
}
