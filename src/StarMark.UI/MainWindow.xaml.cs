#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Windowing;
using StarMark.Integrations.SystemTray;
using StarMark.UI.Helpers;
using StarMark.UI.ViewModels;
using StarMark.UI.Views;

namespace StarMark.UI;

/// <summary>
/// 主窗口。NavigationView 骨架，搜索框始终可见，顶部工具栏，页面切换，主题切换。
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private DispatcherTimer? _debounceTimer;
    private readonly SettingsStore _settings = new();
    private ThemePreference _themePref;
    private TrayHost? _trayHost;
    private bool _allowExit;
    private bool _balloonShown;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = (App.Services.GetService(typeof(MainViewModel)) as MainViewModel)
            ?? throw new InvalidOperationException("MainViewModel 未注册");

        // 恢复并应用上次主题偏好
        _themePref = _settings.LoadTheme();
        ThemeManager.Apply(this, _themePref);
        UpdateThemeIcon();

        _ = ViewModel.LoadCountsAsync();

        // 托盘常驻 + 全局热键
        if (_settings.LoadEnableTray())
        {
            _trayHost = new TrayHost();
            _trayHost.ShowRequested += ShowMainWindow;
            _trayHost.ExitRequested += ExitApp;
            if (_settings.LoadEnableGlobalHotKey())
                _trayHost.TryRegisterHotKey();
            AppWindow.Closing += OnAppWindowClosing;
            Closed += (_, _) =>
            {
                _trayHost?.Dispose();
                _trayHost = null;
            };
        }

        // 默认选中搜索页（触发 SelectionChanged → 导航）
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private IntPtr MainHwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    private void ShowMainWindow()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                TrayHost.ShowAndFocus(MainHwnd);
            }
            catch { }
        });
    }

    private void ExitApp()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _allowExit = true;
            _trayHost?.Dispose();
            _trayHost = null;
            Microsoft.UI.Xaml.Application.Current.Exit();
        });
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowExit || _trayHost == null || !_settings.LoadMinimizeToTray())
            return;

        args.Cancel = true;
        try
        {
            TrayHost.HideToTray(MainHwnd);
            if (!_balloonShown && _trayHost.HotKeyRegistered)
            {
                _trayHost.ShowBalloon("StarMark 正在后台运行", "Ctrl+Alt+Space 随时呼出窗口");
                _balloonShown = true;
            }
        }
        catch { }
    }

    // ===== 主题切换 =====

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _themePref = _themePref switch
        {
            ThemePreference.Default => ThemePreference.Light,
            ThemePreference.Light => ThemePreference.Dark,
            _ => ThemePreference.Default,
        };
        _settings.SaveTheme(_themePref);
        ThemeManager.Apply(this, _themePref);
        UpdateThemeIcon();
    }

    private void UpdateThemeIcon()
    {
        ThemeIcon.Glyph = _themePref switch
        {
            ThemePreference.Light => "\uE706", // 太阳
            ThemePreference.Dark => "\uE708",  // 月亮
            _ => "\uE895",                     // 自动/同步
        };
        ToolTipService.SetToolTip(ThemeButton, _themePref switch
        {
            ThemePreference.Light => "浅色模式（点击切换）",
            ThemePreference.Dark => "深色模式（点击切换）",
            _ => "跟随系统（点击切换）",
        });
    }

    // ===== 导航 =====

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        if (item.Tag is string tag)
        {
            ViewModel.CurrentPageTag = tag;
            NavigateToPage(tag);
        }
    }

    private void NavigateToPage(string tag, object? param = null)
    {
        var pageType = tag switch
        {
            "search" => typeof(SearchPage),
            "tree" => typeof(FolderTreePage),
            "tags" => typeof(TagsPage),
            "activity" => typeof(ActivityPage),
            "hidden" => typeof(HiddenPage),
            _ => typeof(SearchPage),
        };
        ContentFrame.Navigate(pageType, param);
    }

    // ===== 搜索框防抖 =====

    private void SearchBox_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
    {
        _debounceTimer?.Stop();
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _debounceTimer.Tick += (s, args2) =>
        {
            _debounceTimer!.Stop();
            var q = SearchBox.Text;
            ViewModel.Query = q;

            if (!string.IsNullOrWhiteSpace(q) && ViewModel.CurrentPageTag != "search")
            {
                // 切换到搜索页并把查询词传给新页面 ViewModel
                ViewModel.CurrentPageTag = "search";
                foreach (var mi in NavView.MenuItems)
                {
                    if (mi is NavigationViewItem ni && ni.Tag as string == "search")
                    {
                        NavView.SelectedItem = ni;
                        break;
                    }
                }
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ContentFrame.Content is SearchPage sp)
                        sp.ViewModel.Query = q;
                });
            }
            else if (ContentFrame.Content is SearchPage sp)
            {
                sp.ViewModel.Query = q;
            }
        };
        _debounceTimer.Start();
    }

    // ===== 工具栏事件 =====

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 由子页面各自处理
    }

    private void SourceFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            var accent = (SolidColorBrush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
            var muted = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            foreach (var b in new[] { SourceAll, SourceStar, SourceBookmark })
                b.Foreground = muted;
            btn.Foreground = accent;
        }
    }

    private void ShowHidden_Click(object sender, RoutedEventArgs e)
    {
        // 由子页面处理
    }

    private void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        _ = DoSyncAsync();
    }

    private async System.Threading.Tasks.Task DoSyncAsync()
    {
        SyncButton.IsEnabled = false;
        SyncProgress.IsActive = true;
        SyncProgress.Visibility = Visibility.Visible;
        StatusDot.Fill = (SolidColorBrush)Application.Current.Resources["SystemFillColorCautionBrush"];
        StatusText.Text = "同步中...";
        SyncInfoBar.IsOpen = false;

        try
        {
            var syncCoordinator = App.Services.GetService(typeof(StarMark.Core.Sync.SyncCoordinator))
                as StarMark.Core.Sync.SyncCoordinator;
            if (syncCoordinator != null)
            {
                var summary = await syncCoordinator.SyncAllAsync(CancellationToken.None);
                var text = summary.FormatText();
                StatusText.Text = text;
                ShowInfoBar(InfoBarSeverity.Success, "索引同步完成", string.Empty, 6500);
            }
            StatusDot.Fill = (SolidColorBrush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            await ViewModel.LoadCountsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"同步失败: {ex.Message}";
            StatusDot.Fill = (SolidColorBrush)Application.Current.Resources["SystemFillColorCautionBrush"];
            ShowInfoBar(InfoBarSeverity.Error, "同步失败", ex.Message);
        }
        finally
        {
            SyncButton.IsEnabled = true;
            SyncProgress.IsActive = false;
            SyncProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowInfoBar(InfoBarSeverity severity, string title, string message, int autoCloseMs = -1)
    {
        SyncInfoBar.Severity = severity;
        SyncInfoBar.Title = title;
        SyncInfoBar.Message = message;
        SyncInfoBar.IsOpen = true;
        if (autoCloseMs > 0)
            _ = System.Threading.Tasks.Task.Delay(autoCloseMs).ContinueWith(_ =>
                DispatcherQueue.TryEnqueue(() => SyncInfoBar.IsOpen = false));
    }

    private void InfoBar_Close(InfoBar sender, object args)
    {
        sender.IsOpen = false;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "设置页面待实现";
    }
}