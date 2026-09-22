#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;

namespace StarMark.UI.Helpers;

/// <summary>
/// 把 <see cref="DataChangeHub"/> 的广播接成一次<b>去抖后</b>的重载。
/// <para>
/// 三个必要设计：
/// ① <b>去抖</b>：一次批量写入（拖拽排序会连写 N 条、同步会一次导入上千条）会连发 N 次通知，
///    不去抖就是把同一个列表重载 N 遍——表现为点一下待办卡几秒（每次重载都要重排 ListView）。
/// ② <b>强持有委托</b>：<see cref="DataChangeHub"/> 是弱引用订阅，委托若只被弱引用持有会被 GC 立刻回收，
///    订阅随即失效（弱事件的经典坑）。这里用字段把它钉住。
/// ③ <b>最长合并窗口</b>：只"续期"的去抖在持续写入（一次同步导入上千条）下会永远推后，
///    表现为同步跑完前界面纹丝不动、跑完集中刷一次。超过 <c>maxWaitMs</c> 就不再续期。
/// </para>
/// </summary>
public sealed class DataChangeReloader : IDisposable
{
    private readonly Action _handler;               // 强持有，防止委托被回收导致订阅失效
    private readonly IDisposable _subscription;
    private readonly DispatcherQueue _queue;
    private readonly DispatcherQueueTimer _timer;
    private readonly Func<Task> _reload;
    private readonly long _maxWaitMs;
    private long _pendingSince;                     // 本轮"一直在变"的起点（0＝无待刷通知）
    private bool _disposed;

    /// <param name="reload">真正执行的重载（会在 UI 线程上被调用）。</param>
    /// <param name="debounceMs">合并窗口。批量写入时只跑最后一次。</param>
    /// <param name="maxWaitMs">最长合并窗口：自首次通知起超过它就不再往后推（见 ③）。</param>
    public DataChangeReloader(Func<Task> reload, int debounceMs = 250, int maxWaitMs = 1500)
    {
        _reload = reload;
        _maxWaitMs = Math.Max(debounceMs, maxWaitMs);
        _queue = DispatcherQueue.GetForCurrentThread()
                 ?? throw new InvalidOperationException("DataChangeReloader 必须在 UI 线程创建");

        _timer = _queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(debounceMs);
        _timer.Tick += Timer_Tick;

        _handler = OnDataChanged;
        _subscription = DataChangeHub.Subscribe(_handler);
    }

    private void OnDataChanged()
    {
        // 通知可能来自任意线程（仓储跑在线程池上）。DispatcherQueueTimer 是套间亲和对象，
        // 不能跨线程直接 Start —— 用 TryEnqueue 切回 UI 线程再起表。
        _queue.TryEnqueue(() =>
        {
            if (_disposed) return;
            var now = Environment.TickCount64;
            if (_pendingSince == 0) _pendingSince = now;
            // 重置合并窗口：连续通知最终只触发一次重载。
            // ③ 但纯"续期"有个副作用：后台同步连写上千条时窗口被一直往后推，期间 UI 一次都不刷、
            //    写完集中炸一次。超过最长窗口就不再续期，让已经排着的表按原计划触发。
            if (now - _pendingSince < _maxWaitMs)
            {
                _timer.Stop();
                _timer.Start();
            }
        });
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        _timer.Stop();
        _pendingSince = 0;
        if (_disposed) return;
        _ = _reload();
    }

    /// <summary>退订。组件卸载时调用（忘了也不会泄漏——弱引用会自然失效）。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _timer.Stop(); } catch { }
        try { _subscription.Dispose(); } catch { }
    }
}
