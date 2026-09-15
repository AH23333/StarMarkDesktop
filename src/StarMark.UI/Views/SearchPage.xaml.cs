#nullable enable
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

public sealed partial class SearchPage : Page
{
    public SearchPageViewModel ViewModel { get; }

    public SearchPage()
    {
        InitializeComponent();
        ViewModel = (App.Services.GetService(typeof(SearchPageViewModel)) as SearchPageViewModel)
            ?? new SearchPageViewModel(App.Services.GetService(typeof(StarMark.Core.Search.SearchService))
                as StarMark.Core.Search.SearchService
                ?? throw new InvalidOperationException("SearchService 未注册"));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // 不再在此自动重跑搜索：进入搜索页时全局搜索框会立即设置 ViewModel.Query，
        // 由 OnQueryChanged 触发搜索。此前用单例 VM 里残留的旧 Query 重搜，
        // 正是「清空搜索栏后仍闪现上一条搜索”未找到“」的根因。
    }

    private async void Card_OpenRequested(object sender, long itemId)
        => ItemCardActions.Open(this.XamlRoot, itemId);

    private async void Card_EditNoteRequested(object sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.EditNote(this.XamlRoot, vm);

    private async void Card_EditTagsRequested(object sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.EditTags(this.XamlRoot, vm);

    private async void Card_HideRequested(object sender, ViewModels.ItemCardViewModel vm)
    {
        var wasHidden = vm.IsHidden;
        var nowHidden = await ItemCardActions.ToggleHidden(this.XamlRoot, vm);
        if (!wasHidden && nowHidden && !ViewModel.ShowHidden)
            ViewModel.RemoveItem(vm.Id);
    }

    private void Card_TagRemoveRequested(object sender, (ViewModels.ItemCardViewModel VM, string Tag) e)
        => ItemCardActions.RemoveTag(this.XamlRoot, e.VM, e.Tag);

    private void Card_TagAddRequested(object sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.AddTag(this.XamlRoot, vm);
}