#nullable enable
using System;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions.Updates;
using StarMark.Core.Updates;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// SettingsPage 的这一段——「关于与更新」那三下动作：立即检查、立即更新、打开下载页。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里。</para>
/// <para>
/// 三个细节都是被真机或别的批次教过的：① 按钮<b>自己变文案</b>（「正在问…」→ 回到「立即检查」），
/// 因为"按了没反应"与"其实正在跑"在用户侧长得一模一样；② 点下去之前先把上一次的出口按钮收掉——
/// 那几颗是<b>这一次的答案</b>才该有的出口，留着上一版的按钮就等于让界面替一件还没发生的事承诺；
/// ③「立即更新」在跑的那一段<b>同一颗按钮变成「取消更新」</b>：一条十分钟没上限的下载不许只有一条转圈的路
/// （P-55 那条"取消要真能停"在此，批次 QA-2 同一形状）。
/// </para>
/// <para><b>换文案换的是那颗键，不是那句字</b>（批次 WI-2d）：这两颗按钮的正文从这一批起住在文案表上，
/// 而换语言那一刻 <c>LocText</c> 会把<b>当前挂着的那颗键</b>重贴一遍。代码要是继续直接写
/// <c>Content = "立即检查"</c>，两件事会同时发生——英文界面上点完一次检查按钮就变回中文，
/// 以及更要紧的那一件：正在跑的那一颗被重贴成"空闲"那句，界面改口说它没在做事。</para>
/// </summary>
public sealed partial class SettingsPage
{
    private const string CheckNowKey = "settings.general.checkNow";
    private const string CheckingKey = "settings.general.checking";
    private const string UpdateNowKey = "settings.general.updateNow";
    private const string CancelUpdateKey = "settings.general.cancelUpdate";
    private bool _updateBusy;
    private bool _applyBusy;
    private CancellationTokenSource? _applyCts;

    /// <summary>正在飞的字节读数只留最新那一条，加一把"已经排过队"的闩（见 <see cref="ShowDownloadProgress"/>）。</summary>
    private DownloadProgress _latestProgress;
    private bool _progressQueued;

    private async void UpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_updateBusy || _applyBusy) return;      // 正在装新版时，"再问一次"只会把那一行字改回上一次的答案
        _updateBusy = true;
        LocText.SetKey(UpdateCheckButton, CheckingKey);
        UpdateOpenPageButton.Visibility = Visibility.Collapsed;
        UpdateApplyButton.Visibility = Visibility.Collapsed;
        UpdateNotesExpander.Visibility = Visibility.Collapsed;   // 同一批：上一发的正文也不许替这一发承诺
        try
        {
            await ViewModel.CheckForUpdatesAsync();
            ViewModel.RefreshUpdateStatus();
            if (ViewModel.HasDownloadableRelease)
            {
                UpdateApplyButton.Visibility = Visibility.Visible;
                UpdateOpenPageButton.Visibility = Visibility.Visible;
            }
            // 这一格跟着"这一次真带回了可读正文"走，不跟着"有没有新版"走：对方没写说明时点开是空的。
            if (ViewModel.HasReleaseNotes) UpdateNotesExpander.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ViewModel.ReportCheckCrash(ex);
        }
        finally
        {
            _updateBusy = false;
            LocText.SetKey(UpdateCheckButton, CheckNowKey);
        }
    }

    /// <summary>
    /// 「立即更新」：第一下交出去跑，再一下掐掉它。<b>只有 Core 说"已经交给更新器"才让路</b>——
    /// 那一格的意思是活不在我们手上了，界面继续开着只会让更新器等着一个不会退出的进程（#234：
    /// "交出去了"不许被演成"换好了"，而这一句说完就得真的撒手）。
    /// </summary>
    private async void UpdateApply_Click(object sender, RoutedEventArgs e)
    {
        if (_applyBusy)
        {
            _applyCts?.Cancel();
            return;
        }
        _applyBusy = true;
        _applyCts = new CancellationTokenSource();
        LocText.SetKey(UpdateApplyButton, CancelUpdateKey);
        UpdateCheckButton.IsEnabled = false;
        try
        {
            var result = await ViewModel.ApplyUpdateAsync(ShowApplyPhase, ShowDownloadProgress, _applyCts.Token);
            ViewModel.ShowApplyResult(result);
            if (result.IsHandedOff) App.MainWindow?.ExitForUpdateHandoff();
        }
        catch (OperationCanceledException)
        {
            ViewModel.ShowApplyCancelled();          // 掐掉不是失败：那一句与失败那句不同
        }
        catch (Exception ex)
        {
            ViewModel.ReportApplyCrash(ex);
        }
        finally
        {
            _applyCts.Dispose();
            _applyCts = null;
            _applyBusy = false;
            LocText.SetKey(UpdateApplyButton, UpdateNowKey);
            UpdateCheckButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 进度那一行<b>回到 UI 线程再写</b>：那条链一路 <c>ConfigureAwait(false)</c>，
    /// 回调落在工作线程上，直接改绑定属性就是跨线程访问（表现是随机崩，而不是"偶尔不刷新"）。
    /// </summary>
    private void ShowApplyPhase(ApplyPhase phase)
        => DispatcherQueue.TryEnqueue(() => ViewModel.ShowApplyPhase(phase));

    /// <summary>
    /// 字节读数同一条规矩（批次 VW）：它是从传输层那条 <c>ConfigureAwait(false)</c> 的续接上叫上来的，
    /// <b>不回到 UI 线程就是跨线程改绑定属性</b>。回 UI 之外还多做一件"合"：
    /// 下载那一发每秒能报几十次，而这一行字只有最后一个数有人看得懂——
    /// 排队时若已经压着一条没消费的读数，就把旧的换掉而不是攒成一串让界面闪。
    /// </summary>
    private void ShowDownloadProgress(DownloadProgress progress)
    {
        _latestProgress = progress;
        if (_progressQueued) return;
        _progressQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _progressQueued = false;
            ViewModel.ShowDownloadProgress(_latestProgress);
        });
    }

    private async void UpdateOpenPage_Click(object sender, RoutedEventArgs e)
    {
        // 走 LauncherEx 而不是直接调系统：那一句"只放行 http/https/本机文件"的协议闸门只有一个出处。
        var reason = await LauncherEx.TryOpenAsync(ViewModel.ReleasePageUrl);
        ViewModel.ShowOpenReason(reason);
    }
}
