#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
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
    private readonly Microsoft.UI.Xaml.Window _host;

    public QuickLaunchWidget(WidgetStorage storage, WidgetManager manager, string instanceId,
        Microsoft.UI.Xaml.Window host)
    {
        _manager = manager;
        _instanceId = instanceId;
        _host = host;         // 系统选择器必须绑到发起它的那扇窗（否则对话框开不出来）
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

    /// <summary>
    /// 「选择文件…」：一次挑多个，落进与拖放<b>同一对批处理出口</b>。
    /// <para>存在理由不是"多一份入口好看"：程序以管理员身份运行时（开着「本地磁盘搜索」就会提权去配
    /// 提权运行的 Everything），Windows 的 UIPI 会<b>按完整性级别把从资源管理器拖进来的那整条消息流拦掉</b>——
    /// 不报错、不提示、组件看起来"就是不收"。系统选择器跑在本进程的对话框里，与权限等级无关，
    /// 所以"加本地文件"永远有一条点得到的路。</para>
    /// </summary>
    private async void PickFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowInterop.GetHwnd(_host));
        picker.FileTypeFilter.Add("*");            // 快捷入口要能指到任意类型的文件，限死扩展名＝挑不到就是没做
        var files = await picker.PickMultipleFilesAsync();
        if (files is not { Count: > 0 }) return;
        await AddPickedAsync(files.Select(f => (f.Name, f.Path)).ToList());
    }

    /// <summary>「选择文件夹…」：与文件那条同一条落库路（拖放本来就支持文件夹，缺它就更不像话）。</summary>
    private async void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowInterop.GetHwnd(_host));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        await AddPickedAsync(new[] { (folder.Name, folder.Path) });
    }

    /// <summary>
    /// 两条选择器出口与拖放出口<b>共用的那一次落库</b>（<see cref="WidgetManager.AddPathsToLauncherAsync"/>）：
    /// 整批登记进主库 + 整批加进本组件，绝不逐条（逐条＝每项重读重写一次 <c>widgets.json</c> 并各开一次库，批次 PA-6）。
    /// </summary>
    private async Task AddPickedAsync(IReadOnlyList<(string Title, string Path)> picked)
    {
        await _manager.AddPathsToLauncherAsync(_instanceId,
            picked.Select(p => ((string?)p.Title, p.Path)).ToList());
        AddForm.Visibility = Visibility.Collapsed;
    }
}
