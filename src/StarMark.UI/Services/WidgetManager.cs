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
public sealed class WidgetManager
{
    private readonly WidgetStorage _storage;
    private readonly IItemRepository? _repo;
    private readonly WidgetSnapshotService? _snapshotService;
    private readonly Dictionary<string, WidgetWindow> _windows = new();
    private DispatcherQueue? _ui;

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

    /// <summary>在 UI 线程上记录调度器（MainWindow 构造时调用）。</summary>
    public void Initialize(DispatcherQueue ui) => _ui = ui;

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

    /// <summary>某类型是否至少有一个实例（托盘 / 设置开关的"已启用"语义）。</summary>
    public bool IsEnabled(WidgetKind kind) => _storage.Load().Instances.Any(i => i.Kind == kind);

    /// <summary>启用/停用某类型：启用→无实例则新建并显示、有实例则全部显示；停用→移除该类型全部实例。</summary>
    public Task SetEnabledAsync(WidgetKind kind, bool enabled)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var existing = data.Instances.Where(i => i.Kind == kind).ToList();
            if (enabled)
            {
                if (existing.Count == 0)
                {
                    var cfg = CreateInstanceConfig(data, kind);
                    data.Instances.Add(cfg);
                    _storage.Save(data);
                    InstancesChanged?.Invoke();
                    ShowInternal(cfg.Id);
                }
                else
                {
                    foreach (var inst in existing) ShowInternal(inst.Id);
                }
            }
            else if (existing.Count > 0)
            {
                var ids = existing.Select(i => i.Id).ToList();
                data.Instances.RemoveAll(i => i.Kind == kind);
                _storage.Save(data);
                InstancesChanged?.Invoke();
                CloseAll(ids, persist: false);
            }
        });

    /// <summary>新增一个指定类型的组件实例（可重复添加同类型）。</summary>
    public Task AddInstanceAsync(WidgetKind kind)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var cfg = CreateInstanceConfig(data, kind);
            data.Instances.Add(cfg);
            _storage.Save(data);
            InstancesChanged?.Invoke();   // 设置页立即出现新实例行
            ShowInternal(cfg.Id);
        });

    /// <summary>保存某实例的外观覆盖（材质/颜色/边框/圆角/文本缩放）。appearance 为 null 表示清除覆盖、回退全局。</summary>
    public Task SaveInstanceAppearanceAsync(string id, WidgetAppearanceOverride? appearance)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == id);
            if (inst is null) return;
            inst.Appearance = appearance;
            _storage.Save(data);
        });

    /// <summary>
    /// 重命名组件实例（右键菜单「重命名…」）。title 传 null / 空白表示恢复组件类型的默认标题。
    /// 持久化方式与其它每实例字段一致：重新载入 widgets.json → 改实例 → 落盘。
    /// </summary>
    public Task SaveInstanceTitleAsync(string id, string? title)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == id);
            if (inst is null) return;
            inst.Title = string.IsNullOrWhiteSpace(title) ? null : title!.Trim();
            _storage.Save(data);
        });

    /// <summary>移除指定实例（标题栏 ✕ / 设置页移除 / 菜单"移除本组件"）。</summary>
    public Task RemoveInstanceAsync(string id)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == id);
            if (inst is not null)
            {
                data.Instances.Remove(inst);
                _storage.Save(data);
                InstancesChanged?.Invoke();   // 设置页立即移除该实例行
            }
            CloseInternal(id, persist: false);
        });

    /// <summary>移除实例的别名（与旧 API 同名，便于调用处过渡）。</summary>
    public Task RemoveAsync(string id) => RemoveInstanceAsync(id);

    /// <summary>显示指定实例（临时隐藏后恢复；实例不存在则忽略）。</summary>
    public Task ShowAsync(string id) => OnUiAsync(() => ShowInternal(id));

    /// <summary>临时隐藏（实例保留）。</summary>
    public Task HideTemporaryAsync(string id) => OnUiAsync(() =>
    {
        if (_windows.TryGetValue(id, out var w)) HideTemporaryAll(new[] { w });
    });

    /// <summary>新建实例配置：在同类已有实例基础上做级联偏移，避免叠在一起。</summary>
    private static WidgetInstanceConfig CreateInstanceConfig(WidgetStoreData data, WidgetKind kind)
    {
        var kindIndex = (int)kind;
        var sameKind = data.Instances.Count(i => i.Kind == kind);
        return new WidgetInstanceConfig
        {
            Kind = kind,
            // 新建实例默认采用描述符推荐的外壳模式（目前均为 Standard；未来个别组件可声明 Compact/Hidden）
            ChromeMode = WidgetRegistry.Default.Get(kind).DefaultChromeMode,
            X = 120 + kindIndex * 28 + sameKind * 26,
            Y = 90 + kindIndex * 28 + sameKind * 26,
            Width = WidgetStorage.DefaultWidth(kind),
            Height = WidgetStorage.DefaultHeight(kind),
        };
    }

    public Task ShowAllAsync() => OnUiAsync(() =>
    {
        foreach (var inst in _storage.Load().Instances) ShowInternal(inst.Id);
    });

    public Task HideAllAsync() => OnUiAsync(() =>
    {
        HideTemporaryAll(_windows.Values.ToList());
    });

    /// <summary>显示某类型的全部实例（快捷键「显示组件」用）。</summary>
    public Task ShowKindAsync(WidgetKind kind) => OnUiAsync(() =>
    {
        foreach (var inst in _storage.Load().Instances.Where(i => i.Kind == kind)) ShowInternal(inst.Id);
    });

    /// <summary>隐藏某类型的全部实例（快捷键「隐藏组件」用）。</summary>
    public Task HideKindAsync(WidgetKind kind) => OnUiAsync(() =>
    {
        HideTemporaryAll(_windows.Values.Where(w => w.Kind == kind).ToList());
    });

    /// <summary>
    /// 切换某类型组件的显示/隐藏（同一个快捷键既开又关）：
    /// 该类型有可见窗口 → 全部隐藏；否则 → 显示全部实例（没有实例则新建一个）。
    /// </summary>
    public Task ToggleKindAsync(WidgetKind kind) => OnUiAsync(() =>
    {
        var kindWindows = _windows.Values.Where(w => w.Kind == kind).ToList();
        if (kindWindows.Any(w => w.IsVisible))
        {
            HideTemporaryAll(kindWindows);
            return;
        }

        var instances = _storage.Load().Instances.Where(i => i.Kind == kind).ToList();
        if (instances.Count == 0) { _ = AddInstanceAsync(kind); return; }
        foreach (var inst in instances) ShowInternal(inst.Id);
    });

    /// <summary>切换全部组件显示/隐藏（同一个快捷键既开又关）。</summary>
    public Task ToggleAllInstancesAsync() => OnUiAsync(() =>
    {
        var anyVisible = _windows.Values.Any(w => w.IsVisible);
        if (anyVisible)
        {
            HideTemporaryAll(_windows.Values.ToList());
        }
        else
        {
            // 显示分支：恢复到「最后一次切换的布局」而非暴露所有实例，
            // 否则切换布局后被该布局排除的组件会被一起显示，看起来像变回了另一套布局。
            if (_storage.Load().Instances.Count == 0) { _ = AddInstanceAsync(WidgetKind.QuickLaunch); return; }
            ShowByDefaultLayoutOrAll();
        }
    });

    /// <summary>
    /// 切换"所有组件是否置顶于其它窗口"：一个键在置顶/不置顶之间来回。
    /// 方向按"当前有没有任何一个处于置顶"决定（只要还有一个没置顶，这一键就是全部置顶），
    /// 这样用户不必关心个别实例历史上被单独置顶过。落盘一次写完（见 <see cref="WidgetWindow.ApplyGlobalTopmost"/>）。
    /// </summary>
    public Task ToggleAllTopmostAsync() => OnUiAsync(() =>
    {
        if (_windows.Count == 0) return;
        var wantTopmost = !_windows.Values.Any(w => w.IsTopmost);
        foreach (var w in _windows.Values.ToList()) w.ApplyGlobalTopmost(wantTopmost);

        var data = _storage.Load();
        var changed = false;
        foreach (var inst in data.Instances)
            if (inst.Topmost != wantTopmost) { inst.Topmost = wantTopmost; changed = true; }
        if (changed) _storage.Save(data);
    });

    /// <summary>
    /// 除指定实例外的**可见**组件窗口。供 Ctrl+拖动协同移动（对齐 DeskBox CoordinatedMove）招募参与者。
    /// 只在 UI 线程调用。
    /// </summary>
    public IReadOnlyList<WidgetWindow> VisibleWindowsExcept(string excludeInstanceId) =>
        _windows.Values
            .Where(w => w.InstanceId != excludeInstanceId && w.IsVisible && !w.IsDragBusy)
            .ToList();

    /// <summary>托盘"桌面组件"总开关：有可见组件→全部隐藏；否则显示全部已启用实例。</summary>
    public Task ToggleAllAsync() => OnUiAsync(() =>
    {
        var anyVisible = _windows.Values.Any(w => w.IsVisible);
        if (anyVisible)
        {
            HideTemporaryAll(_windows.Values.ToList());
        }
        else
        {
            if (_storage.Load().Instances.Count == 0)
            {
                // 一个组件都没有时，总开关默认给出快捷启动格
                _ = SetEnabledAsync(WidgetKind.QuickLaunch, true);
            }
            else
            {
                // 显示分支：恢复到「最后一次切换的布局」，而非暴露所有实例
                ShowByDefaultLayoutOrAll();
            }
        }
    });

    /// <summary>
    /// 「显示」语义（供「切换所有组件」的「开」分支复用）：
    /// 若存在默认布局（即用户最后一次切换到的布局），则恢复到该布局——
    /// 只显示布局内的实例子集并还原位置、把布局外的实例重新隐藏；
    /// 否则（从未切换过布局）逐个显示全部实例。
    /// 这样切换布局后再一键隐藏、一键显示，回到的是「最后一次切换的布局」，
    /// 而不是把所有被布局排除的组件一起暴露出来、看起来像变回了另一套布局。
    /// </summary>
    private void ShowByDefaultLayoutOrAll()
    {
        var data = _storage.Load();
        var layout = !string.IsNullOrEmpty(data.DefaultLayoutId)
            ? _storage.FindLayout(data.DefaultLayoutId!)
            : null;

        if (layout is not null)
        {
            ApplyLayoutCore(data, layout);
            _storage.Save(data);
        }
        else
        {
            foreach (var inst in data.Instances) ShowInternal(inst.Id);
        }
    }

    /// <summary>启动时恢复组件：若存在默认布局则自动套用它（恢复「最后一次选择的布局」），否则逐个显示全部实例。</summary>
    public Task RestoreOnStartupAsync() => OnUiAsync(() =>
    {
        var data = _storage.Load();
        var layout = !string.IsNullOrEmpty(data.DefaultLayoutId)
            ? _storage.FindLayout(data.DefaultLayoutId!)
            : null;

        if (layout is not null)
        {
            try
            {
                ApplyLayoutCore(data, layout);
                _storage.Save(data);     // 保证默认布局状态与磁盘一致
            }
            catch (Exception ex)
            {
                StarLog.Error("应用默认布局失败，回退为逐个显示实例", ex);
                foreach (var inst in data.Instances)
                {
                    try { ShowInternal(inst.Id); }
                    catch (Exception iex) { StarLog.Error($"恢复桌面组件失败 ({inst.Kind})", iex); }
                }
            }
        }
        else
        {
            foreach (var inst in data.Instances)
            {
                try { ShowInternal(inst.Id); }
                catch (Exception ex) { StarLog.Error($"恢复桌面组件失败 ({inst.Kind})", ex); }
            }
        }
        // 这一段是启动里最重的一块（建窗口＋套材质），单独计时才有"改完有没有变快"的对照
        StarMark.Abstractions.StartupProfile.Mark($"桌面组件恢复（{data.Instances.Count} 个实例）");
    });

    public Task ShutdownAllAsync() => OnUiAsync(() =>
    {
        CloseAll(_windows.Keys.ToList(), persist: true);
    });

    // ───────────────────────── 布局方案 ─────────────────────────

    /// <summary>全部已保存布局（按名称排序）。</summary>
    public IReadOnlyList<WidgetLayout> GetLayouts() => _storage.GetLayouts();

    /// <summary>
    /// 把「用户当前的所有组件」保存为一套布局：记录每个实例的类型、同类序号、位置尺寸、置顶状态，
    /// 以及该实例的**自定义外观**（材质/颜色/边框/圆角/文本缩放）。
    /// <para>
    /// 快照**全部实例**（含当前隐藏者），而非只快照可见窗口 —— 隐藏组件的位置取其已持久化的
    /// config 值。这样一套布局能完整还原用户"所有组件"的摆位与个性化配置。可见窗口读实时矩形，
    /// 保证存的是屏幕上真正的那一块。
    /// </para>
    /// </summary>
    public Task<WidgetLayout?> SaveCurrentLayoutAsync(string name) => OnUiAsync(() =>
    {
        var data = _storage.Load();
        var entries = new List<WidgetLayoutEntry>();
        var perKind = new Dictionary<WidgetKind, int>();

        foreach (var inst in data.Instances)
        {
            var (x, y, width, height) = ResolveLiveRect(inst);

            perKind.TryGetValue(inst.Kind, out var idx);
            entries.Add(new WidgetLayoutEntry
            {
                Kind = inst.Kind,
                Index = idx,
                X = x, Y = y, Width = width, Height = height,
                Topmost = inst.Topmost,
                Appearance = inst.Appearance,   // 快照每实例自定义配置（null = 未修改，应用时跟随当前主题）
            });
            perKind[inst.Kind] = idx + 1;
        }

        if (entries.Count == 0) return null;

        var layout = new WidgetLayout
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = WidgetLayoutCollection.MakeUniqueName(_storage.GetLayouts(), name),
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Entries = entries,
        };
        _storage.SaveLayout(layout);
        LayoutsChanged?.Invoke();
        return layout;
    });

    /// <summary>删除一套布局方案。</summary>
    public Task<bool> DeleteLayoutAsync(string layoutId) => OnUiAsync(() =>
    {
        var removed = _storage.DeleteLayout(layoutId);
        if (removed) LayoutsChanged?.Invoke();
        return removed;
    });

    /// <summary>
    /// 应用一套布局：把布局里的每条记录套到对应实例（按「类型 + 序号」），缺少的实例当场新建；
    /// 布局之外的实例统一隐藏（实例与其内容仍保留，随时可再次显示）。
    /// 因此同一时刻只会显示一套布局（不会出现两套叠屏）。
    /// 应用成功后把该布局记为「默认布局」（下次启动自动恢复）。
    /// </summary>
    public Task<bool> ApplyLayoutAsync(string layoutId) => OnUiAsync(() =>
    {
        var layout = _storage.FindLayout(layoutId);
        if (layout is null) return false;

        var data = _storage.Load();
        ApplyLayoutCore(data, layout);
        data.DefaultLayoutId = layoutId;     // 记为默认布局（最后一次选择的布局）
        _storage.Save(data);
        return true;
    });

    /// <summary>
    /// 套用布局的核心逻辑（不落盘、不改写 DefaultLayoutId，便于启动恢复复用）：
    /// 写入位置/尺寸/置顶、显示布局内实例、隐藏布局外实例。
    /// </summary>
    private void ApplyLayoutCore(WidgetStoreData data, WidgetLayout layout)
        => ApplyGeometryCore(data, layout.Entries);

    /// <summary>
    /// 「几何 + 外观」套用引擎：按「类型 + 序号」把每条几何落位到实例（缺则新建），
    /// 显示入列实例、隐藏其余。布局模板与数据快照共用这一份匹配逻辑，避免两处实现漂移。
    /// 返回条目 → 实例的映射，供快照据此把数据写回正确的实例。
    /// </summary>
    private List<(T entry, WidgetInstanceConfig inst)> ApplyGeometryCore<T>(WidgetStoreData data, IReadOnlyList<T> entries)
        where T : IWidgetGeometryEntry
    {
        var mapping = new List<(T, WidgetInstanceConfig)>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var sameKind = data.Instances.Where(i => i.Kind == entry.Kind).ToList();

            WidgetInstanceConfig inst;
            var created = false;
            if (entry.Index >= 0 && entry.Index < sameKind.Count)
            {
                inst = sameKind[entry.Index];
            }
            else
            {
                var spare = sameKind.FirstOrDefault(i => !used.Contains(i.Id));
                if (spare is not null)
                {
                    inst = spare;
                }
                else
                {
                    inst = CreateInstanceConfig(data, entry.Kind);
                    created = true;
                }
            }

            inst.X = entry.X;
            inst.Y = entry.Y;
            inst.Width = entry.Width;
            inst.Height = entry.Height;
            inst.Topmost = entry.Topmost;
            // 还原"用户对组件的自定义配置"：把该实例的外观覆盖重置为快照值。
            // 快照为 null（保存时用户未单独改过该组件）→ 清空覆盖，令其跟随当前设置的全局主题；
            // 快照非 null（保存时用户已改过）→ 还原成它自己那套自定义外观，不受当前全局主题影响。
            inst.Appearance = entry.Appearance;
            used.Add(inst.Id);
            if (created)
            {
                data.Instances.Add(inst);
                _storage.Save(data);   // 新建实例先落盘，ShowInternal 才能从磁盘读到它并带上新外观
            }

            ShowInternal(inst.Id);
            // 窗口缓存的 config 是上一次 Load 的对象（新建者甚至是保存前的旧磁盘数据），
            // 必须显式下发新位置 + 新外观并回写，否则 ApplyLayoutCore 里改的 inst 不会落到窗口。
            if (_windows.TryGetValue(inst.Id, out var w))
            {
                w.ApplyBounds(entry.X, entry.Y, entry.Width, entry.Height, entry.Topmost);
                w.ApplyAppearance(entry.Appearance);
            }

            mapping.Add((entry, inst));
        }

        // 布局之外的实例：隐藏但保留（内容不丢）
        HideTemporaryAll(_windows.Values.Where(w => !used.Contains(w.InstanceId)).ToList());

        InstancesChanged?.Invoke();
        return mapping;
    }

    // ───────────────────────── 布局与数据快照（#53） ─────────────────────────
    // 快照 = 布局半（几何 + 外观，走同一套 ApplyGeometryCore）+ 数据半（快捷入口/待办/随记/条目格查询）。
    // 与「纯模板」布局的分工：模板可复用、按 Id 就地覆盖；快照是不可变历史点，应用它 = 回到那一刻（Replace + 自动回滚）。

    /// <summary>全部快照点（新的在前）。</summary>
    public IReadOnlyList<WidgetSnapshot> GetSnapshots() => _storage.GetSnapshots();

    /// <summary>
    /// 取某实例当前应被持久化的矩形（必须在 UI 线程调用）。
    /// <para>
    /// 可见「展开态」窗口读实时矩形（屏幕上真正那块）；<b>收起为胶囊的窗口除外</b>——
    /// 此刻 GetWindowRect 返回的是胶囊停靠位，直接写入会把胶囊尺寸错存成展开尺寸，
    /// 下次展开/还原就跳到屏幕边缘。胶囊态与隐藏窗口一律回退实例已持久化的展开态 config 值
    /// （<c>PersistBounds</c> 在收起时保留 inst.X/Y/Width/Height 为展开矩形）。
    /// </para>
    /// </summary>
    private (double X, double Y, double Width, double Height) ResolveLiveRect(WidgetInstanceConfig inst)
    {
        if (_windows.TryGetValue(inst.Id, out var w) && w.IsVisible && !w.IsCollapsed)
        {
            try
            {
                var r = WindowInterop.GetWindowRect(w);
                if (r.Width > 0 && r.Height > 0) return (r.X, r.Y, r.Width, r.Height);
            }
            catch { /* 读实时矩形失败 → 回退持久化值 */ }
        }
        return (inst.X, inst.Y, inst.Width, inst.Height);
    }

    /// <summary>
    /// 在 UI 线程读取「当前所有实例」的实时矩形 + 配置，产出一张尚未填本地条目数据的快照，
    /// 以及各条目对应的实例 ID（随后在 UI 线程外用仓库补 LocalItems）。镜像 <see cref="SaveCurrentLayoutAsync"/> 的取位逻辑。
    /// </summary>
    private (WidgetSnapshot snapshot, List<string> instanceIds) BuildSnapshotShell(string name)
    {
        var data = _storage.Load();
        var entries = new List<WidgetSnapshotEntry>();
        var instanceIds = new List<string>();
        var perKind = new Dictionary<WidgetKind, int>();

        foreach (var inst in data.Instances)
        {
            var (x, y, width, height) = ResolveLiveRect(inst);

            perKind.TryGetValue(inst.Kind, out var idx);
            entries.Add(new WidgetSnapshotEntry
            {
                Kind = inst.Kind,
                Index = idx,
                X = x, Y = y, Width = width, Height = height,
                Topmost = inst.Topmost,
                Title = inst.Title,
                ChromeMode = inst.ChromeMode,
                PrivacyMode = inst.PrivacyMode,
                Appearance = inst.Appearance,
                Links = inst.Links.Select(l => new LinkItem { Id = l.Id, Title = l.Title, Uri = l.Uri, CreatedAt = l.CreatedAt }).ToList(),
                GridTag = inst.GridTag,
                CalcHistory = inst.CalcHistory?.Select(h => new CalcHistoryItem { Expression = h.Expression, Answer = h.Answer }).ToList(),
                WorldClockZones = inst.WorldClockZones?.Select(c => new WorldClockCity(c.ZoneId, c.Name)).ToList(),
                Countdowns = inst.Countdowns?.Select(c => c.Clone()).ToList(),
                Focus = inst.Focus is null ? null : new FocusTimerConfig { FocusMinutes = inst.Focus.FocusMinutes, RestMinutes = inst.Focus.RestMinutes },
                MonitorMetrics = inst.MonitorMetrics,
            });
            instanceIds.Add(inst.Id);
            perKind[inst.Kind] = idx + 1;
        }

        var snapshot = new WidgetSnapshot
        {
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Entries = entries,
        };
        return (snapshot, instanceIds);
    }

    /// <summary>
    /// 把「当前所有组件的布局 + 组件数据」存为一个不可变快照点。
    /// 无实例时不落空快照，返回 null 让调用方给提示。
    /// </summary>
    public async Task<WidgetSnapshot?> CaptureSnapshotAsync(string name)
    {
        var (snapshot, instanceIds) = await OnUiAsync(() => BuildSnapshotShell(name));
        if (snapshot.Entries.Count == 0) return null;

        if (_snapshotService is not null)
        {
            // 只有"名下确实有本地条目"的实例才值得各开一次库去读。空集由这一次查询统一确认——
            // 逐台去问"你是不是空的"，代价全花在空集上（十几个实例里通常只两三台有内容）。
            var owners = await _snapshotService.GetInstancesWithLocalItemsAsync(CancellationToken.None);
            for (var i = 0; i < snapshot.Entries.Count; i++)
            {
                // 任一条目捕获失败即整体抛出：宁可让用户看到"保存失败"，也不能落一张静默缺数据的快照，
                // 否则用户以为已备份、日后据此还原才发现丢了待办/随记，损失不可逆。
                snapshot.Entries[i].LocalItems = owners.Contains(instanceIds[i])
                    ? await _snapshotService.CaptureLocalItemsAsync(instanceIds[i], CancellationToken.None)
                    : new List<SnapshotLocalItem>();
            }
        }

        // AppendSnapshot 内部走 Load→改→Save；与窗口拖动时的 PersistBounds 同写一份磁盘，
        // 必须在 UI 线程串行执行，否则并发改写会丢失其中一方的更新。
        var saved = await OnUiAsync(() => _storage.AppendSnapshot(snapshot));
        SnapshotsChanged?.Invoke();
        return saved;
    }

    /// <summary>
    /// 最近一次 <see cref="ApplySnapshotAsync"/> 未能应用的原因（成功或未尝试＝null，失败时随该次调用刷新）。
    /// <para>快照页原先无论为何失败都只会说"请重试"，而原因埋在日志里：磁盘满、快照被别处删掉、
    /// 组件数据读不出来——用户看到的都是同一句话，也就无从判断重试有没有意义（P-54）。</para>
    /// </summary>
    public string? LastApplyError { get; private set; }

    /// <summary>
    /// 应用快照 = 回到那一刻（Replace）：先自动留一个「应用前」回滚点，再落位布局 + 写回配置数据 + 还原本地条目。
    /// 布局外的实例被隐藏（内容保留）；快照里各组件的快捷入口/待办/随记/条目格查询按「类型+序号」匹配回对应实例。
    /// </summary>
    public async Task<bool> ApplySnapshotAsync(string snapshotId)
    {
        LastApplyError = null;
        var snapshot = _storage.FindSnapshot(snapshotId);
        if (snapshot is null)
        {
            LastApplyError = "该快照已不存在（可能在别处被删除）";
            return false;
        }

        // 1) 自动回滚点：应用前把当前状态先存成一个快照，结果不满意可「应用」它退回这一刻。
        //    快照是 Replace 语义（会覆盖当前待办/随记/摆位）——若这个回滚点没存成，一旦应用出错就无从退回，
        //    属于破坏性且不可逆，因此捕获失败必须中止、绝不继续落位。
        if (!await CaptureSnapshotInternalAsync($"自动备份（应用前） · {DateTime.Now:MM-dd HH:mm}"))
            return false;

        // 2) 布局半 + 配置数据半（UI 线程）：落位几何/外观，并把 Links/Grid/Title/外壳/隐私写回匹配到的实例。
        var dataRestore = await OnUiAsync(() =>
        {
            var data = _storage.Load();
            var mapping = ApplyGeometryCore(data, snapshot.Entries);
            var restore = new List<(string instanceId, IReadOnlyList<SnapshotLocalItem> items)>(mapping.Count);
            foreach (var (entry, inst) in mapping)
            {
                inst.Title = entry.Title;
                inst.ChromeMode = entry.ChromeMode;
                inst.PrivacyMode = entry.PrivacyMode;
                inst.Links = entry.Links.Select(l => new LinkItem { Id = l.Id, Title = l.Title, Uri = l.Uri, CreatedAt = l.CreatedAt }).ToList();
                inst.GridTag = entry.GridTag;
                // null 也要照搬：那一刻这台计算器没有历史，还原后就不该留着之后算出来的条目
                inst.CalcHistory = entry.CalcHistory?.Select(h => new CalcHistoryItem { Expression = h.Expression, Answer = h.Answer }).ToList();
                inst.WorldClockZones = entry.WorldClockZones?.Select(c => new WorldClockCity(c.ZoneId, c.Name)).ToList();
                inst.Countdowns = entry.Countdowns?.Select(c => c.Clone()).ToList();
                inst.Focus = entry.Focus is null ? null : new FocusTimerConfig { FocusMinutes = entry.Focus.FocusMinutes, RestMinutes = entry.Focus.RestMinutes };
                inst.MonitorMetrics = entry.MonitorMetrics;
                restore.Add((inst.Id, entry.LocalItems));
            }
            // 快照还原的是「当时那一整套摆位」，与"最后一次选择的布局"已无对应关系；
            // 若不清默认布局，之后点「显示组件」会按旧 DefaultLayoutId 重新隐藏/落位，把刚还原的状态打回另一套布局。
            data.DefaultLayoutId = null;
            _storage.Save(data);
            return restore;
        });

        // 3) 本地条目数据半（仓库，异步）：整实例先删后插还原待办/随记；DataChangeHub 自动驱动组件重载。
        if (_snapshotService is not null)
        {
            var owners = await _snapshotService.GetInstancesWithLocalItemsAsync(CancellationToken.None);
            foreach (var (instanceId, items) in dataRestore)
            {
                // 快照里这台是空的、库里它也确实是空的 ⇒ 整个跳过（Replace 语义是先删后插，
                // 对空集来说是白开一次库 + 白通知一次组件）。有一侧非空就必须走 Replace。
                if (items.Count == 0 && !owners.Contains(instanceId)) continue;
                try { await _snapshotService.RestoreLocalItemsAsync(instanceId, items, CancellationToken.None); }
                catch (Exception ex) { StarLog.Error($"还原实例本地条目失败 ({instanceId})", ex); }
            }
        }

        // 4) 让每个组件窗口以磁盘上刚写好的配置重建：Title / 外壳 / 隐私 / 几何 / 外观一并生效
        //    （窗口缓存的 _config 是另一份引用，改磁盘不会自动传导，只能重建）。
        await OnUiAsync(() =>
        {
            var keep = new HashSet<string>(dataRestore.Select(r => r.instanceId), StringComparer.Ordinal);
            CloseAll(_windows.Keys.ToList(), persist: false);
            foreach (var id in keep) ShowInternal(id);
        });

        SnapshotsChanged?.Invoke();
        return true;
    }

    /// <summary>删除快照点。</summary>
    public Task<bool> DeleteSnapshotAsync(string snapshotId) => OnUiAsync(() =>
    {
        var removed = _storage.DeleteSnapshot(snapshotId);
        if (removed) SnapshotsChanged?.Invoke();
        return removed;
    });

    /// <summary>
    /// 从快照「提取布局」：把某个快照点的几何 + 外观转成一套**新的、不含数据**的纯模板布局。
    /// 名称由用户输入（空则兜底），与 <see cref="SaveCurrentLayoutAsync"/> 产物同构、可在布局列表里复用。
    /// </summary>
    public Task<WidgetLayout?> ExtractLayoutFromSnapshotAsync(string snapshotId, string layoutName) => OnUiAsync(() =>
    {
        var snapshot = _storage.FindSnapshot(snapshotId);
        if (snapshot is null || snapshot.Entries.Count == 0) return null;

        var layout = new WidgetLayout
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = WidgetLayoutCollection.MakeUniqueName(_storage.GetLayouts(), layoutName),
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            // 只搬几何 + 外观，绝不带入 Links/LocalItems/Grid* —— 提取产物是纯模板（#52 边界在此同样成立）。
            Entries = snapshot.Entries.Select(e => new WidgetLayoutEntry
            {
                Kind = e.Kind, Index = e.Index,
                X = e.X, Y = e.Y, Width = e.Width, Height = e.Height,
                Topmost = e.Topmost, Appearance = e.Appearance,
            }).ToList(),
        };
        _storage.SaveLayout(layout);
        LayoutsChanged?.Invoke();
        return layout;
    });

    /// <summary>捕获内部实现（供 Apply 的自动回滚点复用）：吞掉异常不外抛，但以返回值告知调用方是否存成——回滚点没存成时 Apply 须中止。</summary>
    private async Task<bool> CaptureSnapshotInternalAsync(string name)
    {
        try { return await CaptureSnapshotAsync(name) is not null; }
        catch (Exception ex)
        {
            StarLog.Error("创建应用前回滚快照失败", ex);
            LastApplyError = $"生成「应用前」回滚点失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>设置变更时把半透明材质/不透明度重新应用到所有已打开的组件窗口。</summary>
    public Task RefreshAppearanceAsync() => OnUiAsync(() =>
    {
        foreach (var w in _windows.Values.ToList())
        {
            try { w.RefreshAppearance(); }
            catch (Exception ex) { StarLog.Error("刷新组件外观失败", ex); }
        }
    });

    /// <summary>
    /// 主窗口切换主题后，把所有已打开的组件窗口的主题偏好同步过去并重挂材质。
    /// 组件是独立窗口，主界面 <c>ThemeManager.Apply</c> 不会自动传导，必须显式广播，
    /// 否则组件主题始终停留在各自的旧主题（或 Default=跟随系统），与主界面不同步。
    /// </summary>
    public Task ApplyThemeToAllAsync(ThemePreference pref) => OnUiAsync(() =>
    {
        foreach (var w in _windows.Values.ToList())
        {
            try { w.ApplyTheme(pref); }
            catch (Exception ex) { StarLog.Error("同步组件主题失败", ex); }
        }
    });

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

    // ───────────────────────── 快捷入口 ─────────────────────────

    /// <summary>
    /// 一次拖入 N 个文件的落点：<b>整批只读一次档、只写一次盘、只广播一次</b>。
    /// <para>
    /// 为什么不能沿用逐条 <see cref="AddLinkAsync"/>：每条都要 <c>Load()</c> 再 <c>Save()</c>，
    /// 而 <c>Save()</c> 按设计必须把自己刚写过的快照作废（否则改标题后组件会读到旧内容），
    /// 于是读盘缓存在这条路径上完全帮不上忙——拖 20 个文件就是 20 次整档读 + 20 次整档写 + 20 次重绘。
    /// </para>
    /// </summary>
    /// <returns>真正加进去的条数。实例不存在、或整批都是已有入口时为 0，且<b>不落盘、不广播、不记活动</b>。</returns>
    public async Task<int> AddLinksAsync(string instanceId, IReadOnlyList<(string Title, string Uri)> links)
    {
        if (links is not { Count: > 0 }) return 0;
        var added = await OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return new List<(string, string)>();
            var taken = new HashSet<string>(inst.Links.Select(l => l.Uri), StringComparer.OrdinalIgnoreCase);
            var fresh = new List<(string, string)>();
            foreach (var (title, uri) in links)
            {
                if (string.IsNullOrWhiteSpace(uri)) continue;
                var target = uri.Trim();
                // 去重必须把"同一批里刚收下的那条"也算进来：只对着已落盘的列表比的话，
                // 一次拖进两个同名文件（资源管理器里复制同一文件两次是常事）会两条都塞进去，
                // 界面上就多出一个点开后是同一个地方的重复入口。
                if (!taken.Add(target)) continue;
                var shown = string.IsNullOrWhiteSpace(title) ? target : title.Trim();
                inst.Links.Add(new LinkItem
                {
                    Id = WidgetStorage.NewId(),
                    Title = shown,
                    Uri = target,
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                });
                fresh.Add((shown, target));
            }
            if (fresh.Count == 0) return fresh;             // 没有净变化 ⇒ 一次盘也不写、一次也不广播
            _storage.Save(data);
            return fresh;
        });
        if (added.Count == 0) return 0;

        LinksChanged?.Invoke(instanceId);                  // 整批一次：N 次增量刷新压成 1 次
        // 用户拖入 / 发送一个快捷入口 → 活动流记「新增」（绿）。快捷入口非 items 行，item_key 留空。#51。
        await LogActivitiesAsync(ActivityKind.ItemAdd, added);
        return added.Count;
    }

    /// <summary>新增单个快捷入口（去重）；自动定位到该实例（若该实例窗口已开则增量刷新）。
    /// 走的是 <see cref="AddLinksAsync"/> 那条批处理路，只是长度为 1——判据只留一处。</summary>
    public async Task<bool> AddLinkAsync(string instanceId, string title, string uri)
        => await AddLinksAsync(instanceId, new[] { (title, uri) }) > 0;

    /// <summary>
    /// 保存计算器某实例的历史带（整带替换）。与快捷入口同走实例配置，但<b>不发 LinksChanged 那类广播</b>：
    /// 历史只有产生它的那个窗口在用，广播会让同类型其它实例无谓重绘。
    /// </summary>
    public Task SaveCalcHistoryAsync(string instanceId, IReadOnlyList<CalcHistoryItem> history)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            inst.CalcHistory = history.ToList();
            _storage.Save(data);
        });

    /// <summary>保存世界时钟点位（整表替换）。空表也照写：那是"用户删光了"，与"从没配过"必须可分辨。</summary>
    public Task SaveWorldClockZonesAsync(string instanceId, IReadOnlyList<WorldClockCity> cities)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            inst.WorldClockZones = cities.ToList();
            _storage.Save(data);
        });

    /// <summary>保存倒计时列表（整表替换）。到点提醒键写在条目里，因此刷新循环触发提醒后也要走这里落一次盘。</summary>
    public Task SaveCountdownsAsync(string instanceId, IReadOnlyList<CountdownItem> items)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            inst.Countdowns = items.ToList();
            _storage.Save(data);
        });

    /// <summary>保存番茄钟时长设置（只存时长；进行中的轮次刻意不持久化）。</summary>
    public Task SaveFocusConfigAsync(string instanceId, FocusTimerConfig config)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            inst.Focus = new FocusTimerConfig { FocusMinutes = config.FocusMinutes, RestMinutes = config.RestMinutes };
            _storage.Save(data);
        });

    /// <summary>保存系统监控的指标勾选（整数形态的旗标集合）。0 照写：那是"用户全取消了"，与 null＝没配过必须可分辨。</summary>
    public Task SaveMonitorMetricsAsync(string instanceId, int wire)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            inst.MonitorMetrics = wire;
            _storage.Save(data);
        });

    /// <summary>
    /// 从别处（待办条目右键「开始专注」）拉起番茄钟：落到首个番茄钟实例，无则新建并显示。
    /// <b>成败与原因由那个组件自己显示在界面上</b>（本窗口被唤起到前台，提示文本就在里面）——
    /// 返回值只用于日志，不要在调用点再叠一层弹窗：两处都说一遍会互相矛盾。
    /// </summary>
    public Task<bool> StartFocusAsync(string? title) => OnUiAsync(() =>
    {
        var data = _storage.Load();
        var inst = data.Instances.FirstOrDefault(i => i.Kind == WidgetKind.Focus);
        if (inst is null)
        {
            inst = CreateInstanceConfig(data, WidgetKind.Focus);
            data.Instances.Add(inst);
            _storage.Save(data);
            InstancesChanged?.Invoke();
        }
        ShowInternal(inst.Id);   // 窗口没有就创建、隐藏的就唤起到前台
        var started = _windows.TryGetValue(inst.Id, out var win)
            && win.FindContent<StarMark.UI.Views.FocusTimerWidget>()?.StartFromTodo(title) == true;
        if (!started) StarLog.Info("番茄钟未能开始本轮（组件窗口创建失败，或本轮已在计时）");
        return started;
    });

    /// <summary>从主窗口卡片「发送到快捷启动」：落到首个快捷启动实例，无则新建一个实例并显示。</summary>
    public async Task<bool> AddLinkToQuickLaunchAsync(string title, string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        var id = await GetOrCreateQuickLaunchInstanceIdAsync();
        if (id is null) return false;
        return await AddLinkAsync(id, title, uri);
    }

    /// <summary>
    /// 把拖入快捷启动的一批外部文件/文件夹<b>按路径登记进主库</b>（触发器2「拖入即入库」）。
    /// 以与 Everything 查询/全量索引<b>完全一致</b>的 <c>(filesystem, 路径哈希)</c> 业务键幂等 upsert
    /// （<see cref="LocalFileIdentity"/>），故同一路径无论来自拖入、Everything 还是后台同步都合并为一条，不产生重复。
    /// <b>只写索引记录，绝不移动 / 改名 / 删除磁盘上的实际文件。</b>与快捷入口链接并存：链接负责在本组件展示，
    /// 登记负责使其成为可检索、可持久化置顶/标签/笔记的真实条目。活动流由调用侧的 <see cref="AddLinksAsync"/>
    /// 按真正新增的那些入口记「新增」，此处不重复记录。
    /// <para>整批<b>一次连接、一个事务</b>（逐条登记时拖 20 个文件要开 20 次库、每次还带三遍 PRAGMA），
    /// 因此语义是"要么全记要么全不记"：一次拖放就是一个动作，留下半套登记比整批没记更难解释。
    /// 缺仓储或写库失败仅记日志并静默降级（不影响快捷入口本身）。</para>
    /// </summary>
    /// <returns>真正登记的条数。</returns>
    public async Task<int> RecordPathsToLibraryAsync(IReadOnlyList<(string? Title, string Path)> paths)
    {
        if (_repo is null || paths is not { Count: > 0 }) return 0;
        try
        {
            var drafts = new List<Item>(paths.Count);
            foreach (var (title, path) in paths)
                if (!string.IsNullOrWhiteSpace(path)) drafts.Add(LocalFileIdentity.FromPath(path, title));
            if (drafts.Count == 0) return 0;
            return await _repo.RecordItemsAsync(drafts, CancellationToken.None);
        }
        catch (Exception ex)
        {
            StarLog.Error($"拖入登记本地路径失败（本批 {paths.Count} 项）", ex);
            return 0;
        }
    }

    /// <summary>
    /// 「一批本地路径 → 某个快捷启动实例」的<b>唯一落库形状</b>：一次整批登记 ＋ 一次整批添加。
    /// <para>两条出口都调它：从资源管理器<b>拖入</b>（<c>WidgetWindow.QuickLaunch_Drop</c>）与点<b>「选择文件／选择文件夹」</b>
    /// （<c>QuickLaunchWidget</c>）。两处各写一遍的话，"登记进主库了但没加进组件"这类半套状态迟早只在一处出现——
    /// 而它看起来像另一个功能的 bug，最难归因。</para>
    /// <para>选择器这条路不是锦上添花：程序以管理员身份运行时 Windows（UIPI）会整条拦下从资源管理器拖进来的消息流，
    /// 不报错也不提示，症状就是"拖了没反应"；对话框跑在本进程里，与权限等级无关。</para>
    /// </summary>
    /// <returns>真正加进本组件的入口条数（去重后为 0 也是正常结果，由调用侧说出来，别让它静默）。</returns>
    public async Task<int> AddPathsToLauncherAsync(string instanceId, IReadOnlyList<(string? Title, string Path)> picked)
    {
        var usable = picked.Where(p => !string.IsNullOrWhiteSpace(p.Path)).ToList();
        if (usable.Count == 0) return 0;
        await RecordPathsToLibraryAsync(usable);
        return await AddLinksAsync(instanceId, usable.Select(p => (TitleOf(p.Title, p.Path), new Uri(p.Path).AbsoluteUri)).ToList());
    }

    /// <summary>没带名字就取路径末段；目录带尾斜杠时 <c>GetFileName</c> 会返回空串，退化成去掉斜杠的末段。</summary>
    private static string TitleOf(string? title, string path)
    {
        if (!string.IsNullOrWhiteSpace(title)) return title!;
        var leaf = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(leaf) ? path : leaf;
    }

    private async Task<string?> GetOrCreateQuickLaunchInstanceIdAsync()
    {
        string? id = null;
        await OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Kind == WidgetKind.QuickLaunch);
            if (inst is not null) { id = inst.Id; return; }
            var cfg = CreateInstanceConfig(data, WidgetKind.QuickLaunch);
            data.Instances.Add(cfg);
            _storage.Save(data);
            InstancesChanged?.Invoke();
            ShowInternal(cfg.Id);
            id = cfg.Id;
        });
        return id;
    }

    public async Task RemoveLinkAsync(string instanceId, string uri)
    {
        bool removed = false;
        string? removedTitle = null;
        await OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            var doomed = inst.Links.Where(l => string.Equals(l.Uri, uri, StringComparison.OrdinalIgnoreCase)).ToList();
            if (doomed.Count == 0) return;
            removedTitle = doomed[0].Title;
            inst.Links.RemoveAll(l => string.Equals(l.Uri, uri, StringComparison.OrdinalIgnoreCase));
            _storage.Save(data);
            removed = true;
        });
        if (!removed) return;
        LinksChanged?.Invoke(instanceId);
        // 用户移除一个快捷入口 → 活动流记「删除」（红）。#51。
        await LogActivityAsync(ActivityKind.ItemDelete, removedTitle ?? uri, uri);
    }

    /// <summary>记录一条用户主动活动（快捷入口等非 items 资源）。仓库不可用时静默跳过，绝不打断交互。</summary>
    private async Task LogActivityAsync(ActivityKind kind, string title, string? uri)
    {
        if (_repo is null) return;
        try { await _repo.LogActivityAsync(kind, null, title, uri, CancellationToken.None); }
        catch (Exception ex) { StarLog.Error($"记录快捷入口活动失败 ({kind})", ex); }
    }

    /// <summary>批量记录用户主动活动：<b>整批一次连接</b>（逐条记的话拖 N 个快捷入口就要开 N 次库、
    /// 数 N 遍活动总数、裁 N 遍环形缓冲）。仓库不可用时同样静默跳过，绝不打断交互。</summary>
    private async Task LogActivitiesAsync(ActivityKind kind, IReadOnlyList<(string Title, string Uri)> rows)
    {
        if (_repo is null || rows.Count == 0) return;
        try
        {
            var events = new List<ActivityDraft>(rows.Count);
            foreach (var (title, uri) in rows) events.Add(new ActivityDraft(kind, null, title, uri));
            await _repo.LogActivitiesAsync(events, CancellationToken.None);
        }
        catch (Exception ex) { StarLog.Error($"记录快捷入口活动失败 ({kind} ×{rows.Count})", ex); }
    }

    // ───────────────────────── 跨窗口动作 ─────────────────────────

    public void RequestGlobalSearch(string query) => GlobalSearchRequested?.Invoke(query);

    public void OpenMainWindow() => App.PresentMainWindow();

    public void OpenWidgetSettings() => App.PresentMainWindow(settings: true);

    // ───────────────────────── 内部 ─────────────────────────

    /// <summary>
    /// 显示某实例。<b>这一层只做计时</b>：启动要建十几颗组件窗口，"每颗多贵"必须由日志说话，
    /// 而不是靠"约 -50~150 ms"这类猜（超过 30 ms 才写行，正常环境一条都不出）。
    /// </summary>
    private void ShowInternal(string id)
        => StarMark.Abstractions.StartupProfile.Measure($"显示组件窗口 {id}", () => ShowNow(id), logWhenMs: 30);

    private void ShowNow(string id)
    {
        // 窗口创建/显示必须整体兜错：这里的异常会顺着 UI 线程冒到点击处理，
        // 演变成未处理异常让整个应用卡死崩溃（历史事故：外观设置读取失败导致「全部显示」崩溃）。
        try
        {
            if (!_windows.TryGetValue(id, out var window))
            {
                var data = _storage.Load();
                var cfg = data.Instances.FirstOrDefault(i => i.Id == id);
                if (cfg is null) return;
                window = new WidgetWindow(id, cfg, _storage, _repo, this);
                _windows[id] = window;
            }
            window.Reveal();
        }
        catch (Exception ex)
        {
            StarLog.Error($"显示桌面组件失败 ({id})", ex);
        }
    }

    /// <summary>
    /// 关闭一个实例。<paramref name="persist"/> 决定"要不要先把屏幕上的几何留在盘上"：
    /// 移除实例（那一行已从存档里删掉）与应用快照（磁盘刚按快照写好，不许用旧几何盖回去）时必须 false，
    /// 退出应用时必须 true。<b>这个旗标曾经被完全忽略</b>——于是每个调用点都各写一趟整档，
    /// 而"该不该写"根本没人说了算。
    /// </summary>
    private void CloseInternal(string id, bool persist = true)
    {
        if (!_windows.Remove(id, out var window)) return;
        if (persist) PersistBoundsFor(new[] { window });
        window.Shutdown();
    }

    /// <summary>整批关闭：<b>几何一次落盘</b>，再逐个关窗（逐个 PersistBounds 时 N 个窗口＝N 趟整档读写）。</summary>
    private void CloseAll(IReadOnlyList<string> ids, bool persist)
    {
        if (persist)
        {
            var targets = new List<WidgetWindow>();
            foreach (var id in ids)
                if (_windows.TryGetValue(id, out var w)) targets.Add(w);
            PersistBoundsFor(targets);
        }
        foreach (var id in ids.ToList()) CloseInternal(id, persist: false);
    }

    /// <summary>整批临时隐藏：同样先一次性把几何写好，再逐个隐藏。</summary>
    private void HideTemporaryAll(IReadOnlyList<WidgetWindow> windows)
    {
        if (windows.Count == 0) return;
        PersistBoundsFor(windows);
        foreach (var w in windows) w.HideTemporary();
    }

    /// <summary>
    /// 一批窗口各自的几何<b>共用一次读档、最多一次落盘</b>。<see cref="WidgetStorage.Mutate"/> 存在的理由就在这里：
    /// <c>WidgetWindow</c> 自己 Load+Save 的话，N 个窗口就是 N 趟整档读写（隐藏/关闭/协同拖动都会踩到）。
    /// 一个窗口都没写动到（实例已移除）时一次盘都不写。
    /// </summary>
    private void PersistBoundsFor(IReadOnlyList<WidgetWindow> windows) =>
        _storage.Mutate(data =>
        {
            var wrote = false;
            foreach (var w in windows) wrote |= w.WriteBoundsInto(data);
            return wrote;
        });
}
