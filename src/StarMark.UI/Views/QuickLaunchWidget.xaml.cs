#nullable enable
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Controls;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 快捷启动格（A-4）：复用 <see cref="ItemCard"/>（右键菜单/标签/预览/发送到桌面，视觉与主窗一致）
/// 渲染置顶条目与自定义快捷入口。本组件只负责展示，不再内嵌搜索栏（搜索统一走快捷搜索组件 / 主窗搜索页）。
/// 置顶条目走真实 Item，暴露全部操作；自定义快捷入口是合成 Item（<see cref="ItemCardViewModel.IsLauncherMode"/>）
/// 只暴露打开/复制/预览。
/// </summary>
public sealed partial class QuickLaunchWidget : UserControl
{
    public QuickLaunchWidgetViewModel ViewModel { get; }

    private readonly WidgetManager _manager;
    private readonly string _instanceId;

    public QuickLaunchWidget(WidgetStorage storage, IItemRepository? repo, WidgetManager manager, string instanceId)
    {
        _manager = manager;
        _instanceId = instanceId;
        ViewModel = new QuickLaunchWidgetViewModel(storage, repo, instanceId);

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
        ViewModel.Dispose();   // 退订数据广播
    }

    private void OnLinksChanged(string _) => DispatcherQueue?.TryEnqueue(ViewModel.ReloadLinks);

    // ── ItemCard 事件路由（置顶 / 快捷入口共用 CardTemplate）──

    private void Card_OpenRequested(object sender, long itemId)
    {
        // 启动器模式的合成条目（自定义快捷入口）直接按 URI 打开，不走主库。
        if (sender is ItemCard { ViewModel: { IsLauncherMode: true } vm })
            _ = LauncherEx.OpenAsync(vm.Uri);
        else
            ItemCardActions.Open(this.XamlRoot, itemId);
    }

    private void Card_EditNoteRequested(object sender, ItemCardViewModel vm)
        => ItemCardActions.EditNote(this.XamlRoot, vm);

    private void Card_EditTagsRequested(object sender, ItemCardViewModel vm)
        => ItemCardActions.EditTags(this.XamlRoot, vm);

    private async void Card_HideRequested(object sender, ItemCardViewModel vm)
        => await ItemCardActions.ToggleHidden(this.XamlRoot, vm);

    private async void Card_PinRequested(object sender, ItemCardViewModel vm)
    {
        ItemCardActions.TogglePin(vm);
        // 置顶态变化后刷新置顶集合（取消置顶即从本组件移除，新置顶立即出现）。
        await ViewModel.ReloadPinnedAsync();
    }

    private void Card_CopyLinkRequested(object sender, ItemCardViewModel vm)
        => ItemCardActions.CopyUri(vm);

    private void Card_OpenLocationRequested(object sender, ItemCardViewModel vm)
        => ItemCardActions.OpenLocation(vm);

    private void Card_TagFilterRequested(object sender, (ItemCardViewModel VM, string Tag) e)
    {
        var main = App.Services.GetRequiredService<MainViewModel>();
        main?.ToggleGlobalTagFilter(e.Tag);
    }

    private void Card_TagRemoveRequested(object sender, (ItemCardViewModel VM, string Tag) e)
        => ItemCardActions.RemoveTag(this.XamlRoot, e.VM, e.Tag);

    private void Card_TagAddRequested(object sender, ItemCardViewModel vm)
        => ItemCardActions.AddTag(this.XamlRoot, vm);

    // 快捷启动「快捷入口」删除：合成条目按 URI 从本组件移除（带外部居中确认弹窗）。
    private async void Card_DeleteRequested(object sender, ItemCardViewModel vm)
    {
        if (vm is not { IsLauncherMode: true }) return;
        var ok = await CenteredDialog.ConfirmAsync(
            "删除快捷入口",
            $"确定删除快捷入口「{vm.Title}」？此操作不可撤销。",
            primaryText: "删除", cancelText: "取消",
            owner: App.MainWindow, dedupeKey: $"deletelink:{vm.Uri}");
        if (ok) await _manager.RemoveLinkAsync(_instanceId, vm.Uri);
    }

    // ── 置顶条目操作 ──

    private void RefreshPinned_Click(object sender, RoutedEventArgs e) => _ = ViewModel.ReloadPinnedAsync();

    // ── 快捷入口操作 ──

    private void ToggleAddForm_Click(object sender, RoutedEventArgs e)
    {
        AddForm.Visibility = AddForm.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (AddForm.Visibility == Visibility.Visible)
            AddUriBox.Focus(FocusState.Programmatic);
    }

    private async void LinkRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string uri }) await _manager.RemoveLinkAsync(_instanceId, uri);
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
        {
            // 含 '#' 的本地文件名默认标题：TryPathFromUri 保留 '#'（LocalPath 会截断成 "C"）。
            var filePath = StarMark.Abstractions.LocalFileIdentity.TryPathFromUri(parsed.AbsoluteUri, out var fp) ? fp : parsed.LocalPath;
            name = parsed.IsFile ? Path.GetFileName(filePath) : parsed.Host;
        }
        await _manager.AddLinkAsync(_instanceId, name, parsed.AbsoluteUri);
        AddNameBox.Text = AddUriBox.Text = string.Empty;
        AddForm.Visibility = Visibility.Collapsed;
    }
}
