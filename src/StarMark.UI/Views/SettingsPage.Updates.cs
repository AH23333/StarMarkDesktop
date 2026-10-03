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
/// </summary>
public sealed partial class SettingsPage
{
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
        UpdateCheckButton.Content = "正在问…";
        UpdateOpenPageButton.Visibility = Visibility.Collapsed;
        UpdateApplyButton.Visibility = Visibility.Collapsed;
        try
        {
            await ViewModel.CheckForUpdatesAsync();
            ViewModel.RefreshUpdateStatus();
            if (ViewModel.HasDownloadableRelease)
            {
                UpdateApplyButton.Visibility = Visibility.Visible;
                UpdateOpenPageButton.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            ViewModel.ReportCheckCrash(ex);
        }
        finally
        {
            _updateBusy = false;
            UpdateCheckButton.Content = "立即检查";
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
        UpdateApplyButton.Content = "取消更新";
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
            UpdateApplyButton.Content = "立即更新";
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
