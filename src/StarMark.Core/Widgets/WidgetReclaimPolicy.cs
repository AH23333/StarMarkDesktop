#nullable enable
using System;

namespace StarMark.Core.Widgets;

/// <summary>
/// 一颗隐藏中的组件窗能不能收掉（批次 ST，P-133）。
/// <para>
/// 为什么值得单列：隐藏的组件只 <c>SW_HIDE</c>，窗口、WinUI 宿主与组合面一块都不释放。
/// 真机量到 12 颗组件窗＝<b>80 MB 私有字节 / 771 个句柄</b>，而进程内<b>托管堆只有 5–6 MB</b>——
/// 也就是说这笔钱全在原生侧，清托管缓存一类优化一点都碰不到它。要省就得真的把窗口关掉。
/// </para>
/// <para>
/// 但"关掉窗口"等于丢掉只存在那颗窗口里的东西：一个字打了一半的随记、跑到一半的番茄钟、没提交的表达式。
/// 那种丢法按缺陷算（稳定性优先于省下的内存），所以<b>每一类都要在这里点名表态</b>，
/// 而不是由宿主写一句"隐藏的都可以收"——那一句在新增组件类型的那天就会悄悄变成假话。
/// </para>
/// </summary>
public enum WidgetReclaimVerdict
{
    /// <summary>内容随时能从存档／库重算出来，且没有任何"只在这颗窗口里"的输入 ⇒ 满宽限期就收。</summary>
    Reclaimable,

    /// <summary>有只在内存里的用户输入（打了一半的字、未提交的表达式）⇒ 收了就是丢字，不收。</summary>
    HoldsDraftInput,

    /// <summary>有正在进行的会话（番茄钟跑到一半）⇒ 收了就是中断，不收。</summary>
    RunningSession,

    /// <summary>
    /// 还没逐颗读过它的在途状态 ⇒ <b>保守不收</b>。
    /// <para>这一档存在的意义是把"没核实"与"核实过不能收"分开写：升级成 <see cref="Reclaimable"/>
    /// 要带证据（读过那颗组件的实现）与用例，而不是顺手改一行。</para>
    /// </summary>
    NotAudited,
}

/// <summary>隐藏组件的窗口回收判据：宽限期 + 每一类组件的表态。纯函数，可逐条断言。</summary>
public static class WidgetReclaimPolicy
{
    /// <summary>
    /// 隐藏要<b>持续</b>多久才收窗口（用户裁决 2026-10-01："隐藏满 5 分钟才收"）。
    /// <para>为什么要有宽限期而不是"一隐藏就收"：日常"藏一下马上调出来"是最常做的动作，
    /// 而重建一颗窗口实测 33–52 ms（12 颗批量 0.5–1 s）。把这笔代价加在那种动作上，
    /// 省的是内存、赔的是手感。</para>
    /// </summary>
    public static readonly TimeSpan HiddenGrace = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 每一类组件点名表态。<b>"每一类"不由编译器保证，由形状闸门 <c>TheVerdictTableNamesEveryKind</c> 保证</b>：
    /// 消掉 CS8524 只能加<b>兜底臂（<c>_ =&gt;</c>）</b>，而兜底臂恰恰是"新类型悄悄落到某一档"的那条缝，
    /// 所以它取<b>最保守的那一支</b>——没点名的类型不收，而不是"顺手可以收"。
    /// 新增组件时真会红的是那颗闸门（它逐个数过枚举里的名字有没有都在这张表里）。
    /// </summary>
    public static WidgetReclaimVerdict Verdict(WidgetKind kind) => kind switch
    {
        // 展示与查询都从库／存档现算，窗口里没有独有输入
        WidgetKind.Clock => WidgetReclaimVerdict.Reclaimable,
        WidgetKind.WorldClock => WidgetReclaimVerdict.Reclaimable,
        WidgetKind.TagGrid => WidgetReclaimVerdict.Reclaimable,
        WidgetKind.Clipboard => WidgetReclaimVerdict.Reclaimable,
        WidgetKind.Activity => WidgetReclaimVerdict.Reclaimable,
        WidgetKind.Pinned => WidgetReclaimVerdict.Reclaimable,
        WidgetKind.Glance => WidgetReclaimVerdict.Reclaimable,
        // 采样本来就"看不见就不采"（可见才采样是 MJ 定的口径），收窗与重建都不多打一次外部服务
        WidgetKind.SystemMonitor => WidgetReclaimVerdict.Reclaimable,

        WidgetKind.QuickNote => WidgetReclaimVerdict.HoldsDraftInput,
        WidgetKind.Todo => WidgetReclaimVerdict.HoldsDraftInput,
        WidgetKind.Search => WidgetReclaimVerdict.HoldsDraftInput,
        WidgetKind.Calc => WidgetReclaimVerdict.HoldsDraftInput,

        WidgetKind.Focus => WidgetReclaimVerdict.RunningSession,

        // 还没逐颗读过实现：天气重新显示是否要多打一次网络、快捷启动的内联重命名是否即刻落盘、
        // SMTC 重订阅会不会丢当前曲目、倒计时的到点确认态住在哪儿——问清一颗升一颗。
        WidgetKind.Weather => WidgetReclaimVerdict.NotAudited,
        WidgetKind.QuickLaunch => WidgetReclaimVerdict.NotAudited,
        WidgetKind.Music => WidgetReclaimVerdict.NotAudited,
        WidgetKind.Countdown => WidgetReclaimVerdict.NotAudited,

        // 没点名的类型（含新加的那一种）一律保守：宁可少省几 MB，也不丢用户的字。
        // 编译器要求这一臂（switch 表达式里它写作 `_`，不是 `default`），所以"新增类型必须被点名"
        // 由闸门 TheVerdictTableNamesEveryKind 逐个数枚举名字来保证。
        _ => WidgetReclaimVerdict.NotAudited,
    };

    /// <summary>
    /// 该不该收这一颗。<b>两条都要成立</b>：这一类被点名判为可收，且已经持续隐藏满宽限期。
    /// <para>时间判据写成"至少这么久"（不是"超过"）——正好 5 分钟是"已经给满机会"，不收就再等一轮巡查；
    /// 负数（时钟回拨／未来时刻）不收，也不夹端：把越界夹成"收"是最坏的一种错法。</para>
    /// </summary>
    public static bool ShouldReclaim(WidgetKind kind, TimeSpan hiddenFor)
        => Verdict(kind) == WidgetReclaimVerdict.Reclaimable && hiddenFor >= HiddenGrace;

    /// <summary>没收回的那一颗在日志里说什么：把"为什么没动它"说清，否则"清理了但内存没降"永远归不了因。</summary>
    public static string Reason(WidgetKind kind, TimeSpan hiddenFor)
        => Verdict(kind) switch
        {
            WidgetReclaimVerdict.Reclaimable when hiddenFor >= HiddenGrace => "已给满宽限期，按判据应当被收掉",
            WidgetReclaimVerdict.Reclaimable =>
                $"还在宽限期内（已隐藏 {Format(hiddenFor)}，满 {Format(HiddenGrace)} 才收）",
            WidgetReclaimVerdict.HoldsDraftInput => "它可能有只在这颗窗口里的输入",
            WidgetReclaimVerdict.RunningSession => "它可能有正在进行的会话",
            WidgetReclaimVerdict.NotAudited => "这一类还没逐颗核实过在途状态，按保守不收",
            _ => "这一类的表态没人认识，按保守不收",
        };

    /// <summary>日志里的时长：整分钟带秒，不满一分钟只报秒（"0 分 8 秒"那种写法读起来像笔误）。</summary>
    public static string Format(TimeSpan value)
        => value.TotalMinutes < 1 ? $"{(int)value.TotalSeconds} 秒"
                                  : $"{(int)value.TotalMinutes} 分 {value.Seconds} 秒";
}
