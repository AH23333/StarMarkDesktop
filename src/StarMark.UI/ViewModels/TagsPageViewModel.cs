#nullable enable
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.Abstractions;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 标签云页面 ViewModel。对应浏览器扩展 tags tab + tag-cloud。
/// </summary>
public partial class TagsPageViewModel : ObservableObject
{
    private readonly IItemRepository _repository;

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _emptyHint = string.Empty;

    public ObservableCollection<TagItemViewModel> Tags { get; } = new();
    public ObservableCollection<ItemCardViewModel> FilteredResults { get; } = new();

    [ObservableProperty] private bool _hasActiveFilter;

    /// <summary>当前生效的标签筛选（chip，可单个移除）。对齐扩展 tag-banner。</summary>
    public ObservableCollection<TagFilterChip> ActiveFilterChips { get; } = new();

    private List<string> _activeFilters = new();

    public TagsPageViewModel(IItemRepository repository)
    {
        _repository = repository;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        Tags.Clear();
        try
        {
            var tagList = await _repository.GetAllTagsAsync(CancellationToken.None);
            foreach (var (name, count) in tagList)
                Tags.Add(new TagItemViewModel(name, count));
            EmptyHint = Tags.Count == 0 ? "暂无标签" : string.Empty;
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

    [RelayCommand]
    private async Task ToggleTagAsync(string tagName)
    {
        if (_activeFilters.Contains(tagName))
            _activeFilters.Remove(tagName);
        else
            _activeFilters.Add(tagName);

        HasActiveFilter = _activeFilters.Count > 0;
        SyncFilterChips();

        // 刷新标签选中状态
        foreach (var tag in Tags)
            tag.IsSelected = _activeFilters.Contains(tag.Name);

        await ApplyFilterAsync();
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        _activeFilters.Clear();
        HasActiveFilter = false;
        SyncFilterChips();
        foreach (var tag in Tags)
            tag.IsSelected = false;
        FilteredResults.Clear();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ApplyFilterAsync()
    {
        FilteredResults.Clear();
        if (_activeFilters.Count == 0) return;

        try
        {
            var filter = new BrowseFilter
            {
                TagFilters = _activeFilters,
                Limit = 200,
            };
            var items = await _repository.GetAllAsync(filter, CancellationToken.None);
            foreach (var item in items)
                FilteredResults.Add(new ItemCardViewModel(item));
        }
        catch (Exception ex)
        {
            // 静默吞掉会让「按标签查看」看起来完全失效，至少留下日志可查
            StarMark.Abstractions.StarLog.Error("标签筛选失败", ex);
        }
    }

    private void SyncFilterChips()
    {
        ActiveFilterChips.Clear();
        foreach (var t in _activeFilters)
            ActiveFilterChips.Add(new TagFilterChip(t));
    }

    /// <summary>卡片标签点击：以单个标签作为筛选条件（导航参数传入）。</summary>
    public async Task FilterByTagAsync(string tagName)
    {
        _activeFilters = new List<string> { tagName };
        HasActiveFilter = true;
        SyncFilterChips();
        foreach (var tag in Tags)
            tag.IsSelected = tag.Name == tagName;
        await ApplyFilterAsync();
    }
}

/// <summary>
/// 标签云中的单个标签。对应浏览器扩展 count-tag。
/// </summary>
public partial class TagItemViewModel : ObservableObject
{
    public string Name { get; }
    public int Count { get; }
    public string FormattedName => $"#{Name}";

    [ObservableProperty] private bool _isSelected;

    public TagItemViewModel(string name, int count)
    {
        Name = name;
        Count = count;
    }
}
