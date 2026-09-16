#nullable enable
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 多标签搜索组件（R2 试点，顺带补齐「多标签 AND 搜索」桌面版）。
/// 关键词 + 多选标签（AND）联合过滤，结果内联展示，点击用 LauncherEx 打开。
/// </summary>
public sealed partial class SearchWidget : UserControl
{
    public SearchWidgetViewModel ViewModel { get; }

    public SearchWidget(IItemRepository? repo)
    {
        ViewModel = new SearchWidgetViewModel(repo);
        InitializeComponent();
        ViewModel.Results.CollectionChanged += (_, _) => UpdateEmptyHint();
        _ = ViewModel.LoadTagsAsync();
    }

    private void UpdateEmptyHint()
    {
        var empty = ViewModel.Results.Count == 0;
        if (EmptyHint is not null) EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (ResultsRepeater is not null) ResultsRepeater.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Query = SearchBox?.Text ?? string.Empty;
        await ViewModel.RunSearchAsync();
    }

    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.Query = SearchBox?.Text ?? string.Empty;
            await ViewModel.RunSearchAsync();
        }
    }

    private void TagToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
            ViewModel.ToggleTag(name);
    }

    private void ClearTags_Click(object sender, RoutedEventArgs e) => ViewModel.ClearTags();

    private async void ResultOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string uri }) await LauncherEx.OpenAsync(uri);
    }
}

/// <summary>标签选中态底色转换器：选中返回主题强调色（柔和），未选中透明。</summary>
public sealed class TagSelectedBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type? targetType, object? parameter, string? language)
    {
        var selected = value is bool b && b;
        if (selected && Application.Current.Resources.TryGetValue("AppAccentSoftBrush", out var brush))
            return brush;
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object? ConvertBack(object? value, Type? targetType, object? parameter, string? language)
        => throw new NotSupportedException();
}
