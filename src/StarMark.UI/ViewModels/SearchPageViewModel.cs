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
        };

        try
        {
            var result = await _searchService.SearchAsync(Query.Trim(), filter, token);
            if (token.IsCancellationRequested) return;

            Results.Clear();
            foreach (var item in result.Items)
                Results.Add(new ItemCardViewModel(item));

            ClearSelection();
            HasResults = Results.Count > 0;
            EmptyHint = Results.Count == 0 ? $"未找到与 \"{Query.Trim()}\" 相关的条目" : string.Empty;
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
}

internal static class EnumerableEx { public static void Let<T>(this T? item, Action<T> action) where T : class { if (item != null) action(item); } }
