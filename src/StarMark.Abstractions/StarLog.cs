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

    // 每条日志都要落一次盘，热路径上先把"目录已建"与"当日文件名"记下来：
    // 否则每行都会做一遍环境目录查询 + CreateDirectory + 日期格式化。
    private static bool _dirEnsured;
    private static string? _dayFile;
    private static DateTime _dayFileExpiry;

    public static string LogDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConstants.AppName, "logs");

    // 锁定 InvariantCulture：th-TH/ar-SA 的 CurrentCulture 用佛历/希吉来历，否则文件名会得到 2569/1447 这类年份，与文档承诺及行内 [O] 时间戳矛盾。
    public static string CurrentLogFile
        => Path.Combine(LogDirectory, $"starmark-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception ex) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex = null)
    {
        // 主事件行恒为一行（消息内的 CR/LF 折成字面量）；异常堆栈另起、逐行缩进续写。
        var body = ex is null ? SanitizeMessage(message) : SanitizeMessage(message) + StackTail(ex);
        var line = $"[{DateTimeOffset.Now:O}] {level} {body}";
        lock (Gate)
        {
            try
            {
                if (!_dirEnsured)
                {
                    Directory.CreateDirectory(LogDirectory);
                    _dirEnsured = true;
                }
                var now = DateTime.Now;
                if (_dayFile is null || now >= _dayFileExpiry)
                {
                    _dayFile = Path.Combine(LogDirectory,
                        $"starmark-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
                    _dayFileExpiry = now.Date.AddDays(1);   // 跨零点自动换档
                }
                File.AppendAllText(_dayFile, line + Environment.NewLine);
            }
            catch
            {
                // 日志失败不影响主流程；但作废缓存，下一行会重试建目录（目录被手工删过也能自愈）
                _dirEnsured = false;
                _dayFile = null;
            }
        }
        Debug.WriteLine($"[StarMark] {level} {body}");
    }

    /// <summary>
    /// 防日志伪造/注入（CWE-117）：把**消息主体**内嵌的换行折叠成字面量 <c>\n</c>，保证"一条日志事件的主行=一行物理记录"。
    /// 日志消息常内插外部可控串（书签标题、拖入路径、URI），若原样落 CR/LF 即可另起一行、伪造出
    /// <c>[时间戳] LEVEL …</c> 条目、污染审计与任何按行锚定 <c>^[</c> 解析日志的下游。无换行的消息原样返回（零行为变更）。
    /// 注：异常堆栈不经过此函数（见 <see cref="StackTail"/>），以保留其多行可读性。
    /// </summary>
    internal static string SanitizeMessage(string message)
    {
        if (message.Length == 0 || message.IndexOfAny(CrLf) < 0) return message;
        // 先 CRLF→单个字面量，再处理残留的孤立 CR / LF，避免一次换行被转义成两段。
        return message.Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n");
    }

    /// <summary>
    /// 把异常渲染为**缩进续行块**：堆栈保留多行可读性，但每一行前缀 <c>____</c>（四空格），
    /// 使其永不以 <c>[</c> 起始——因而任何一行（含 <c>ex.Message</c> 里攻击者伪造的 <c>\n[2099…] INFO evil</c>）
    /// 都不会被按行锚定 <c>^[时间戳]</c> 的解析器误认成新日志条目。既有防伪、又不牺牲栈可读性。
    /// </summary>
    internal static string StackTail(Exception ex)
    {
        var text = ex.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
        return "\n" + string.Join("\n", text.Split('\n').Select(l => "    " + l));
    }

    private static readonly char[] CrLf = { '\r', '\n' };
}