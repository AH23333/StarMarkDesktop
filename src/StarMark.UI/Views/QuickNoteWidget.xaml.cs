#nullable enable
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using StarMark.Core.Widgets;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 随记组件（R3 收尾）：XAML + ViewModel + ItemsRepeater，替代原
/// WidgetWindow.BuildQuickNote() 的手工构建。保存 / 删除只刷新列表集合，
/// 输入框内容保留（不再整树重建）；数据契约不变（widgets.json Notes）。
/// </summary>
public sealed partial class QuickNoteWidget : UserControl
{
    public QuickNoteWidgetViewModel ViewModel { get; }

    public QuickNoteWidget(WidgetStorage storage)
    {
        ViewModel = new QuickNoteWidgetViewModel(storage);
        InitializeComponent();
    }

    private void SaveNote_Click(object sender, RoutedEventArgs e) => Save();

    private void NoteBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl && e.Key == VirtualKey.Enter) Save();
    }

    private void Save()
    {
        if (ViewModel.Save(NoteBox.Text))
            NoteBox.Text = string.Empty;
    }

    private void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long id })
            ViewModel.Delete(id);
    }
}
