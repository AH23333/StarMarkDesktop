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
        ViewModel.SearchCompleted += UpdateEmptyHint;
        _ = ViewModel.LoadTagsAsync();
        // 打开组件即展示最近条目（空态浏览），而不是先给用户一页「无结果」。
        _ = ViewModel.RunSearchAsync();
    }

    private void UpdateEmptyHint()
    {
        var empty = ViewModel.Results.Count == 0;
        if (EmptyHint is not null)
        {
            EmptyHint.Text = ViewModel.EmptyHint;
            EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ResultsRepeater is not null) ResultsRepeater.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Query = SearchBox?.Text ?? string.Empty;
        await ViewModel.RunSearchAsync();
    }

    /// <summary>排序下拉：索引→排序键，切换即重搜（浏览态空词也走同一入口）。</summary>
    private static readonly string[] SortKeys = { "relevance", "recent", "name" };

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortBox is null) return;      // 初值 SelectedIndex=0 在 InitializeComponent 期触发，ViewModel 尚未就绪
        var idx = SortBox.SelectedIndex;
        ViewModel.Sort = idx >= 0 && idx < SortKeys.Length ? SortKeys[idx] : "relevance";
        _ = ViewModel.RunSearchAsync();
    }

    /// <summary>搜索即输入：边打边搜（去抖），对照 DeskBox 弹窗引擎；回车/按钮仍立即搜。</summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.Query = SearchBox?.Text ?? string.Empty;
        _ = ViewModel.SearchDebouncedAsync();
    }

    /// <summary>↑↓ 移动选中并滚入视野；回车打开当前选中项（无选中则立即搜）。对照 DeskBox 键盘导航。</summary>
    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Up:
                ViewModel.MoveSelection(-1);
                BringSelectedIntoView();
                e.Handled = true;
                break;
            case VirtualKey.Down:
                ViewModel.MoveSelection(1);
                BringSelectedIntoView();
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                if (ViewModel.Selected is { } sel)
                {
                    await LauncherEx.OpenAsync(sel.Uri);
                }
                else
                {
                    ViewModel.Query = SearchBox?.Text ?? string.Empty;
                    await ViewModel.RunSearchAsync();
                }
                e.Handled = true;
                break;
        }
    }

    /// <summary>把选中行滚入视野（ItemsRepeater 容器按需创建；失败静默，不影响选中态）。</summary>
    private void BringSelectedIntoView()
    {
        var index = ViewModel.SelectedIndex;
        if (index < 0) return;
        try
        {
            if (ResultsRepeater.GetOrCreateElement(index) is UIElement el)
                el.StartBringIntoView();
        }
        catch (Exception ex)
        {
            StarMark.Abstractions.StarLog.Error("搜索组件键盘导航滚动失败", ex);
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
        if (sender is Button { Tag: SearchResultItem row }) await LauncherEx.OpenAsync(row.Uri);
    }

    /// <summary>右键（ContextRequested）：弹出与主窗口条目完全一致的 ContextFlyout（批次 M 的共享菜单工厂）。
    /// <para>
    /// 必须拦 <b>ContextRequested</b> 而非 RightTapped：WidgetWindow 把组件级菜单挂在
    /// <c>RootBorder.ContextFlyout</c>，而 WinUI 3 的 ContextFlyout 是响应 <b>ContextRequested</b> 路由事件弹出的。
    /// 本行是普通 Button、无自身 ContextFlyout，若不在此消费事件，ContextRequested 冒泡到 RootBorder 即弹组件菜单。
    /// 置 <c>args.Handled=true</c> 后事件不再上溯。
    /// </para>
    /// <para>
    /// 行对象取自 <c>Tag="{x:Bind}"</c>（编译期绑定，可靠）而非 <c>DataContext</c>：本项目 <c>ItemCard.xaml</c> 已注明
    /// ItemsRepeater 不保证把数据项写入容器 <c>DataContext</c>，依赖它取值不稳。
    /// </para></summary>
    private void Row_ContextRequested(object sender, ContextRequestedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: SearchResultItem item } el)
        {
            args.Handled = true;   // 阻止冒泡到 RootBorder.ContextFlyout（组件菜单）
            // 传入由行数据重建的兜底条目：Everything 实时源结果未入库、Id=0，GetByIdAsync 查不到，
            // 若无兜底则菜单直接不弹（此前右键"什么都不出现"的真因）。与主窗口用内存 VM 建菜单同源。
            var fallback = new Item { Id = item.Id, Type = item.Type, Title = item.Title, Subtitle = item.Subtitle, Uri = item.Uri };
            ItemContextMenu.ShowForItem(item.Id, el, fallback);
        }
    }
}

/// <summary>标签选中态底色转换器：选中返回主题强调色（柔和），未选中透明。</summary>
public sealed class TagSelectedBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type? targetType, object? parameter, string? language)
    {
        var selected = value is bool b && b;
        // 主题感知解析（§3.1）：组件窗口主题与主窗同步，按系统暗色判定取 ThemeDictionaries
        if (selected && ThemeBrush.Resolve(ThemeManager.IsAppDark(), "AppAccentSoftBrush") is { } brush)
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
