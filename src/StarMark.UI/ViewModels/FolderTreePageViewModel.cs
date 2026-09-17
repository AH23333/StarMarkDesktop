#nullable enable
using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using StarMark.Abstractions;
using StarMark.UI.Helpers;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 文件夹页面 ViewModel。构建语义文件夹树（书签 → FolderPaths 层级；文件 → file:// 目录；GitHub/剪贴板 → 伪根）。
/// 渲染层用 TreeView 按需展开 + 惰性挂载条目，缩进/展开/虚拟化交给控件。
/// </summary>
public partial class FolderTreePageViewModel : ObservableObject
{
    public const int MaxItemsPerFolder = 30;

    /// <summary>每次「展开更多」追加的条目数。</summary>
    public const int ExpandMoreStep = 50;

    private readonly IItemRepository _repository;

    public MainViewModel Main { get; }

    // 加载串行化：避免并发 Roots.Clear/Add 竞态与 RebuildTree 重入重建（点击标签/实时改标签
    // 多次触发 Load 时曾导致主界面卡死/崩溃）。同一时刻仅一个加载在跑，后续请求合并为一次。
    private readonly object _loadGate = new();
    private bool _loadRunning;
    private bool _loadPending;
    // 条目实时改标签去抖：连续改标签合并为一次全量重载，避免风暴式刷新。
    private Timer? _tagChangeTimer;

    // UI 调度器：ViewModel 由 DI 在 UI 线程创建，捕获后用于把集合变更封送回 UI 线程，
    // 避免 Timer/线程池回调里直接改 ObservableCollection 触发 RPC_E_WRONG_THREAD（卡死/崩溃）。
    private readonly DispatcherQueue? _ui = DispatcherQueue.GetForCurrentThread();

    private Task RunOnUi(Action action)
    {
        if (_ui is null) { action(); return Task.CompletedTask; }
        if (_ui.HasThreadAccess) { action(); return Task.CompletedTask; }
        var tcs = new TaskCompletionSource();
        _ui.TryEnqueue(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    [ObservableProperty] private bool _hasTagFilter;

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

    public FolderTreePageViewModel(IItemRepository repository, MainViewModel main)
    {
        _repository = repository;
        Main = main;
        // 全局标签筛选变化（标签页增删/清除）→ 重载树
        Main.GlobalTagFiltersChanged += OnGlobalTagFiltersChanged;
        // 语言下拉变化 → 仅显示匹配语言的 star 条目，重载树
        Main.LanguageFilterChanged += OnLanguageFilterChanged;
        // 条目自身标签被增删 → 当前浏览树可能应纳入/剔除该条目，实时重载（仅文件夹页激活时）
        ItemCardActions.ItemTagsChanged += OnItemTagsChanged;
    }

    private void OnGlobalTagFiltersChanged()
    {
        HasTagFilter = Main.HasGlobalTagFilters;
        _ = LoadCommand.ExecuteAsync(null);
    }

    private void OnLanguageFilterChanged()
    {
        _ = LoadCommand.ExecuteAsync(null);
    }

    private void OnItemTagsChanged()
    {
        if (Main.CurrentPageTag != "tree") return;
        // 去抖：连续增删标签合并为一次重载，避免每次编辑都全量重建树（卡顿/崩溃来源）。
        _tagChangeTimer?.Dispose();
        _tagChangeTimer = new Timer(_ =>
        {
            _tagChangeTimer = null;
            _ = LoadCommand.ExecuteAsync(null);
        }, null, 250, Timeout.Infinite);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        // 串行化执行：同一时刻仅一个加载在跑，后续并发请求合并为一次（_loadPending），
        // 避免 Roots.Clear/Add 竞态与 RebuildTree 重入重建（曾多次触发卡死/崩溃）。
        lock (_loadGate)
        {
            if (_loadRunning) { _loadPending = true; return; }
            _loadRunning = true;
        }
        try
        {
            do
            {
                _loadPending = false;
                await LoadCoreAsync();
            }
            while (_loadPending);
        }
        finally
        {
            lock (_loadGate) { _loadRunning = false; }
        }
    }

    /// <summary>真正执行加载：清空并重建 Roots，末尾触发 RootsReady 由页面重建树。
    /// 所有触及 Roots / IsLoading / EmptyHint（即会触发 PropertyChanged / CollectionChanged）的代码都封送回 UI 线程，
    /// 避免标签删除的去抖 Timer 在后台线程直接改集合导致 RPC_E_WRONG_THREAD 卡死/崩溃。</summary>
    private async Task LoadCoreAsync()
    {
        await RunOnUi(() => { IsLoading = true; });

        BrowseFilter filter;
        try
        {
            filter = new BrowseFilter
            {
                Sort = CurrentSort,
                IncludeHidden = ShowHidden,
                TypeFilter = CurrentSource switch { "all" => null, "star" => "githubstar", _ => CurrentSource },
                // 全局标签筛选（标签页多选，AND 语义）
                TagFilters = Main.HasGlobalTagFilters ? Main.GlobalTagFilters.Select(t => t.Name).ToList() : null,
                // 语言下拉直选（仅显示匹配语言的 star 条目）
                Language = string.IsNullOrEmpty(Main.CurrentLanguage) ? null : Main.CurrentLanguage,
                Limit = 2000,
            };
        }
        catch (Exception ex)
        {
            await RunOnUi(() => { EmptyHint = $"加载失败: {ex.Message}"; IsLoading = false; });
            return;
        }

        IReadOnlyList<Item> items;
        try
        {
            items = await _repository.GetAllAsync(filter, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await RunOnUi(() => { EmptyHint = $"加载失败: {ex.Message}"; IsLoading = false; });
            return;
        }

        try
        {
            await RunOnUi(() =>
            {
                Roots.Clear();
                var nodeMap = new Dictionary<string, FolderPathNodeViewModel>(StringComparer.OrdinalIgnoreCase);
                var pinnedItems = new List<Item>();
                foreach (var item in items)
                {
                    if (item.Pinned) pinnedItems.Add(item);
                    var path = FolderPathUtil.GetSegments(item);
                    var node = GetOrCreateNode(nodeMap, path);
                    node.Items.Add(item);
                }

                foreach (var root in nodeMap.Values.Where(n => n.Parent == null)
                                                 .OrderBy(n => n.RootOrder)
                                                 .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
                    Roots.Add(root);

                // 置顶伪根：聚合所有置顶条目，置顶后才有的可见入口（排序 RootOrder=-1 稳居首位）
                if (pinnedItems.Count > 0)
                {
                    var pinnedRoot = new FolderPathNodeViewModel(FolderPathUtil.PinnedGroup, null);
                    foreach (var item in pinnedItems)
                        pinnedRoot.Items.Add(item);
                    Roots.Insert(0, pinnedRoot);
                }

                EmptyHint = Roots.Count == 0
                    ? (HasTagFilter ? "所选标签下没有条目，可在上方筛选栏移除或清除" : "暂无条目，请先同步数据")
                    : string.Empty;
                IsLoading = false;
                RootsReady?.Invoke();
            });
        }
        catch (Exception ex)
        {
            StarLog.Error("重建文件夹树失败", ex);
        }
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
        => HydrateRange(node, 0, MaxItemsPerFolder);

    public IReadOnlyList<ItemCardViewModel> HydrateRange(FolderPathNodeViewModel node, int skip, int take)
        => node.Items.Skip(skip).Take(take).Select(i => new ItemCardViewModel(i)).ToList();

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