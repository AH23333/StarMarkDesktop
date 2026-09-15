#nullable enable
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.Abstractions;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 文件夹树页面 ViewModel。对应浏览器扩展收藏夹树浏览。
/// </summary>
public partial class FolderTreePageViewModel : ObservableObject
{
    private readonly IItemRepository _repository;

    [ObservableProperty] private string _currentSort = "recent";
    [ObservableProperty] private string _currentSource = "all";
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _emptyHint = string.Empty;

    public ObservableCollection<FolderNodeViewModel> RootNodes { get; } = new();
    public ObservableCollection<ItemCardViewModel> FlatResults { get; } = new();

    public FolderTreePageViewModel(IItemRepository repository)
    {
        _repository = repository;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        RootNodes.Clear();
        FlatResults.Clear();

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

            // 按 type 分组构建树节点
            var stars = items.Where(i => i.Type == ItemType.GitHubStar).ToList();
            var bookmarks = items.Where(i => i.Type == ItemType.Bookmark).ToList();
            var files = items.Where(i => i.Type == ItemType.File).ToList();

            if (stars.Count > 0)
                RootNodes.Add(new FolderNodeViewModel("⭐ GitHub Stars", stars));
            if (bookmarks.Count > 0)
                RootNodes.Add(new FolderNodeViewModel("🔖 浏览器书签", bookmarks));
            if (files.Count > 0)
                RootNodes.Add(new FolderNodeViewModel("📄 本地文件", files));

            EmptyHint = RootNodes.Count == 0 ? "暂无条目，请先同步数据" : string.Empty;
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

    partial void OnCurrentSortChanged(string value) => _ = LoadAsync();
    partial void OnCurrentSourceChanged(string value) => _ = LoadAsync();
    partial void OnShowHiddenChanged(bool value) => _ = LoadAsync();
}

/// <summary>
/// 文件夹树节点。对应浏览器扩展 FolderNode。
/// </summary>
public partial class FolderNodeViewModel : ObservableObject
{
    public string Name { get; }
    public IReadOnlyList<Item> Items { get; }
    public ObservableCollection<ItemCardViewModel> VisibleItems { get; } = new();

    [ObservableProperty] private bool _isExpanded;

    public int TotalCount => Items.Count;

    public string Chevron => IsExpanded ? "▾" : "▸";

    public FolderNodeViewModel(string name, IReadOnlyList<Item> items)
    {
        Name = name;
        Items = items;
        // 默认显示前 50 条
        foreach (var item in items.Take(50))
            VisibleItems.Add(new ItemCardViewModel(item));
    }

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
        OnPropertyChanged(nameof(Chevron));
    }
}
