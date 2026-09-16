#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarMark.Core.Widgets;
using StarMark.UI.Services;
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
        BuildWidgetRows();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadFromStore();
        BuildWidgetRows();
    }

    private WidgetManager? WidgetManager()
        => App.Services.GetService(typeof(WidgetManager)) as WidgetManager;

    /// <summary>逐组件一行：开关（启用/停用）＋“显示”按钮（临时隐藏后找回）。</summary>
    private void BuildWidgetRows()
    {
        var mgr = WidgetManager();
        if (mgr is null || WidgetRows is null) return;

        WidgetRows.Children.Clear();
        foreach (var kind in WidgetStorage.AllKinds)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var toggle = new ToggleSwitch
            {
                Header = WidgetStorage.KindTitle(kind),
                OnContent = "已添加",
                OffContent = "未添加",
                IsOn = mgr.IsEnabled(kind),
                MinWidth = 0,
            };
            var captured = kind;
            toggle.Toggled += (_, _) =>
                _ = mgr.SetEnabledAsync(captured, toggle.IsOn);

            var showBtn = new Button
            {
                Content = "显示",
                Style = (Style)Application.Current.Resources["SecondaryButton"],
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            showBtn.Click += (_, _) => _ = mgr.ShowAsync(captured);

            Grid.SetColumn(toggle, 0);
            Grid.SetColumn(showBtn, 2);
            row.Children.Add(toggle);
            row.Children.Add(showBtn);
            WidgetRows.Children.Add(row);
        }
    }

    private void WidgetShowAll_Click(object sender, RoutedEventArgs e)
        => _ = WidgetManager()?.ShowAllAsync();

    private void WidgetHideAll_Click(object sender, RoutedEventArgs e)
        => _ = WidgetManager()?.HideAllAsync();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveCommand.Execute(null);
        // 托盘/热键即时生效
        App.MainWindow?.ApplyTraySettings();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
        => App.MainWindow?.NavigateTo("tree");
}
