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
/// <para>
/// <b>事件语义（P-44）</b>：通知分两族——<see cref="DataChangeKind.Items"/> 是「条目数据变了」
/// （增删改条目、标签、置顶、快照、活动、同步导入），<see cref="DataChangeKind.Settings"/> 是
/// 「应用设置变了」（今天唯一的一处：热榜速览开关）。发通知的一侧必须挑对族；
/// 订阅的一侧用 <c>interests</c> 声明自己关心哪几族，不关心的族 hub 直接不递——
/// 「设置变了」不再触发关注条目的订阅者整轮重载，反之亦然。
/// </para>
/// </summary>
public static class DataChangeHub
{
    private static readonly object Gate = new();
    private static readonly List<Subscriber> Subscribers = new();

    /// <summary>
    /// 订阅数据变更。返回值 Dispose 即退订（不调用也不会泄漏：弱引用会自然失效）。
    /// <para>
    /// <paramref name="interests"/> 是订阅侧声明的「我关心哪几族变更」（默认全收）：
    /// hub 在广播时按它过滤，声明之外的族不会递到这个处理器。
    /// </para>
    /// </summary>
    public static IDisposable Subscribe(Action<DataChangeKind> handler, DataChangeKind interests = DataChangeKind.All)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        Subscriber sub;
        lock (Gate)
        {
            Prune();
            sub = new Subscriber(new WeakReference<Action<DataChangeKind>>(handler), interests);
            Subscribers.Add(sub);
        }
        return new Subscription(sub);
    }

    /// <summary>
    /// 广播一次「<paramref name="kind"/> 这一族的数据变了」——族是必填的：
    /// 不带族的「变了」会把设置开关与条目写入混成一句话，订阅侧无从过滤（P-44）。
    /// 可以在任意线程调用（仓储的异步方法常跑在线程池上）；处理器自己负责切回 UI 线程。
    /// 单个处理器抛异常不影响其它订阅者。
    /// </summary>
    public static void Notify(DataChangeKind kind)
    {
        List<Action<DataChangeKind>> snapshot;
        lock (Gate)
        {
            Prune();
            snapshot = new List<Action<DataChangeKind>>(Subscribers.Count);
            foreach (var sub in Subscribers)
                if ((sub.Interests & kind) != 0 && sub.Weak.TryGetTarget(out var handler))
                    snapshot.Add(handler);
        }

        foreach (var handler in snapshot)
        {
            try
            {
                handler(kind);
            }
            catch (Exception ex)
            {
                // 一个组件刷新失败不该连累其它组件，也不该冒到写入方（会把一次保存变成"保存失败"）
                StarLog.Error("处理数据变更通知失败", ex);
            }
        }
    }

    private static void Prune() => Subscribers.RemoveAll(s => !s.Weak.TryGetTarget(out _));

    /// <summary>一条订阅：弱持有的处理器＋它声明关心的变更族。</summary>
    private sealed class Subscriber
    {
        public readonly WeakReference<Action<DataChangeKind>> Weak;
        public readonly DataChangeKind Interests;

        public Subscriber(WeakReference<Action<DataChangeKind>> weak, DataChangeKind interests)
        {
            Weak = weak;
            Interests = interests;
        }
    }

    private sealed class Subscription : IDisposable
    {
        private Subscriber? _sub;
        public Subscription(Subscriber sub) => _sub = sub;

        public void Dispose()
        {
            var sub = _sub;
            _sub = null;
            if (sub is null) return;
            lock (Gate)
            {
                Subscribers.Remove(sub);
            }
        }
    }
}

/// <summary>
/// 数据变更的<b>族</b>（P-44）：一次 <c>Notify</c> 必须说清自己改的是哪一族。
/// <para>
/// 为什么分成旗而不是两颗平行事件：订阅者可以一次声明关心多族（ GlanceWidget 对
/// <see cref="All"/> 全收），发通知的一侧也不必记「这颗变更要通知几颗事件」——
/// 一颗 Notify 一颗旗，订阅侧用 interests 与它按位求交。
/// </para>
/// </summary>
[Flags]
public enum DataChangeKind
{
    /// <summary>没有变更（只作零值；不会有人对它发通知）。</summary>
    None = 0,

    /// <summary>条目数据变了：增删改条目、标签、置顶、快照、活动、同步导入。</summary>
    Items = 1,

    /// <summary>应用设置变了（今天唯一一处：热榜速览开关）。</summary>
    Settings = 2,

    /// <summary>两族都算：默认订阅口径（不声明 interests 的订阅者全收）。</summary>
    All = Items | Settings,
}
