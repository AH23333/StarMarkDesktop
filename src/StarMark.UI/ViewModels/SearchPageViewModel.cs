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
    private readonly IItemRepository? _repo;
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
    /// 当前生效的标签过滤统一走 Main.GlobalTagFilters（单一真源，对齐扩展 tagFilters）。
    /// 搜索页不再持有独立的标签状态，避免与导航栏「标签」出现两套入口、互相脱节。
    /// </summary>

    public ObservableCollection<ItemCardViewModel> Results { get; } = new();

    // ───────── 结果分段（P2-7，对齐扩展「精确匹配 / 相关结果」两段）─────────
    // 与 Results 持有同一批 VM 实例（分区展示），键盘导航仍走扁平 Results。

    /// <summary>精确匹配段：标题以关键词开头或 URI 包含关键词。</summary>
    public ObservableCollection<ItemCardViewModel> ExactResults { get; } = new();

    /// <summary>相关结果段：其余命中。</summary>
    public ObservableCollection<ItemCardViewModel> RelatedResults { get; } = new();

    [ObservableProperty] private bool _hasExact;
    [ObservableProperty] private bool _hasRelated;
    [ObservableProperty] private string _exactHeader = "精确匹配";
    [ObservableProperty] private string _relatedHeader = "相关结果";

    // ───────── 语言筛选维度（P2-7，GitHubStar 主语言）─────────

    [ObservableProperty] private string _currentLanguage = string.Empty;

    /// <summary>语言下拉可选项：由最近一次（未按语言过滤的）搜索结果聚合而来。</summary>
    public ObservableCollection<string> AvailableLanguages { get; } = new();

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
        ExactResults.Clear();
        RelatedResults.Clear();
        HasExact = false;
        HasRelated = false;
        ClearSelection();
        HasResults = false;
        IsSearching = false;
        StatusText = string.Empty;
        EmptyHint = "输入关键词开始搜索";
        // 语言筛选与选项一并复位，避免残留过滤让下次搜索静默变窄。
        if (CurrentLanguage.Length > 0)
            CurrentLanguage = string.Empty; // 触发 OnCurrentLanguageChanged → 幂等空查询
        AvailableLanguages.Clear();
        // 注意：全局标签筛选（Main.GlobalTagFilters）不在离开搜索页时清空，
        // 它独立于搜索、由导航栏「标签」控制，应在各页面间持久保留。
        if (Query.Length > 0)
            Query = string.Empty; // 触发 OnQueryChanged → SearchAsync 空查询分支（幂等）
    }

    public SearchPageViewModel(SearchService searchService, MainViewModel main, IItemRepository? repo = null)
    {
        _searchService = searchService;
        _repo = repo;
        Main = main;
        // 全局标签筛选变化（标签页增删/清除）→ 按新组合重搜（搜索在标签筛选结果内执行）
        Main.GlobalTagFiltersChanged += OnGlobalTagFiltersChanged;
    }

    /// <summary>主窗口 ViewModel（持有全局标签筛选单一真源）。</summary>
    public MainViewModel Main { get; }

    [RelayCommand]
    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        if (string.IsNullOrWhiteSpace(Query) && !Main.HasGlobalTagFilters)
        {
            // 空关键词 + 无标签 → 浏览模式：展示最近条目（对齐扩展：清空搜索框回到浏览列表而非空白）
            await LoadBrowseAsync(token);
            return;
        }
        // 空关键词 + 已选标签 → 不提前返回：SearchService 会退化为「按标签浏览」
        // （与桌面组件同一规则），让主界面也能不输关键词、纯靠标签组合过滤。

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
            Language = string.IsNullOrEmpty(CurrentLanguage) ? null : CurrentLanguage,
            Tags = Main.HasGlobalTagFilters ? Main.GlobalTagFilters.Select(t => t.Name).ToArray() : null,
        };

        try
        {
            var result = await _searchService.SearchAsync(Query.Trim(), filter, token);
            if (token.IsCancellationRequested) return;

            Results.Clear();
            ExactResults.Clear();
            RelatedResults.Clear();
            var keyword = Query.Trim();
            for (var i = 0; i < result.Items.Count; i++)
            {
                var vm = new ItemCardViewModel(result.Items[i]);
                vm.HighlightQuery = keyword;
                Results.Add(vm);
                // 分段：Items 前 ExactCount 条为精确匹配，其余为相关结果
                if (i < result.ExactCount) ExactResults.Add(vm); else RelatedResults.Add(vm);
            }
            HasExact = ExactResults.Count > 0;
            HasRelated = RelatedResults.Count > 0;
            ExactHeader = $"精确匹配 ({ExactResults.Count})";
            RelatedHeader = $"相关结果 ({RelatedResults.Count})";

            // 语言下拉选项：仅在「未按语言过滤」时重建，避免过滤后列表塌缩成单项
            if (string.IsNullOrEmpty(CurrentLanguage))
            {
                AvailableLanguages.Clear();
                foreach (var lang in result.Items
                             .Where(it => it.Type == ItemType.GitHubStar && !string.IsNullOrEmpty(it.ExtraJson))
                             .Select(it => TryGetLanguage(it))
                             .Where(l => l is not null)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                             .Cast<string>())
                {
                    AvailableLanguages.Add(lang);
                }
            }

            ClearSelection();
            HasResults = Results.Count > 0;
            EmptyHint = Results.Count == 0
                ? (Main.HasGlobalTagFilters
                    ? $"没有同时带 {string.Join(" + ", Main.GlobalTagFilters.Select(t => "#" + t.Name))} 的条目"
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

    /// <summary>
    /// 浏览模式：无关键词、无标签时展示最近条目（工具栏的来源 / 排序 / 隐藏状态同样生效）。
    /// 对齐扩展：清空搜索框后回到浏览列表，而不是留一页空白。
    /// </summary>
    private async Task LoadBrowseAsync(CancellationToken token)
    {
        IsSearching = true;
        StatusText = "加载中...";
        Results.Clear();
        ExactResults.Clear();
        RelatedResults.Clear();
        ClearSelection();
        try
        {
            var source = CurrentSource switch
            {
                "star" => "githubstar",
                "bookmark" => "bookmark",
                _ => null,
            };
            var filter = new BrowseFilter
            {
                Sort = CurrentSort,
                TypeFilter = source,
                IncludeHidden = ShowHidden,
                Limit = 100,
            };
            var items = _repo is not null
                ? await _repo.GetAllAsync(filter, token)
                : Array.Empty<Item>();
            if (token.IsCancellationRequested) return;

            foreach (var it in items)
            {
                var vm = new ItemCardViewModel(it);
                Results.Add(vm);
                RelatedResults.Add(vm);
            }
            HasExact = false;
            HasRelated = Results.Count > 0;
            ExactHeader = "精确匹配";
            RelatedHeader = $"最近条目 ({Results.Count})";
            HasResults = Results.Count > 0;
            EmptyHint = Results.Count == 0 ? "没有可展示的条目" : string.Empty;
            StatusText = $"最近 {Results.Count} 条";
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

    public void RemoveItem(long id)
    {
        var vm = Results.FirstOrDefault(r => r.Id == id);
        if (vm is null) return;
        Results.Remove(vm);
        ExactResults.Remove(vm);
        RelatedResults.Remove(vm);
        HasExact = ExactResults.Count > 0;
        HasRelated = RelatedResults.Count > 0;
        ExactHeader = $"精确匹配 ({ExactResults.Count})";
        RelatedHeader = $"相关结果 ({RelatedResults.Count})";
    }

    partial void OnQueryChanged(string value)
    {
        _ = SearchAsync();
    }

    partial void OnShowHiddenChanged(bool value) { _ = SearchAsync(); }

    partial void OnCurrentSourceChanged(string value) { _ = SearchAsync(); }

    partial void OnCurrentSortChanged(string value) { _ = SearchAsync(); }

    partial void OnCurrentLanguageChanged(string value) { _ = SearchAsync(); }

    /// <summary>解析条目主语言（仅 GitHubStar）。供语言下拉聚合。</summary>
    private static string? TryGetLanguage(Item it)
    {
        if (string.IsNullOrEmpty(it.ExtraJson)) return null;
        try
        {
            var meta = System.Text.Json.JsonSerializer.Deserialize<StarMark.Abstractions.GitHubStarMeta>(it.ExtraJson);
            return string.IsNullOrEmpty(meta?.Language) ? null : meta.Language;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    // ───────── 全局标签筛选（单一真源 = Main.GlobalTagFilters，对齐扩展 tagFilters）─────────

    /// <summary>
    /// 全局标签筛选变化（导航栏「标签」增删/清除）→ 若已有查询词或标签，按新组合重搜。
    /// 搜索始终在标签筛选结果之内执行（AND 语义）。
    /// </summary>
    private void OnGlobalTagFiltersChanged()
    {
        if (Query.Length > 0 || Main.HasGlobalTagFilters)
            _ = SearchAsync();
    }
}

/// <summary>吸顶标签筛选条上的一枚标签（带 ✕ 可移除）。</summary>
public partial class TagFilterChip : ObservableObject
{
    public string Name { get; }

    public TagFilterChip(string name) => Name = name;
}
