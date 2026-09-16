#nullable enable
using System;
using System.Collections.ObjectModel;
using System.IO;
using StarMark.Abstractions;
using StarMark.Core.Widgets;

namespace StarMark.UI.ViewModels;

/// <summary>快捷启动格中的「置顶条目」行（来自 StarMark 数据库 Pinned=1）。</summary>
public sealed record QuickLaunchPinned(string Title, string Uri, string Emoji);

/// <summary>快捷启动格中的「快捷入口」行（用户自定义，存于 widgets.json）。</summary>
public sealed record QuickLaunchLink(long Id, string Title, string Uri);

/// <summary>
/// 快捷启动格 ViewModel（R1 试点）。数据绑定集合，替代原 code-behind 的 StackPanel 手工构建。
/// 置顶条目来自数据库，快捷入口来自本地存储；两者均为 ObservableCollection，
/// 配合 ItemsRepeater 实现 R3 的增量更新（勾选/增删只改集合，不重建整棵 UI 树）。
/// </summary>
public sealed class QuickLaunchWidgetViewModel
{
    private readonly IItemRepository? _repo;
    private readonly WidgetStorage _storage;

    public ObservableCollection<QuickLaunchPinned> Pinned { get; } = new();
    public ObservableCollection<QuickLaunchLink> Links { get; } = new();

    public QuickLaunchWidgetViewModel(WidgetStorage storage, IItemRepository? repo)
    {
        _storage = storage;
        _repo = repo;
    }

    public async Task LoadAsync()
    {
        await ReloadPinnedAsync();
        ReloadLinks();
    }

    public async Task ReloadPinnedAsync()
    {
        Pinned.Clear();
        if (_repo is null) return;
        try
        {
            var items = await _repo.GetPinnedAsync(8, CancellationToken.None);
            foreach (var it in items)
                Pinned.Add(new QuickLaunchPinned(it.Title, it.Uri, EmojiFor(it.Type)));
        }
        catch (Exception ex)
        {
            StarMark.Abstractions.StarLog.Error("加载置顶条目失败", ex);
        }
    }

    public void ReloadLinks()
    {
        Links.Clear();
        var data = _storage.Load();
        foreach (var l in data.Links)
            Links.Add(new QuickLaunchLink(l.Id, l.Title, l.Uri));
    }

    public static string EmojiFor(ItemType t) => t switch
    {
        ItemType.File => "📁",
        ItemType.Bookmark => "🔖",
        ItemType.GitHubStar => "⭐",
        _ => "📌",
    };

    /// <summary>
    /// 快捷入口 URI 解析（文件 / 文件夹 / 网页 / 裸域名补 https://）。
    /// 同时被快捷启动格拖放与"添加"表单复用，避免逻辑分散。
    /// </summary>
    internal static bool TryParseUri(string text, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeFile))
        {
            uri = parsed;
            return true;
        }
        if (File.Exists(text) || Directory.Exists(text))
        {
            uri = new Uri(text);
            return true;
        }
        // 裸域名补 https://
        if (text.Contains('.') && !text.Contains(' ') &&
            Uri.TryCreate("https://" + text, UriKind.Absolute, out parsed))
        {
            uri = parsed;
            return true;
        }
        return false;
    }
}
