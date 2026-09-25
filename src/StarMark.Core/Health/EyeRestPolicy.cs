#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Capture;

namespace StarMark.Core.Health;

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
