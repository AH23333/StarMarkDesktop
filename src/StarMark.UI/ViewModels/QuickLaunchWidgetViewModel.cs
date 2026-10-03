#nullable enable
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using StarMark.Abstractions;
using StarMark.Core.Widgets;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 快捷启动 ViewModel（A-4）：只承载用户自定义快捷入口。
/// 自定义快捷入口无主库 Item，故用合成 Item + <see cref="ItemCardViewModel.IsLauncherMode"/>
/// 隐藏会误写主库的操作（隐藏/置顶/笔记/标签）。
/// <para>
/// 置顶条目集合（原 <c>Pinned</c> + 数据广播同步）已按用户裁决摘除：那批数据由「置顶条目」组件负责展示，
/// 一格两处等于同一件事做两遍。
/// </para>
/// </summary>
public sealed class QuickLaunchWidgetViewModel
{
    private readonly WidgetStorage _storage;
    private readonly string _instanceId;

    /// <summary>用户自定义快捷入口（合成 Item + IsLauncherMode，仅打开/复制/预览/删除）。</summary>
    public ObservableCollection<ItemCardViewModel> Links { get; } = new();

    public QuickLaunchWidgetViewModel(WidgetStorage storage, string instanceId)
    {
        _storage = storage;
        _instanceId = instanceId;
    }

    public void ReloadLinks()
    {
        Links.Clear();
        var data = _storage.Load();
        var inst = data.Instances.FirstOrDefault(i => i.Id == _instanceId);
        if (inst is null) return;
        foreach (var l in inst.Links)
        {
            // 合成 Item：无主库 id，IsLauncherMode 隐藏会误写主库的操作（隐藏/置顶/笔记/标签）。
            var item = new Item
            {
                Type = ItemType.Bookmark,
                Source = ItemSources.Local,
                SourceId = "link:" + l.Uri,
                Title = l.Title,
                Uri = l.Uri,
            };
            Links.Add(new ItemCardViewModel(item) { IsLauncherMode = true });
        }
    }

    /// <summary>
    /// 快捷入口 URI 解析（文件 / 文件夹 / 网页 / 裸域名补 https://）。
    /// 同时被快捷启动拖放和"添加"表单复用，避免逻辑分散。
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
        if (StarMark.UI.Helpers.LauncherEx.ExistsOnDisk(text))
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
