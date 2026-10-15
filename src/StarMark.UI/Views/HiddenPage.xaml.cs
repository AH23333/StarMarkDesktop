#nullable enable
using Microsoft.Extensions.DependencyInjection;
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
        ViewModel = (App.Services.GetRequiredService<HiddenPageViewModel>())
            ?? new HiddenPageViewModel(GetRepo());
        // 空态那句由 VM 交「键＋实参」，本页只负责贴（换语言的重贴归 LocText 那份名单，⛔ 再造一位主人）。
        ViewModel.PropertyChanged += OnEmptyHintChanged;
        ApplyEmptyHint();
        ViewModel.LoadCommand.Execute(null);
    }

    private void OnEmptyHintChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HiddenPageViewModel.EmptyHint)) ApplyEmptyHint();
    }

    /// <summary>「没话了」也走这一颗门：SetMessage(… null) 把重贴名单那条一起摘掉，否则换语言把旧句子贴回来。</summary>
    private void ApplyEmptyHint() => LocText.SetMessage(EmptyHintText, ViewModel.EmptyHint);

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnEmptyHintChanged;
    }

    private static StarMark.Abstractions.IItemRepository GetRepo()
        => App.Services.GetRequiredService<StarMark.Abstractions.IItemRepository>();

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