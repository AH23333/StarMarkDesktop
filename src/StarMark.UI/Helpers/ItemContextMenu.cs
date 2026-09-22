#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Helpers;

/// <summary>
/// 条目右键菜单的<b>单一真源构建器</b>（批次 M）：让「快捷搜索 / 搜索结果格 / 标签格 / 快捷启动」等
/// 条目型桌面组件的紧凑行，弹出与主窗口 <see cref="Controls.ItemCard"/> 逐条一致的 ContextFlyout。
/// <para>
/// 菜单结构与 <c>Controls/ItemCard.xaml</c> 的 ContextFlyout 保持一一对应（打开 / 打开所在位置 / 复制链接
/// / 预览 / 删除(仅启动器态) / 置顶 / 发送到桌面·快捷启动 / 编辑笔记 / 编辑标签 / 隐藏），动作全部走
/// <see cref="ItemCardActions"/>（已含 try/catch + 日志），保证行为、可见性规则（HasOpenLocation /
/// IsLauncherMode）与主窗口完全相同。
/// </para>
/// <para>
/// <b>为何按需构建、每次右击现取条目</b>：置顶/隐藏/标签态需实时准确，而组件行是轻量记录、非
/// <see cref="ItemCardViewModel"/>；这里用条目 Id 现查一条完整 <see cref="Item"/> 包成 VM 再建菜单，
/// 既拿到最新 Tags/Pinned/Hidden，又无需把组件行数据模型整体重构（保持组件紧凑密度、改动面最小）。
/// </para>
/// </summary>
internal static class ItemContextMenu
{
    /// <summary>
    /// 在 <paramref name="anchor"/> 处弹出与主窗口一致的条目右键菜单。
    /// <para>
    /// 优先按 Id 现查一条完整 <see cref="Item"/>，以取回最新的 Tags/Pinned/Hidden；
    /// 查不到时用 <paramref name="fallback"/>（组件行自身数据重建的条目）兜底——
    /// 这正是 <b>Everything 实时源虚拟条目</b>（未入库、Id=0）与「刚被删除的行」的场景：
    /// 主窗口 <see cref="Controls.ItemCard"/> 的 ContextFlyout 本就由内存 VM 直接构建、不查库，
    /// 若这里查不到就 return，会让组件行右键「什么都不弹」，与主窗口不一致。兜底后菜单照常出现，
    /// 按 URI 的动作（打开 / 复制链接 / 预览 / 打开所在位置 / 发送到快捷启动）全部可用。
    /// </para>
    /// </summary>
    public static async void ShowForItem(long itemId, FrameworkElement anchor, Item? fallback = null)
    {
        try
        {
            if (anchor.XamlRoot is not { } root) return;
            var item = await App.Services.GetRequiredItemRepository()
                .GetByIdAsync(itemId, CancellationToken.None)
                ?? fallback;   // 虚拟条目(Id=0)查不到 / 行已删：用行内数据兜底，保证与主窗口一致地弹出条目菜单
            if (item is null) return;
            Build(new ItemCardViewModel(item), root).ShowAt(anchor);
        }
        catch (Exception ex)
        {
            StarLog.Error($"构建条目右键菜单失败 (id={itemId})", ex);
        }
    }

    /// <summary>
    /// 按 <see cref="ItemCardViewModel"/> 状态构建右键菜单。<paramref name="root"/> 用于把弹窗按发起窗居中
    /// （与 <see cref="ItemCardActions"/> 内的 ResolveOwner 同源）。
    /// </summary>
    private static MenuFlyout Build(ItemCardViewModel vm, XamlRoot root)
    {
        var flyout = new MenuFlyout();

        flyout.Items.Add(Item("打开", (_, _) => OpenByRow(root, vm)));

        if (vm.HasOpenLocation)
            flyout.Items.Add(Item("打开所在位置", (_, _) => ItemCardActions.OpenLocation(vm)));

        flyout.Items.Add(Item("复制链接/路径", (_, _) => ItemCardActions.CopyUri(vm)));
        flyout.Items.Add(Item("预览", async (_, _) => await PreviewAsync(vm, root)));

        // 未入库的实时源虚拟条目（Everything 文件结果，Id=0）：显式「记录到本地」把路径登记为主库条目，
        // 之后便可在库中被检索、并被置顶/标签格持久化（这些操作自身也会按需自动登记，此处提供主动入口）。
        if (vm.Id == 0 && !vm.IsLauncherMode)
            flyout.Items.Add(Item("记录到本地", (_, _) => _ = RecordToLocalAsync(vm)));

        if (vm.IsLauncherMode)
        {
            // 启动器态（快捷启动的合成入口）：只保留按 URI 移除，隐藏一切会误写主库的操作。
            flyout.Items.Add(Item("删除", (_, _) => RemoveLauncherEntry(vm)));
            return flyout;
        }

        flyout.Items.Add(Item(vm.PinMenuText, (_, _) => ItemCardActions.TogglePin(vm)));
        flyout.Items.Add(Item("发送到桌面 · 快捷启动", (_, _) => _ = SendToQuickLaunchAsync(vm)));
        flyout.Items.Add(Item("编辑笔记", (_, _) => ItemCardActions.EditNote(root, vm)));
        flyout.Items.Add(Item("编辑标签", (_, _) => ItemCardActions.EditTags(root, vm)));
        flyout.Items.Add(Item(vm.HideMenuText, (_, _) => _ = ItemCardActions.ToggleHidden(root, vm)));
        return flyout;
    }

    private static MenuFlyoutItem Item(string text, RoutedEventHandler click)
    {
        var mi = new MenuFlyoutItem { Text = text };
        mi.Click += click;
        return mi;
    }

    /// <summary>
    /// 打开条目：已入库条目（Id&gt;0）沿用 <see cref="ItemCardActions.Open"/>（按最新库值打开）；
    /// 未入库的实时源虚拟条目（Everything，Id=0）库里查不到，直接按行 URI 打开，与左侧单击
    /// （<c>ResultOpen_Click</c> 走 <see cref="LauncherEx"/>）行为一致——否则右键「打开」对其静默失效。
    /// </summary>
    private static void OpenByRow(XamlRoot root, ItemCardViewModel vm)
    {
        if (vm.Id != 0) { ItemCardActions.Open(root, vm.Id); return; }
        _ = LauncherEx.OpenAsync(vm.Uri);
    }

    /// <summary>预览（QuickLook 式内嵌预览窗）：与主窗口 ItemCard.Preview_Click 同一宿主、同一提交行为。</summary>
    private static async Task PreviewAsync(ItemCardViewModel vm, XamlRoot root)
    {
        try
        {
            var host = new Controls.PreviewHost { ViewModel = vm };
            var title = vm.Title.Length <= 40 ? vm.Title : vm.Title[..40] + "…";
            var owner = WindowInterop.ResolveWindow(root, App.MainWindow);
            var result = await CenteredDialog.ShowContentAsync(
                title, host, owner: owner,
                dedupeKey: vm.PreviewDedupeKey, width: 820, height: 640,
                primaryText: "打开", cancelText: "关闭");
            if (result == CenteredDialog.HostedDialogResult.Committed)
                OpenByRow(root, vm);
        }
        catch (Exception ex)
        {
            StarLog.Error($"预览失败 (id={vm.Id})", ex);
        }
    }

    /// <summary>
    /// 「记录到本地」：把一条未入库的实时源虚拟条目（Everything，Id=0）按 (source, source_id) 幂等登记进主库，
    /// 使其成为可检索、可被置顶/标签格持久化的真实条目。<b>只写索引记录，绝不移动 / 改名 / 删除磁盘上的实际文件。</b>
    /// 登记成功再补记一条「新增」活动（用户主动动作，#51）；缺业务键（返回 0）时静默不写活动流。
    /// </summary>
    private static async Task RecordToLocalAsync(ItemCardViewModel vm)
    {
        if (vm.Id != 0) return;
        try
        {
            var item = vm.GetItem();
            if (await ItemCardActions.EnsureRecordedAsync(item) == 0) return;
            await ItemCardActions.GetRepo().LogActivityAsync(
                ActivityKind.ItemAdd, $"{item.Source}:{item.SourceId}", item.Title, item.Uri, CancellationToken.None);
        }
        catch (Exception ex)
        {
            StarLog.Error($"记录到本地失败 (uri={vm.Uri})", ex);
        }
    }

    private static async Task SendToQuickLaunchAsync(ItemCardViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Uri)) return;
        try
        {
            var mgr = App.Services.GetRequiredService<WidgetManager>();
            await mgr.AddLinkToQuickLaunchAsync(vm.Title, vm.Uri);
        }
        catch (Exception ex)
        {
            StarLog.Error($"发送到快捷启动失败 (id={vm.Id})", ex);
        }
    }

    /// <summary>启动器态「删除」：把这条自定义入口从组件配置移除（不触主库）。</summary>
    private static void RemoveLauncherEntry(ItemCardViewModel vm)
    {
        try
        {
            // 与主窗口 ItemCard 的 launcher-mode 删除一致：按 URI 移除；宿主组件订阅此事件完成移除。
            LauncherEntryRemoved?.Invoke(vm.Uri);
        }
        catch (Exception ex)
        {
            StarLog.Error($"删除快捷入口失败 (uri={vm.Uri})", ex);
        }
    }

    /// <summary>启动器态条目被「删除」时广播其 URI，由承载组件（快捷启动）订阅后从自身配置移除。</summary>
    public static event Action<string?>? LauncherEntryRemoved;
}
