#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace StarMark.Abstractions;

/// <summary>
/// 统一日志门面：每日滚动文件（%LOCALAPPDATA%\StarMark\logs\starmark-YYYYMMDD.log）+ Debug 输出。
/// 线程安全。替代此前分散的 starmark-startup.log / Trace.WriteLine。
/// <para>
/// 片243 起落盘走<b>后台队列</b>：调用线程（常是 UI 线程）只做一次线程安全入队，
/// 专职线程攒批刷盘。此前每条日志＝调用者线程一次同步 open-append-close＋全局锁——
/// 打开设置页一轮 60+ 条全在 UI 线程上打文件 I/O，是全天微卡顿的固定成分。
/// 顺序由单队列 FIFO 保证；进程退出由 <c>ProcessExit</c> 保尾 flush（刷盘线程是 IsBackground，不拦进程走）。
/// </para>
/// </summary>
public static class StarLog
{
    private static readonly object Gate = new();

    // 每批落盘前先把"目录已建"与"当日文件名"记下来：
    // 否则每批都要做一遍环境目录查询 + CreateDirectory + 日期格式化。
    private static bool _dirEnsured;
    private static string? _dayFile;
    private static DateTime _dayFileExpiry;

    // ───────────────────────── 后台写盘队列 ─────────────────────────
    // 无界是有意的：日志风暴（一次打开设置页几十条）只涨内存不阻塞调用者；有界满了丢日志比卡 UI 更糟。
    private const int FlushBatchCap = 256;
    private static readonly BlockingCollection<string> Pending = new();
    private static Thread? _flushThread;
    private static readonly object FlushThreadGate = new();

    static StarLog()
    {
        // 进程退出保尾：IsBackground 的刷盘线程不会拦着进程走，滞留队列必须在这里倒干净——
        // 最后一批往往正是崩溃/退出前那几条最有诊断价值的行。
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { FlushNow(); } catch { /* 保尾失败也不许把退出流程砸了 */ }
        };
    }

    /// <summary>
    /// 日志目录覆盖，<b>只为测试准备</b>：默认写进 <c>%LOCALAPPDATA%\StarMark\logs</c>，
    /// 单测里那些"写失败要降级""来源跳过要记一行"的断言于是也往同一份真机日志里追加行——
    /// 排查真机问题时要先分辨哪几行根本没在真机上发生过。改道临时目录后，这份文件只剩真机的事。
    /// </summary>
    public static string? DirectoryOverride { get; set; }

    public static string LogDirectory
        => DirectoryOverride
           ?? Path.Combine(UserDataPaths.RedirectedDataRoot()
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConstants.AppName), "logs");

    // 锁定 InvariantCulture：th-TH/ar-SA 的 CurrentCulture 用佛历/希吉来历，否则文件名会得到 2569/1447 这类年份，与文档承诺及行内 [O] 时间戳矛盾。
    public static string CurrentLogFile
        => Path.Combine(LogDirectory, $"starmark-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception ex) => Write("ERROR", message, ex);

    /// <summary>
    /// 按 <paramref name="key"/> 节流的 INFO：窗口内首次立即写，其后同 key 只累计，
    /// 到下一个窗口边界连写成一条 "(此前 x N 条未记)"。
    /// </summary>
    public static void InfoThrottled(string key, string message, int windowMs = 60000)
        => WriteThrottled("INFO", key, message, windowMs);

    /// <summary>见 <see cref="InfoThrottled"/>。用于"每次搜索/每次落盘都会重复同一句"的热路径诊断与降级告警。</summary>
    public static void WarnThrottled(string key, string message, int windowMs = 60000)
        => WriteThrottled("WARN", key, message, windowMs);

    private static readonly object ThrottleGate = new();
    private static readonly Dictionary<string, (long Since, int Suppressed)> ThrottleState = new();

    private static void WriteThrottled(string level, string key, string message, int windowMs)
    {
        var (emit, suppressed) = TryThrottle(key, Environment.TickCount64, windowMs);
        if (!emit) return;
        Write(level, suppressed > 0 ? $"{message}（此前 {windowMs / 1000}s 内同类 {suppressed} 条未记）" : message);
    }

    /// <summary>
    /// 节流判定（与落盘分开，便于按契约单测）：<b>窗口内首次立即放行</b>，其后同 key 只累计；
    /// 到下一个窗口边界放行时带上被压条数，故"这事还在反复发生"的证据不丢。
    /// </summary>
    /// <returns>Emit＝本条该不该写；SuppressedBefore＝上次放行以来被压掉的条数（放行时才有意义）。</returns>
    internal static (bool Emit, int SuppressedBefore) TryThrottle(string key, long nowMs, int windowMs)
    {
        // key 必须是**有界集合**（源 Id、材质 kind、文件路径）——拿用户输入当 key 会让字典无界增长。
        lock (ThrottleGate)
        {
            var known = ThrottleState.TryGetValue(key, out var st);
            if (known && nowMs - st.Since < windowMs)
            {
                ThrottleState[key] = (st.Since, st.Suppressed + 1);
                return (false, 0);
            }
            ThrottleState[key] = (nowMs, 0);
            return (true, known ? st.Suppressed : 0);
        }
    }

    private static void Write(string level, string message, Exception? ex = null)
    {
        // 主事件行恒为一行（消息内的 CR/LF 折成字面量）；异常堆栈另起、逐行缩进续写。
        var body = ex is null ? SanitizeMessage(message) : SanitizeMessage(message) + StackTail(ex);
        var line = $"[{DateTimeOffset.Now:O}] {level} {body}";
        EnqueueOrAppend(line);
        Debug.WriteLine($"[StarMark] {level} {body}");
    }

    /// <summary>调用线程只入队（纳秒级）；队列不可用时才退回同步落盘——宁可慢，不可丢。</summary>
    private static void EnqueueOrAppend(string line)
    {
        try
        {
            EnsureFlushThread();
            Pending.Add(line);
            return;
        }
        catch { }
        AppendLines(new[] { line });
    }

    private static void EnsureFlushThread()
    {
        if (_flushThread is not null) return;
        lock (FlushThreadGate)
        {
            if (_flushThread is not null) return;
            _flushThread = new Thread(FlushLoop)
            {
                IsBackground = true,
                Name = "StarLog-Flush",
            };
            _flushThread.Start();
        }
    }

    private static void FlushLoop()
    {
        var batch = new List<string>(FlushBatchCap);
        while (true)
        {
            batch.Clear();
            // 无日志时阻塞等首条（零 CPU）；首条到手后零超时收割余量攒成一批，一次开-写-关写完整批
            if (!Pending.TryTake(out var first, Timeout.Infinite)) continue;
            batch.Add(first);
            while (batch.Count < FlushBatchCap && Pending.TryTake(out var more, 0)) batch.Add(more);
            AppendLines(batch);
        }
    }

    /// <summary>把滞留队列倒干净并同步落盘：进程退出保尾用；单测也用它把「已入队」钉成「已到盘上」再断言。</summary>
    internal static void FlushNow()
    {
        lock (Gate)
        {
            var rest = new List<string>();
            while (Pending.TryTake(out var line, 0)) rest.Add(line);
            if (rest.Count > 0) AppendCore(rest);
        }
    }

    private static void AppendLines(IReadOnlyList<string> lines)
    {
        lock (Gate)
        {
            AppendCore(lines);
        }
    }

    private static void AppendCore(IReadOnlyList<string> lines)
    {
        try
        {
            var dir = LogDirectory;
            var now = DateTime.Now;
            // 缓存的当日档只在「同一目录」内有效：LogDirectory 变了（测试改道目录切换、数据根变化）
            // 还用旧缓存路径＝整批写向已不存在的目录、被自愈吞掉。换目录即作废，重走建目录。
            if (_dayFile is null || now >= _dayFileExpiry || Path.GetDirectoryName(_dayFile) != dir)
            {
                _dirEnsured = false;
                _dayFile = Path.Combine(dir,
                    $"starmark-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
                _dayFileExpiry = now.Date.AddDays(1);   // 跨零点自动换档
            }
            if (!_dirEnsured)
            {
                Directory.CreateDirectory(dir);
                _dirEnsured = true;
            }
            File.AppendAllText(_dayFile, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }
        catch
        {
            // 日志失败不影响主流程；但作废缓存，下一行会重试建目录（目录被手工删过也能自愈）
            _dirEnsured = false;
            _dayFile = null;
        }
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