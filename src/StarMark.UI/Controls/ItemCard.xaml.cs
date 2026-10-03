#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StarMark.Abstractions;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Controls;

/// <summary>
/// 可复用结果卡片用户控件。对应浏览器扩展 ResultCard。
/// 通过 ViewModel 属性 + 事件回调与外部交互。
/// </summary>
public sealed partial class ItemCard : UserControl
{
    public event EventHandler<long>? OpenRequested;
    public event EventHandler<ItemCardViewModel>? EditNoteRequested;
    public event EventHandler<ItemCardViewModel>? EditTagsRequested;
    public event EventHandler<ItemCardViewModel>? HideRequested;
    public event EventHandler<ItemCardViewModel>? PinRequested;

    /// <summary>标签快速移除（点击芯片 ✕ 时触发）。</summary>
    public event EventHandler<(ItemCardViewModel VM, string Tag)>? TagRemoveRequested;

    /// <summary>标签筛选（点击标签名称时触发，进入该标签全部条目）。</summary>
    public event EventHandler<(ItemCardViewModel VM, string Tag)>? TagFilterRequested;

    /// <summary>标签快速添加（点击「＋」按钮时触发）。</summary>
    public event EventHandler<ItemCardViewModel>? TagAddRequested;

    /// <summary>复制链接/路径（EverythingToolbar 式快捷操作）。</summary>
    public event EventHandler<ItemCardViewModel>? CopyLinkRequested;

    /// <summary>打开所在位置（仅本地文件；资源管理器定位）。</summary>
    public event EventHandler<ItemCardViewModel>? OpenLocationRequested;

    /// <summary>删除条目（快捷启动「快捷入口」场景使用，按 URI 移除）。</summary>
    public event EventHandler<ItemCardViewModel>? DeleteRequested;

    private ItemCardViewModel? _viewModel;
    public ItemCardViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            DataContext = value;
            Bindings.Update();
        }
    }

    /// <summary>
    /// 隐藏标签相关 UI（标签行 + 「＋ 标签」按钮）。快捷启动的「快捷入口」是合成条目、不入库，
    /// 不应提供标签能力，由宿主模板置 <c>True</c>。
    /// </summary>
    public static readonly DependencyProperty SuppressTagsProperty =
        DependencyProperty.Register(nameof(SuppressTags), typeof(bool), typeof(ItemCard), new PropertyMetadata(false));

    public bool SuppressTags
    {
        get => (bool)GetValue(SuppressTagsProperty);
        set => SetValue(SuppressTagsProperty, value);
    }

    public ItemCard() { InitializeComponent(); }

    private void Title_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel != null) OpenRequested?.Invoke(this, ViewModel.Id);
    }

    private void Menu_Open(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) OpenRequested?.Invoke(this, ViewModel.Id);
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var vm = ViewModel;
        var host = new PreviewHost { ViewModel = vm };
        // 统一走外部居中窗口（非 ContentDialog）：按用户主题着色、可拖动、不可重复。
        var result = await CenteredDialog.ShowContentAsync(
            ItemCardPolicy.TruncatedTitle(vm.Title),
            host, owner: App.MainWindow,
            dedupeKey: vm.PreviewDedupeKey, width: 820, height: 640,
            primaryText: vm.OpenMenuText, cancelText: "关闭");
        if (result == CenteredDialog.HostedDialogResult.Committed)
            OpenRequested?.Invoke(this, vm.Id);
    }

    private void EditNote_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) EditNoteRequested?.Invoke(this, ViewModel);
    }

    private void EditTags_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) EditTagsRequested?.Invoke(this, ViewModel);
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) HideRequested?.Invoke(this, ViewModel);
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) PinRequested?.Invoke(this, ViewModel);
    }

    /// <summary>发送到桌面“快捷启动”组件（自动启用并显示该组件，重复条目不重复添加）。</summary>
    private void SendToWidget_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || string.IsNullOrWhiteSpace(ViewModel.Uri)) return;
        var mgr = App.Services.GetRequiredService<WidgetManager>();
        if (mgr is not null) _ = mgr.AddLinkToQuickLaunchAsync(ViewModel.Title, ViewModel.Uri);
    }

    private void Menu_CopyLink(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) CopyLinkRequested?.Invoke(this, ViewModel);
    }

    /// <summary>
    /// 「复制图片」：交位图数据而不是路径（与上面那颗 CopyLink 是两件事）。
    /// <b>刻意自包含、不走页面事件</b>（同 <see cref="DeleteClipboard_Click"/>）：这一项要出现在剪贴板页、
    /// 文件夹树、搜索页与组件行等十余处，靠宿主逐个订阅就一定会有某一处"菜单里有、点了没反应"。
    /// </summary>
    private void Menu_CopyImage(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ItemCardActions.CopyImage(ViewModel);
    }

    /// <summary>
    /// 「贴到桌面」（ClipIMG-P3）：与「复制图片」同一族自包含出口——不走页面事件。
    /// 卡片被剪贴板页 / 文件夹树 / 搜索页 / 组件行十余处复用，靠宿主订阅必有某一处"菜单里有、点了没反应"
    /// （2d 那次就是出口只长在了一个入口上）；成功反馈由 PinManager 的通知卡统一说这里不重复。</summary>
    private void Menu_PinToDesktop(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ItemCardActions.PinImageToDesktop(ViewModel);
    }

    /// <summary>热榜行的 ⭐Star（图标按钮与右键菜单同一实现）。动作结果由宿主订阅 NoticeRaised 显示。</summary>
    private async void Star_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await TrendingItemActions.ToggleStarAsync(vm);
    }

    /// <summary>热榜行的 🔖收进收藏 / 移出收藏（本机书签，与 Star 是两个不同落点）。</summary>
    private async void Collect_Click(object sender, RoutedEventArgs e)
    {
        // 分流只在这一处（ItemCollectActions）：热榜的 🔖 与 RSS 的「收藏到文件夹」共用同一个按钮位
        if (ViewModel is { } vm) await ItemCollectActions.ToggleAsync(vm);
    }

    private void Menu_Star_Click(object sender, RoutedEventArgs e) => Star_Click(sender, e);
    private void Menu_Collect_Click(object sender, RoutedEventArgs e) => Collect_Click(sender, e);

    private void Menu_OpenLocation(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) OpenLocationRequested?.Invoke(this, ViewModel);
    }

    private void Tag_Chip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && ViewModel != null)
            TagFilterRequested?.Invoke(this, (ViewModel, tag));
    }

    private void Tag_Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && ViewModel != null)
            TagRemoveRequested?.Invoke(this, (ViewModel, tag));
    }

    private void Tag_Add_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) TagAddRequested?.Invoke(this, ViewModel);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) DeleteRequested?.Invoke(this, ViewModel);
    }

    /// <summary>
    /// 删除这一条。<b>一颗菜单项、两种落点</b>（批次 VQ）：剪贴板行删行并顺带删我们自己 <c>clip/</c> 目录里的附件；
    /// 本机文件行<b>只删行，绝不碰磁盘</b>——那个文件归用户（甚至在可移动盘上），这一行只是库里的一个索引。
    /// 分支写在宿主这一侧、判据（能不能出现）写在 <see cref="ItemCardPolicy"/> 那一侧，两处各自只有一个主人。
    /// </summary>
    private void DeleteClipboard_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (ViewModel.Type == StarMark.Abstractions.ItemType.File) ItemCardActions.DeleteFileRow(ViewModel.Id);
        else ItemCardActions.DeleteClipboard(ViewModel);
    }
}