#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

    /// <summary>标签快速移除（点击芯片时触发）。</summary>
    public event EventHandler<(ItemCardViewModel VM, string Tag)>? TagRemoveRequested;

    /// <summary>标签快速添加（点击「＋」按钮时触发）。</summary>
    public event EventHandler<ItemCardViewModel>? TagAddRequested;

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
        if (ViewModel == null || XamlRoot == null) return;
        var vm = ViewModel;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = vm.Title.Length <= 40 ? vm.Title : vm.Title[..40] + "…",
            Content = new PreviewHost { ViewModel = vm },
            PrimaryButtonText = "打开",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
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

    private void Tag_Chip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && ViewModel != null)
            TagRemoveRequested?.Invoke(this, (ViewModel, tag));
    }

    private void Tag_Add_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) TagAddRequested?.Invoke(this, ViewModel);
    }
}