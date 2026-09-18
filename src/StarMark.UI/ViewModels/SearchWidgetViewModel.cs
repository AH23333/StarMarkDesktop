#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Abstractions;
using StarMark.Core.Search;

namespace StarMark.UI.ViewModels;

/// <summary>搜索组件中的标签 chip（名称 + 命中数 + 是否选中）。</summary>
public sealed partial class TagChip : ObservableObject
{
    public string Name { get; }
    public int Count { get; }

    [ObservableProperty]
    private bool _selected;

    public TagChip(string name, int count, bool selected)
    {
        Name = name;
        Count = count;
        _selected = selected;
    }
}

/// <summary>搜索结果行（桌面组件内联展示，复用统一条目模型）。</summary>
public sealed record SearchResultItem(long Id, string Title, string Subtitle, string Uri, string Emoji);

/// <summary>
/// 搜索组件 ViewModel（R2 试点，顺带实现「多标签 AND 搜索」桌面版）：
/// 关键词 + 多选标签（AND 语义）联合过滤，结果内联展示。
/// 空关键词 + 有标签时退化为「按标签浏览」（复用 BrowseFilter 的 AND 标签子句）。
/// </summary>
public sealed class SearchWidgetViewModel
{
    private readonly IItemRepository? _repo;
    private readonly SearchService? _search;

    public ObservableCollection<TagChip> Tags { get; } = new();
    public ObservableCollection<SearchResultItem> Results { get; } = new();

    public string Query { get; set; } = string.Empty;

    public int ResultCount => Results.Count;

    public bool HasResults => Results.Count > 0;

    public SearchWidgetViewModel(IItemRepository? repo, SearchService? search = null)
    {
        _repo = repo;
        _search = search;
    }

    /// <summary>加载全部标签（按命中数倒序，最多展示前 60 个，避免超长标签云卡顿）。</summary>
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

    public void ToggleTag(string name)
    {
        var chip = Tags.FirstOrDefault(t => t.Name == name);
        if (chip is null) return;
        chip.Selected = !chip.Selected;
        _ = RunSearchAsync();
    }

    public void ClearTags()
    {
        foreach (var chip in Tags)
            if (chip.Selected) chip.Selected = false;
        _ = RunSearchAsync();
    }

    public async Task RunSearchAsync()
    {
        Results.Clear();
        var selected = Tags.Where(t => t.Selected).Select(t => t.Name).ToList();
        var q = (Query ?? string.Empty).Trim();

        // 统一搜索编排（与主窗口 SearchPage 同源）：FTS5 + Everything 实时源合并去重，
        // 未入库的本地文件（Everything 虚拟条目）由此可达；
        // 空关键词 + 标签退化为按标签浏览（SearchService 内部同规则）。
        if (_search is not null)
        {
            try
            {
                var result = await _search.SearchAsync(q, new SearchFilter { Tags = selected, MaxResults = 200 }, CancellationToken.None);
                foreach (var it in result.Items)
                    Results.Add(new SearchResultItem(it.Id, it.Title, it.Subtitle, it.Uri, EmojiFor(it.Type)));
            }
            catch (Exception ex)
            {
                StarMark.Abstractions.StarLog.Error("桌面搜索失败", ex);
            }
            return;
        }

        // 兜底：无 SearchService 时退回仓库直查（仅 FTS / 标签浏览，无实时源）
        if (_repo is null) return;
        try
        {
            IReadOnlyList<Item> items;
            if (string.IsNullOrEmpty(q))
            {
                // 仅按标签浏览（AND 语义）
                items = await _repo.GetAllAsync(new BrowseFilter { TagFilters = selected, Limit = 200 }, CancellationToken.None);
            }
            else
            {
                var result = await _repo.SearchAsync(q, new SearchFilter { Tags = selected, MaxResults = 200 }, CancellationToken.None);
                items = result.Items;
            }

            foreach (var it in items)
                Results.Add(new SearchResultItem(it.Id, it.Title, it.Subtitle, it.Uri, EmojiFor(it.Type)));
        }
        catch (Exception ex)
        {
            StarMark.Abstractions.StarLog.Error("桌面搜索失败", ex);
        }
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
