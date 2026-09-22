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

    /// <summary>启动时的 Everything 就绪流程：SDK DLL → （主程序未运行时）自动安装主程序。</summary>
    public async Task EnsureReadyAsync()
    {
        var sdkReady = await EnsureSdkReadyAsync();

        // 诊断埋点（V2）：把启动时的 Everything 可用性一次打全，用于定位用户报告的
        // "Everything 已开却搜不到本地文件"——区分究竟是①主程序未被探测到②SDK DLL 未加载③IPC 通但查询空。
        var running = EverythingInterop.IsRunning();
        var exe = EverythingInterop.FindEverythingExecutable();
        StarLog.Info(
            $"Everything 就绪快照：主程序运行(FindWindow)={running} · SDK已加载={sdkReady} · " +
            $"探测到exe={(exe is null ? "未找到" : exe)} · SDK DLL={EverythingInterop.SdkDllPath}" +
            $"({(File.Exists(EverythingInterop.SdkDllPath) ? "存在" : "缺失")})");

        if (!running)
        {
            await EnsureEverythingInstalledAsync();
        }
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

    /// <summary>本地磁盘搜索「开启」结果，供设置页据此决定开关回退与文案。</summary>
    public enum LocalDiskSearchEnableOutcome { Ready, ElevationDeclined, Failed }

    /// <summary>
    /// 用户开启「本地磁盘搜索」时的完整准备流程（对应需求「开启则申请提权、开启 SDK」）：
    /// ① 下载 / 加载 Everything64.dll（SDK，无需提权）；② 主程序缺失则装官方 Everything；
    /// ③ <b>安装并启动「Everything 服务」——这一步才申请提权（一次 UAC）</b>；④ 确保普通权限客户端在跑。
    /// <para>
    /// 为什么提权只用于「装服务」、而非把 Everything 本身提权：SDK 走 WM_COPYDATA 与 Everything 客户端窗口通信，
    /// 若把客户端抬到 High IL，普通权限的 StarMark 发的窗口消息会被 UIPI 拦截、IPC 恒失败（见 EverythingInterop 注释）。
    /// 官方「Everything 服务」以 SYSTEM 身份读全盘 NTFS(MFT) 并喂给普通 IL 客户端——既拿到全盘索引，又保住 IPC。
    /// </para>
    /// 返回 <see cref="LocalDiskSearchEnableOutcome.ElevationDeclined"/> 表示用户在 UAC 取消（调用方应把开关回退为关）。
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

        // 申请提权：装「Everything 服务」。用户取消 UAC → ElevationDeclined（开关回退）。
        if (!await EnsureServiceInstalledAsync(exe, ct))
            return LocalDiskSearchEnableOutcome.ElevationDeclined;

        await EnsureClientRunningAsync(exe, ct);   // 客户端保持普通 IL，WM_COPYDATA 才有落点

        return EverythingInterop.IsRunning()
            ? LocalDiskSearchEnableOutcome.Ready
            : LocalDiskSearchEnableOutcome.Failed;
    }

    /// <summary>
    /// 以提权方式执行 <c>Everything.exe -install-service</c>（voidtools 官方命令：装 SYSTEM 级索引服务）。
    /// 幂等：服务已存在时 Everything 自身快速返回。用户在 UAC 点「否」→ <c>runas</c> 抛 <see cref="System.ComponentModel.Win32Exception"/>，
    /// 捕获后返回 false 让调用方把开关回退为关（＝「不使用则纯当书签 / Star 工具」）。
    /// </summary>
    private static async Task<bool> EnsureServiceInstalledAsync(string everythingExe, CancellationToken ct)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = everythingExe,
                Arguments = "-install-service",
                Verb = "runas",                 // 触发一次 UAC：仅服务安装提权，客户端随后仍普通权限运行
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is not null) await proc.WaitForExitAsync(ct);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 1223 ERROR_CANCELLED：用户在 UAC 取消提权——属正常，视为「不开启」。
            return false;
        }
        catch (Exception ex)
        {
            StarLog.Error("安装 Everything 服务失败（-install-service）", ex);
            return false;
        }
    }

    /// <summary>确保普通完整性 Everything 客户端在运行（提供 IPC 落点窗口）；未运行则拉起并最多等 ~5s。</summary>
    private static async Task EnsureClientRunningAsync(string everythingExe, CancellationToken ct)
    {
        if (EverythingInterop.IsRunning()) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = everythingExe,
                UseShellExecute = true,
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
