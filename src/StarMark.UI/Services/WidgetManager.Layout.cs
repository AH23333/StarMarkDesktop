#nullable enable
using Microsoft.UI.Dispatching;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
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
        LastPersistError = null;
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
        // SaveLayout 没写进磁盘时交回 null（P-16）：此刻照旧发 LayoutsChanged 并返回内存里的
        // layout 就是谎报——它只在缓存里，重启后布局列表里不会有它。
        if (_storage.SaveLayout(layout) is null)
        {
            StarLog.Error($"保存布局未落盘（写盘失败或被占用）：{name}");
            LastPersistError = LocMessage.Of("widget.persist.notSavedBody");
            return null;
        }
        LayoutsChanged?.Invoke();
        return layout;
    });

    /// <summary>删除一套布局方案。返回 false ＝没删掉：要么本来就没有（静默照旧），要么有但没写进磁盘
    /// （此时 <see cref="LastPersistError"/> 非空——缓存里删了、盘上还在，重启后会回来，P-16）。</summary>
    public Task<bool> DeleteLayoutAsync(string layoutId) => OnUiAsync(() =>
    {
        LastPersistError = null;
        // 删前先记「它本来在不在」：Delete 动的是缓存对象，删后再回头查只会假「没找到」。
        var existed = _storage.FindLayout(layoutId) is not null;
        var removed = _storage.DeleteLayout(layoutId);
        if (removed) LayoutsChanged?.Invoke();
        else if (existed)
        {
            StarLog.Error($"删除布局未落盘（写盘失败或被占用）：{layoutId}");
            LastPersistError = LocMessage.Of("widget.persist.notDeletedBody");
        }
        return removed;
    });

    /// <summary>
    /// 应用一套布局：把布局里的每条记录套到对应实例（按「类型 + 序号」），缺少的实例当场新建；
    /// 布局之外的实例统一隐藏（实例与其内容仍保留，随时可再次显示）。
    /// 因此同一时刻只会显示一套布局（不会出现两套叠屏）。
    /// 应用成功后把该布局记为「默认布局」（下次启动自动恢复）。
    /// </summary>
    public async Task<bool> ApplyLayoutAsync(string layoutId)
    {
        var layout = await OnUiAsync(() => _storage.FindLayout(layoutId));
        if (layout is null) return false;

        var data = await OnUiAsync(() => _storage.Load());
        // 分帧套用（片243）：一颗一拍 UI 回合，点「应用布局」不再整轮秒级卡死——与启动恢复同待遇
        await ApplyLayoutSlicedAsync(data, layout);
        return await OnUiAsync(() =>
        {
            data.DefaultLayoutId = layoutId;     // 记为默认布局（最后一次选择的布局）
            _storage.Save(data);
            return true;
        });
    }

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
            ApplyGeometryEntryCore(data, entry, mapping, used);
        }

        // 布局之外的实例：隐藏但保留（内容不丢）
        HideTemporaryAll(_windows.Values.Where(w => !used.Contains(w.InstanceId)).ToList());

        InstancesChanged?.Invoke();
        return mapping;
    }

    /// <summary>
    /// 几何套用的<b>单条目</b>一步：匹配/新建实例、写几何与外观、显示、下发窗口。
    /// 从整循环里抽出这一步，是为了让分帧版（<see cref="ApplyLayoutSlicedAsync"/>）能
    /// 「每颗一拍」复用同一段匹配逻辑——快照与布局已经漂过一次，这里再抄一份必漂第三次。
    /// </summary>
    private void ApplyGeometryEntryCore<T>(WidgetStoreData data, T entry,
        List<(T entry, WidgetInstanceConfig inst)> mapping, HashSet<string> used)
        where T : IWidgetGeometryEntry
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
        if (created) data.Instances.Add(inst);
        // ✅P-43 widgets 半边：新建实例不再"先落盘再从盘上读回来"——待落盘的存档直接递给显示路径
        // （ShowNow 用那份内存建窗，不再整档读盘），落盘由调用方收尾那一次 Save 完成（套布局/还原快照
        // /启动恢复本来就有那一次）。从前这里每建一颗就是一趟整档写＋（Save 作废缓存后）一趟整档读，
        // N 颗新实例＝N 趟往返，还把半套布局提前写上了盘——中途出错留下的是半新半旧的落盘态。
        ShowInternal(inst.Id, data);
        // 窗口缓存的 config 是上一次 Load 的对象（新建者甚至是保存前的旧磁盘数据），
        // 必须显式下发新位置 + 新外观并回写，否则 ApplyLayoutCore 里改的 inst 不会落到窗口。
        if (_windows.TryGetValue(inst.Id, out var w))
        {
            w.ApplyBounds(entry.X, entry.Y, entry.Width, entry.Height, entry.Topmost);
            w.ApplyAppearance(entry.Appearance);
        }

        mapping.Add((entry, inst));
    }

    /// <summary>
    /// 分帧版布局套用（启动恢复与「应用布局」共用）：逐条目一拍 UI 回合——每颗窗口
    /// 「建窗＋套外观」之间 UI 线程能喘气；收尾（隐藏布局外实例＋InstancesChanged）
    /// 与同步版完全一致。此前 12 颗在同一回合里连建，UI 线程连占 1.7-2.1s
    /// （UIStallWatchdog 每轮启动都报，用户感知就是「启动那两秒点不动」）。
    /// </summary>
    private async Task ApplyLayoutSlicedAsync(WidgetStoreData data, WidgetLayout layout)
    {
        var mapping = new List<(WidgetLayoutEntry, WidgetInstanceConfig)>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in layout.Entries)
        {
            await OnUiAsync(() => ApplyGeometryEntryCore(data, entry, mapping, used));
            // 片244：一拍回合只切开了建窗，首帧合成仍一批落地（1860 ms／UI 自耗 1657 ms 实测）。
            // 每颗 Reveal 后留真墙钟间隔，让首帧各自落进合成回合——启动恢复与「应用布局」同待遇。
            await Task.Delay(WidgetStartupPolicy.RevealIntervalMs);
        }

        await OnUiAsync(() =>
        {
            // 布局之外的实例：隐藏但保留（内容不丢）
            HideTemporaryAll(_windows.Values.Where(w => !used.Contains(w.InstanceId)).ToList());
            InstancesChanged?.Invoke();
        });
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
