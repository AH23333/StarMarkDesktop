#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace StarMark.Abstractions;

/// <summary>
/// 统一条目源接口。本地文件、GitHub Stars、浏览器书签、Ditto 剪贴板均实现此接口。
/// 对应技术文档 §3.2。
/// </summary>
public interface IItemSource
{
    /// <summary>来源标识（filesystem / chrome / github / ditto ...）。见 <see cref="ItemSources"/>。</summary>
    string SourceId { get; }

    /// <summary>显示名称（用于同步状态、设置页）。</summary>
    string DisplayName { get; }

    /// <summary>检测依赖是否可用（如 Everything 是否运行、浏览器是否安装）。</summary>
    bool IsAvailable { get; }

    /// <summary>从源拉取全量条目，写入 items 表。同步协调器调用。</summary>
    Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct);

    /// <summary>从源实时搜索（不依赖 SQLite）。供统一搜索跨源混排使用。</summary>
    /// <param name="query">关键词。</param>
    /// <param name="filter">过滤条件（类型、来源、数值范围）。</param>
    Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct);
}

/// <summary>同步上下文，承载增量同步所需的检查点状态。</summary>
public sealed class SyncContext
{
    public long? LastSyncedAt { get; init; }
    public string? ContinuationToken { get; init; }
}

/// <summary>搜索过滤条件。对应 UI 工具栏的过滤选项。</summary>
public sealed class SearchFilter
{
    public ItemType? Type { get; init; }

    /// <summary>Stars 数下限。用于 GitHubStar 过滤。</summary>
    public long? StarsMin { get; init; }

    /// <summary>更新时间下限（Unix 秒）。</summary>
    public long? DateFrom { get; init; }

    /// <summary>结果包含的字段（决定 Everything SDK 的请求标志集）。</summary>
    public bool IncludeSize { get; init; }

    public bool IncludeDate { get; init; }

    /// <summary>最大返回条数。</summary>
    public int MaxResults { get; init; } = 100;

    /// <summary>偏移量（分页）。</summary>
    public int Offset { get; init; }

    /// <summary>是否包含隐藏条目（默认排除）。</summary>
    public bool IncludeHidden { get; init; }

    /// <summary>排序：relevance(默认，按 FTS 相关度) / recent / stars / name。</summary>
    public string? Sort { get; init; }

    /// <summary>
    /// 标签过滤（AND 语义）：条目必须同时具备这里列出的全部标签。
    /// null 或空集合表示不过滤。对齐浏览器扩展 search-worker 的
    /// <c>opts.tags.every(t =&gt; item.tags.includes(t))</c>。
    /// </summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>是否有生效的标签过滤。</summary>
    public bool HasTags => Tags is { Count: > 0 };
}
