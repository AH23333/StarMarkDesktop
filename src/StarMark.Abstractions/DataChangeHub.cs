#nullable enable
using System;
using System.Collections.Generic;

namespace StarMark.Abstractions;

/// <summary>
/// 数据变更广播：**任何一处**改了库里的数据（主界面置顶/删除/改标签、同步导入、组件自己增删待办）
/// 都在这里发一次通知，所有订阅者据此重载，从而做到「主界面改了，桌面组件立刻跟着变」。
/// <para>
/// 为什么放在 Abstractions：<see cref="IItemRepository"/> 的实现（StarMark.Data）是最全的写入收口点，
/// 而订阅方在 UI 层；依赖方向是 <c>Core → Data → Abstractions</c>，只有 Abstractions 能被两边同时引用。
/// </para>
/// <para>
/// 为什么用<b>弱引用</b>订阅：组件窗口会被反复创建/销毁，静态事件若强持有处理器，
/// 关闭的组件会连同整个可视树一起泄漏（这是静态事件的经典坑）。弱引用下订阅者被回收后自动失效。
/// 代价是订阅方必须自己<b>强持有</b>那个委托（见 <c>DataChangeReloader</c>），否则委托会被立刻回收。
/// </para>
/// </summary>
public static class DataChangeHub
{
    private static readonly object Gate = new();
    private static readonly List<WeakReference<Action>> Subscribers = new();

    /// <summary>
    /// 订阅数据变更。返回值 Dispose 即退订（不调用也不会泄漏：弱引用会自然失效）。
    /// </summary>
    public static IDisposable Subscribe(Action handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        WeakReference<Action> weak;
        lock (Gate)
        {
            Prune();
            weak = new WeakReference<Action>(handler);
            Subscribers.Add(weak);
        }
        return new Subscription(weak);
    }

    /// <summary>
    /// 广播一次「数据变了」。
    /// 可以在任意线程调用（仓储的异步方法常跑在线程池上）；处理器自己负责切回 UI 线程。
    /// 单个处理器抛异常不影响其它订阅者。
    /// </summary>
    public static void Notify()
    {
        List<Action> snapshot;
        lock (Gate)
        {
            Prune();
            snapshot = new List<Action>(Subscribers.Count);
            foreach (var weak in Subscribers)
                if (weak.TryGetTarget(out var handler)) snapshot.Add(handler);
        }

        foreach (var handler in snapshot)
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                // 一个组件刷新失败不该连累其它组件，也不该冒到写入方（会把一次保存变成"保存失败"）
                StarLog.Error("处理数据变更通知失败", ex);
            }
        }
    }

    private static void Prune() => Subscribers.RemoveAll(w => !w.TryGetTarget(out _));

    private sealed class Subscription : IDisposable
    {
        private WeakReference<Action>? _weak;
        public Subscription(WeakReference<Action> weak) => _weak = weak;

        public void Dispose()
        {
            var weak = _weak;
            _weak = null;
            if (weak is null) return;
            lock (Gate)
            {
                Subscribers.Remove(weak);
            }
        }
    }
}
