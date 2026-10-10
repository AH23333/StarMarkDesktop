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
using StarMark.Abstractions.I18n;
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

    /// <summary>顶栏那一行状态<b>唯一的出口</b>（批次 WI-2m）：只把"键＋读数"交给 <c>LocText.SetMessage</c> 那颗门。</summary>
    /// <remarks>那颗元素上挂着 <c>i18n:LocText.Key="top.indexReady"</c>（没有同步在跑时的那句常态）。
    /// 直接写 <c>StatusText.Text</c> 会留下一个洞：换语言那一刻它按<b>注册项里那句</b>重贴，
    /// 而注册项还停在标记那颗键上＝"同步中…"被"索引就绪"盖掉——界面替一件还在跑的事改口（坑表 #295 的镜像）。
    /// 走门＝把"当前这一句"记进注册项，重贴时贴回的是同一句、按当时的语言重新取词。</remarks>
    private void SetStatusText(LocMessage? message) => LocText.SetMessage(StatusText, message);

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
        SetStatusText(LocMessage.Of("top.statusCancelling"));
    }

    private async Task DoSyncAsync()
    {
        SyncButton.IsEnabled = false;
        SyncProgress.IsActive = true;
        SyncProgress.Visibility = Visibility.Visible;
        SyncCancelButton.IsEnabled = true;
        SyncCancelButton.Visibility = Visibility.Visible;
        SetStatusDot(StatusKind.Caution);
        SetStatusText(LocMessage.Of("top.statusSyncing"));
        SyncInfoBar.IsOpen = false;

        var cts = _syncCts = new CancellationTokenSource();
        try
        {
            var syncCoordinator = App.Services.GetRequiredService<StarMark.Core.Sync.SyncCoordinator>();
            {
                var summary = await syncCoordinator.SyncAllAsync(cts.Token);
                // Core 交回的是"还没取词"的那句话：在这里 Render 一次＝那句话的语言被钉死在同步完成的那一刻
                SetStatusText(summary.Message());
                ShowInfoBar(InfoBarSeverity.Success, LocMessage.Of("top.syncDoneTitle"), null, 6500);
            }
            SetStatusDot(StatusKind.Success);
            await ViewModel.LoadCountsAsync();
            await LoadStarLanguagesAsync();   // 同步后新 star 的语言要出现在下拉里
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // 用户主动取消不是故障：与「同步失败」分开报。已拉完的源逐源提交过（幂等 upsert），
            // 所以这句话必须说清"部分结果留下了、缺的下次补齐"，否则用户以为白点了一次同步。
            var cancelled = LocMessage.Of("top.syncCancelledBody");
            SetStatusText(cancelled);
            SetStatusDot(StatusKind.Success);
            ShowInfoBar(InfoBarSeverity.Informational, LocMessage.Of("top.syncCancelledTitle"), cancelled, 6500);
            await ViewModel.LoadCountsAsync();
        }
        catch (Exception ex)
        {
            // 异常原文是数据不是措辞：它当实参进去，句子本身仍由表管
            SetStatusText(LocMessage.Of("top.statusFailed", ex.Message));
            SetStatusDot(StatusKind.Caution);
            ShowInfoBar(InfoBarSeverity.Error, LocMessage.Of("top.syncFailedTitle"), LocMessage.Of("common.rawValue", ex.Message));
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
    public void ShowError(LocMessage title, LocMessage message)
        => ShowInfoBar(InfoBarSeverity.Error, title, message, 8000);

    /// <summary>
    /// 坏消息 ＋ <b>就地那一颗按钮</b>（批次 VQ）。
    /// <para>为什么要多这一格：只说"这个文件已经不在了"，用户仍停在原地——他真正要问的是"那这一行怎么办"。
    /// P-54 的口径是<b>程序能做的动作不许写成用户作业</b>，所以"从库里删掉这一行"必须贴在这句话旁边，
    /// 而不是让他去某个设置页找一个清理入口（今天也确实没有那一页）。
    /// 按钮只在"删得掉、且删是对的动作"那一类失败里出现——"系统里没有能开它的程序"给同一颗按钮就是错的。</para>
    /// </summary>
    public void ShowErrorWithAction(LocMessage title, LocMessage message, LocMessage actionLabel, Action onAction)
    {
        var button = new Button();
        LocText.SetMessage(button, actionLabel);   // 名字进重贴名单：这颗按钮跟的是那句坏消息，不是那条状态行
        button.Click += (_, _) =>
        {
            SyncInfoBar.IsOpen = false;
            onAction();
        };
        ShowInfoBar(InfoBarSeverity.Error, title, message, 15000, button);
    }

    /// <summary>
    /// 中性提示（不是错误也不是"操作完成"）：右下角提示卡贴不上屏幕时的兜底出口。
    /// 停留久一点——护眼这类提醒是"给你看一眼"，不该 8 秒就自己收走。
    /// </summary>
    public void ShowNotice(LocMessage title, LocMessage message)
        => ShowInfoBar(InfoBarSeverity.Informational, title, message, 15000);

    private void ShowInfoBar(InfoBarSeverity severity, LocMessage title, LocMessage? message, int autoCloseMs = -1, object? action = null)
    {
        SyncInfoBar.Severity = severity;
        // 这两句是**常驻**元素（同一颗 SyncInfoBar 反复换内容）⇒ 只许走"键＋实参进注册项"那一条路，
        // ⛔ 直接赋成品字：赋了就是"形参交身份、手臂⛔ 跟着搬"那半截（新证人 TheNoticeCardFamilyIsMovedWhole 拦着）。
        LocText.SetMessage(SyncInfoBar, InfoBar.TitleProperty, title);
        LocText.SetMessage(SyncInfoBar, InfoBar.MessageProperty, message);
        // 每次都写：上一句留下的按钮不清掉，下一次"已完成"旁边就挂着一颗对不上号的按钮。
        // （WinUI 的 InfoBar 没有 ActionContent 那一格，能挂东西的位置就是 Content——按钮放进去即可。）
        SyncInfoBar.Content = action;
        SyncInfoBar.IsOpen = true;
        if (autoCloseMs > 0)
            _ = Task.Delay(autoCloseMs).ContinueWith(_ =>
                DispatcherQueue.TryEnqueue(() => SyncInfoBar.IsOpen = false));
    }

    private void InfoBar_Close(InfoBar sender, object args) => sender.IsOpen = false;
}
