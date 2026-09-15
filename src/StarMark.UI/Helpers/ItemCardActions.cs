#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Helpers;

/// <summary>
/// ItemCard 操作集中处理：打开条目 / 编辑笔记 / 编辑标签 / 隐藏切换。
/// 供所有页面复用，避免重复实现 ContentDialog 逻辑。
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
        catch { }
    }

    public static async void EditNote(XamlRoot xamlRoot, ItemCardViewModel vm)
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
            await GetRepo().SetNoteAsync(vm.Id, box.Text.Trim(), CancellationToken.None);
        }
    }

    public static async void EditTags(XamlRoot xamlRoot, ItemCardViewModel vm)
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
                .Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tags)
                await repo.AddTagAsync(vm.Id, tag, CancellationToken.None);
        }
    }

    public static async void ToggleHidden(XamlRoot xamlRoot, ItemCardViewModel vm)
    {
        var repo = GetRepo();
        await repo.SetHiddenAsync(vm.Id, !vm.IsHidden, CancellationToken.None);
    }
}