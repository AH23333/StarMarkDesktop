#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;

namespace StarMark.UI.ViewModels;

/// <summary>差异化条目格的查询模式（StarMark 护城河：全部基于统一 items 表）。</summary>
public enum ItemGridMode
{
    /// <summary>标签格：某标签条目常驻。</summary>
    Tag,
    /// <summary>最近活动格：按 updated_at 展示最近条目。</summary>
    Activity,
    /// <summary>置顶条目格：pinned=1 的条目。</summary>
    Pinned,
    /// <summary>
    /// 剪贴板格：本机复制历史常驻（<c>source='clipboard'</c>）。<b>展示型组件</b>——
    /// 与置顶条目格同构：一行一条、点一条把它再复制回剪贴板、右键是与主窗逐条一致的条目菜单、
    /// 并订阅数据广播实时刷新（复制一条就当场出现在桌面上）。
    /// </summary>
    Clipboard,
}

/// <summary>
/// 条目格里的一行（复用统一条目模型，渲染与主窗口一致）。
/// <b>必须带上 <paramref name="Source"/>/<paramref name="Type"/> 等业务键</b>：右键菜单由共享工厂按需现查库构建，
/// 查不到时要用行数据兜底建条目（<c>ItemContextMenu.ShowForItem</c> 的 fallback）——而
/// 「这条是不是内置剪贴板历史（可删）」「打开=复制还是启动」这类判据全都依赖 Type/Source。
/// </summary>
public sealed record ItemRowItem(long Id, string Title, string Subtitle, string Uri, string Emoji, ItemType Type, string Source, string SourceId);

/// <summary>
/// 差异化条目格 ViewModel（Phase A-2，StarMark 护城河）：
/// 标签格 / 剪贴板格 / 最近活动格 / 置顶条目格，全部查询统一 <c>items</c> 表，
/// 与 DeskBox 的"文件收纳"路线完全区分。只有标签格支持组件内配置（钉标签）并持久化到 widgets.json；
/// 其余三格的内容就是"当前库里最近的那一份"，没有可钉参数。
/// 抄 DeskBox 思路：内容只读查询、外壳由 WidgetWindow 承载。
/// </summary>
public sealed class ItemGridWidgetViewModel
{
    private readonly IItemRepository? _repo;
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

    /// <summary>是否仍需要用户配置（标签格还没钉标签）。</summary>
    public bool NeedsConfig => Mode == ItemGridMode.Tag && string.IsNullOrWhiteSpace(GridTag);

    /// <summary>该格是否需要配置栏（只有标签格有可钉参数）。</summary>
    public bool IsConfigurable => Mode == ItemGridMode.Tag;

    /// <summary>剪贴板格最多铺几条：桌面上的展示型组件要"一眼看完最近"，完整列表在「剪贴板」页里看。</summary>
    public const int ClipboardLimit = 100;

    /// <summary>
    /// 空列表时该说什么。<b>剪贴板格不能只说"暂无条目"</b>：一片空白最容易被读成"组件坏了"，
    /// 而真实原因有四种（没开启 / 开着但监听窗没建立 / 正在暂停 / 真的还没复制过），
    /// 每种对应的下一步动作都不一样——照实说是省掉一轮"是不是 bug"来回沟通的唯一办法。
    /// </summary>
    public string EmptyStateText => Mode switch
    {
        ItemGridMode.Clipboard when !App.IsClipboardHistoryEnabled()
            => "剪贴板历史还没开启：到「设置 → 数据 → 剪贴板历史」打开后，复制过的内容会自动出现在这里。",
        ItemGridMode.Clipboard when !App.IsClipboardCollecting
            => "开关已打开，但采集窗口没建立——期间不会记录任何复制。到设置里把该开关关掉再打开即可重试。",
        ItemGridMode.Clipboard when App.IsClipboardPaused
            => "正在暂停记录（临时粘贴私密内容用的）。到「剪贴板」页关掉「暂停记录」就会继续采集。",
        ItemGridMode.Clipboard
            => "还没有记录到任何复制内容。复制一段文字，它就会出现在这里。",
        ItemGridMode.Activity
            => "暂无活动记录。新增 / 删除 / 修改条目（含待办、随记、快捷入口、笔记、标签）后会显示在这里。",
        _ => NeedsConfig ? "先在上方配置要钉的内容" : "暂无条目",
    };

    public ItemGridWidgetViewModel(ItemGridMode mode, WidgetStorage storage, IItemRepository? repo, string instanceId, WidgetKind kind)
    {
        Mode = mode;
        _storage = storage;
        _repo = repo;
        _instanceId = instanceId;
        _kind = kind;
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
                ItemGridMode.Pinned => await _repo.GetPinnedAsync(200, CancellationToken.None),
                // 剪贴板格：只读本机采集的那一份（source 限定 ⇒ 不会把 Ditto 的历史混进来，
                // 那类行的正文属于外部程序，展示在这里会诱导用户去删别人的库）。
                ItemGridMode.Clipboard => (await _repo.GetBySourceAsync(
                        ItemSources.Clipboard, ItemType.Clipboard, ClipboardLimit, CancellationToken.None))
                    // 与「剪贴板」页同一口径：置顶优先，其余按最近复制（仓储已按 updated_at 倒序）。
                    .OrderByDescending(i => i.Pinned).ToList(),
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

    private void SaveConfig()
    {
        var data = _storage.Load();
        var inst = WidgetStorage.GetOrAddInstance(data, _instanceId, _kind);
        inst.GridTag = GridTag;
        _storage.Save(data);
    }

    public static string EmojiFor(ItemType t) => t switch
    {
        ItemType.File => "📁",
        ItemType.Bookmark => "🔖",
        ItemType.GitHubStar => "⭐",
        ItemType.Clipboard => "📋",
        ItemType.Todo => "✅",
        ItemType.Note => "📝",
        _ => "📌",
    };
}
