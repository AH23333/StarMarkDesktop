#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.Integrations.SystemTray;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;
using StarMark.UI.Views;

namespace StarMark.UI;

/// <summary>
/// 主窗口。NavigationView 骨架，搜索框始终可见，顶部工具栏，页面切换，主题切换。
/// 同时承载系统托盘与桌面组件（DeskBox 式独立小组件）的入口。
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
    private readonly WidgetManager _widgetManager;
    private MenuFlyout? _widgetsMenu;
    private int _diagSimStep;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = (App.Services.GetService(typeof(MainViewModel)) as MainViewModel)
            ?? throw new InvalidOperationException("MainViewModel 未注册");
        _widgetManager = (App.Services.GetService(typeof(WidgetManager)) as WidgetManager)
            ?? throw new InvalidOperationException("WidgetManager 未注册");
        _widgetManager.Initialize(DispatcherQueue);
        _widgetManager.GlobalSearchRequested += SearchFromWidget;

        SetupImmersiveTitleBar();
        SetSourceButtonsHighlight("all");

        // 恢复并应用上次主题偏好
        _themePref = _settings.LoadTheme();
        ThemeManager.Apply(this, _themePref);
        UpdateThemeIcon();

        _ = ViewModel.LoadCountsAsync();

        // 托盘常驻 + 全局热键
        if (_settings.LoadEnableTray()) CreateTray(registerHotkey: _settings.LoadEnableGlobalHotKey());
        AppWindow.Closing += OnAppWindowClosing;

        // 默认选中文件夹页（触发 SelectionChanged → 导航）
        var startTag = Environment.GetEnvironmentVariable("STARMARK_START_PAGE");
        var startIndex = startTag switch { "tags" => 1, "tree" => 0, _ => 0 };
        NavView.SelectedItem = NavView.MenuItems[startIndex];

        // 启动时恢复已启用的桌面组件
        DispatcherQueue.TryEnqueue(async () =>
        {
            try { await _widgetManager.RestoreOnStartupAsync(); }
            catch (Exception ex) { StarLog.Error("恢复桌面组件失败", ex); }
        });

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

        SetupDiag();
    }

    private IntPtr MainHwnd => WindowNative.GetWindowHandle(this);

    // ───────────────────────── 托盘 ─────────────────────────

    private void CreateTray(bool registerHotkey)
    {
        if (_trayHost is not null) return;
        _trayHost = new TrayHost();
        _trayHost.IsWidgetEnabled = i => _widgetManager.IsEnabled((WidgetKind)i);
        _trayHost.ShowRequested += () => DispatcherQueue.TryEnqueue(() => Present(false));
        _trayHost.ExitRequested += () => DispatcherQueue.TryEnqueue(ExitApp);
        _trayHost.WidgetsToggleRequested += () => DispatcherQueue.TryEnqueue(() => _ = _widgetManager.ToggleAllAsync());
        _trayHost.WidgetToggleRequested += i => DispatcherQueue.TryEnqueue(() =>
        {
            var kind = (WidgetKind)i;
            _ = _widgetManager.SetEnabledAsync(kind, !_widgetManager.IsEnabled(kind));
        });
        _trayHost.ShowAllWidgetsRequested += () => DispatcherQueue.TryEnqueue(() => _ = _widgetManager.ShowAllAsync());
        _trayHost.HideAllWidgetsRequested += () => DispatcherQueue.TryEnqueue(() => _ = _widgetManager.HideAllAsync());
        _trayHost.SettingsRequested += () => DispatcherQueue.TryEnqueue(() => Present(true));
        _trayHost.Initialize(registerHotkey);
    }

    private void DisposeTray()
    {
        _trayHost?.Dispose();
        _trayHost = null;
    }

    /// <summary>设置页保存后调用：托盘/热键即时生效，无需重启。</summary>
    public void ApplyTraySettings()
    {
        var trayEnabled = _settings.LoadEnableTray();
        var hotkeyEnabled = _settings.LoadEnableGlobalHotKey();
        if (trayEnabled)
        {
            CreateTray(registerHotkey: false);
            if (hotkeyEnabled) _trayHost?.RegisterGlobalHotKey();
            else _trayHost?.UnregisterGlobalHotKey();
        }
        else
        {
            DisposeTray();
        }
    }

    private async void ExitApp()
    {
        _allowExit = true;
        try { await _widgetManager.ShutdownAllAsync(); }
        catch (Exception ex) { StarLog.Error("关闭桌面组件失败", ex); }
        DisposeTray();
        Application.Current.Exit();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowExit) return;

        if (_trayHost != null && _settings.LoadMinimizeToTray())
        {
            // 最小化到托盘：主窗口隐藏，组件继续保留
            args.Cancel = true;
            try
            {
                AppWindow.Hide();
                if (!_balloonShown)
                {
                    _trayHost.ShowNotification("StarMark 正在后台运行", "Ctrl+Alt+Space 随时呼出窗口");
                    _balloonShown = true;
                }
            }
            catch { }
            return;
        }

        // 真正退出：取消默认关闭流程，统一关停组件/托盘后结束进程，
        // 避免组件窗口（WS_EX_TOOLWINDOW）残留导致进程孤儿化
        args.Cancel = true;
        ExitApp();
    }

    /// <summary>唤起并前置主窗口；settings=true 时直接打开设置页。</summary>
    public void Present(bool settings)
    {
        try
        {
            if (!AppWindow.IsVisible) AppWindow.Show();
            Activate();
            WindowInterop.ShowWindow(MainHwnd, WindowInterop.SW_RESTORE);
            WindowInterop.SetForegroundWindow(MainHwnd);
            if (settings) OpenSettingsPage();
        }
        catch (Exception ex) { StarLog.Error("唤起主窗口失败", ex); }
    }

    private void OpenSettingsPage()
    {
        ViewModel.CurrentPageTag = "settings";
        NavView.SelectedItem = null;
        SetNavVisible(false);
        if (ContentFrame.Content is not SettingsPage)
            ContentFrame.Navigate(typeof(SettingsPage));
        PushToolbarToContent();
    }

    private void SearchFromWidget(string query)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Present(false);
            SearchBox.Text = query;
            if (string.IsNullOrWhiteSpace(query)) return;
            ViewModel.Query = query;
            ViewModel.CurrentPageTag = "search";
            NavView.SelectedItem = null;
            SetNavVisible(false);
            ContentFrame.Navigate(typeof(SearchPage));
            PushToolbarToContent();
            if (ContentFrame.Content is SearchPage sp)
                sp.ViewModel.Query = query;
        });
    }

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

    public void RefreshThemeIcon(ThemePreference pref)
    {
        _themePref = pref;
        ThemeManager.Apply(this, pref);
        UpdateThemeIcon();
    }

    // ===== 导航 =====

    /// <summary>
    /// 搜索态折叠整条导航栏，浏览态恢复。
    /// 浏览器扩展是「搜索时用 toolbar 整行替换 tabs」，不留空白；
    /// 这里靠 NavView 独占 Grid 的一行（Height=Auto）+ Collapsed 实现同样效果。
    /// </summary>
    private void SetNavVisible(bool visible)
        => NavView.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs? args)
    {
        // 编程触发（初始选择）时 args 为 null，直接读 SelectedItem
        if (NavView.SelectedItem is not NavigationViewItem item) return;
        if (item.Tag is string tag)
        {
            ViewModel.CurrentPageTag = tag;
            SetNavVisible(true);
            NavigateToPage(tag);
            PushToolbarToContent();
        }
    }

    public void NavigateTo(string tag, object? param = null)
    {
        NavView.SelectedItem = null;
        SetNavVisible(tag is not ("search" or "settings"));
        NavigateToPage(tag, param);
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
                SetNavVisible(false);
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
                // 清空关键词后恢复导航栏，否则用户被困在搜索页无法切回浏览页
                if (string.IsNullOrWhiteSpace(q)) SetNavVisible(true);
            }
        };
        _debounceTimer.Start();
    }

    private void SearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (ContentFrame.Content is not SearchPage sp || sp.ViewModel.Results.Count == 0) return;
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Down:
                sp.MoveKeyboardSelection(1);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Up:
                sp.MoveKeyboardSelection(-1);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter when sp.ViewModel.SelectedItem is { } selected:
                var ctrl = Microsoft.UI.Input.InputKeyboardSource
                    .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                if (ctrl) ItemCardActions.OpenLocation(selected);
                else ItemCardActions.Open(SearchBox.XamlRoot, selected.Id);
                e.Handled = true;
                break;
        }
    }

    // ===== 工具栏事件（全局唯一：排序、来源、显示隐藏）=====

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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
        // 复选框只是当前页过滤器；隐藏页由导航菜单“隐藏”进入
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

    private string CurrentSourceTag() => _currentSource;

    // ───────── 语言筛选（常态显示于工具栏；选项由搜索页结果聚合）─────────

    private const string LanguageAllItem = "语言：全部";
    private bool _syncingLanguageCombo;
    private bool _languageHooked;

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLanguageCombo) return;
        if (ContentFrame?.Content is not SearchPage sp) return;
        var selected = LanguageCombo.SelectedItem as string;
        sp.ViewModel.CurrentLanguage = selected is null || selected == LanguageAllItem
            ? string.Empty
            : selected;
    }

    private void HookLanguageOptions(SearchPageViewModel vm)
    {
        if (_languageHooked) return;
        _languageHooked = true;
        // SearchPageViewModel 是单例：选项集合变化（每次搜索后重建）时同步下拉框
        vm.AvailableLanguages.CollectionChanged += (_, _) => SyncLanguageCombo(vm);
    }

    /// <summary>把搜索页聚合出的语言选项同步到工具栏下拉框（null/空 = 回到「语言：全部」）。</summary>
    private void SyncLanguageCombo(SearchPageViewModel? vm)
    {
        _syncingLanguageCombo = true;
        try
        {
            var items = new List<string> { LanguageAllItem };
            if (vm is not null) items.AddRange(vm.AvailableLanguages);
            LanguageCombo.ItemsSource = items;
            var current = vm?.CurrentLanguage ?? string.Empty;
            LanguageCombo.SelectedIndex = string.IsNullOrEmpty(current) ? 0 : items.IndexOf(current);
        }
        finally { _syncingLanguageCombo = false; }
    }

    private string CurrentSortTag()
        => SortCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : "recent";

    private void SyncButton_Click(object sender, RoutedEventArgs e) => _ = DoSyncAsync();

    private async Task DoSyncAsync()
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
            _ = Task.Delay(autoCloseMs).ContinueWith(_ =>
                DispatcherQueue.TryEnqueue(() => SyncInfoBar.IsOpen = false));
    }

    private void InfoBar_Close(InfoBar sender, object args) => sender.IsOpen = false;

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettingsPage();

    // ===== 桌面组件顶栏入口 =====

    private void WidgetsButton_Click(object sender, RoutedEventArgs e)
    {
        _widgetsMenu ??= BuildWidgetsMenu();
        _widgetsMenu.ShowAt(WidgetsButton, new Point(0, WidgetsButton.ActualHeight));
    }

    private MenuFlyout BuildWidgetsMenu()
    {
        var menu = new MenuFlyout();
        foreach (var kind in WidgetStorage.AllKinds)
        {
            var item = new ToggleMenuFlyoutItem { Text = WidgetStorage.KindTitle(kind) };
            var captured = kind;
            item.Click += (_, _) =>
                _ = _widgetManager.SetEnabledAsync(captured, !_widgetManager.IsEnabled(captured));
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var showAll = new MenuFlyoutItem { Text = "全部显示" };
        showAll.Click += (_, _) => _ = _widgetManager.ShowAllAsync();
        var hideAll = new MenuFlyoutItem { Text = "全部隐藏" };
        hideAll.Click += (_, _) => _ = _widgetManager.HideAllAsync();
        menu.Items.Add(showAll);
        menu.Items.Add(hideAll);
        menu.Items.Add(new MenuFlyoutSeparator());
        var toggle = new MenuFlyoutItem { Text = "显示/隐藏全部组件" };
        toggle.Click += (_, _) => _ = _widgetManager.ToggleAllAsync();
        menu.Items.Add(toggle);
        var manage = new MenuFlyoutItem { Text = "在设置中管理…" };
        manage.Click += (_, _) => OpenSettingsPage();
        menu.Items.Add(manage);

        menu.Opening += (_, _) =>
        {
            for (var i = 0; i < WidgetStorage.AllKinds.Count; i++)
            {
                if (menu.Items[i] is ToggleMenuFlyoutItem t)
                    t.IsChecked = _widgetManager.IsEnabled(WidgetStorage.AllKinds[i]);
            }
        };
        return menu;
    }

    // ===== 开发辅助：状态转储（STARMARK_DIAG / STARMARK_SIM_*）=====

    private void SetupDiag()
    {
        var diagPath = Environment.GetEnvironmentVariable("STARMARK_DIAG");
        var simQuery = Environment.GetEnvironmentVariable("STARMARK_SIM_QUERY");
        if (string.IsNullOrWhiteSpace(diagPath)) return;

        var simOn = !string.IsNullOrWhiteSpace(simQuery);
        var diagTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        diagTimer.Tick += (_, _) =>
        {
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
                    SearchBox.Text = simQuery!;      // 第1次：输入查询
                else if (probe == simQuery && _diagSimStep == 0)
                {
                    _diagSimStep = 1;
                    SearchBox.Text = string.Empty;  // 第2次：清空搜索栏
                }
            }
            System.IO.File.AppendAllText(diagPath!, lines.ToString());
        };
        diagTimer.Start();
    }
}