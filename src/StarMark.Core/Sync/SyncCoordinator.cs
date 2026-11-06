#nullable enable
using System.Diagnostics;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Integrations.GitHub;

namespace StarMark.Core.Sync;

/// <summary>
/// 同步协调器。对应技术文档 §4.7 sync_state 与 §3.2 SyncCoordinator。
/// 职责：
///   - 遍历所有 IItemSource，串行 FetchAsync（避免 GitHub API 限流叠加）
///   - 全量结果批量 UpsertAsync 到 DB
///   - 返回汇总结果，供 UI 显示
/// </summary>
public sealed class SyncCoordinator
{
    private readonly IItemRepository _repository;
    private readonly IReadOnlyList<IItemSource> _sources;
    /// <summary>重入闸（P-31 (C)）：一轮同步没收尾时，后来的调用<b>排队</b>而不是并发跑、也不是丢弃。</summary>
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    public SyncCoordinator(IItemRepository repository, IEnumerable<IItemSource> sources)
    {
        _repository = repository;
        _sources = sources.ToList();
    }

    /// <summary>
    /// 执行一次全量同步。每个源拉取后立即 upsert（避免缓存全部结果占用内存）。
    /// <para>
    /// P-31 (C)：重入＝<b>排队</b>。上一轮还没收尾时再点一次同步，这一轮在闸外等它收尾、然后自己跑——
    /// 每个请求都被兑现（丢弃的话「点了同步没反应」就成了偶发且无从解释），并发跑则两头读改写同一批行。
    /// 排队的那一轮也吃取消（<c>WaitAsync</c> 挂着令牌：调用方一取消就撤队，⛔ 傻等）；闸的收与放
    /// 都在 <c>finally</c> 里对齐——任何一条出口都不许把闸卡死，卡死等于从此再也同步不了。
    /// </para>
    /// </summary>
    public async Task<SyncSummary> SyncAllAsync(CancellationToken ct)
    {
        // 门口先回绝已取消的令牌：ThrowIfCancellationRequested 抛的正是 OperationCanceledException 本型；
        // ⛔ 让它走到下面 WaitAsync——那里抛的是子类 TaskCanceledException，「整轮被取消」与「闸没等到」
        // 两种口径就分不开了（取消的那一发不该被当成一次同步结果，见 LastSummary 的注）。
        ct.ThrowIfCancellationRequested();
        // 预检只探不占（⛔ Wait(0)：那会真的把闸拿到手，随后的 WaitAsync 就成了自己等自己）：
        // 闸被占＝本轮要排队，留一行可观测的日志——用户侧的症状是「连点了两次」，日志得能看出第二次在等。
        if (_syncGate.CurrentCount == 0)
            StarLog.Info("另一轮同步尚未收尾，本轮排队等候…");
        await _syncGate.WaitAsync(ct);
        try
        {
            return await SyncAllCoreAsync(ct);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<SyncSummary> SyncAllCoreAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var results = new List<SourceSyncResult>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        StarLog.Info($"同步开始，共 {_sources.Count} 个源");

        foreach (var source in _sources)
        {
            if (!source.IsAvailable)
            {
                StarLog.Warn($"源 {source.SourceId} 不可用（未配置或依赖未运行）");
                results.Add(new SourceSyncResult
                {
                    SourceId = source.SourceId,
                    Success = true,
                    // P-38 (A)：Success 那颗布尔语义一字未动（你从未启用的源不该每轮刷红），
                    // 但「没参与」与「拉了且成功」从这一刀起是两颗状态——第三态交展示层着色，
                    // ⛔ 让谁去读 Error 里那句话猜「这算不算成功」。
                    Skipped = true,
                    PulledCount = 0,
                    Error = LocMessage.Of("sync.src.unavailable"),
                });
                continue;
            }

            try
            {
                var fetch = await source.FetchAsync(new SyncContext(), ct);
                var items = fetch.Items;
                if (items.Count > 0)
                {
                    // 防御：本地条目（待办/随记，source=local）永不参与同步覆盖。
                    // 正常来源不会产出 local 条目，此处仅作保险，避免未来误覆盖。
                    var syncable = items.Where(i => i.Source != ItemSources.Local).ToList();
                    if (syncable.Count > 0)
                        await _repository.UpsertAsync(syncable, ct);
                }
                // 载荷已在库里，才允许源提交自己的检查点（ETag 那一类）。顺序反过来＝崩溃窗口：
                // ETag 先进库、条目没进 ⇒ 下一轮命中 304 返回零条 ⇒ 那批新数据永不补齐（P-19）。
                // 提交失败会让这一轮被记成"该源失败"，代价只是下一轮多拉一次，不会少数据。
                await source.CommitCheckpointAsync(ct);
                StarLog.Info($"源 {source.SourceId} 拉取 {items.Count} 条");
                // 失联对账（片215·P-5B）：源声明「我的全量清单足以对账」（SupportsStaleReconciliation）
                // 且这一轮没被预算掐断（!Truncated），才按现值清单把「源里已经没有」的旧行标灰
                // （置/清 items.stale，回归时同一轮 CASE 清位）。两道闸缺一不可：截断的清单
                // 当对账依据会把首页之外的合法历史批量误标（账本护栏③）。
                // 顺序放在检查点之后：对账不是这一轮的既定产出，崩在它身上不该连坐已落库的载荷与检查点。
                if (source.SupportsStaleReconciliation && !fetch.Truncated)
                {
                    var present = items.Where(i => i.Source != ItemSources.Local).ToList();
                    foreach (var group in present.GroupBy(i => i.Type))
                    {
                        var newlyStale = await _repository.ReconcileStaleAsync(source.SourceId, group.Key,
                            group.Select(i => i.SourceId).Distinct().ToList(), ct);
                        if (newlyStale > 0)
                            StarLog.Info($"源 {source.SourceId} 对账：{newlyStale} 条旧条目已不在源清单中，标为失联");
                    }
                }
                results.Add(new SourceSyncResult
                {
                    SourceId = source.SourceId,
                    Success = true,
                    PulledCount = items.Count,
                    // P-58：信封上那颗「这一轮没拉全」跟着载荷进结果。它与 Success 正交——拉到的
                    // 那批确实都进库了（Success 照旧 true），「没拉全」由文案那一链（Message／灰卡）自己说。
                    Truncated = fetch.Truncated,
                });
            }
            // 单源内部超时（HttpClient 抛的 TaskCanceledException 是 OperationCanceledException 子类）
            // 在顶层 ct 未取消时并非「用户取消整次同步」。旧实现一律无差别上抛 → 一个慢/卡住的源
            // 拖垮整批，且丢弃前面已拉取好的兄弟结果。与其余每个源的容错口径对齐：记为该源失败、
            // 继续下一个；仅当调用方确已取消（ct.IsCancellationRequested）才上抛中断整次同步。
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                StarLog.Error($"源 {source.SourceId} 拉取超时/被单独取消，已跳过该源并继续其余源");
                results.Add(new SourceSyncResult
                {
                    SourceId = source.SourceId,
                    Success = false,
                    PulledCount = 0,
                    Error = LocMessage.Of("sync.src.timeout"),
                    ErrorKind = SourceSyncErrorKind.Unknown,
                });
            }
            catch (OperationCanceledException) { throw; }   // 调用方真正取消整次同步 → 上抛
            catch (GitHubApiException gex)
            {
                StarLog.Error($"源 {source.SourceId} 同步失败（{gex.Kind}）: {gex.Message}");
                // 「为什么没成」这句话自本批起由表念（✅WI-81／P-212 走 B）：认得出档的那四档，
                // 分类本身就是用户要知道的事，而 gex.Message 是「GitHub 请求失败（429 TooManyRequests）」那种机器话——
                // 它留在上一行的日志里。只有认不出档的那一档才把原文当实参递进去（口径同 ✅WI-79 那四颗 RSS 出口）。
                var (kind, reason) = gex.Kind switch
                {
                    GitHubErrorKind.Auth => (SourceSyncErrorKind.Auth, LocMessage.Of("sync.src.auth")),
                    GitHubErrorKind.RateLimit => (SourceSyncErrorKind.RateLimit, LocMessage.Of("sync.src.rateLimit")),
                    GitHubErrorKind.Forbidden => (SourceSyncErrorKind.Forbidden, LocMessage.Of("sync.src.forbidden")),
                    GitHubErrorKind.Server => (SourceSyncErrorKind.Server, LocMessage.Of("sync.src.server")),
                    _ => (SourceSyncErrorKind.Unknown, LocMessage.Of("sync.src.githubOther", gex.Message)),
                };
                results.Add(new SourceSyncResult
                {
                    SourceId = source.SourceId,
                    Success = false,
                    PulledCount = 0,
                    Error = reason,
                    ErrorKind = kind,
                });
            }
            catch (SourceReadException srex)
            {
                // ✅P-28B′：源侧「读不到」的可识别失败（Ditto 库被独占、在场书签文件全数读败）——
                // 必须排在下面那颗兜底 Exception 之前、又与 GitHubApiException 并列：兜底那颗念的是
                // 「同步内部故障」（软件的错），这一颗说的是「这颗源的数据这一轮没读出来」（外部的错），
                // 用户据此知道下一步是「去把 Ditto 开着／稍后再试」，而不是「软件坏了」。
                // 底层那颗真异常留在日志里（inner 那句机器话不上屏幕，口径同 GitHubApiException 那条）。
                StarLog.Error($"源 {source.SourceId} 读取数据失败: {srex.InnerException?.Message ?? srex.Message}");
                results.Add(new SourceSyncResult
                {
                    SourceId = source.SourceId,
                    Success = false,
                    PulledCount = 0,
                    Error = LocMessage.Of("sync.src.read"),
                    ErrorKind = SourceSyncErrorKind.Read,
                });
            }
            catch (Exception ex)
            {
                StarLog.Error($"源 {source.SourceId} 同步失败: {ex.Message}");
                results.Add(new SourceSyncResult
                {
                    SourceId = source.SourceId,
                    Success = false,
                    PulledCount = 0,
                    Error = LocMessage.Of("sync.src.other", ex.Message),
                    ErrorKind = SourceSyncErrorKind.Unknown,
                });
            }
        }

        sw.Stop();
        StarLog.Info($"同步结束，耗时 {sw.ElapsedMilliseconds}ms，失败 {results.Count(r => !r.Success)} 源");
        var summary = new SyncSummary
        {
            StartedAt = now,
            ElapsedMs = sw.ElapsedMilliseconds,
            Sources = results,
        };
        // 记一次、发一次，界面上谁要读都读同一颗（P-212 走 (B) 的后半：设置页那颗 GitHub 卡也要说得出
        // "哪一源为什么没成"，而提示卡 9 秒就收了）。这里⛔ 造第二份汇总给事件——订阅者拿到的
        // 就是 LastSummary 那一颗，两份读数从此不可能分岔。
        LastSummary = summary;
        SyncCompleted?.Invoke(summary);
        return summary;
    }

    /// <summary>
    /// 本进程内<b>最后一次跑完的</b>同步汇总；null＝这一场还没跑完过一次。
    /// <para>为什么⛔ 落盘：落盘就要回答"跨重启那一句还算不算数"，而屏幕需要的是"这一场同步跑完了什么"；
    /// 新启动时这一格收着（⛔ 拿旧档的句子冒充刚刚发生过的事）。</para>
    /// <para>被取消的那一发走不到这里：源抛 <see cref="OperationCanceledException"/> 时，带
    /// <c>when (!ct.IsCancellationRequested)</c> 的那一条 catch 管"单源自己超时"（记成一源失败、其余继续），
    /// 顶层令牌确实取消了才由下一条上抛中断整次同步。取消⛔ 是"一次结果"，把上一发的读数抹掉反而说了假话。</para>
    /// </summary>
    public SyncSummary? LastSummary { get; private set; }

    /// <summary>每一发跑完时发一次，实参＝<b>就是</b> <see cref="LastSummary"/> 那一颗对象。</summary>
    /// <remarks>发在 <c>SyncAllAsync</c> 的续跑线上（通常＝UI 线程，因为调用方是界面上的命令），
    /// 但"通常"⛔ 是判据 ⇒ 订阅者自己 marshal（设置页那一格进 <c>DispatcherQueue</c>）。</remarks>
    public event Action<SyncSummary>? SyncCompleted;
}

/// <summary>单次同步的汇总报告。</summary>
public sealed class SyncSummary
{
    public long StartedAt { get; init; }
    public long ElapsedMs { get; init; }
    public List<SourceSyncResult> Sources { get; init; } = new();

    public int TotalPulled => Sources.Sum(s => s.PulledCount);
    public int FailedCount => Sources.Count(s => !s.Success);
    /// <summary>没参与的源几颗（P-38 (A)）：不可用被跳过的那些——既⛔ 计进失败、也⛔ 与「拉了且成功」
    /// 同形。主窗那张灰卡的判据读的就是它。</summary>
    public int SkippedCount => Sources.Count(s => s.Skipped);
    /// <summary>「拉了但没拉全」的源几颗（P-58）：被各自那颗拉取预算掐断的那批（今天只有 GitHub）。
    /// 与 <see cref="SkippedCount"/> 同一族正交读数——<see cref="SourceSyncResult.Success"/> 照旧 true，
    /// 它只负责把「没拉全」从日志浮到汇总那句话与灰卡，⛔ 顶替失败那颗数。</summary>
    public int TruncatedCount => Sources.Count(s => s.Truncated);

    /// <summary>
    /// 这一次同步的<b>那句汇总话（还没取词）</b>：键＋读数交出去，由界面在贴上去的那一刻按当时的语言取词
    /// （批次 WI-2m；口径同 <see cref="LocMessage"/>——在这里先 Render 一次＝外层跟着语言翻、
    /// 这一行留在中文，屏幕上就是半中半英，且换语言那一刻没人重贴）。
    /// <para>P-58 起是三链：失败优先（对不上账最重的那句），没失败但有源没拉全时说清
    /// 「几个源没拉全」（<c>top.syncDoneTruncated</c>），两样都没有才落回那句「同步完成」。</para>
    /// </summary>
    public LocMessage Message()
        => FailedCount > 0
            ? LocMessage.Of("top.syncPartial", TotalPulled, FailedCount, ElapsedMs)
            : TruncatedCount > 0
                ? LocMessage.Of("top.syncDoneTruncated", TotalPulled, TruncatedCount, ElapsedMs)
                : LocMessage.Of("top.syncDone", TotalPulled, ElapsedMs);

    /// <summary>
    /// 失败的那几源<b>逐源一行「谁＋为什么」</b>（批次 WI-81，P-212 走 B 那一头）。
    /// <para>为什么非要有这一颗：<see cref="Message"/> 只报"失败 N 源"那颗数，而屏幕前的用户问的是
    /// "哪一源、为什么"——答案决定下一步动作（换 Token／等一会儿再同步／去把 Ditto 开着）。
    /// 从前那句"为什么"是 Core 里写死的中文常量，而它连一个屏幕上的读者都没有（＝P-212 撞出的形状）。</para>
    /// <para>行与行之间那根<b>换行</b>是本方法那颗 <c>gap</c> 的<b>默认实参</b>（宿主按自己那一格可以改递），
    /// 它当实参进 <c>sync.src.lineJoin</c> 的 <c>{2}</c> 槽——是布局而⛔ 进表（规则 #100，
    /// 先例＝<c>clip.clean.lineJoin</c>／<c>settings.data.backup.lineJoin</c>）。
    /// 源名交的是 <see cref="SourceNames.NameOf"/> 那颗 <see cref="LocMessage"/>，⛔ 在这里念成中文：
    /// 嵌套的那一句要到贴上去那一刻才按当时的语言取词（✅WI-57／P-187）。</para>
    /// </summary>
    /// <remarks>没有失败源时返回 null：调用方⛔ 拿一颗"空"去顶在一句已完成的消息下面。
    /// 行怎么挑、怎么接住住在 <see cref="LinesWhere"/> 那条共用手臂里——跳过名单那一半
    /// （<see cref="SkippedLines"/>）与它同一条手臂，键与接合子只有一份主人。</remarks>
    public LocMessage? FailureLines(string gap = "\n") => LinesWhere(s => !s.Success, s => s.Error, gap);

    /// <summary>被跳过的那几源<b>逐源一行「谁＋为什么没参与」</b>（P-38 (A)）：主窗那张灰卡的正文。
    /// <para>「为什么没参与」复用不可用那颗键——跳过⛔ 是一种新的失败原因，不该另造一句对不上账的话；
    /// 行与行的接法与 <see cref="FailureLines"/> 完全同一条手臂，只有挑哪几行那颗谓词不同。</para></summary>
    /// <remarks>没有跳过的源时返回 null：调用方⛔ 拿一颗"空"去顶在标题下面。</remarks>
    public LocMessage? SkippedLines(string gap = "\n") => LinesWhere(s => s.Skipped, s => s.Error, gap);

    /// <summary>没拉全的那几源<b>逐源一行「谁＋上限那句话」</b>（P-58）：主窗那张灰卡的第二份名单。
    /// <para>这一行的「为什么」不读 <see cref="SourceSyncResult.Error"/>——没拉全不是失败，
    /// <c>Error</c> 在这一格里是 null；那颗键（<c>sync.src.truncated</c>）说的是「这一轮被预算掐断」
    /// 这个事实，对每一颗被掐的源都成立，所以整份名单共用同一次取词（词在表里，⛔ 在这里拼成品字）。</para></summary>
    /// <remarks>没有没拉全的源时返回 null：调用方⛔ 拿一颗"空"去顶在标题下面。</remarks>
    public LocMessage? TruncatedLines(string gap = "\n")
        => LinesWhere(s => s.Truncated, _ => LocMessage.Of("sync.src.truncated"), gap);

    /// <summary>灰卡那份<b>合订正文</b>（P-58）：跳过与没拉全两份名单哪份在念哪份，两份同场时接成一块。
    /// <para>合订住在 Core 而不住在主窗：<c>sync.src.lineJoin</c> 那颗布局键全 src 只许一处取词
    /// （#161 那族——主窗自己再取一次，就是给「两块怎么接」留了第二处能各自被改坏的主人）。</para></summary>
    /// <remarks>两份名单都空时返回 null——调用方先各自问过 <see cref="SkippedLines"/>／
    /// <see cref="TruncatedLines"/> 再进来（绿卡判据与标题挑选都要那两颗 null 读数，合订不替它们做主）。</remarks>
    public LocMessage? SkippedOrTruncatedLines(string gap = "\n")
    {
        var skipped = SkippedLines(gap);
        var truncated = TruncatedLines(gap);
        return skipped is null ? truncated : truncated is null ? skipped : Join(skipped, truncated, gap);
    }

    /// <summary>逐源挑行的共用手臂（P-38 (A) 把它从 <see cref="FailureLines"/> 里请出来）：
    /// 行＝<c>sync.src.line</c>（源名＋原因），行与行＝<see cref="Join"/>——行那颗键在这里取词，
    /// 接合那颗只有 <see cref="Join"/> 一处主人（#161 那族：多一份实现＝多一处能各自被改坏的正文）。
    /// <para>P-58 起那颗「原因」由调用方递进来（<paramref name="reason"/>）：失败/跳过名单念
    /// <see cref="SourceSyncResult.Error"/>，没拉全名单念表里那颗键——同一行形状，各自的原因。</para></summary>
    private LocMessage? LinesWhere(Func<SourceSyncResult, bool> pick, Func<SourceSyncResult, object?> reason, string gap)
    {
        LocMessage? body = null;
        foreach (var s in Sources)
        {
            if (!pick(s)) continue;
            var line = LocMessage.Of("sync.src.line", SourceNames.NameOf(s.SourceId), reason(s));
            body = body is null ? line : Join(body, line, gap);
        }
        return body;
    }

    /// <summary>两块正文接成一块：<c>sync.src.lineJoin</c> 全 src 唯一取词处（名单内的行与行、
    /// 两份名单彼此，接的都是这一颗；<paramref name="gap"/> 当实参进它的 <c>{2}</c> 槽——
    /// 是布局而⛔ 进表，规则 #100，先例＝<c>clip.clean.lineJoin</c>／<c>settings.data.backup.lineJoin</c>）。</summary>
    private static LocMessage Join(LocMessage first, LocMessage second, string gap)
        => LocMessage.Of("sync.src.lineJoin", first, second, gap);
}

/// <summary>
/// 同步错误分类（P1-4）。<b>现在的读者</b>＝主窗口那颗提示卡：命中 <see cref="Auth"/> 时它在卡上多摆一颗
/// 「去常规页换 Token」的按钮（批次 WI-81／P-212 走 B——它从前那句承诺是"给界面一份能操作的提示"，
/// 而这一档<b>全仓零读者</b>，那就是坑表 #588 那一族：注释写的是打算，不是现场）。
/// </summary>
public enum SourceSyncErrorKind
{
    Unknown,
    /// <summary>鉴权失败（Token 无效/过期）。</summary>
    Auth,
    /// <summary>API 限流。</summary>
    RateLimit,
    /// <summary>权限不足（403）。</summary>
    Forbidden,
    /// <summary>服务端异常（5xx）。</summary>
    Server,
    /// <summary>源侧读取失败（P-28B′）：数据文件被独占/锁住/读不出——不是同步逻辑的错，
    /// 是「这颗源这一轮没读出来」；界面那句「稍后再试/去把对应程序开着」指的就是这一档。</summary>
    Read,
}

/// <summary>单个源的同步结果。</summary>
public sealed class SourceSyncResult
{
    // ⛔ 在这里再放一份"这个名字念什么"（批次 WI-57，P-187）：念法自本批起只有语言表一个主人，
    // 要给人看的那一句按 SourceId 现取（SourceNames.NameOf）；这一份只服务汇总与控制台，身份就够了。
    public string SourceId { get; init; } = string.Empty;
    public bool Success { get; init; }
    /// <summary>这一源这一轮<b>没参与</b>（不可用被跳过）。P-38 (A)：<see cref="Success"/> 维持 true 不动
    /// （你从未启用的源不该每轮刷红），「没参与」与「拉了且成功」的界线由这颗旗划——三态读数见
    /// <see cref="Status"/>。</summary>
    public bool Skipped { get; init; }
    public int PulledCount { get; init; }
    /// <summary>这一源这一轮<b>被预算掐断、没拉全</b>（P-58）：载荷照常落库，<see cref="Success"/> 照旧 true，
    /// <see cref="Error"/> 照旧 null——「没拉全」⛔ 混进那两格里说（拉到的那批是真的成功，
    /// 塞进 Error 则成了第三种失败）。这颗旗由 <c>FetchResult.Truncated</c> 那只信封带上来，
    /// 读者＝<see cref="SyncSummary.TruncatedCount"/>／<see cref="SyncSummary.TruncatedLines"/>。</summary>
    public bool Truncated { get; init; }
    /// <summary>
    /// 这一源<b>为什么</b>没成（键＋实参，⛔ 成品字）。批次 WI-81／P-212 走 B 把它从 <c>string</c> 升上来的：
    /// 从前这里住的是「拉取超时或被取消（未中断其余源）」那种写死的中文，而屏幕上那一行只报颗数——
    /// 那句话既没人念、也换不了语言（同一形状 ✅WI-79 在 RSS 那侧修掉过，坑表 #586）。
    /// </summary>
    public LocMessage? Error { get; init; }
    /// <summary>P1-4 错误分类，便于 UI 给出针对性引导。</summary>
    public SourceSyncErrorKind ErrorKind { get; init; } = SourceSyncErrorKind.Unknown;

    /// <summary>三态读数（P-38 (A)）：<b>现算</b>而非存档——<see cref="SourceSyncStatus.Failed"/> ⇔
    /// <c>!Success</c>、<see cref="SourceSyncStatus.Skipped"/> ⇔ <see cref="Skipped"/>、其余＝
    /// <see cref="SourceSyncStatus.Succeeded"/>，三颗永远同口供（存一份就可能分岔：一颗说跳过、一颗说成功）。</summary>
    public SourceSyncStatus Status => !Success ? SourceSyncStatus.Failed
        : Skipped ? SourceSyncStatus.Skipped
        : SourceSyncStatus.Succeeded;
}

/// <summary>
/// 单源那一轮的三态（P-38 (A)）。<see cref="SourceSyncResult.Success"/> 那颗布尔的<b>语义一字未动</b>
/// （跳过仍记 true——你从未启用的源不该每轮刷红）；这一档只是把「没参与」从「拉了且成功」里分出来，
/// 交展示层分别着色，⛔ 让谁靠读 <see cref="SourceSyncResult.Error"/> 那句话去猜。
/// </summary>
public enum SourceSyncStatus
{
    /// <summary>拉了（哪怕零新增）且没出错。</summary>
    Succeeded,
    /// <summary>源不可用（未配置或依赖未运行）——没参与这一轮，也不是故障。</summary>
    Skipped,
    /// <summary>该源这一轮没成（<c>Success = false</c>）。</summary>
    Failed,
}
