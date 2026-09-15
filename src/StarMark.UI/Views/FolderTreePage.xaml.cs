#nullable enable
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Microsoft.UI;
using StarMark.Abstractions;
using StarMark.UI.Controls;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 文件夹页：手风琴式目录。每个文件夹 = 可折叠头部 + 展开后挂载子文件夹头与条目卡片。
/// 不使用 TreeViewItem 容器（其固定行高会压扁 ItemCard），卡片与搜索页同款全高渲染。
/// </summary>
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

    private void OnRootsReady() => RebuildTree();

    private void RebuildTree()
    {
        FolderRoot.Children.Clear();
        foreach (var root in ViewModel.Roots)
            FolderRoot.Children.Add(BuildFolder(root, 0));
    }

    // ===== 手风琴构建 =====

    /// <summary>
    /// 构建一个文件夹节点：头部（可折叠）+ 展开后内容（子文件夹 + 本层条目卡片）。
    /// 展开内容在首次点击时才惰性构建，避免海量条目一次性渲染。
    /// </summary>
    private StackPanel BuildFolder(FolderPathNodeViewModel node, int depth)
    {
        var body = new StackPanel { Spacing = 2, Visibility = Visibility.Collapsed };
        bool built = false;

        var header = new Button
        {
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6 + depth * 18, 4, 6, 4),
            Tag = node,
            Content = BuildHeaderContent(node, false),
        };
        header.Click += (_, _) =>
        {
            StarLog.Info($"Accordion click: {node.Name} (items={node.Items.Count}, children={node.Children.Count})");
            if (body.Visibility == Visibility.Collapsed)
            {
                BuildOnce();
                body.Visibility = Visibility.Visible;
                header.Content = BuildHeaderContent(node, true);
                StarLog.Info($"Accordion expanded: {node.Name} bodyChildren={body.Children.Count}");
            }
            else
            {
                body.Visibility = Visibility.Collapsed;
                header.Content = BuildHeaderContent(node, false);
                StarLog.Info($"Accordion collapsed: {node.Name}");
            }
        };

        void BuildOnce()
        {
            if (built) return;
            built = true;
            try
            {
                var entryPad = 6 + (depth + 1) * 18;
                foreach (var child in node.Children)
                    body.Children.Add(BuildFolder(child, depth + 1));
                foreach (var vm in ViewModel.Hydrate(node))
                {
                    var wrap = new StackPanel { Padding = new Thickness(entryPad, 4, 0, 0) };
                    wrap.Children.Add(CreateCard(vm));
                    body.Children.Add(wrap);
                }
                if (node.Items.Count > FolderTreePageViewModel.MaxItemsPerFolder)
                {
                    body.Children.Add(new TextBlock
                    {
                        Text = $"… 还有 {node.Items.Count - FolderTreePageViewModel.MaxItemsPerFolder} 条（按需加载上限）",
                        Style = (Style)Application.Current.Resources["MutedText"],
                        FontSize = 11,
                        Margin = new Thickness(entryPad, 8, 0, 4),
                    });
                }
            }
            catch (Exception ex)
            {
                StarLog.Error($"BuildOnce failed for {node.Name}", ex);
                body.Children.Add(new TextBlock { Text = $"加载失败: {ex.Message}", Foreground = new SolidColorBrush(Colors.OrangeRed) });
            }
        }

        var full = new StackPanel();
        full.Children.Add(header);
        full.Children.Add(body);
        return full;
    }

    private static StackPanel BuildHeaderContent(FolderPathNodeViewModel node, bool expanded)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new FontIcon
        {
            Glyph = expanded ? "\uE70D" : "\uE76C",
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        row.Children.Add(new TextBlock
        {
            Text = "📁",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = node.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        row.Children.Add(new TextBlock
        {
            Text = $"({node.TotalCount})",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    // ===== 条目卡片 =====

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

    private void Card_OpenRequested(object sender, long itemId)
        => ItemCardActions.Open(this.XamlRoot, itemId);

    private void Card_EditNoteRequested(object sender, ItemCardViewModel vm)
        => ItemCardActions.EditNote(this.XamlRoot, vm);

    private void Card_EditTagsRequested(object sender, ItemCardViewModel vm)
        => ItemCardActions.EditTags(this.XamlRoot, vm);

    private async void Card_HideRequested(object sender, ItemCardViewModel vm)
    {
        var nowHidden = await ItemCardActions.ToggleHidden(this.XamlRoot, vm);
        if (nowHidden)
            _ = ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void Card_TagRemoveRequested(object sender, (ItemCardViewModel VM, string Tag) e)
        => ItemCardActions.RemoveTag(this.XamlRoot, e.VM, e.Tag);

    private void Card_TagAddRequested(object sender, ItemCardViewModel vm)
        => ItemCardActions.AddTag(this.XamlRoot, vm);
}