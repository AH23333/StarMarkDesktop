#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Core.Search;
using StarMark.Core.Widgets;

namespace StarMark.UI.ViewModels;

/// <summary>差异化条目格的查询模式（StarMark 护城河：全部基于统一 items 表）。</summary>
public enum ItemGridMode
{
    /// <summary>标签格：某标签条目常驻。</summary>
    Tag,
    /// <summary>搜索结果格：钉一条查询常驻。</summary>
    Search,
    /// <summary>最近活动格：按 updated_at 展示最近条目。</summary>
    Activity,
    /// <summary>置顶条目格：pinned=1 的条目。</summary>
    Pinned,
}

/// <summary>条目格里的一行（复用统一条目模型，渲染与主窗口一致）。</summary>
public sealed record ItemRowItem(long Id, string Title, string Subtitle, string Uri, string Emoji, ItemType Type);

/// <summary>
/// 差异化条目格 ViewModel（Phase A-2，StarMark 护城河）：
/// 标签格 / 搜索结果格 / 最近活动格 / 置顶条目格，全部查询统一 <c>items</c> 表，
/// 与 DeskBox 的"文件收纳"路线完全区分。标签格与搜索结果格支持组件内配置（钉标签/钉查询）并持久化到 widgets.json。
/// 抄 DeskBox 思路：内容只读查询、外壳由 WidgetWindow 承载；复用 SearchWidget 的仓库直查兜底。
/// </summary>
public sealed class ItemGridWidgetViewModel
{
    private readonly IItemRepository? _repo;
    private readonly SearchService? _search;
    private readonly WidgetStorage _storage;
    private readonly string _instanceId;
    private readonly WidgetKind _kind;

    public ItemGridMode Mode { get; }

    public ObservableCollection<ItemRowItem> Items { get; } = new();

    /// <summary>标签格所钉的标签名。</summary>
    public string? GridTag { get; private set; }

    /// <summary>搜索结果格所钉的关键词。</summary>
    public string? Query { get; set; }

    /// <summary>搜索结果格所钉的标签过滤（AND 语义）。</summary>
    public ObservableCollection<TagChip> Tags { get; } = new();

    /// <summary>是否仍需要用户配置（标签格缺标签 / 搜索格缺关键词）。</summary>
    public bool NeedsConfig =>
        Mode == ItemGridMode.Tag ? string.IsNullOrWhiteSpace(GridTag)
        : Mode == ItemGridMode.Search ? string.IsNullOrWhiteSpace(Query)
        : false;

    /// <summary>该格是否需要配置栏（仅标签格 / 搜索结果格）。</summary>
    public bool IsConfigurable => Mode is ItemGridMode.Tag or ItemGridMode.Search;

    public ItemGridWidgetViewModel(ItemGridMode mode, WidgetStorage storage, IItemRepository? repo, string instanceId, WidgetKind kind, SearchService? search = null)
    {
        Mode = mode;
        _storage = storage;
        _repo = repo;
        _instanceId = instanceId;
        _kind = kind;
        _search = search;
        LoadConfig();
        _ = LoadAsync();
    }

    private void LoadConfig()
    {
        var inst = _storage.FindInstance(_instanceId);
        if (inst is null) return;
        GridTag = inst.GridTag;
        Query = inst.GridQuery;
        Tags.Clear();
        if (inst.GridTags is { Count: > 0 })
            foreach (var t in inst.GridTags)
                Tags.Add(new TagChip(t, 0, true));
    }

    public async Task LoadAsync()
    {
        Items.Clear();
        if (_repo is null) return;
        try
        {
            IReadOnlyList<Item> items = Mode switch
            {
                ItemGridMode.Tag => await _repo.GetAllAsync(
                    new BrowseFilter { TagFilters = new List<string> { GridTag ?? string.Empty }, Limit = 200 }, CancellationToken.None),
                ItemGridMode.Search => _search is not null
                    ? (await _search.SearchAsync(Query ?? string.Empty,
                        new SearchFilter { Tags = Tags.Where(t => t.Selected).Select(t => t.Name).ToList(), MaxResults = 200 }, CancellationToken.None)).Items
                    : (await _repo.SearchAsync(Query ?? string.Empty,
                        new SearchFilter { Tags = Tags.Where(t => t.Selected).Select(t => t.Name).ToList(), MaxResults = 200 }, CancellationToken.None)).Items,
                ItemGridMode.Activity => await _repo.GetRecentAsync(200, CancellationToken.None),
                ItemGridMode.Pinned => await _repo.GetPinnedAsync(200, CancellationToken.None),
                _ => Array.Empty<Item>(),
            };

            foreach (var it in items)
                Items.Add(new ItemRowItem(it.Id, it.Title, it.Subtitle, it.Uri, EmojiFor(it.Type), it.Type));
        }
        catch (Exception ex)
        {
            StarMark.Abstractions.StarLog.Error("加载条目格失败", ex);
        }
    }

    /// <summary>标签格：设置所钉标签并持久化后重载。</summary>
    public void ApplyTagConfig(string tag)
    {
        GridTag = tag.Trim();
        SaveConfig();
        _ = LoadAsync();
    }

    /// <summary>搜索结果格：设置所钉关键词并持久化后重载。</summary>
    public void ApplySearchConfig(string query)
    {
        Query = query.Trim();
        SaveConfig();
        _ = LoadAsync();
    }

    public void ToggleTag(string name)
    {
        var chip = Tags.FirstOrDefault(t => t.Name == name);
        if (chip is null) return;
        chip.Selected = !chip.Selected;
        _ = LoadAsync();
    }

    public void ClearTags()
    {
        foreach (var chip in Tags)
            if (chip.Selected) chip.Selected = false;
        _ = LoadAsync();
    }

    public async Task LoadTagsAsync()
    {
        Tags.Clear();
        if (_repo is null) return;
        try
        {
            var tags = await _repo.GetAllTagsAsync(CancellationToken.None);
            foreach (var (name, count) in tags.Take(60))
                Tags.Add(new TagChip(name, count, false));
        }
        catch (Exception ex)
        {
            StarMark.Abstractions.StarLog.Error("加载标签云失败", ex);
        }
    }

    private void SaveConfig()
    {
        var data = _storage.Load();
        var inst = WidgetStorage.GetOrAddInstance(data, _instanceId, _kind);
        inst.GridTag = GridTag;
        inst.GridQuery = Query;
        inst.GridTags = Tags.Where(t => t.Selected).Select(t => t.Name).ToList();
        _storage.Save(data);
    }

    public static string EmojiFor(ItemType t) => t switch
    {
        ItemType.File => "📁",
        ItemType.Bookmark => "🔖",
        ItemType.GitHubStar => "⭐",
        ItemType.Todo => "✅",
        ItemType.Note => "📝",
        _ => "📌",
    };
}
