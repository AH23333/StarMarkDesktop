#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;

namespace StarMark.UI.ViewModels;

/// <summary>随记组件中的一条（数据来自统一 items 表，source = local）。</summary>
public sealed record QuickNoteRow(long Id, string Text);

/// <summary>
/// 随记组件 ViewModel（A-3）：数据统一写入 items 表（source = <see cref="ItemSources.Local"/>），
/// 多实例靠 source_id 编码 instanceId 隔离。保存 / 删除只刷新列表集合，输入框内容保留。
/// 相比旧版（widgets.json 独立 Notes 列表）更统一——随记也可搜索、可入标签格。
/// </summary>
public sealed class QuickNoteWidgetViewModel
{
    private const int DisplayLimit = 30;

    private readonly IItemRepository? _repo;
    private readonly string _instanceId;
    private readonly DispatcherQueue _dispatcher;

    /// <summary>数据变更同步器：别处改了随记（另一个随记组件 / 主界面）时自动去抖重载。</summary>
    private readonly DataChangeReloader _sync;

    public ObservableCollection<QuickNoteRow> Notes { get; } = new();

    public QuickNoteWidgetViewModel(IItemRepository? repo, string instanceId)
    {
        _repo = repo;
        _instanceId = instanceId;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("QuickNoteWidgetViewModel 必须在 UI 线程构造");
        _sync = new DataChangeReloader(LoadAsync);
        _ = LoadAsync();
    }

    /// <summary>退订数据广播（组件卸载时调用）。</summary>
    public void Dispose() => _sync.Dispose();

    public async Task LoadAsync()
    {
        if (_repo is null) return;
        try
        {
            var items = await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Note, ct: CancellationToken.None);
            var rows = items
                .Where(i => LocalItemState.DecodeInstanceId(i.SourceId) == _instanceId)
                .OrderByDescending(i => i.CreatedAt)
                .Take(DisplayLimit)
                .Select(i => new QuickNoteRow(i.Id, i.Title))
                .ToList();
            RunOnUi(() =>
            {
                // 内容没变就别动集合：全量 Clear+Add 会让列表把所有行容器销毁重建，
                // 数据广播一来就重建，肉眼可见地抖一下。
                if (Notes.Count == rows.Count && Notes.Zip(rows).All(p => p.First == p.Second)) return;
                Notes.Clear();
                foreach (var r in rows) Notes.Add(r);
            });
        }
        catch (Exception ex)
        {
            StarLog.Error("加载随记失败", ex);
        }
    }

    /// <summary>保存一条随记（空文本忽略）；返回是否保存成功。</summary>
    public async Task<bool> SaveAsync(string text)
    {
        if (_repo is null || string.IsNullOrWhiteSpace(text)) return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var item = new Item
        {
            Type = ItemType.Note,
            Source = ItemSources.Local,
            SourceId = LocalItemState.EncodeSourceId(_instanceId, WidgetStorage.NewId()),
            Title = text.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _repo.UpsertLocalItemAsync(item);
        await LoadAsync();
        return true;
    }

    public async Task DeleteAsync(long id)
    {
        if (_repo is null) return;
        var items = await _repo.GetBySourceAsync(ItemSources.Local, ItemType.Note, ct: CancellationToken.None);
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
