#nullable enable
using System.Diagnostics;
using System.Globalization;

namespace StarMark.Abstractions;

/// <summary>
/// 统一日志门面：每日滚动文件（%LOCALAPPDATA%\StarMark\logs\starmark-YYYYMMDD.log）+ Debug 输出。
/// 线程安全。替代此前分散的 starmark-startup.log / Trace.WriteLine。
/// </summary>
public static class StarLog
{
    private static readonly object Gate = new();

    public static string LogDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConstants.AppName, "logs");

    // 锁定 InvariantCulture：th-TH/ar-SA 的 CurrentCulture 用佛历/希吉来历，否则文件名会得到 2569/1447 这类年份，与文档承诺及行内 [O] 时间戳矛盾。
    public static string CurrentLogFile
        => Path.Combine(LogDirectory, $"starmark-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception ex) => Write("ERROR", $"{message} {ex}");

    private static void Write(string level, string message)
    {
        var line = $"[{DateTimeOffset.Now:O}] {level} {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(CurrentLogFile, line);
            }
            catch { /* 日志失败不影响主流程 */ }
            Debug.WriteLine($"[StarMark] {level} {message}");
        }
    }
}