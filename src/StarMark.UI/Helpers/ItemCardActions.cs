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

    public static async void EditNote(XamlRoot xamlRoot, ItemCardViewModel vm)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = "编辑笔记",
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                XamlRoot = xamlRoot,
                DefaultButton = ContentDialogButton.Primary,
                Content = new TextBox
                {
                    Text = vm.Notes ?? string.Empty,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = 100,
                    PlaceholderText = "写下笔记...",
                },
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var box = (TextBox)dialog.Content;
                var text = box.Text.Trim();
                await GetRepo().SetNoteAsync(vm.Id, text, CancellationToken.None);
                vm.ApplyNotes(string.IsNullOrWhiteSpace(text) ? null : text);
            }
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
            var box = new TextBox
            {
                Text = string.Join(", ", vm.Tags),
                PlaceholderText = "逗号分隔多个标签，如: ai, llm",
                Margin = new Thickness(0, 8, 0, 0),
            };

            var dialog = new ContentDialog
            {
                Title = "编辑标签",
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                XamlRoot = xamlRoot,
                DefaultButton = ContentDialogButton.Primary,
                Content = box,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var repo = GetRepo();
                var current = await repo.GetTagsForItemAsync(vm.Id, CancellationToken.None);
                foreach (var tag in current)
                    await repo.RemoveTagAsync(vm.Id, tag, CancellationToken.None);
                var tags = box.Text.Split(new[] { ',', '，', ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var tag in tags)
                    await repo.AddTagAsync(vm.Id, tag, CancellationToken.None);
                vm.ApplyTags(tags);
            }
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
        }
        catch (Exception ex)
        {
            StarLog.Error($"移除标签失败 (id={vm.Id}, tag={tag})", ex);
        }
    }

    public static async void AddTag(XamlRoot xamlRoot, ItemCardViewModel vm)
    {
        try
        {
            var box = new TextBox
            {
                PlaceholderText = "标签名称，如 ai、llm",
                Margin = new Thickness(0, 8, 0, 0),
            };
            var dialog = new ContentDialog
            {
                Title = "快速添加标签",
                PrimaryButtonText = "添加",
                CloseButtonText = "取消",
                XamlRoot = xamlRoot,
                DefaultButton = ContentDialogButton.Primary,
                Content = box,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var name = box.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;
            var repo = GetRepo();
            await repo.AddTagAsync(vm.Id, name, CancellationToken.None);
            vm.ApplyTags(vm.Tags.Concat(new[] { name }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (Exception ex)
        {
            StarLog.Error($"添加标签失败 (id={vm.Id})", ex);
        }
    }
}
