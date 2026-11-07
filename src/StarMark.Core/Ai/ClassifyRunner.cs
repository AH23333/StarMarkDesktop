#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.I18n;

namespace StarMark.Core.Ai;

/// <summary>一批的结果。<b>每批都要能单独回报</b>：一次整理三百条就是六批，
/// 第五批失败与"整件事失败"是完全不同的两句话。</summary>
/// <param name="Index">第几批（0 起，界面上显示为"第 N / M 批"）。</param>
/// <param name="ItemCount">这一批问了几条。</param>
/// <param name="Ok">这一批是否拿到了可用的答复（读不出 JSON 也算不 Ok）。</param>
/// <param name="Error">不 Ok 的原因。</param>
/// <param name="Proposals">这一批建议打上的标签（已按批次内编号对好号）。</param>
/// <param name="MissingCount">答复里漏掉的条数——<b>它不是失败，但必须被数出来</b>：
/// 模型答了 43/50 与答了 50/50 在"整理完了"这句话上不该长得一样。</param>
/// <param name="UnknownCount">答复里出现了但本批没有的编号数（被丢掉了）。</param>
/// <param name="Usage">这一批的 token 账（§20.1）——<b>null＝这一批没成行，没得可记</b>：
/// 失败的批不猜消耗（有些失败确实一个 token 都没花出去），入账只认服务回过答复的批。</param>
/// <param name="Failure">这一批最终败在哪一型上（Ok 批＝Unknown 占位）。重试/熔断的分界读它（P-297）。</param>
/// <param name="RetryAfterSeconds">服务最后一次给的 <c>Retry-After</c> 秒数（没给＝null）。</param>
/// <param name="Attempts">这一批实际发了几次（含重试；一次成＝1）。</param>
public sealed record ClassifyBatchReport(
    int Index,
    int ItemCount,
    bool Ok,
    LocMessage? Error,
    IReadOnlyList<TagProposal> Proposals,
    int MissingCount,
    int UnknownCount,
    AiUsage? Usage = null,
    AiFailureKind Failure = AiFailureKind.Unknown,
    int? RetryAfterSeconds = null,
    int Attempts = 1);

/// <summary>整轮的结果。</summary>
/// <param name="StoppedBatches">因为叫停而没问成的批数——<b>正在飞的那一批也算在内</b>（它没拿到结果）。
/// 与 RSS 那边同一套三态：<b>按了停止不该把已经整理出来的结果一起丢掉</b>，也不该把它记成"这几批坏了"。</param>
/// <param name="SkippedBatches">因为<b>熔断</b>而根本没发起的批数（P-297）：致命型失败（额度耗尽/Key 被拒…）
/// 或连续两批重试后仍全败——线是真的断了，后面的批不再烧超时。<b>与 StoppedBatches 分开</b>：
/// "我叫的停"与"线断了"是两件事，混在一个数里，"还剩几批没问"就永远对不上。</param>
/// <param name="AbortReason">熔断那句话（「第 N 批起没有再问：原因」）；null＝没熔断。</param>
public sealed record ClassifyRunReport(
    IReadOnlyList<ClassifyBatchReport> Batches,
    IReadOnlyList<TagProposal> Proposals,
    int RequestedItems,
    int FailedBatches,
    int StoppedBatches,
    IReadOnlyList<string> NewTags,
    int SkippedBatches = 0,
    LocMessage? AbortReason = null)
{
    public int AnsweredItems => Proposals.Count;
    public int BatchesTotal => Batches.Count + StoppedBatches + SkippedBatches;
    public bool AnythingToApply => Proposals.Count > 0;
}

/// <summary>
/// 批量整理的编排：分批 → 问 → 解读 → 落检查点 → 下一批。<b>本类不碰网络也不写库</b>：
/// 调用以委托注入（界面给 <c>AiGateway</c>，单测给假答复），写库由上层按批次应用。
/// 这样"第五批坏了怎么办""停了还剩什么""编号对不上怎么办"全都能在 no-network 下断言。
/// <para>片233（P-297）起这里多了三条韧性规则：<b>批内重试＋退避</b>（可重试型每批最多三试，
/// 退避先看服务的 <c>Retry-After</c>，否则固定档）、<b>致命型即停</b>（额度耗尽/Key 拒这类，
/// 重试只是把同一堵墙再撞两遍）、<b>连败熔断</b>（连续两批三试全败 ⇒ 剩余批不再问）。
/// 叫停语义零改动：按停止照旧"留下已整理的、剩下的计入 StoppedBatches"。</para>
/// </summary>
public static class ClassifyRunner
{
    /// <summary>一次问的时限。本地小模型对一批（O7 后 80 条上限）正常在几十秒内，给到 180 秒是留给冷启动；
    /// 再长就变成"用户不知道该等还是该停"。</summary>
    public const int DefaultTimeoutSeconds = 180;

    /// <summary>可调的下界。低于这个数连本机模型冷启动都过不去，每一批都会立刻超时——
    /// 那是"看着设了个更严的值，实际把功能关掉了"。</summary>
    public const int MinTimeoutSeconds = 10;

    /// <summary>可调的上界。一批发到刻钟以上，用户已经判断不出自己该等还是该停，
    /// 而「停止」那颗按钮的出口本来就在界面上。</summary>
    public const int MaxTimeoutSeconds = 900;

    /// <summary>一批最多发几次（首问＋重试）。三次是"瞬时抖动能救回来"与"用户等不起"之间的定值：
    /// 再多一次，等待时间翻倍而救回率几乎不动（P-297 方案甲）。</summary>
    public const int MaxAttemptsPerBatch = 3;

    /// <summary>重试的固定退避档（服务没给 <c>Retry-After</c> 时用）：先短后长。</summary>
    public const int FirstRetryDelaySeconds = 5;
    public const int SecondRetryDelaySeconds = 15;

    /// <summary><c>Retry-After</c> 的采纳上限。服务方偶尔会写一个很大的数（十分钟级），
    /// 照单全收＝用户对着一个"看起来死了"的界面干等；夹到 60 秒，超出的部分按 60 秒等——
    /// <b>服务说"别急着来"不等于"用户愿意等"</b>，两边各让一步。</summary>
    public const int MaxRetryAfterSeconds = 60;

    /// <summary>把"用户填的那个秒数"换算成能用的秒数：<b>没填或越界一律回默认，不静默夹到边上</b>
    /// （夹边只该用在"想要但够不着"那一类，而 0 秒与 9999 秒不是够不着，是不成立）。
    /// <para>判据只有这一处：设置页与仓储都调它，界面里不许再写第二份 10/900/180。</para></summary>
    public static int NormalizeTimeoutSeconds(int? seconds)
        => seconds is { } v && v >= MinTimeoutSeconds && v <= MaxTimeoutSeconds
            ? v
            : DefaultTimeoutSeconds;

    public static async Task<ClassifyRunReport> RunAsync(
        IReadOnlyList<ClassifyItem> items,
        IReadOnlyList<string> catalog,
        Func<AiRequest, Task<AiReply>> call,
        Func<ClassifyBatchReport, Task>? onBatchAsync,
        CancellationToken ct,
        int timeoutSeconds = DefaultTimeoutSeconds,
        Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        var batches = ClassifyPrompt.Batches(items);
        var reports = new List<ClassifyBatchReport>(batches.Count);
        var proposals = new List<TagProposal>();
        var newTags = new List<string>();
        var stopped = 0;
        var skipped = 0;
        LocMessage? abortReason = null;
        var failedStreak = 0;

        for (var i = 0; i < batches.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                stopped = batches.Count - i;
                break;
            }

            var batch = batches[i];
            ClassifyBatchReport report;
            try
            {
                report = await OneWithRetryAsync(i, batch, catalog, call, timeoutSeconds, ct, wait);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 叫停落在"正在问的那一批"里面：这一批和它后面的都不再问。
                // <b>不能把这一下继续抛出去</b>——抛出去就等于把已经整理好的前五批一起丢掉，
                // 而"按了停止反而什么都没剩下"是用户最没法理解的一种失败（与 RSS 那侧同一口径）。
                stopped = batches.Count - i;
                break;
            }

            reports.Add(report);
            if (report.Ok)
            {
                failedStreak = 0;
                proposals.AddRange(report.Proposals);
                newTags.AddRange(from proposal in report.Proposals from tag in proposal.Tags select tag);
            }
            else if (batches.Count - i - 1 > 0)
            {
                // 熔断的两种脸（P-297）：致命型（额度耗尽/Key 拒/协议不认——重试只是把同一堵墙再撞两遍）
                // 第一批就停；可重试型连着两批三试全败——线是真的断了，别把一整轮全烧在超时上。
                // 只在"后面还有批"时熔断：最后一批的失败照旧只记失败，不产生「未再尝试」那半句。
                if (!AiFailures.IsRetryable(report.Failure)) skipped = batches.Count - i - 1;
                else if (++failedStreak >= 2) skipped = batches.Count - i - 1;
                if (skipped > 0)
                    abortReason = LocMessage.Of("ai.run.aborted", i + 2, report.Error);
            }

            // 检查点必须在下一批之前落定：否则进程中途被杀时，界面显示的进度会比真实进度新，
            // 续跑就会跳过还没写库的那一批（扩展侧"状态先行"是同一条教训）。
            if (onBatchAsync is not null) await onBatchAsync(report);
            if (skipped > 0) break;
        }

        return new ClassifyRunReport(
            reports,
            proposals,
            items.Count,
            reports.Count(report => !report.Ok),
            stopped,
            newTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            skipped,
            abortReason);
    }

    /// <summary>单批＋重试：可重试型失败最多再发 <see cref="MaxAttemptsPerBatch"/> 减一次，
    /// 退避先看服务的 <c>Retry-After</c>（夹在 <see cref="MaxRetryAfterSeconds"/> 内），否则按固定档。
    /// 退避途中被叫停 ⇒ 抛给外层按"叫停落在这一批"收——<b>退避是等待，不是占着不让停</b>。</summary>
    private static async Task<ClassifyBatchReport> OneWithRetryAsync(
        int index, IReadOnlyList<ClassifyItem> batch, IReadOnlyList<string> catalog,
        Func<AiRequest, Task<AiReply>> call, int timeoutSeconds, CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? wait)
    {
        var report = await OneAsync(index, batch, catalog, call, timeoutSeconds, ct);
        var attempts = 1;
        while (!report.Ok && attempts < MaxAttemptsPerBatch && !ct.IsCancellationRequested)
        {
            if (!AiFailures.IsRetryable(report.Failure)) break;   // 致命型：重试只是把同一堵墙再撞两遍
            var delaySeconds = report.RetryAfterSeconds is { } asked && asked > 0
                ? Math.Min(asked, MaxRetryAfterSeconds)
                : (attempts == 1 ? FirstRetryDelaySeconds : SecondRetryDelaySeconds);
            StarLog.Info($"[AI] 第 {index + 1} 批第 {attempts} 次失败（{report.Failure}），{delaySeconds} 秒后重试");
            var delay = TimeSpan.FromSeconds(delaySeconds);
            if (wait is null) await Task.Delay(delay, ct);
            else await wait(delay, ct);
            attempts++;
            report = await OneAsync(index, batch, catalog, call, timeoutSeconds, ct);
        }
        return report with { Attempts = attempts };
    }

    /// <summary>单批：任何异常都收在这里，<b>绝不让它冒到循环外</b>。
    /// 一个批超时不该把已经整理好的五批丢掉——那是用户最没法理解的那种失败。</summary>
    private static async Task<ClassifyBatchReport> OneAsync(
        int index, IReadOnlyList<ClassifyItem> batch, IReadOnlyList<string> catalog,
        Func<AiRequest, Task<AiReply>> call, int timeoutSeconds, CancellationToken ct)
    {
        try
        {
            var request = new AiRequest(
                ClassifyPrompt.SystemPrompt(catalog),
                ClassifyPrompt.UserPrompt(batch),
                TimeoutSeconds: timeoutSeconds,
                // 一批（O7 后最多 80 条）× 最多 4 个标签，留出 JSON 的标点与换行
                MaxTokens: Math.Clamp(batch.Count * 28, 400, 4000));
            var reply = await call(request);
            if (!reply.Ok)
                return new ClassifyBatchReport(index, batch.Count, false, reply.Explain(LocMessage.Of("ai.where.channel")),
                    Array.Empty<TagProposal>(), batch.Count, 0, reply.Usage,
                    reply.Failure, reply.RetryAfterSeconds);

            var parsed = ClassifyPrompt.Parse(reply.Text, batch, catalog);
            if (!parsed.Readable)
                return new ClassifyBatchReport(index, batch.Count, false, parsed.Error, Array.Empty<TagProposal>(),
                    batch.Count, parsed.UnknownIds.Count);

            return new ClassifyBatchReport(index, batch.Count, true, null, parsed.Proposals,
                parsed.MissingIds.Count, parsed.UnknownIds.Count, reply.Usage);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 途中被叫停：这一批记为"没问成"，但循环外层的 stopped 计数才代表"剩下的没问"
            throw;
        }
        catch (Exception ex)
        {
            return new ClassifyBatchReport(index, batch.Count, false, LocMessage.Of("ai.run.batchFailed", ex.Message),
                Array.Empty<TagProposal>(), batch.Count, 0);
        }
    }
}
