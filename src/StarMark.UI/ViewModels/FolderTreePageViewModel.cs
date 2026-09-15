#nullable enable
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.Abstractions;
using Microsoft.UI.Xaml;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 文件夹树页面 ViewModel。真实嵌套文件夹树：
/// 书签 → ExtraJson.BookmarkMeta.FolderPaths（'/' 分隔）；文件 → file:// 目录；GitHub/剪贴板 → 伪根分组。
/// 采用扁平化渲染：深度(Depth)缩进 + 逐级展开，子文件夹作为独立节点插入列表。
/// </summary>
public partial class FolderTreePageViewModel : ObservableObject
{
    private readonly IItemRepository _repository;
    private FolderPathNodeViewModel? _root;

    [ObservableProperty] private string _currentSort = "recent";
    [ObservableProperty] private string _currentSource = "all";
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _emptyHint = string.Empty;

    public ObservableCollection<FolderPathNodeViewModel> VisibleNodes { get; } = new();

    public FolderTreePageViewModel(IItemRepository repository)
    {
        _repository = repository;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        VisibleNodes.Clear();

        try
        {
            var filter = new BrowseFilter
            {
                Sort = CurrentSort,
                IncludeHidden = ShowHidden,
                TypeFilter = CurrentSource == "all" ? null : CurrentSource,
                Limit = 500,
            };
            var items = await _repository.GetAllAsync(filter, CancellationToken.None);
            var itemList = items.ToList();

            _root = new FolderPathNodeViewModel("ROOT");
            BuildTree(_root, itemList);
            RebuildVisible();
            EmptyHint = _root.Children.Count == 0 ? "暂无条目，请先同步数据" : string.Empty;
        }
        catch (Exception ex)
        {
            EmptyHint = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildTree(FolderPathNodeViewModel root, List<Item> items)
    {
        foreach (var item in items)
        {
            var path = FolderPathUtil.GetSegments(item);

            var node = root;
            for (int i = 0; i < path.Length; i++)
                node = node.GetOrAddChild(path[i]);

            var list = (node.Items as List<Item>)!;
            list.Add(item);
        }
    }

    private void RebuildVisible()
    {
        VisibleNodes.Clear();
        if (_root == null) return;
        foreach (var child in _root.Children)
            AppendVisible(child, 0);
    }

    private void AppendVisible(FolderPathNodeViewModel node, int depth)
    {
        node.Depth = depth;
        VisibleNodes.Add(node);
        if (!node.IsExpanded) return;
        foreach (var child in node.Children)
            AppendVisible(child, depth + 1);
    }

    partial void OnCurrentSortChanged(string value) => _ = LoadAsync();
    partial void OnCurrentSourceChanged(string value) => _ = LoadAsync();
    partial void OnShowHiddenChanged(bool value) => _ = LoadAsync();
}

/// <summary>
/// 文件夹树节点。对应浏览器扩展 FolderNode，支持任意深度嵌套。
/// </summary>
public partial class FolderPathNodeViewModel : ObservableObject
{
    public string Name { get; }
    public List<FolderPathNodeViewModel> Children { get; } = new();
    public IReadOnlyList<Item> Items { get; }
    public ObservableCollection<ItemCardViewModel> VisibleItems { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private int _depth;

    public int TotalCount => Items.Count + Children.Sum(c => c.TotalCount);

    public string Chevron => IsExpanded ? "▾" : "▸";

    public Thickness Indent => new(Depth * 18, 0, 0, 0);

    public FolderPathNodeViewModel(string name)
    {
        Name = name;
        Items = new List<Item>();
    }

    internal List<Item> ItemList => (List<Item>)Items;

    public FolderPathNodeViewModel GetOrAddChild(string segment)
    {
        var existing = Children.Find(c => c.Name == segment);
        if (existing != null) return existing;
        var child = new FolderPathNodeViewModel(segment);
        Children.Add(child);
        return child;
    }

    /// <summary>填充本节点条目（惰性，首次展开时调用）。</summary>
    public void HydrateItems()
    {
        if (VisibleItems.Count > 0) return;
        foreach (var item in Items.Take(30))
            VisibleItems.Add(new ItemCardViewModel(item));
    }

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
        if (IsExpanded)
            HydrateItems();
        OnPropertyChanged(nameof(Chevron));
        OnPropertyChanged(nameof(Indent));
    }
}