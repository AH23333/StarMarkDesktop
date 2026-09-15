#nullable enable
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using StarMark.UI.Controls;
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
        ViewModel.RootsReady += OnRootsReady;
    }

    private static StarMark.Abstractions.IItemRepository GetRepo()
        => App.Services.GetService(typeof(StarMark.Abstractions.IItemRepository))
            as StarMark.Abstractions.IItemRepository
            ?? throw new InvalidOperationException("IItemRepository 未注册");

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadCommand.ExecuteAsync(null);
    }

    // ===== TreeView 构建（TreeViewNode 层级，惰性挂载条目） =====

    private void OnRootsReady() => RebuildTree();

    private void RebuildTree()
    {
        FolderTree.RootNodes.Clear();
        foreach (var root in ViewModel.Roots)
            FolderTree.RootNodes.Add(CreateFolderNode(root));
    }

    private static TreeViewNode CreateFolderNode(FolderPathNodeViewModel node)
    {
        var header = BuildHeader(node);
        header.Tag = node; // 通过 Tag 关联语义节点，供 Expanding 惰性挂载
        var treeNode = new TreeViewNode { Content = header };
        foreach (var child in node.Children)
            treeNode.Children.Add(CreateFolderNode(child));
        return treeNode;
    }

    private static StackPanel BuildHeader(FolderPathNodeViewModel node)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(new TextBlock { Text = "📁", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(new TextBlock
        {
            Text = node.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 360,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        header.Children.Add(new TextBlock
        {
            Text = $"({node.TotalCount})",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        return header;
    }

    private void FolderTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is not TreeViewNode node) return;
        if (node.Content is not StackPanel header || header.Tag is not FolderPathNodeViewModel folder) return;
        // 只挂载一次
        if (node.Children.Any(c => c.Content is ItemCard)) return;

        foreach (var vm in ViewModel.Hydrate(folder))
        {
            var card = CreateCard(vm);
            node.Children.Add(new TreeViewNode { Content = card });
        }
    }

    private ItemCard CreateCard(ItemCardViewModel vm)
    {
        var card = new ItemCard { ViewModel = vm };
        card.OpenRequested += Card_OpenRequested;
        card.EditNoteRequested += Card_EditNoteRequested;
        card.EditTagsRequested += Card_EditTagsRequested;
        card.HideRequested += Card_HideRequested;
        card.TagRemoveRequested += Card_TagRemoveRequested;
        card.TagAddRequested += Card_TagAddRequested;
        return card;
    }

    // ===== 事件处理（所有 ItemCard 共用） =====

    private void Card_OpenRequested(object sender, long itemId)
        => ItemCardActions.Open(this.XamlRoot, itemId);

    private void Card_EditNoteRequested(object sender, ItemCardViewModel vm)
        => ItemCardActions.EditNote(this.XamlRoot, vm);

    private void Card_EditTagsRequested(object sender, ItemCardViewModel vm)
        => ItemCardActions.EditTags(this.XamlRoot, vm);

    private async void Card_HideRequested(object sender, ItemCardViewModel vm)
    {
        var nowHidden = await ItemCardActions.ToggleHidden(this.XamlRoot, vm);
        if (!nowHidden) return;
        foreach (var node in FolderTree.RootNodes)
            RemoveIfHidden(node, vm);
    }

    private static void RemoveIfHidden(TreeViewNode node, ItemCardViewModel vm)
    {
        foreach (var child in node.Children.ToList())
        {
            if (child.Content is ItemCard card && ReferenceEquals(card.ViewModel, vm))
            {
                node.Children.Remove(child);
                return;
            }
            RemoveIfHidden(child, vm);
        }
    }

    private void Card_TagRemoveRequested(object sender, (ItemCardViewModel VM, string Tag) e)
        => ItemCardActions.RemoveTag(this.XamlRoot, e.VM, e.Tag);

    private void Card_TagAddRequested(object sender, ItemCardViewModel vm)
        => ItemCardActions.AddTag(this.XamlRoot, vm);
}