#nullable enable
using Microsoft.UI.Dispatching;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// 桌面组件管理器（对标 DeskBox WidgetManager 的单窗口多实例模型）：
/// 每种组件一个独立 <see cref="WidgetWindow"/>，实例保活（隐藏而非关闭），
/// 启用集合/位置/置顶/快捷入口全部持久化到 widgets.json。
/// 所有公开方法都会封送到 UI 线程（托盘回调与设置页都可能调用）。
/// </summary>
public sealed class WidgetManager
{
    private readonly WidgetStorage _storage;
    private readonly IItemRepository? _repo;
    private readonly Dictionary<WidgetKind, WidgetWindow> _windows = new();
    private DispatcherQueue? _ui;

    /// <summary>快捷入口数据变化（新增/删除/外部发送），快捷启动格订阅刷新。</summary>
    public event Action? LinksChanged;

    /// <summary>快捷搜索组件请求唤起主窗口并执行搜索。</summary>
    public event Action<string>? GlobalSearchRequested;

    public WidgetManager(WidgetStorage storage, IItemRepository? repo)
    {
        _storage = storage;
        _repo = repo;
    }

    /// <summary>在 UI 线程上记录调度器（MainWindow 构造时调用）。</summary>
    public void Initialize(DispatcherQueue ui) => _ui = ui;

    private DispatcherQueue Ui() =>
        _ui ??= DispatcherQueue.GetForCurrentThread()
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

    // ───────────────────────── 启用集合 ─────────────────────────

    public bool IsEnabled(WidgetKind kind) => _storage.Load().Enabled.Contains(kind);

    public IReadOnlyList<WidgetKind> EnabledKinds => _storage.Load().Enabled;

    /// <summary>启用/停用某组件。启用→创建并显示；停用→关闭并从启用集合移除。</summary>
    public Task SetEnabledAsync(WidgetKind kind, bool enabled)
        => OnUiAsync(() =>
        {
            var data = _storage.Load();
            var has = data.Enabled.Contains(kind);
            if (enabled == has)
            {
                // 状态一致但用户可能想把已启用却被临时隐藏的窗口叫出来
                if (enabled && (!_windows.TryGetValue(kind, out var w) || !w.IsVisible)) ShowInternal(kind);
                return;
            }
            if (enabled)
            {
                data.Enabled.Add(kind);
                _storage.Save(data);
                ShowInternal(kind);
            }
            else
            {
                data.Enabled.Remove(kind);
                _storage.Save(data);
                CloseInternal(kind);
            }
        });

    /// <summary>移除组件（标题栏 ✕ / 菜单取消勾选）。</summary>
    public Task RemoveAsync(WidgetKind kind) => SetEnabledAsync(kind, false);

    /// <summary>显示某组件（临时隐藏后恢复；未启用则自动启用）。</summary>
    public Task ShowAsync(WidgetKind kind) => OnUiAsync(() =>
    {
        if (!IsEnabled(kind))
        {
            var data = _storage.Load();
            data.Enabled.Add(kind);
            _storage.Save(data);
        }
        ShowInternal(kind);
    });

    /// <summary>临时隐藏（实例保留）。</summary>
    public Task HideTemporaryAsync(WidgetKind kind) => OnUiAsync(() =>
    {
        if (_windows.TryGetValue(kind, out var w)) w.HideTemporary();
    });

    public Task ShowAllAsync() => OnUiAsync(() =>
    {
        foreach (var kind in _storage.Load().Enabled) ShowInternal(kind);
    });

    public Task HideAllAsync() => OnUiAsync(() =>
    {
        foreach (var w in _windows.Values.ToList()) w.HideTemporary();
    });

    /// <summary>托盘“桌面组件”总开关：有可见组件→全部隐藏；否则显示全部已启用。</summary>
    public Task ToggleAllAsync() => OnUiAsync(() =>
    {
        var anyVisible = _windows.Values.Any(w => w.IsVisible);
        if (anyVisible)
        {
            foreach (var w in _windows.Values.ToList()) w.HideTemporary();
        }
        else
        {
            var enabled = _storage.Load().Enabled;
            if (enabled.Count == 0)
            {
                // 一个组件都没有时，总开关默认给出快捷启动格
                _ = SetEnabledAsync(WidgetKind.QuickLaunch, true);
            }
            else
            {
                foreach (var kind in enabled) ShowInternal(kind);
            }
        }
    });

    /// <summary>启动时恢复启用的组件（MainWindow 构造后调用）。</summary>
    public Task RestoreOnStartupAsync() => OnUiAsync(() =>
    {
        foreach (var kind in _storage.Load().Enabled)
        {
            try { ShowInternal(kind); }
            catch (Exception ex) { StarLog.Error($"恢复桌面组件失败 ({kind})", ex); }
        }
    });

    public Task ShutdownAllAsync() => OnUiAsync(() =>
    {
        foreach (var kind in _windows.Keys.ToList()) CloseInternal(kind, persist: false);
    });

    // ───────────────────────── 吸附支持 ─────────────────────────

    /// <summary>其他可见组件窗口的当前矩形（物理像素），供拖动吸附。</summary>
    public IReadOnlyList<RectInt32> GetOtherBounds(WidgetKind self)
    {
        var list = new List<RectInt32>();
        foreach (var (kind, window) in _windows)
        {
            if (kind == self || !window.IsVisible) continue;
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

    /// <summary>新增快捷入口（去重）；会自动启用并显示快捷启动格。</summary>
    public async Task<bool> AddLinkAsync(string title, string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        var added = await OnUiAsync(() =>
        {
            var data = _storage.Load();
            if (data.Links.Any(l => string.Equals(l.Uri, uri, StringComparison.OrdinalIgnoreCase)))
                return false;
            data.Links.Add(new LinkItem
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

        if (!IsEnabled(WidgetKind.QuickLaunch))
            await SetEnabledAsync(WidgetKind.QuickLaunch, true);
        else if (_windows.TryGetValue(WidgetKind.QuickLaunch, out var w) && !w.IsVisible)
            await ShowAsync(WidgetKind.QuickLaunch);

        LinksChanged?.Invoke();
        return true;
    }

    public Task RemoveLinkAsync(string uri) => OnUiAsync(() =>
    {
        var data = _storage.Load();
        var removed = data.Links.RemoveAll(l => string.Equals(l.Uri, uri, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            _storage.Save(data);
            LinksChanged?.Invoke();
        }
    });

    // ───────────────────────── 跨窗口动作 ─────────────────────────

    public void RequestGlobalSearch(string query) => GlobalSearchRequested?.Invoke(query);

    public void OpenMainWindow() => App.PresentMainWindow();

    public void OpenWidgetSettings() => App.PresentMainWindow(settings: true);

    // ───────────────────────── 内部 ─────────────────────────

    private void ShowInternal(WidgetKind kind)
    {
        if (!_windows.TryGetValue(kind, out var window))
        {
            window = new WidgetWindow(kind, _storage, _repo, this);
            _windows[kind] = window;
        }
        window.Reveal();
    }

    private void CloseInternal(WidgetKind kind, bool persist = true)
    {
        if (_windows.Remove(kind, out var window))
        {
            window.Shutdown();
        }
    }
}
