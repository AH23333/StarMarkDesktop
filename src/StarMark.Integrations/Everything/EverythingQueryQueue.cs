#nullable enable
using System.Runtime.Versioning;
using StarMark.Abstractions;

namespace StarMark.Integrations.Everything;

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

    public EverythingSource(EverythingQueryQueue queue)
    {
        _queue = queue;
    }

    public string SourceId => ItemSources.FileSystem;

    public string DisplayName => "本地文件 (Everything)";

    public bool IsAvailable => EverythingInterop.IsRunning();

    /// <summary>
    /// 全量拉取——Everything 不支持批量枚举，仅在用户首次搜索时实时查询。
    /// 此处返回空列表；items 表的 file 类型条目由用户主动添加（右键菜单 / 拖拽）。
    /// </summary>
    public Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());

    public Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct)
    {
        if (!IsAvailable)
        {
            return Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());
        }
        return _queue.QueryAsync(query, filter, ct);
    }
}
