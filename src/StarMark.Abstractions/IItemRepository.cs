#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StarMark.Abstractions;

/// <summary>统一搜索结果。</summary>
public sealed class SearchResult
{
    public required IReadOnlyList<Item> Items { get; init; }
    public int Total { get; init; }
    public long ElapsedMs { get; init; }

    /// <summary>精确匹配条数（Items 前 ExactCount 条为「精确匹配」，其余为「相关结果」）。
    /// 对应扩展 selectors.ts 的 isStrong 分段，见对比方案 P2-7。</summary>
    public int ExactCount { get; init; }
}

/// <summary>仓储接口。供 ItemRepository 实现，UI/AppService 调用。</summary>
public interface IItemRepository
{
    /// <summary>两阶段查询：FTS5 MATCH → JOIN items 数值过滤。对应技术文档 §4.6。</summary>
    Task<SearchResult> SearchAsync(string keyword, SearchFilter filter, CancellationToken ct);

    Task<Item?> GetByIdAsync(long id, CancellationToken ct);

    /// <summary>批量插入/更新条目。同步协调器调用。</summary>
    Task UpsertAsync(IReadOnlyList<Item> items, CancellationToken ct);

    /// <summary>标签 CRUD。</summary>
    Task<IReadOnlyList<(string Name, int Count)>> GetAllTagsAsync(CancellationToken ct);

    Task<List<string>> GetTagsForItemAsync(long itemId, CancellationToken ct);

    /// <summary>给条目打标签。幂等。</summary>
    Task AddTagAsync(long itemId, string tagName, CancellationToken ct);

    Task RemoveTagAsync(long itemId, string tagName, CancellationToken ct);

    /// <summary>笔记。</summary>
    Task<string?> GetNoteAsync(long itemId, CancellationToken ct);

    Task SetNoteAsync(long itemId, string content, CancellationToken ct);

    /// <summary>条目总数（按类型分桶）。对应侧边栏状态栏 "Stars: X / Bookmarks: Y / Files: Z"。</summary>
    Task<Dictionary<ItemType, int>> GetCountsByTypeAsync(CancellationToken ct);

    /// <summary>获取所有可见条目（浏览模式），按排序方式返回。</summary>
    Task<IReadOnlyList<Item>> GetAllAsync(BrowseFilter filter, CancellationToken ct);

    /// <summary>获取隐藏条目列表。</summary>
    Task<IReadOnlyList<Item>> GetHiddenAsync(CancellationToken ct);

    /// <summary>设置条目隐藏状态。</summary>
    Task SetHiddenAsync(long itemId, bool hidden, CancellationToken ct);

    /// <summary>设置条目置顶状态（用户状态，同步不覆盖）。</summary>
    Task SetPinnedAsync(long itemId, bool pinned, CancellationToken ct);

    /// <summary>获取最近更新的条目（活动时间线）。</summary>
    Task<IReadOnlyList<Item>> GetRecentAsync(int limit, CancellationToken ct);

    /// <summary>记录一条活动事件（新增 / 移除条目等）。环形缓冲仅保留最近 500 条。见扩展对比方案 P1-3。</summary>
    Task LogActivityAsync(ActivityKind kind, string? itemKey, string title, string? uri, CancellationToken ct);

    /// <summary>读取最近的活动事件（活动时间线）。见扩展对比方案 P1-3。</summary>
    Task<IReadOnlyList<ActivityRecord>> GetActivityAsync(int limit, CancellationToken ct);

    /// <summary>读取同步状态键值（sync_state 表）。见扩展对比方案 P1-4。</summary>
    Task<string?> GetSyncStateAsync(string key, CancellationToken ct);

    /// <summary>写入同步状态键值（sync_state 表，幂等 upsert）。见扩展对比方案 P1-4。</summary>
    Task SetSyncStateAsync(string key, string value, CancellationToken ct);

    /// <summary>获取用户置顶条目（桌面快捷启动组件），按更新时间倒序。</summary>
    Task<IReadOnlyList<Item>> GetPinnedAsync(int limit, CancellationToken ct);
}

/// <summary>浏览过滤器（非搜索模式）。</summary>
public sealed class BrowseFilter
{
    public string? TypeFilter { get; init; }
    public string Sort { get; init; } = "recent";
    public bool IncludeHidden { get; init; }
    public IReadOnlyList<string>? TagFilters { get; init; }
    public int Limit { get; init; } = 500;
}
