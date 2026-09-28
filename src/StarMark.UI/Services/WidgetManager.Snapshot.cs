#nullable enable
using Microsoft.UI.Dispatching;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// WidgetManager 的这一段——快照：壳子怎么建、捕获/应用/删除/提取布局，以及回滚点。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class WidgetManager
{

    // ───────────────────────── 布局与数据快照（#53） ─────────────────────────
    // 快照 = 布局半（几何 + 外观，走同一套 ApplyGeometryCore）+ 数据半（快捷入口/待办/随记/条目格查询）。
    // 与「纯模板」布局的分工：模板可复用、按 Id 就地覆盖；快照是不可变历史点，应用它 = 回到那一刻（Replace + 自动回滚）。

    /// <summary>全部快照点（新的在前）。</summary>
    public IReadOnlyList<WidgetSnapshot> GetSnapshots() => _storage.GetSnapshots();

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
}
