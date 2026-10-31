#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Abstractions.I18n;
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

/// <summary>搜索结果行（桌面组件内联展示）。可被 ↑↓ 键盘选中并高亮，故为 ObservableObject。</summary>
public sealed partial class SearchResultItem : ObservableObject
{
    public long Id { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Uri { get; }
    public string Emoji { get; }

    /// <summary>条目类型：右键菜单需据此判断「打开所在位置」等可见性，并为未入库的实时源虚拟条目（Id=0）兜底重建一条 Item。</summary>
    public ItemType Type { get; }

    /// <summary>业务键 (Source, SourceId)：Everything 实时源虚拟条目（Id=0）据此在右键「记录到本地」/置顶/标签时幂等登记真实条目。</summary>
    public string Source { get; }
    public string SourceId { get; }

    /// <summary>↑↓ 键盘导航时的选中高亮。</summary>
    [ObservableProperty] private bool _isSelected;

    public SearchResultItem(long id, string title, string subtitle, string uri, string emoji, ItemType type, string source, string sourceId)
    {
        Id = id; Title = title; Subtitle = subtitle; Uri = uri; Emoji = emoji; Type = type; Source = source; SourceId = sourceId;
    }
}

/// <summary>
/// 搜索组件 ViewModel（R2 试点，顺带实现「多标签 AND 搜索」桌面版）：
/// 关键词 + 多选标签（AND 语义）联合过滤，结果内联展示。
/// 空关键词 + 有标签时退化为「按标签浏览」（复用 BrowseFilter 的 AND 标签子句）。
/// </summary>
public sealed class SearchWidgetViewModel
{
    private readonly IItemRepository? _repo;
    private readonly SearchService? _search;

    // 序列化搜索：ToggleTag/ClearTags 以 _ = RunSearchAsync() fire-and-forget，连点会交叠，
    // 旧的 Clear→await→Add 与新的相撞导致结果翻倍。用 CTS 取消上一次、并在落结果前复查取消态。
    private CancellationTokenSource? _searchCts;

    // 搜索即输入去抖：连打只保留最后一次查询，避免每个按键都打一次 Everything IPC。
    private CancellationTokenSource? _debounceCts;

    public ObservableCollection<TagChip> Tags { get; } = new();
    public ObservableCollection<SearchResultItem> Results { get; } = new();

    public string Query { get; set; } = string.Empty;

    /// <summary>排序方式：relevance（相关度，默认）/ recent（最近更新）/ name（名称）。由视图排序下拉写入。</summary>
    public string Sort { get; set; } = "relevance";

    public int ResultCount => Results.Count;

    public bool HasResults => Results.Count > 0;

    /// <summary>当前结果是否来自「空态浏览最近条目」（用于区分空态文案：还没搜 / 没找到）。</summary>
    public bool IsBrowsing { get; private set; }

    /// <summary>空态提示（Results 为空时由视图展示）：交的是<b>「键＋实参」</b>，取词发生在宿主贴上去那一刻（批次 WI-67 片9b）。
    /// <para>留着一颗 <c>string</c> 属性＝那句话又被冻在"上一次算出它的那一门语言"里（✅片8 那颗 <c>WidgetsLoadHint</c> 同一形状）。</para></summary>
    public LocMessage? EmptyHint { get; private set; } = LocMessage.Of("widget.sw.empty.start");

    /// <summary>一轮搜索收尾时触发，通知视图刷新空态文案（Results 的 CollectionChanged 早于文案确定）。</summary>
    public event Action? SearchCompleted;

    // ───────── ↑↓ 键盘选中（对齐主窗口 SearchPage 的键盘导航）─────────
    private int _selectedIndex = -1;

    /// <summary>当前 ↑↓ 选中的结果；无选中返回 null。</summary>
    public SearchResultItem? Selected
        => _selectedIndex >= 0 && _selectedIndex < Results.Count ? Results[_selectedIndex] : null;

    /// <summary>当前选中项索引（-1 表示无）。供视图把选中行滚入视野。</summary>
    public int SelectedIndex => _selectedIndex;

    /// <summary>↑↓ 移动选中项（夹在 [0, count-1]，未选中时 ↓→首项）。</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) { _selectedIndex = -1; return; }
        var next = _selectedIndex < 0
            ? (delta > 0 ? 0 : Results.Count - 1)
            : Math.Clamp(_selectedIndex + delta, 0, Results.Count - 1);
        SetSelected(next);
    }

    private void SetSelected(int index)
    {
        if (_selectedIndex >= 0 && _selectedIndex < Results.Count)
            Results[_selectedIndex].IsSelected = false;
        _selectedIndex = index;
        if (index >= 0 && index < Results.Count)
            Results[index].IsSelected = true;
    }

    /// <summary>新一轮结果落地后复位选中：有结果即自动选中首项（对齐 DeskBox 首项自动选）。</summary>
    private void ResetSelectionAfterPopulate()
    {
        SetSelected(-1);
        if (Results.Count > 0) SetSelected(0);
    }

    /// <summary>
    /// 结果行的<b>唯一映射</b>：两颗显示出口（标题／副标题）当场按当时的语言念（✅P-199 同一条口径），
    /// 装载与换语言重算共用这一颗 ⇒ ⛔ 第二份写法。
    /// <para>类型名写全（⛔ 用省略写法 <c>new(...)</c>）：图标槽位那颗尺（<c>ItemTypeGlyphGateTests</c>）
    /// 是按"类型名＋左括号"找构造点的，省掉类型名＝把它的构造点藏走。注释里也⛔ 把那串字面量重打一遍——
    /// 那颗尺读的是整档原文（⛔ 先抹注释），一句注释就能给它造出一颗假构造点。</para>
    /// </summary>
    private static SearchResultItem RowOf(Item it) => new SearchResultItem(
        it.Id, ClipboardEntry.TitleDisplayOf(it), ClipboardEntry.SubtitleDisplayOf(it),
        it.Uri, ItemCardPolicy.GlyphOf(it.Type), it.Type, it.Source, it.SourceId);

    /// <summary>上一轮那批 <see cref="Item"/>：换语言那一刻按原顺序重念两颗显示出口，⛔ 再打一次库。</summary>
    private readonly List<Item> _rowsSource = new();

    private SearchResultItem PushRow(Item it)
    {
        _rowsSource.Add(it);
        return RowOf(it);
    }

    /// <summary>
    /// 换语言那一刻：把结果那一列按<b>已经在手的那一份</b>逐行重念标题与副标题。
    /// <para>片156 真机就地 en→ja 读到：那一格里标题、输入框提示、那颗按钮、排序标签、键位提示、
    /// 「タグ」与「結果」全改了口，只有结果那两行仍念 <c>Image</c>——它们住在
    /// <see cref="SearchResultItem"/> 的只读字符串槽里（不是 INPC，⛔ 有人报它）。</para>
    /// <para>按索引替换而⛔ <c>Clear()</c>：那颗会吃掉用户的选中位置，也让整列闪一次白；
    /// 也⛔ 在这里重新发起一次搜索（语言换了，命中的那批条目没换，而那一趟含实时源）。</para>
    /// </summary>
    public void ReresolveDisplay()
    {
        for (var i = 0; i < Results.Count && i < _rowsSource.Count; i++)
        {
            var fresh = RowOf(_rowsSource[i]);
            if (!Equals(Results[i], fresh)) Results[i] = fresh;
        }
    }

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

    /// <summary>
    /// 搜索即输入（对照 DeskBox SearchPopupWindow 的 35ms UI 去抖）：连打时只保留最后一次查询。
    /// 在 UI 线程 await Task.Delay（不 ConfigureAwait(false)），保证后续 Results 变更回到 UI 线程。
    /// </summary>
    public async Task SearchDebouncedAsync(int debounceMs = 120)
    {
        _debounceCts?.Cancel();
        var dts = _debounceCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(debounceMs, dts.Token);
        }
        catch (OperationCanceledException)
        {
            return;   // 有更新的输入进来，本轮放弃
        }
        finally
        {
            if (ReferenceEquals(_debounceCts, dts)) _debounceCts = null;
            dts.Dispose();
        }
        await RunSearchAsync();
    }

    public async Task RunSearchAsync()
    {
        _searchCts?.Cancel();                 // 取消上一次；其自身 finally 负责 Dispose
        var cts = _searchCts = new CancellationTokenSource();
        var ct = cts.Token;

        Results.Clear();
        _rowsSource.Clear();   // 与结果同生同灭：换语言重算按索引配这一份
        _selectedIndex = -1;
        IsBrowsing = false;
        var selected = Tags.Where(t => t.Selected).Select(t => t.Name).ToList();
        var q = (Query ?? string.Empty).Trim();

        try
        {
            // 空关键词 + 无标签：不再留白——展示最近条目（对齐主窗口 SearchPage 的浏览态、
            // 以及 DeskBox 空态推荐：搜索框空时先给出可点内容，而不是「无结果」的错觉）。
            // 经 BrowseFilter 走 GetAllAsync，使排序下拉（名称/最近更新）在浏览态同样生效。
            if (q.Length == 0 && selected.Count == 0)
            {
                IsBrowsing = true;
                EmptyHint = LocMessage.Of("widget.sw.empty.noEntries");
                if (_repo is not null)
                {
                    var recent = await _repo.GetAllAsync(
                        new BrowseFilter { Sort = Sort == "name" ? "name" : "recent", Limit = 60 }, ct);
                    if (ct.IsCancellationRequested) return;
                    foreach (var it in recent)
                        Results.Add(PushRow(it));
                    ResetSelectionAfterPopulate();
                }
                // 有货可看时那句空态⛔ 上屏（视图按 Results.Count 决定这一格可见性），所以这里交 null 而⛔ 再造一颗"最近条目"。
                EmptyHint = Results.Count == 0 ? LocMessage.Of("widget.sw.empty.noRecent") : null;
                return;
            }

            // 统一搜索编排（与主窗口 SearchPage 同源）：FTS5 + Everything 实时源合并去重，
            // 未入库的本地文件（Everything 虚拟条目）由此可达；
            // 空关键词 + 标签退化为按标签浏览（SearchService 内部同规则）。
            if (_search is not null)
            {
                var result = await _search.SearchAsync(q, new SearchFilter { Tags = selected, MaxResults = 200, Sort = Sort }, ct);
                if (ct.IsCancellationRequested) return;   // 已被更新的搜索取代，丢弃本次结果（即便 provider 未提前中断）
                foreach (var it in result.Items)
                    Results.Add(PushRow(it));
                ResetSelectionAfterPopulate();
                EmptyHint = EmptyMessageFor(q, selected);
                return;
            }

            // 兜底：无 SearchService 时退回仓库直查（仅 FTS / 标签浏览，无实时源）
            if (_repo is null) return;
            IReadOnlyList<Item> items;
            if (string.IsNullOrEmpty(q))
            {
                // 仅按标签浏览（AND 语义）
                items = await _repo.GetAllAsync(new BrowseFilter { TagFilters = selected, Sort = Sort == "relevance" ? "recent" : Sort, Limit = 200 }, ct);
            }
            else
            {
                var result = await _repo.SearchAsync(q, new SearchFilter { Tags = selected, MaxResults = 200, Sort = Sort }, ct);
                items = result.Items;
            }
            if (ct.IsCancellationRequested) return;
            foreach (var it in items)
                Results.Add(PushRow(it));
            ResetSelectionAfterPopulate();
            EmptyHint = EmptyMessageFor(q, selected);
        }
        catch (OperationCanceledException)
        {
            // 被更新的搜索取代：正常丢弃，不记为错误
            return;
        }
        catch (Exception ex)
        {
            StarMark.Abstractions.StarLog.Error("桌面搜索失败", ex);
            // 原先只有一句"搜索失败，请重试"：既没说为什么（库被占用？SQL 异常？来源抛的？），
            // 也没交代"要不要按什么按钮重试"——本组件重新输入关键词就会再跑一轮，把这两点写清楚（P-56）。
            EmptyHint = LocMessage.Of("widget.sw.empty.failed", ex.Message);
        }
        finally
        {
            cts.Dispose();
            // 只有"仍是本轮"时才清空字段，避免下一轮对已 Dispose 的 CTS 调 Cancel 抛 ObjectDisposedException。
            if (ReferenceEquals(_searchCts, cts)) _searchCts = null;
            if (!ct.IsCancellationRequested) SearchCompleted?.Invoke();
        }
    }

    /// <summary>无结果时按查询词 / 标签组合给出的空态：<b>与主窗搜索页共用同一颗键</b>（批次 WI-67 片9b）。
    /// <para>那两句从前在这两个 VM 里<b>各抄一份</b>——同一句话两个主人，改一处另一处静默留在旧措辞上。
    /// 标签那一串的连接符也交表（<c>search.tagJoin</c>）：⛔ 在 C# 里 <c>string.Join(" + ", …)</c> 念完，
    /// 那样连接符就被冻在算出它的那一刻（先例＝<c>capture.bar.nameJoin</c>，规则 #100）。</para></summary>
    private LocMessage? EmptyMessageFor(string q, IReadOnlyList<string> selected)
    {
        if (Results.Count > 0) return null;
        if (q.Length == 0 && selected.Count > 0)
            return LocMessage.Of("search.empty.tagAnd",
                new LocJoin(selected.Select(t => (object?)("#" + t)).ToList(), "search.tagJoin"));
        return LocMessage.Of("search.empty.noMatch", q);
    }
}
