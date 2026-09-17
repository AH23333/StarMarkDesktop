#nullable enable
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.Abstractions;
using StarMark.Core.Search;
using StarMark.Core.Sync;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 主窗口 ViewModel。管理导航状态、全局搜索查询、同步状态。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly SearchService _searchService;
    private readonly SyncCoordinator _syncCoordinator;
    private readonly IItemRepository _repository;

    [ObservableProperty] private string _currentPageTag = "tree";
    [ObservableProperty] private string _statusText = "索引就绪";
    [ObservableProperty] private string _statusDotBrush = "StatusOkBrush";
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private int _starsCount;
    [ObservableProperty] private int _bookmarksCount;
    [ObservableProperty] private int _filesCount;
    [ObservableProperty] private string _query = string.Empty;

    // ────── 全局标签筛选（标签页多选 → 文件夹页消费，对齐扩展侧栏 tag-banner 交互）──────

    /// <summary>当前生效的全局标签筛选（AND 语义）。由标签页写入，文件夹页消费。</summary>
    public ObservableCollection<TagFilterChip> GlobalTagFilters { get; } = new();

    private bool _hasGlobalTagFilters;
    public bool HasGlobalTagFilters
    {
        get => _hasGlobalTagFilters;
        private set => SetProperty(ref _hasGlobalTagFilters, value);
    }

    /// <summary>全局标签筛选变化（增/删/清）。文件夹页订阅后重载列表。</summary>
    public event Action? GlobalTagFiltersChanged;

    public void ToggleGlobalTagFilter(string tag)
    {
        var existing = GlobalTagFilters.FirstOrDefault(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) GlobalTagFilters.Remove(existing);
        else GlobalTagFilters.Add(new TagFilterChip(tag));
        OnGlobalTagFiltersChanged();
    }

    public void RemoveGlobalTagFilter(string tag)
    {
        var existing = GlobalTagFilters.FirstOrDefault(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase));
        if (existing is null) return;
        GlobalTagFilters.Remove(existing);
        OnGlobalTagFiltersChanged();
    }

    public void SetGlobalTagFilters(IReadOnlyList<string> tags)
    {
        GlobalTagFilters.Clear();
        foreach (var t in tags)
            GlobalTagFilters.Add(new TagFilterChip(t));
        OnGlobalTagFiltersChanged();
    }

    public void ClearGlobalTagFilters()
    {
        if (GlobalTagFilters.Count == 0) return;
        GlobalTagFilters.Clear();
        OnGlobalTagFiltersChanged();
    }

    private void OnGlobalTagFiltersChanged()
    {
        HasGlobalTagFilters = GlobalTagFilters.Count > 0;
        GlobalTagFiltersChanged?.Invoke();
    }

    public MainViewModel(SearchService searchService, SyncCoordinator syncCoordinator, IItemRepository repository)
    {
        _searchService = searchService;
        _syncCoordinator = syncCoordinator;
        _repository = repository;
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        IsSyncing = true;
        StatusDotBrush = "StatusBusyBrush";
        StatusText = "同步中...";
        try
        {
            var summary = await _syncCoordinator.SyncAllAsync(CancellationToken.None);
            StatusText = summary.FormatText();
            StatusDotBrush = "StatusOkBrush";
            await LoadCountsAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"同步失败: {ex.Message}";
            StatusDotBrush = "StatusBusyBrush";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    public async Task LoadCountsAsync()
    {
        try
        {
            var counts = await _repository.GetCountsByTypeAsync(CancellationToken.None);
            StarsCount = counts.GetValueOrDefault(ItemType.GitHubStar, 0);
            BookmarksCount = counts.GetValueOrDefault(ItemType.Bookmark, 0);
            FilesCount = counts.GetValueOrDefault(ItemType.File, 0);
        }
        catch { }
    }
}
