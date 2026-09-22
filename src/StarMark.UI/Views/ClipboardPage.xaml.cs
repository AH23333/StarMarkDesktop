#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 「剪贴板历史」页：列出本机采集到的复制记录，点一条即再复制回剪贴板。
/// </summary>
public sealed partial class ClipboardPage : Page
{
    public ClipboardPageViewModel ViewModel { get; }

    // 新复制要在页面开着时就冒出来（用户是边复制边翻历史），所以订阅数据广播；
    // 去抖/最长合并窗口由 DataChangeReloader 统一管，页面只管"重载一次"。
    private DataChangeReloader? _reload;

    public ClipboardPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<ClipboardPageViewModel>();
        Unloaded += (_, _) => { _reload?.Dispose(); _reload = null; };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _reload ??= new DataChangeReloader(() => { ViewModel.LoadCommand.Execute(null); return System.Threading.Tasks.Task.CompletedTask; });
        ViewModel.LoadCommand.Execute(null);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _reload?.Dispose();
        _reload = null;
    }

    /// <summary>
    /// 点卡片 / "复制链接"菜单：剪贴板条目没有 URI，"打开"在这里就是"再复制一次"。
    /// 静默什么都不做是最糟的（用户以为坏了），所以成功与失败都要在状态行留一句。
    /// </summary>
    private async void Card_OpenRequested(object sender, long itemId)
    {
        if (sender is Controls.ItemCard { ViewModel: { } vm })
            await ViewModel.ReuseAsync(vm);
    }

    private async void Card_CopyRequested(object? sender, ItemCardViewModel vm) => await ViewModel.ReuseAsync(vm);

    private void Card_PinRequested(object? sender, ItemCardViewModel vm) => ItemCardActions.TogglePin(vm);
    // ToggleHidden 返回 Task<bool>（要等落库才知道隐藏/显示是否生效），其余是 async void 的即发即忘动作。
    private async void Card_HideRequested(object? sender, ItemCardViewModel vm) => await ItemCardActions.ToggleHidden(this.XamlRoot, vm);
    private void Card_EditNoteRequested(object? sender, ItemCardViewModel vm) => ItemCardActions.EditNote(this.XamlRoot, vm);
    private void Card_EditTagsRequested(object? sender, ItemCardViewModel vm) => ItemCardActions.EditTags(this.XamlRoot, vm);
    private void Card_TagAddRequested(object? sender, ItemCardViewModel vm) => ItemCardActions.AddTag(this.XamlRoot, vm);
    private void Card_TagRemoveRequested(object? sender, (ItemCardViewModel VM, string Tag) e) => ItemCardActions.RemoveTag(this.XamlRoot, e.VM, e.Tag);
    private void Card_TagFilterRequested(object? sender, (ItemCardViewModel VM, string Tag) e) => App.MainWindow?.NavigateTo("tags", e.Tag);

    private void Pause_Changed(object sender, RoutedEventArgs e)
    {
        // ToggleButton 的 IsTwoWay 已经翻过 ViewModel.Paused；这里只在"事件源于用户点击"时落动作。
        // 双向绑定回灌（进入页面时赋值）不会触发 Checked/Unchecked，故无需再判来源。
        ViewModel.Paused = PauseButton.IsChecked == true;
        App.SetClipboardPaused(PauseButton.IsChecked == true);
        ViewModel.StatusText = PauseButton.IsChecked == true
            ? "已暂停记录：期间不再写入新内容。要恢复请再点一次。"
            : "已恢复记录。";
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.Entries.Count;
        var confirmed = await Helpers.CenteredDialog.ConfirmAsync(
            "清空剪贴板历史",
            $"将删除本机记录的 {count} 条复制内容（含你置顶的条目），删除后无法恢复。书签 / Star / 待办 / 随记不受影响。",
            primaryText: "清空",
            cancelText: "取消",
            owner: App.MainWindow,
            dedupeKey: "clearclipboard");
        if (!confirmed) return;

        await ViewModel.ClearAllAsync();
    }
}
