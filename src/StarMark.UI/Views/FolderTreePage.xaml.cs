#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

public sealed partial class FolderTreePage : Page
{
    public FolderTreePageViewModel ViewModel { get; }

    public FolderTreePage()
    {
        InitializeComponent();
        ViewModel = (App.Services.GetService(typeof(FolderTreePageViewModel)) as FolderTreePageViewModel)
            ?? new FolderTreePageViewModel(GetRepo());
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

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel == null || SortCombo.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is string tag) ViewModel.CurrentSort = tag;
    }

    private void TreeSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            ViewModel.CurrentSource = tag;
            var accent = (SolidColorBrush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
            var muted = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            foreach (var b in new[] { TreeSourceAll, TreeSourceStar, TreeSourceBookmark })
                b.Foreground = muted;
            btn.Foreground = accent;
        }
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