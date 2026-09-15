#nullable enable
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
