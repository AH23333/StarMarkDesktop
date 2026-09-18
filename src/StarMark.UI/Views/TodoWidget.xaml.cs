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
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 待办组件（R3 收尾）：XAML + ViewModel + ItemsRepeater。
/// 勾选 / 删除 / 新增只刷新 ViewModel 集合（增量更新），不再重建整棵组件 UI；
/// 数据契约不变（统一 items 表，source = local）。
/// <para>
/// 本轮对齐 DeskBox Todo 的高感知子集：**筛选分段带计数**、**颜色标记**、**截止日期**、**删除撤销条**。
/// 相比 DeskBox 未做：拖拽排序、子步骤、Markdown 备注、主/从双栏、7 段筛选 —— 这些对一个
/// 桌面小组件属于过度设计（DeskBox 的 Todo 光 UI 代码就有数千行）。
/// </para>
/// </summary>
public sealed partial class TodoWidget : UserControl
{
    public TodoWidgetViewModel ViewModel { get; }

    public TodoWidget(IItemRepository? repo, string instanceId)
    {
        ViewModel = new TodoWidgetViewModel(repo, instanceId);
        InitializeComponent();

        // VM 的三个计数/统计/撤销状态都由 INPC 推送 —— 这里手动同步 UI。
        // 不额外堆 Converter 的原因：Visiblity、Button 选中态这些是「多个控件联动」的视觉状态，
        // 用一处集中刷新比分散绑定更好维护，也避开 x:Bind 默认 OneTime 不订阅 INPC 的老坑。
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ViewModel.HasUndo) or nameof(ViewModel.UndoText)
                or nameof(ViewModel.AllCount) or nameof(ViewModel.ActiveCount)
                or nameof(ViewModel.CompletedCount) or nameof(ViewModel.SummaryText)
                or nameof(ViewModel.Filter))
            {
                SyncChrome();
            }
        };
        SyncChrome();
        Unloaded += (_, _) => ViewModel.DismissUndo();
    }

    private void SyncChrome()
    {
        FilterAllButton.Content = $"全部 {ViewModel.AllCount}";
        FilterActiveButton.Content = $"未完成 {ViewModel.ActiveCount}";
        FilterCompletedButton.Content = $"已完成 {ViewModel.CompletedCount}";

        ApplyHighlight(FilterAllButton, ViewModel.Filter == TodoFilter.All);
        ApplyHighlight(FilterActiveButton, ViewModel.Filter == TodoFilter.Active);
        ApplyHighlight(FilterCompletedButton, ViewModel.Filter == TodoFilter.Completed);

        SummaryBlock.Text = ViewModel.SummaryText;
        UndoBar.Visibility = ViewModel.HasUndo ? Visibility.Visible : Visibility.Collapsed;
        UndoTextBlock.Text = ViewModel.UndoText;
    }

    /// <summary>选中项的视觉强调：加粗 + 提高不透明度（未选中保持淡色）。</summary>
    private static void ApplyHighlight(Button b, bool selected)
    {
        b.FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold
                                : Microsoft.UI.Text.FontWeights.Normal;
        b.Opacity = selected ? 1.0 : 0.62;
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || string.IsNullOrWhiteSpace(InputBox.Text)) return;
        ViewModel.DismissUndo();   // 新操作作废上一次撤销机会（DeskBox 语义）
        _ = ViewModel.AddAsync(InputBox.Text);
        InputBox.Text = string.Empty;
    }

    private void TodoCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: long id } box)
            _ = ViewModel.ToggleAsync(id, box.IsChecked == true);
    }

    private void DeleteTodo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long id })
            _ = ViewModel.DeleteAsync(id);
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var v))
            ViewModel.Filter = (TodoFilter)v;
    }

    /// <summary>
    /// 颜色菜单：靠 MenuFlyoutItem 在父 MenuFlyout 中的**索引**反推颜色层级。
    /// 直接按 Text 比对会在改文案时失效，按索引又不直观 —— 故两者都不用：
    /// 菜单项顺序固定为 [无, 分隔线, 红, 橙, 黄, 绿, 蓝, 紫]，索引 → 颜色即下面的映射。
    /// </summary>
    private void TodoColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: long id } item) return;
        if (item.Parent is not MenuFlyout menu) return;
        var index = menu.Items.IndexOf(item);
        int color = index switch
        {
            0 => 0,          // 无
            2 => 1,          // 红
            3 => 2,          // 橙
            4 => 3,          // 黄
            5 => 4,          // 绿
            6 => 5,          // 蓝
            7 => 6,          // 紫
            _ => 0,
        };
        _ = ViewModel.SetColorAsync(id, color);
    }

    private async void Undo_Click(object sender, RoutedEventArgs e) => await ViewModel.UndoDeleteAsync();

    private void UndoDismiss_Click(object sender, RoutedEventArgs e) => ViewModel.DismissUndo();
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

/// <summary>
/// 待办颜色标记：index（0 = 无）→ 实心画笔。
/// 同时被当作透明度源复用（index=0 时返回 0 —— 无描边色块时不画，避免每行都顶一个灰点）。
/// </summary>
public sealed class TodoColorConverter : IValueConverter
{
    private static readonly Windows.UI.Color[] Colors =
    {
        Microsoft.UI.Colors.Red,         // 1 红：紧急
        Microsoft.UI.Colors.Orange,      // 2 橙：重要
        Microsoft.UI.Colors.Gold,        // 3 黄：提醒
        Microsoft.UI.Colors.Green,       // 4 绿：常规
        Microsoft.UI.Colors.DodgerBlue,  // 5 蓝：稍后
        Microsoft.UI.Colors.MediumPurple,// 6 紫：灵感
    };

    public object? Convert(object? value, Type? targetType, object? parameter, string? language)
    {
        var idx = value is int i ? i : 0;
        if (idx <= 0 || idx > Colors.Length)
        {
            // 无颜色：画笔透明（看不见）；透明度维度返回 0
            return targetType == typeof(double) ? (object)0.0 : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        var brush = new SolidColorBrush(Colors[idx - 1]);
        return targetType == typeof(double) ? (object)1.0 : brush;
    }

    public object? ConvertBack(object? value, Type? targetType, object? parameter, string? language)
        => throw new NotSupportedException();
}

/// <summary>空字符串 → Collapsed，有内容 → Visible（用于截止日期那一行，未设截止时不占位）。</summary>
public sealed class TextToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type? targetType, object? parameter, string? language)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object? ConvertBack(object? value, Type? targetType, object? parameter, string? language)
        => throw new NotSupportedException();
}
