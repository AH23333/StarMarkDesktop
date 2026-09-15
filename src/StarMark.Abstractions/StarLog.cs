#nullable enable
using System.Diagnostics;

namespace StarMark.Abstractions;

/// <summary>
/// 统一日志门面：每日滚动文件（%LOCALAPPDATA%\StarMark\logs\starmark-YYYYMMDD.log）+ Debug 输出。
/// 线程安全。替代此前分散的 starmark-startup.log / Trace.WriteLine。
/// </summary>
public static class StarLog
{
    private static readonly object Gate = new();

    public static string LogDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarMark", "logs");

    public static string CurrentLogFile
        => Path.Combine(LogDirectory, $"starmark-{DateTime.Now:yyyyMMdd}.log");

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