#nullable enable
using Microsoft.UI.Dispatching;
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
            ?? new FolderTreePageViewModel(GetRepo(), GetMain());
        ViewModel.RootsReady += OnRootsReady;
        // 主题切换会改变代码构建处的主题画笔解析，需重建以刷新颜色
        ActualThemeChanged += (_, _) => RebuildTree();
    }

    private static StarMark.Abstractions.IItemRepository GetRepo()
        => App.Services.GetService(typeof(StarMark.Abstractions.IItemRepository))
            as StarMark.Abstractions.IItemRepository
            ?? throw new InvalidOperationException("IItemRepository 未注册");

    private static StarMark.UI.ViewModels.MainViewModel GetMain()
        => App.Services.GetService(typeof(StarMark.UI.ViewModels.MainViewModel))
            as StarMark.UI.ViewModels.MainViewModel
            ?? throw new InvalidOperationException("MainViewModel 未注册");

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadCommand.ExecuteAsync(null);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // 单例 VM 的 RootsReady 在 ctor 订阅且永不退订；离开本页后必须退订，
        // 否则死页会持续接收重建事件（重载树、操作已分离的元素），既泄漏又可能在标签变化时崩溃。
        ViewModel.RootsReady -= OnRootsReady;
    }

    // ────── 全局标签筛选栏 ──────

    /// <summary>从筛选条移除一枚标签。延迟到消息队列末尾，避免 ItemsRepeater 重入崩溃。</summary>
    private void RemoveFilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tagName })
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => ViewModel.Main.RemoveGlobalTagFilter(tagName));
    }

    private void ClearTagFilters_Click(object sender, RoutedEventArgs e)
        => DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => ViewModel.Main.ClearGlobalTagFilters());

    /// <summary>
    /// 重建后要恢复的展开状态：键 = 节点路径（根到该节点的名称拼接），见 <see cref="PathOf"/>。
    /// 没有它的话，任何一次重载（改标签、排序、筛选）都会把用户展开的文件夹全部收回去。
    /// </summary>
    private readonly HashSet<string> _expandedPaths = new(StringComparer.Ordinal);

    /// <summary>每个已展开文件夹已加载的条目数（「展开更多」后重载不至于回到默认 30 条）。</summary>
    private readonly Dictionary<string, int> _loadedCountByPath = new(StringComparer.Ordinal);

    /// <summary>本次构建出的全部文件夹视图，用于在重建结束后恢复展开状态。</summary>
    private readonly List<FolderView> _views = new();

    private void OnRootsReady() => RebuildTree();

    private void RebuildTree()
    {
        FolderRoot.Children.Clear();
        _views.Clear();
        foreach (var root in ViewModel.Roots)
            FolderRoot.Children.Add(BuildFolder(root, 0, string.Empty));

        // 重载会把用户展开的文件夹全部收起（表现为「给条目增删标签后界面被强制收拢」）。
        // 这里在重建后立即按记录的恢复回去，让重载对用户无感。
        foreach (var view in _views.ToList())
        {
            if (!_expandedPaths.Contains(view.Key)) continue;
            try { view.Expand(); }
            catch (Exception ex) { StarLog.Error($"恢复展开状态失败: {view.Key}", ex); }
        }
    }

    /// <summary>节点路径键：从根到该节点的名称链，用于跨重建识别同一个文件夹。</summary>
    private static string PathOf(FolderPathNodeViewModel node, string parentPath)
        => string.IsNullOrEmpty(parentPath) ? node.Name : $"{parentPath}/{node.Name}";

    /// <summary>单个文件夹的可视化句柄（供重建后恢复展开状态）。</summary>
    private sealed class FolderView
    {
        public required string Key { get; init; }
        public required Action Expand { get; init; }
    }

    // ===== 手风琴构建 =====

    /// <summary>
    /// 构建一个文件夹节点：头部（可折叠）+ 展开后内容（子文件夹 + 本层条目卡片）。
    /// 展开内容在首次点击时才惰性构建，避免海量条目一次性渲染。
    /// </summary>
    private StackPanel BuildFolder(FolderPathNodeViewModel node, int depth, string parentPath)
    {
        var key = PathOf(node, parentPath);
        var body = new StackPanel { Spacing = 2, Visibility = Visibility.Collapsed };
        bool built = false;
        var itemsPanel = new StackPanel { Spacing = 2 };
        Button? expandBtn = null;
        int loaded = _loadedCountByPath.TryGetValue(key, out var saved) ? saved : 0;

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
            if (body.Visibility == Visibility.Collapsed) DoExpand();
            else DoCollapse();
        };

        // 展开：立即呈现动画与头部切换，重活（构建子树 + 卡片）延后到下一消息帧。
        // 同时记录展开状态：树重载后据此复原，避免用户已展开的文件夹被强制收拢。
        void DoExpand()
        {
            body.Visibility = Visibility.Visible;
            header.Content = BuildHeaderContent(node, true);
            _expandedPaths.Add(key);
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(BuildOnce);
        }

        void DoCollapse()
        {
            body.Visibility = Visibility.Collapsed;
            header.Content = BuildHeaderContent(node, false);
            _expandedPaths.Remove(key);
            _loadedCountByPath.Remove(key);
        }

        void RenderItems()
        {
            itemsPanel.Children.Clear();
            var entryPad = 6 + (depth + 1) * 18;
            int shown = 0;
            foreach (var vm in ViewModel.HydrateRange(node, 0, loaded))
            {
                var wrap = new StackPanel { Padding = new Thickness(entryPad, 4, 0, 0) };
                wrap.Children.Add(CreateCard(vm));
                itemsPanel.Children.Add(wrap);
                shown++;
            }
            var remaining = node.Items.Count - loaded;
            if (remaining > 0)
            {
                if (expandBtn == null)
                {
                    expandBtn = new Button
                    {
                        Margin = new Thickness(entryPad, 8, 0, 4),
                        Padding = new Thickness(12, 4, 12, 4),
                        BorderThickness = new Thickness(0),
                        FontSize = 11,
                        Style = (Style)Application.Current.Resources["SecondaryButton"], // 仅 Style 查找（非画笔），不受主题冻结影响
                    };
                    expandBtn.Click += (_, _) =>
                    {
                        loaded = Math.Min(node.Items.Count, loaded + FolderTreePageViewModel.ExpandMoreStep);
                        _loadedCountByPath[key] = loaded;   // 重载后仍保留用户的展开深度
                        // 延后构建，避免一次性创建大量卡片时界面卡住。
                        DispatcherQueue.GetForCurrentThread()?.TryEnqueue(RenderItems);
                        StarLog.Info($"ExpandMore: {node.Name} loaded={loaded}/{node.Items.Count}");
                    };
                    body.Children.Add(expandBtn);
                }
                expandBtn.Content = $"展开更多（剩余 {remaining} 条）";
            }
            else if (expandBtn != null)
            {
                body.Children.Remove(expandBtn);
                expandBtn = null;
            }
        }

        void BuildOnce()
        {
            if (built) return;
            built = true;
            try
            {
                foreach (var child in node.Children)
                    body.Children.Add(BuildFolder(child, depth + 1, key));
                body.Children.Add(itemsPanel);
                // 用户此前点过「展开更多」：恢复他的展开深度，而不是回到默认条数
                loaded = _loadedCountByPath.TryGetValue(key, out var prev) && prev > 0
                    ? Math.Min(prev, node.Items.Count)
                    : Math.Min(FolderTreePageViewModel.MaxItemsPerFolder, node.Items.Count);
                _loadedCountByPath[key] = loaded;
                // 卡片创建较重，延后到下一消息帧：先让展开动画/头部切换立即呈现，消除点击卡顿。
                DispatcherQueue.GetForCurrentThread()?.TryEnqueue(RenderItems);
            }
            catch (Exception ex)
            {
                StarLog.Error($"BuildOnce failed for {node.Name}", ex);
                body.Children.Add(new TextBlock { Text = $"加载失败: {ex.Message}", Foreground = new SolidColorBrush(Colors.OrangeRed) });
            }
        }

        _views.Add(new FolderView { Key = key, Expand = DoExpand });

        // 子文件夹是「父级展开后才构建」的，此时 RebuildTree 的复位循环早已跑完，
        // 所以这里再自查一次：只要记过展开就立刻恢复。
        if (_expandedPaths.Contains(key)) DoExpand();

        var full = new StackPanel();
        full.Children.Add(header);
        full.Children.Add(body);
        return full;
    }

    private StackPanel BuildHeaderContent(FolderPathNodeViewModel node, bool expanded)
    {
        // 按页面 ActualTheme 解析画笔（应用级主题启动后冻结，浅色模式下
        // Application.Current.Resources 会解析出深色画笔 → 白字白底）
        var muted = ThemeBrush.For(this.ActualTheme, "AppMutedBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new FontIcon
        {
            Glyph = expanded ? "\uE70D" : "\uE76C",
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = muted,
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
            Foreground = muted,
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
        card.PinRequested += Card_PinRequested;
        card.CopyLinkRequested += Card_CopyLinkRequested;
        card.OpenLocationRequested += Card_OpenLocationRequested;
        card.TagFilterRequested += Card_TagFilterRequested;
        card.TagRemoveRequested += Card_TagRemoveRequested;
        card.TagAddRequested += Card_TagAddRequested;
        return card;
    }

    private void Card_OpenRequested(object? sender, long itemId)
        => ItemCardActions.Open(this.XamlRoot, itemId);

    private void Card_EditNoteRequested(object? sender, ItemCardViewModel vm)
        => ItemCardActions.EditNote(this.XamlRoot, vm);

    private void Card_EditTagsRequested(object? sender, ItemCardViewModel vm)
        => ItemCardActions.EditTags(this.XamlRoot, vm);

    private async void Card_HideRequested(object? sender, ItemCardViewModel vm)
    {
        var nowHidden = await ItemCardActions.ToggleHidden(this.XamlRoot, vm);
        if (nowHidden)
            _ = ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void Card_PinRequested(object? sender, ItemCardViewModel vm)
    {
        ItemCardActions.TogglePin(vm);
        // 置顶影响浏览排序，稍后重载（给切换动画留一拍）
        _ = Task.Delay(150).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => _ = ViewModel.LoadCommand.ExecuteAsync(null)));
    }

    private void Card_CopyLinkRequested(object? sender, ItemCardViewModel vm)
        => ItemCardActions.CopyUri(vm);

    private void Card_OpenLocationRequested(object? sender, ItemCardViewModel vm)
        => ItemCardActions.OpenLocation(vm);

    private void Card_TagFilterRequested(object? sender, (ItemCardViewModel VM, string Tag) e)
        => App.MainWindow?.NavigateTo("tags", e.Tag);

    private void Card_TagRemoveRequested(object? sender, (ItemCardViewModel VM, string Tag) e)
        => ItemCardActions.RemoveTag(this.XamlRoot, e.VM, e.Tag);

    private void Card_TagAddRequested(object? sender, ItemCardViewModel vm)
        => ItemCardActions.AddTag(this.XamlRoot, vm);
}