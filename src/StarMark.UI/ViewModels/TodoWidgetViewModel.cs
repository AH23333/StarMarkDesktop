#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;

namespace StarMark.UI.ViewModels;

/// <summary>待办组件中的一行（数据来自统一 items 表，source = local）。</summary>
public sealed record TodoRow(
    long Id,
    string Text,
    bool Done,
    int Color,
    long? DueTs,
    string DueText,
    int? Order);

/// <summary>待办列表筛选。</summary>
public enum TodoFilter
{
    All = 0,
    Active = 1,
    Completed = 2,
}

/// <summary>
/// 待办组件 ViewModel（A-3）：数据统一写入 items 表（source = <see cref="ItemSources.Local"/>），
/// 多实例靠 source_id 编码 instanceId 隔离。
/// <para>
/// 本轮补齐 DeskBox TodoWidgetViewModel 中**用户最能感知**的四项（其内部 Todo 有数千行，
/// 这里只取高价值子集，不做子步骤 / 附件等重功能）：
/// ① 筛选分段各带**计数徽标**；② **颜色标记**；③ **截止日期**（今天/明天/清除）；
/// ④ 删除后**内联撤销条**；⑤ **拖拽排序**（写 extra_json 的 order，仅「全部」筛选下开放）。
/// 颜色与截止沿用 extra_json 键（见 <see cref="LocalItemState"/>），旧数据零迁移。
/// </para>
/// </summary>
public sealed class TodoWidgetViewModel : ObservableObject
{
    private readonly IItemRepository? _repo;
    private readonly string _instanceId;
    private readonly DispatcherQueue _dispatcher;

    /// <summary>
    /// 数据变更同步器：任何一处（主界面 / 同步 / 另一个待办组件）改了库，本组件都会去抖重载一次，
    /// 不会出现"主界面改了、组件还是老的"。
    /// </summary>
    private readonly DataChangeReloader _sync;

    /// <summary>加载闸门：广播 + 本地操作可能同时触发重载，串行化避免重复打库。</summary>
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    /// <summary>全部行（内部真源）；UI 绑的是筛选后的 <see cref="Visible"/>。</summary>
    private List<TodoRow> _all = new();

    private TodoFilter _filter = TodoFilter.All;
    private Item? _undoSnapshot;
    private string _undoText = string.Empty;
    /// <summary>是否发生过一次真实拖拽（决定拖拽落地是否写盘，见 <see cref="PersistVisibleOrderAsync"/>）。</summary>
    private bool _dragActive;

    public TodoWidgetViewModel(IItemRepository? repo, string instanceId)
    {
        _repo = repo;
        _instanceId = instanceId;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("TodoWidgetViewModel 必须在 UI 线程构造");
        _sync = new DataChangeReloader(LoadAsync);
        _ = LoadAsync();
    }

    /// <summary>退订数据广播（组件卸载时调用）。</summary>
    public void Dispose() => _sync.Dispose();

    /// <summary>按当前筛选可见的行（UI 绑定它）。</summary>
    public ObservableCollection<TodoRow> Visible { get; } = new();

    public TodoFilter Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, value)) return;
            ApplyFilter();
        }
    }

    /// <summary>
    /// 是否允许拖拽排序。只在「全部」筛选下开放：筛选视图里看到的是子集，
    /// 把子集的顺序写回全量列表会产生歧义（被隐藏的行该插在哪？），
    /// 与其猜，不如这时不开放拖拽。
    /// </summary>
    public bool CanReorder => _filter == TodoFilter.All;

    public int AllCount => _all.Count;
    public int ActiveCount => _all.Count(r => !r.Done);
    public int CompletedCount => _all.Count(r => r.Done);

    /// <summary>底部统计文案。DeskBox 区分「全部完成」与「当前筛选无结果」两种空态，这里同口径。</summary>
    public string SummaryText => ActiveCount == 0 && AllCount > 0
        ? "全部完成"
        : AllCount == 0 ? string.Empty : $"剩余 {ActiveCount} 项";

    /// <summary>撤销条是否显示（删除后可撤销）。</summary>
    public bool HasUndo => _undoSnapshot is not null;

    public string UndoText
    {
        get => _undoText;
        private set => SetProperty(ref _undoText, value);
    }

    public async Task LoadAsync()
    {
        if (_repo is null) return;
        await _loadGate.WaitAsync();
        try
        {
            var items = await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Todo, ct: CancellationToken.None);
            var now = DateTimeOffset.Now;
            var rows = items
                .Where(i => LocalItemState.DecodeInstanceId(i.SourceId) == _instanceId)
                .Select(i => BuildRow(i, now))
                // 手动排过序的按 order 升序走在最前；没排过的（老数据）落在后面，
                // 组内再按「未完成优先 → 截止早的优先 → 新的优先」自动排。
                .OrderBy(r => r.Order ?? int.MaxValue)
                .ThenBy(r => r.Done)
                .ThenBy(r => r.DueTs ?? long.MaxValue)
                .ThenByDescending(r => r.Id)
                .ToList();

            RunOnUi(() =>
            {
                _all = rows;
                ApplyFilter();
            });
        }
        catch (Exception ex)
        {
            StarLog.Error("加载待办失败", ex);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private static TodoRow BuildRow(Item i, DateTimeOffset now)
    {
        var due = LocalItemState.GetDue(i);
        return new TodoRow(
            i.Id,
            i.Title,
            LocalItemState.IsDone(i),
            LocalItemState.GetColor(i),
            due,
            LocalItemState.DescribeDue(due, now),
            LocalItemState.GetOrder(i));
    }

    /// <summary>
    /// 按当前筛选重建可见集合并刷新计数。
    /// 三个计数属性由 <see cref="ObservableObject"/> 的 <see cref="OnPropertyChanged(string)"/> 显式推送 ——
    /// XAML 侧必须写 <c>Mode=OneWay</c>，默认的 OneTime 不会订阅 INPC（项目已踩过两次）。
    /// </summary>
    private void ApplyFilter()
    {
        RunOnUi(() =>
        {
            SyncVisible();
            OnPropertyChanged(nameof(AllCount));
            OnPropertyChanged(nameof(ActiveCount));
            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(CanReorder));
        });
    }

    /// <summary>当前筛选下应该可见的行（按 _all 的既定顺序）。</summary>
    private List<TodoRow> FilteredRows()
    {
        var result = new List<TodoRow>(_all.Count);
        foreach (var r in _all)
        {
            if (_filter == TodoFilter.Active && r.Done) continue;
            if (_filter == TodoFilter.Completed && !r.Done) continue;
            result.Add(r);
        }
        return result;
    }

    /// <summary>
    /// 把 <see cref="Visible"/> 增量对齐到筛选结果。
    /// <para>
    /// 这里是「无脑 Clear + 全量 Add」的替代方案。数据广播一来就整表重建，
    /// ListView 会把所有行的容器全部销毁重建——条目一多就是一次肉眼可见的卡顿，
    /// 而且这一路都跑在 UI 线程上，同屏其它组件也会被拖住（用户报的"输一条待办，其它组件跟着发呆"）。
    /// 改成按 id 做最小差异后，绝大多数重载是**零操作**。
    /// </para>
    /// </summary>
    private void SyncVisible()
    {
        var target = FilteredRows();
        var targetIds = new HashSet<long>(target.Select(r => r.Id));

        // 1) 移除已不在结果里的
        for (var i = Visible.Count - 1; i >= 0; i--)
            if (!targetIds.Contains(Visible[i].Id)) Visible.RemoveAt(i);

        // 2) 逐位对齐：内容变了就替换，位置错了就移动，缺了就插入
        for (var i = 0; i < target.Count; i++)
        {
            var want = target[i];
            if (i < Visible.Count && Visible[i].Id == want.Id)
            {
                if (!Equals(Visible[i], want)) Visible[i] = want;   // 勾选/改色/改日期
                continue;
            }

            var at = -1;
            for (var j = i + 1; j < Visible.Count; j++)
                if (Visible[j].Id == want.Id) { at = j; break; }

            if (at >= 0) Visible.Move(at, i);
            else Visible.Insert(i, want);
        }

        // 3) 尾部多余的（理论上不会有，留作兜底）
        while (Visible.Count > target.Count) Visible.RemoveAt(Visible.Count - 1);
    }

    /// <summary>标记「确实发生过一次拖拽」。只有它为真时 <see cref="PersistVisibleOrderAsync"/> 才落盘。</summary>
    public void BeginReorder() => _dragActive = true;

    /// <summary>
    /// 把当前可见顺序写回存储（拖拽排序落地）。ListView 的内置重排已经把
    /// <see cref="Visible"/> 调整好了，这里只负责持久化。
    /// <para>
    /// 两个护栏：
    /// ① <b>必须真的拖过</b>——集合的增删也可能让 ListView 派发一次 DragItemsCompleted，
    ///    不加闸门就会在每次增删时白跑一轮「全表读 + 逐条写」，这正是"输一条待办卡一下"的来源；
    /// ② <b>一次读库批量写</b>——早先是每条都 <see cref="FindItemAsync"/>（内部整表读一遍），
    ///    N 条就是 N 次全表扫描，改成先建 id 索引再逐条写。
    /// </para>
    /// </summary>
    public async Task PersistVisibleOrderAsync()
    {
        if (!_dragActive) return;
        _dragActive = false;
        if (_repo is null || Visible.Count == 0) return;

        var ids = Visible.Select(r => r.Id).ToList();
        var idSet = new HashSet<long>(ids);
        // 被筛选隐藏的行（「全部」视图下为空）保持相对次序排在显式排序之后
        var rest = _all.Where(r => !idSet.Contains(r.Id)).Select(r => r.Id).ToList();
        var full = ids.Concat(rest).ToList();

        var items = await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Todo, ct: CancellationToken.None);
        var byId = new Dictionary<long, Item>();
        foreach (var it in items) byId[it.Id] = it;

        for (var i = 0; i < full.Count; i++)
        {
            if (!byId.TryGetValue(full[i], out var it)) continue;
            LocalItemState.SetOrder(it, i);
            it.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await _repo.UpsertLocalItemAsync(it);
        }

        await LoadAsync();
    }

    public async Task AddAsync(string text)
    {
        if (_repo is null || string.IsNullOrWhiteSpace(text)) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var item = new Item
        {
            Type = ItemType.Todo,
            Source = ItemSources.Local,
            SourceId = LocalItemState.EncodeSourceId(_instanceId, WidgetStorage.NewId()),
            Title = text.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        // 列表一旦启用过手动排序，新条目必须也带上 order，否则它会因为没有序号
        // 被排到所有已排序条目之后 —— 表现为「新增的待办跑到列表最底下」。
        LocalItemState.SetOrder(item, NextTopOrder());
        await _repo.UpsertLocalItemAsync(item);   // 写回真实行 id（INSERT ... RETURNING id）

        // 乐观插入：写完立刻把新行画出来，不等下一次整表读取。
        // 之前是"落盘 → 全表重读 → 重建列表"，回车到看见新条目之间隔着一次数据库往返 + 一次列表重建。
        RunOnUi(() =>
        {
            _all.Insert(0, BuildRow(item, DateTimeOffset.Now));
            ApplyFilter();
        });

        // 后台对齐真实排序（增量 diff 下这基本是零操作）
        await LoadAsync();
    }

    /// <summary>新条目的排序号：比现有最小号再小 1，于是出现在列表最上面（与「新的优先」一致）。</summary>
    private int NextTopOrder()
    {
        var known = _all.Where(r => r.Order.HasValue).Select(r => r.Order!.Value).ToList();
        return known.Count == 0 ? 0 : known.Min() - 1;
    }

    public async Task ToggleAsync(long id, bool done)
    {
        var it = await FindItemAsync(id);
        if (it is null) return;
        LocalItemState.SetDone(it, done);
        it.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _repo!.UpsertLocalItemAsync(it);
        await LoadAsync();
    }

    /// <summary>设置颜色标记（0 = 清除）。</summary>
    public async Task SetColorAsync(long id, int color)
    {
        var it = await FindItemAsync(id);
        if (it is null) return;
        LocalItemState.SetColor(it, color);
        it.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _repo!.UpsertLocalItemAsync(it);
        await LoadAsync();
    }

    /// <summary>
    /// 设置截止日期。<paramref name="dayOffset"/> 为相对今天的天数（0=今天，1=明天）；
    /// null 表示清除。统一存「当日 0 点」以按天比较。
    /// </summary>
    public async Task SetDueAsync(long id, int? dayOffset)
    {
        var it = await FindItemAsync(id);
        if (it is null) return;
        LocalItemState.SetDue(it, dayOffset is null
            ? null
            : LocalItemState.DayStartUnix(DateTimeOffset.Now.AddDays(dayOffset.Value)));
        it.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _repo!.UpsertLocalItemAsync(it);
        await LoadAsync();
    }

    /// <summary>
    /// 删除并把整条 item 快照留作撤销依据。
    /// 撤销走「重新 Upsert 快照」——快照保留了同一个 source_id，
    /// 所以恢复出来的还是同一条业务记录（行 id 可能不同，但业务唯一键是 (source, source_id)）。
    /// </summary>
    public async Task DeleteAsync(long id)
    {
        var it = await FindItemAsync(id);
        if (it is null) return;
        await _repo!.DeleteBySourceIdAsync(ItemSources.Local, it.SourceId);

        RunOnUi(() =>
        {
            _undoSnapshot = it;
            UndoText = $"已删除「{Trim(it.Title)}」";
            OnPropertyChanged(nameof(HasUndo));
        });
        await LoadAsync();
    }

    public async Task UndoDeleteAsync()
    {
        var snapshot = _undoSnapshot;
        if (snapshot is null) return;
        DismissUndo();
        snapshot.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _repo!.UpsertLocalItemAsync(snapshot);
        await LoadAsync();
    }

    /// <summary>关闭撤销条（下次删除前 / 组件隐藏时清理，避免快照长期驻留内存）。</summary>
    public void DismissUndo()
    {
        if (_undoSnapshot is null) return;
        RunOnUi(() =>
        {
            _undoSnapshot = null;
            UndoText = string.Empty;
            OnPropertyChanged(nameof(HasUndo));
        });
    }

    /// <summary>按行 id 取回原始 item（仓库只提供「按 source 拉全部」，故本地过滤）。</summary>
    private async Task<Item?> FindItemAsync(long id)
    {
        if (_repo is null) return null;
        try
        {
            var items = await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Todo, ct: CancellationToken.None);
            return items.FirstOrDefault(i => i.Id == id);
        }
        catch (Exception ex)
        {
            StarLog.Error("查询待办条目失败", ex);
            return null;
        }
    }

    private static string Trim(string s) =>
        s.Length <= 12 ? s : string.Concat(s.AsSpan(0, 12), "…");

    private void RunOnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }
}
