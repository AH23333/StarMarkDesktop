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
    private bool _backgroundNoticeShown;
    private readonly WidgetManager _widgetManager;
    private MenuFlyout? _widgetsMenu;
    private int _diagSimStep;
    // star 项目中真实存在的编程语言（下拉数据源，见 LoadStarLanguagesAsync）
    private readonly List<string> _starLanguages = new();
    // 主界面的数据变更同步器：此前全应用只有组件 VM 订阅 DataChangeHub，主窗口一个都没有 ——
    // 组件里改一条随记/待办、或备份还原落库后，主界面的侧栏计数与当前列表页
    // （文件夹 / 标签 / 已隐藏 / 活动）全停在旧数据，要切页或重启才更新。这里补上订阅。
    private DataChangeReloader? _dataSync;

    // 提醒出口不住在这里：批次 RV 起统一走 <see cref="NoticeCard"/>（右下角那张提示卡）。
    // 这里原先替托盘转了一道手，最终落到系统那句"改图标附通知"的调用上；而那一发在 Windows 11 上
    // <b>API 返回 TRUE、屏幕上什么都没有</b>：调用方把它读成"已经提醒过了"，于是主窗提示条与"只留日志"
    // 两条兜底按构造永不触发——真机上"所有声称气泡效果的，均无提示效果"就是这么来的。
    // 判据现在只能落在窗口的实际矩形上；而且主窗收进托盘时也该能提醒，所以这道转手整个删掉了。

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

        // 启动时恢复已启用的桌面组件（「开机自动加载组件」关掉时一颗都不建，见 WidgetManager.RestoreOnStartupAsync）
        DispatcherQueue.TryEnqueue(async () =>
        {
            try { await _widgetManager.RestoreOnStartupAsync(_settings.LoadWidgetsLoadOnStartup()); }
            catch (Exception ex) { StarLog.Error("恢复桌面组件失败", ex); }
            // WE-2：恢复**之后**那一段以前没有刻度——日志里最后一个分段停在"桌面组件恢复"（它在恢复结束时才记），
            // 而卡顿看门狗报的那 1.5 s 正好落在它后面，于是谁也说不清是谁占着 UI 线程。补上右半边刻度。
            StarMark.Abstractions.StartupProfile.Mark("组件恢复任务返回（UI 队列）");
            // P-134 选项 (A)：上面那把刻度**之后**到组件真出现在屏幕上之间，进程内仍是一条空白——
            // 真机外部采样在同一 PID 上 1.5 秒后读到私有 610 MB，而最后一条 Mark 还停在 99 MB，
            // 于是"创建期那 400 MB 花在哪一刻"归不了因（没刻度就只能猜，见坑表 #160）。
            // 下面两把把这段夹住：① UI 线程被再次调度（＝这一批工作交回消息泵）；② 合成器给出第一帧
            // （＝DWM 合成面建立，材质与重定向表面最可能在这里付钱）。两把的先后本身就是读数。
            // ⚠ 只许往后挂：插进创建循环就等于同时改掉 P-133 要量的那个东西（两批不许混）。
            MarkUiHandback();
            MarkFirstComposedFrame();
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

    /// <summary>
    /// 点亮后的第一把刻度：UI 线程把这批工作交回消息泵、又被再次调度的那一刻（<b>Low</b> 档，不插队、只排一次）。
    /// <para>
    /// 为什么用它而不是"等 UI 空闲"：<c>DispatcherQueuePriority</c> 只有 Low/Normal/High 三档，
    /// <b>没有 Idle</b>——写"空闲"就是在报一个这把尺子给不出的数。Low 档说明的是"后面的活儿都排在我后头"，
    /// 至于界面静下来没有，由第二把刻度回答。
    /// </para>
    /// </summary>
    private void MarkUiHandback()
        => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => StarMark.Abstractions.StartupProfile.Mark("组件点亮后 UI 让出（Low 档）"));

    /// <summary>
    /// 点亮后的第二把刻度：合成器给出<b>第一帧</b>。回调里<b>先退订再打刻度</b>——
    /// 留着就是每帧写两行日志，把量具本身变成噪声源（正是 P-134 拒掉选项 (B) 的那个理由）。
    /// <para>
    /// 界面彻底静止后可能不再出帧，所以这一把<b>可能不来</b>；来了与没来都是信息，
    /// 也因此它必须和第一把刻度是<b>两条独立的 Mark</b>——合并成一条就会变成"刻度看着有，其实一条都没有"。
    /// </para>
    /// </summary>
    private void MarkFirstComposedFrame()
    {
        void OnRendering(object? sender, object args)
        {
            CompositionTarget.Rendering -= OnRendering;
            StarMark.Abstractions.StartupProfile.Mark("组件首个组合帧提交");
        }
        CompositionTarget.Rendering += OnRendering;
    }

    private IntPtr MainHwnd => WindowNative.GetWindowHandle(this);

    /// <summary>
    /// 「立即更新」交棒之后的退出。<b>必须先把单实例锁让出去，再走同一条收尾</b>：
    /// 不让锁的症状是更新器起的新一版去抢锁失败、转而去唤起这个马上要消失的旧窗口，
    /// 于是两个进程都没了——用户看到的就是"更新完程序打不开了"（见 <see cref="App.ReleaseSingleInstanceForHandoff"/>）。
    /// </summary>
    public void ExitForUpdateHandoff()
    {
        StarLog.Info("退出：更新已交给更新器，让出单实例锁后收尾");
        App.ReleaseSingleInstanceForHandoff();
        ExitApp();
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
                if (!_backgroundNoticeShown && NoticeCard.Show("StarMark 正在后台运行", "Ctrl+Alt+Space 随时呼出窗口"))
                {
                    // 只有真的贴上了屏幕才算"说过这一次"：上一版按"气泡返回 true"记账，于是那条提醒
                    // 一辈子只发一次、而用户一次都没看见——记错了账就把唯一的补发机会也烧掉了。
                    _backgroundNoticeShown = true;
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
}