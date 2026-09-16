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
        FileName        = 0x00000001,
        Path            = 0x00000002,
        FullPath        = 0x00000004,
        Size            = 0x00000040,
        DateModified    = 0x00000100,
        DateCreated     = 0x00000200,
    }

    // ===== Everything IPC 窗口消息 =====
    private const string EverythingWindowClass = "EVERYTHING_TASKBAR_NOTIFICATION";
    private const int Everything_WM_COPYDATA_GLOBAL = 0;
    private const int Everything_WM_COPYDATA = 0x004A;
    // Everything 1.4 的 IPC 消息 ID
    private const int EVERYTHING_IPC_COPYDATA_QUERYW = 18;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowW(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint RegisterWindowMessageW([MarshalAs(UnmanagedType.LPWStr)] string lpString);

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    /// <summary>检测 Everything 主程序是否运行。</summary>
    public static bool IsRunning()
    {
        var hwnd = FindWindowW(EverythingWindowClass, null);
        return hwnd != IntPtr.Zero;
    }

    /// <summary>
    /// 查询文件。1.4 IPC 通过 WM_COPYDATA 同步发送，结果通过 SendMessage 收件箱窗口接收。
    /// 此实现为简化版：直接调用 Everything SDK 的 GetResult 函数。
    /// 完整实现需在程序内注册隐藏窗口作为收件箱，处理 WM_COPYDATA_RESPONSE 消息。
    /// </summary>
    public static IReadOnlyList<Item> Query(
        string query, RequestFlags flags, int maxResults, CancellationToken ct)
    {
        // Phase 1 MVP：暂用 Everything CLI 子进程方式（everything.exe -search ... -sort-... -limit ...）
        // 作为 1.4 IPC 实现占位。完整 IPC 实现需要注册收件箱窗口，留待 Phase 1 后续迭代。
        return QueryViaCli(query, flags, maxResults, ct);
    }

    /// <summary>
    /// 通过 Everything 命令行接口（CLI）查询。
    /// 调用 everything.exe -search &lt;query&gt; -sort-ascending -limit N -csv 输出 CSV 解析。
    /// 这是 MVP 阶段的稳妥方案：不依赖 IPC，不依赖第三方包，Everything 主程序运行即可。
    /// </summary>
    private static IReadOnlyList<Item> QueryViaCli(
        string query, RequestFlags flags, int maxResults, CancellationToken ct)
    {
        var everythingExe = FindEverythingExecutable();
        if (everythingExe == null) return Array.Empty<Item>();

        var sb = new StringBuilder();
        sb.Append("-search \"").Append(query.Replace("\"", "\"\"")).Append("\" ");
        // 入库场景（P0-1b）需要较大上限（默认 5000）；实时搜索传 100 不受影响。
        sb.Append("-limit ").Append(Math.Min(maxResults, 20000)).Append(' ');
        sb.Append("-csv ");
        if (flags.HasFlag(RequestFlags.Size)) sb.Append("-size ");
        if (flags.HasFlag(RequestFlags.DateModified)) sb.Append("-dm ");
        sb.Append("-sort-name-ascending ");

        var psi = new ProcessStartInfo
        {
            FileName = everythingExe,
            Arguments = sb.ToString(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return Array.Empty<Item>();

            ct.Register(() =>
            {
                try { if (!proc.HasExited) proc.Kill(); } catch { /* ignore */ }
            });

            var items = new List<Item>();
            // CSV 表头：Name,Path,Size,Date Modified,Date Created
            // 第一行是表头，跳过
            var firstLine = true;
            while (!proc.StandardOutput.EndOfStream)
            {
                if (ct.IsCancellationRequested) break;
                var line = proc.StandardOutput.ReadLine();
                if (line == null) break;
                if (firstLine) { firstLine = false; continue; }
                var item = ParseCsvLine(line, flags);
                if (item != null) items.Add(item);
            }

            if (!proc.WaitForExit(5000)) { try { proc.Kill(); } catch { /* ignore */ } }
            return items;
        }
        catch
        {
            return Array.Empty<Item>();
        }
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
