#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPageViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = (App.Services.GetService(typeof(SettingsPageViewModel)) as SettingsPageViewModel)
            ?? new SettingsPageViewModel();
        ViewModel.LoadFromStore();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadFromStore();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
        => ViewModel.SaveCommand.Execute(null);

    private void Back_Click(object sender, RoutedEventArgs e)
        => App.MainWindow?.NavigateTo("search");
}