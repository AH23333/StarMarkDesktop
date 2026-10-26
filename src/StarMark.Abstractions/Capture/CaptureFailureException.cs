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

    /// <summary>
    /// 屏幕上那句「为什么」只许由这里决定怎么交：自家的异常把 <see cref="Reason"/> 原样递上屏，
    /// 别人的异常（WinRT、GDI 之外那些系统异常）才走数据门那颗 <c>common.rawValue</c>——它们确实没有句子可交表。
    /// <para>一颗主人而不是每个 catch 各写一遍三元表达式：后者正是 P-196 数过的那种「同一条规则抄第二、第三份」。</para>
    /// </summary>
    public static LocMessage ReasonOf(Exception ex) =>
        ex is CaptureFailureException failure ? failure.Reason : LocMessage.Of("common.rawValue", ex.Message);
}
