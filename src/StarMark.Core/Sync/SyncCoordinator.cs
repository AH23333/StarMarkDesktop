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

    public SyncCoordinator(IItemRepository repository, IEnumerable<IItemSource> sources)
    {
        _repository = repository;
        _sources = sources.ToList();
    }

    /// <summary>
    /// 执行一次全量同步。每个源拉取后立即 upsert（避免缓存全部结果占用内存）。
    /// </summary>
    public async Task<SyncSummary> SyncAllAsync(CancellationToken ct)
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
                    PulledCount = 0,
                    Error = LocMessage.Of("sync.src.unavailable"),
                });
                continue;
            }

            try
            {
                var items = await source.FetchAsync(new SyncContext(), ct);
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
                results.Add(new SourceSyncResult
                {
                    SourceId = source.SourceId,
                    Success = true,
                    PulledCount = items.Count,
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
        return new SyncSummary
        {
            StartedAt = now,
            ElapsedMs = sw.ElapsedMilliseconds,
            Sources = results,
        };
    }
}

/// <summary>单次同步的汇总报告。</summary>
public sealed class SyncSummary
{
    public long StartedAt { get; init; }
    public long ElapsedMs { get; init; }
    public List<SourceSyncResult> Sources { get; init; } = new();

    public int TotalPulled => Sources.Sum(s => s.PulledCount);
    public int FailedCount => Sources.Count(s => !s.Success);

    /// <summary>
    /// 这一次同步的<b>那句汇总话（还没取词）</b>：键＋读数交出去，由界面在贴上去的那一刻按当时的语言取词
    /// （批次 WI-2m；口径同 <see cref="LocMessage"/>——在这里先 Render 一次＝外层跟着语言翻、
    /// 这一行留在中文，屏幕上就是半中半英，且换语言那一刻没人重贴）。
    /// </summary>
    public LocMessage Message()
        => FailedCount == 0
            ? LocMessage.Of("top.syncDone", TotalPulled, ElapsedMs)
            : LocMessage.Of("top.syncPartial", TotalPulled, FailedCount, ElapsedMs);

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
    /// <remarks>没有失败源时返回 null：调用方⛔ 拿一颗"空"去顶在一句已完成的消息下面。</remarks>
    public LocMessage? FailureLines(string gap = "\n")
    {
        LocMessage? body = null;
        foreach (var s in Sources)
        {
            if (s.Success) continue;
            var line = LocMessage.Of("sync.src.line", SourceNames.NameOf(s.SourceId), s.Error);
            body = body is null ? line : LocMessage.Of("sync.src.lineJoin", body, line, gap);
        }
        return body;
    }
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
}

/// <summary>单个源的同步结果。</summary>
public sealed class SourceSyncResult
{
    // ⛔ 在这里再放一份"这个名字念什么"（批次 WI-57，P-187）：念法自本批起只有语言表一个主人，
    // 要给人看的那一句按 SourceId 现取（SourceNames.NameOf）；这一份只服务汇总与控制台，身份就够了。
    public string SourceId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public int PulledCount { get; init; }
    /// <summary>
    /// 这一源<b>为什么</b>没成（键＋实参，⛔ 成品字）。批次 WI-81／P-212 走 B 把它从 <c>string</c> 升上来的：
    /// 从前这里住的是「拉取超时或被取消（未中断其余源）」那种写死的中文，而屏幕上那一行只报颗数——
    /// 那句话既没人念、也换不了语言（同一形状 ✅WI-79 在 RSS 那侧修掉过，坑表 #586）。
    /// </summary>
    public LocMessage? Error { get; init; }
    /// <summary>P1-4 错误分类，便于 UI 给出针对性引导。</summary>
    public SourceSyncErrorKind ErrorKind { get; init; } = SourceSyncErrorKind.Unknown;
}
