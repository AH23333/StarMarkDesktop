#nullable enable
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 差异化条目格宿主（Phase A-2，StarMark 护城河）：
/// 标签格 / 剪贴板格 / 最近活动格 / 置顶条目格，共用一个控件，按 <see cref="ItemGridMode"/> 决定查询策略。
/// 标签格在组件内即可配置（钉标签）并持久化到 widgets.json；活动格 / 置顶格 / 剪贴板格直接查询统一 items 表，无需配置。
/// 抄 DeskBox 思路：内容只读查询、外壳由 WidgetWindow 承载。
/// </summary>
public sealed partial class ItemGridWidget : UserControl
{
    public ItemGridWidgetViewModel ViewModel { get; }

    public ItemGridWidget(ItemGridMode mode, WidgetWindow host)
    {
        ViewModel = new ItemGridWidgetViewModel(
            mode, host.Storage, host.Repository, host.InstanceId, host.Kind);

        InitializeComponent();

        // 配置栏：只有标签格有可钉的参数（一个标签名）。
        if (ViewModel.IsConfigurable)
        {
            ConfigBar.Visibility = Visibility.Visible;
            ConfigHint.Text = "输入要常驻桌面的标签名（如 rag、llm），回车或点「应用」。";
            ConfigBox.PlaceholderText = "标签名，如 rag";
            // 回填已钉内容：早先这里从不回填，重新打开组件时输入框是空的，
            // 即便后台仍按上次的标签出结果，用户也会误以为「配置丢了 / 搜不到」。
            ConfigBox.Text = ViewModel.GridTag ?? string.Empty;
        }

        // 最近活动格（#51）：只展示事件流，隐藏可点开/右键的条目列表；其余模式反之。
        if (mode == ItemGridMode.Activity)
        {
            ResultsRepeater.Visibility = Visibility.Collapsed;
            EventsRepeater.Visibility = Visibility.Visible;
        }

        ViewModel.Items.CollectionChanged += (_, _) => UpdateEmptyHint();
        ViewModel.Events.CollectionChanged += (_, _) => UpdateEmptyHint();
        UpdateEmptyHint();

        // 卸载即退订数据广播：组件会被反复创建/销毁，留着订阅会白跑数据库查询。
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    private void UpdateEmptyHint()
    {
        bool isActivity = ViewModel.Mode == ItemGridMode.Activity;
        var empty = isActivity ? ViewModel.Events.Count == 0 : ViewModel.Items.Count == 0;
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (isActivity)
            EventsRepeater.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        else
            ResultsRepeater.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        // 文案交给 ViewModel：剪贴板格的"为什么是空的"有四种，各自对应不同的下一步动作。
        if (empty) EmptyHint.Text = ViewModel.EmptyStateText;
    }

    private void ConfigBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) Apply_Click(sender, e);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var text = (ConfigBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text)) return;
        ViewModel.ApplyTagConfig(text);
    }

    /// <summary>
    /// 行数据 → 条目。<b>行是轻量记录、不带 Description</b>，所以按 Id 走 <see cref="ItemCardActions"/> 那条
    /// "现查库再动作"的路，正文/标签/置顶态才拿得全（剪贴板格的"再复制"尤其依赖正文）。
    /// </summary>
    private static Item RowAsItem(ItemRowItem row) => new()
    {
        Id = row.Id,
        Type = row.Type,
        Title = row.Title,
        Subtitle = row.Subtitle,
        Uri = row.Uri,
        Source = row.Source,
        SourceId = row.SourceId,
    };

    /// <summary>
    /// 点一行 = 该行的主操作。<b>刻意不直接 <c>LauncherEx.OpenAsync(row.Uri)</c></b>：
    /// 剪贴板条目（以及无链接的待办/随记）Uri 恒为空，那样点下去静默无事发生。
    /// 交给 <see cref="ItemCardActions.Open(XamlRoot, ItemCardViewModel)"/> 按条目类型分流——
    /// 剪贴板条目＝把正文再复制回剪贴板（含回声登记，不会被自己再记一条），
    /// 已入库条目＝按最新库值打开，未入库虚拟行（Everything，Id=0）＝按行 Uri 启动。
    /// </summary>
    private void ResultOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ItemRowItem row })
            ItemCardActions.Open(this.XamlRoot, new ItemCardViewModel(RowAsItem(row)));
    }

    /// <summary>拖出（CanDrag/DragStarting）：把该行本地文件/文件夹以存储项引用拖到桌面/资源管理器，网页则拖为快捷方式。只交引用不改动磁盘。</summary>
    private void Row_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is FrameworkElement { Tag: ItemRowItem row })
            ItemDragHelper.BeginFromUri(row.Uri, args);
    }

    /// <summary>右键（ContextRequested）：弹出与主窗口条目完全一致的 ContextFlyout（批次 M 的共享菜单工厂）。
    /// 对标签格 / 剪贴板格 / 置顶条目格的所有条目生效（最近活动格仅展示、不挂此处理器）。
    /// <para>
    /// 必须拦 <b>ContextRequested</b> 而非 RightTapped：WinUI 3 的 ContextFlyout 响应 ContextRequested 弹出，
    /// 组件级菜单挂在 <c>RootBorder.ContextFlyout</c>，普通 Button 行不消费该事件即冒泡命中组件菜单。
    /// 置 <c>args.Handled=true</c> 后与真实 ItemCard（自带 ContextFlyout）就近消费的行为一致。
    /// </para>
    /// <para>
    /// 行对象取自 <c>Tag="{x:Bind}"</c>（编译期绑定，可靠），不依赖 <c>DataContext</c>：ItemsRepeater 不保证把
    /// 数据项写入容器 DataContext（见 ItemCard.xaml 同类注释），依赖它取值不稳。
    /// </para></summary>
    private void Row_ContextRequested(object sender, ContextRequestedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: ItemRowItem item } el)
        {
            args.Handled = true;   // 阻止冒泡到 RootBorder.ContextFlyout（组件菜单）
            // 标签格等偶尔会遇到未入库的虚拟行（Id=0，来自 Everything 实时源）：
            // 传兜底条目后它也能弹菜单（Id=0 时 ShowForItem 的 GetByIdAsync 查不到 → 用行数据）。
            ItemContextMenu.ShowForItem(item.Id, el, RowAsItem(item));
        }
    }
}
