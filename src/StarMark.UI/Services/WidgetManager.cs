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

    public WidgetManager(WidgetStorage storage, IItemRepository? repo)
    {
        _storage = storage;
        _repo = repo;
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
        var tcs = new TaskCompletionSource();
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
        var tcs = new TaskCompletionSource<T>();
        ui.TryEnqueue(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
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
                foreach (var id in ids) CloseInternal(id, persist: false);
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
        if (_windows.TryGetValue(id, out var w)) w.HideTemporary();
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
        foreach (var w in _windows.Values.ToList()) w.HideTemporary();
    });

    /// <summary>显示某类型的全部实例（快捷键「显示组件」用）。</summary>
    public Task ShowKindAsync(WidgetKind kind) => OnUiAsync(() =>
    {
        foreach (var inst in _storage.Load().Instances.Where(i => i.Kind == kind)) ShowInternal(inst.Id);
    });

    /// <summary>隐藏某类型的全部实例（快捷键「隐藏组件」用）。</summary>
    public Task HideKindAsync(WidgetKind kind) => OnUiAsync(() =>
    {
        foreach (var w in _windows.Values.Where(w => w.Kind == kind).ToList()) w.HideTemporary();
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
            foreach (var w in kindWindows) w.HideTemporary();
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
            foreach (var w in _windows.Values.ToList()) w.HideTemporary();
        }
        else
        {
            // 显示分支：恢复到「最后一次切换的布局」而非暴露所有实例，
            // 否则切换布局后被该布局排除的组件会被一起显示，看起来像变回了另一套布局。
            if (_storage.Load().Instances.Count == 0) { _ = AddInstanceAsync(WidgetKind.QuickLaunch); return; }
            ShowByDefaultLayoutOrAll();
        }
    });

    /// <summary>托盘"桌面组件"总开关：有可见组件→全部隐藏；否则显示全部已启用实例。</summary>
    public Task ToggleAllAsync() => OnUiAsync(() =>
    {
        var anyVisible = _windows.Values.Any(w => w.IsVisible);
        if (anyVisible)
        {
            foreach (var w in _windows.Values.ToList()) w.HideTemporary();
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
    });

    public Task ShutdownAllAsync() => OnUiAsync(() =>
    {
        foreach (var id in _windows.Keys.ToList()) CloseInternal(id, persist: false);
    });

    // ───────────────────────── 布局方案 ─────────────────────────

    /// <summary>全部已保存布局（按名称排序）。</summary>
    public IReadOnlyList<WidgetLayout> GetLayouts() => _storage.GetLayouts();

    /// <summary>
    /// 把「当前屏幕上可见的组件」保存为一套布局：记录每个实例的类型、同类序号、位置尺寸与置顶状态。
    /// 只快照可见窗口——布局的语义就是「用户当下看到的这一屏」。
    /// </summary>
    public Task<WidgetLayout?> SaveCurrentLayoutAsync(string name) => OnUiAsync(() =>
    {
        var data = _storage.Load();
        var entries = new List<WidgetLayoutEntry>();
        var perKind = new Dictionary<WidgetKind, int>();

        foreach (var inst in data.Instances)
        {
            if (!_windows.TryGetValue(inst.Id, out var w) || !w.IsVisible) continue;
            Windows.Graphics.RectInt32 r;
            try { r = WindowInterop.GetWindowRect(w); }
            catch { continue; }
            if (r.Width <= 0 || r.Height <= 0) continue;

            perKind.TryGetValue(inst.Kind, out var idx);
            entries.Add(new WidgetLayoutEntry
            {
                Kind = inst.Kind,
                Index = idx,
                X = r.X, Y = r.Y, Width = r.Width, Height = r.Height,
                Topmost = inst.Topmost,
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
    {
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in layout.Entries)
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
            used.Add(inst.Id);
            if (created) data.Instances.Add(inst);

            ShowInternal(inst.Id);
            // 窗口缓存的 config 是上一次 Load 的对象，必须显式下发新位置并回写
            if (_windows.TryGetValue(inst.Id, out var w))
                w.ApplyBounds(entry.X, entry.Y, entry.Width, entry.Height, entry.Topmost);
        }

        // 布局之外的实例：隐藏但保留（内容不丢）
        foreach (var id in _windows.Keys.ToList())
        {
            if (used.Contains(id)) continue;
            if (_windows.TryGetValue(id, out var w)) w.HideTemporary();
        }

        InstancesChanged?.Invoke();
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

    /// <summary>新增快捷入口（去重）；自动定位到该实例（若该实例窗口已开则增量刷新）。</summary>
    public async Task<bool> AddLinkAsync(string instanceId, string title, string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        var added = await OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return false;
            if (inst.Links.Any(l => string.Equals(l.Uri, uri, StringComparison.OrdinalIgnoreCase)))
                return false;
            inst.Links.Add(new LinkItem
            {
                Id = WidgetStorage.NewId(),
                Title = string.IsNullOrWhiteSpace(title) ? uri : title.Trim(),
                Uri = uri.Trim(),
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
            _storage.Save(data);
            return true;
        });
        if (!added) return false;

        LinksChanged?.Invoke(instanceId);
        return true;
    }

    /// <summary>从主窗口卡片「发送到快捷启动」：落到首个快捷启动实例，无则新建一个实例并显示。</summary>
    public async Task<bool> AddLinkToQuickLaunchAsync(string title, string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        var id = await GetOrCreateQuickLaunchInstanceIdAsync();
        if (id is null) return false;
        return await AddLinkAsync(id, title, uri);
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
        await OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            if (inst.Links.RemoveAll(l => string.Equals(l.Uri, uri, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                _storage.Save(data);
                removed = true;
            }
        });
        if (removed) LinksChanged?.Invoke(instanceId);
    }

    // ───────────────────────── 跨窗口动作 ─────────────────────────

    public void RequestGlobalSearch(string query) => GlobalSearchRequested?.Invoke(query);

    public void OpenMainWindow() => App.PresentMainWindow();

    public void OpenWidgetSettings() => App.PresentMainWindow(settings: true);

    // ───────────────────────── 内部 ─────────────────────────

    private void ShowInternal(string id)
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

    private void CloseInternal(string id, bool persist = true)
    {
        if (_windows.Remove(id, out var window))
        {
            window.Shutdown();
        }
    }
}
