#nullable enable
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using StarMark.Core.Search;
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
        // 统一搜索编排来自 DI（与主窗口 SearchPage 同源）；拿不到时 VM 自动退回仓库直查。
        ViewModel = new SearchWidgetViewModel(repo, App.Services.GetService<SearchService>());
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

/// <summary>
/// 简单换行面板：WinUI 3 无内置 WrapPanel / WrapLayout（WMC0001），
/// ItemsRepeater 也不接受非虚拟化布局；标签云这类变宽条目的换行
/// 由本面板承担（配合 ItemsControl 的 ItemsPanel 使用）。
/// </summary>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(6.0));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(6.0));

    /// <summary>水平间距（同 行 相邻子项之间）。</summary>
    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    /// <summary>垂直间距（行与行之间）。</summary>
    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var wrapWidth = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        double x = 0, y = 0, lineH = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(wrapWidth, double.PositiveInfinity));
            var w = child.DesiredSize.Width;
            var h = child.DesiredSize.Height;

            if (x > 0 && x + HorizontalSpacing + w > wrapWidth)
            {
                // 当前行放不下 → 换行
                x = 0;
                y += lineH + VerticalSpacing;
                lineH = 0;
            }
            else if (x > 0)
            {
                x += HorizontalSpacing;
            }

            x += w;
            lineH = Math.Max(lineH, h);
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            y + lineH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineH = 0;

        foreach (var child in Children)
        {
            var w = child.DesiredSize.Width;
            var h = child.DesiredSize.Height;

            if (x > 0 && x + HorizontalSpacing + w > finalSize.Width)
            {
                x = 0;
                y += lineH + VerticalSpacing;
                lineH = 0;
            }
            else if (x > 0)
            {
                x += HorizontalSpacing;
            }

            child.Arrange(new Rect(x, y, w, h));
            x += w;
            lineH = Math.Max(lineH, h);
        }

        return finalSize;
    }
}
