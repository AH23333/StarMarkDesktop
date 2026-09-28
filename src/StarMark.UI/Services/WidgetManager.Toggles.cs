#nullable enable
using Microsoft.UI.Dispatching;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// WidgetManager 的这一段——整群开关这一头：全部显示/隐藏、按 kind 切、全部置顶/不置顶，以及外观与主题的批量刷新。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class WidgetManager
{

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
}
