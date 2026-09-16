#nullable enable
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.Abstractions;
using StarMark.Core.Search;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 搜索页面 ViewModel。对应浏览器扩展搜索框 + 结果列表。
/// </summary>
public partial class SearchPageViewModel : ObservableObject
{
    private readonly SearchService _searchService;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _emptyHint = "输入关键词开始搜索";
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _currentSort = "relevance";
    [ObservableProperty] private string _currentSource = "all";
    [ObservableProperty] private bool _showHidden;

    /// <summary>键盘导航当前选中索引（↑↓）；-1 = 未选中。</summary>
    [ObservableProperty] private int _selectedIndex = -1;

    /// <summary>
    /// 当前生效的标签过滤（AND 语义：条目须同时具备全部标签）。
    /// 对齐浏览器扩展侧边栏的 tagFilters + 吸顶 tag-banner。
    /// </summary>
    public ObservableCollection<TagFilterChip> ActiveTags { get; } = new();

    [ObservableProperty] private bool _hasActiveTags;

    public ObservableCollection<ItemCardViewModel> Results { get; } = new();

    public ItemCardViewModel? SelectedItem
        => SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;

    /// <summary>↑/↓ 移动键盘选中项。</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        SetSelectedIndex(Math.Clamp(SelectedIndex + delta, 0, Results.Count - 1));
    }

    public void SetSelectedIndex(int index)
    {
        if (Results.Count == 0) return;
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
            Results[SelectedIndex].IsKeyboardSelected = false;
        SelectedIndex = Math.Clamp(index, 0, Results.Count - 1);
        Results[SelectedIndex].IsKeyboardSelected = true;
    }

    public void ClearSelection()
    {
        if (SelectedItem is { } old) old.IsKeyboardSelected = false;
        SelectedIndex = -1;
    }

    /// <summary>重置搜索页：清掉查询词与全部结果（离开搜索态时调用，
    /// 保证下次进入搜索页不会闪现上次的搜索结果）。</summary>
    public void Reset()
    {
        Results.Clear();
        ClearSelection();
        HasResults = false;
        IsSearching = false;
        StatusText = string.Empty;
        EmptyHint = "输入关键词开始搜索";
        // 标签过滤一并无条件清空：否则下次进入搜索页会按上次残留的标签直接出结果。
        // 先清标签再清 Query，避免中间态触发一次带标签的空查询。
        if (ActiveTags.Count > 0)
        {
            ActiveTags.Clear();
            HasActiveTags = false;
        }
        if (Query.Length > 0)
            Query = string.Empty; // 触发 OnQueryChanged → SearchAsync 空查询分支（幂等）
    }

    public SearchPageViewModel(SearchService searchService)
    {
        _searchService = searchService;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        if (string.IsNullOrWhiteSpace(Query))
        {
            Results.Clear();
            ClearSelection();
            HasResults = false;
            EmptyHint = "输入关键词开始搜索";
            return;
        }

        IsSearching = true;
        StatusText = "搜索中...";

        var filter = new SearchFilter
        {
            MaxResults = 100,
            IncludeSize = true,
            IncludeDate = true,
            IncludeHidden = ShowHidden,
            Type = CurrentSource switch
            {
                "star" => ItemType.GitHubStar,
                "bookmark" => ItemType.Bookmark,
                _ => null,
            },
            Sort = CurrentSort,
            Tags = ActiveTags.Count > 0 ? ActiveTags.Select(t => t.Name).ToArray() : null,
        };

        try
        {
            var result = await _searchService.SearchAsync(Query.Trim(), filter, token);
            if (token.IsCancellationRequested) return;

            Results.Clear();
            var keyword = Query.Trim();
            foreach (var item in result.Items)
            {
                var vm = new ItemCardViewModel(item);
                vm.HighlightQuery = keyword;
                Results.Add(vm);
            }

            ClearSelection();
            HasResults = Results.Count > 0;
            EmptyHint = Results.Count == 0
                ? (ActiveTags.Count > 0
                    ? $"没有同时带 {string.Join(" + ", ActiveTags.Select(t => "#" + t.Name))} 的条目"
                    : $"未找到与 \"{keyword}\" 相关的条目")
                : string.Empty;
            StatusText = $"命中 {result.Items.Count} 条 · {result.ElapsedMs}ms";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = $"错误: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    public void RemoveItem(long id) { Results.FirstOrDefault(r => r.Id == id)?.Let(_ => Results.Remove(_)); }

    partial void OnQueryChanged(string value)
    {
        _ = SearchAsync();
    }

    partial void OnShowHiddenChanged(bool value) { _ = SearchAsync(); }

    partial void OnCurrentSourceChanged(string value) { _ = SearchAsync(); }

    partial void OnCurrentSortChanged(string value) { _ = SearchAsync(); }

    // ───────── 标签过滤（AND 语义，对齐扩展 tagFilters + 吸顶 banner）─────────

    /// <summary>追加一个标签过滤条件。已存在则忽略（避免重复 AND 同一标签）。</summary>
    public void AddTagFilter(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        if (ActiveTags.Any(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase))) return;
        ActiveTags.Add(new TagFilterChip(tag));
        HasActiveTags = true;
        _ = SearchAsync();
    }

    public void RemoveTagFilter(string tag)
    {
        var chip = ActiveTags.FirstOrDefault(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase));
        if (chip is null) return;
        ActiveTags.Remove(chip);
        HasActiveTags = ActiveTags.Count > 0;
        _ = SearchAsync();
    }

    /// <summary>清空全部标签过滤；清空后若无关键词则不展示任何内容（回到初始空态）。</summary>
    public void ClearTagFilters()
    {
        if (ActiveTags.Count == 0) return;
        ActiveTags.Clear();
        HasActiveTags = false;
        _ = SearchAsync();
    }
}

/// <summary>吸顶标签筛选条上的一枚标签（带 ✕ 可移除）。</summary>
public partial class TagFilterChip : ObservableObject
{
    public string Name { get; }

    public TagFilterChip(string name) => Name = name;
}

internal static class EnumerableEx { public static void Let<T>(this T? item, Action<T> action) where T : class { if (item != null) action(item); } }
