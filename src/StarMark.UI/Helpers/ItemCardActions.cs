#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
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

    public static IItemRepository GetRepo()
        => App.Services.GetService(typeof(IItemRepository)) as IItemRepository
            ?? throw new InvalidOperationException("IItemRepository 未注册");

    public static async void Open(XamlRoot xamlRoot, long itemId)
    {
        try
        {
            var repo = GetRepo();
            var item = await repo.GetByIdAsync(itemId, CancellationToken.None);
            if (item != null && !string.IsNullOrEmpty(item.Uri))
                await Windows.System.Launcher.LaunchUriAsync(new Uri(item.Uri));
        }
        catch (Exception ex)
        {
            StarLog.Error($"打开条目失败 (id={itemId})", ex);
        }
    }

    /// <summary>打开所在位置：本地文件 → 资源管理器定位；其余类型无位置概念。</summary>
    public static async void OpenLocation(ItemCardViewModel vm)
    {
        try
        {
            if (!vm.HasOpenLocation) return;
            var localPath = new Uri(vm.Uri).LocalPath;
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
                try { text = new Uri(text).LocalPath; } catch { }
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
    private static Window? ResolveOwner(XamlRoot? xamlRoot) => App.MainWindow;

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
            await GetRepo().SetNoteAsync(vm.Id, text, CancellationToken.None);
            vm.ApplyNotes(string.IsNullOrWhiteSpace(text) ? null : text);
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

            foreach (var tag in current.Where(c => !desired.Contains(c, StringComparer.OrdinalIgnoreCase)))
                await repo.RemoveTagAsync(vm.Id, tag, CancellationToken.None);
            foreach (var tag in desired.Where(d => !current.Contains(d, StringComparer.OrdinalIgnoreCase)))
                await repo.AddTagAsync(vm.Id, tag, CancellationToken.None);

            if (desired.Count > 0 || current.Count > 0)
                vm.ApplyTags(desired);

            ItemTagsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StarLog.Error($"编辑标签失败 (id={vm.Id})", ex);
        }
    }

    public static async Task<bool> ToggleHidden(XamlRoot xamlRoot, ItemCardViewModel vm)
    {
        var repo = GetRepo();
        var newState = !vm.IsHidden;
        await repo.SetHiddenAsync(vm.Id, newState, CancellationToken.None);
        vm.SetHidden(newState);
        return newState;
    }

    public static async void RemoveTag(XamlRoot xamlRoot, ItemCardViewModel vm, string tag)
    {
        try
        {
            await GetRepo().RemoveTagAsync(vm.Id, tag, CancellationToken.None);
            vm.ApplyTags(vm.Tags.Where(t => !string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)).ToArray());
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
            foreach (var tag in desired.Where(d => !current.Contains(d, StringComparer.OrdinalIgnoreCase)))
                await repo.AddTagAsync(vm.Id, tag, CancellationToken.None);

            vm.ApplyTags(current.Concat(desired).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            ItemTagsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StarLog.Error($"添加标签失败 (id={vm.Id})", ex);
        }
    }
}
