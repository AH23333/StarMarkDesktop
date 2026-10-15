#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

public sealed partial class TagsPage : Page
{
    public TagsPageViewModel ViewModel { get; }

    public TagsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<TagsPageViewModel>();
        // 空态那句由 VM 交「键＋实参」，本页只负责贴（换语言的重贴归 LocText 那份名单，⛔ 再造一位主人）。
        ViewModel.PropertyChanged += OnEmptyHintChanged;
        ApplyEmptyHint();
        ViewModel.LoadCommand.Execute(null);
        // 主题切换后重载标签云，让按主题明度计算的标签颜色随之刷新
        ActualThemeChanged += (_, _) => ViewModel.LoadCommand.Execute(null);
    }

    private void OnEmptyHintChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagsPageViewModel.EmptyHint)) ApplyEmptyHint();
    }

    /// <summary>「没话了」也走这一颗门：SetMessage(… null) 把重贴名单那条一起摘掉，否则换语言把旧句子贴回来。</summary>
    private void ApplyEmptyHint() => LocText.SetMessage(EmptyHintText, ViewModel.EmptyHint);

    /// <summary>那颗 VM 是<b>单例</b>而本页每次导航新建：挂着不摘＝每进一次本页多留一份订阅（坑表 #386 那一族）。</summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnEmptyHintChanged;
    }

    private static StarMark.Abstractions.IItemRepository GetRepo()
        => App.Services.GetRequiredService<StarMark.Abstractions.IItemRepository>();

    private static StarMark.UI.ViewModels.MainViewModel GetMain()
        => App.Services.GetRequiredService<StarMark.UI.ViewModels.MainViewModel>();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadCommand.Execute(null);
        if (e.Parameter is string tag && !string.IsNullOrWhiteSpace(tag))
        {
            _ = ViewModel.FilterByTagAsync(tag);
        }
    }

    private void Tag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagName)
            _ = ViewModel.ToggleTagCommand.ExecuteAsync(tagName);
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.ClearFiltersCommand.ExecuteAsync(null);
    }

    private void RemoveFilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tagName })
            _ = ViewModel.ToggleTagCommand.ExecuteAsync(tagName);
    }

    private void Card_OpenRequested(object sender, long itemId)
    {
        // 与主窗一致：本地文件虚拟条目（Id=0）须按视图模型 Uri 打开。
        if (sender is Controls.ItemCard { ViewModel: { } vm }) { ItemCardActions.Open(this.XamlRoot, vm); return; }
        ItemCardActions.Open(this.XamlRoot, itemId);
    }

    private void Card_EditNoteRequested(object? sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.EditNote(this.XamlRoot, vm);

    private void Card_EditTagsRequested(object? sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.EditTags(this.XamlRoot, vm);

    private async void Card_HideRequested(object? sender, ViewModels.ItemCardViewModel vm)
    {
        await ItemCardActions.ToggleHidden(this.XamlRoot, vm);
    }

    private void Card_PinRequested(object? sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.TogglePin(vm);

    private void Card_CopyLinkRequested(object? sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.CopyUri(vm);

    private void Card_OpenLocationRequested(object? sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.OpenLocation(vm);

    private void Card_TagFilterRequested(object? sender, (ViewModels.ItemCardViewModel VM, string Tag) e)
        => App.MainWindow?.NavigateTo("tags", e.Tag);

    private void Card_TagRemoveRequested(object? sender, (ViewModels.ItemCardViewModel VM, string Tag) e)
    {
        ItemCardActions.RemoveTag(this.XamlRoot, e.VM, e.Tag);
        ViewModel.LoadCommand.Execute(null);
    }

    private void Card_TagAddRequested(object? sender, ViewModels.ItemCardViewModel vm)
    {
        ItemCardActions.AddTag(this.XamlRoot, vm);
        ViewModel.LoadCommand.Execute(null);
    }
}