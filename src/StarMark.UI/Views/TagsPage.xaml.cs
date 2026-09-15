#nullable enable
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
        ViewModel = (App.Services.GetService(typeof(TagsPageViewModel)) as TagsPageViewModel)
            ?? new TagsPageViewModel(GetRepo());
        ViewModel.LoadCommand.Execute(null);
    }

    private static StarMark.Abstractions.IItemRepository GetRepo()
        => App.Services.GetService(typeof(StarMark.Abstractions.IItemRepository))
            as StarMark.Abstractions.IItemRepository
            ?? throw new InvalidOperationException("IItemRepository 未注册");

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadCommand.Execute(null);
    }

    private async void Tag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagName)
            await ViewModel.ToggleTagCommand.ExecuteAsync(tagName);
    }

    private async void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ClearFiltersCommand.ExecuteAsync(null);
    }

    private void Card_OpenRequested(object sender, long itemId)
        => ItemCardActions.Open(this.XamlRoot, itemId);

    private void Card_EditNoteRequested(object sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.EditNote(this.XamlRoot, vm);

    private void Card_EditTagsRequested(object sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.EditTags(this.XamlRoot, vm);

    private void Card_HideRequested(object sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.ToggleHidden(this.XamlRoot, vm);
}