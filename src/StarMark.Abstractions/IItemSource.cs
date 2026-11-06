#nullable enable
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.I18n;

namespace StarMark.Abstractions;

/// <summary>
/// 统一条目源接口。本地文件、GitHub Stars、浏览器书签、Ditto 剪贴板均实现此接口。
/// 对应技术文档 §3.2。
/// </summary>
public interface IItemSource
{
    /// <summary>来源标识（filesystem / chrome / github / ditto ...）。见 <see cref="ItemSources"/>。</summary>
    string SourceId { get; }

    /// <summary>检测依赖是否可用（如 Everything 是否运行、浏览器是否安装）。</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// <see cref="IsAvailable"/> 为真时，它<b>靠什么成立</b>（片212·P-47）：同一个词「可用」底下
    /// 压着三种不同的事实——配了 Token、本机有它的数据文件、外部程序正在运行。诊断面板的
    /// 可用句按它换说法，排障的人才知道「下一步该做什么」（补配置／去启动外部程序／本机本来就没有）。
    /// <para>走默认实现（与 <see cref="AvailabilityHint"/> 同一条路子）：既有源与测试桩都可以不动；
    /// 愿意说清自己那种事实的源各自覆写。</para>
    /// </summary>
    SourceAvailabilityKind AvailabilityKind => SourceAvailabilityKind.Configured;

    /// <summary>
    /// 不可用时，"它到底找过什么 / 缺的是什么"——诊断面板把这句显示出来。
    /// <para>
    /// 为什么要有它：只报一个"不可用"，用户无从分辨<b>这台机器压根没装</b>与<b>装了但读不到</b>，
    /// 也就不知道"Edge 能用、Chrome 不可用"是正常现象还是坏了（文案不许比已知事实更含糊）。
    /// 走默认实现，所以既有源与测试桩都可以不动；愿意说细节的源各自覆写。
    /// </para>
    /// <para><b>交的是 <see cref="LocMessage"/>，不是拼好的那句</b>（批次 WI-57，P-187）：这一句直接上屏幕，
    /// 而屏幕上那一行的语言由<b>贴上去的那一刻</b>决定。旧签名返回 <c>string</c> 时，语言在源算出它的
    /// 那一瞬间就定死了——换语言只会重贴外层「没有这项数据：」，屏幕上剩下半中半英（坑表 #361 同一族）。</para>
    /// <para><b>本接口从前还有一条 <c>DisplayName</c>，本批删掉而不是改交键</b>：它的读者只有同步汇总的
    /// 控制台输出，而"这个名字念什么"自本批起住在表里（<see cref="SourceNames"/>）。留着那份中文常量＝
    /// 同一句字两个主人、两份可各自被改（P-171 那一族），要日志里的名字就按 <see cref="SourceId"/> 现取。</para>
    /// </summary>
    LocMessage? AvailabilityHint => null;

    /// <summary>
    /// 从源拉取全量条目，写入 items 表。同步协调器调用。
    /// <para>
    /// <b>回执是信封不是裸列表（P-58）</b>：载荷之外，「这一轮被预算掐断、没拉全」那颗旗
    /// 跟着同一只信封走（<see cref="FetchResult.Truncated"/>）。旧签名只交列表 ⇒ 那颗旗没有出口，
    /// 界面上那句「同步完成」对超限用户是一句对不上账的话——数据面（预算复位）P-57 先落，
    /// 文案面靠这只信封把信号带到汇总那一段。
    /// </para>
    /// </summary>
    Task<FetchResult> FetchAsync(SyncContext ctx, CancellationToken ct);

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

    /// <summary>
    /// 这颗源的<b>全量拉取清单能不能当对账依据</b>（片215·P-5B）：能，同步协调器就在落库之后
    /// 按它把「源里已经没有」的旧行标上失联（<c>items.stale</c>，界面标灰）；不能，就一根指头都不碰。
    /// <para>
    /// 为什么默认 false 而不是谁都能对账：<b>「拉到了 N 条」与「源里一共只有这 N 条」不是同一句话</b>——
    /// 有拉取预算的源（GitHub 的 5000 顶）被掐断时，首页之外的合法历史会被当成失联行批量标灰
    /// （账本护栏③的原话：截断源做全量对账会误删首页之外的合法历史）。只有「没有预算、
    /// 每一轮都是完整清单」的源（书签族）才有资格举手。
    /// </para>
    /// <para>
    /// 走默认实现（与 <see cref="AvailabilityKind"/> 同一条路子）：既有源与测试桩都可以不动，
    /// 愿意担这份责任的源各自覆写。第二道闸（<see cref="FetchResult.Truncated"/>）由协调器现查，
    /// 两道闸都过才对账——源举手了但那一轮仍被掐断（未来的书签预算）时也不误标。
    /// </para>
    /// </summary>
    bool SupportsStaleReconciliation => false;

    /// <summary>从源实时搜索（不依赖 SQLite）。供统一搜索跨源混排使用。</summary>
    /// <param name="query">关键词。</param>
    /// <param name="filter">过滤条件（类型、来源、数值范围）。</param>
    Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct);
}

/// <summary>
/// 「这个源的『可用』靠什么成立」的三种事实（片212·P-47）：
/// <b>GitHub 配了 Token</b>、<b>书签/Ditto 在本机找到了数据文件</b>、<b>Everything 引擎正在运行</b>——
/// 旧的一句「可用」把三种不同的"可用"压成同一句话，用户排障时无从分辨
/// 「补配置就能用」「去启动那个程序」与「这台机器本来就没有它的数据」。
/// </summary>
public enum SourceAvailabilityKind
{
    /// <summary>可用性靠配置成立（如 GitHub Token 已配置）。</summary>
    Configured,

    /// <summary>可用性靠本机数据文件在场成立（书签文件、Ditto 数据库）。</summary>
    FilePresent,

    /// <summary>可用性靠外部程序正在运行成立（Everything 引擎）。</summary>
    DependencyRunning,
}

/// <summary>
/// <see cref="IItemSource.FetchAsync"/> 的回执信封（P-58）：载荷之外再带一颗「这一轮没拉全」的旗。
/// <para>
/// 为什么是信封而<b>不是</b>源上一颗可查属性：读属性那一拍与拉取那一拍分开，中间任何一次新的拉取
/// （哪怕来自另一颗线程上的调用方）都能把旗翻面，读到的就不知是哪一轮的；旗跟着那一轮的载荷
/// 装在同一只信封里，两份永远同源。信封优于源上挂可变旗，正如检查点优于挂在客户端上的游标。
/// </para>
/// <para>
/// 今天唯一举旗的成因是 GitHub 那颗拉取预算（<c>GitHubClient.MaxStarredItems</c>，P-57 落的那条腿）；
/// 本地三颗源恒 <see cref="Truncated"/> = false。新源若有同族成因，在自己那只信封上举旗即可，
/// 协调器到文案的那条链不用再动。
/// </para>
/// </summary>
public sealed record FetchResult(IReadOnlyList<Item> Items, bool Truncated)
{
    /// <summary>没被掐断的一轮的惯用形：本地三颗源与测试桩都从这一颗进。</summary>
    public static FetchResult Full(IReadOnlyList<Item> items) => new(items, false);
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
    /// <para><c>starred</c> 排的是 <c>extra_json.StarredAt</c>，GitHub 源自 P-6A 起装的是<b>真 starred_at</b>
    /// （用户点 Star 的时刻，star+json 信封取回），档名「最近 Star」与数据对得上——
    /// <c>GitHubTruthInLabelingGateTests</c> 钉的就是「标签↔SQL↔取值」那条链，哪条腿退化它当场红。</para></summary>
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

/// <summary>
/// 「这一轮读取源数据失败」的可识别异常（P-28B′）：Ditto 的库被独占、书签文件正被浏览器写入
/// 这类<b>源侧读取故障</b>由实现抛出，<c>SyncCoordinator</c> 据此把这一轮记成
/// 「源读取失败」而不是笼统的「同步内部故障」——用户在汇总行看到的是「哪个源读不出来」，
/// 而不是一句对不上账的「同步完成」或「未知错误」。
/// <para>
/// 为什么住在这一档：抛它的（Integrations 的源实现）与接它的（Core 的协调器）中间隔着
/// 依赖方向 <c>Core → Integrations → Abstractions</c>，两边同时可见的只有 Abstractions。
/// </para>
/// <para>
/// 与 <see cref="OperationCanceledException"/> 的分工：取消不是故障，协调器对 OCE 走
/// rethrow／静默两条既有路；只有<b>真读取失败</b>才包成本异常往上递。
/// </para>
/// </summary>
public sealed class SourceReadException : Exception
{
    public SourceReadException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
