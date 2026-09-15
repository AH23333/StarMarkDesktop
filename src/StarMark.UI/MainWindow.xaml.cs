#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Windows.Graphics;
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

        SetupImmersiveTitleBar();
        SetSourceButtonsHighlight("all");

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

        // 默认选中文件夹页（触发 SelectionChanged → 导航）
        var startTag = Environment.GetEnvironmentVariable("STARMARK_START_PAGE");
        var startIndex = startTag switch { "tags" => 1, "tree" => 0, _ => 0 };
        NavView.SelectedItem = NavView.MenuItems[startIndex];

        // 开发辅助：启动即搜索（STARMARK_START_QUERY），用于冒烟渲染卡片
        var startQuery = Environment.GetEnvironmentVariable("STARMARK_START_QUERY");
        if (!string.IsNullOrWhiteSpace(startQuery))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.CurrentPageTag = "search";
                NavView.SelectedItem = null;
                ContentFrame.Navigate(typeof(SearchPage));
                PushToolbarToContent();
                if (ContentFrame.Content is SearchPage sp)
                    sp.ViewModel.Query = startQuery;
            });
        }

        // 开发辅助：状态转储 + 脚本化搜索/清空（STARMARK_DIAG / STARMARK_SIM_*），用于复现 UI 缺陷并留文本证据
        var diagPath = Environment.GetEnvironmentVariable("STARMARK_DIAG");
        var simQuery = Environment.GetEnvironmentVariable("STARMARK_SIM_QUERY");
        if (!string.IsNullOrWhiteSpace(diagPath))
        {
            var simOn = !string.IsNullOrWhiteSpace(simQuery);
            var diagTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            diagTimer.Tick += (_, _) =>
            {
                static string F(object? o) => o?.ToString() ?? "null";
                var lines = new System.Text.StringBuilder();
                lines.AppendLine($"[tick {DateTime.Now:HH:mm:ss.fff}] page={ViewModel.CurrentPageTag} searchbox=[{SearchBox.Text}]");
                if (ContentFrame.Content is SearchPage sp)
                {
                    lines.AppendLine($"  SEARCH: query=[{sp.ViewModel.Query}] empty=[{sp.ViewModel.EmptyHint}] " +
                                     $"results={sp.ViewModel.Results.Count} has={sp.ViewModel.HasResults} busy={sp.ViewModel.IsSearching}");
                }
                if (ContentFrame.Content is FolderTreePage tp)
                {
                    var roots = tp.ViewModel.Roots;
                    lines.AppendLine($"  TREE: roots={roots.Count} empty=[{tp.ViewModel.EmptyHint}]");
                    foreach (var r in roots.Take(12))
                    {
                        lines.AppendLine($"    - {r.Name} (total={r.TotalCount} own={r.Items.Count} sub={r.Children.Count})");
                        foreach (var c in tp.ViewModel.Hydrate(r).Take(6))
                            lines.AppendLine($"        card[{r.Name}]: {c.Type} | {c.Title}");
                        if (r.Children.Count > 0)
                            foreach (var ch in r.Children)
                                foreach (var c in tp.ViewModel.Hydrate(ch).Take(3))
                                    lines.AppendLine($"        card[{r.Name}/{ch.Name}]: {c.Type} | {c.Title}");
                    }
                }
                if (simOn)
                {
                    var probe = SearchBox.Text;
                    if (probe == string.Empty)
                        SearchBox.Text = simQuery;      // 第1次：输入查询
                    else if (probe == simQuery && _diagSimStep == 0)
                    {
                        _diagSimStep = 1;
                        SearchBox.Text = string.Empty;  // 第2次：清空搜索栏
                    }
                }
                System.IO.File.AppendAllText(diagPath, lines.ToString());
            };
            diagTimer.Start();
        }
    }

    private int _diagSimStep;

    private IntPtr MainHwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// <summary>Win11 风格沉浸式标题栏：内容扩展到标题栏区域，标题栏按钮透明融合。</summary>
    private void SetupImmersiveTitleBar()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = Colors.Transparent;
        titleBar.ButtonPressedBackgroundColor = Colors.Transparent;

        ApplyDragRects();
        SizeChanged += (_, _) => ApplyDragRects();
    }

    private void ApplyDragRects()
    {
        // 拖拽区 = 顶栏中段（logo + 状态），右侧留给交互按钮与系统窗口按钮。
        // 此前拖拽区覆盖到 width-140，把同步/主题/设置按钮整块吞掉 → 顶部功能失效。
        var width = AppWindow.Size.Width;
        var height = TopBar.ActualHeight > 0 ? (int)TopBar.ActualHeight : 52;
        var buttonsRightPad = 150;                       // 顶栏右侧 padding（避开系统窗口按钮）
        var buttonsWidth = TopBarButtons.ActualWidth > 0 ? TopBarButtons.ActualWidth : 150;
        var dragW = Math.Max(0, width - buttonsRightPad - (int)buttonsWidth - 8);
        AppWindow.TitleBar.SetDragRectangles(new RectInt32[]
        {
            new() { X = 0, Y = 0, Width = dragW, Height = height },
        });
    }

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
            PushToolbarToContent();
        }
    }

    public void NavigateTo(string tag)
    {
        NavView.SelectedItem = null;
        NavigateToPage(tag);
        PushToolbarToContent();
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
            "settings" => typeof(SettingsPage),
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
                // 顶部搜索框常驻：输入自动切到搜索页（搜索页不在导航菜单内）
                ViewModel.CurrentPageTag = "search";
                NavView.SelectedItem = null;
                DispatcherQueue.TryEnqueue(() =>
                {
                    ContentFrame.Navigate(typeof(SearchPage));
                    PushToolbarToContent();
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

    // ===== 工具栏事件（全局唯一：排序、来源、显示隐藏）=====

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            PushToolbarToContent();
    }

    private string _currentSource = "all";

    private void SourceFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        _currentSource = tag;
        SetSourceButtonsHighlight(tag);
        PushToolbarToContent();
    }

    private void ShowHidden_Click(object sender, RoutedEventArgs e)
    {
        PushToolbarToContent();
    }

    private void SetSourceButtonsHighlight(string source)
    {
        var accent = (SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var muted = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var white = new SolidColorBrush(Colors.White);
        foreach (var (btn, tag) in new[] { (SourceAll, "all"), (SourceStar, "star"), (SourceBookmark, "bookmark") })
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
                break;
            case FolderTreePage ftp:
                ftp.ViewModel.CurrentSource = source;
                ftp.ViewModel.CurrentSort = sort;
                ftp.ViewModel.ShowHidden = hidden;
                break;
        }
    }

    private string CurrentSourceTag() => _currentSource;

    private string CurrentSortTag()
        => SortCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : "recent";

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
        ViewModel.CurrentPageTag = "settings";
        NavView.SelectedItem = null;
        ContentFrame.Navigate(typeof(SettingsPage));
        PushToolbarToContent();
    }

    public void RefreshThemeIcon(ThemePreference pref)
    {
        _themePref = pref;
        UpdateThemeIcon();
    }
}