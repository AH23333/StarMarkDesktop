#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Helpers;

/// <summary>
/// ItemCard 操作集中处理：打开条目 / 编辑笔记 / 编辑标签 / 隐藏 / 置顶 / 复制链接 / 打开所在位置。
/// 供所有页面复用，避免重复实现 ContentDialog 逻辑。
/// 所有入口都有异常保护并写 StarLog——async void 中的未捕获异常会直接命中全局 UnhandledException。
/// </summary>
public static class ItemCardActions
{
    /// <summary>
    /// 条目标签被改动后触发（增/删/重设）。供搜索页/文件夹页订阅——
    /// 用户在内联编辑标签后，当前的条件搜索与浏览结果需要实时刷新（对齐扩展实时条件搜索）。
    /// 在 UI 线程触发（调用方均为 UI 事件处理器，async void 默认回到 UI 同步上下文）。
    /// </summary>
    public static event Action? ItemTagsChanged;

    public static IItemRepository GetRepo() => App.Services.GetRequiredItemRepository();

    /// <summary>
    /// 确保一条「实时源虚拟条目」（Everything 文件结果：Id=0、查询时从不入库）已登记进主库，返回其真实 Id；
    /// 已入库条目（Id&gt;0）原样返回其 Id。<b>只登记索引记录（复用同步口径的幂等 upsert），绝不移动 / 改名 / 删除磁盘上的实际文件。</b>
    /// 登记会把真实 Id 回填到 <paramref name="item"/>——其宿主 <see cref="ItemCardViewModel"/> 随即指向真实条目，
    /// 故紧随其后的置顶/隐藏/标签/笔记等按 Id 写库的操作得以生效。缺 (Source, SourceId) 业务键无法登记时返回 0。
    /// </summary>
    public static async Task<long> EnsureRecordedAsync(Item? item, CancellationToken ct = default)
    {
        if (item is null) return 0;
        if (item.Id > 0) return item.Id;
        return await GetRepo().RecordItemAsync(item, ct);
    }

    public static async void Open(XamlRoot xamlRoot, long itemId)
    {
        try
        {
            var repo = GetRepo();
            var item = await repo.GetByIdAsync(itemId, CancellationToken.None);
            if (item != null && !string.IsNullOrEmpty(item.Uri))
                await LauncherEx.OpenAsync(item.Uri);
        }
        catch (Exception ex)
        {
            StarLog.Error($"打开条目失败 (id={itemId})", ex);
        }
    }

    /// <summary>
    /// 按卡片视图模型打开条目。<b>未入库的实时源虚拟条目（Everything 本地文件结果 Id=0）库里必然查不到，
    /// 旧的"只按 Id 打开"对它们静默失效</b>——现象即"文件夹能开、文件点了没反应"。Id≤0 时直接按其 Uri 走
    /// <see cref="LauncherEx"/>（ShellExecute：文件交系统关联程序，无关联则由 Windows 提示选应用；文件夹在资源管理器打开）；
    /// 已入库条目仍按 Id 取最新库值，行为不变。
    /// </summary>
    public static void Open(XamlRoot xamlRoot, ItemCardViewModel? vm)
    {
        if (vm is null) return;
        if (vm.Id <= 0) { _ = LauncherEx.OpenAsync(vm.Uri); return; }
        Open(xamlRoot, vm.Id);
    }

    /// <summary>打开所在位置：本地文件 → 资源管理器定位；其余类型无位置概念。</summary>
    public static async void OpenLocation(ItemCardViewModel vm)
    {
        try
        {
            if (!vm.HasOpenLocation) return;
            // TryPathFromUri 而非 new Uri().LocalPath：后者在 '#' 处截断，含 '#' 的文件会定位到错误路径。
            if (!LocalFileIdentity.TryPathFromUri(vm.Uri, out var localPath)) return;
            // 该启动点不经 LaunchGuard.IsAllowedScheme，路径直拼进 explorer.exe 命令行、被子进程重切参数。
            // 被污染备份/快照的 uri 可携双引号越界注入额外参数 → 任意本地程序被执行，故闸门拒含引号者；
            // 并要求目标确在磁盘上（合法「打开所在位置」恒满足；对已删除/伪造路径 explorer 本就无意义）。
            if (!StarMark.Abstractions.LaunchGuard.IsSafeShellSelectTarget(localPath)) return;
            if (!System.IO.File.Exists(localPath) && !System.IO.Directory.Exists(localPath)) return;
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{localPath}\"");
        }
        catch (Exception ex)
        {
            StarLog.Error($"打开所在位置失败 (id={vm.Id}, uri={vm.Uri})", ex);
        }
        await Task.CompletedTask;
    }

    /// <summary>复制链接/路径到剪贴板（file:// 转本地路径）。</summary>
    public static async void CopyUri(ItemCardViewModel vm)
    {
        try
        {
            var text = vm.Uri;
            if (text.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                // TryPathFromUri 保留 '#'/%，new Uri().LocalPath 会在 '#' 截断而复制到错误路径。
                if (LocalFileIdentity.TryPathFromUri(text, out var localPath)) text = localPath;
            }
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            StarLog.Error($"复制链接失败 (id={vm.Id})", ex);
        }
        await Task.CompletedTask;
    }

    public static async void TogglePin(ItemCardViewModel vm)
    {
        try
        {
            // 未入库的实时源虚拟条目（Everything，Id=0）没有可写 pinned 的主库行：先按路径登记拿真实 Id。
            if (vm.Id == 0 && await EnsureRecordedAsync(vm.GetItem()) == 0) return;
            var newState = !vm.IsPinned;
            await GetRepo().SetPinnedAsync(vm.Id, newState, CancellationToken.None);
            vm.SetPinned(newState);
        }
        catch (Exception ex)
        {
            StarLog.Error($"置顶切换失败 (id={vm.Id})", ex);
        }
    }

    /// <summary>把 XamlRoot 解析为发起窗口（用于弹窗居中显示器）。无可靠映射时回落到主窗口。</summary>
    private static Window? ResolveOwner(XamlRoot? xamlRoot) => WindowInterop.ResolveWindow(xamlRoot, App.MainWindow);

    public static async void EditNote(XamlRoot xamlRoot, ItemCardViewModel vm)
    {
        try
        {
            var box = new TextBox
            {
                Text = vm.Notes ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 100,
                PlaceholderText = "写下笔记...",
            };
            box.Select(box.Text.Length, 0);

            // 统一走外部居中窗口（非 ContentDialog），自动按用户主题着色、可拖动、不可重复。
            var result = await CenteredDialog.ShowContentAsync(
                "编辑笔记", box, owner: ResolveOwner(xamlRoot),
                dedupeKey: $"editnote:{vm.Id}", width: 460, height: 260,
                primaryText: "保存", cancelText: "取消");

            if (result != CenteredDialog.HostedDialogResult.Committed) return;
            var text = box.Text.Trim();
            // 仅在真正改动时记一条「修改」事件——空保存/重复保存不污染活动流（#51）。
            var oldNotes = vm.Notes ?? string.Empty;
            if (string.Equals(oldNotes, text, StringComparison.Ordinal)) return;
            // 虚拟条目（Everything，Id=0）无主库行可写 notes：先按路径登记拿真实 Id，再落笔。
            if (vm.Id == 0 && await EnsureRecordedAsync(vm.GetItem()) == 0) return;
            await GetRepo().SetNoteAsync(vm.Id, text, CancellationToken.None);
            vm.ApplyNotes(string.IsNullOrWhiteSpace(text) ? null : text);
            await LogModify(vm);
        }
        catch (Exception ex)
        {
            StarLog.Error($"编辑笔记失败 (id={vm.Id})", ex);
        }
    }

    public static async void EditTags(XamlRoot xamlRoot, ItemCardViewModel vm)
    {
        try
        {
            var repo = GetRepo();
            var editor = new Controls.TagEditor
            {
                AllTags = (await repo.GetAllTagsAsync(CancellationToken.None)).Select(t => t.Name).ToList(),
            };
            editor.SetTags(vm.Tags);

            // 统一走外部居中窗口（非 ContentDialog），自动按用户主题着色、可拖动、不可重复。
            var result = await CenteredDialog.ShowContentAsync(
                "编辑标签", editor, owner: ResolveOwner(xamlRoot),
                dedupeKey: $"edittags:{vm.Id}", width: 460, height: 460,
                primaryText: "保存", cancelText: "取消");

            if (result != CenteredDialog.HostedDialogResult.Committed) return;

            // 差集写库：只删真正减少的、只加真正新增的。
            // 原实现是「全删再全加」的 N+1 往返，标签多时可感知卡顿，且会 churn tags 表。
            var desired = editor.Tags.ToList();
            var current = await repo.GetTagsForItemAsync(vm.Id, CancellationToken.None);

            var removed = current.Where(c => !desired.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            var added = desired.Where(d => !current.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();

            // 虚拟条目（Everything，Id=0）无主库行可挂标签：有净改动时先按路径登记拿真实 Id，再写关联。
            if (vm.Id == 0 && (added.Count > 0 || removed.Count > 0) && await EnsureRecordedAsync(vm.GetItem()) == 0) return;

            foreach (var tag in removed)
                await repo.RemoveTagAsync(vm.Id, tag, CancellationToken.None);
            foreach (var tag in added)
                await repo.AddTagAsync(vm.Id, tag, CancellationToken.None);

            if (desired.Count > 0 || current.Count > 0)
                vm.ApplyTags(desired);

            // 只有净增删才记「修改」；原样保存不写活动流（#51）。
            if (removed.Count > 0 || added.Count > 0)
                await LogModify(vm);

            ItemTagsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StarLog.Error($"编辑标签失败 (id={vm.Id})", ex);
        }
    }

    public static async Task<bool> ToggleHidden(XamlRoot xamlRoot, ItemCardViewModel vm)
    {
        // 兄弟方法（EditTags/RemoveTag/AddTag）均有 try/catch，此前唯 ToggleHidden 裸奔：
        // 它被多个 async void 的 Card_HideRequested 直接 await（SearchPage / QuickLaunchWidget /
        // FolderTreePage / TagsPage），SetHiddenAsync 抛 SqliteException 会顺着 async void 冒到
        // UI 线程成未处理异常。改为兜异常并回传"未改变"的当前状态（调用方据此不再做移除/刷新）。
        try
        {
            // 虚拟条目（Everything，Id=0）无主库行可写 hidden：先按路径登记拿真实 Id，「隐藏」才落得下去。
            if (vm.Id == 0 && await EnsureRecordedAsync(vm.GetItem()) == 0) return vm.IsHidden;
            var repo = GetRepo();
            var newState = !vm.IsHidden;
            await repo.SetHiddenAsync(vm.Id, newState, CancellationToken.None);
            vm.SetHidden(newState);
            return newState;
        }
        catch (Exception ex)
        {
            StarLog.Error($"切换隐藏状态失败 (id={vm.Id})", ex);
            return vm.IsHidden;
        }
    }

    public static async void RemoveTag(XamlRoot xamlRoot, ItemCardViewModel vm, string tag)
    {
        try
        {
            await GetRepo().RemoveTagAsync(vm.Id, tag, CancellationToken.None);
            vm.ApplyTags(vm.Tags.Where(t => !string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)).ToArray());
            await LogModify(vm);
            ItemTagsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StarLog.Error($"移除标签失败 (id={vm.Id}, tag={tag})", ex);
        }
    }

    /// <summary>
    /// 快速添加标签。与 <see cref="EditTags"/> 共用 TagEditor，因此同样享有
    /// 历史标签建议（避免把 ai 打成 AI 造成同义标签分裂）。
    /// </summary>
    public static async void AddTag(XamlRoot xamlRoot, ItemCardViewModel vm)
    {
        try
        {
            var repo = GetRepo();
            var editor = new Controls.TagEditor
            {
                AllTags = (await repo.GetAllTagsAsync(CancellationToken.None)).Select(t => t.Name).ToList(),
            };

            // 统一走外部居中窗口（非 ContentDialog），自动按用户主题着色、可拖动、不可重复。
            var result = await CenteredDialog.ShowContentAsync(
                "快速添加标签", editor, owner: ResolveOwner(xamlRoot),
                dedupeKey: $"addtag:{vm.Id}", width: 460, height: 460,
                primaryText: "添加", cancelText: "取消");

            if (result != CenteredDialog.HostedDialogResult.Committed) return;

            var desired = editor.Tags.ToList();
            if (desired.Count == 0) return;

            var current = await repo.GetTagsForItemAsync(vm.Id, CancellationToken.None);
            var toAdd = desired.Where(d => !current.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();

            // 虚拟条目（Everything，Id=0）无主库行可挂标签：确有新增时先按路径登记拿真实 Id。
            if (vm.Id == 0 && toAdd.Count > 0 && await EnsureRecordedAsync(vm.GetItem()) == 0) return;

            foreach (var tag in toAdd)
                await repo.AddTagAsync(vm.Id, tag, CancellationToken.None);

            vm.ApplyTags(current.Concat(desired).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            if (toAdd.Count > 0)
                await LogModify(vm);
            ItemTagsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StarLog.Error($"添加标签失败 (id={vm.Id})", ex);
        }
    }

    /// <summary>
    /// 记一条用户主动「修改」事件（笔记 / 标签增删）。活动流只反映用户操作，不含同步/种子（#51）。
    /// item_key 用条目的 (source:source_id) 作为业务键，便于后续按条目聚合。
    /// </summary>
    private static async Task LogModify(ItemCardViewModel vm)
    {
        try
        {
            var it = vm.GetItem();
            var key = !string.IsNullOrEmpty(it.Source) ? $"{it.Source}:{it.SourceId}" : null;
            await GetRepo().LogActivityAsync(ActivityKind.ItemModify, key, vm.Title, vm.Uri, CancellationToken.None);
        }
        catch (Exception ex)
        {
            StarLog.Error($"记录修改活动失败 (id={vm.Id})", ex);
        }
    }
}
