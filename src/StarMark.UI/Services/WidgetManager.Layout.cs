#nullable enable
using Microsoft.UI.Dispatching;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// WidgetManager 的这一段——布局：存哪几套、怎么应用（应用走整批一次落盘，不逐窗各写一趟）。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class WidgetManager
{

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
}
