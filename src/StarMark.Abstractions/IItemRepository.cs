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

    /// <summary>
    /// 给条目打标签。幂等。
    /// <para><b>返回值＝这次真的挂上了一个新挂接吗</b>（P-40）：<c>false</c> 只有一种解释——<b>这个挂接本来就存在</b>。
    /// （目标条目不存在时<b>不是</b>返回 <c>false</c>，而是抛：<c>INSERT OR IGNORE</c> 只咽 UNIQUE/主键/CHECK 那类冲突，
    /// <b>外键违例照常报错</b>——这是本批复验时纠正的一条旧说法。）
    /// 没变化就不广播，所以基元内部<b>连带 <c>DataChangeHub.Notify()</c> 一起不发</b>。</para>
    /// </summary>
    Task<bool> AddTagAsync(long itemId, string tagName, CancellationToken ct);

    /// <summary>
    /// 摘掉一个标签挂接。幂等。<b>返回值＝这次真的删掉了一行挂接吗</b>（P-40，理由同 <see cref="AddTagAsync"/>）。
    /// </summary>
    Task<bool> RemoveTagAsync(long itemId, string tagName, CancellationToken ct);

    /// <summary>笔记。</summary>
    Task<string?> GetNoteAsync(long itemId, CancellationToken ct);

    /// <summary>
    /// 写笔记。<b>返回值＝<c>UPDATE ... WHERE id</c> 真的落到了那一行吗</b>（P-40）。
    /// <para>SQLite 对"值没变"的 UPDATE 也报 1 行，所以 <c>false</c> 只有一个解释：<b>那条条目已经不在了</b>
    /// （被并发改删）。这种写必须让调用方知道——否则界面上笔记框显示着新内容、活动流记下一笔"修改"，
    /// 而库里什么都没有（与 <see cref="DeleteClipboardEntryAsync"/> 已有的 bool 约定同一条口径）。</para>
    /// </summary>
    Task<bool> SetNoteAsync(long itemId, string content, CancellationToken ct);

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

    /// <summary>
    /// 设置条目隐藏状态。<b>返回值＝真的落到了那一行吗</b>（P-40，判读同 <see cref="SetNoteAsync"/>：
    /// <c>false</c> ⇒ 那条条目已经不在了，调用方不许把界面翻成"已隐藏"就完事）。
    /// </summary>
    Task<bool> SetHiddenAsync(long itemId, bool hidden, CancellationToken ct);

    /// <summary>
    /// 设置条目置顶状态（用户状态，同步不覆盖）。<b>返回值＝真的落到了那一行吗</b>（P-40，同上）。
    /// </summary>
    Task<bool> SetPinnedAsync(long itemId, bool pinned, CancellationToken ct);

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

    /// <summary>
    /// 列出"由 RSS 收藏动作写进库"的那些链接（一次连接、只读 <c>source_id</c> 一列、<b>不带行数窗口</b>）。
    /// <para>为什么不用 <see cref="GetBySourceAsync"/> 再在内存里筛：那个调用有 <c>limit</c>（默认 1000），
    /// 落在窗口外的已收藏条目会被当成"没收藏过"，于是 RSS 页上那颗按钮说假话——
    /// <b>少报比报错更难被发现</b>（批次 PA 的同一教训：界面报"没有"，用户就信了"没有"）。</para>
    /// <para>作用域同样写进 SQL：<c>source='local'</c> + <c>type=bookmark</c> + 前缀，
    /// 只认这一族 <c>source_id</c>，别处生产的同 URL 书签不会被算进来。</para>
    /// </summary>
    Task<IReadOnlyList<string>> GetCollectedRssLinksAsync(CancellationToken ct = default);

    /// <summary>
    /// 按来源 + source_id 删除一条本地条目。<b>返回值＝真的删掉了一行吗</b>（P-40）。
    /// <para>这里 <c>false</c> 是<b>合法的幂等结果</b>（同步/收藏那一路可能重复调到同一键），所以基元只负责
    /// "没删到就不广播、也不去碰 clip 目录"，<b>不</b>替调用方把它报成失败；要不要说、怎么说归调用方。</para>
    /// </summary>
    Task<bool> DeleteBySourceIdAsync(string source, string sourceId, CancellationToken ct = default);

    /// <summary>写入本地条目（待办/随记）：只写 items 表，不写活动流、不跑 UriNormalizer/LanguageDetector。</summary>
    /// <param name="activity">带上它，那一笔活动就<b>与本次写落在同一事务里</b>（调用方因此不必
    /// 为了"记一笔"再开一次库）。活动的主体字段（键/标题/URI）<b>由本方法从这条 item 现推</b>——
    /// 调用方只说"这一笔算什么"，于是时间线里不可能出现一条与库里内容对不上的标题。</param>
    Task UpsertLocalItemAsync(Item item, CancellationToken ct = default, ActivityKind? activity = null);

    /// <summary>
    /// 记录一条剪贴板历史（source=<see cref="ItemSources.Clipboard"/>）：按 (source, source_id) 幂等，
    /// 同一段文本再次复制 = 移回最近 + 复制次数累加，并<b>保留</b>用户在该条上的置顶/隐藏/笔记/标签。
    /// 不写活动流；落库后按上限轮转，置顶条目豁免删除。返回带真实 Id 与合并后 extra_json 的条目。
    /// </summary>
    /// <remarks>
    /// 轮转<b>分桶</b>：这一行是图片还是文本/文件，由它自己的 <c>extra_json.clipFormat</c> 决定，
    /// 不是由调用方说——所以两个上限都传进来、各归各的桶。§4 给图片和文本两条独立线（200 / 500），
    /// 混成一桶的话"复制满 500 段文字"会把用户的截图裁出历史，反过来也一样，而两种都看不出成因。
    /// 图片行被裁掉时，实现还要<b>尽力删掉它记着的主图与缩略图</b>（文件不在事务里，删不掉就出声，
    /// 由启动对账数成孤儿）。
    /// </remarks>
    /// <param name="maxEntries">非图片那一路的上限（名字沿用不改：既有文本测试的调用点都写它）。</param>
    /// <param name="imageMaxEntries">图片那一路的上限。</param>
    Task<Item> RecordClipboardAsync(Item draft, CancellationToken ct = default,
        int maxEntries = Clipboard.ClipboardPolicy.MaxEntries,
        int imageMaxEntries = Clipboard.ClipboardPolicy.DefaultImageMaxEntries);

    /// <summary>清空全部剪贴板历史（含置顶条目），返回删除条数。图片行按行记着的文件名一并尽力删文件。</summary>
    Task<int> ClearClipboardHistoryAsync(CancellationToken ct = default);

    /// <summary>
    /// 删除<b>一条</b>剪贴板历史，返回是否真的删掉一行。实现必须在 SQL 里同时限定 Id 与
    /// <c>source='clipboard'</c>——否则误传的 Id 会连书签 / Star / 待办一起删掉。
    /// </summary>
    Task<bool> DeleteClipboardEntryAsync(long itemId, CancellationToken ct = default);

    /// <summary>
    /// 删掉若干条<b>本机文件条目</b>（库里 <c>type=file</c> 且 <c>source</c> 是 filesystem/local 的那些行），
    /// 返回真正删掉的行数。实现必须在 SQL 里同时限定 Id 与 (type, source)——否则误传的 Id 会连书签 / Star / 待办一起删掉。
    /// <para>⚠ <b>只删行，绝不碰磁盘上的文件</b>：那一行只是我们自己库里的索引，文件归用户（剪贴板图片那一族
    /// 删行时顺带删文件，是因为那些附件是写进我们目录的，两类所有权不同）。</para>
    /// </summary>
    Task<int> DeleteFileEntriesAsync(IReadOnlyList<long> itemIds, CancellationToken ct = default);

    /// <summary>
    /// 列出<b>所有</b>图片行的文件名与缺失标记，供启动对账（§3-Q6 三分类）使用。
    /// <para>实现必须无行数窗口：借一个带 limit 的查询来对账，窗口外的行会被当成"没人认领的文件"，
    /// 于是本来完好的目录被报成一堆孤儿。也只需文件名——把正文捞进来是白付的内存。</para>
    /// </summary>
    Task<IReadOnlyList<Clipboard.ClipboardEntry.ClipAssetRow>> GetClipboardImageAssetsAsync(CancellationToken ct = default);

    /// <summary>
    /// 批量置 / 清 <c>clipMissing</c>，返回<b>真正改动</b>的行数（已经标着的 / 坏 JSON 的行都不算，
    /// 所以调用方可以用"要改的条数 − 返回条数"发现有一批行标不上去）。
    /// <para>置标记是<b>唯一</b>允许因文件缺失而写回这些行的动作；<b>清库与删行都不许反过来删文件</b>，
    /// 那些没人认领的文件只能数出来给用户看（决议 §3-Q6 第二类）。</para>
    /// </summary>
    Task<int> SetClipboardMissingFlagsAsync(
        IReadOnlyList<long> markMissing, IReadOnlyList<long> clearMissing, CancellationToken ct = default);

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

    /// <summary>
    /// 把某个条目的标签集合<b>整体设成</b> <paramref name="desired"/>（差集写入），一次连接、一次事务、一次通知。
    /// <para>
    /// 为什么要有这个方法：标签编辑器是"改一次标签、库里跑好几趟"最典型的地方——
    /// 逐条 <c>AddTagAsync</c>/<c>RemoveTagAsync</c> 时每个标签各开一次库、各重建一次 search_text、
    /// 各通知一次组件；加五个删三个就是十六次往返，而其中每次"重建索引"用的都是半成品状态。
    /// 中间态本身还是个正确性问题：<b>search_text 会被按最终的标签集重建一次才对</b>。
    /// </para>
    /// <para>没有净变化时不写不通知不记活动（原样保存不该在时间线里留下"我改过"）。</para>
    /// </summary>
    Task<TagEditResult> SetItemTagsAsync(long itemId, IReadOnlyList<string> desired, CancellationToken ct = default);

    /// <summary>
    /// 按给定顺序把 <c>order</c> 写回这批本地条目（待办/随记的拖动排序），返回真正改了写法的条目数。
    /// <para>
    /// 一次连接、一次事务，<b>每条只更新 extra_json 与 updated_at 两列</b>。
    /// 之前是逐条 <c>UpsertLocalItemAsync</c>：拖一次 20 条的待办要开 20 次库，
    /// 而且每次都按整行重写并<strong>重建一次全文索引</strong>——顺序根本不在 search_text 里，
    /// 那 20 次索引重建是纯粹的浪费，还顺手把"整行覆盖"的风险带进一个只该改一个数字的动作。
    /// </para>
    /// <para>顺序与现状一致的条目<b>不写</b>；整批都没变时不发任何写语句、不通知。</para>
    /// </summary>
    Task<int> ReorderLocalItemsAsync(IReadOnlyList<long> orderedIds, CancellationToken ct = default);

    /// <summary>
    /// <b>一次事务</b>登记多条实时源虚拟条目（一次拖入 N 个文件），返回真正拿到 Id 的条数。
    /// 逐条 <see cref="RecordItemAsync"/> 的形状是"每项一趟往返"的典型：N 个文件就是 N 次开库
    /// （每次还要跑三遍 PRAGMA）、N 个事务、N 次组件通知。
    /// </summary>
    /// <remarks>
    /// 语义与逐条调用完全一致：缺 <c>Source</c>/<c>SourceId</c> 业务键的<b>不写</b>、
    /// 同一 <c>(source, source_id)</c> 幂等合并、保留既有 hidden/pinned/notes、按最终内容重建 search_text。
    /// 差别在于<b>整批要么全记要么全不记</b>（一个拖放动作是一个动作），
    /// 且整批都没键时连库都不开。
    /// 同一批里出现两次的路径<b>只登记一次</b>，所以返回值是"登记了几条"而不是"收了几项"。
    /// </remarks>
    Task<int> RecordItemsAsync(IReadOnlyList<Item> items, CancellationToken ct = default);

    /// <summary>
    /// <b>一次连接</b>写入一批活动事件，环形缓冲口径与单条 <see cref="LogActivityAsync"/> 相同（只留最近 500 条）。
    /// 空集合不开库。
    /// </summary>

    /// <summary>
    /// 按 id 取回<b>一条</b>本地条目（待办/随记组件每次编辑都要先拿到它）。
    /// <para>
    /// 为什么不用 <see cref="GetBySourceAsync"/> 再在内存里筛：那个调用会把该类型的全部本地条目
    /// （默认上限 1000，每条还带一次标签 <c>GROUP_CONCAT</c>）读回来，只为了用其中一行——
    /// 用户有 300 条待办时，勾一次框就要物化 300 行。
    /// </para>
    /// <para><c>source='local'</c> 与 <paramref name="type"/> 一律<b>在 SQL 里限定</b>：
    /// 拿着一个来自别处的 id（书签 / Star / 剪贴板）来改，这里必须回 null，
    /// 否则组件就能把整行 <c>extra_json</c> 写到别人的记录上。<b>隐藏的行照样取得到</b>
    /// （撤销与删除要能作用在自己刚操作过的那一行上，隐藏不是"不许改"的信号）。</para>
    /// </summary>
    Task<Item?> GetLocalItemAsync(long id, ItemType type, CancellationToken ct = default);

    /// <summary>
    /// 删掉一条本地条目，并在<b>同一连接同一事务</b>里（可选）记一笔活动，返回被删掉的那一行（供撤销用）；
    /// 没删到就返回 null，此时<b>活动也不记</b>。
    /// <para>作用域与 <see cref="GetLocalItemAsync"/> 同样在 SQL 里限定，故一次删除既不用先把整表读回来找那一行，
    /// 也不可能删到别处的记录。</para>
    /// <para>删除与活动同事务：否则中途出点问题就是「条目没了、时间线里却找不到这一笔」——
    /// 而时间线正是用户回头查自己改动时唯一能看的证据。</para>
    /// </summary>
    Task<Item?> DeleteLocalItemAsync(long id, ItemType type, ActivityKind? activity = null, CancellationToken ct = default);

    /// <summary>
    /// 列出"当前确实有本地条目（待办/随记）"的组件实例 id（去重、按 Ordinal 排序）。
    /// <b>一次连接、只读 <c>source_id</c> 一列</b>（不跑每行一次的标签 <c>GROUP_CONCAT</c>），
    /// 实例归属一律交给 <c>LocalItemState.DecodeInstanceId</c> 解——<b>编码规则只有一份，
    /// 这里不再另写一套 SQL 字符串切割</b>（否则改了编码，这条就会安静地漏人）。
    /// <para>用途：快照要为每个实例捕获/还原本地条目，而十几个实例里通常只有两三台真的有内容；
    /// 逐个实例各开一次库去"确认它是空的"，代价全花在空集上。</para>
    /// </summary>
    Task<IReadOnlyList<string>> GetInstancesWithLocalItemsAsync(CancellationToken ct = default);
    Task LogActivitiesAsync(IReadOnlyList<ActivityDraft> events, CancellationToken ct = default);
}

/// <summary>"往活动流记这一笔"。<see cref="IItemRepository.LogActivitiesAsync"/> 的入参。</summary>
public sealed record ActivityDraft(ActivityKind Kind, string? ItemKey, string Title, string? Uri);

/// <summary>一次标签编辑的结果。<b>把"有没有变"回报清楚，界面才知道要不要刷新</b>。</summary>
public sealed record TagEditResult(bool Changed, int Added, int Removed, IReadOnlyList<string> FinalTags)
{
    public static readonly TagEditResult Unchanged = new(false, 0, 0, Array.Empty<string>());
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
