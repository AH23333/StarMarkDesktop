#nullable enable
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.Abstractions;
using StarMark.Core.Search;
using StarMark.Core.Sync;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 主窗口 ViewModel。管理导航状态、全局搜索查询、跨页共享的标签/语言筛选与计数。
/// （同步的进行状态与结果由 MainWindow 顶栏直接呈现，不在这里另存一份。）
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly SearchService _searchService;
    private readonly IItemRepository _repository;

    [ObservableProperty] private string _currentPageTag = "tree";
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

    // ────── 全局语言筛选（主界面语言下拉直选 → 文件夹/搜索页消费）──────

    /// <summary>当前生效的语言筛选（extra_json.Language）。空字符串表示不过滤。</summary>
    [ObservableProperty] private string _currentLanguage = string.Empty;

    /// <summary>语言筛选变化。文件夹页订阅后重载列表，仅显示匹配语言的 star 条目。</summary>
    public event Action? LanguageFilterChanged;

    partial void OnCurrentLanguageChanged(string value) => LanguageFilterChanged?.Invoke();

    public MainViewModel(SearchService searchService, IItemRepository repository)
    {
        _searchService = searchService;
        _repository = repository;
    }

    // 同步入口只有 MainWindow.DoSyncAsync 一处（顶栏「同步」+「取消同步」）。
    // 这里原先另有一个无人绑定的 SyncAsync/IsSyncing/StatusText/StatusDotBrush：一条不带取消、
    // 也不写 InfoBar 的第二同步路径，一旦被人绑上就会与真入口分叉（P-55 收口时删除）。

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
