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

    public MainViewModel Main { get; }

    /// <summary>是否有生效的标签筛选（全局状态，转发自 MainViewModel）。</summary>
    public bool HasActiveFilter => Main.HasGlobalTagFilters;

    /// <summary>当前生效的标签筛选 chips（全局集合，标签页与文件夹页共享同一份）。</summary>
    public ObservableCollection<TagFilterChip> ActiveFilterChips => Main.GlobalTagFilters;

    public TagsPageViewModel(IItemRepository repository, MainViewModel main)
    {
        _repository = repository;
        Main = main;
        Main.GlobalTagFiltersChanged += () => OnPropertyChanged(nameof(HasActiveFilter));
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
        Main.ToggleGlobalTagFilter(tagName);

        // 刷新标签选中状态
        foreach (var tag in Tags)
            tag.IsSelected = Main.GlobalTagFilters.Any(t => string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase));

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        Main.ClearGlobalTagFilters();
        foreach (var tag in Tags)
            tag.IsSelected = false;
        await Task.CompletedTask;
    }

    /// <summary>卡片标签点击：以单个标签作为筛选条件（导航参数传入）。仅设定全局筛选，结果在文件夹页查看。</summary>
    public Task FilterByTagAsync(string tagName)
    {
        Main.SetGlobalTagFilters(new List<string> { tagName });
        foreach (var tag in Tags)
            tag.IsSelected = tag.Name == tagName;
        return Task.CompletedTask;
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
