#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Core.Search;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;

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

/// <summary>
/// 条目格里的一行（复用统一条目模型，渲染与主窗口一致）。
/// 携带 <paramref name="Source"/>/<paramref name="SourceId"/> 业务键：搜索结果格合并 Everything 实时源时，
/// 虚拟行（Id=0）不入库，右键「记录到本地」/置顶/标签等需据此幂等登记真实条目（见 <c>ItemCardActions.EnsureRecordedAsync</c>）。
/// </summary>
public sealed record ItemRowItem(long Id, string Title, string Subtitle, string Uri, string Emoji, ItemType Type, string Source, string SourceId);

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

    /// <summary>数据变更同步器：主界面置顶/取消置顶、改标签、删除条目后本组件自动重载。</summary>
    private readonly DataChangeReloader _sync;

    /// <summary>加载闸门：广播与用户操作可能同时触发，串行化避免重复打库。</summary>
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public ItemGridMode Mode { get; }

    public ObservableCollection<ItemRowItem> Items { get; } = new();

    /// <summary>最近活动格（#51）专用：用户主动的增/删/改事件流（非条目本身），按时间倒序。</summary>
    public ObservableCollection<ActivityItemViewModel> Events { get; } = new();

    /// <summary>标签格所钉的标签名。</summary>
    public string? GridTag { get; private set; }

    /// <summary>搜索结果格所钉的关键词。</summary>
    public string? Query { get; set; }

    /// <summary>搜索结果格所钉的标签过滤（AND 语义）。</summary>
    public ObservableCollection<TagChip> Tags { get; } = new();

    /// <summary>搜索结果格排序键：relevance（相关度，默认）/ recent（最近更新）/ name（名称）。与快捷搜索同源。</summary>
    public string Sort { get; private set; } = "relevance";

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
        _sync = new DataChangeReloader(LoadAsync);
        _ = LoadAsync();
    }

    /// <summary>退订数据广播 / 释放闸门（组件卸载时调用）。</summary>
    public void Dispose()
    {
        _sync.Dispose();
        _loadGate.Dispose();
    }

    private void LoadConfig()
    {
        var inst = _storage.FindInstance(_instanceId);
        if (inst is null) return;
        GridTag = inst.GridTag;
        Query = inst.GridQuery;
        Sort = string.IsNullOrWhiteSpace(inst.GridSort) ? "relevance" : inst.GridSort;
        Tags.Clear();
        if (inst.GridTags is { Count: > 0 })
            foreach (var t in inst.GridTags)
                Tags.Add(new TagChip(t, 0, true));
    }

    public async Task LoadAsync()
    {
        if (_repo is null) return;
        await _loadGate.WaitAsync();
        try
        {
            // 最近活动格（#51）：读用户主动增/删/改的「事件流」，而不是「最近更新条目」。
            if (Mode == ItemGridMode.Activity)
            {
                var recs = await _repo.GetActivityAsync(200, CancellationToken.None);
                ApplyEvents(recs);
                return;
            }

            IReadOnlyList<Item> items = Mode switch
            {
                ItemGridMode.Tag => await _repo.GetAllAsync(
                    new BrowseFilter { TagFilters = new List<string> { GridTag ?? string.Empty }, Limit = 200 }, CancellationToken.None),
                ItemGridMode.Search => _search is not null
                    ? (await _search.SearchAsync(Query ?? string.Empty,
                        new SearchFilter { Tags = Tags.Where(t => t.Selected).Select(t => t.Name).ToList(), MaxResults = 200, Sort = Sort }, CancellationToken.None)).Items
                    : (await _repo.SearchAsync(Query ?? string.Empty,
                        new SearchFilter { Tags = Tags.Where(t => t.Selected).Select(t => t.Name).ToList(), MaxResults = 200, Sort = Sort }, CancellationToken.None)).Items,
                ItemGridMode.Pinned => await _repo.GetPinnedAsync(200, CancellationToken.None),
                _ => Array.Empty<Item>(),
            };

            var rows = items
                .Select(it => new ItemRowItem(it.Id, it.Title, it.Subtitle, it.Uri, EmojiFor(it.Type), it.Type, it.Source, it.SourceId))
                .ToList();
            ApplyRows(rows);
        }
        catch (Exception ex)
        {
            StarMark.Abstractions.StarLog.Error("加载条目格失败", ex);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    /// <summary>
    /// 把 <see cref="Items"/> 增量对齐到查询结果。
    /// 早先是「先 Clear 再逐条 Add」——数据一变就这么来一次，列表会闪一下白并重建所有行容器；
    /// 置顶条目格挂在桌面上常驻，隔几秒闪一次是不能接受的。改成按 id 做最小差异后，
    /// 内容没变时是<b>零操作</b>。
    /// </summary>
    private void ApplyRows(List<ItemRowItem> rows)
    {
        var targetIds = new HashSet<long>(rows.Select(r => r.Id));

        for (var i = Items.Count - 1; i >= 0; i--)
            if (!targetIds.Contains(Items[i].Id)) Items.RemoveAt(i);

        for (var i = 0; i < rows.Count; i++)
        {
            var want = rows[i];
            if (i < Items.Count && Items[i].Id == want.Id)
            {
                if (Items[i] != want) Items[i] = want;   // 标题/链接变了
                continue;
            }

            var at = -1;
            for (var j = i + 1; j < Items.Count; j++)
                if (Items[j].Id == want.Id) { at = j; break; }

            if (at >= 0) Items.Move(at, i);
            else Items.Insert(i, want);
        }

        while (Items.Count > rows.Count) Items.RemoveAt(Items.Count - 1);
    }

    /// <summary>
    /// 最近活动格的事件流增量对齐（#51）：按事件 id 做最小差异，避免每次数据广播都 Clear 重建导致常驻组件闪一下白。
    /// 事件按时间倒序，新事件天然落在最前。
    /// </summary>
    private void ApplyEvents(IReadOnlyList<ActivityRecord> recs)
    {
        var target = recs.Select(r => new ActivityItemViewModel(r)).ToList();
        var targetIds = new HashSet<long>(target.Select(t => t.Id));

        for (var i = Events.Count - 1; i >= 0; i--)
            if (!targetIds.Contains(Events[i].Id)) Events.RemoveAt(i);

        for (var i = 0; i < target.Count; i++)
        {
            var want = target[i];
            if (i < Events.Count && Events[i].Id == want.Id) continue;   // 同一事件，标题/时间是快照，不重绘
            var at = -1;
            for (var j = i + 1; j < Events.Count; j++)
                if (Events[j].Id == want.Id) { at = j; break; }
            if (at >= 0) Events.Move(at, i);
            else Events.Insert(i, want);
        }

        while (Events.Count > target.Count) Events.RemoveAt(Events.Count - 1);
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

    /// <summary>搜索结果格：切换排序键（relevance/recent/name）并持久化后重载。与快捷搜索排序下拉同源。</summary>
    public void ApplySort(string sort)
    {
        var next = string.IsNullOrWhiteSpace(sort) ? "relevance" : sort;
        if (Sort == next) return;
        Sort = next;
        SaveConfig();
        _ = LoadAsync();
    }

    /// <summary>当前排序键映射到下拉索引（0 相关度 / 1 最近更新 / 2 名称），供视图初始化选中项。</summary>
    public int SortIndex => Sort switch { "recent" => 1, "name" => 2, _ => 0 };

    public void ToggleTag(string name)
    {
        var chip = Tags.FirstOrDefault(t => t.Name == name);
        if (chip is null) return;
        chip.Selected = !chip.Selected;
        SaveConfig();   // 持久化标签选择：搜索结果格是「钉一条查询」的常驻组件，选/取消标签须与钉查询一样落盘，
                        // 否则重启/重建后 LoadConfig 恢复不到刚才的标签过滤（表现为「标签选择无结果」的另一半）。
        _ = LoadAsync();
    }

    public void ClearTags()
    {
        foreach (var chip in Tags)
            if (chip.Selected) chip.Selected = false;
        SaveConfig();
        _ = LoadAsync();
    }

    public async Task LoadTagsAsync()
    {
        if (_repo is null) return;
        // 记住重建前已选的标签（含构造函数里 LoadConfig 刚从 widgets.json 恢复的钉选标签）。
        // 早先这里无条件 Tags.Clear() 再把每个 chip 按 selected:false 重建，会：
        // ① 抹掉 LoadConfig 恢复的钉选态；② 与构造函数首轮 _ = LoadAsync() 竞争，
        //    让 LoadAsync 读到空的标签集 → 钉了标签却「无结果」。改为按名字保留选择态。
        var selected = new HashSet<string>(Tags.Where(t => t.Selected).Select(t => t.Name));
        try
        {
            var tags = await _repo.GetAllTagsAsync(CancellationToken.None);
            var cloud = tags.Take(60).ToList();
            // 钉选但不在前 60 热门标签里的，补进标签云，保证其选择态可见、可再点取消。
            foreach (var name in selected)
                if (!cloud.Any(t => t.Name == name))
                    cloud.Add((name, 0));
            Tags.Clear();
            foreach (var (name, count) in cloud)
                Tags.Add(new TagChip(name, count, selected.Contains(name)));
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
        inst.GridSort = Sort == "relevance" ? null : Sort;   // null=默认相关度，避免给非搜索格/默认态写冗余字段
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
