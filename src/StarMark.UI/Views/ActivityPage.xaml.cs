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
        // 空态那句由 VM 交「键＋实参」，本页只负责贴（换语言的重贴归 LocText 那份名单，⛔ 再造一位主人）。
        ViewModel.PropertyChanged += OnEmptyHintChanged;
        ApplyEmptyHint();
        ViewModel.LoadCommand.Execute(null);
    }

    private void OnEmptyHintChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActivityPageViewModel.EmptyHint)) ApplyEmptyHint();
    }

    /// <summary>「没话了」也走这一颗门：SetMessage(… null) 把重贴名单那条一起摘掉，否则换语言把旧句子贴回来。</summary>
    private void ApplyEmptyHint() => LocText.SetMessage(EmptyHintText, ViewModel.EmptyHint);

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnEmptyHintChanged;
    }

    private static IItemRepository GetRepo()
        => App.Services.GetRequiredService<IItemRepository>();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadCommand.Execute(null);
    }

    private void Activity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string uri } || string.IsNullOrEmpty(uri)) return;
        // 批次 VR：以前这里 await 完就把回报丢了——活动流里点一条打不开的记录，界面上一个字都不出现。
        // 异常兜底与"为什么打不开"都在 LauncherEx／ItemCardActions 那一侧统一说，这里不再各写一份 try。
        StarMark.UI.Helpers.ItemCardActions.OpenUriAndReport(uri);
    }
}
