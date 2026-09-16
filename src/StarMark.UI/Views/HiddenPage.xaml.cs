#nullable enable
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

public sealed partial class HiddenPage : Page
{
    public HiddenPageViewModel ViewModel { get; }

    public HiddenPage()
    {
        InitializeComponent();
        ViewModel = (App.Services.GetService(typeof(HiddenPageViewModel)) as HiddenPageViewModel)
            ?? new HiddenPageViewModel(GetRepo());
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

    private async void Card_RestoreRequested(object? sender, ViewModels.ItemCardViewModel vm)
    {
        await ViewModel.RestoreCommand.ExecuteAsync(vm);
    }

    private void Card_TagFilterRequested(object? sender, (ViewModels.ItemCardViewModel VM, string Tag) e)
        => App.MainWindow?.NavigateTo("tags", e.Tag);

    private void Card_TagRemoveRequested(object? sender, (ViewModels.ItemCardViewModel VM, string Tag) e)
        => ItemCardActions.RemoveTag(this.XamlRoot, e.VM, e.Tag);

    private void Card_TagAddRequested(object? sender, ViewModels.ItemCardViewModel vm)
        => ItemCardActions.AddTag(this.XamlRoot, vm);
}