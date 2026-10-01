#nullable enable
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// SettingsPage 的这一段——「关于与更新」那两下动作（批次 UE）：立即检查、打开下载页。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里。</para>
/// <para>
/// 两个细节都是被真机教过的：① 按钮<b>自己变文案</b>（「正在问…」→ 回到「立即检查」），
/// 因为"按了没反应"与"其实正在跑"在用户侧长得一模一样；② 点下去之前先把上一次的「打开下载页」收掉——
/// 那一颗是<b>这一次的答案</b>才该有的出口，留着上一版的按钮就等于让界面替一件还没发生的事承诺。
/// </para>
/// </summary>
public sealed partial class SettingsPage
{
    private bool _updateBusy;

    private async void UpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_updateBusy) return;
        _updateBusy = true;
        UpdateCheckButton.Content = "正在问…";
        UpdateOpenPageButton.Visibility = Visibility.Collapsed;
        try
        {
            await ViewModel.CheckForUpdatesAsync();
            ViewModel.RefreshUpdateStatus();
            if (ViewModel.HasDownloadableRelease) UpdateOpenPageButton.Visibility = Visibility.Visible;
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

    private async void UpdateOpenPage_Click(object sender, RoutedEventArgs e)
    {
        // 走 LauncherEx 而不是直接调系统：那一句"只放行 http/https/本机文件"的协议闸门只有一个出处。
        var reason = await LauncherEx.TryOpenAsync(ViewModel.ReleasePageUrl);
        ViewModel.ShowOpenReason(reason);
    }
}
