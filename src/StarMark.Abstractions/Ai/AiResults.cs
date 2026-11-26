#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Abstractions.I18n;

namespace StarMark.Abstractions.Ai;

/// <summary>
/// 一次调用的 token 账（§20.1 计量）。<b>可为 null 是常态而不是异常</b>：不少本地模型与代理
/// 不回 usage 字段——没有实测数字时上层按"估算"口径补记（<see cref="Estimated"/> 分开两回事），
/// "这个数能不能当账单用"必须在值层面就看得出来，而不是靠界面备注。
/// </summary>
/// <param name="Estimated">true＝服务没回 usage，这是按字符数折算的估计值；false＝服务报的原数。</param>
public sealed record AiUsage(int InputTokens, int OutputTokens, bool Estimated)
{
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>两笔账并一笔（P-300 升帽重试）：撞墙那一发已花的真账不许在救回之后消失——
    /// §20.1 入账的那一行得是这一批的<b>总账</b>。任一侧是估算，并起来就按估算报
    /// （<see cref="Estimated"/> 是整笔账的口径，不是逐段的）。</summary>
    public static AiUsage? Combine(AiUsage? left, AiUsage? right)
        => (left, right) switch
        {
            (null, null) => null,
            (null, { } only) => only,
            ({ } only, null) => only,
            ({ } l, { } r) => new AiUsage(l.InputTokens + r.InputTokens, l.OutputTokens + r.OutputTokens,
                l.Estimated || r.Estimated),
        };
}

/// <summary>一次补调的结果。<b>失败必须带种类，而且种类与原文原因都要留</b>：
/// 种类决定"上哪儿改"，原文决定"改的时候该看哪一句"——只留一个都会让用户来回试。</summary>
/// <param name="RetryAfterSeconds">服务在 <c>Retry-After</c> 响应头里给的「隔几秒再来」（429/503 常带）。
/// null＝服务没说——编排层的固定退避顶上（片233·P-297）。</param>
/// <param name="FinishReason">结束原因（OpenAI 的 <c>finish_reason</c>／Ollama 的 <c>done_reason</c>，P-298 丙）。
/// null＝服务没说；<see cref="AiWire.CutOffReason"/>＝被输出帽切齐。<b>非空正文＋被切齐时这仍是 Ok</b>
/// （答了一半的提案不丢），截断标由编排层汇成「有 N 批没答完」那半句；空正文＋被切齐在 provider 那侧
/// 已经落成 <see cref="AiFailureKind.Truncated"/> 失败，不走这一位。</param>
public sealed record AiReply(bool Ok, string? Text, AiFailureKind Failure = AiFailureKind.Unknown,
    LocMessage? Detail = null, AiUsage? Usage = null, int? RetryAfterSeconds = null,
    string? FinishReason = null)
{
    public static AiReply Success(string text, AiUsage? usage = null, string? finishReason = null)
        => new(true, text, Usage: usage, FinishReason: finishReason);

    public static AiReply Fail(AiFailureKind kind, LocMessage? detail = null, int? retryAfterSeconds = null)
        => new(false, null, kind, detail, RetryAfterSeconds: retryAfterSeconds);

    /// <summary>界面/日志上的一句话。<see cref="AiSettings.DescribeWhere"/> 进来，
    /// 因为"连不上"必须说清是连不上<em>哪儿</em>。交 <see cref="LocMessage"/>＝两个消费者各按自己的时刻取词。</summary>
    public LocMessage Explain(LocMessage where) =>
        Ok ? LocMessage.Of("ai.reply.answered") : AiFailures.Reason(Failure, Detail, where);
}

/// <summary>服务侧"有哪些模型"的清点结果。<b>这是「测试连接」的实质内容</b>：
/// 只回"通了"没有用——用户真正卡住的是"通了，但模型名不对"。</summary>
public sealed record AiModelInventory(
    bool Ok,
    IReadOnlyList<string> Models,
    AiFailureKind Failure = AiFailureKind.Unknown,
    LocMessage? Detail = null)
{
    public static AiModelInventory Fail(AiFailureKind kind, LocMessage? detail = null)
        => new(false, Array.Empty<string>(), kind, detail);

    /// <summary>「测试连接」给用户看的那一句。<b>要同时报"找过哪里""按哪种通道""模型在不在"</b>：
    /// 少报任何一项，用户都得再点一次才知道下一步。
    /// <para>交「键＋实参」——这一句今天整个原样贴进设置页那一格（<c>probe.Report</c>），
    /// 词在产地取走就等于贴上去那一刻已经过时（批次 WI-67 片7）。</para></summary>
    public LocMessage Explain(LocMessage where, string? configuredModel)
    {
        if (!Ok) return AiFailures.Reason(Failure, Detail, where);
        var listed = AiWire.Preview(Models);
        if (string.IsNullOrWhiteSpace(configuredModel))
            return LocMessage.Of("ai.inventory.answeredList", where, listed);
        var found = AiWire.ListsModel(configuredModel, Models);
        // 模型名是<b>数据</b>（服务侧的标识符），照原样进槽；⛔ 给它套一句翻译
        return found
            ? LocMessage.Of("ai.inventory.answeredHasModel", where, configuredModel.Trim())
            : LocMessage.Of("ai.inventory.answeredLacksModel", where, configuredModel.Trim(), listed);
    }
}
