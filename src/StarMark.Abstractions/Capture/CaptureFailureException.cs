namespace StarMark.Abstractions.Capture;

using System;
using StarMark.Abstractions.I18n;

/// <summary>
/// 截图标注那一族抛出去的「为什么没成」——带的是<b>键＋实参</b>，不是已经念完的那句中文。
/// <para>为什么要这一颗：抛出去的那句话往下有两种消费者——日志与崩溃转储（要字符串），
/// 和<b>屏幕上那句提示</b>（要把{0}插在表里那句「标注没能画上去：{0}」的槽里）。
/// 只交字符串就满足了前者、把中文钉在三种语言上：英文与日文界面显示半英半日半中
/// （P-192 在识字那一路与剪贴板图片那一路量到的是同一副坏法）。</para>
/// <para>形状照 <c>StarMark.Abstractions.Trending.TrendingException</c> 那位先例：<b>两头都给</b>——
/// <c>Message</c> 当场念完（日志那条出口本来不上屏），<see cref="Reason"/> 原样交出去，
/// 取词仍发生在宿主贴上去那一刻（换语言整条一起改口）。</para>
/// </summary>
public sealed class CaptureFailureException(LocMessage reason, string? paramName = null) : Exception(reason.Render())
{
    /// <summary>那句「为什么」还没取词的那一半（屏幕上要的是这颗）。</summary>
    public LocMessage Reason { get; } = reason;

    /// <summary>原来 ArgumentException 带的参数名：日志与转储里还得看得见。</summary>
    public string? ParamName { get; } = paramName;

    // WinRT 的剪贴板异常可能没有 Message，不能让失败卡只剩标题。
    public static LocMessage ReasonOf(Exception ex)
    {
        if (ex is CaptureFailureException failure) return failure.Reason;
        if (ex.HResult == unchecked((int)0x800401D0)) return LocMessage.Of("capture.clipboard.cannotOpen");
        return string.IsNullOrWhiteSpace(ex.Message)
            ? LocMessage.Of("capture.error.noMessage", $"0x{ex.HResult:X8}")
            : LocMessage.Of("common.rawValue", ex.Message);
    }
}
