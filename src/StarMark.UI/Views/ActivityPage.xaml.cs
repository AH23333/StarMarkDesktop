#nullable enable
using Microsoft.Extensions.DependencyInjection;
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarMark.Abstractions;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

public sealed partial class ActivityPage : Page
{
    public ActivityPageViewModel ViewModel { get; }

    public ActivityPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<ActivityPageViewModel>();
        ViewModel.LoadCommand.Execute(null);
    }

    private static IItemRepository GetRepo()
        => App.Services.GetRequiredService<IItemRepository>();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadCommand.Execute(null);
    }

    private async void Activity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string uri } || string.IsNullOrEmpty(uri)) return;
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
        }
        catch (Exception ex)
        {
            StarLog.Warn($"活动条目打开失败: {uri} ({ex.Message})");
        }
    }
}
