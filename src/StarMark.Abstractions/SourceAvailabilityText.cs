#nullable enable
using System;

namespace StarMark.Abstractions;

/// <summary>
/// 诊断面板里"某个来源现在到底怎么样"那句话的唯一写法（纯函数）。
/// <para>
/// 旧写法是 <c>IsAvailable ? "可用" : "不可用"</c>：一个"不可用"把三种完全不同的事实压成一样——
/// 没装这个浏览器、没开那个外部程序、没配 Token。用户看到的就是"是不是坏了"，
/// 而正确反应可能是"我本来就没用 Ditto"。所以这里规定：<b>不可用时要说成因</b>，
/// 成因由各来源自己给（<see cref="IItemSource.AvailabilityHint"/>：它才知道自己找过哪条路径）。
/// </para>
/// </summary>
public static class SourceAvailabilityText
{
    /// <summary>可用时的固定值。</summary>
    public const string Available = "可用";

    /// <summary>来源没给成因时的兜底（不猜原因，只说明"本机没有它的数据/依赖"）。</summary>
    public const string UnknownReason = "本机没有这项数据或依赖没启动";

    /// <summary>不可用时展示的成因上限（诊断面板是一行一行读的，不该塞进整段路径清单）。</summary>
    private const int MaxHintLength = 200;

    public static string Of(bool available, string? hint)
    {
        if (available) return Available;
        var why = hint?.Trim();
        if (string.IsNullOrEmpty(why)) return "没有这项数据：" + UnknownReason;
        return why.Length > MaxHintLength ? "没有这项数据：" + why[..MaxHintLength] + "…" : "没有这项数据：" + why;
    }
}
