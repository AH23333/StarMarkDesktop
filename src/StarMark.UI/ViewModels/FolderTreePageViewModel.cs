#nullable enable
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.Abstractions;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 文件夹页面 ViewModel。构建语义文件夹树（书签 → FolderPaths 层级；文件 → file:// 目录；GitHub/剪贴板 → 伪根）。
/// 渲染层用 TreeView 按需展开 + 惰性挂载条目，缩进/展开/虚拟化交给控件。
/// </summary>
public partial class FolderTreePageViewModel : ObservableObject
{
    private const int MaxItemsPerFolder = 30;

    private readonly IItemRepository _repository;

    [ObservableProperty] private string _currentSort = "recent";
    [ObservableProperty] private string _currentSource = "all";
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _emptyHint = string.Empty;

    public bool HasEmptyHint => !string.IsNullOrEmpty(EmptyHint);

    partial void OnEmptyHintChanged(string value) => OnPropertyChanged(nameof(HasEmptyHint));

    public ObservableCollection<FolderPathNodeViewModel> Roots { get; } = new();

    /// <summary>在 Roots 加载完毕后触发，通知页面重建 TreeView。</summary>
    public event Action? RootsReady;

    public FolderTreePageViewModel(IItemRepository repository)
    {
        _repository = repository;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        Roots.Clear();

        try
        {
            var filter = new BrowseFilter
            {
                Sort = CurrentSort,
                IncludeHidden = ShowHidden,
                TypeFilter = CurrentSource switch { "all" => null, "star" => "githubstar", _ => CurrentSource },
                Limit = 2000,
            };
            var items = await _repository.GetAllAsync(filter, CancellationToken.None);

            var nodeMap = new Dictionary<string, FolderPathNodeViewModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var path = FolderPathUtil.GetSegments(item);
                var node = GetOrCreateNode(nodeMap, path);
                node.Items.Add(item);
            }

            foreach (var root in nodeMap.Values.Where(n => n.Parent == null)
                                             .OrderBy(n => n.RootOrder)
                                             .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
                Roots.Add(root);

            EmptyHint = Roots.Count == 0 ? "暂无条目，请先同步数据" : string.Empty;
        }
        catch (Exception ex)
        {
            EmptyHint = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }

        RootsReady?.Invoke();
    }

    /// <summary>按路径链逐级创建/复用节点，作为树根列表返回最顶层节点。</summary>
    private static FolderPathNodeViewModel GetOrCreateNode(Dictionary<string, FolderPathNodeViewModel> nodeMap, string[] path)
    {
        FolderPathNodeViewModel? parent = null;
        foreach (var segment in path)
        {
            if (parent == null)
            {
                if (!nodeMap.TryGetValue(segment, out var root))
                {
                    root = new FolderPathNodeViewModel(segment, null);
                    nodeMap[segment] = root;
                }
                parent = root;
                continue;
            }

            parent = parent.GetOrAddChild(segment);
        }
        return parent!;
    }

    public IReadOnlyList<ItemCardViewModel> Hydrate(FolderPathNodeViewModel node)
        => node.Items.Take(MaxItemsPerFolder).Select(i => new ItemCardViewModel(i)).ToList();

    partial void OnCurrentSortChanged(string value) => _ = LoadAsync();
    partial void OnCurrentSourceChanged(string value) => _ = LoadAsync();
    partial void OnShowHiddenChanged(bool value) => _ = LoadAsync();
}

/// <summary>
/// 文件夹树节点（语义层）。Name + 子文件夹 + 词条。
/// </summary>
public sealed class FolderPathNodeViewModel
{
    public string Name { get; }
    public FolderPathNodeViewModel? Parent { get; }
    public List<FolderPathNodeViewModel> Children { get; } = new();
    public List<Item> Items { get; } = new();

    public int TotalCount => Items.Count + Children.Sum(c => c.TotalCount);

    public int RootOrder => FolderPathUtil.RootOrder(Name);

    public FolderPathNodeViewModel(string name, FolderPathNodeViewModel? parent)
    {
        Name = name;
        Parent = parent;
    }

    public FolderPathNodeViewModel GetOrAddChild(string segment)
    {
        var existing = Children.Find(c => c.Name == segment);
        if (existing != null) return existing;
        var child = new FolderPathNodeViewModel(segment, this);
        Children.Add(child);
        return child;
    }
}