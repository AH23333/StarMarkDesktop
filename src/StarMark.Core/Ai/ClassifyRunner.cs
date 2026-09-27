#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Ai;

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
public sealed record ClassifyBatchReport(
    int Index,
    int ItemCount,
    bool Ok,
    string? Error,
    IReadOnlyList<TagProposal> Proposals,
    int MissingCount,
    int UnknownCount,
    AiUsage? Usage = null);

/// <summary>整轮的结果。</summary>
/// <param name="StoppedBatches">因为叫停而没问成的批数——<b>正在飞的那一批也算在内</b>（它没拿到结果）。
/// 与 RSS 那边同一套三态：<b>按了停止不该把已经整理出来的结果一起丢掉</b>，也不该把它记成"这几批坏了"。</param>
public sealed record ClassifyRunReport(
    IReadOnlyList<ClassifyBatchReport> Batches,
    IReadOnlyList<TagProposal> Proposals,
    int RequestedItems,
    int FailedBatches,
    int StoppedBatches,
    IReadOnlyList<string> NewTags)
{
    public int AnsweredItems => Proposals.Count;
    public int BatchesTotal => Batches.Count + StoppedBatches;
    public bool AnythingToApply => Proposals.Count > 0;
}

/// <summary>
/// 批量整理的编排：分批 → 问 → 解读 → 落检查点 → 下一批。<b>本类不碰网络也不写库</b>：
/// 调用以委托注入（界面给 <c>AiGateway</c>，单测给假答复），写库由上层按批次应用。
/// 这样"第五批坏了怎么办""停了还剩什么""编号对不上怎么办"全都能在 no-network 下断言。
/// </summary>
public static class ClassifyRunner
{
    /// <summary>一次问的时限。本地小模型对 50 条正常在几十秒内，给到 180 秒是留给冷启动；
    /// 再长就变成"用户不知道该等还是该停"。</summary>
    public const int DefaultTimeoutSeconds = 180;

    public static async Task<ClassifyRunReport> RunAsync(
        IReadOnlyList<ClassifyItem> items,
        IReadOnlyList<string> catalog,
        Func<AiRequest, Task<AiReply>> call,
        Func<ClassifyBatchReport, Task>? onBatchAsync,
        CancellationToken ct,
        int timeoutSeconds = DefaultTimeoutSeconds)
    {
        var batches = ClassifyPrompt.Batches(items);
        var reports = new List<ClassifyBatchReport>(batches.Count);
        var proposals = new List<TagProposal>();
        var newTags = new List<string>();
        var stopped = 0;

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
                report = await OneAsync(i, batch, catalog, call, timeoutSeconds, ct);
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
                proposals.AddRange(report.Proposals);
                newTags.AddRange(from proposal in report.Proposals from tag in proposal.Tags select tag);
            }

            // 检查点必须在下一批之前落定：否则进程中途被杀时，界面显示的进度会比真实进度新，
            // 续跑就会跳过还没写库的那一批（扩展侧"状态先行"是同一条教训）。
            if (onBatchAsync is not null) await onBatchAsync(report);
        }

        return new ClassifyRunReport(
            reports,
            proposals,
            items.Count,
            reports.Count(report => !report.Ok),
            stopped,
            newTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
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
                // 一批 50 条 × 最多 4 个标签，留出 JSON 的标点与换行
                MaxTokens: Math.Clamp(batch.Count * 28, 400, 4000));
            var reply = await call(request);
            if (!reply.Ok)
                return new ClassifyBatchReport(index, batch.Count, false, reply.Explain("AI 通道"), Array.Empty<TagProposal>(), batch.Count, 0);

            var parsed = ClassifyPrompt.Parse(reply.Text, batch, catalog);
            if (!parsed.Readable)
                return new ClassifyBatchReport(index, batch.Count, false, parsed.Error, Array.Empty<TagProposal>(), batch.Count, parsed.UnknownIds.Count);

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
            return new ClassifyBatchReport(index, batch.Count, false, "这一批出错了：" + ex.Message,
                Array.Empty<TagProposal>(), batch.Count, 0);
        }
    }
}
