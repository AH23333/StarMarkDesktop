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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpLibFileName);

    /// <summary>SDK DLL 缓存位置：%LOCALAPPDATA%\StarMark\sdk\Everything64.dll。</summary>
    internal static string SdkDllPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarMark", "sdk");
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

                if (!System.Runtime.InteropServices.NativeLibrary.TryLoad(path, out _))
                {
                    _sdkLoadError = new InvalidOperationException($"Everything64.dll 加载失败：{path}");
                    StarLog.Error(_sdkLoadError.Message);
                    return false;
                }

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
        Everything_SetMax((uint)Math.Min(maxResults, 20000));
        Everything_SetSearchW(query);

        if (!Everything_QueryW(true))
        {
            // 常见原因：Everything 主程序未运行 / IPC 不可达（EverythingSource.IsAvailable 已前置拦截多数情况）
            StarLog.Warn($"Everything SDK 查询失败（GetLastError={Everything_GetLastError()}）：{query}");
            return Array.Empty<Item>();
        }

        var count = Everything_GetNumResults();
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
                SourceId = ComputeStableHash(fullPath),
                Title = name,
                Subtitle = dir,
                Uri = "file://" + fullPath.Replace('\\', '/'),
                SearchText = name + ' ' + dir,
                FileSize = size,
                CreatedAt = dateModified ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                UpdatedAt = dateModified ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
        }
        return items;
    }

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

        // 文件路径哈希作为 source_id
        var sourceId = ComputeStableHash(fullPath);

        return new Item
        {
            Type = ItemType.File,
            Source = ItemSources.FileSystem,
            SourceId = sourceId,
            Title = name,
            Subtitle = path,
            Uri = "file://" + fullPath.Replace('\\', '/'),
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

    /// <summary>稳定的路径哈希，作为 source_id 的基础（保证同一路径幂等）。</summary>
    private static string ComputeStableHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input.ToLowerInvariant());
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 8);
    }
}
