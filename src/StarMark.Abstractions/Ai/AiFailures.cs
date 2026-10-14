#nullable enable
using System;
using StarMark.Abstractions.I18n;

namespace StarMark.Abstractions.Ai;

/// <summary>
/// AI 调用失败的<b>种类</b>。<b>分型的唯一理由是让界面能给出"下一步做什么"</b>：
/// "没配置""服务没起""模型名写错""Key 不对""被限流""太慢"是六种完全不同的处置，
/// 统一显示成"AI 调用失败"就等于把六种故障都变成了不可报告故障。
/// </summary>
public enum AiFailureKind
{
    NotConfigured,
    Unreachable,
    ModelNotFound,
    Unauthorized,
    RateLimited,
    TimedOut,
    ServiceError,
    BadResponse,
    Unknown,
}

/// <summary>状态码/响应体 → 失败种类，以及种类 → <b>那句还没取词的原因</b>。<b>纯函数，不碰网络</b>。</summary>
public static class AiFailures
{
    /// <param name="status">HTTP 状态码；null＝连上了但没有响应（DNS/拒绝连接/超时都算这一类）。</param>
    /// <param name="body">响应体原文（Ollama 与 OpenAI 都把原因写在这里，而且各家措辞不同）。</param>
    public static AiFailureKind FromStatus(int? status, string? body)
    {
        var text = body ?? string.Empty;
        // 服务在监听、但模型名对不上——两家都可能回 404，而措辞里点名 model 的才是这一种
        if (status == 404 && MentionsMissingModel(text)) return AiFailureKind.ModelNotFound;
        if (status is null) return AiFailureKind.Unreachable;
        if (status == 401 || status == 403) return AiFailureKind.Unauthorized;
        if (status == 429) return AiFailureKind.RateLimited;
        if (status == 408 || status == 504) return AiFailureKind.TimedOut;
        // 400/404/422：是"我们发出去的东西它不认"（地址少一段、参数它不支持），与"服务自己坏了"要分开
        if (status == 404 || status == 400 || status == 422) return AiFailureKind.BadResponse;
        if (status >= 500) return AiFailureKind.ServiceError;
        if (status >= 400) return AiFailureKind.ServiceError;
        return AiFailureKind.Unknown;
    }

    private static bool MentionsMissingModel(string text)
        => text.Contains("model", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || text.Contains("no such", StringComparison.OrdinalIgnoreCase)
                || text.Contains("does not exist", StringComparison.OrdinalIgnoreCase));

    /// <summary>给用户看的那一句。<b>必须包含"上哪儿改"</b>：只描述现象的原因，用户读完只能再点一次同一个按钮。
    /// <para>交「键＋实参」而⛔ 交成品字（批次 WI-67 片7）：这一句被三处念——设置页那一格、
    /// 「测试连接」的报告、批量整理那句"这批为什么没成"，而且每一处都可能在换语言之后被重贴一次。
    /// 在这里取词就把语言钉死在"失败那一刻"，屏幕上留下半中半英（坑表 #393／P-196 那一族）。</para>
    /// <para><c>detail</c>＝服务回的那段原文，或本仓另一句"原因"（已是 <see cref="LocMessage"/>，⛔ 在这里取词）；
    /// <c>where</c>＝这一轮发到哪儿、用哪个模型（<see cref="AiSettings.DescribeWhere"/>）。</para></summary>
    public static LocMessage Reason(AiFailureKind kind, LocMessage? detail, LocMessage where) => kind switch
    {
        // "未配置"这一类必须自带落点：用户是在设置页外面点了一个功能才被挡下的，
        // 只说"没有填模型名"他还得自己找出是哪一栏。
        AiFailureKind.NotConfigured => detail is { } why
            ? LocMessage.Of("ai.failure.notConfiguredWith", why)
            : LocMessage.Of("ai.failure.notConfigured"),
        AiFailureKind.Unreachable => LocMessage.Of("ai.failure.unreachable", where),
        AiFailureKind.ModelNotFound => LocMessage.Of("ai.failure.modelNotFound", where),
        AiFailureKind.Unauthorized => LocMessage.Of("ai.failure.unauthorized"),
        AiFailureKind.RateLimited => LocMessage.Of("ai.failure.rateLimited"),
        AiFailureKind.TimedOut => LocMessage.Of("ai.failure.timedOut"),
        AiFailureKind.ServiceError => LocMessage.Of("ai.failure.serviceError", Shown(detail)),
        AiFailureKind.BadResponse => LocMessage.Of("ai.failure.badResponse", Shown(detail)),
        _ => LocMessage.Of("ai.failure.unknown", Shown(detail)),
    };

    /// <summary>响应体里的原因常常是一整段 JSON：<b>压掉换行并截断</b>，否则一行提示会把界面撑破。
    /// <para>⚠ 这颗帮手交的是"还没算完的那颗实参"而⛔ 一个字符串：截断要先有字才量得出长度，
    /// 而"什么时候有字"必须等到贴上去那一刻——在这里 <c>Render()</c> 一次就等于把明细的语言
    /// 钉在失败发生那一瞬间（外层换语言只重贴外层，屏幕上剩半中半英）。
    /// <c>string.Format</c> 对实参调的正是 <see cref="object.ToString"/>，于是压平与截断跟着取词一起落在那一刻。</para>
    /// <para>明细空白（含纯空格）念 <c>ai.failure.noReason</c>：今天 null 念那句、空白什么都不念，
    /// 于是屏幕上是一句"服务侧报错：（多为…）"那样吊着一个分隔符的话；表里的句子没法表达"可选的半句"，
    /// 这一批把它统一成"没有明细就说没有明细"（登记在本批报告的改口那一节）。</para></summary>
    private static object Shown(LocMessage? detail) => new DeferredDetail(detail);

    private sealed class DeferredDetail
    {
        private readonly LocMessage? _detail;

        public DeferredDetail(LocMessage? detail) => _detail = detail;

        public override string ToString()
        {
            var flat = _detail is { } d
                ? d.Render().Replace('\r', ' ').Replace('\n', ' ').Trim()
                : string.Empty;
            return flat.Length == 0
                ? LocMessage.Of("ai.failure.noReason").Render()
                : TextTrim.Ellipsize(flat, 160);
        }
    }
}
