#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.UI.Helpers;

namespace StarMark.UI.ViewModels;

/// <summary>
/// SettingsPageViewModel 的这一段——失联书签那一头（片215·P-5B）：同步对账标出来的行
/// （<c>items.stale=1</c>）在这里报条数、给那一键。
/// <para>按访问面拆出来的 partial：<b>不持有构造</b>——字段上下文与 <c>_repository</c> 都住在主文件里，
/// 与 <c>SettingsPageViewModel.LocalDisk</c> 同一拆法（口径同它卷首那句「好让主文件回到能一眼看完的尺寸」）。</para>
/// <para>处置拍板＝账本 P-5 (B)「标灰＋一键清」：程序只挑出来、说清楚，删不删用户按——
/// 旧行身上挂着标签与笔记，「源里没了」与「该删」不是同一件事（与批次 VQ 的本机文件行同一口径）。</para>
/// </summary>
public partial class SettingsPageViewModel
{
    /// <summary>那句话（含「读不到库」的坏消息）；<b>空＝还没数过</b>。非空就一定显示。</summary>
    [ObservableProperty] private LocMessage? _staleBookmarksStatus;

    /// <summary>有没有可一键清的失联行（那颗「清掉这些行」按钮的可见性）。</summary>
    [ObservableProperty] private bool _hasStaleBookmarks;

    /// <summary>
    /// 那句话该不该显示。<b>与 <see cref="HasStaleBookmarks"/> 刻意分开</b>：把说明的可见性绑在
    /// 「有没有可删的行」上，「数失败了」那句就会跟着按钮一起被藏起来——点了按钮既没反应也没人说话
    /// （与 <c>SettingsPageViewModel.LocalDisk</c> 那颗 <c>HasMissingFileRowsNotice</c> 同一教训）。
    /// </summary>
    public bool HasStaleBookmarksNotice => StaleBookmarksStatus is not null;

    partial void OnStaleBookmarksStatusChanged(LocMessage? value)
        => OnPropertyChanged(nameof(HasStaleBookmarksNotice));

    /// <summary>
    /// 数一遍失联书签行（进设置页与清完之后各跑一次）。同步对账每轮自己会刷新 stale 位，
    /// 这里只读计数——<b>⛔ 替对账做决定</b>：这一格说「现在有几条」，不悄悄改任何一位。
    /// </summary>
    public async Task RefreshStaleBookmarksAsync()
    {
        if (_repository is not { } repo)
        {
            StaleBookmarksStatus = null;
            HasStaleBookmarks = false;
            return;
        }
        try
        {
            var count = await repo.CountStaleAsync(CancellationToken.None);
            HasStaleBookmarks = count > 0;
            StaleBookmarksStatus = count == 0
                ? LocMessage.Of("settings.extend.staleNone")
                : LocMessage.Of("settings.extend.staleCount", count);
        }
        catch (Exception ex)
        {
            StarLog.Error("统计失联书签条目失败", ex);
            HasStaleBookmarks = false;
            StaleBookmarksStatus = LocMessage.Of("settings.extend.staleCountFailed", ex.Message);
        }
    }

    /// <summary>
    /// 一键清掉失联书签行。<b>先报条数再确认</b>（一次点掉几十行而没有回看的机会，等于把「清理」
    /// 做成第二个缺陷），删完重数一遍——留着上一轮的计数就是新的一份假话（口径同
    /// <c>SettingsPageViewModel.LocalDisk</c> 那颗 Clean 的收尾）。
    /// <para>标题/按钮/收尾那几句与磁盘卡共用（<c>settings.local.*</c>）；正文各自立键——
    /// 磁盘那句「磁盘上的文件没有被改动」对书签是不相干的话，⛔ 拿来顶。</para>
    /// </summary>
    [RelayCommand]
    private async Task CleanStaleBookmarksAsync()
    {
        if (_repository is not { } repo) return;

        int count;
        try
        {
            count = await repo.CountStaleAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            StarLog.Error("统计失联书签条目失败", ex);
            App.MainWindow?.ShowError(LocMessage.Of("settings.local.cleanFailedTitle"),
                LocMessage.Of("common.rawValue", ex.Message));
            return;
        }
        if (count == 0)
        {
            await RefreshStaleBookmarksAsync();
            return;
        }
        if (!await CenteredDialog.ConfirmAsync(
                LocMessage.Of("settings.local.cleanRowsTitle"),
                LocMessage.Of("settings.extend.staleCleanBody", count),
                LocMessage.Of("settings.local.cleanRowsGo"), LocMessage.Of("settings.local.cleanRowsKeep"),
                dedupeKey: "clean-stale-bookmarks")) return;
        try
        {
            var removed = await repo.DeleteStaleAsync(CancellationToken.None);
            App.MainWindow?.ShowNotice(
                removed > 0 ? LocMessage.Of("settings.local.clearedTitle") : LocMessage.Of("settings.local.nothingLeftTitle"),
                removed > 0 ? LocMessage.Of("settings.extend.staleClearedBody", removed)
                            : LocMessage.Of("settings.local.nothingLeftBody"));
        }
        catch (Exception ex)
        {
            StarLog.Error("清理失联书签条目失败", ex);
            App.MainWindow?.ShowError(LocMessage.Of("settings.local.cleanFailedTitle"),
                LocMessage.Of("common.rawValue", ex.Message));
        }
        await RefreshStaleBookmarksAsync();
    }
}
