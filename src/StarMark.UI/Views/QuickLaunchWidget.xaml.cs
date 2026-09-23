#nullable enable
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
/// 快捷启动（A-4）：复用 <see cref="ItemCard"/> 渲染用户自定义快捷入口（合成 Item，
/// <see cref="ItemCardViewModel.IsLauncherMode"/> ⇒ 只暴露打开/复制/预览/删除）。
/// 本组件只负责展示，不再内嵌搜索栏（搜索统一走快捷搜索组件 / 主窗搜索页）。
/// <para>
/// 置顶条目此前也渲染在这一格里，与「置顶条目」组件是同一批数据的两个入口 ⇒ 经用户裁决摘除（批次 IX），
/// 连带删掉只为置顶区服务的仓储注入与数据广播同步。
/// </para>
/// </summary>
public sealed partial class QuickLaunchWidget : UserControl
{
    public QuickLaunchWidgetViewModel ViewModel { get; }

    private readonly WidgetManager _manager;
    private readonly string _instanceId;

    public QuickLaunchWidget(WidgetStorage storage, WidgetManager manager, string instanceId)
    {
        _manager = manager;
        _instanceId = instanceId;
        ViewModel = new QuickLaunchWidgetViewModel(storage, instanceId);

        InitializeComponent();

        // 快捷入口变化（增删 / 外部拖入）只增量刷新 Links 集合，不重建整棵 UI。
        _manager.LinksChanged += OnLinksChanged;
        Unloaded += QuickLaunchWidget_Unloaded;

        // 入口来自本地存储（widgets.json），无异步数据源。
        ViewModel.ReloadLinks();
    }

    private void QuickLaunchWidget_Unloaded(object sender, RoutedEventArgs e)
    {
        _manager.LinksChanged -= OnLinksChanged;
        Unloaded -= QuickLaunchWidget_Unloaded;
    }

    private void OnLinksChanged(string _) => DispatcherQueue?.TryEnqueue(ViewModel.ReloadLinks);

    // ── ItemCard 事件路由（快捷入口）──

    private void Card_OpenRequested(object sender, long itemId)
    {
        // 快捷入口是合成条目，主库里没有对应 Item ⇒ 按 URI 打开（协议闸门在 LauncherEx 内）。
        if (sender is ItemCard { ViewModel: { } vm }) _ = LauncherEx.OpenAsync(vm.Uri);
    }

    private void Card_CopyLinkRequested(object sender, ItemCardViewModel vm)
        => ItemCardActions.CopyUri(vm);

    private void Card_OpenLocationRequested(object sender, ItemCardViewModel vm)
        => ItemCardActions.OpenLocation(vm);

    // 快捷入口删除：合成条目按 URI 从本组件移除（带外部居中确认弹窗）。
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

    // ── 快捷入口操作 ──

    private void ToggleAddForm_Click(object sender, RoutedEventArgs e)
    {
        AddForm.Visibility = AddForm.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (AddForm.Visibility == Visibility.Visible)
            AddUriBox.Focus(FocusState.Programmatic);
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
            var filePath = LocalFileIdentity.TryPathFromUri(parsed.AbsoluteUri, out var fp) ? fp : parsed.LocalPath;
            name = parsed.IsFile ? Path.GetFileName(filePath) : parsed.Host;
        }
        await _manager.AddLinkAsync(_instanceId, name, parsed.AbsoluteUri);
        AddNameBox.Text = AddUriBox.Text = string.Empty;
        AddForm.Visibility = Visibility.Collapsed;
    }
}
