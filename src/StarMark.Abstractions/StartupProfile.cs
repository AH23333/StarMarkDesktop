#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace StarMark.Abstractions;

/// <summary>
/// 启动分段计时：把"启动慢"从体感变成日志里能前后对比的数。
/// <para>
/// 为什么要有它：性能清单上每条收益都写着"约 -50~150 ms"这种猜出来的数，而真机日志里已经出现过
/// "首帧阻塞 1516 ms"——没有分段数字，改完也不知道到底有没有变快，下一次还是一样靠猜。
/// 只做观测，不改任何行为；一行日志的成本换掉一整轮推测。
/// </para>
/// </summary>
public static class StartupProfile
{
    private static readonly object _gate = new();

    /// <summary>
    /// 唯一的时钟：<b>Stopwatch，不是 <c>Environment.TickCount64</c></b>。
    /// <para>
    /// 批次 WE-2 的起因就是这把尺子本身：TickCount64 的分辨率约 <b>15.6 ms</b>（跟着系统计时器节拍跳），
    /// 于是日志里那一串"+15 ms / +16 ms / +31 ms"不是实测值，而是量化台阶——
    /// 一颗真花 2 ms 的段与一颗花 15 ms 的段长得一模一样，"每颗组件都是 15 ms"那种整齐数字本身就是证据。
    /// 拿这把尺子永远分不清"首屏之后那 1.5 s 到底是谁占着 UI 线程"。
    /// </para>
    /// </summary>
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>
    /// 当前读数（毫秒）。<b>读数向下取整到整毫秒，但分辨率不是这个数</b>：
    /// 它来自 <see cref="Stopwatch"/> 的高频刻度，而不是 15.6 ms 的系统计时器节拍。
    /// </summary>
    private static long NowMs => Clock.ElapsedMilliseconds;

    private static long _start = NowMs;
    private static long _last = _start;
    private static readonly List<string> _segments = new();

    /// <summary>本类计时源的分辨率（每秒刻度数）。测试用它钉"这把尺子比一个系统计时器节拍细"。</summary>
    internal static long ClockTicksPerSecond => Stopwatch.Frequency;

    /// <summary>
    /// 最后一个<b>已经完成</b>的刻度（<see cref="Mark"/> 与 <see cref="Measure"/> 都算），形如「组件显示点亮 时钟 +210 ms」。
    /// <para>
    /// 为什么 <c>Segments</c> 的末尾不够用：启动里最重的那一段（建 19 个组件窗）只在<b>结束时</b> Mark 一次，
    /// 所以卡在那段中间时，分段表末尾还是"首帧提交"——等于没报。而 <c>Measure</c> 是逐件打的，
    /// 把它也算进刻度，卡顿日志才能指出"最后一个做完的动作是谁"。
    /// </para>
    /// <para>只赋字符串（引用写是原子的），且<b>不含锁</b>：它可能在被别的线程读（看门狗的观察线程）。</para>
    /// </summary>
    public static string? LastCheckpoint => _lastCheckpoint;

    private static volatile string? _lastCheckpoint;

    /// <summary>
    /// 那把刻度是<b>多久以前</b>打的（毫秒）；一条刻度都没有时返回 null。
    /// <para>没有这个数，"卡顿前最后一个完成的刻度"在程序跑了几分钟之后会变成一根误导人的指针：
    /// 刻度表在启动之后就基本不再前进，于是任何一次后来的冻结都会指着"组件恢复任务返回"，
    /// 读的人以为卡在启动里。带上年龄，日志自己就说清了"这是很久以前的刻度，不是刚刚"。</para>
    /// </summary>
    public static long? LastCheckpointAgeMs
    {
        // 64 位字段不能标 volatile（CS0677），所以成对用 Volatile.Read / Interlocked.Exchange：
        // 读的人（看门狗的观察线程）不能看到写了一半的值。
        get { var at = System.Threading.Volatile.Read(ref _lastCheckpointAt); return at < 0 ? null : NowMs - at; }
    }

    private static long _lastCheckpointAt = -1;

    /// <summary>
    /// 输出去处。<b>测试里必须换掉</b>：单测不该往用户真实的日志文件里写行。
    /// </summary>
    internal static Action<string> Sink = StarLog.Info;

    /// <summary>
    /// 一条刻度的<b>内存读数</b>从哪来（批次 SS）。默认走真系统 API；测试注入固定读数——
    /// 真实值不许进断言（同 #212 那一族：钉住一个会变的数，得到的只是一条随时序飘红的"契约"）。
    /// </summary>
    internal static Func<string> MemoryReader = SystemMemoryLine;

    /// <summary>
    /// 一条刻度的<b>本线程 CPU 读数</b>从哪来（批次 TC）。<b>由宿主在启动最早处接进来</b>——
    /// <c>UIStallWatchdog.InstallSelfCpuReader()</c>（App 构造里、第一把刻度之前）。
    /// <para>
    /// 为什么不自己读：这一层写明不许有 Win32 互操作（层次闸门
    /// <c>AnsiCodePageWiringGateTests.AbstractionsLayerStillContainsNoWin32</c> 就钉着这件事），
    /// 而"按线程的 CPU 时间"在 net9 的 BCL 里根本没有（<c>Thread.ProcessorTime</c>／
    /// <c>Thread.GetCurrentProcessorTime()</c> 都是 CS0117），只能是 kernel32 的活。
    /// </para>
    /// <para>
    /// <b>没接线就是 null</b> ⇒ 刻度行老实写"未知"，并在第一次用到时说一次。这个形状不崩、不报错、
    /// 只是那一列从此空白——而它是"这段到底是算还是等"唯一能定方向的数据（批次 WJ／TB 各栽过一次）。
    /// </para>
    /// </summary>
    public static Func<long?>? ThreadCpuMsReader;

    /// <summary>刻度行上那半句 CPU 的<b>唯一出处</b>。没接线／读不到都写"未知"，<b>不许退化成 0</b>。</summary>
    private static string ThreadCpuField()
    {
        if (ThreadCpuMsReader is not { } read) return CpuUnknown("未接线：宿主没把本线程 CPU 读数交给刻度");
        try
        {
            return read() is { } ms ? $"{ms} ms" : CpuUnknown("接线方返回 null（系统没给这个数）");
        }
        catch (Exception ex)
        {
            return CpuUnknown(ex.GetType().Name);   // 读不到就少一个数，不许静默补 0
        }
    }

    private static bool _cpuUnavailableReported;

    /// <summary>
    /// 失效要说一次，但只说一次：这根柱子跑在每一把刻度上，每次都念一遍就成了噪声源。
    /// 不说则是另一种坏——它不崩、不报错、LastError 也不动，只是那一列从此空白，
    /// 而它是"这段到底是算还是等"唯一能定方向的数据（批次 WJ／TB 各栽过一次）。
    /// </summary>
    private static string CpuUnknown(string why)
    {
        if (!_cpuUnavailableReported)
        {
            _cpuUnavailableReported = true;
            StarLog.Warn($"[启动] 本线程 CPU 读数不可得（{why}），刻度行的 CPU 列将一直是\"未知\"");
        }
        return "未知";
    }

    private static Process? _self;

    /// <summary>
    /// 工作集／私有字节／托管堆／句柄／已加载程序集。<b>只给形状不给判断</b>：
    /// 这五个数要能前后对比，"这一档值不值"是读表的人定的，不是这把尺子定的。
    /// <para>句柄与程序集数是<b>懒加载是否真的生效</b>的唯一廉价证据：一个功能关着却仍在加载，
    /// 数字会先于任何界面症状变胖（而"关着也在跑"这件事在屏幕上根本看不见）。</para>
    /// </summary>
    internal static string SystemMemoryLine()
    {
        try
        {
            var p = _self ??= Process.GetCurrentProcess();
            // 必须 Refresh：同一进程里读自己时，Process 对象会把首次读到的值缓存下来——
            // 不刷新的那把尺子一整轮启动都报同一个数（SS 第一跑就是这样：工作集 109 MB、句柄 561 一路不动，
            // 而外部采样同一进程明明是 292 MB / 2044 句柄）。一条永远不变的读数比没有读数更误导人。
            p.Refresh();
            const long Mb = 1024L * 1024L;
            return "工作集 " + p.WorkingSet64 / Mb + " MB · 私有 " + p.PrivateMemorySize64 / Mb
                + " MB · 托管堆 " + GC.GetTotalMemory(false) / Mb + " MB · 句柄 " + p.HandleCount
                + " · 程序集 " + AppDomain.CurrentDomain.GetAssemblies().Length;
        }
        catch (Exception ex)
        {
            // 尺子不许把被量的东西弄崩：这一段跑在启动路径上，读不到就照实说读不到（不许静默少一行）
            return "读数不可得（" + ex.GetType().Name + "）";
        }
    }

    /// <summary>记一段（自上一段起的耗时 + 自会话开始的累计）。</summary>
    public static void Mark(string segment) => Mark(segment, NowMs);

    /// <summary>带时刻的重载只为可测：真实时钟下"增量该是几毫秒"没法断言。</summary>
    internal static void Mark(string segment, long now)
    {
        string line;
        lock (_gate)
        {
            var sinceLast = now - _last;
            _last = now;
            _segments.Add($"{segment} +{sinceLast} ms");
            NoteCheckpoint(segment, sinceLast);
            // CPU 读数与那两个时长**写在同一行**（批次 TC）：TB 的教训是"同一行印出来"不等于"同一段量出来"——
            // 分在两行（或另取时刻）就给了读的人做错减法的机会。写在一行、且带着线程号，
            // 相邻两把刻度就能直接求"这段墙钟里这根线程烧了多少 CPU"，跨线程的两行则一眼就能看出不能相减。
            line = $"[启动] {segment}：+{sinceLast} ms（自会话开始累计 {now - _start} ms" +
                   $"｜线程#{Environment.CurrentManagedThreadId} CPU 累计 {ThreadCpuField()}）";
        }
        Sink(line);
        // 计时行与内存行**成对**：只发一条的话，读表的人得自己猜"这堆内存是在哪一段之间涨的"。
        // Measure 不发——它按阈值过滤噪声、且逐组件高频调用，每段都读一次系统开销就跑到被量的那段里去了。
        Sink($"[内存] {segment}：{MemoryReader()}");
    }

    /// <summary>本次启动已记下的分段表（诊断面板用，按记录顺序）。</summary>
    public static IReadOnlyList<string> Segments
    {
        get { lock (_gate) return _segments.ToArray(); }
    }

    /// <summary>重新对齐基线并清空分段表。测试用（真实启动路径每次都从进程起点开始，不需要重置）。</summary>
    internal static void ResetForTests(long start)
    {
        lock (_gate)
        {
            _start = start;
            _last = start;
            _segments.Clear();
            _lastCheckpoint = null;
            System.Threading.Interlocked.Exchange(ref _lastCheckpointAt, -1);
        }
    }

    /// <summary>
    /// 推进"最后一个完成的刻度"。<b>标签与年龄一起写</b>：只改标签会让年龄停在更早的那把刻度上，
    /// 日志里就会出现"刚刚完成的刻度，其实是 3 分钟前"。
    /// </summary>
    private static void NoteCheckpoint(string label, long ms)
    {
        _lastCheckpoint = $"{label} +{ms} ms";
        System.Threading.Interlocked.Exchange(ref _lastCheckpointAt, NowMs);   // 年龄一律按真实时钟算（带时刻的 Mark 重载不许拿合成值当基准）
    }

    /// <summary>量一段真实工作并按阈值写日志，返回它的返回值。</summary>
    public static T Measure<T>(string label, Func<T> work, long logWhenMs = 0)
    {
        var from = NowMs;
        var result = work();
        var ms = NowMs - from;
        // 刻度**不论是否达到写日志的阈值都要记**：低于阈值的段正是卡顿时期仅有的刻度（见 LastCheckpoint）。
        // 抛出的一段不记（它没完成），于是看门狗看到的是"最后一个做完的动作"，而不是卡住的那个。
        NoteCheckpoint(label, ms);
        if (ms >= logWhenMs) Sink($"[耗时] {label}：{ms} ms");
        return result;
    }

    /// <summary>量一段没有返回值的工作并按阈值写日志。</summary>
    public static void Measure(string label, Action work, long logWhenMs = 0)
    {
        var from = NowMs;
        work();
        var ms = NowMs - from;
        NoteCheckpoint(label, ms);
        if (ms >= logWhenMs) Sink($"[耗时] {label}：{ms} ms");
    }
}
