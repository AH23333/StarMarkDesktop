#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.UI.Helpers;
using Windows.ApplicationModel.DataTransfer;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 「剪贴板历史」页 ViewModel：列出本机采集到的复制记录，点一条即复制回剪贴板。
/// <para>
/// 与其他页不同，这里的<b>主操作是"再复制"</b>而不是"打开"：剪贴板条目没有 URI，
/// 用户要的从来是"把那段文字再放回剪贴板"。所以整页刻意复用 <see cref="ItemCard"/>
/// （置顶/标签/笔记/隐藏/删除一套现成动作），只在点击与"复制"上改语义。
/// </para>
/// </summary>
public partial class ClipboardPageViewModel : ObservableObject
{
    private readonly IItemRepository _repository;

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _emptyHint = string.Empty;
    [ObservableProperty] private bool _hasItems;
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>状态行是否要有内容（XAML 用现成的 BoolToVisibility 控制那一行的显示）。</summary>
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    /// <summary>
    /// 暂停标记。真正的动作在页面代码里（<c>App.SetClipboardPaused</c>）——两处都要改的是同一个事实，
    /// 所以刻意只留一个入口，不做出"VM 一份、窗口一份"的双主人状态。
    /// </summary>
    [ObservableProperty] private bool _paused;

    /// <summary>总开关是否开着。关着时页面给出"去设置里开启"的入口，而不是空列表装死。</summary>
    [ObservableProperty] private bool _enabled;

    public ObservableCollection<ItemCardViewModel> Entries { get; } = new();

    public ClipboardPageViewModel(IItemRepository repository)
    {
        _repository = repository;
        Enabled = new SettingsStore().LoadClipboardHistoryEnabled();
        Paused = IsPausedNow();
    }

    /// <summary>读采集窗口的真实暂停位（没建窗口时按未暂停显示，不猜）。</summary>
    private static bool IsPausedNow()
    {
        try
        {
            var watcher = App.Services.GetRequiredService<StarMark.Integrations.Clipboard.ClipboardWatcher>();
            return watcher.IsRunning && watcher.Paused;
        }
        catch { return false; }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        Entries.Clear();
        try
        {
            var items = await _repository.GetBySourceAsync(
                ItemSources.Clipboard, ItemType.Clipboard, ClipboardPolicy.MaxEntries, CancellationToken.None);

            // 置顶优先，其余按"最近复制"（仓储已按 updated_at 倒序）——与列表语义一致：
            // 用户标过星的东西不该因为又被复制了一次就跳走。
            foreach (var item in items.OrderByDescending(i => i.Pinned))
                Entries.Add(new ItemCardViewModel(item));

            HasItems = Entries.Count > 0;
            EmptyHint = !Enabled
                ? "剪贴板历史未开启：到「设置 → 剪贴板历史」打开后，从这里复制过的内容会自动出现在这里。"
                : HasItems ? string.Empty : "还没有记录到任何复制内容。";
        }
        catch (Exception ex)
        {
            // HasItems 必须一起复位：那一行的可见性绑的就是它，留着 true 会让"加载失败"这句话
            // 直接被折叠掉——用户看到的是空白页加一句"还没有记录"，比报错更难查。
            HasItems = false;
            EmptyHint = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>把一条历史复制回剪贴板（点卡片即触发）。返回是否成功，页面据此给反馈。</summary>
    public async Task<bool> ReuseAsync(ItemCardViewModel vm)
    {
        var text = vm.Description ?? string.Empty;
        if (text.Length == 0)
        {
            StatusText = "这条历史没有可复制的正文";
            return false;
        }

        try
        {
            // 先登记回声再写：否则"从历史页复制"会被自己再采集一条（并刷新次数），列表莫名重排。
            App.NoteClipboardOwnWrite(text);
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            StatusText = $"已复制到剪贴板：{vm.Title}";
        }
        catch (Exception ex)
        {
            StatusText = $"复制失败：{ex.Message}";
            StarLog.Error($"剪贴板历史再复制失败 (id={vm.Id})", ex);
            return false;
        }

        await Task.CompletedTask;
        return true;
    }

    /// <summary>
    /// 清空全部历史（含置顶）。返回被删条数，让调用方能在确认文案里说清后果；失败返回 -1
    /// 并把原因写进状态行——这条路径由用户的"清空"按钮直连，抛出去就是一次 async void 崩溃，
    /// 静默返回则等于"点了没反应、历史还在"。
    /// </summary>
    public async Task<int> ClearAllAsync()
    {
        int deleted;
        try
        {
            deleted = await _repository.ClearClipboardHistoryAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            StatusText = $"清空失败：{ex.Message}（列表未变动，可再点一次）";
            StarLog.Error("清空剪贴板历史失败", ex);
            return -1;
        }

        StatusText = $"已清空 {deleted} 条剪贴板历史";
        Entries.Clear();
        HasItems = false;
        EmptyHint = Enabled ? "还没有记录到任何复制内容。" : EmptyHint;
        return deleted;
    }
}
