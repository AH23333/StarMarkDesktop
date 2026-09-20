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

    /// <summary>
    /// 把一条「实时源虚拟条目」（Everything 文件结果等：<c>Id=0</c>、查询时从不入库）按 <c>(source, source_id)</c>
    /// 幂等登记进主库，返回持久化后的真实 <c>Id</c>（同一 <c>source_id</c> 已入库则返回既有 Id，不产生重复行）。
    /// 复用与后台同步完全一致的 upsert 口径（保留既有 hidden/pinned/notes、按 CJK 重建 search_text），
    /// 故与后续 Everything 全量索引天然合并、不分裂。<b>只写索引记录，绝不改动磁盘上的实际文件</b>（不移动 / 改名 / 删除）。
    /// 真实 Id 同时回填到 <paramref name="item"/>，调用方可继续使用。缺 <c>Source</c>/<c>SourceId</c> 业务键时返回 0。
    /// </summary>
    Task<long> RecordItemAsync(Item item, CancellationToken ct);

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

    /// <summary>
    /// star 条目中实际存在的编程语言列表（去重、归一化）。
    /// 主界面语言下拉据此渲染——只显示真实存在于 star 项目的语言。
    /// </summary>
    Task<IReadOnlyList<string>> GetStarLanguagesAsync(CancellationToken ct = default);

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

    /// <summary>按来源读取条目（本地待办/随记用）。type 可限定；按更新时间倒序。</summary>
    Task<IReadOnlyList<Item>> GetBySourceAsync(string source, ItemType? type = null, int limit = 1000, CancellationToken ct = default);

    /// <summary>按来源 + source_id 删除一条本地条目。</summary>
    Task DeleteBySourceIdAsync(string source, string sourceId, CancellationToken ct = default);

    /// <summary>写入本地条目（待办/随记）：只写 items 表，不写活动流、不跑 UriNormalizer/LanguageDetector。</summary>
    Task UpsertLocalItemAsync(Item item, CancellationToken ct = default);

    /// <summary>
    /// 读取某一组件实例名下的全部本地条目（待办 + 随记），含隐藏/置顶/子标题/URI/描述/笔记与标签等用户状态。
    /// 以 <c>source_id</c> 前缀 <c>instanceId + "|"</c> 精确圈定本实例，无跨实例数量窗口（区别于
    /// <see cref="GetBySourceAsync"/> 的全局 limit 截断）。供快照忠实捕获使用。
    /// </summary>
    Task<IReadOnlyList<Item>> GetLocalItemsForInstanceAsync(string instanceId, CancellationToken ct = default);

    /// <summary>
    /// 单事务把某实例的本地条目整体替换为给定集合（快照忠实还原用）：先按前缀删除本实例既有条目
    /// （连带其 <c>item_tags</c> 级联），再逐条插入并保留隐藏/置顶/子标题/URI/描述/笔记，且按标签名重新挂接。
    /// 调用方负责给定唯一且已按目标实例编码的 <c>source_id</c>。全程原子，任何异常回滚不留下半套数据。
    /// </summary>
    Task ReplaceLocalItemsForInstanceAsync(string instanceId, IReadOnlyList<Item> items, CancellationToken ct = default);
}

/// <summary>浏览过滤器（非搜索模式）。</summary>
public sealed class BrowseFilter
{
    public string? TypeFilter { get; init; }
    public string Sort { get; init; } = "recent";
    public bool IncludeHidden { get; init; }
    public IReadOnlyList<string>? TagFilters { get; init; }
    public int Limit { get; init; } = 500;

    /// <summary>语言筛选（extra_json.Language）。null/空表示不过滤。主界面语言下拉直选后仅显示匹配 star 条目。</summary>
    public string? Language { get; init; }
}
