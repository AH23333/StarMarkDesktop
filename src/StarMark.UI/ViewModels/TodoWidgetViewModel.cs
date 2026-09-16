#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using StarMark.Core.Widgets;

namespace StarMark.UI.ViewModels;

/// <summary>待办组件中的一行（数据来自 widgets.json 的 Todos 列表）。</summary>
public sealed record TodoRow(long Id, string Text, bool Done);

/// <summary>
/// 待办组件 ViewModel（R3 收尾）：ObservableCollection + ItemsRepeater，
/// 勾选 / 删除 / 新增后只刷新列表集合（保持既有排序：未完成在前、各按时间倒序），
/// 不再由 code-behind 重建整棵组件 UI。数据契约不变（WidgetStorage.Todos）。
/// </summary>
public sealed class TodoWidgetViewModel
{
    private readonly WidgetStorage _storage;

    public ObservableCollection<TodoRow> Todos { get; } = new();

    public TodoWidgetViewModel(WidgetStorage storage)
    {
        _storage = storage;
        Reload();
    }

    /// <summary>从 widgets.json 重新装载列表（Normalize 负责排序与限量）。</summary>
    public void Reload()
    {
        Todos.Clear();
        foreach (var todo in _storage.Load().Todos)
            Todos.Add(new TodoRow(todo.Id, todo.Text, todo.Done));
    }

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var data = _storage.Load();
        data.Todos.Add(new TodoItem
        {
            Id = WidgetStorage.NewId(),
            Text = text.Trim(),
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        _storage.Save(data);
        Reload();
    }

    public void Toggle(long id, bool done)
    {
        var data = _storage.Load();
        var todo = data.Todos.FirstOrDefault(t => t.Id == id);
        if (todo is null || todo.Done == done) return;
        todo.Done = done;
        _storage.Save(data);
        Reload();
    }

    public void Delete(long id)
    {
        var data = _storage.Load();
        data.Todos.RemoveAll(t => t.Id == id);
        _storage.Save(data);
        Reload();
    }
}
