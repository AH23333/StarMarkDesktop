#nullable enable
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Versioning;
using StarMark.Abstractions;

namespace StarMark.Integrations.Everything;

/// <summary>
/// 本地文件索引配置（P0-1b）。由 UI 层从 <see cref="StarMark.UI.Helpers.SettingsStore"/> 读取后注入，
/// 避免 StarMark.Integrations 反向依赖 StarMark.UI。
/// </summary>
public sealed class FileIndexOptions
{
    /// <summary>需要索引的本地根目录。空表示使用默认（桌面/下载/文档）。</summary>
    public IReadOnlyList<string> Roots { get; set; } = new List<string>();

    /// <summary>每个根目录的索引数量上限。默认 5000。</summary>
    public int MaxCount { get; set; } = 5000;

    /// <summary>
    /// 本地磁盘搜索总开关（默认关）。关时 <see cref="EverythingSource.IsAvailable"/> 恒 false，
    /// 统一搜索直接跳过该源（不发 IPC 查询），且启动流程绝不下载 SDK / 安装 / 拉起 Everything——保障默认零内存零打扰。
    /// 由 UI 层从 SettingsStore 读取注入，避免 Integrations 反向依赖 UI。
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Everything SDK 1.4 (WM_COPYDATA) 适配器 + 查询队列。
/// 对应技术文档 §6.3：SDK 一次只能处理一个查询，非线程安全。
/// 缓解：查询队列 + 取消机制（用户键入新关键词时取消前一个）。
/// 对应技术文档 §6.2：Everything 1.5 命名管道限制 High-IL，主进程 Medium-IL 无法直连。
/// MVP 阶段使用 1.4 SDK + WM_COPYDATA（兼容 Medium-IL）。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EverythingQueryQueue : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;

    /// <summary>
    /// 执行查询。键入新关键词时取消前一个查询并释放 SDK 资源。
    /// </summary>
    public async Task<IReadOnlyList<Item>> QueryAsync(
        string query, SearchFilter filter, CancellationToken externalCt)
    {
        _cts?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var localCt = _cts.Token;

        await _gate.WaitAsync(localCt);
        try
        {
            // 动态选取最小必要请求标志集（不展示的字段不请求）
            var flags = ComputeMinimalFlags(filter);
            return EverythingInterop.Query(query, flags, filter.MaxResults, localCt);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 根据当前搜索结果面板实际展示的字段，动态选取最小必要的查询标志集。
    /// 例：列表不展示文件大小，就不请求 EVERYTHING_REQUEST_SIZE。
    /// </summary>
    private static EverythingInterop.RequestFlags ComputeMinimalFlags(SearchFilter f)
    {
        var flags = EverythingInterop.RequestFlags.FileName
                  | EverythingInterop.RequestFlags.Path
                  | EverythingInterop.RequestFlags.FullPath;
        if (f.IncludeSize) flags |= EverythingInterop.RequestFlags.Size;
        if (f.IncludeDate) flags |= EverythingInterop.RequestFlags.DateModified;
        return flags;
    }

    public ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Everything 1.4 适配器。实现 <see cref="IItemSource"/>。
/// 通过 Everything 的窗口消息 IPC（WM_COPYDATA）查询文件。
/// 需要 Everything 主程序运行（检测窗口类名）。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EverythingSource : IItemSource
{
    private readonly EverythingQueryQueue _queue;
    private readonly FileIndexOptions _options;

    public EverythingSource(EverythingQueryQueue queue, FileIndexOptions options)
    {
        _queue = queue;
        _options = options;
    }

    public string SourceId => ItemSources.FileSystem;

    public string DisplayName => "本地文件 (Everything)";

    /// <summary>
    /// 检测依赖是否可用。总开关关闭时恒 false——统一搜索据此跳过本源，不发任何 IPC 查询，
    /// 保障默认（轻度用户）零本地文件搜索、零额外内存。开启后才看 Everything 是否在运行。
    /// </summary>
    public bool IsAvailable => _options.Enabled && EverythingInterop.IsRunning();

    /// <summary>
    /// 全量拉取（P0-1b）：把用户配置的本地根目录下的文件索引进 items 表，落库为 ItemType.File。
    /// 只索引指定根目录（不扫全盘）、每目录带数量上限；source_id 用路径哈希保证幂等，
    /// 重复同步不会产生多余条目。Everything 未运行或根目录为空时返回空列表。
    /// </summary>
    public async Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
    {
        if (!IsAvailable || _options.Roots.Count == 0)
            return Array.Empty<Item>();

        var results = new List<Item>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in _options.Roots)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
                continue;

            // 以根目录路径作为 Everything 查询词，匹配其下（含子目录）全部文件。
            var filter = new SearchFilter { IncludeSize = true, MaxResults = _options.MaxCount };
            var items = await _queue.QueryAsync(root, filter, ct);
            foreach (var item in items)
            {
                if (seen.Add(item.SourceId))
                    results.Add(item);
            }
        }
        return results;
    }

    public Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct)
    {
        if (!IsAvailable)
        {
            // 诊断埋点（V2）：走到这里说明调用方探测到可用、真正查询时又不可用（多为竞态）；
            // 若日志里这条频繁出现而"跳过"那条却没有，则 IsAvailable 判定本身不稳定。
            StarLog.Warn($"Everything 搜索被跳过：查询瞬间 IsAvailable=false（FindWindow 未探到 EVERYTHING 窗口），关键词「{query}」");
            return Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());
        }
        return _queue.QueryAsync(query, filter, ct);
    }

    private const string SdkZipUrl = "https://www.voidtools.com/Everything-SDK.zip";

    /// <summary>
    /// 确保 Everything SDK DLL 可用：应用目录 / 缓存目录缺失时，从 voidtools 下载官方 SDK zip
    /// 并提取 x64 DLL 到缓存目录（%LOCALAPPDATA%\StarMark\sdk），然后加载。
    /// 注意：SDK 是 IPC 包装，Everything 主程序仍需在后台运行；主程序未安装时的自动安装见
    /// <see cref="EnsureEverythingInstalledAsync"/>。
    /// </summary>
    public async Task<bool> EnsureSdkReadyAsync()
    {
        if (EverythingInterop.EnsureSdkLoaded()) return true;
        try
        {
            StarLog.Info("未找到 Everything64.dll：开始下载官方 Everything SDK…");
            var sdkPath = EverythingInterop.SdkDllPath;
            var sdkDir = Path.GetDirectoryName(sdkPath)!;
            Directory.CreateDirectory(sdkDir);
            var zip = Path.Combine(sdkDir, "Everything-SDK.zip");
            if (!File.Exists(zip))
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var bytes = await http.GetByteArrayAsync(SdkZipUrl);
                await File.WriteAllBytesAsync(zip, bytes);
            }

            using var archive = ZipFile.OpenRead(zip);
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, "Everything64.dll", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                StarLog.Error("Everything SDK zip 中未找到 Everything64.dll。");
                return false;
            }

            using var src = entry.Open();
            using var dst = File.Create(sdkPath);
            src.CopyTo(dst);

            StarLog.Info("Everything SDK 下载完成。");
            // 上面 :154 的首次尝试已把 _sdkLoadError 置位；不清标记，紧随其后的 EnsureSdkLoaded 会短路返回 false，
            // 刚下载好的 DLL 在本进程永不被加载（须重启）。清后再试。
            EverythingInterop.ResetLoadState();
            return EverythingInterop.EnsureSdkLoaded();
        }
        catch (Exception ex)
        {
            StarLog.Error("Everything SDK 下载/加载失败（离线时属正常，降级为无本地文件实时搜索）", ex);
            return false;
        }
    }

    /// <summary>启动时的 Everything 就绪流程：SDK DLL → 主程序缺失则安装 → **确保客户端在运行**（拉起）。</summary>
    public async Task EnsureReadyAsync()
    {
        var sdkReady = await EnsureSdkReadyAsync();

        var exe = EverythingInterop.FindEverythingExecutable();
        if (exe is null)
        {
            await EnsureEverythingInstalledAsync();   // 探测不到 → voidtools 官方静默安装
            exe = EverythingInterop.FindEverythingExecutable();
        }

        // 关键：安装≠启动。NSIS 静默安装不会拉起客户端；用户手动关掉 Everything 后重新开启本地搜索时，
        // 若这里不拉起，FindWindow 恒探不到窗口 → EverythingSource.IsAvailable 恒 false → 本地文件永远不进结果。
        // （EnableAsync 有此步、旧的 EnsureReadyAsync 漏了，正是「开启后没启动 Everything」的根因。）
        if (!EverythingInterop.IsRunning() && exe is not null)
            await EnsureClientRunningAsync(exe, CancellationToken.None);

        // 诊断埋点（V2）：把启动时的 Everything 可用性一次打全，用于定位"开了仍搜不到"——
        // 区分①主程序未运行②SDK DLL 未加载③IPC 通但查询空。
        StarLog.Info(
            $"Everything 就绪快照：SDK已加载={sdkReady} · 运行(FindWindow)={EverythingInterop.IsRunning()} · " +
            $"探测到exe={(exe is null ? "未找到" : exe)} · SDK DLL={EverythingInterop.SdkDllPath}" +
            $"({(File.Exists(EverythingInterop.SdkDllPath) ? "存在" : "缺失")})");
    }

    private const string InstallerUrl = "https://www.voidtools.com/Everything-1.4.1.1028.x64-Setup.exe";

    /// <summary>
    /// 确保本机有可用的 Everything（用户要求：探测不到时默认安装）。
    /// 全部探测手段落空时，下载 voidtools 官方安装包静默安装（NSIS /S，需要提权时由 UAC 弹窗交用户确认），
    /// 完成后重新探测；失败（离线 / 用户取消）仅记录日志并降级为无本地文件实时搜索，不影响应用其余功能。
    /// 返回探测/安装后的最终可用状态。
    /// </summary>
    public async Task<bool> EnsureEverythingInstalledAsync()
    {
        try
        {
            if (EverythingInterop.FindEverythingExecutable() is not null) return true;

            StarLog.Info("未检测到 Everything：开始自动安装（voidtools 官方 1.4 安装包，静默模式）…");
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StarMark.Abstractions.AppConstants.AppName, "downloads");
            Directory.CreateDirectory(dir);
            var installer = Path.Combine(dir, "Everything-1.4.1.1028.x64-Setup.exe");
            if (!File.Exists(installer))
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var bytes = await http.GetByteArrayAsync(InstallerUrl);
                await File.WriteAllBytesAsync(installer, bytes);
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = installer,
                Arguments = "/S",
                UseShellExecute = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is not null) await proc.WaitForExitAsync();

            var found = EverythingInterop.FindEverythingExecutable() is not null;
            StarLog.Info(found
                ? "Everything 自动安装完成并已识别。"
                : "Everything 自动安装执行完毕，但仍未探测到 Everything.exe。");
            return found;
        }
        catch (Exception ex)
        {
            StarLog.Error("Everything 自动安装失败（离线 / 用户取消 UAC 属正常情况，降级为无本地文件实时搜索）", ex);
            return false;
        }
    }

    /// <summary>本地磁盘搜索「开启」结果，供设置页据此决定状态文案。</summary>
    public enum LocalDiskSearchEnableOutcome { Ready, Failed }

    /// <summary>
    /// 用户开启「本地磁盘搜索」时的准备流程：<b>不再装「Everything 服务」</b>（那是 Session-0 服务，反倒会让
    /// 普通权限客户端被顶成空壳、对外 IPC 失效）——改由 <b>StarMark 自身以管理员权限重启</b>（见 UI 层 Privilege /
    /// App 启动逻辑），与用户那只（可能常以管理员运行的）Everything 同完整性级别，WM_COPYDATA IPC 便不被 UIPI 拦。
    /// 本方法只做：① 确保 SDK 就绪（下载 / 加载 Everything64.dll）；② 主程序缺失则装官方 Everything；③ 确保客户端在跑。
    /// </summary>
    public async Task<LocalDiskSearchEnableOutcome> EnableAsync(CancellationToken ct)
    {
        await EnsureSdkReadyAsync();   // 下载 / 加载 SDK DLL（不提权）

        var exe = EverythingInterop.FindEverythingExecutable();
        if (exe is null)
        {
            // 主程序未装：官方安装器（NSIS /S，自带一次 UAC 提权）。装后再定位一次。
            await EnsureEverythingInstalledAsync();
            exe = EverythingInterop.FindEverythingExecutable();
            if (exe is null) return LocalDiskSearchEnableOutcome.Failed;
        }

        await EnsureClientRunningAsync(exe, ct);   // 确保有可 IPC 的客户端窗口

        return EverythingInterop.IsRunning()
            ? LocalDiskSearchEnableOutcome.Ready
            : LocalDiskSearchEnableOutcome.Failed;
    }

    /// <summary>确保有一个可 IPC 的 Everything 客户端在运行；未运行则拉起并最多等 ~5s。</summary>
    private static async Task EnsureClientRunningAsync(string everythingExe, CancellationToken ct)
    {
        if (EverythingInterop.IsRunning()) return;
        try
        {
            // -hidden：Everything 官方参数，启动后不弹主窗口（只在后台提供 IPC 落点窗口）。
            // 用户要的是「在主搜索栏直接搜本地文件」，不该看到另开一个 Everything 界面来回切换。
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = everythingExe,
                Arguments = "-hidden",
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            });
        }
        catch (Exception ex)
        {
            StarLog.Error("启动 Everything 客户端失败", ex);
            return;
        }
        for (var i = 0; i < 20 && !EverythingInterop.IsRunning(); i++)
        {
            try { await Task.Delay(250, ct); } catch (OperationCanceledException) { break; }
        }
    }
}
