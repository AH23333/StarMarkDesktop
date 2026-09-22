#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;

namespace StarMark.Integrations.Everything;

/// <summary>
/// Everything SDK 1.4 P/Invoke 封装。
/// 通过 WM_COPYDATA 向 Everything 主程序窗口发送查询请求。
/// 不依赖第三方 NuGet 包，直接调用 Everything SDK 的 IPC 协议。
/// 对应技术文档 §6.3：SDK 一次只能处理一个查询。
/// </summary>
internal static class EverythingInterop
{
    [Flags]
    public enum RequestFlags : uint
    {
        // 数值 = 官方 SDK 头文件 include/Everything.h 的 EVERYTHING_REQUEST_* 常量（1.4）
        FileName        = 0x00000001,
        Path            = 0x00000002,
        FullPath        = 0x00000004,
        Size            = 0x00000010,
        DateModified    = 0x00000040,
        DateCreated     = 0x00000020,
    }

    // ===== Everything 主程序窗口检测 =====
    private const string EverythingWindowClass = "EVERYTHING_TASKBAR_NOTIFICATION";

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowW(string lpClassName, string? lpWindowName);

    // ===== Everything SDK（Everything64.dll）P/Invoke =====
    // SDK 是官方 IPC 包装（内部 SendMessageTimeout，官方注明线程安全）；需要 Everything 主程序在后台运行。
    private static bool _sdkLoaded;
    private static Exception? _sdkLoadError;
    private static readonly object SdkLoadGate = new();

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern void Everything_SetSearchW(string lpString);

    [DllImport("Everything64.dll", SetLastError = false)]
    private static extern void Everything_SetMax(uint dwMax);

    [DllImport("Everything64.dll", SetLastError = false)]
    private static extern void Everything_SetRequestFlags(uint dwRequestFlags);

    [DllImport("Everything64.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Everything_QueryW([MarshalAs(UnmanagedType.Bool)] bool bWait);

    [DllImport("Everything64.dll", SetLastError = false)]
    private static extern void Everything_Reset();

    [DllImport("Everything64.dll", SetLastError = false)]
    private static extern uint Everything_GetNumResults();

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern uint Everything_GetResultFullPathNameW(uint dwIndex, System.Text.StringBuilder wbuf, uint wbufSizeInWchars);

    [DllImport("Everything64.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Everything_GetResultSize(uint dwIndex, out long lpSize);

    [DllImport("Everything64.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Everything_GetResultDateModified(uint dwIndex, out long lpDateModified);

    [DllImport("Everything64.dll", SetLastError = false)]
    private static extern uint Everything_GetLastError();

    // Everything_Startup 只在 Everything **1.5** SDK 里存在；**1.4 SDK 无此导出**（官方 SDK 参考仅列 Everything_Cleanup，
    // 1.4 直接 Everything_Query 即可）。若用 [DllImport] 硬绑，会在正常的 1.4 Everything64.dll 上抛
    // EntryPointNotFoundException，令 SDK 整体加载失败、本地文件搜索恒空（用户 1.4.1.x 实测即此）。
    // 故不静态声明，改由 EnsureSdkLoaded 按导出符号「探测式」可选调用。
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EverythingStartupFn();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpLibFileName);

    /// <summary>SDK DLL 缓存位置：%LOCALAPPDATA%\StarMark\sdk\Everything64.dll。</summary>
    internal static string SdkDllPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StarMark.Abstractions.AppConstants.AppName, "sdk");
            return Path.Combine(dir, "Everything64.dll");
        }
    }

    /// <summary>
    /// 加载 Everything64.dll：查找顺序为应用目录 → SDK 缓存目录。
    /// 用 NativeLibrary 预加载后，后续按模块名的 DllImport 即解析到该模块。
    /// </summary>
    public static bool EnsureSdkLoaded()
    {
        if (_sdkLoaded) return true;
        lock (SdkLoadGate)
        {
            if (_sdkLoaded) return true;
            if (_sdkLoadError is not null) return false;
            try
            {
                var candidates = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "Everything64.dll"),
                    SdkDllPath,
                };
                var path = candidates.FirstOrDefault(File.Exists);
                if (path is null)
                {
                    _sdkLoadError = new FileNotFoundException("Everything64.dll 未找到（SDK 尚未下载）");
                    return false;
                }

                if (!System.Runtime.InteropServices.NativeLibrary.TryLoad(path, out var module))
                {
                    _sdkLoadError = new InvalidOperationException($"Everything64.dll 加载失败：{path}");
                    StarLog.Error(_sdkLoadError.Message);
                    return false;
                }

                // 仅 1.5 SDK 需要显式初始化 IPC 接收端；1.4 SDK 无 Everything_Startup 导出、直接查询即可。
                // 探测到该导出才调用（并据返回值判失败），否则跳过——两代 SDK 兼容，且不再在 1.4 上抛 EntryPointNotFound。
                if (System.Runtime.InteropServices.NativeLibrary.TryGetExport(module, "Everything_Startup", out var startupPtr))
                {
                    var startup = System.Runtime.InteropServices.Marshal
                        .GetDelegateForFunctionPointer<EverythingStartupFn>(startupPtr);
                    if (!startup())
                    {
                        var err = Everything_GetLastError();
                        _sdkLoadError = new InvalidOperationException($"Everything_Startup 失败（GetLastError={err}：{DescribeSdkError(err)}）");
                        StarLog.Error(_sdkLoadError.Message);
                        return false;
                    }
                }

                _sdkModule = module;
                _sdkLoaded = true;
                return true;
            }
            catch (Exception ex)
            {
                _sdkLoadError = ex;
                StarLog.Error("加载 Everything SDK 失败", ex);
                return false;
            }
        }
    }

    private static IntPtr _sdkModule;

    /// <summary>
    /// 清除上次的加载失败标记。<b>必须在 SDK 重新下载/安装成功后、再次 <see cref="EnsureSdkLoaded"/> 之前调用</b>：
    /// 否则 <see cref="_sdkLoadError"/> 的短路会让重试恒返回 false，导致自动下载好的 DLL 在本进程内永不被加载（须重启）。
    /// </summary>
    public static void ResetLoadState()
    {
        lock (SdkLoadGate) _sdkLoadError = null;
    }

    /// <summary>卸载已加载的 SDK 模块并复位状态，使缓存的 Everything64.dll 文件解除进程内映射、可被删除。
    /// 供设置页「删除本地搜索引擎」在删文件前调用（尽力而为；运行时后续 SDK 查询将失效，需重开）。</summary>
    public static void FreeSdk()
    {
        lock (SdkLoadGate)
        {
            if (_sdkModule != IntPtr.Zero)
            {
                try { System.Runtime.InteropServices.NativeLibrary.Free(_sdkModule); } catch { }
                _sdkModule = IntPtr.Zero;
            }
            _sdkLoaded = false;
            _sdkLoadError = null;
        }
    }

    /// <summary>检测 Everything 主程序是否运行。</summary>
    public static bool IsRunning()
    {
        var hwnd = FindWindowW(EverythingWindowClass, null);
        return hwnd != IntPtr.Zero;
    }

    /// <summary>
    /// 通过 Everything SDK DLL（Everything64.dll，IPC）查询文件。
    /// 无窗口、无子进程——替换原 "everything.exe -search" GUI 子进程占位实现
    /// （该实现每次查询都会把 Everything 主窗口弹到前台，导致用户无法正常使用）。
    /// SDK 为阻塞式 IPC（内部 SendMessageTimeout，线程安全），由 EverythingQueryQueue 串行化调用。
    /// </summary>
    public static IReadOnlyList<Item> Query(
        string query, RequestFlags flags, int maxResults, CancellationToken ct)
    {
        if (!EnsureSdkLoaded()) return Array.Empty<Item>();

        Everything_Reset();
        Everything_SetRequestFlags((uint)flags);
        Everything_SetMax((uint)Math.Min(maxResults, AppConstants.EverythingMaxResults));
        Everything_SetSearchW(query);

        if (!Everything_QueryW(true))
        {
            // 常见原因：未调用 Startup（本类 EnsureSdkLoaded 已补）/ Everything 主程序未运行 /
            // Everything 与 StarMark 管理员权限等级不一致致 UIPI 拦截 WM_COPYDATA / SDK 与主程序版本不匹配。
            var err = Everything_GetLastError();
            StarLog.Warn($"Everything SDK 查询失败（GetLastError={err}：{DescribeSdkError(err)}）：{query}");
            return Array.Empty<Item>();
        }

        var count = Everything_GetNumResults();
        if (count == 0)
        {
            // 诊断埋点（V2）：IPC 调用成功但零结果——用于区分"根本没连上 Everything"与"连上了但索引/权限导致查不到"。
            // 连同 GetLastError 一并记录，便于用户复现日志精准定位。
            StarLog.Warn($"Everything 查询命中 0 条：关键词「{query}」GetLastError={Everything_GetLastError()}（max={maxResults}）");
        }
        var items = new List<Item>(Math.Min((int)count, maxResults));
        for (uint i = 0; i < count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var buf = new System.Text.StringBuilder(1024);
            Everything_GetResultFullPathNameW(i, buf, 1024);
            var fullPath = buf.ToString();
            if (string.IsNullOrEmpty(fullPath)) continue;

            long? size = null;
            if (flags.HasFlag(RequestFlags.Size) && Everything_GetResultSize(i, out var s)) size = s;

            long? dateModified = null;
            if (flags.HasFlag(RequestFlags.DateModified) && Everything_GetResultDateModified(i, out var ft))
                dateModified = DateTimeOffset.FromFileTime(ft).ToUnixTimeSeconds();

            var name = Path.GetFileName(fullPath);
            var dir = Path.GetDirectoryName(fullPath) ?? string.Empty;

            items.Add(new Item
            {
                Type = ItemType.File,
                Source = ItemSources.FileSystem,
                SourceId = LocalFileIdentity.SourceIdForPath(fullPath),
                Title = name,
                Subtitle = dir,
                Uri = LocalFileIdentity.UriForPath(fullPath),
                SearchText = name + ' ' + dir,
                FileSize = size,
                CreatedAt = dateModified ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                UpdatedAt = dateModified ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
        }
        return items;
    }

    /// <summary>把 Everything SDK 的 EVERYTHING_ERROR_* 数字码翻成人话，供日志定位用。</summary>
    private static string DescribeSdkError(uint code) => code switch
    {
        0 => "OK",
        1 => "内存不足(MEMORY)",
        2 => "IPC 不可达——多为未调用 Startup（本类已补），或 Everything 与 StarMark 管理员权限不一致被 UIPI 拦截，或 SDK 与主程序版本不匹配",
        3 => "注册窗口类失败(REGISTERCLASSEX)",
        4 => "创建窗口失败(CREATEWINDOW)",
        5 => "创建线程失败(CREATETHREAD)",
        6 => "无效搜索请求(INVALID)",
        7 => "无效调用顺序(INVALIDCALL)",
        _ => "未知错误",
    };

    private static string? _cachedExecutable;

    /// <summary>
    /// 定位 Everything 主程序：缓存 → 运行中窗口反查进程路径 → 注册表 → 常见安装目录 → PATH。
    /// 覆盖：默认安装、每用户安装、便携版、非默认路径（只要在运行就能反查到）。
    /// </summary>
    internal static string? FindEverythingExecutable()
    {
        if (_cachedExecutable is not null && File.Exists(_cachedExecutable)) return _cachedExecutable;

        // 1) Everything 正在运行：从其任务栏通知窗口反查进程路径（最可靠，覆盖任意安装位置）
        var hwnd = FindWindowW(EverythingWindowClass, null);
        if (hwnd != IntPtr.Zero)
        {
            var fromWindow = TryGetProcessPathFromWindow(hwnd);
            if (fromWindow is not null) return _cachedExecutable = fromWindow;
        }

        // 2) 注册表：安装程序写入的卸载信息 / App Paths
        foreach (var candidate in EnumerateRegistryCandidates())
        {
            if (File.Exists(candidate)) return _cachedExecutable = candidate;
        }

        // 3) 常见安装目录（含每用户安装与便携版常见位置）
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Everything", "Everything.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Everything", "Everything.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Everything", "Everything.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Everything", "Everything.exe"),
        };
        foreach (var p in candidates)
        {
            if (File.Exists(p)) return _cachedExecutable = p;
        }

        // 4) PATH 中查找
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var full = Path.Combine(dir, "Everything.exe");
                    if (File.Exists(full)) return _cachedExecutable = full;
                }
                catch { /* ignore */ }
            }
        }
        return null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);

    /// <summary>从窗口句柄反查所属进程的可执行文件路径（Everything 在运行时最可靠的定位方式）。</summary>
    private static string? TryGetProcessPathFromWindow(IntPtr hwnd)
    {
        try
        {
            if (GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == 0) return null;
            const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
            var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero) return null;
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                uint size = 1024;
                return QueryFullProcessImageNameW(hProcess, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(hProcess); }
        }
        catch { return null; }
    }

    /// <summary>从注册表枚举 Everything 可能的安装位置（卸载信息 InstallLocation / DisplayIcon / App Paths）。</summary>
    private static System.Collections.Generic.List<string> EnumerateRegistryCandidates()
    {
        var result = new System.Collections.Generic.List<string>();
        string[] keys =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Everything",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Everything",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Everything.exe",
        };
        foreach (var key in keys)
        {
            foreach (var root in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
            {
                try
                {
                    using var k = root.OpenSubKey(key);
                    if (k is null) continue;
                    var install = k.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(install))
                        result.Add(Path.Combine(install, "Everything.exe"));
                    var displayIcon = k.GetValue("DisplayIcon") as string;
                    if (!string.IsNullOrWhiteSpace(displayIcon))
                        result.Add(displayIcon.Split(',')[0]);
                    var defaultValue = k.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(defaultValue))
                        result.Add(defaultValue);
                }
                catch { /* ignore */ }
            }
        }
        return result;
    }

    /// <summary>解析 Everything CSV 输出的一行（Name,Path,Size,Date Modified,Date Created）。</summary>
    internal static Item? ParseCsvLine(string line, RequestFlags flags)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        // 简易 CSV 解析（不含引号转义字段——文件路径不会包含逗号本身，但可能包含引号）
        var fields = ParseCsvLineSimple(line);
        if (fields.Count == 0) return null;

        var name = fields[0];
        var path = fields.Count > 1 ? fields[1] : string.Empty;
        var fullPath = Path.IsPathRooted(path) && name.Length > 0
            ? Path.Combine(path, name)
            : name;

        long? size = null;
        long? dateModified = null;
        if (flags.HasFlag(RequestFlags.Size) && fields.Count > 2)
        {
            if (long.TryParse(fields[2], out var s)) size = s;
        }
        if (flags.HasFlag(RequestFlags.DateModified) && fields.Count > 3)
        {
            if (DateTime.TryParse(fields[3], out var d))
            {
                dateModified = new DateTimeOffset(d.ToUniversalTime()).ToUnixTimeSeconds();
            }
        }

        // 文件路径哈希作为 source_id（与 Everything 查询侧、拖拽登记侧同一基元，保证同路径幂等合并）
        var sourceId = LocalFileIdentity.SourceIdForPath(fullPath);

        return new Item
        {
            Type = ItemType.File,
            Source = ItemSources.FileSystem,
            SourceId = sourceId,
            Title = name,
            Subtitle = path,
            Uri = LocalFileIdentity.UriForPath(fullPath),
            SearchText = name + ' ' + path,
            FileSize = size,
            CreatedAt = dateModified ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAt = dateModified ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
    }

    /// <summary>简易 CSV 字段分割：按逗号切分，处理双引号包裹。</summary>
    private static List<string> ParseCsvLineSimple(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }
}
