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

    /// <summary>本次切片之后还有未取回的命中（供「加载更多」按钮可见性）。
    /// 分页语义＝合并去重后再切片，见 <c>SearchService</c> 与 P-42。</summary>
    public bool HasMore { get; init; }
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
    /// 记录一条剪贴板历史（source=<see cref="ItemSources.Clipboard"/>）：按 (source, source_id) 幂等，
    /// 同一段文本再次复制 = 移回最近 + 复制次数累加，并<b>保留</b>用户在该条上的置顶/隐藏/笔记/标签。
    /// 不写活动流；落库后按 <paramref name="maxEntries"/> 轮转，置顶条目豁免删除。
    /// 返回带真实 Id 与合并后 extra_json 的条目。
    /// </summary>
    Task<Item> RecordClipboardAsync(Item draft, CancellationToken ct = default, int maxEntries = Clipboard.ClipboardPolicy.MaxEntries);

    /// <summary>清空全部剪贴板历史（含置顶条目），返回删除条数。</summary>
    Task<int> ClearClipboardHistoryAsync(CancellationToken ct = default);

    /// <summary>
    /// 删除<b>一条</b>剪贴板历史，返回是否真的删掉一行。实现必须在 SQL 里同时限定 Id 与
    /// <c>source='clipboard'</c>——否则误传的 Id 会连书签 / Star / 待办一起删掉。
    /// </summary>
    Task<bool> DeleteClipboardEntryAsync(long itemId, CancellationToken ct = default);

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

    /// <summary>
    /// <b>一次事务里</b>给一批条目追加标签，返回真正发生变化的条目数。
    /// <para>
    /// 为什么要有这个方法：AI 批量整理一次要动几百条。用 <see cref="AddTagAsync"/> 逐条调的话，
    /// 一个条目两个标签就要开 2 次连接、起 2 个事务、把同一个条目的 search_text 重建 2 遍、
    /// 通知 DataChangeHub 2 次——"给 500 条打标签"从几秒变成几十秒。
    /// 这里把往返压成：<b>连接 1 次、每个条目最多重建一次索引、整批通知一次</b>。
    /// </para>
    /// <para>
    /// 语义与逐条调用一致：标签<b>只追加不替换</b>（用户手打过的不动）、已关联的跳过、
    /// 标签名大小写不敏感地复用同一行、每个发生变化的条目写一条「修改」活动。
    /// 一个条目都不需要变化时<b>不发任何写语句</b>（幂等重放不该留下活动流水）。
    /// </para>
    /// </summary>
    Task<int> TagItemsAsync(IReadOnlyList<ItemTagAssignment> assignments, CancellationToken ct = default);

    /// <summary>
    /// 取"还没有任何标签"的条目，按浏览页同一套最近优先顺序，最多 <paramref name="limit"/> 条。
    /// <para>
    /// <b>这个条件只能在 SQL 里判</b>：先取一批再看有没有标签，取的那一批是有窗口的——
    /// 库里最近几千条都打了标签时，早期那些没标签的条目根本进不了窗口，
    /// 于是界面会说"没有待整理的条目"，而实际上还有几百条。少报的这类错比报错更难发现。
    /// </para>
    /// <para>返回的条目 <see cref="Item.Tags"/> 恒为空（这就是筛选条件本身），
    /// 因此不跑每行一次的 GROUP_CONCAT 子查询。</para>
    /// </summary>
    Task<IReadOnlyList<Item>> GetUntaggedAsync(IReadOnlyList<ItemType> types, int limit, CancellationToken ct = default);
}

/// <summary>"给这个条目追加这些标签"。<see cref="IItemRepository.TagItemsAsync"/> 的入参。</summary>
public sealed record ItemTagAssignment(long ItemId, IReadOnlyList<string> Tags);

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
