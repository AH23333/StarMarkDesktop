#nullable enable
using System.Collections.Generic;
using System.IO;
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

    public bool IsAvailable => EverythingInterop.IsRunning();

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
            return Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());
        }
        return _queue.QueryAsync(query, filter, ct);
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
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarMark", "downloads");
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
}
