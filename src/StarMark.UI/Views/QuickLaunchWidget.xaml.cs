#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 快捷启动格（R1 试点）：XAML + ViewModel + ItemsRepeater，替代原 code-behind
/// 手工 StackPanel 构建。置顶条目来自数据库、快捷入口来自 widgets.json，
/// 两者均为 ObservableCollection，配合 ItemsRepeater 实现 R3 增量更新——
/// 勾选 / 增删只改集合，不再重建整棵 UI 树。
/// </summary>
public sealed partial class QuickLaunchWidget : UserControl
{
    public QuickLaunchWidgetViewModel ViewModel { get; }

    private readonly WidgetManager _manager;

    public QuickLaunchWidget(WidgetStorage storage, IItemRepository? repo, WidgetManager manager)
    {
        _manager = manager;
        ViewModel = new QuickLaunchWidgetViewModel(storage, repo);

        InitializeComponent();

        // 快捷入口变化（增删 / 外部拖入）只增量刷新 Links 集合，不重建整棵 UI。
        _manager.LinksChanged += OnLinksChanged;
        Unloaded += QuickLaunchWidget_Unloaded;

        // 置顶条目来自数据库（异步），快捷入口来自本地存储；首屏一次性加载。
        _ = ViewModel.LoadAsync();
    }

    private void QuickLaunchWidget_Unloaded(object sender, RoutedEventArgs e)
    {
        _manager.LinksChanged -= OnLinksChanged;
        Unloaded -= QuickLaunchWidget_Unloaded;
    }

    private void OnLinksChanged() => DispatcherQueue?.TryEnqueue(ViewModel.ReloadLinks);

    // ── 置顶条目 ──

    private void RefreshPinned_Click(object sender, RoutedEventArgs e) => _ = ViewModel.ReloadPinnedAsync();

    private async void PinnedOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string uri }) await OpenUriAsync(uri);
    }

    // ── 快捷入口 ──

    private void ToggleAddForm_Click(object sender, RoutedEventArgs e)
    {
        AddForm.Visibility = AddForm.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (AddForm.Visibility == Visibility.Visible)
            AddUriBox.Focus(FocusState.Programmatic);
    }

    private async void LinkOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string uri }) await OpenUriAsync(uri);
    }

    private async void LinkRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string uri }) await _manager.RemoveLinkAsync(uri);
    }

    private void AddUriBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) _ = SubmitAddLinkAsync();
    }

    private async void AddLinkSubmit_Click(object sender, RoutedEventArgs e) => await SubmitAddLinkAsync();

    private async Task SubmitAddLinkAsync()
    {
        var raw = (AddUriBox.Text ?? string.Empty).Trim();
        if (!QuickLaunchWidgetViewModel.TryParseUri(raw, out var parsed) || parsed is null)
        {
            AddUriBox.Focus(FocusState.Programmatic);
            return;
        }
        var name = (AddNameBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
            name = parsed.IsFile ? Path.GetFileName(parsed.LocalPath) : parsed.Host;
        await _manager.AddLinkAsync(name, parsed.AbsoluteUri);
        AddNameBox.Text = AddUriBox.Text = string.Empty;
        AddForm.Visibility = Visibility.Collapsed;
    }

    private static async Task OpenUriAsync(string uri)
    {
        try
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
                await Launcher.LaunchUriAsync(parsed);
        }
        catch (Exception ex)
        {
            StarLog.Error($"打开快捷入口失败: {uri}", ex);
        }
    }
}
