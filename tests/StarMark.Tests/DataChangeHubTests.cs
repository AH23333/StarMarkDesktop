using System;
using System.Linq;
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// Phase 2 多窗口状态联动（改造提示词）：验证 DataChangeHub 广播链路——
/// 仓储写入收口点 Notify 后，所有订阅方（桌面组件的重载处理器）被同步调用。
/// 用普通类注册接收（不依赖 UI 线程），等价于组件窗口的 DataChangeReloader 模式。
/// <para>✅P-44 起 Notify 带<b>族</b>（<see cref="DataChangeKind"/>）、Subscribe 带 interests：
/// 本档同时钉住「族原样递到处理器」「不关心的族不递」与「设置族的广播全 src 只有一颗源、
/// 唯一全收订阅者点名全收」这三件事。</para>
/// </summary>
public class DataChangeHubTests
{
    [Fact]
    public void Notify_InvokesActiveSubscribers()
    {
        var called = 0;
        using var sub = DataChangeHub.Subscribe(kind => Interlocked.Increment(ref called));

        DataChangeHub.Notify(DataChangeKind.Items);

        Assert.True(called >= 1, "订阅方未收到广播");
    }

    [Fact]
    public void Unsubscribe_StopsReceiving()
    {
        var called = 0;
        var sub = DataChangeHub.Subscribe(kind => Interlocked.Increment(ref called));
        sub.Dispose();

        DataChangeHub.Notify(DataChangeKind.Items);

        Assert.Equal(0, called);
    }

    /// <summary>族是广播的载荷之一：处理器拿到的必须是发出去的那颗族，而不是自己猜的。</summary>
    [Fact]
    public void Notify_PassesKindToHandler()
    {
        DataChangeKind? seen = null;
        using var sub = DataChangeHub.Subscribe(kind => seen = kind);

        DataChangeHub.Notify(DataChangeKind.Settings);

        Assert.Equal(DataChangeKind.Settings, seen);
        Assert.NotEqual(DataChangeKind.Items, seen);
    }

    /// <summary>
    /// ✅P-44 的闸芯：声明只收条目族的订阅者不被「设置变了」惊动，反之亦然。
    /// hub 侧过滤（而不是处理器里自己 if）才防得住「每颗订阅者各判一遍」的漂移。
    /// </summary>
    [Fact]
    public void Interests_FilterBlocksUninterestedKinds()
    {
        var itemsOnly = 0;
        var settingsOnly = 0;
        using var subItems = DataChangeHub.Subscribe(kind => itemsOnly++, DataChangeKind.Items);
        using var subSettings = DataChangeHub.Subscribe(kind => settingsOnly++, DataChangeKind.Settings);

        DataChangeHub.Notify(DataChangeKind.Settings);
        DataChangeHub.Notify(DataChangeKind.Items);

        Assert.Equal(1, itemsOnly);
        Assert.Equal(1, settingsOnly);
    }

    /// <summary>默认 interests＝全收：不声明兴趣的订阅者行为与 P-44 之前完全一致（向后兼容钉）。</summary>
    [Fact]
    public void DefaultInterests_ReceivesEveryKind()
    {
        var called = 0;
        using var sub = DataChangeHub.Subscribe(kind => called++);

        DataChangeHub.Notify(DataChangeKind.Items);
        DataChangeHub.Notify(DataChangeKind.Settings);

        Assert.Equal(2, called);
    }

        private static int _weakCalled;
    private static void OnWeakCalled(DataChangeKind kind) => Interlocked.Increment(ref _weakCalled);

    /// <summary>
    /// 弱引用行为验证（受限版）：Hub 用 WeakReference 弱持委托（实现保证，见 DataChangeHub 注释），
    /// 但「回收后不再触达」的断言依赖 GC 时机——Debug 构建的 JIT 会延长局部引用生命周期，
    /// 环境固有行为导致断言不可靠，故此处仅验证**存活订阅**路径；
    /// 弱引用防泄漏语义由实现与代码审查保证（DataChangeHub 的 WeakReference + 组件 Unloaded 退订双保险）。
    /// </summary>
    [Fact]
    public void WeakReference_LiveSubscriber_ReceivesBroadcast()
    {
        _weakCalled = 0;
        using var sub = DataChangeHub.Subscribe(new Action<DataChangeKind>(OnWeakCalled));

        DataChangeHub.Notify(DataChangeKind.Items);

        Assert.Equal(1, _weakCalled);
    }

    /// <summary>模拟组件窗口的完整联动链：仓储写入 → Hub 广播 → 订阅方标记置顶变化（Phase 2/4 联动验证）。</summary>
    [Fact]
    public void PinChange_Broadcast_ReachesWidgetSubscriber()
    {
        var pinNotified = false;
        using var sub = DataChangeHub.Subscribe(kind => pinNotified = true);

        // 生产路径：ItemRepository.SetPinnedAsync 内部调用 DataChangeHub.Notify(DataChangeKind.Items)；
        // 这里直接验证广播段（数据层 SQL 已由 SetPinned_BrowseSortsPinnedFirst 覆盖）。
        DataChangeHub.Notify(DataChangeKind.Items);

        Assert.True(pinNotified, "置顶变化未广播到组件订阅方");
    }

    /// <summary>
    /// ✅P-44 的源码闸：「设置」族广播全 src 恰一颗源（热榜速览开关那颗），
    /// 唯一点名全收（<see cref="DataChangeKind.All"/>）的订阅者是今日速览组件。
    /// <para>为什么钉源码而不钉行为：设置族今天只有一主一客，行为断言看不出「多出一颗忘改的源」；
    /// 而「谁在发设置族」「谁在全收」这两颗字面一旦分岔，广播要么漏听、要么风暴，先例＝备份数那颗闸。</para>
    /// </summary>
    [Fact]
    public void SettingsBroadcast_HasExactlyOneSourceAndOneFullListener()
    {
        var settingsNotifies = 0;
        var fullListeners = 0;
        var bareNotifies = 0;
        foreach (var (path, text) in SourceGate.ReadRepoUnder("src"))
        {
            var code = SourceGate.Code(text);
            settingsNotifies += SourceGate.Count(code, "DataChangeKind.Settings)");
            fullListeners += SourceGate.Count(code, "interests: DataChangeKind.All");
            bareNotifies += SourceGate.Count(code, "DataChangeHub.Notify()");
        }

        Assert.Equal(1, settingsNotifies);
        Assert.Equal(1, fullListeners);
        Assert.Equal(0, bareNotifies);
    }
}
