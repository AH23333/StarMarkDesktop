#nullable enable
using Microsoft.UI.Dispatching;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// WidgetManager 的这一段——快捷启动的数据面：一条链接的增删、整批落库的唯一出口、活动记录。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class WidgetManager
{

    // ───────────────────────── 快捷入口 ─────────────────────────

    /// <summary>
    /// 一次拖入 N 个文件的落点：<b>整批只读一次档、只写一次盘、只广播一次</b>。
    /// <para>
    /// 为什么不能沿用逐条 <see cref="AddLinkAsync"/>：每条都要 <c>Load()</c> 再 <c>Save()</c>，
    /// 而 <c>Save()</c> 按设计必须把自己刚写过的快照作废（否则改标题后组件会读到旧内容），
    /// 于是读盘缓存在这条路径上完全帮不上忙——拖 20 个文件就是 20 次整档读 + 20 次整档写 + 20 次重绘。
    /// </para>
    /// </summary>
    /// <returns>真正加进去的条数。实例不存在、或整批都是已有入口时为 0，且<b>不落盘、不广播、不记活动</b>。</returns>
    public async Task<int> AddLinksAsync(string instanceId, IReadOnlyList<(string Title, string Uri)> links)
    {
        if (links is not { Count: > 0 }) return 0;
        var added = await OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return new List<(string, string)>();
            var taken = new HashSet<string>(inst.Links.Select(l => l.Uri), StringComparer.OrdinalIgnoreCase);
            var fresh = new List<(string, string)>();
            foreach (var (title, uri) in links)
            {
                if (string.IsNullOrWhiteSpace(uri)) continue;
                var target = uri.Trim();
                // 去重必须把"同一批里刚收下的那条"也算进来：只对着已落盘的列表比的话，
                // 一次拖进两个同名文件（资源管理器里复制同一文件两次是常事）会两条都塞进去，
                // 界面上就多出一个点开后是同一个地方的重复入口。
                if (!taken.Add(target)) continue;
                var shown = string.IsNullOrWhiteSpace(title) ? target : title.Trim();
                inst.Links.Add(new LinkItem
                {
                    Id = WidgetStorage.NewId(),
                    Title = shown,
                    Uri = target,
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                });
                fresh.Add((shown, target));
            }
            if (fresh.Count == 0) return fresh;             // 没有净变化 ⇒ 一次盘也不写、一次也不广播
            _storage.Save(data);
            return fresh;
        });
        if (added.Count == 0) return 0;

        LinksChanged?.Invoke(instanceId);                  // 整批一次：N 次增量刷新压成 1 次
        // 用户拖入 / 发送一个快捷入口 → 活动流记「新增」（绿）。快捷入口非 items 行，item_key 留空。#51。
        await LogActivitiesAsync(ActivityKind.ItemAdd, added);
        return added.Count;
    }

    /// <summary>新增单个快捷入口（去重）；自动定位到该实例（若该实例窗口已开则增量刷新）。
    /// 走的是 <see cref="AddLinksAsync"/> 那条批处理路，只是长度为 1——判据只留一处。</summary>
    public async Task<bool> AddLinkAsync(string instanceId, string title, string uri)
        => await AddLinksAsync(instanceId, new[] { (title, uri) }) > 0;

    /// <summary>从主窗口卡片「发送到快捷启动」：落到首个快捷启动实例，无则新建一个实例并显示。</summary>
    public async Task<bool> AddLinkToQuickLaunchAsync(string title, string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        var id = await GetOrCreateQuickLaunchInstanceIdAsync();
        if (id is null) return false;
        return await AddLinkAsync(id, title, uri);
    }

    /// <summary>
    /// 把拖入快捷启动的一批外部文件/文件夹<b>按路径登记进主库</b>（触发器2「拖入即入库」）。
    /// 以与 Everything 查询/全量索引<b>完全一致</b>的 <c>(filesystem, 路径哈希)</c> 业务键幂等 upsert
    /// （<see cref="LocalFileIdentity"/>），故同一路径无论来自拖入、Everything 还是后台同步都合并为一条，不产生重复。
    /// <b>只写索引记录，绝不移动 / 改名 / 删除磁盘上的实际文件。</b>与快捷入口链接并存：链接负责在本组件展示，
    /// 登记负责使其成为可检索、可持久化置顶/标签/笔记的真实条目。活动流由调用侧的 <see cref="AddLinksAsync"/>
    /// 按真正新增的那些入口记「新增」，此处不重复记录。
    /// <para>整批<b>一次连接、一个事务</b>（逐条登记时拖 20 个文件要开 20 次库、每次还带三遍 PRAGMA），
    /// 因此语义是"要么全记要么全不记"：一次拖放就是一个动作，留下半套登记比整批没记更难解释。
    /// 缺仓储或写库失败仅记日志并静默降级（不影响快捷入口本身）。</para>
    /// </summary>
    /// <returns>真正登记的条数。</returns>
    public async Task<int> RecordPathsToLibraryAsync(IReadOnlyList<(string? Title, string Path)> paths)
    {
        if (_repo is null || paths is not { Count: > 0 }) return 0;
        try
        {
            var drafts = new List<Item>(paths.Count);
            foreach (var (title, path) in paths)
                if (!string.IsNullOrWhiteSpace(path)) drafts.Add(LocalFileIdentity.FromPath(path, title));
            if (drafts.Count == 0) return 0;
            return await _repo.RecordItemsAsync(drafts, CancellationToken.None);
        }
        catch (Exception ex)
        {
            StarLog.Error($"拖入登记本地路径失败（本批 {paths.Count} 项）", ex);
            return 0;
        }
    }

    /// <summary>
    /// 「一批本地路径 → 某个快捷启动实例」的<b>唯一落库形状</b>：一次整批登记 ＋ 一次整批添加。
    /// <para>两条出口都调它：从资源管理器<b>拖入</b>（<c>WidgetWindow.QuickLaunch_Drop</c>）与点<b>「选择文件／选择文件夹」</b>
    /// （<c>QuickLaunchWidget</c>）。两处各写一遍的话，"登记进主库了但没加进组件"这类半套状态迟早只在一处出现——
    /// 而它看起来像另一个功能的 bug，最难归因。</para>
    /// <para>选择器这条路不是锦上添花：程序以管理员身份运行时 Windows（UIPI）会整条拦下从资源管理器拖进来的消息流，
    /// 不报错也不提示，症状就是"拖了没反应"；对话框跑在本进程里，与权限等级无关。</para>
    /// </summary>
    /// <returns>真正加进本组件的入口条数（去重后为 0 也是正常结果，由调用侧说出来，别让它静默）。</returns>
    public async Task<int> AddPathsToLauncherAsync(string instanceId, IReadOnlyList<(string? Title, string Path)> picked)
    {
        var usable = picked.Where(p => !string.IsNullOrWhiteSpace(p.Path)).ToList();
        if (usable.Count == 0) return 0;
        await RecordPathsToLibraryAsync(usable);
        return await AddLinksAsync(instanceId, usable.Select(p => (TitleOf(p.Title, p.Path), new Uri(p.Path).AbsoluteUri)).ToList());
    }

    /// <summary>没带名字就取路径末段；目录带尾斜杠时 <c>GetFileName</c> 会返回空串，退化成去掉斜杠的末段。</summary>
    private static string TitleOf(string? title, string path)
    {
        if (!string.IsNullOrWhiteSpace(title)) return title!;
        var leaf = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(leaf) ? path : leaf;
    }

    private async Task<string?> GetOrCreateQuickLaunchInstanceIdAsync()
    {
        string? id = null;
        await OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Kind == WidgetKind.QuickLaunch);
            if (inst is not null) { id = inst.Id; return; }
            var cfg = CreateInstanceConfig(data, WidgetKind.QuickLaunch);
            data.Instances.Add(cfg);
            _storage.Save(data);
            InstancesChanged?.Invoke();
            ShowInternal(cfg.Id);
            id = cfg.Id;
        });
        return id;
    }

    public async Task RemoveLinkAsync(string instanceId, string uri)
    {
        bool removed = false;
        string? removedTitle = null;
        await OnUiAsync(() =>
        {
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst is null) return;
            var doomed = inst.Links.Where(l => string.Equals(l.Uri, uri, StringComparison.OrdinalIgnoreCase)).ToList();
            if (doomed.Count == 0) return;
            removedTitle = doomed[0].Title;
            inst.Links.RemoveAll(l => string.Equals(l.Uri, uri, StringComparison.OrdinalIgnoreCase));
            _storage.Save(data);
            removed = true;
        });
        if (!removed) return;
        LinksChanged?.Invoke(instanceId);
        // 用户移除一个快捷入口 → 活动流记「删除」（红）。#51。
        await LogActivityAsync(ActivityKind.ItemDelete, removedTitle ?? uri, uri);
    }
}
