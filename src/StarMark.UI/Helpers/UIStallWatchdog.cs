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
/// <para>
/// 两条日志都带上<b>卡顿之前最后一个完成的刻度</b>（<c>StartupProfile.LastCheckpoint</c>，
/// 由 <c>Mark</c> 与 <c>Measure</c> 共同推进）。只有时长时，"启动后 1.5 s 卡住"是一段悬空的空档——
/// 分段表只在整块工作<b>结束时</b>记一笔，卡在那块工作中间时它还是上一笔，等于没报。
/// 夹住这一笔，空档才有左边的刻度。刻度还带上"当时已经放了多久"——启动之外它几乎不再前进，
/// 光看标签会把十分钟后的冻结读成卡在启动里。
/// </para>
/// <para>
/// 恢复那一行还带上<b>这段时间 UI 线程自己烧了多少 CPU</b>（<c>GetThreadTimes</c> 用户态＋内核态）。
/// 两个数一比就能定方向：<b>CPU ≈ 墙钟＝忙在自己手上</b>（整表重读、几百个卡片元素、布局重排——该拆分或挪线程）；
/// <b>CPU ≪ 墙钟＝在等别人</b>（写锁、IPC、外部进程、DWM——那种情况下优化 CPU 是白改）。
/// 只有这条日志线在真机上知道答案，而改错的代价是把不忙的那一段挪来挪去。
/// </para>
/// </summary>
public static class UIStallWatchdog
{
    private const int IntervalMs = 500;
    private const long StallMs = 1000;      // 超过即判定为一次卡顿
    private const long SuspendedMs = IntervalMs * 6L;

    /// <summary>
    /// <c>GetThreadTimes</c> 要求的访问权＝<b>THREAD_QUERY_INFORMATION（0x0040）</b>。
    /// <para>
    /// 这里原来写的是 <c>0x0400</c>——那是 <c>THREAD_DIRECT_IMPERSONATION</c>；而
    /// <c>THREAD_QUERY_LIMITED_INFORMATION</c> 又是另一个数（0x0800）。三个数长得很像，
    /// 写错的后果不是崩，是 <c>OpenThread</c> 返回 0 ⇒ CPU 列永远显示"未知"，
    /// 而这根柱子正是"启动后 UI 冻结"唯一能定方向的数据（CPU≈墙钟＝忙在自己手上；CPU≪墙钟＝在等锁/IPC）。
    /// </para>
    /// </summary>
    private const uint ThreadQueryInformation = 0x0040;

    private static Timer? _timer;
    private static long _lastAck;           // UI 线程最近一次执行回执的时刻
    private static long _lastProbe;         // 看门狗自己最近一次运行的时刻
    private static long _stallStart;        // 0 = 当前没有正在记录的卡顿
    private static long _reportedMs;        // 上一条"仍在阻塞"报的时长（倍增节流）
    private static volatile string? _where; // 回执时刻顺带取的前台页（在 UI 线程上读）
    private static IntPtr _uiThread;        // UI 线程句柄：只为读它用了多少 CPU
    private static long _stallStartCpu = -1;

    /// <summary>
    /// 一次回执的<b>两个读数</b>：UI 线程空闲到那一刻的时刻，与那一刻它已累计的 CPU。
    /// <para>
    /// 为什么必须成对（批次 TB）：恢复行要说的是"这段墙钟里 UI 烧了多少 CPU"，
    /// 两个数就得取自<b>同一个瞬间</b>。拆成两个字段各写各的（时刻一份、CPU 一份），
    /// 观测线程就可能读到"新时刻＋旧 CPU"的错配一对——又是一个不崩、不报错、只把结论偏掉的形状。
    /// </para>
    /// </summary>
    private sealed record AckMark(long AtMs, long Cpu);

    private static volatile AckMark? _mark;

    /// <summary>
    /// 判定为卡顿那一刻的最后一条<b>已完成刻度</b>（快照，含当时的年龄）。
    /// <para>WE-2 缺的就是这个：分段表停在"桌面组件恢复"，看门狗报的 1.5 s 落在它<b>之后</b>，
    /// 于是只知道"卡了"、不知道"卡在哪件事之前"。带上这一条，悬空的空档就有了左边的刻度。</para>
    /// </summary>
    private static string? _stallCheckpoint;

    public static void Start(DispatcherQueue? queue)
    {
        if (queue is null || _timer is not null) return;
        var now = Environment.TickCount64;
        Interlocked.Exchange(ref _lastAck, now);
        Interlocked.Exchange(ref _lastProbe, now);
        // Start 就在 UI 线程上调（App 里紧接主窗创建），所以这里取到的正是被观测的那根线程
        if (_uiThread == IntPtr.Zero)
        {
            _uiThread = OpenThread(ThreadQueryInformation, false, GetCurrentThreadId());
            // 取不到句柄就让日志当场说一次：否则整根 CPU 柱子静默失效，
            // 看到的只是"未知"，谁也不会去查为什么（这次的教训就是它默默坏了很久）
            if (_uiThread == IntPtr.Zero)
                StarLog.Warn($"[卡顿] UI 线程 CPU 取不到（OpenThread 失败 Win32 " +
                             $"{System.Runtime.InteropServices.Marshal.GetLastWin32Error()}），" +
                             "恢复行将只有墙钟时长");
        }
        // 起点快照必须在这里先放一份，不能等第一次回执：真机 6 颗那一跑里，第一段长阻塞就发生在那之前，
        // 于是 `_mark` 还是 null ⇒ 恢复行只能写"其间用了约 未知 ms CPU"（同时"前台页=未知"，同一根因）。
        // 时刻与 CPU 仍取自同一个 now——批次 TB 立的规矩在这一格同样成立。
        _mark = new AckMark(now, CpuMs() ?? -1);
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
            _stallStartCpu = -1;
            _stallCheckpoint = null;
            return;
        }

        var stalled = now - Interlocked.Read(ref _lastAck);
        if (stalled < StallMs)
        {
            // 基准取"进入卡顿那一刻快照的回执时刻"：此处若再读 _lastAck，可能已被刚排队的回执刷新成 now。
            var start = Interlocked.Exchange(ref _stallStart, 0);
            var cpuAtStart = _stallStartCpu;      // 这两个卡顿起点快照只有观察线程读写，不用原子
            var checkpointAtStart = _stallCheckpoint;
            _stallStartCpu = -1;
            _stallCheckpoint = null;
            if (start > 0)
            {
                var wall = now - start;
                var cpu = cpuAtStart < 0 ? null : CpuMs() - cpuAtStart;
                StarLog.Warn($"[卡顿] UI 线程恢复，本轮阻塞约 {wall} ms" +
                             $"（其间 UI 线程自己用了约 {(cpu is { } c ? c.ToString() : "未知")} ms CPU，" +
                             $"卡顿期间前台页={_where ?? "未知"}，卡顿前最后一个完成的刻度：{checkpointAtStart ?? "无"}）");
            }
        }
        else
        {
            // 起点与 CPU 基线**取自同一个回执快照**（批次 TB）。CPU 基线早先是在"判定为卡顿"那一刻才取的，
            // 于是恢复行里的 CPU 只覆盖"越过 1 s 阈值之后"那一段，而 wall 覆盖整段——
            // 两者相减会凭空多出约一个阈值的"看起来在等"的时间：那行字面写着"其间用了 Y ms CPU"，
            // 量的却不是"其间"。缺陷不崩、不报错、LastError 也不动，只有结论会被偏掉。
            var mark = _mark;
            var startMs = mark?.AtMs ?? Interlocked.Read(ref _lastAck);
            if (Interlocked.CompareExchange(ref _stallStart, startMs, 0) == 0)
            {
                _stallStartCpu = mark?.Cpu ?? -1;
                _stallCheckpoint = CheckpointAtStallStart();   // 同上：事后读会把卡顿期间才补上的刻度当成起点
            }
            if (stalled >= Math.Max(StallMs, Interlocked.Read(ref _reportedMs) * 2))
            {
                Interlocked.Exchange(ref _reportedMs, stalled);
                StarLog.Warn($"[卡顿] UI 线程已阻塞 {stalled} ms（仍在进行，前台页={_where ?? "未知"}，" +
                             $"卡顿前最后一个完成的刻度：{_stallCheckpoint ?? "无"}）");
            }
        }

        // 阻塞期间照样投递：队列里最多少量空任务，等 UI 缓过来会一口气执行完，
        // 由此 _lastAck 重新跟上——恢复行就是这么触发的。
        if (!queue.TryEnqueue(Ack))
            Stop();   // 队列已注销（主窗没了），没有观测对象
    }

    private static void Ack()
    {
        var now = Environment.TickCount64;
        // 时刻与 CPU 一次写进同一个快照（一次 GetThreadTimes，每根心跳 500 ms 一把，代价是 µs 级）：
        // 分开写两个字段，观测线程读到的就可能是"这一次的时刻＋上一次的 CPU"。
        _mark = new AckMark(now, CpuMs() ?? -1);
        Interlocked.Exchange(ref _lastAck, now);
        Interlocked.Exchange(ref _reportedMs, 0);
        var tag = App.MainWindow?.ViewModel?.CurrentPageTag;
        _where = string.IsNullOrWhiteSpace(tag) ? null : tag;
    }

    /// <summary>UI 线程已用 CPU 时间（用户态＋内核态，毫秒）。<b>取不到就返回 null</b>——日志里宁可少一个数，也不报个假的。</summary>
    private static long? CpuMs()
        => _uiThread != IntPtr.Zero && GetThreadTimes(_uiThread, out _, out _, out var kernel, out var user)
            ? (kernel + user) / 10_000            // FILETIME 是 100 ns 刻度
            : null;

    /// <summary>
    /// 把"<b>当前线程</b>的 CPU 读数"接进启动刻度（批次 TC，由 <c>App</c> 构造里、第一把刻度之前调用）。
    /// <para>
    /// 为什么不写在 <c>StartupProfile</c> 里：那一层写明不许碰 Win32（层次闸门
    /// <c>AnsiCodePageWiringGateTests.AbstractionsLayerStillContainsNoWin32</c> 守着），而 net9 的 BCL
    /// 根本没有按线程的 CPU 时间（<c>Thread.ProcessorTime</c> 与 <c>Thread.GetCurrentProcessorTime()</c> 都是
    /// CS0117，<c>GetCurrentThreadTimes</c> 又不在 kernel32 的直接导出里）⇒ 只能是这里的活。
    /// 放本文件而不是另开一处：线程时间的互操作只许有一个主人，跨线程读（<see cref="CpuMs"/>）与自读
    /// 共用同一个 <c>GetThreadTimes</c> 声明。
    /// </para>
    /// </summary>
    public static void InstallSelfCpuReader() => StartupProfile.ThreadCpuMsReader = SelfThreadCpuMs;

    /// <summary>
    /// 读<b>自己</b>这根线程用的是当前线程<b>伪句柄</b>（<c>GetCurrentThread()</c> 即 <c>(HANDLE)-2</c>，
    /// 内联定义不是导出符号，所以按值写）。这条路上<b>没有权利位可写错</b>——而 <see cref="CpuMs"/>
    /// 那种跨线程读要的 <c>THREAD_QUERY_INFORMATION</c> 一旦写错，不崩不报错，只是整列从此"未知"
    /// （批次 WJ 栽过的就是那一位）。
    /// </summary>
    private static long? SelfThreadCpuMs()
        => GetThreadTimes(CurrentThreadHandle, out _, out _, out var kernel, out var user)
            ? (kernel + user) / 10_000            // 与 CpuMs 同一个口径，两边相减才是同一种数
            : null;

    private static readonly IntPtr CurrentThreadHandle = new(-2);

    /// <summary>
    /// 卡顿起点那一刻的刻度，连同它<b>当时已经放了多久</b>（形如「组件显示点亮 时钟 +210 ms，163 ms 前」）；
    /// 一条刻度都没有则 null。
    /// <para>为什么要带上年龄：刻度表主要在启动阶段前进，程序跑起来之后就几乎不再动。
    /// 只报标签的话，一次发生在十分钟之后的冻结会被写成"卡在『组件恢复任务返回』之前"，
    /// 读的人照着启动那条链去找，永远找不到。年龄让这一行自己说清"那是很久以前的刻度"。</para>
    /// </summary>
    private static string? CheckpointAtStallStart()
    {
        var label = StartupProfile.LastCheckpoint;
        if (label is null) return null;
        var ageText = StartupProfile.LastCheckpointAgeMs is not { } a ? "时间未知"
            : a < 1000 ? $"{a} ms 前"
            : $"{NumberText.UpTo1(a / 1000.0)} 秒前";
        return $"{label}，{ageText}";
    }

    // 这三个 kernel32 调用只服务"卡顿取证"这一件事，所以留在本文件里不外溢
    // （WindowInterop 那份全是窗口/显示器相关，混进去只会让两边都难查）。
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetThreadTimes(IntPtr hThread, out long lpCreationTime,
        out long lpExitTime, out long lpKernelTime, out long lpUserTime);
}
