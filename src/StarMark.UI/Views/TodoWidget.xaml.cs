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
/// 本轮对齐 DeskBox Todo 的高感知子集：**筛选分段带计数**、**颜色标记**、**截止日期**、
/// **删除撤销条**、**拖拽排序**（仅「全部」筛选下开放）。
/// 相比 DeskBox 未做：子步骤、Markdown 备注、主/从双栏、7 段筛选 —— 这些对一个
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

        // 只有"真的开始拖了"才在 DragItemsCompleted 里写盘：集合增删也可能派发该事件，
        // 不设闸门就会把每次增删都变成一轮全表写（卡顿来源）。
        TodoList.DragItemsStarting += (_, _) => ViewModel.BeginReorder();

        Unloaded += (_, _) =>
        {
            ViewModel.DismissUndo();
            ViewModel.Dispose();
        };
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
    /// 拖拽排序落地。ListView 的 CanReorderItems 已经把 <see cref="TodoWidgetViewModel.Visible"/>
    /// 调整成新顺序，这里只负责写盘。
    /// <para>
    /// 关键：不要在事件里同步再动集合。拖放刚结束时框架还在收尾容器状态，
    /// 此时同步增删 ObservableCollection 会撞上 UI 线程重入（本项目踩过：直接崩进程），
    /// 所以走异步（内部 await 后才回到 UI 线程改集合）。
    /// </para>
    /// </summary>
    private void TodoList_DragItemsCompleted(object sender, DragItemsCompletedEventArgs e)
        => _ = ViewModel.PersistVisibleOrderAsync();

    /// <summary>
    /// 颜色档位与文案：<b>只这一份</b>。0＝无（清掉标记），1～6 对应 <see cref="TodoColorConverter"/> 的调色板序号。
    /// </summary>
    private static readonly (int Tier, string Label)[] ColorTiers =
    {
        (0, "无"), (1, "红（紧急）"), (2, "橙（重要）"), (3, "黄（提醒）"),
        (4, "绿（常规）"), (5, "蓝（稍后）"), (6, "紫（灵感）"),
    };

    /// <summary>截止日档位与文案：null＝清除。<b>0＝今天、1＝明天</b>，都是"日历日"，不是 24 小时（✅P-37）。</summary>
    private static readonly (int? Offset, string Label)[] DueTiers =
    {
        (0, "今天"), (1, "明天"), (null, "清除"),
    };

    /// <summary>
    /// 点颜色那颗 → <b>现建</b>菜单，行 id 与档位都用捕获变量带进每一次点击。
    /// <para>
    /// 为什么不走 XAML 里那份 <c>Button.Flyout</c>：它要把 id 从被点的菜单项反查回来
    /// （<c>item.Tag</c> 或 <c>item.Parent</c>），而<b>这条反查在这台机器上从来没通过</b>——
    /// 库里 8 条待办没有一个 <c>color</c> 键，而同一模板里那颗<b>不套 flyout</b>、直接 <c>Tag="{x:Bind Id}"</c>
    /// 的 CheckBox 却写过 <c>done</c>（⇒ 模板元素上的 Tag 带得出 id，flyout 里那一层带不出/接不上）。
    /// 原来那两处 <c>return</c> 一声不出，所以它静默了一年多——现在认不到 id 会记一行 Warn（P-54 那一族）。
    /// </para>
    /// </summary>
    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRowId(sender, out var id)) return;
        var menu = new MenuFlyout();
        foreach (var (tier, label) in ColorTiers)
        {
            var item = new MenuFlyoutItem { Text = label };
            if (tier == 0) item.Icon = new FontIcon { Glyph = "\uE711", FontSize = 12 };
            menu.Items.Add(item);
            var chosen = tier;                       // 捕获：档位不再靠菜单项索引反推
            item.Click += (_, _) => _ = ViewModel.SetColorAsync(id, chosen);
        }
        menu.ShowAt((FrameworkElement)sender);
    }

    /// <summary>点日历那颗：今天／明天／清除，同一套形状（id 与偏移都是捕获变量）。</summary>
    private void DueButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRowId(sender, out var id)) return;
        var menu = new MenuFlyout();
        foreach (var (offset, label) in DueTiers)
        {
            var item = new MenuFlyoutItem { Text = label };
            menu.Items.Add(item);
            var chosen = offset;
            item.Click += (_, _) => _ = ViewModel.SetDueAsync(id, chosen);
        }
        menu.ShowAt((FrameworkElement)sender);
    }

    /// <summary>
    /// 行 id 只从<b>被点的那颗按钮</b>的 <c>Tag</c> 取——模板元素上的 <c>x:Bind</c> 是这条链里唯一被证过带得出 id 的一环。
    /// 认不到不静默：<b>记一行 Warn</b>，否则又是"点了没反应、日志一声不出"（那颗颜色点就这么哑了一年多）。
    /// </summary>
    private static bool TryRowId(object sender, out long id)
    {
        if (sender is FrameworkElement { Tag: long v }) { id = v; return true; }
        StarLog.Warn("[待办] 行内那颗按钮认不出这一行的 id（Tag 不是 long），这次点击没有落库");
        id = 0;
        return false;
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
