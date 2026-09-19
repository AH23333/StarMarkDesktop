#nullable enable
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
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
            if (mode == ItemGridMode.Search)
            {
                TagCloudPanel.Visibility = Visibility.Visible;
                _ = ViewModel.LoadTagsAsync();
            }
        }

        ViewModel.Items.CollectionChanged += (_, _) => UpdateEmptyHint();
        UpdateEmptyHint();

        // 卸载即退订数据广播：组件会被反复创建/销毁，留着订阅会白跑数据库查询。
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    private void UpdateEmptyHint()
    {
        var empty = ViewModel.Items.Count == 0;
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ResultsRepeater.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (empty)
            EmptyHint.Text = ViewModel.NeedsConfig ? "先在上方配置要钉的内容" : "暂无条目";
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

    private void ClearTags_Click(object sender, RoutedEventArgs e) => ViewModel.ClearTags();

    private async void ResultOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string uri }) await LauncherEx.OpenAsync(uri);
    }
}
