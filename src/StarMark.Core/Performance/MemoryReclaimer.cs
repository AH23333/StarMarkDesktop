#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using StarMark.Abstractions;

namespace StarMark.Core.Performance;

/// <summary>可被 <see cref="MemoryReclaimer"/> 回收的缓存 / 资源参与者。</summary>
public interface IMemoryReclaimParticipant
{
    /// <summary>主动丢弃最不常用的条目，把占用压到预算内（由 Reclaimer 在超预算时调用）。</summary>
    void Trim();
}

/// <summary>
/// 内存门禁（DeskBox 的 <c>MemoryReclaimer</c> 同款思路）：常驻应用的进程工作集监控器。
/// <para>
/// 按 <see cref="PerformanceSettingsPolicy.EffectiveBudgetMb"/> 的预算，每隔一段时间检查进程工作集，
/// 超预算即触发回收——先 <see cref="GC.Collect(int)"/>，再通知所有注册的参与者（有界缓存等）<see cref="IMemoryReclaimParticipant.Trim"/>。
/// 任意一步异常都静默吞掉，绝不冒泡到 UI 线程（否则可能演变成未处理异常致应用崩溃）。
/// </para>
/// </summary>
public sealed class MemoryReclaimer
{
    /// <summary>全局单例。</summary>
    public static MemoryReclaimer Default { get; } = new();

    private readonly List<WeakReference<IMemoryReclaimParticipant>> _participants = new();
    private readonly object _lock = new();
    private System.Timers.Timer? _timer;
    private int _running;

    /// <summary>最近一次测量的进程工作集（MB），供诊断页展示。</summary>
    public double LastWorkingSetMb { get; private set; }

    /// <summary>上次回收发生的时间；未发生过为 null。</summary>
    public DateTimeOffset? LastReclaimAt { get; private set; }

    /// <summary>回收发生时触发（诊断用），参数为触发时的工作集 MB。</summary>
    public event Action<double>? Reclaiming;

    private MemoryReclaimer() { }

    /// <summary>启动后台监控（幂等）。</summary>
    public void Start(TimeSpan? interval = null)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        _timer = new System.Timers.Timer(interval?.TotalMilliseconds ?? 15000)
        {
            AutoReset = true,
        };
        _timer.Elapsed += OnTick;
        _timer.Start();
        // 启动即测一次，更新诊断读数（不强制回收）
        try { LastWorkingSetMb = ReadWorkingSetMb(); } catch { }
    }

    /// <summary>停止后台监控（幂等）。</summary>
    public void Stop()
    {
        if (Interlocked.Exchange(ref _running, 0) == 0) return;
        try { _timer?.Stop(); _timer?.Dispose(); } catch { }
        _timer = null;
    }

    /// <summary>注册一个可被回收的参与者（弱引用，参与者销毁后自动失效，不会阻止 GC）。</summary>
    public void Register(IMemoryReclaimParticipant participant)
    {
        if (participant is null) return;
        lock (_lock)
        {
            _participants.Add(new WeakReference<IMemoryReclaimParticipant>(participant));
            // 顺手清掉已死亡的弱引用，避免无限增长
            _participants.RemoveAll(w => !w.TryGetTarget(out _));
        }
    }

    /// <summary>立即执行一次回收（无论是否超预算）。供「诊断 → 立即清理」等入口调用。</summary>
    public void Trim()
    {
        try
        {
            var mb = ReadWorkingSetMb();
            LastWorkingSetMb = mb;
            LastReclaimAt = DateTimeOffset.Now;
            Reclaiming?.Invoke(mb);

            // 1) 托管堆回收（最便宜、最常生效）
            GC.Collect(2, GCCollectionMode.Forced, blocking: false);
            GC.WaitForPendingFinalizers();

            // 2) 通知参与者各自丢弃最不常用的条目
            List<IMemoryReclaimParticipant> alive;
            lock (_lock)
            {
                alive = new List<IMemoryReclaimParticipant>();
                _participants.RemoveAll(w =>
                {
                    if (w.TryGetTarget(out var p)) { alive.Add(p); return false; }
                    return true;
                });
            }
            foreach (var p in alive)
            {
                try { p.Trim(); } catch (Exception ex) { StarLog.Error("内存参与者回收失败", ex); }
            }
        }
        catch (Exception ex)
        {
            StarLog.Error("内存回收失败", ex);
        }
    }

    private void OnTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            var mb = ReadWorkingSetMb();
            LastWorkingSetMb = mb;
            var budget = PerformanceSettingsPolicy.EffectiveBudgetMb();
            if (mb > budget) Trim();
        }
        catch (Exception ex)
        {
            StarLog.Error("内存门禁轮询失败", ex);
        }
    }

    private static double ReadWorkingSetMb()
    {
        using var proc = Process.GetCurrentProcess();
        return proc.WorkingSet64 / (1024.0 * 1024.0);
    }
}
