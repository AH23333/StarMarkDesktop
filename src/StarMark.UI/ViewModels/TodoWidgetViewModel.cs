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

namespace StarMark.UI.ViewModels;

/// <summary>待办组件中的一行（数据来自统一 items 表，source = local）。</summary>
public sealed record TodoRow(
    long Id,
    string Text,
    bool Done,
    int Color,
    long? DueTs,
    string DueText);

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
/// 这里只取高价值子集，不做 DragDrop 排序 / 子步骤 / 附件等重功能）：
/// ① 筛选分段各带**计数徽标**；② **颜色标记**；③ **截止日期**（今天/明天/清除）；
/// ④ 删除后**内联撤销条**。
/// 颜色与截止沿用 extra_json 键（见 <see cref="LocalItemState"/>），旧数据零迁移。
/// </para>
/// </summary>
public sealed class TodoWidgetViewModel : ObservableObject
{
    private readonly IItemRepository? _repo;
    private readonly string _instanceId;
    private readonly DispatcherQueue _dispatcher;

    /// <summary>全部行（内部真源）；UI 绑的是筛选后的 <see cref="Visible"/>。</summary>
    private List<TodoRow> _all = new();

    private TodoFilter _filter = TodoFilter.All;
    private Item? _undoSnapshot;
    private string _undoText = string.Empty;

    public TodoWidgetViewModel(IItemRepository? repo, string instanceId)
    {
        _repo = repo;
        _instanceId = instanceId;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("TodoWidgetViewModel 必须在 UI 线程构造");
        _ = LoadAsync();
    }

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
        try
        {
            var items = await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Todo, ct: CancellationToken.None);
            var now = DateTimeOffset.Now;
            var rows = items
                .Where(i => LocalItemState.DecodeInstanceId(i.SourceId) == _instanceId)
                .OrderBy(i => LocalItemState.IsDone(i))
                // 有截止的排前面（早的在前），无截止的按创建时间新→旧
                .ThenBy(i => LocalItemState.GetDue(i) ?? long.MaxValue)
                .ThenByDescending(i => i.CreatedAt)
                .Select(i => BuildRow(i, now))
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
            LocalItemState.DescribeDue(due, now));
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
            Visible.Clear();
            foreach (var r in _all)
            {
                if (_filter == TodoFilter.Active && r.Done) continue;
                if (_filter == TodoFilter.Completed && !r.Done) continue;
                Visible.Add(r);
            }
            OnPropertyChanged(nameof(AllCount));
            OnPropertyChanged(nameof(ActiveCount));
            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(SummaryText));
        });
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
        await _repo.UpsertLocalItemAsync(item);
        await LoadAsync();
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
