#nullable enable
using System.Threading;
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;

namespace StarMark.UI.Helpers;

/// <summary>
/// UI 线程卡顿看门狗：把"感觉卡死了"变成日志里可查的行数与时长。
/// <para>
/// 原理：线程池每 <see cref="IntervalMs"/> 向 UI 队列投一个空任务，任务体只干一件事——把"我被执行了"
/// 的时刻写回 <c>_lastAck</c>。于是"本轮探测时刻 − 最近回执时刻"就是 UI 线程已经多久没空出来，
/// 与它在哪件事上忙无关（阻塞在 SQLite、Everything IPC 还是整表重建都会被抓到）。
/// </para>
/// <para>
/// 只做观测：不改任何行为、不引依赖。日志量有上界（时长倍增上报，一次长冻结约几行），
/// 且进程被挂起/系统睡眠会被识别为"看门狗自己也没被调度"而不算应用卡顿。
/// </para>
/// </summary>
public static class UIStallWatchdog
{
    private const int IntervalMs = 500;
    private const long StallMs = 1000;      // 超过即判定为一次卡顿
    private const long SuspendedMs = IntervalMs * 6L;

    private static Timer? _timer;
    private static long _lastAck;           // UI 线程最近一次执行回执的时刻
    private static long _lastProbe;         // 看门狗自己最近一次运行的时刻
    private static long _stallStart;        // 0 = 当前没有正在记录的卡顿
    private static long _reportedMs;        // 上一条"仍在阻塞"报的时长（倍增节流）
    private static volatile string? _where; // 回执时刻顺带取的前台页（在 UI 线程上读）

    public static void Start(DispatcherQueue? queue)
    {
        if (queue is null || _timer is not null) return;
        var now = Environment.TickCount64;
        Interlocked.Exchange(ref _lastAck, now);
        Interlocked.Exchange(ref _lastProbe, now);
        _timer = new Timer(_ => Probe(queue), null, IntervalMs, IntervalMs);
    }

    public static void Stop()
    {
        Interlocked.Exchange(ref _timer, null)?.Dispose();
    }

    private static void Probe(DispatcherQueue queue)
    {
        var now = Environment.TickCount64;
        var selfGap = now - Interlocked.Read(ref _lastProbe);
        Interlocked.Exchange(ref _lastProbe, now);

        if (selfGap >= SuspendedMs)
        {
            // 连本回调都隔了这么久才跑 ⇒ 进程被挂起或系统睡眠，重新对齐基线，不算应用卡顿
            Interlocked.Exchange(ref _lastAck, now);
            Interlocked.Exchange(ref _stallStart, 0);
            return;
        }

        var stalled = now - Interlocked.Read(ref _lastAck);
        if (stalled < StallMs)
        {
            // 基准取"进入卡顿那一刻快照的回执时刻"：此处若再读 _lastAck，可能已被刚排队的回执刷新成 now。
            var start = Interlocked.Exchange(ref _stallStart, 0);
            if (start > 0)
                StarLog.Warn($"[卡顿] UI 线程恢复，本轮阻塞约 {now - start} ms（卡顿期间前台页={_where ?? "未知"}）");
        }
        else
        {
            Interlocked.CompareExchange(ref _stallStart, Interlocked.Read(ref _lastAck), 0);
            if (stalled >= Math.Max(StallMs, Interlocked.Read(ref _reportedMs) * 2))
            {
                Interlocked.Exchange(ref _reportedMs, stalled);
                StarLog.Warn($"[卡顿] UI 线程已阻塞 {stalled} ms（仍在进行，前台页={_where ?? "未知"}）");
            }
        }

        // 阻塞期间照样投递：队列里最多少量空任务，等 UI 缓过来会一口气执行完，
        // 由此 _lastAck 重新跟上——恢复行就是这么触发的。
        if (!queue.TryEnqueue(Ack))
            Stop();   // 队列已注销（主窗没了），没有观测对象
    }

    private static void Ack()
    {
        Interlocked.Exchange(ref _lastAck, Environment.TickCount64);
        Interlocked.Exchange(ref _reportedMs, 0);
        var tag = App.MainWindow?.ViewModel?.CurrentPageTag;
        _where = string.IsNullOrWhiteSpace(tag) ? null : tag;
    }
}
