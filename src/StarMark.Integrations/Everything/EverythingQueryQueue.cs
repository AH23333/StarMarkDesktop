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

        // 必须离开调用线程：Everything_QueryW(true) 是阻塞式 IPC（内部 SendMessageTimeout 等回包）。
        // 从 UI 线程直接调它，只要引擎在重建索引 / 回包慢 / 权限等级不一致导致回包被 UIPI 丢弃，
        // 整个界面就按超时时长冻住（实测表现为"搜索时高频卡死"）；且这里的 await 若无
        // ConfigureAwait(false) 会回到 UI 线程续跑，所以两件事都要断开。
        await _gate.WaitAsync(localCt).ConfigureAwait(false);
        try
        {
            // 动态选取最小必要请求标志集（不展示的字段不请求）
            var flags = ComputeMinimalFlags(filter);
            var max = filter.MaxResults;
            var offset = (uint)Math.Max(0, filter.Offset);
            var sort = StarMark.Abstractions.EverythingSort.Map(filter.Sort) ?? 0;
            return await Task.Run(
                () => EverythingInterop.Query(query, flags, max, localCt, sort, offset),
                localCt).ConfigureAwait(false);
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
        // 「类型」多选的检索式只加在这一条链路上：用户关键词本身逐字保留（手敲 ext:/size: 仍生效），
        // 片段绝不回写搜索框。
        return _queue.QueryAsync(
            StarMark.Abstractions.FileKindQuery.Compose(query, filter.FileQueryFragments), filter, ct);
    }

    private const string SdkZipUrl = "https://www.voidtools.com/Everything-SDK.zip";

    /// <summary>
    /// 确保 Everything SDK DLL 可用：应用目录 / 缓存目录缺失时，从 voidtools 下载官方 SDK zip
    /// 并提取 x64 DLL 到缓存目录（%LOCALAPPDATA%\StarMark\sdk），然后加载。
    /// 注意：SDK 是 IPC 包装，Everything 主程序仍需在后台运行；StarMark 自带 standard Everything 的拉起/接管见
    /// <see cref="EnsureOwnedRunningAsync"/>。
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

    /// <summary>启动就绪流程（C′）：SDK DLL → 确保 StarMark 自带 standard Everything 在运行并接管默认实例。</summary>
    /// <summary>
    /// 启动/开启时的准备流程。整段都是**同步阻塞**（枚举进程与读主模块、Kill+WaitForExit、
    /// 下载与解压 SDK/便携版、FindWindow 轮询），而调用方是 App 启动路径与设置页开关（UI 线程）——
    /// 不 offload 就是"一开本地磁盘搜索，界面先冻死数秒到十几秒"（用户报的"卡死"主因之一）。
    /// </summary>
    public Task EnsureReadyAsync() => Task.Run(async () =>
    {
        var sdkReady = await EnsureSdkReadyAsync().ConfigureAwait(false);
        var ownedRunning = await EnsureOwnedRunningAsync(CancellationToken.None).ConfigureAwait(false);

        // 诊断：一次打全启动时 Everything 可用性，便于定位"开了仍搜不到"——①自带实例没起②SDK DLL 未加载③IPC 通但查询空。
        StarLog.Info(
            $"Everything 就绪快照(C′)：SDK已加载={sdkReady} · 自带实例接管={ownedRunning} · " +
            $"运行(FindWindow)={EverythingInterop.IsRunning()} · 自带exe路径={(File.Exists(OwnedExePath) ? "存在" : "未下载")} · " +
            $"SDK DLL={EverythingInterop.SdkDllPath}({(File.Exists(EverythingInterop.SdkDllPath) ? "存在" : "缺失")})");
    });

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
    /// 用户开启「本地磁盘搜索」时的准备流程（C′）：① 确保 SDK 就绪（下载 / 加载 Everything64.dll）；
    /// ② 确保 StarMark 自带 standard Everything 在运行并接管默认实例（<see cref="EnsureOwnedRunningAsync"/>）。
    /// 不再依赖/探测用户机上那只（可能不应答 IPC 的）第三方 Everything，也不再装 Session-0「Everything 服务」。
    /// </summary>
    public Task<LocalDiskSearchEnableOutcome> EnableAsync(CancellationToken ct)
        // 同 EnsureReadyAsync：这段全是阻塞式系统调用，而它是设置页开关的直接 await 目标。
        => Task.Run(async () =>
        {
            await EnsureSdkReadyAsync().ConfigureAwait(false);   // 下载 / 加载 SDK DLL（不提权）
            var ok = await EnsureOwnedRunningAsync(ct).ConfigureAwait(false);
            return ok ? LocalDiskSearchEnableOutcome.Ready : LocalDiskSearchEnableOutcome.Failed;
        }, ct);

    // ===== C′：StarMark 自带 standard Everything（显式自有路径 + 接管默认实例）=====
    // 动机：用户机上的第三方 Everything repack（如强制提权的 Lite 版）不对外应答标准 SDK IPC，且运行/注册表探测
    // 会把我们指到那只坏 repack。C′ 改为自带一份官方 standard Everything 便携版到自有目录、开启时接管唯一默认实例，
    // 复用已真机验证正确的 1.4 SDK 通道查询。代价（已获用户确认）：开启时会关闭其它 Everything 实例。
    private const string PortableZipUrl = "https://www.voidtools.com/Everything-1.4.1.1024.x64.zip";
    private static string OwnedDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        StarMark.Abstractions.AppConstants.AppName, "everything");
    private static string OwnedExePath => Path.Combine(OwnedDir, "Everything.exe");

    /// <summary>确保「StarMark 自带」standard Everything 已运行并持有默认实例：自带实例已在跑→跳过；否则
    /// （按需下载便携版→）关闭其它 Everything 实例→以 -hidden 拉起自带实例→等 IPC 窗口就绪。返回最终是否可 IPC。</summary>
    public static async Task<bool> EnsureOwnedRunningAsync(CancellationToken ct)
    {
        if (OwnedEverythingRunning()) return true;

        var exe = await EnsureOwnedEverythingAsync(ct);
        if (exe is null) return false;

        // 默认实例只能有一个：接管所有权，关掉其它 Everything（提权下可结束 High-IL repack；失败仅降级）。
        CloseOtherEverything(exe);
        // 关掉的正是"上一只 IPC 窗口"⇒ 作废 IsRunning 的粘性，否则下面轮询会把它的命中当成自带实例就绪
        EverythingInterop.ForgetRunning();

        // 让自带实例不占系统托盘图标（用户视觉上只有 StarMark 一个应用）：仅改我们掌控的自带 ini。
        SeedOwnedTrayHidden();

        try
        {
            // -startup：Everything 官方参数，启动后不显示主窗口（仅托盘/后台），且照常创建 IPC 通知窗口。
            // （真机自证：`Everything.exe -startup` 起标准版后 es 能连上并出结果；用 -hidden 则 FindWindow 探不到窗口。）
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = "-startup",
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            });
        }
        catch (Exception ex)
        {
            StarLog.Error("拉起自带 Everything 失败", ex);
            return false;
        }

        // 首次运行要建索引/落 INI，给足 ~15s；一旦 FindWindow 探到 IPC 窗口即返回。
        for (var i = 0; i < 60 && !EverythingInterop.IsRunning(); i++)
        {
            try { await Task.Delay(250, ct); } catch (OperationCanceledException) { break; }
        }
        var up = EverythingInterop.IsRunning();
        if (!up) StarLog.Warn("自带 Everything 已拉起但 FindWindow 仍未探到 IPC 窗口（可能仍在初始化/被安全软件拦/权限不一致）。");
        return up;
    }

    /// <summary>自带 standard Everything 是否已在运行——按主模块路径精确匹配自有路径，避免把用户 repack 误当作自带。</summary>
    private static bool OwnedEverythingRunning()
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("Everything"))
            {
                try
                {
                    if (string.Equals(p.MainModule?.FileName, OwnedExePath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { /* 跨完整性级别读不到主模块，忽略该进程 */ }
                finally { p.Dispose(); }
            }
        }
        catch (Exception ex) { StarLog.Error("枚举 Everything 进程失败", ex); }
        return false;
    }

    /// <summary>关闭除待启动自带实例外的所有 Everything 进程（提权可结束 High-IL 的 repack）。单个失败仅记录并继续。</summary>
    private static void CloseOtherEverything(string keepExePath)
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("Everything"))
            {
                try
                {
                    string? path = null;
                    try { path = p.MainModule?.FileName; } catch { }
                    if (path is not null && string.Equals(path, keepExePath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch (Exception ex) { StarLog.Error("关闭其它 Everything 实例失败（可能无权限，降级继续）", ex); }
                finally { p.Dispose(); }
            }
        }
        catch (Exception ex) { StarLog.Error("枚举待关闭 Everything 进程失败", ex); }
    }

    /// <summary>确保自带 standard Everything 落到自有目录（%LOCALAPPDATA%\StarMark\everything\Everything.exe）。
    /// A2 两级来源：① 优先复制<b>随应用分发</b>的副本（发布时把 portable Everything.exe/.lng 放进应用目录或
    /// its <c>everything\</c> 子目录 → 新环境零联网即可用）；② 无副本才从 voidtools 官网下载兜底。
    /// 只取固定文件名，无 Zip Slip 面。返回落地路径或 null。</summary>
    private static async Task<string?> EnsureOwnedEverythingAsync(CancellationToken ct)
    {
        if (File.Exists(OwnedExePath)) return OwnedExePath;
        Directory.CreateDirectory(OwnedDir);

        // ① 随应用分发的副本（免联网）
        foreach (var dir in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "everything"),
                     AppContext.BaseDirectory,
                 })
        {
            var src = Path.Combine(dir, "Everything.exe");
            if (!File.Exists(src)) continue;
            try
            {
                File.Copy(src, OwnedExePath, overwrite: true);
                var lng = Path.Combine(dir, "Everything.lng");
                if (File.Exists(lng))
                    File.Copy(lng, Path.Combine(OwnedDir, "Everything.lng"), overwrite: true);
                StarLog.Info($"自带 Everything 采用随应用分发副本：{src}");
                return OwnedExePath;
            }
            catch (Exception ex) { StarLog.Error("复制随应用分发的 Everything 失败，改用联网下载兜底", ex); }
        }

        // ② 兜底：官网下载便携版
        try
        {
            StarLog.Info("未发现随应用分发副本：下载官方 standard Everything（便携版）到 StarMark 自有目录…");
            var zip = Path.Combine(OwnedDir, "Everything-portable.zip");
            if (!File.Exists(zip))
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var bytes = await http.GetByteArrayAsync(PortableZipUrl, ct);
                await File.WriteAllBytesAsync(zip, bytes, ct);
            }
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var name in new[] { "Everything.exe", "Everything.lng" })
                {
                    var entry = archive.Entries.FirstOrDefault(e =>
                        string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (entry is not null)
                        entry.ExtractToFile(Path.Combine(OwnedDir, name), overwrite: true);
                }
            }
            try { File.Delete(zip); } catch { }

            if (File.Exists(OwnedExePath))
            {
                StarLog.Info($"自带 Everything 就绪：{OwnedExePath}");
                return OwnedExePath;
            }
            StarLog.Error("自带 Everything 解压后仍未见 Everything.exe。");
            return null;
        }
        catch (Exception ex)
        {
            StarLog.Error("下载/解压自带 Everything 失败（离线属正常，降级为无本地文件搜索）", ex);
            return null;
        }
    }

    // ===== 自带引擎的用户管理面（供设置页：占用大小 / 打开所在目录 / 删除） =====

    /// <summary>StarMark 数据根目录（%LOCALAPPDATA%\StarMark，内含 sdk\ 与 everything\）。</summary>
    public static string DataRootPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StarMark.Abstractions.AppConstants.AppName);

    private static string SdkDir => Path.GetDirectoryName(EverythingInterop.SdkDllPath)!;

    /// <summary>把自带实例的 <c>show_tray_icon</c> 置 0（写我们掌控的自带 ini，不动用户任何 Everything）。</summary>
    private static void SeedOwnedTrayHidden()
    {
        try
        {
            var ini = Path.Combine(OwnedDir, "Everything.ini");
            var lines = File.Exists(ini) ? File.ReadAllLines(ini).ToList() : new List<string>();
            void Set(string key, string val)
            {
                for (var i = 0; i < lines.Count; i++)
                    if (lines[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) { lines[i] = $"{key}={val}"; return; }
                lines.Add($"{key}={val}");
            }
            Set("show_tray_icon", "0");
            File.WriteAllText(ini, string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex) { StarLog.Error("设置自带 Everything 不显示托盘图标失败（不影响搜索）", ex); }
    }

    /// <summary>本地搜索引擎占用字节数（everything\ + sdk\ 递归求和；单个文件读不了则跳过）。</summary>
    public static long GetEngineOccupancyBytes()
    {
        long sum = 0;
        foreach (var dir in new[] { OwnedDir, SdkDir })
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { sum += new FileInfo(f).Length; } catch { /* 占用/权限，跳过 */ }
                }
            }
            catch (Exception ex) { StarLog.Error("统计本地搜索引擎占用失败", ex); }
        }
        return sum;
    }

    /// <summary>是否已下载/安装过本地搜索引擎（任一目录非空）。</summary>
    public static bool IsEngineInstalled()
    {
        foreach (var dir in new[] { OwnedDir, SdkDir })
        {
            try { if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any()) return true; }
            catch { }
        }
        return false;
    }

    /// <summary>在资源管理器中打开 StarMark 数据根目录（含 everything\ 与 sdk\）。</summary>
    public static void OpenEngineFolder()
    {
        try
        {
            Directory.CreateDirectory(DataRootPath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = DataRootPath, UseShellExecute = true });
        }
        catch (Exception ex) { StarLog.Error("打开本地搜索引擎目录失败", ex); }
    }

    /// <summary>删除本地搜索引擎（everything\ + sdk\）：先结束所有 Everything 进程、再卸载进程内映射的 SDK，
    /// 最后尽力删目录。个别文件仍被占用会留下，不抛错。用户下次开启本地搜索会自动重新下载。</summary>
    public static void DeleteEngine()
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("Everything"))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { }
                finally { p.Dispose(); }
            }
        }
        catch (Exception ex) { StarLog.Error("结束 Everything 进程失败", ex); }

        EverythingInterop.FreeSdk();   // 解除 Everything64.dll 进程内映射，尽量让它可删

        foreach (var dir in new[] { OwnedDir, SdkDir })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (Exception ex) { StarLog.Error($"删除本地搜索引擎目录失败（部分文件可能仍被占用）：{dir}", ex); }
        }
    }
}
