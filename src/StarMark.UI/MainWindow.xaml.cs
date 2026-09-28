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
using StarMark.Core.Hotkeys;
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
    /// <summary>当前实际生效的主题偏好（运行期切换后会立即更新），供弹窗等独立窗口对齐主界面主题，
    /// 避免「主界面已切浅色、弹窗仍按旧磁盘偏好渲染成深色」的割裂。</summary>
    public ThemePreference CurrentThemePreference => _themePref;
    private TrayHost? _trayHost;
    private bool _allowExit;
    private bool _balloonShown;
    private readonly WidgetManager _widgetManager;
    private MenuFlyout? _widgetsMenu;
    private int _diagSimStep;
    // star 项目中真实存在的编程语言（下拉数据源，见 LoadStarLanguagesAsync）
    private readonly List<string> _starLanguages = new();
    // 主界面的数据变更同步器：此前全应用只有组件 VM 订阅 DataChangeHub，主窗口一个都没有 ——
    // 组件里改一条随记/待办、或备份还原落库后，主界面的侧栏计数与当前列表页
    // （文件夹 / 标签 / 已隐藏 / 活动）全停在旧数据，要切页或重启才更新。这里补上订阅。
    private DataChangeReloader? _dataSync;

    /// <summary>
    /// 组件侧的托盘气泡出口（倒计时到点等）。<b>返回是否真的发出去了</b>：托盘没启用时是 false，
    /// 调用方据此留下替代证据（日志 + 组件内的常驻高亮），而不是以为"已经提醒过用户了"。
    /// </summary>
    public bool TryShowTrayNotification(string title, string message)
    {
        var tray = _trayHost;
        return tray is not null && tray.ShowNotification(title, message);
    }

    public MainWindow()
    {
        InitializeComponent();
        StarMark.Abstractions.StartupProfile.Mark("主窗 XAML 加载（InitializeComponent）");
        WindowInterop.TrackWindow(this);   // 供弹窗按发起窗口所在显示器居中
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        _widgetManager = App.Services.GetRequiredService<WidgetManager>();
        _widgetManager.Initialize(DispatcherQueue);
        _widgetManager.GlobalSearchRequested += SearchFromWidget;

        // 半透明材质（macOS 风）：主窗口与桌面组件统一质感，材质与不透明度可在设置页「常规 → 外观」调整
        RefreshAppearance();

        SetupImmersiveTitleBar();
        SetSourceButtonsHighlight("all");

        // 恢复并应用上次主题偏好
        _themePref = _settings.LoadTheme();
        ThemeManager.Apply(this, _themePref);
        UpdateThemeIcon();
        RefreshAppearance();   // 主题确定后再按实际主题解析背景/材质，避免启动即用错主题色

        // 与组件窗口(WidgetWindow 订阅 RootBorder.ActualThemeChanged)对齐：主题在运行期落地/翻转后，
        // 用窗口"实际主题"重铺材质与背景。否则主窗只按启动时推导的偏好上色，
        // 浅色主题下可能仍按 isDark=true 铺成黑/深色（实色纯黑、深色材质基色）。
        RootGrid.ActualThemeChanged += (_, _) => RefreshAppearance();

        _ = ViewModel.LoadCountsAsync();
        _ = LoadStarLanguagesAsync();   // 语言下拉只显示 star 中真实存在的语言

        // 托盘常驻 + 全局热键
        if (_settings.LoadEnableTray()) CreateTray();
        AppWindow.Closing += OnAppWindowClosing;

        // 默认选中文件夹页（触发 SelectionChanged → 导航）
        StarMark.Abstractions.StartupProfile.Mark("主窗侧栏与托盘（导航到首屏之前）");
        var startTag = Environment.GetEnvironmentVariable("STARMARK_START_PAGE");
        var startIndex = startTag switch { "tags" => 1, "tree" => 0, _ => 0 };
        NavView.SelectedItem = NavView.MenuItems[startIndex];

        // 「热榜」是可选浏览面：开关关着时导航项本身不显示（用户裁决），设置页改动即时生效、不需要重启。
        ApplyTrendingNavVisibility(_settings.LoadTrendingEnabled());
        ApplyRssNavVisibility(_settings.LoadRssEnabled());

        // 启动时恢复已启用的桌面组件
        DispatcherQueue.TryEnqueue(async () =>
        {
            try { await _widgetManager.RestoreOnStartupAsync(); }
            catch (Exception ex) { StarLog.Error("恢复桌面组件失败", ex); }
            // WE-2：恢复**之后**那一段以前没有刻度——日志里最后一个分段停在"桌面组件恢复"（它在恢复结束时才记），
            // 而卡顿看门狗报的那 1.5 s 正好落在它后面，于是谁也说不清是谁占着 UI 线程。补上右半边刻度。
            StarMark.Abstractions.StartupProfile.Mark("组件恢复任务返回（UI 队列）");
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

        // 订阅数据广播（去抖 250ms）：别处（组件写随记/待办、同步、备份还原）落库后，
        // 刷新侧栏计数并重载当前列表页。构造必须在 UI 线程且 ContentFrame 已就绪之后。
        _dataSync = new DataChangeReloader(RefreshOnDataChangedAsync);
    }

    /// <summary>数据变更后的主界面刷新：计数恒刷，当前页按类型重载（见 <see cref="RefreshCurrentPageForData"/>）。</summary>
    private async Task RefreshOnDataChangedAsync()
    {
        await ViewModel.LoadCountsAsync();
        RefreshCurrentPageForData();
    }

    /// <summary>
    /// 只重载「当前正显示、且内容来自条目库」的页，避免无谓打库。
    /// <para>
    /// 刻意<b>不</b>自动重载 <see cref="SearchPage"/>：用户可能正在搜索框逐字输入，
    /// 广播一来就重跑查询会打断编辑（且结果本由 OnQueryChanged 驱动，无需外部刷新）；
    /// <see cref="SettingsPage"/> 无条目列表，同样跳过。
    /// </para>
    /// </summary>
    private void RefreshCurrentPageForData()
    {
        switch (ContentFrame?.Content)
        {
            case FolderTreePage ftp: _ = ftp.ViewModel.LoadCommand.ExecuteAsync(null); break;
            case TagsPage tp: tp.ViewModel.LoadCommand.Execute(null); break;
            case HiddenPage hp: hp.ViewModel.LoadCommand.Execute(null); break;
            case ActivityPage ap: ap.ViewModel.LoadCommand.Execute(null); break;
        }
    }

    private IntPtr MainHwnd => WindowNative.GetWindowHandle(this);

    /// <summary>
    /// 重新应用毛玻璃材质（亚克力 / 云母 / 不透明）。
    /// 主窗口可选择是否跟随组件的同款材质（设置页「常规 → 外观」）；开启后根网格设为透明，
    /// 让霜化背景透出（与组件一致）。
    /// </summary>
    public void RefreshAppearance()
    {
        try
        {
            // 主窗口材质独立于组件（批次 J：拆「主窗口使用同一材质」为各自可选）。
            // 未选材质（None）即不透明，沿用旧的「开关关」语义；其余材质与组件走同一套用色逻辑。
            var kind = _settings.LoadMainWindowBackdrop();
            var translucent = kind != WidgetBackdropKind.None;
            // 优先取窗口"实际主题"（RootGrid 已被 ThemeManager 盖成具体 Light/Dark，且随系统/偏好翻转），
            // 仅在实际主题还是 Default（尚未落地）时才回落到按偏好推导。
            // 组件窗口一直是这么做的，故组件浅色正常、主窗曾出现浅色仍偏黑。
            var theme = RootGrid.ActualTheme == ElementTheme.Default
                ? TargetTheme(_themePref)
                : RootGrid.ActualTheme;
            WidgetAppearance.ApplyBackdrop(
                this, kind, _settings.LoadMainWindowOpacity(), theme);
            // 主题色一律按目标主题解析（ThemeBrush.For），不能取 Application.Current.Resources[key]
            // —— 应用级主题在窗口创建后冻结，那里解析出的永远是初始主题的画笔。
            // 表面画笔跟着材质走（与组件 ApplyAppearanceCore 同构，保证主界面/组件同款观感、同步切换）：
            // ① 背衬画笔已接管整窗（除实色外的亚克力/云母/纯色）时，RootGrid 透明让背衬单独透出着色，
            //    避免与背衬叠两层 alpha（发灰/过实）；② 实色或未生效时回落 MainWindowSurfaceBrush 实色，
            //    绝不变全透明幽灵窗。批次 K 起材质统一走扁平背衬，不再区分「原生控制器 / 纯色」两条路径。
            var backdropActive = translucent && WidgetAppearance.IsBackdropSurfaceActive(this);
            if (backdropActive)
            {
                RootGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else if (kind == WidgetBackdropKind.None)
            {
                // 实色（None）：不再交回框架 XAML `{ThemeResource ApplicationPageBackgroundThemeBrush}`——
                // 批次 O 曾假设它"浅色即浅色"，但真机在浅色下仍解析出深色/纯黑（该内置画笔的 ThemeDictionaries
                // 桶在运行期是 ResourceDictionaryThemeData，XAML 侧偶发命中深色 Default 桶）。
                // 改为按窗口"实际主题"显式铺主题化实色：浅色＝浅灰白、深色＝柔和深灰（非纯黑），且随
                // ActualThemeChanged（构造期已订阅）在切换时重铺。绝不依赖任何可能判错的框架画笔资源。
                var lightSurface = RootGrid.ActualTheme != ElementTheme.Dark;
                RootGrid.Background = new SolidColorBrush(lightSurface
                    ? Windows.UI.Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3)
                    : Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x20));
                // 节流：RefreshAppearance 在拖滑杆时按 350 ms 自动保存的节奏被调，这句会成串刷屏。
                StarLog.InfoThrottled($"main-solid:{_themePref}",
                    $"[材质诊断] 实色(None) 主窗底色按实际主题铺：ActualTheme={RootGrid.ActualTheme} pref={_themePref} → {(lightSurface ? "浅" : "深")}",
                    windowMs: 5000);
            }
            else
            {
                // 半透明材质但背衬未挂上（防御路径）：用按 isDark 直接算色的 SurfaceBrush，可靠不分主题。
                RootGrid.Background = WidgetAppearance.MainWindowSurfaceBrush(theme, kind, translucent, _settings.LoadMainWindowOpacity());
            }
            // 主题切换后代码构建的画笔重解析（来源按钮高亮、状态点）
            SetSourceButtonsHighlight(_currentSource);
            SetStatusDot(_statusKind);
            // 弹窗是独立顶层窗口：主窗只改自己的根主题传导不到它们，必须显式重刷，
            // 否则切深浅色时"仍打开的弹窗全部停在旧主题"（外观编辑器/预览/标签/确认框都在内）。
            CenteredDialog.ApplyThemeToOpenDialogs(_themePref);
        }
        catch (Exception ex)
        {
            StarLog.Error("应用主窗口半透明材质失败", ex);
        }
    }

    /// <summary>把主题偏好推导为可用于 <see cref="ThemeBrush.For"/> 的元素主题（Default 跟随系统）。</summary>
    private static ElementTheme TargetTheme(ThemePreference pref) => pref switch
    {
        ThemePreference.Light => ElementTheme.Light,
        ThemePreference.Dark => ElementTheme.Dark,
        _ => ThemeManager.IsSystemDark() ? ElementTheme.Dark : ElementTheme.Light,
    };

    // ───────────────────────── 托盘 ─────────────────────────

    private void CreateTray()
    {
        if (_trayHost is not null) return;
        _trayHost = new TrayHost();
        // 子菜单直接从组件注册表生成：以前这是 TrayHost 里一份手写的 5 条标题数组，
        // 注册表长出到 12 种后，7 种组件（含新的剪贴板格）在托盘里既看不到也关不掉（P-62b）。
        _trayHost.WidgetMenuItems = WidgetStorage.AllKinds
            .Select(k => new TrayWidgetItem((int)k, WidgetStorage.KindTitle(k)))
            .ToList();
        _trayHost.IsWidgetEnabled = kind => _widgetManager.IsEnabled((WidgetKind)kind);
        _trayHost.ShowRequested += () => DispatcherQueue.TryEnqueue(() => Present(false));
        _trayHost.ExitRequested += () => DispatcherQueue.TryEnqueue(ExitApp);
        _trayHost.WidgetsToggleRequested += () => DispatcherQueue.TryEnqueue(() => _ = _widgetManager.ToggleAllAsync());
        _trayHost.WidgetToggleRequested += kind => DispatcherQueue.TryEnqueue(() =>
        {
            var k = (WidgetKind)kind;
            _ = _widgetManager.SetEnabledAsync(k, !_widgetManager.IsEnabled(k));
        });
        _trayHost.ShowAllWidgetsRequested += () => DispatcherQueue.TryEnqueue(() => _ = _widgetManager.ShowAllAsync());
        _trayHost.HideAllWidgetsRequested += () => DispatcherQueue.TryEnqueue(() => _ = _widgetManager.HideAllAsync());
        _trayHost.SettingsRequested += () => DispatcherQueue.TryEnqueue(() => Present(true));
        // 附加命令的清单用回调现取，不用属性快照：主题/性能模式/自启/热键开关都会变，
        // 快照等于"托盘显示的是上一次右键时的状态"，勾选框会指着错的那一项。
        _trayHost.CommandProvider = BuildTrayCommands;
        _trayHost.CommandInvoked += tag => DispatcherQueue.TryEnqueue(() => RunTrayCommand(tag));
        _trayHost.Initialize();
    }

    // ───────────────────────── 托盘：附加命令 ─────────────────────────
    // 编号是"宿主自己的语义标签"，与 TrayHost 发出去的菜单命令号无关（它按渲染顺序发号）。

    private const int TrayScreenshot = 1;
    private const int TrayTopmostToggle = 2;
    private const int TrayScreenPin = 3;
    private const int TrayScreenOcr = 7;
    private const int TrayCanvas = 8;
    private const int TrayPinsShowHide = 4;
    private const int TrayPinsClickThrough = 5;
    private const int TrayPinsCloseAll = 6;
    private const int TrayThemeDefault = 10;
    private const int TrayThemeLight = 11;
    private const int TrayThemeDark = 12;
    private const int TrayPerfBalanced = 20;
    private const int TrayPerfSaver = 21;
    private const int TrayAutostart = 30;
    private const int TrayHotkeysEnabled = 31;

    private static StarMark.UI.Services.AutostartService Autostart()
        => App.Services.GetRequiredService<StarMark.UI.Services.AutostartService>();

    /// <summary>右键那一刻现取托盘附加命令：勾选态反映当前设置，正在截图时不让人再点一次。</summary>
    private IReadOnlyList<TrayHost.TrayCommandItem> BuildTrayCommands()
    {
        var perf = _settings.LoadPerformanceMode();
        var hotkeysOn = _settings.LoadEnableGlobalHotKey();
        var (pins, hidden) = (StarMark.UI.Services.PinManager.Count, StarMark.UI.Services.PinManager.AreHidden);
        var list = new List<TrayHost.TrayCommandItem>
        {
            new("截图（框选区域）", TrayScreenshot,
                Enabled: !StarMark.UI.Services.ScreenshotService.IsCapturing, SeparatorBefore: true),
            new("贴图（框选后钉在桌面）", TrayScreenPin,
                Enabled: !StarMark.UI.Services.ScreenshotService.IsCapturing),
            new("识字（框选后复制文字）", TrayScreenOcr,
                Enabled: !StarMark.UI.Services.ScreenshotService.IsCapturing),
            // 画布的开关。穿透态下画布收不到任何鼠标事件，托盘这一项与全局热键是"找回来"的两条路（§16.6）。
            new("屏幕画布（讲解时在屏幕上画）", TrayCanvas,
                StarMark.UI.Services.CanvasService.IsRunning, SeparatorBefore: true),
            // 三条"所有贴图"的动作。一张都没有时点它们都是空动作 ⇒ 灰掉并把状态写进标签，
            // 比"点了没反应"好（P-54 口径）。标签按要执行的动作说人话（菜单惯例），
            // 只有"忽略鼠标"这项是状态开关，所以它用勾选态。
            new(PinLabel(pins, hidden), TrayPinsShowHide, SeparatorBefore: true, Enabled: pins > 0),
            new("贴图忽略鼠标", TrayPinsClickThrough, StarMark.UI.Services.PinManager.ClickThrough, Enabled: pins > 0),
            new("关闭所有贴图", TrayPinsCloseAll, Enabled: pins > 0),
            new("所有组件置顶 / 不置顶", TrayTopmostToggle, SeparatorBefore: true),
            new("主题 · 跟随系统", TrayThemeDefault, _themePref == ThemePreference.Default, SeparatorBefore: true),
            new("主题 · 浅色", TrayThemeLight, _themePref == ThemePreference.Light),
            new("主题 · 深色", TrayThemeDark, _themePref == ThemePreference.Dark),
            new("性能 · 均衡", TrayPerfBalanced, perf == StarMark.Core.Performance.PerformanceMode.Balanced, SeparatorBefore: true),
            new("性能 · 省资源", TrayPerfSaver, perf == StarMark.Core.Performance.PerformanceMode.ResourceSaver),
            new("开机自动启动", TrayAutostart, Autostart().IsEnabled(), SeparatorBefore: true),
            new("全局快捷键已启用", TrayHotkeysEnabled, hotkeysOn),
        };
        // 画布总开关关掉时整条不出现（发起人裁决："关掉就别留入口"）。按 Ctrl+Alt+D 仍会给一句
        // "要先在设置里打开"——那是 CanvasService 的闸门，不是这条菜单项的职责（见 Start 里的开关检查）。
        return _settings.LoadCanvasEnabled()
            ? list
            : list.Where(item => item.Tag != TrayCanvas).ToList();
    }

    /// <summary>贴图显隐那一项的标签：没有贴图时把状态写进文字，灰掉的项才知道自己为什么点不动。</summary>
    private static string PinLabel(int pins, bool hidden)
        => pins == 0 ? "显示 / 收起所有贴图（当前没有贴图）" : hidden ? "显示所有贴图" : "收起所有贴图";

    private void RunTrayCommand(int tag)
    {
        switch (tag)
        {
            case TrayScreenshot:
                StarMark.UI.Services.ScreenshotService.Start(StarMark.UI.Services.CaptureMode.Toolbar);
                break;
            case TrayScreenPin:
                StarMark.UI.Services.ScreenshotService.Start(StarMark.UI.Services.CaptureMode.Pin);
                break;
            case TrayScreenOcr:
                StarMark.UI.Services.ScreenshotService.Start(StarMark.UI.Services.CaptureMode.Ocr);
                break;
            case TrayCanvas:
                StarMark.UI.Services.CanvasService.Toggle();
                break;
            case TrayPinsShowHide:
                StarMark.UI.Services.PinManager.ToggleHidden();
                break;
            case TrayPinsClickThrough:
                StarMark.UI.Services.PinManager.ToggleClickThrough();
                break;
            case TrayPinsCloseAll:
                StarMark.UI.Services.PinManager.CloseAll();
                break;
            case TrayTopmostToggle:
                _ = _widgetManager.ToggleAllTopmostAsync();
                break;
            case TrayThemeDefault:
            case TrayThemeLight:
            case TrayThemeDark:
                ApplyThemePreference(tag switch
                {
                    TrayThemeLight => ThemePreference.Light,
                    TrayThemeDark => ThemePreference.Dark,
                    _ => ThemePreference.Default,
                });
                break;
            case TrayPerfBalanced:
            case TrayPerfSaver:
                var mode = tag == TrayPerfSaver ? StarMark.Core.Performance.PerformanceMode.ResourceSaver : StarMark.Core.Performance.PerformanceMode.Balanced;
                // 只需要写盘：性能模式的消费方（内存回收、监控采样间隔…）每次都经
                // PerformanceSettingsPolicy 现读，所以不需要"再通知一遍"，也就不会漏通知。
                _settings.SavePerformanceMode(mode);
                break;
            case TrayAutostart:
                Autostart().SetEnabled(!Autostart().IsEnabled());
                break;
            case TrayHotkeysEnabled:
                var enable = !_settings.LoadEnableGlobalHotKey();
                _settings.SaveEnableGlobalHotKey(enable);
                ApplyTraySettings();
                break;
        }
    }

    private void DisposeTray()
    {
        _trayHost?.Dispose();
        _trayHost = null;
    }

    /// <summary>设置页保存后调用：托盘与全局热键开关即时生效，无需重启。</summary>
    public void ApplyTraySettings()
    {
        var trayEnabled = _settings.LoadEnableTray();
        var hotkeyEnabled = _settings.LoadEnableGlobalHotKey();
        if (trayEnabled) CreateTray();
        else DisposeTray();

        // 热键的注册 / 注销只归 HotkeyService。此前这里调的是 TrayHost.RegisterGlobalHotKey()——
        // 一个只打一行日志的空壳（热键早就迁到 MainWindow 句柄上注册），所以「启用全局快捷键」
        // 拨完当场不生效，要等重启或下一次点「保存快捷键」。
        if (App.Services.GetRequiredService<HotkeyService>() is { } hotkey)
            hotkey.ApplyBindings(hotkeyEnabled
                ? _settings.GetRegisterableHotkeyBindings()
                : new Dictionary<string, HotkeyGesture>());
    }

    private async void ExitApp()
    {
        _allowExit = true;
        StarLog.Info("退出：开始收尾（停同步 → 关组件 → 停采集 → 摘托盘 → 结束进程）");
        // 硬退出保险：收尾里任何一步卡住（22 个组件窗口的关闭，只要有一个不返回就够）都会让进程留在
        // "托盘图标已经没了、StarMark.UI.exe 还在锁着 dll" 这个状态。这条后台线程不参与任何业务，
        // 正常路径会自己走到最后那句 Environment.Exit，它只在 5 秒后替用户把话说完。
        new Thread(() => { Thread.Sleep(5000); Environment.Exit(0); }) { IsBackground = true }.Start();
        _dataSync?.Dispose();
        try { await _widgetManager.ShutdownAllAsync(); }
        catch (Exception ex) { StarLog.Error("关闭桌面组件失败", ex); }
        try { App.ApplyClipboardHistory(false); }
        catch (Exception ex) { StarLog.Error("停止剪贴板采集失败", ex); }
        DisposeTray();
        WidgetAppearance.ReleaseBackdrop(this);   // 释放主窗口的材质控制器（原生合成资源）
        try { Application.Current.Exit(); }
        catch (Exception ex) { StarLog.Error("Application.Exit 失败（不影响结束进程）", ex); }
        // WinUI 3 的 Application.Exit 只把应用从 UI 上摘下来，**不带下线进程**。真机日志里
        // "有会话开始、无进程退出"正好对上"托盘图标早已不见、进程还在锁 dll"（dotnet run 报 MSB3027）。
        // 退出这条路必须自己结束进程，不能指望框架替我们收。
        Environment.Exit(0);
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

    /// <summary>唤起并前置主窗口；settings=true 时直接打开设置页，可指定要落在哪个页签。</summary>
    public void Present(bool settings, string? settingsTab = null)
    {
        try
        {
            if (!AppWindow.IsVisible) AppWindow.Show();
            Activate();
            WindowInterop.ShowWindow(MainHwnd, WindowInterop.SW_RESTORE);
            WindowInterop.SetForegroundWindow(MainHwnd);
            if (settings) OpenSettingsPage(settingsTab);
        }
        catch (Exception ex) { StarLog.Error("唤起主窗口失败", ex); }
    }

    /// <summary>
    /// 打开设置页。<paramref name="tab"/> 是页签标题（如「健康与诊断」）：从组件右键跳过来的人
    /// 要的是那一栏，落在默认页等于让他到了门口再自己找房间。
    /// </summary>
    private void OpenSettingsPage(string? tab = null)
    {
        ViewModel.CurrentPageTag = "settings";
        NavView.SelectedItem = null;
        SetNavVisible(false);
        if (ContentFrame.Content is not SettingsPage)
            ContentFrame.Navigate(typeof(SettingsPage));
        if (ContentFrame.Content is SettingsPage page) page.SelectTab(tab);
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
        // 悬停/按下高亮由 ApplyTitleBarButtonColors 按主题设置；之前被设为全透明 → 最小化/窗口化/关闭
        // 按钮悬停无任何反馈。这里不再写死透明，改在 ApplyTitleBarButtonColors 内给出主题化中性色。
        ApplyTitleBarButtonColors();

        ApplyDragRects();
        SizeChanged += (_, _) => ApplyDragRects();
    }

    /// <summary>
    /// 系统标题栏按钮（最小化 / 窗口化 / 关闭）的悬停与按下高亮。
    /// 之前三个按钮的 Hover/Pressed 背景被写死为 <see cref="Colors.Transparent"/>，导致悬停无反馈；
    /// 这里按当前主题给出低透明度中性色（浅色压暗、深色提亮），让悬停/按下有可见高亮，对齐 WinUI 原生标题栏。
    /// 主题切换时调用，保证深浅色下都正确。
    /// </summary>
    private void ApplyTitleBarButtonColors()
    {
        try
        {
            var titleBar = AppWindow.TitleBar;
            var isDark = TargetTheme(_themePref) == ElementTheme.Dark;
            titleBar.ButtonHoverBackgroundColor = isDark
                ? Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)   // 深色：约 20% 白
                : Windows.UI.Color.FromArgb(0x1F, 0x00, 0x00, 0x00);   // 浅色：约 12% 黑
            titleBar.ButtonPressedBackgroundColor = isDark
                ? Windows.UI.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)   // 深色：约 33% 白
                : Windows.UI.Color.FromArgb(0x33, 0x00, 0x00, 0x00);   // 浅色：约 20% 黑
        }
        catch { }
    }

    private void ApplyDragRects()
    {
        // 拖拽区 = 顶栏中段（logo + 状态），右侧留给交互按钮与系统窗口按钮。
        // AppWindow.Size 与 SetDragRectangles 均以物理像素为单位，而 ActualHeight/ActualWidth 是 DIP；
        // 非 100% DPI 下不换算会让拖拽区高度不足、右边界压到按钮（低分屏拖不动、高分屏误触按钮）。
        var scale = WindowInterop.GetScale(this);
        var width = AppWindow.Size.Width;
        var heightDip = TopBar.ActualHeight > 0 ? TopBar.ActualHeight : 52;
        var height = (int)Math.Round(heightDip * scale);
        var buttonsRightPad = (int)Math.Round(150 * scale);   // 顶栏右侧 padding（避开系统窗口按钮）
        var buttonsWidthDip = TopBarButtons.ActualWidth > 0 ? TopBarButtons.ActualWidth : 150;
        var gap = (int)Math.Round(8 * scale);
        var dragW = Math.Max(0, width - buttonsRightPad - (int)Math.Round(buttonsWidthDip * scale) - gap);
        AppWindow.TitleBar.SetDragRectangles(new RectInt32[]
        {
            new() { X = 0, Y = 0, Width = dragW, Height = height },
        });
    }

    // ===== 主题切换 =====

    /// <summary>主窗口右上角快捷切换主题后触发（参数为新主题偏好）。设置页据此把自身的主题选择
    /// 同步过来，否则离开设置页时其兜底保存会用过期的 ThemeIndex 把主题强制切回旧值。</summary>
    public static event Action<ThemePreference>? ThemePreferenceQuickSwitched;

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _themePref = _themePref switch
        {
            ThemePreference.Default => ThemePreference.Light,
            ThemePreference.Light => ThemePreference.Dark,
            _ => ThemePreference.Default,
        };
        ApplyThemePreference(_themePref);
    }

    /// <summary>
    /// 套用一个主题偏好并把它传导到所有该知道的地方：主窗、标题栏按钮、组件、设置页。
    /// <b>标题栏一键循环与托盘直接指定都走这里</b>——这条链有 6 步，写两遍迟早有一遍漏掉某一步
    /// （漏掉"组件主题"就是当初"切主题后组件不跟"那一类缺陷的形状）。
    /// </summary>
    private void ApplyThemePreference(ThemePreference pref)
    {
        _themePref = pref;
        _settings.SaveTheme(pref);
        ThemeManager.Apply(this, pref);
        UpdateThemeIcon();
        RefreshAppearance();   // 主题画笔按窗口实际主题重新解析，否则一键切换后主界面背景色不跟随
        ApplyTitleBarButtonColors();                 // 标题栏按钮高亮随主题
        _ = _widgetManager.ApplyThemeToAllAsync(pref); // 同步组件主题（组件是独立窗口，不会自动传导）
        // 通知设置页同步主题选择（设置页已打开时尤其关键）
        ThemePreferenceQuickSwitched?.Invoke(pref);
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
        RefreshAppearance();   // 设置页切换主题后同步刷新主界面背景
        ApplyTitleBarButtonColors();                 // 标题栏按钮高亮随主题
        _ = _widgetManager.ApplyThemeToAllAsync(pref); // 同步组件主题（组件是独立窗口，不会自动传导）
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
        // 编程式导航不触发 SelectionChanged（上面把 SelectedItem 置 null），故在此同步当前页标记；
        // 否则 FolderTreePageViewModel/SearchPageViewModel 的「仅当前页刷新」闸停在旧值，深链进入的页不再响应数据变更。
        ViewModel.CurrentPageTag = tag;
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
            "clipboard" => typeof(ClipboardPage),
            "trending" => typeof(TrendingPage),
            "rss" => typeof(RssPage),
            "snapshot" => typeof(SnapshotPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(SearchPage),
        };
        ContentFrame.Navigate(pageType, param);
    }

    /// <summary>
    /// 按设置开关导航栏的「热榜」项（<b>即时</b>，不等重启）。
    /// <para>关掉的那一刻若正停在这一页，必须退回文件夹页：入口已经没了、屏幕上却还留着它的内容，
    /// 是"程序坏了"的典型观感（P-54 那条口径的另一面——状态变了界面就得跟着变）。</para>
    /// </summary>
    public void ApplyTrendingNavVisibility(bool enabled)
    {
        NavTrendingItem.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled && ViewModel.CurrentPageTag == "trending") NavigateTo("tree");
    }

    /// <summary>
    /// 按 RSS 总开关即时收放导航栏的「RSS」项（与 <see cref="ApplyTrendingNavVisibility"/> 同一形状：
    /// 关掉的那一刻若正停在这一页就退回文件夹页——入口已经没了、屏幕上却还留着它的内容，是"程序坏了"的典型观感）。
    /// </summary>
    public void ApplyRssNavVisibility(bool enabled)
    {
        NavRssItem.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled && ViewModel.CurrentPageTag == "rss") NavigateTo("tree");
    }

    // ===== 搜索框防抖 =====

    private void SearchBox_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
    {
        // 单实例：本方法按击键频率触发，续期只需 Stop/Start，不必每键 new 一个计时器再另挂委托。
        var timer = _debounceTimer ??= BuildDebounceTimer();
        timer.Stop();
        timer.Start();
    }

    private DispatcherTimer BuildDebounceTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (s, args2) =>
        {
            timer.Stop();
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
        return timer;
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
                else ItemCardActions.Open(SearchBox.XamlRoot, selected);   // 传视图模型：Everything 文件结果是 Id=0 虚拟条目，按 Id 查库会静默失效
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
        // 语言是 Star 的维度：来源切到本地文件时它无从生效，直接禁用而不是"选了没反应"。
        LanguageCombo.IsEnabled = tag != "file";
        PushToolbarToContent();
    }

    // ───────── 「类型」多选（本地文件；翻译成 Everything 检索式，不进搜索框）─────────

    private bool _suppressFileKindEvents;

    private void FileKind_Changed(object sender, RoutedEventArgs e) => ApplyFileKinds();

    private void FileKind_Clear_Click(object sender, RoutedEventArgs e)
    {
        // 逐个取消会各触发一次 Unchecked；不抑制就会为"清除"这一动作重跑 N 次搜索。
        _suppressFileKindEvents = true;
        try
        {
            foreach (var box in FileKindBoxes()) box.IsChecked = false;
        }
        finally
        {
            _suppressFileKindEvents = false;
        }
        ApplyFileKinds();
    }

    private IEnumerable<CheckBox> FileKindBoxes()
        => FileKindPanel is { } a && FileCondPanel is { } b
            ? a.Children.OfType<CheckBox>().Concat(b.Children.OfType<CheckBox>())
            : Enumerable.Empty<CheckBox>();

    /// <summary>当前勾选的类型（面板 Tag 存枚举名）。面板尚未创建时视为无勾选。</summary>
    private IReadOnlyList<FileKind> CurrentFileKinds()
    {
        var kinds = new List<FileKind>(8);
        foreach (var box in FileKindBoxes())
            if (box.IsChecked == true && box.Tag is string tag
                && Enum.TryParse<FileKind>(tag, out var kind)) kinds.Add(kind);
        return kinds;
    }

    private void ApplyFileKinds()
    {
        if (_suppressFileKindEvents || FileKindButton == null) return;
        var kinds = CurrentFileKinds();
        FileKindButton.Content = kinds.Count == 0 ? "类型筛选" : $"类型筛选 · {kinds.Count}";
        // 只下发给搜索页：书签/Star 没有扩展名与体积概念，类型筛选对它们无意义。
        if (ContentFrame?.Content is SearchPage sp) sp.ViewModel.SetFileKinds(kinds);
    }

    private void ShowHidden_Click(object sender, RoutedEventArgs e)
    {
        // 复选框只是当前页过滤器；隐藏页由导航菜单“隐藏”进入
        PushToolbarToContent();
    }

    /// <summary>状态点语义色（caution=进行中/失败，success=成功）；主题切换时按此重解析。</summary>
    private StatusKind _statusKind = StatusKind.Caution;

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

    private string CurrentSourceTag() => _currentSource;

    // ───────── 语言筛选（常态显示于工具栏；选项由搜索页结果聚合）─────────

    private const string LanguageAllItem = "语言：全部";
    private bool _syncingLanguageCombo;
    private bool _languageHooked;

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLanguageCombo) return;
        var selected = LanguageCombo.SelectedItem as string;
        var lang = (selected is null || selected == LanguageAllItem) ? string.Empty : selected;
        // 单一事实来源：主界面语言下拉同时驱动浏览（文件夹/star）与搜索两套过滤
        ViewModel.CurrentLanguage = lang;
        if (ContentFrame?.Content is SearchPage sp) sp.ViewModel.CurrentLanguage = lang;
    }

    private void HookLanguageOptions(SearchPageViewModel vm)
    {
        if (_languageHooked) return;
        _languageHooked = true;
        // SearchPageViewModel 是单例：选项集合变化（每次搜索后重建）时同步下拉框
        vm.AvailableLanguages.CollectionChanged += (_, _) => SyncLanguageCombo(vm);
    }

    /// <summary>
    /// 把语言选项同步到工具栏下拉框：目录（始终直选）+ 搜索页聚合到的语言去重合并；
    /// 当前选中以 <see cref="MainViewModel.CurrentLanguage"/> 为准（null/空 = 「语言：全部」）。
    /// </summary>
    private void SyncLanguageCombo(SearchPageViewModel? vm)
    {
        _syncingLanguageCombo = true;
        try
        {
            // 只取 star 条目中真实存在的语言（由 LoadStarLanguagesAsync 从库里聚合），
            // 不再使用 LanguageCatalog.AllNames —— 避免出现当前 star 项目不存在的语言选项。
            var set = new HashSet<string>(_starLanguages, StringComparer.OrdinalIgnoreCase);
            // 搜索结果里实际出现的语言一并合并（同样是真实存在的语言）
            if (vm is not null) foreach (var l in vm.AvailableLanguages) set.Add(l);
            var items = new List<string> { LanguageAllItem };
            items.AddRange(set);
            LanguageCombo.ItemsSource = items;
            var current = ViewModel.CurrentLanguage;
            LanguageCombo.SelectedIndex = string.IsNullOrEmpty(current)
                ? 0
                : Math.Max(0, items.IndexOf(current));
        }
        finally { _syncingLanguageCombo = false; }
    }

    /// <summary>
    /// 从库里聚合 star 条目中<b>真实存在</b>的编程语言，作为语言下拉的数据源。
    /// 保证下拉里每一项都至少对应一个 star 项目，不会出现未被任何 star 使用的语言。
    /// </summary>
    private async Task LoadStarLanguagesAsync()
    {
        try
        {
            var repo = App.Services.GetRequiredService<IItemRepository>();
            if (repo is null) return;
            var langs = await repo.GetStarLanguagesAsync();
            _starLanguages.Clear();
            _starLanguages.AddRange(langs);
            SyncLanguageCombo(ContentFrame?.Content as SearchPageViewModel);
        }
        catch (Exception ex)
        {
            StarLog.Error("加载 star 语言列表失败", ex);
        }
    }

    private string CurrentSortTag()
        => SortCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : "recent";

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
    /// 中性提示（不是错误也不是"操作完成"）：托盘气泡发不出去时的兜底出口。
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