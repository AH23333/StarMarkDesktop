#nullable enable
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using StarMark.Abstractions;
using StarMark.Core.Search;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 差异化条目格宿主（Phase A-2，StarMark 护城河）：
/// 标签格 / 搜索结果格 / 最近活动格 / 置顶条目格，共用一个控件，按 <see cref="ItemGridMode"/> 决定查询策略。
/// 标签格与搜索结果格在组件内即可配置（钉标签 / 钉查询）并持久化到 widgets.json；
/// 活动格 / 置顶条目格直接查询统一 items 表，无需配置。
/// 抄 DeskBox 思路：内容只读查询、外壳由 WidgetWindow 承载。
/// </summary>
public sealed partial class ItemGridWidget : UserControl
{
    public ItemGridWidgetViewModel ViewModel { get; }

    public ItemGridWidget(ItemGridMode mode, WidgetWindow host)
    {
        var search = App.Services.GetService<SearchService>();
        ViewModel = new ItemGridWidgetViewModel(
            mode, host.Storage, host.Repository, host.InstanceId, host.Kind, search);

        InitializeComponent();

        // 配置栏可见性：仅标签格 / 搜索结果格需要。
        if (ViewModel.IsConfigurable)
        {
            ConfigBar.Visibility = Visibility.Visible;
            ConfigHint.Text = mode == ItemGridMode.Tag
                ? "输入要常驻桌面的标签名（如 rag、llm），回车或点「应用」。"
                : "输入要常驻桌面的查询词（可叠加标签），回车或点「应用」。";
            ConfigBox.PlaceholderText = mode == ItemGridMode.Tag ? "标签名，如 rag" : "查询词，如 stars>500";
            // 回填已钉内容：早先这里从不回填，重新打开组件时输入框是空的，
            // 即便后台仍按上次的查询/标签出结果，用户也会误以为「配置丢了 / 搜不到」。
            ConfigBox.Text = mode == ItemGridMode.Tag
                ? (ViewModel.GridTag ?? string.Empty)
                : (ViewModel.Query ?? string.Empty);
            if (mode == ItemGridMode.Search)
            {
                TagCloudPanel.Visibility = Visibility.Visible;
                SortRow.Visibility = Visibility.Visible;
                SortBox.SelectedIndex = ViewModel.SortIndex;   // 回填已钉排序；与默认相关度一致时不变、不触发重搜
                _ = ViewModel.LoadTagsAsync();
            }
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

        if (empty)
            EmptyHint.Text = isActivity
                ? "暂无活动记录。新增 / 删除 / 修改条目（含待办、随记、快捷入口、笔记、标签）后会显示在这里。"
                : ViewModel.NeedsConfig ? "先在上方配置要钉的内容" : "暂无条目";
    }

    private void ConfigBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) Apply_Click(sender, e);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var text = (ConfigBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (ViewModel.Mode == ItemGridMode.Tag)
            ViewModel.ApplyTagConfig(text);
        else
            ViewModel.ApplySearchConfig(text);
    }

    private void TagToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
            ViewModel.ToggleTag(name);
    }

    private static readonly string[] SortKeys = { "relevance", "recent", "name" };

    /// <summary>排序下拉切换：索引→排序键，交 ViewModel.ApplySort 落盘并重载（相关度为默认，与快捷搜索同源）。</summary>
    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = SortBox.SelectedIndex;
        if (idx < 0 || idx >= SortKeys.Length) return;
        ViewModel.ApplySort(SortKeys[idx]);
    }

    private void ClearTags_Click(object sender, RoutedEventArgs e) => ViewModel.ClearTags();

    private async void ResultOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ItemRowItem row }) await LauncherEx.OpenAsync(row.Uri);
    }

    /// <summary>右键（ContextRequested）：弹出与主窗口条目完全一致的 ContextFlyout（批次 M 的共享菜单工厂）。
    /// 对标签格 / 搜索结果格 / 置顶条目格的所有条目生效（最近活动格仅展示、不挂此处理器）。
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
            // 搜索结果格会合并 Everything 实时源（未入库、Id=0）；置顶/标签格为已入库行。
            // 传兜底条目后虚拟行也能弹菜单（Id=0 时 ShowForItem 的 GetByIdAsync 查不到 → 用行数据）。
            var fallback = new Item { Id = item.Id, Type = item.Type, Title = item.Title, Subtitle = item.Subtitle, Uri = item.Uri };
            ItemContextMenu.ShowForItem(item.Id, el, fallback);
        }
    }
}
