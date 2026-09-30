#nullable enable
using System;

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

/// <summary>状态码/响应体 → 失败种类，以及种类 → 中文原因。<b>纯函数，不碰网络</b>。</summary>
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

    /// <summary>给用户看的那一句。<b>必须包含"上哪儿改"</b>：只描述现象的原因，用户读完只能再点一次同一个按钮。</summary>
    public static string Reason(AiFailureKind kind, string? detail, string where) => kind switch
    {
        // "未配置"这一类必须自带落点：用户是在设置页外面点了一个功能才被挡下的，
        // 只说"没有填模型名"他还得自己找出是哪一栏。
        AiFailureKind.NotConfigured => "AI 还没配置好："
            + (string.IsNullOrWhiteSpace(detail) ? "请检查设置里的 AI 那一栏" : detail + "（在设置 → AI 助手那一栏改）"),
        AiFailureKind.Unreachable =>
            $"连不上 {where}。没在服务端口上监听的进程会这样：本机装了但没启动，或端口/地址不是这一格填的。",
        AiFailureKind.ModelNotFound =>
            $"{where} 这个模型名服务侧不认。名字要与服务自己的列表完全一致（大小写与后缀都算），" +
            "本机 Ollama 还需先拉取过该模型。",
        AiFailureKind.Unauthorized => "Key 被拒绝（401/403）。请核对 Key 是否属于这个端点、是否已过期或被撤销。",
        AiFailureKind.RateLimited => "被限流了（429）。批量整理会在批次边界自动放慢重试，无需改配置。",
        AiFailureKind.TimedOut => "服务响应超时。本机模型第一次装载会明显偏慢；若每次都这样，请调大超时或换小模型。",
        AiFailureKind.ServiceError => "服务侧报错：" + Shown(detail ?? "未给出原因") + "（多为服务本身异常，不是这里的配置）",
        AiFailureKind.BadResponse =>
            "服务答了，但答的不是能读的形式（404 也常是地址少了一段，例如 OpenAI 兼容端点要填到 /v1）。" + Shown(detail),
        _ => "AI 调用没成功：" + Shown(detail ?? "未给出原因"),
    };

    /// <summary>响应体里的原因常常是一整段 JSON：<b>压掉换行并截断</b>，否则一行提示会把界面撑破。</summary>
    private static string Shown(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return string.Empty;
        var flat = detail.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return "　" + TextTrim.Ellipsize(flat, 160);
    }

    public static string NameOf(AiFailureKind kind) => kind switch
    {
        AiFailureKind.NotConfigured => "未配置",
        AiFailureKind.Unreachable => "连不上",
        AiFailureKind.ModelNotFound => "模型名不对",
        AiFailureKind.Unauthorized => "凭据被拒",
        AiFailureKind.RateLimited => "限流",
        AiFailureKind.TimedOut => "超时",
        AiFailureKind.ServiceError => "服务侧错误",
        AiFailureKind.BadResponse => "答的形式不对",
        _ => "未知",
    };
}
