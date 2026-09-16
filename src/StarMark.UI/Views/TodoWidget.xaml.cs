#nullable enable
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using StarMark.Core.Widgets;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 待办组件（R3 收尾）：XAML + ViewModel + ItemsRepeater，替代原
/// WidgetWindow.BuildTodo() 的手工构建。勾选 / 删除 / 新增只刷新
/// ViewModel.Todos 集合（增量更新），不再重建整棵组件 UI；数据契约不变（widgets.json Todos）。
/// </summary>
public sealed partial class TodoWidget : UserControl
{
    public TodoWidgetViewModel ViewModel { get; }

    public TodoWidget(WidgetStorage storage)
    {
        ViewModel = new TodoWidgetViewModel(storage);
        InitializeComponent();
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || string.IsNullOrWhiteSpace(InputBox.Text)) return;
        ViewModel.Add(InputBox.Text);
        InputBox.Text = string.Empty;
    }

    private void TodoCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: long id } box)
            ViewModel.Toggle(id, box.IsChecked == true);
    }

    private void DeleteTodo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long id })
            ViewModel.Delete(id);
    }
}

/// <summary>已完成待办的置灰透明度转换器（等价原 code-behind 的 0.45）。</summary>
public sealed class DoneOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type? targetType, object? parameter, string? language)
        => (value is bool done && done) ? 0.45 : 1.0;

    public object? ConvertBack(object? value, Type? targetType, object? parameter, string? language)
        => throw new NotSupportedException();
}

/// <summary>已完成待办的删除线转换器（等价原 code-behind 的 Strikethrough）。</summary>
public sealed class DoneDecorationConverter : IValueConverter
{
    public object? Convert(object? value, Type? targetType, object? parameter, string? language)
        => (value is bool done && done)
            ? Windows.UI.Text.TextDecorations.Strikethrough
            : Windows.UI.Text.TextDecorations.None;

    public object? ConvertBack(object? value, Type? targetType, object? parameter, string? language)
        => throw new NotSupportedException();
}
