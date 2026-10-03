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

    /// <summary>
    /// 不可用时，"它到底找过什么 / 缺的是什么"——诊断面板把这句原样显示出来。
    /// <para>
    /// 为什么要有它：只报一个"不可用"，用户无从分辨<b>这台机器压根没装</b>与<b>装了但读不到</b>，
    /// 也就不知道"Edge 能用、Chrome 不可用"是正常现象还是坏了（文案不许比已知事实更含糊）。
    /// 走默认实现，所以既有源与测试桩都可以不动；愿意说细节的源各自覆写。
    /// </para>
    /// </summary>
    string? AvailabilityHint => null;

    /// <summary>从源拉取全量条目，写入 items 表。同步协调器调用。</summary>
    Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct);

    /// <summary>
    /// <b>载荷已经落库</b>，源可以提交自己的检查点了（ETag / 游标那一类）。默认什么都不做。
    /// <para>
    /// 为什么要有它（P-19）：检查点与载荷不在同一个原子边界里＝一次崩就永久少一批。
    /// 从前 <c>GitHubSource.FetchAsync</c> 在自己内部就把新 ETag 写进 <c>sync_state</c>，
    /// 而 upsert 是协调器拿到返回之后才做的：中间崩溃 ⇒ 下一轮首页带 <c>If-None-Match</c> 命中 304、
    /// 返回零条 ⇒ <b>上一轮已拉到却没落库的那批 Star 再也不会被重新拉回来</b>。
    /// 现在顺序固定成"拉 → 落库 → 提交检查点"，任何一步失败都只会导致<b>下一轮多拉一次</b>，不会少数据。
    /// </para>
    /// <para>
    /// 走默认实现（与 <see cref="AvailabilityHint"/> 同一条路子）：既有源与测试桩都可以不动，
    /// 只有"自己带检查点"的源需要覆写——加成员不改老成员签名，就不会牵连整条同步链。
    /// </para>
    /// </summary>
    Task CommitCheckpointAsync(CancellationToken ct) => Task.CompletedTask;

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
    public int MaxResults { get; init; } = AppConstants.DefaultMaxResults;

    /// <summary>偏移量（分页）。</summary>
    public int Offset { get; init; }

    /// <summary>是否包含隐藏条目（默认排除）。</summary>
    public bool IncludeHidden { get; init; }

    /// <summary>排序：relevance(默认，按 FTS 相关度) / recent / starred / collected(最近收藏) / stars / name。
    /// <para><c>starred</c> 排的是 <c>extra_json.StarredAt</c>，而 GitHub 源今天把那颗<b>填成仓库最后一次 push
    /// 时间</b>（不是"用户点 Star 的时间"，P-6）。因此展示这一档的地方必须说"推送"而不是"Star"——
    /// <c>GitHubTruthInLabelingGateTests</c> 钉的就是这三环那条链。哪天真去取 starred_at 它会当场红，
    /// 数据变准确的同一天才轮得到把标签改短。</para></summary>
    public string? Sort { get; init; }

    /// <summary>
    /// 语言筛选（GitHubStar 主语言，取 extra_json 的 Language 字段）。
    /// null/空表示不过滤。对齐扩展搜索工具栏的语言维度，见对比方案 P2-7。
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// 标签过滤（AND 语义）：条目必须同时具备这里列出的全部标签。
    /// null 或空集合表示不过滤。对齐浏览器扩展 search-worker 的
    /// <c>opts.tags.every(t =&gt; item.tags.includes(t))</c>。
    /// </summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// 本地磁盘检索的附加 Everything 检索式片段（搜索栏「类型」多选翻译而来，见
    /// <see cref="FileKindQuery.Fragments"/>）。只有 <c>EverythingSource</c> 消费它：
    /// 其余来源（书签 / Star / 剪贴板）没有扩展名与体积概念，片段对它们是噪音。
    /// </summary>
    public IReadOnlyList<string>? FileQueryFragments { get; init; }

    /// <summary>是否有生效的标签过滤。</summary>
    public bool HasTags => Tags is { Count: > 0 };

    /// <summary>
    /// 复制本条件、只把取数窗口改成 <paramref name="maxResults"/>（并把偏移归零）。
    /// <para>
    /// 分页语义是"合并去重后再切片"（见 <c>SearchService</c> 与 P-42）：每条腿一律<b>从 0</b> 取到
    /// "本页末"，所以腿侧只需要一个更大的 MaxResults，<b>不必也不应</b>理解 Offset——这样任何
    /// IItemSource 实现都不会出现"忘了把窗口加宽 ⇒ 深页静默少一批"的坑。
    /// </para>
    /// </summary>
    public SearchFilter WithFetchWindow(int maxResults) => new()
    {
        Type = Type,
        StarsMin = StarsMin,
        DateFrom = DateFrom,
        IncludeSize = IncludeSize,
        IncludeDate = IncludeDate,
        MaxResults = maxResults,
        Offset = 0,
        IncludeHidden = IncludeHidden,
        Sort = Sort,
        Language = Language,
        Tags = Tags,
        FileQueryFragments = FileQueryFragments,
    };
}
