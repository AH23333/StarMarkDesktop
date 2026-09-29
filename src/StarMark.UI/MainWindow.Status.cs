#nullable enable
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;
using StarMark.Abstractions;
using StarMark.Abstractions.Language;
using StarMark.Core.Capture;
using StarMark.Core.Hotkeys;
using StarMark.Core.Widgets;
using StarMark.Integrations.SystemTray;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;
using StarMark.UI.Views;

namespace StarMark.UI;

/// <summary>
/// MainWindow 的这一段——状态与同步这一头：小圆点的种类、工具条投影、错误/提示的可见回执，以及同步的取消。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class MainWindow
{

    private enum StatusKind
    {
        Caution,
        Success,
    }

    /// <summary>状态点按窗口实际主题解析画笔（§3.1 主题感知），切换主题时经 RefreshAppearance 重放。</summary>
    private void SetStatusDot(StatusKind kind)
    {
        _statusKind = kind;
        var key = kind == StatusKind.Success ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush";
        StatusDot.Fill = ThemeBrush.For(RootGrid.ActualTheme, key)
            ?? (SolidColorBrush)Application.Current.Resources[key]; // 词典缺失时兜底（仅 Style 场景外的最后手段）
    }

    private void SetSourceButtonsHighlight(string source)
    {
        // 主题感知解析（§3.1）：应用级资源在窗口创建后冻结，运行期切主题会拿到错误主题的画笔
        var accent = ThemeBrush.For(RootGrid.ActualTheme, "AccentFillColorDefaultBrush");
        var muted = ThemeBrush.For(RootGrid.ActualTheme, "TextFillColorSecondaryBrush");
        var white = new SolidColorBrush(Colors.White);
        foreach (var (btn, tag) in new[]
                 { (SourceAll, "all"), (SourceStar, "star"), (SourceBookmark, "bookmark"), (SourceFile, "file") })
        {
            var selected = tag == source;
            btn.Background = selected ? accent : new SolidColorBrush(Colors.Transparent);
            btn.Foreground = selected ? white : muted;
        }
    }

    /// <summary>把全局工具栏状态下发到当前页面（搜索/文件夹页），并触发重新查询。</summary>
    private void PushToolbarToContent()
    {
        // XAML 解析期间（如 SortCombo 初始 SelectedIndex）相关控件可能尚未创建
        if (ContentFrame == null || ShowHiddenCheck == null) return;
        if (ContentFrame.Content is not Page page) return;
        var source = CurrentSourceTag();
        var sort = CurrentSortTag();
        var hidden = ShowHiddenCheck.IsChecked == true;

        switch (page)
        {
            case SearchPage sp:
                sp.ViewModel.CurrentSource = source;
                sp.ViewModel.CurrentSort = sort;
                sp.ViewModel.ShowHidden = hidden;
                HookLanguageOptions(sp.ViewModel);
                SyncLanguageCombo(sp.ViewModel);
                break;
            case FolderTreePage ftp:
                ftp.ViewModel.CurrentSource = source;
                ftp.ViewModel.CurrentSort = sort;
                ftp.ViewModel.ShowHidden = hidden;
                SyncLanguageCombo(null);
                break;
        }
    }

    private void SyncButton_Click(object sender, RoutedEventArgs e) => _ = DoSyncAsync();

    /// <summary>在途同步的取消源。null＝没有同步在跑。</summary>
    private CancellationTokenSource? _syncCts;

    private void SyncCancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_syncCts is null) return;
        _syncCts.Cancel();
        // 取消是"请求"：正在 await 的那一页要等 HTTP 回包或抛异常才退出。按钮就地禁用，
        // 免得用户连点三下以为没反应、转去杀进程。
        SyncCancelButton.IsEnabled = false;
        StatusText.Text = "正在取消同步…";
    }

    private async Task DoSyncAsync()
    {
        SyncButton.IsEnabled = false;
        SyncProgress.IsActive = true;
        SyncProgress.Visibility = Visibility.Visible;
        SyncCancelButton.IsEnabled = true;
        SyncCancelButton.Visibility = Visibility.Visible;
        SetStatusDot(StatusKind.Caution);
        StatusText.Text = "同步中...";
        SyncInfoBar.IsOpen = false;

        var cts = _syncCts = new CancellationTokenSource();
        try
        {
            var syncCoordinator = App.Services.GetRequiredService<StarMark.Core.Sync.SyncCoordinator>();
            {
                var summary = await syncCoordinator.SyncAllAsync(cts.Token);
                var text = summary.FormatText();
                StatusText.Text = text;
                ShowInfoBar(InfoBarSeverity.Success, "索引同步完成", string.Empty, 6500);
            }
            SetStatusDot(StatusKind.Success);
            await ViewModel.LoadCountsAsync();
            await LoadStarLanguagesAsync();   // 同步后新 star 的语言要出现在下拉里
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // 用户主动取消不是故障：与「同步失败」分开报。已拉完的源逐源提交过（幂等 upsert），
            // 所以这句话必须说清"部分结果留下了、缺的下次补齐"，否则用户以为白点了一次同步。
            const string cancelled = "已取消同步：已拉完的源已入库，未拉完的下次同步继续补齐。";
            StatusText.Text = cancelled;
            SetStatusDot(StatusKind.Success);
            ShowInfoBar(InfoBarSeverity.Informational, "已取消同步", cancelled, 6500);
            await ViewModel.LoadCountsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"同步失败: {ex.Message}";
            SetStatusDot(StatusKind.Caution);
            ShowInfoBar(InfoBarSeverity.Error, "同步失败", ex.Message);
        }
        finally
        {
            SyncButton.IsEnabled = true;
            SyncProgress.IsActive = false;
            SyncProgress.Visibility = Visibility.Collapsed;
            SyncCancelButton.Visibility = Visibility.Collapsed;
            var own = _syncCts;
            _syncCts = null;
            own?.Dispose();   // 在途 await 已退出（正常返回或抛错）才走到这里
        }
    }

    /// <summary>
    /// 供共享卡片动作（<c>ItemCardActions</c>）等"没有自己状态行的页面"用的瞬时错误提示。
    /// 判据同 P-53/P-54：<b>日志不是用户能看到的反馈面</b>——失败要么就地写清楚，要么就别报"已完成"。
    /// </summary>
    public void ShowError(string title, string message)
        => ShowInfoBar(InfoBarSeverity.Error, title, message, 8000);

    /// <summary>
    /// 中性提示（不是错误也不是"操作完成"）：右下角提示卡贴不上屏幕时的兜底出口。
    /// 停留久一点——护眼这类提醒是"给你看一眼"，不该 8 秒就自己收走。
    /// </summary>
    public void ShowNotice(string title, string message)
        => ShowInfoBar(InfoBarSeverity.Informational, title, message, 15000);

    private void ShowInfoBar(InfoBarSeverity severity, string title, string message, int autoCloseMs = -1)
    {
        SyncInfoBar.Severity = severity;
        SyncInfoBar.Title = title;
        SyncInfoBar.Message = message;
        SyncInfoBar.IsOpen = true;
        if (autoCloseMs > 0)
            _ = Task.Delay(autoCloseMs).ContinueWith(_ =>
                DispatcherQueue.TryEnqueue(() => SyncInfoBar.IsOpen = false));
    }

    private void InfoBar_Close(InfoBar sender, object args) => sender.IsOpen = false;
}
