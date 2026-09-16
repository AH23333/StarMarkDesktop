#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using StarMark.Core.Widgets;

namespace StarMark.UI.ViewModels;

/// <summary>随记组件中的一条（数据来自 widgets.json 的 Notes 列表）。</summary>
public sealed record QuickNoteRow(long Id, string Text);

/// <summary>
/// 随记组件 ViewModel（R3 收尾）：保存 / 删除只刷新列表集合（与原实现一致：
/// 最新在前、最多展示 30 条），不再由 code-behind 重建整棵组件 UI。
/// 数据契约不变（WidgetStorage.Notes）。
/// </summary>
public sealed class QuickNoteWidgetViewModel
{
    /// <summary>与原 code-behind 一致：列表最多展示 30 条（存储层 Normalize 仍保留 100 条）。</summary>
    private const int DisplayLimit = 30;

    private readonly WidgetStorage _storage;

    public ObservableCollection<QuickNoteRow> Notes { get; } = new();

    public QuickNoteWidgetViewModel(WidgetStorage storage)
    {
        _storage = storage;
        Reload();
    }

    public void Reload()
    {
        Notes.Clear();
        foreach (var note in _storage.Load().Notes.Take(DisplayLimit))
            Notes.Add(new QuickNoteRow(note.Id, note.Text));
    }

    /// <summary>保存一条随记（空文本忽略）；返回是否保存成功。</summary>
    public bool Save(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var data = _storage.Load();
        data.Notes.Insert(0, new QuickNoteItem
        {
            Id = WidgetStorage.NewId(),
            Text = text.Trim(),
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        _storage.Save(data);
        Reload();
        return true;
    }

    public void Delete(long id)
    {
        var data = _storage.Load();
        data.Notes.RemoveAll(n => n.Id == id);
        _storage.Save(data);
        Reload();
    }
}
