#nullable enable
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 快捷启动格 ViewModel（A-4）：复用 <see cref="ItemCardViewModel"/> 统一渲染置顶条目与自定义快捷入口。
/// 本组件只做「展示 + 与主窗一致的右键」，不再内嵌搜索栏（搜索统一走快捷搜索组件 / 主窗搜索页）。
/// 自定义快捷入口无主库 Item，故用合成 Item + <see cref="ItemCardViewModel.IsLauncherMode"/> 隐藏会误写主库的操作。
/// </summary>
public sealed partial class QuickLaunchWidgetViewModel : ObservableObject
{
    private readonly IItemRepository? _repo;
    private readonly WidgetStorage _storage;
    private readonly string _instanceId;

    /// <summary>数据变更同步器：主界面置顶/取消置顶后，本组件的置顶区立刻跟着变。</summary>
    private readonly DataChangeReloader _sync;

    /// <summary>置顶条目（来自 StarMark 数据库 Pinned=1），完整 ItemCard 能力。</summary>
    public ObservableCollection<ItemCardViewModel> Pinned { get; } = new();

    /// <summary>用户自定义快捷入口（合成 Item + IsLauncherMode，仅打开/复制/预览）。</summary>
    public ObservableCollection<ItemCardViewModel> Links { get; } = new();

    public QuickLaunchWidgetViewModel(WidgetStorage storage, IItemRepository? repo, string instanceId)
    {
        _storage = storage;
        _repo = repo;
        _instanceId = instanceId;
        _sync = new DataChangeReloader(LoadAsync);
    }

    /// <summary>退订数据广播（组件卸载时调用）。</summary>
    public void Dispose() => _sync.Dispose();

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
                Pinned.Add(new ItemCardViewModel(it));
        }
        catch (Exception ex)
        {
            StarMark.Abstractions.StarLog.Error("加载置顶条目失败", ex);
        }
    }

    /// <summary>取消置顶：写库后增量刷新置顶集合（不重建整棵 UI）。</summary>
    public async Task UnpinAsync(long id)
    {
        if (_repo is not null)
        {
            try { await _repo.SetPinnedAsync(id, false, CancellationToken.None); }
            catch (Exception ex) { StarMark.Abstractions.StarLog.Error("取消置顶失败", ex); }
        }
        await ReloadPinnedAsync();
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
    /// 同时被快捷启动格拖放和"添加"表单复用，避免逻辑分散。
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
