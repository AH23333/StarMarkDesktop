#nullable enable
using Microsoft.UI.Dispatching;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// 桌面组件管理器（对标 DeskBox WidgetManager 的多实例模型）：
/// 同一类型可存在多个独立 <see cref="WidgetWindow"/>，每个实例由唯一 <see cref="WidgetInstanceConfig.Id"/> 标识，
/// 实例保活（隐藏而非关闭）。启用/位置/置顶/各实例内容（待办/随记/快捷入口）全部按实例隔离持久化到 widgets.json。
/// 所有公开方法都会封送到 UI 线程（托盘回调与设置页都可能调用）。
/// </summary>
public sealed partial class WidgetManager
{
    private readonly WidgetStorage _storage;
    private readonly IItemRepository? _repo;
    private readonly WidgetSnapshotService? _snapshotService;
    private readonly Dictionary<string, WidgetWindow> _windows = new();
    private DispatcherQueue? _ui;

    /// <summary>
    /// "隐藏满宽限期后收窗口"的<b>一次性</b>定时器（批次 ST）。
    /// <para>整群共用一个，不是每颗一个：每颗一个的话，12 颗组件就是 12 张常转的表，
    /// 而那件事一次巡查就能全做完。</para>
    /// </summary>
    private DispatcherQueueTimer? _reclaimTimer;

    /// <summary>某实例的快捷入口数据变化（新增/删除/外部发送），携带实例 ID 供快捷启动格增量刷新。</summary>
    public event Action<string>? LinksChanged;

    /// <summary>快捷搜索组件请求唤起主窗口并执行搜索。</summary>
    public event Action<string>? GlobalSearchRequested;

    /// <summary>
    /// 实例集合发生变化（新建 / 移除 / 按类型启停）。
    /// 设置页「组件」标签页据此立即重绘实例行，实现增删后实时反映。
    /// </summary>
    public event Action? InstancesChanged;

    /// <summary>布局方案发生变化（保存 / 删除），设置页据此刷新布局列表与快捷键行。</summary>
    public event Action? LayoutsChanged;

    /// <summary>快照点集合发生变化（保存 / 应用产生的自动备份 / 删除），「快照」页与组件右键菜单据此刷新。</summary>
    public event Action? SnapshotsChanged;

    public WidgetManager(WidgetStorage storage, IItemRepository? repo)
    {
        _storage = storage;
        _repo = repo;
        // 快照的数据读写只在仓库可用时才成立（无仓库 → 只存布局/配置，本地条目数据为空）。
        _snapshotService = repo is not null ? new WidgetSnapshotService(repo) : null;
    }

    private DispatcherQueue Ui()
        => _ui ??= DispatcherQueue.GetForCurrentThread()
        ?? throw new InvalidOperationException("WidgetManager 尚未在 UI 线程初始化");

    private Task OnUiAsync(Action action)
    {
        var ui = Ui();
        if (ui.HasThreadAccess) { action(); return Task.CompletedTask; }
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (ui.TryEnqueue(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
            return tcs.Task;
        return Task.CompletedTask;
    }

    private Task<T> OnUiAsync<T>(Func<T> func)
    {
        var ui = Ui();
        if (ui.HasThreadAccess) return Task.FromResult(func());
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        // 分发器已停（窗口正在关闭）时 TryEnqueue 返回 false：必须显式失败，否则 await 一个永不完成的
        // Task → 应用静默挂死且无任何日志（区别于 Action 重载可安全空转）。
        if (!ui.TryEnqueue(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            }))
            tcs.TrySetException(new InvalidOperationException("UI 分发器不可用，操作已取消"));
        return tcs.Task;
    }

    // ───────────────────────── 实例集合 ─────────────────────────

    /// <summary>全部组件实例（设置页 / 托盘菜单遍历用）。</summary>
    public IReadOnlyList<WidgetInstanceConfig> Instances => _storage.Load().Instances;

    /// <summary>
    /// 这颗实例<b>当前那扇宿主窗口</b>；没在册（未显示 / 隐藏后被回收）时为 null。
    /// <para>组件想"在自己那块屏幕上弹输入框"时问这一句。不去爬可视树：窗口的 <c>Content</c> 之上
    /// 并没有一条指向 <see cref="WidgetWindow"/> 的父子链，爬是爬不到的；而"组件与宿主各认一份父窗"
    /// 迟早与册内那份对不上（同一件判据两处各写一份，记忆 ⑧）。</para>
    /// </summary>
    public WidgetWindow? WindowOf(string instanceId) => _windows.GetValueOrDefault(instanceId);

    // ───────────────────────── 吸附支持 ─────────────────────────

    /// <summary>其他可见组件窗口的当前矩形（物理像素），供拖动吸附。</summary>
    public IReadOnlyList<RectInt32> GetOtherBounds(string selfId)
    {
        var list = new List<RectInt32>();
        foreach (var (id, window) in _windows)
        {
            if (id == selfId || !window.IsVisible) continue;
            try
            {
                var r = WindowInteropGetRect(window);
                if (r.Width > 0) list.Add(r);
            }
            catch { }
        }
        return list;
    }

    private static RectInt32 WindowInteropGetRect(WidgetWindow window)
        => Helpers.WindowInterop.GetWindowRect(window);
}
