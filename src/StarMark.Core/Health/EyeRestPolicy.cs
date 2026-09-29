#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Capture;

namespace StarMark.Core.Health;

/// <summary>
/// 到点之后<b>用什么形式提醒</b>（批次 RS，用户裁决：把"暗幕"从"强制休息"里解出来）。
/// <para>
/// 旧形状是一个布尔（<c>EyeRestEnforced</c>）：<b>暗幕被绑在强制上</b>——不开强制就只有一张提示卡，于是"试一试当前设置的效果"
/// 试不到暗幕，而开了强制又 20 秒不能提前结束（发起人原话："要么无法使用暗幕，要么使用暗幕时无法提前跳过"）。
/// 现在"出现什么"与"能不能提前退出"合成<b>一个三档选择</b>，两件事各有其位置：
/// </para>
/// <list type="bullet">
/// <item><description><see cref="Card"/>：只发一张右下角提示卡（最轻，什么都不盖）。</description></item>
/// <item><description><see cref="Curtain"/>：盖一层暗幕，<b>点一下屏幕就提前结束</b>——这是新的默认档。</description></item>
/// <item><description><see cref="Forced"/>：盖暗幕且<b>不接任何退出</b>，20 秒走完才恢复（原规格那条"Esc 不跳过，防形同虚设"仍在，只是不再是拿到暗幕的唯一代价）。</description></item>
/// </list>
/// <para><b>数值钉死，不靠声明顺序</b>：这个枚举按整数落进 <c>settings.json</c>（同 <c>ItemType</c>／<c>WidgetChromeMode</c>
/// 批次 EV 立下的口径）——中间插一档或换顺序会让用户已有的设置静默变成另一档。</para>
/// </summary>
public enum EyeRestNotice
{
    /// <summary>只发一张右下角提示卡（批次 RV：托盘气泡在 Windows 11 上"API 返回成功、屏幕上什么都没有"）。</summary>
    Card = 0,
    /// <summary>暗幕 + 可提前结束。</summary>
    Curtain = 1,
    /// <summary>暗幕 + 不可跳过。</summary>
    Forced = 2,
}

/// <summary>
/// 护眼节拍的纯逻辑：什么时候该提醒、前台全屏时怎么让路、一次提醒之后从哪里重新开始。
/// <para>
/// 之所以把这套判定从服务里抽出来：它每一条都对应一种"只有真机才看得见"的难受——
/// 到点就弹不看场景（正在放 PPT / 全屏游戏被打断）、错过之后补一发（人离开半小时回来先挨一提醒）、
/// 提醒完不重置节拍（于是每 15 秒 Nag 一次）。这些都能在单测里用注入的时刻钉死，
/// 不必拿真机等 45 分钟。
/// </para>
/// <para>
/// <b>时间一律由调用方注入</b>（<c>DateTimeOffset now</c>），本类不读时钟、不起表、不碰窗口。
/// </para>
/// </summary>
public sealed class EyeRestPolicy
{
    /// <summary>默认提醒形式＝<b>暗幕，可点一下提前结束</b>（批次 RS 用户裁决：暗幕不该被强制休息绑住）。</summary>
    public const EyeRestNotice DefaultNotice = EyeRestNotice.Curtain;

    /// <summary>默认工作间隔（分钟）。规格 §2.7 定的 45 分钟。</summary>
    public const int DefaultIntervalMinutes = 45;

    public const int MinIntervalMinutes = 5;
    public const int MaxIntervalMinutes = 180;

    /// <summary>
    /// 界面上给的间隔档位（分钟）。<b>是档位不是滑杆</b>：与标注的细/中/粗同一口径
    /// （"点一下就能选到"），且滑杆会按自动保存的节奏反复整档读写存档。
    /// 默认值 45 必须在这一列里——否则设置页回灌时选不中当前值。
    /// </summary>
    public static readonly int[] IntervalOptions = { 15, 20, 30, 45, 60, 90 };

    /// <summary>给界面下拉用的档位文案（与 <see cref="IntervalOptions"/> 同序）。文案与数值放在一处，
    /// 免得"下拉里有 120 分钟但 ClampInterval 不认"这类分岔。</summary>
    public static IReadOnlyList<string> IntervalLabels { get; } =
        IntervalOptions.Select(minutes => minutes + " 分钟").ToList();

    /// <summary>第 <paramref name="index"/> 档是多少分钟（下标越界夹到两端，不抛）。</summary>
    public static int IntervalAt(int index)
        => IntervalOptions[Math.Clamp(index, 0, IntervalOptions.Length - 1)];

    /// <summary>某个分钟数落在哪一档（找不到就给最接近的一档，供设置页回灌下拉的选中项）。</summary>
    public static int IntervalIndexOf(int minutes)
    {
        var want = ClampInterval(minutes);
        var best = 0;
        for (var i = 1; i < IntervalOptions.Length; i++)
            if (Math.Abs(IntervalOptions[i] - want) < Math.Abs(IntervalOptions[best] - want)) best = i;
        return best;
    }

    /// <summary>
    /// 提醒形式的三档（<b>顺序就是界面上的顺序</b>）。默认 <see cref="EyeRestNotice.Curtain"/>：
    /// 发起人这次的裁决就是"暗幕不该被强制休息绑住"，所以默认给看得见效果的那一档。
    /// </summary>
    public static readonly EyeRestNotice[] NoticeOptions =
        { EyeRestNotice.Card, EyeRestNotice.Curtain, EyeRestNotice.Forced };

    /// <summary>给界面下拉用的三档文案（与 <see cref="NoticeOptions"/> 同序；文案与数值一处，理由同 <see cref="IntervalLabels"/>）。
    /// <para><b>秒数从 <see cref="RestSeconds"/> 来，不写进字符串里</b>：档位话说"20 秒"而遮罩倒数到 30 是用户第一眼看得见的自相矛盾。</para></summary>
    public static IReadOnlyList<string> NoticeLabels { get; } =
    [
        "只发一张右下角提示卡（什么都不盖）",
        "盖一层暗幕（点一下屏幕即可提前结束）",
        $"盖一层暗幕且 {RestSeconds} 秒不可跳过（强制休息）",
    ];

    /// <summary>某一档在下拉里排第几（认不得的值先走 <c>ClampNotice</c> 回落，不抛）。</summary>
    public static int NoticeIndexOf(EyeRestNotice notice) => Array.IndexOf(NoticeOptions, ClampNotice(notice));

    /// <summary>
    /// 某一档怎么说（下拉项、状态行、日志、「试一试」的回执共用这一条）。
    /// <para>"这一档到底会发生什么"写两份就会有一份漏掉——批次 RS 之前正是两份（强制那句写了"不能提前跳过"，
    /// 提示卡那句没写"点一下屏幕"），所以这里只留一处出处，界面各处都来取。</para>
    /// </summary>
    public static string NoticeLabel(EyeRestNotice notice) => NoticeLabels[NoticeIndexOf(notice)];

    /// <summary>第 <paramref name="index"/> 档是哪一种（下标越界夹到两端，不抛）。</summary>
    public static EyeRestNotice NoticeAt(int index)
        => NoticeOptions[Math.Clamp(index, 0, NoticeOptions.Length - 1)];

    /// <summary>
    /// 这一档是哪一种提醒形式。<b>读不出来（缺字段、或认不得的数值）回默认档</b>（＝暗幕可跳），
    /// <b>不夹到最近一端</b>：万一将来多出一个第四档、或有人手改出一个 7，静默变成"强制不可跳"
    /// ＝替一处数据损坏把用户所有退出出口关掉。
    /// <para>同一个函数也兜"传进来的枚举值本身不合法"（<c>(EyeRestNotice)7</c> 这种）——
    /// 判据只有一份，读取侧与使用侧走同一次夹（记忆 ⑧）。</para>
    /// </summary>
    public static EyeRestNotice ClampNotice(int? raw)
        => raw is { } value && IsKnownNotice(value) ? (EyeRestNotice)value : DefaultNotice;

    /// <summary>枚举版的同一道夹（调用方拿到的是枚举时用这条，别再自己判）。</summary>
    public static EyeRestNotice ClampNotice(EyeRestNotice notice)
        => IsKnownNotice((int)notice) ? notice : DefaultNotice;

    /// <summary>
    /// 存档里读出来的那个数是不是一个<b>认得的</b>档（0/1/2）。只由上面的 <c>ClampNotice</c> 使用——
    /// 单独暴露它是为了让"设置页下拉的选项数"这类普查能问同一份事实，而不是各自数一遍。
    /// </summary>
    public static bool IsKnownNotice(int raw) => raw is (int)EyeRestNotice.Card or (int)EyeRestNotice.Curtain or (int)EyeRestNotice.Forced;

    /// <summary>这一档要不要盖暗幕（提示卡档之外都盖）。</summary>
    public static bool UsesCurtain(EyeRestNotice notice) => notice != EyeRestNotice.Card;

    /// <summary>
    /// 这一档<b>允不允许用户提前结束</b>。只有强制档不允许——那是它唯一的语义，
    /// 所以绝不能反过来推："盖了暗幕"不等于"扣住用户"（旧形状正是这么绑的，才产生那句"要么用不上、要么退不出"）。
    /// </summary>
    public static bool IsSkippable(EyeRestNotice notice) => notice != EyeRestNotice.Forced;

    /// <summary>
    /// 幕布上那一句退出说明。两档暗幕各说一条，且<b>只在这里写一次</b>：界面（设置页／组件菜单）要说同一件事时
    /// 也来这儿取，免得"能不能提前退出"这类话在四处各写一份、其中一处漏掉。
    /// <para><b>刻意不承诺 Esc</b>：幕布是"点亮但不抢焦点"的窗（抢了就会把用户正在填的表单的打字吞进一扇空窗），
    /// 而按键只投递给有焦点的窗 ⇒ 不激活就收不到 Esc。可跳过档给的是<b>鼠标出口</b>（点一下即可，
    /// 点击不依赖焦点），这也符合"不吃键盘的覆盖窗要留纯鼠标出口"那条既有口径。</para>
    /// </summary>
    public static string NoticeHint(EyeRestNotice notice) => notice switch
    {
        EyeRestNotice.Forced => $"这一轮是强制休息：{RestSeconds} 秒走完会自动恢复，中途不能提前结束。",
        _ => "想提前结束就点一下屏幕任意处；什么都不做的话，时间到了会自动恢复。",
    };

    /// <summary>强制模式下遮罩停留多久（秒）。20 秒是"看远处"够用的最短值，再长就从护眼变成惩罚。</summary>
    public const int RestSeconds = 20;

    /// <summary>到点但前台全屏时，隔多久再探一次。</summary>
    public static readonly TimeSpan DeferGap = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 错过提醒的多大宽限期。超过它就不再补发，直接安静地重新起一轮。
    /// <para>轮询是 15 秒一次，正常永远落在宽限内；越过的只有三种情况：机器休眠/被挂起、
    /// 进程被暂停、以及全屏让路让了一大段（放映/会议/电影）。这三种情形下用户<b>已经离开过屏幕</b>，
    /// 补一发"该休息了"是打扰而不是护眼。</para>
    /// </summary>
    public static readonly TimeSpan MissedGrace = TimeSpan.FromMinutes(2);

    private DateTimeOffset _cycleStartedAt;
    private DateTimeOffset _nextProbeAt;

    public EyeRestPolicy(DateTimeOffset now)
    {
        _cycleStartedAt = now;
        _nextProbeAt = now;
    }

    /// <summary>到点之后要不要现在提醒，还是继续等／让路。</summary>
    public enum Decision
    {
        /// <summary>没到点、还在延后的重试窗口里、或这一轮已经错过——继续等。</summary>
        Waiting,
        /// <summary>到点了，但前台是全屏应用：这一按不弹，<see cref="DeferGap"/> 后再探。</summary>
        Deferred,
        /// <summary>该提醒了。</summary>
        Due,
    }

    /// <summary>本轮节拍的起点（设置页据此算"下一次大约几点"）。</summary>
    public DateTimeOffset CycleStartedAt => _cycleStartedAt;

    /// <summary>下一次该提醒的时刻（区间非法时按默认值算，见 <see cref="ClampInterval"/>）。</summary>
    public DateTimeOffset DueAt(int intervalMinutes) => _cycleStartedAt + TimeSpan.FromMinutes(ClampInterval(intervalMinutes));

    /// <summary>
    /// 把设置里的区间夹成可用值。<b>非法值回落默认而不是夹到边界</b>：
    /// 存档里出现 0 或负数（手改过 settings.json、旧版本字段缺省）时，夹到 5 分钟等于
    /// "每 5 分钟打断一次"——那是把一处数据损坏放大成最打扰人的行为，宁可回到大家认识的 45。
    /// </summary>
    public static int ClampInterval(int minutes)
        => minutes is >= MinIntervalMinutes and <= MaxIntervalMinutes ? minutes : DefaultIntervalMinutes;

    /// <summary>
    /// 现在该做什么决定。<paramref name="foregroundFullscreen"/> 只在"已经到点且真的要弹"时才被读，
    /// 所以调用方每次 tick 都可以放心传现取值——本函数不会为了 Waiting 去要求探测。
    /// </summary>
    public Decision Decide(DateTimeOffset now, int intervalMinutes, bool deferOnFullscreen, bool foregroundFullscreen)
    {
        var dueAt = DueAt(intervalMinutes);
        if (now < dueAt) return Decision.Waiting;

        // 先判宽限期：越界的一轮直接重置，不弹也不再去探前台（省掉一次无谓的 P/Invoke）
        if (now - dueAt > MissedGrace)
        {
            Reset(now);
            return Decision.Waiting;
        }

        if (now < _nextProbeAt) return Decision.Waiting;

        if (deferOnFullscreen && foregroundFullscreen)
        {
            _nextProbeAt = now + DeferGap;
            return Decision.Deferred;
        }
        return Decision.Due;
    }

    /// <summary>
    /// 一次提醒已经交出去（遮罩淡出／气泡已发）：节拍起点挪到现在，重新计时。
    /// <para>规格点名的那条坑——"提醒后不重置计时"会让提醒变成每 tick 一次的轰炸。
    /// 调用方必须<b>在展示之前</b>就调它：万一遮罩窗没建起来（内存不足、显示被拔掉），
    /// 宁可不提醒也不要每 15 秒重试轰炸。</para>
    /// </summary>
    public void Reset(DateTimeOffset now)
    {
        _cycleStartedAt = now;
        _nextProbeAt = now;
    }

    /// <summary>
    /// 窗口矩形是否铺满整块屏（＝全屏应用）。
    /// <para>用"覆盖住屏幕四条边"而不是"面积相等"：无边框全屏窗的尺寸可能与屏差一两个像素
    /// （DWM 边界、分数缩放取整），面积相等会因这一两个像素判成"不是全屏"，
    /// 于是提醒照旧砸在正在放 PPT 的屏幕上。容差只给 <see cref="FullscreenTolerancePx"/> 这么宽：
    /// 再宽就会把"露着任务栏的最大化窗口"也放进来（那条边差的是几十像素）。</para>
    /// <para>反过来说，最大化窗口必须判成"不是全屏"——用户还在正常干活，遮罩该弹；
    /// 判据正是"四条边都贴住"。</para>
    /// </summary>
    public static bool CoversScreen(IntRect window, IntRect screen)
        => window.X <= screen.X + FullscreenTolerancePx && window.Right >= screen.Right - FullscreenTolerancePx
        && window.Y <= screen.Y + FullscreenTolerancePx && window.Bottom >= screen.Bottom - FullscreenTolerancePx;

    /// <summary>全屏判据的边界容差（物理像素）。任务栏最小也有几十像素，2 不会把最大化窗口放进来。</summary>
    private const int FullscreenTolerancePx = 2;
}
