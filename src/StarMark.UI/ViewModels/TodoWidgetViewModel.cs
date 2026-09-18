#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;
using StarMark.Core.Widgets;

namespace StarMark.UI.ViewModels;

/// <summary>待办组件中的一行（数据来自统一 items 表，source = local）。</summary>
public sealed record TodoRow(long Id, string Text, bool Done);

/// <summary>
/// 待办组件 ViewModel（A-3）：数据统一写入 items 表（source = <see cref="ItemSources.Local"/>），
/// 多实例靠 source_id 编码 instanceId 隔离。勾选 / 删除 / 新增只刷新列表集合（增量更新）。
/// 相比旧版（widgets.json 独立 Todos 列表）更统一——待办也成为可搜索/可入标签格的统一条目。
/// </summary>
public sealed class TodoWidgetViewModel
{
    private readonly IItemRepository? _repo;
    private readonly string _instanceId;
    private readonly DispatcherQueue _dispatcher;

    public ObservableCollection<TodoRow> Todos { get; } = new();

    public TodoWidgetViewModel(IItemRepository? repo, string instanceId)
    {
        _repo = repo;
        _instanceId = instanceId;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("TodoWidgetViewModel 必须在 UI 线程构造");
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (_repo is null) return;
        try
        {
            var items = await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Todo, ct: CancellationToken.None);
            var rows = items
                .Where(i => LocalItemState.DecodeInstanceId(i.SourceId) == _instanceId)
                .OrderBy(i => LocalItemState.IsDone(i))
                .ThenByDescending(i => i.CreatedAt)
                .Select(i => new TodoRow(i.Id, i.Title, LocalItemState.IsDone(i)))
                .ToList();
            RunOnUi(() =>
            {
                Todos.Clear();
                foreach (var r in rows) Todos.Add(r);
            });
        }
        catch (Exception ex)
        {
            StarLog.Error("加载待办失败", ex);
        }
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
        if (_repo is null) return;
        var items = await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Todo, ct: CancellationToken.None);
        var it = items.FirstOrDefault(i => i.Id == id);
        if (it is null) return;
        LocalItemState.SetDone(it, done);
        it.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _repo.UpsertLocalItemAsync(it);
        await LoadAsync();
    }

    public async Task DeleteAsync(long id)
    {
        if (_repo is null) return;
        var items = await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Todo, ct: CancellationToken.None);
        var it = items.FirstOrDefault(i => i.Id == id);
        if (it is null) return;
        await _repo.DeleteBySourceIdAsync(ItemSources.Local, it.SourceId);
        await LoadAsync();
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }
}
