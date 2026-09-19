#nullable enable
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Core.Backup;
using StarMark.Core.Widgets;
using StarMark.Core.Performance;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI;

/// <summary>
/// WinUI 3 应用程序入口。DI 容器 + 数据库初始化 + 单实例 + 桌面组件。
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static MainWindow? MainWindow { get; private set; }

    private Window? _window;

    /// <summary>单实例互斥体（进程生命周期内保持引用，防止被 GC 释放）。</summary>
    private static Mutex? _singleInstanceMutex;
    private const string SingleInstanceMutexName = "Local\\StarMark.Desktop.SingleInstance";

    public App()
    {
        InitializeComponent();
        // 全局未处理异常：写入 StarLog 便于诊断
        UnhandledException += (_, e) =>
        {
            StarLog.Error($"UI 未处理异常: {e.Exception}");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            StarLog.Error($"非 UI 线程未处理异常: {e.ExceptionObject}");
        };
    }

    /// <summary>
    /// 启动期异常的兜底弹窗：避免在 OnLaunched 抛错时进程静默退出、用户看到"双击无反应"。
    /// 用 Win32 MessageBox（不依赖任何 XAML 窗口，启动早期即可用）。
    /// </summary>
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private static void ShowStartupError(Exception ex)
    {
        try { StarLog.Error("启动失败", ex); } catch { }
        try
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                StarMark.Abstractions.AppConstants.AppName, "logs");
            MessageBoxW(IntPtr.Zero,
                $"StarMark 启动失败：\n\n{ex.GetType().Name}: {ex.Message}\n\n详细日志见：\n{logPath}",
                "StarMark 启动错误", 0x10 /* MB_ICONERROR */);
        }
        catch { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 单实例：再次启动时唤起已有主窗口并退出新进程
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName,
            createdNew: out var createdNew);
        if (!createdNew)
        {
            TryActivateExistingInstance();
            Environment.Exit(0);
            return;
        }

        // 1. 构建 DI 容器
        var services = new ServiceCollection();

        // 数据访问层
        var dbFactory = new StarMark.Data.DbConnectionFactory();
        services.AddSingleton(dbFactory);
        services.AddSingleton(sp => new StarMark.Data.MigrationRunner(sp.GetRequiredService<StarMark.Data.DbConnectionFactory>()));
        services.AddSingleton<IItemRepository, StarMark.Data.ItemRepository>();

        // 备份与恢复（P0-2）：不可重建的用户元数据需要可导出/回滚
        services.AddSingleton<IBackupRepository, StarMark.Data.BackupRepository>();
        services.AddSingleton<BackupService>();

        // 集成适配层
        // P0-1b：本地文件索引配置从设置读取后注入 EverythingSource（避免 Integrations 反向依赖 UI）。
        var fileSettings = new StarMark.UI.Helpers.SettingsStore();
        services.AddSingleton(new StarMark.Integrations.Everything.FileIndexOptions
        {
            Roots = fileSettings.LoadFileIndexRoots().ToList(),
            MaxCount = fileSettings.LoadMaxFileIndexCount(),
        });
        services.AddSingleton<StarMark.Integrations.Everything.EverythingQueryQueue>();
        services.AddSingleton<StarMark.Integrations.Everything.EverythingSource>(sp =>
            new StarMark.Integrations.Everything.EverythingSource(
                sp.GetRequiredService<StarMark.Integrations.Everything.EverythingQueryQueue>(),
                sp.GetRequiredService<StarMark.Integrations.Everything.FileIndexOptions>()));
        services.AddSingleton<IItemSource>(sp => sp.GetRequiredService<StarMark.Integrations.Everything.EverythingSource>());

        services.AddSingleton(sp =>
            StarMark.Integrations.GitHub.GitHubOptions.Load().WithEnvironmentOverrides());
        services.AddSingleton<StarMark.Integrations.GitHub.GitHubClient>();
        services.AddSingleton<StarMark.Integrations.GitHub.GitHubSource>();
        services.AddSingleton<IItemSource>(sp => sp.GetRequiredService<StarMark.Integrations.GitHub.GitHubSource>());

        // 浏览器书签源（Chrome / Edge）
        services.AddSingleton<StarMark.Integrations.Bookmarks.ChromeBookmarksSource>();
        services.AddSingleton<IItemSource>(sp => sp.GetRequiredService<StarMark.Integrations.Bookmarks.ChromeBookmarksSource>());
        services.AddSingleton<StarMark.Integrations.Bookmarks.EdgeBookmarksSource>();
        services.AddSingleton<IItemSource>(sp => sp.GetRequiredService<StarMark.Integrations.Bookmarks.EdgeBookmarksSource>());

        // Ditto 剪贴板源（直读 DittoDB.db）
        services.AddSingleton<StarMark.Integrations.Ditto.DittoSource>();
        services.AddSingleton<IItemSource>(sp => sp.GetRequiredService<StarMark.Integrations.Ditto.DittoSource>());

        // 应用服务层
        services.AddSingleton<StarMark.Core.Search.SearchService>();
        services.AddSingleton<StarMark.Core.Sync.SyncCoordinator>();
        services.AddSingleton<StarMark.Core.Diagnostics.DiagnosticsService>();

        // 桌面组件（DeskBox 式独立小组件）
        services.AddSingleton<WidgetStorage>();
        services.AddSingleton<WidgetManager>();
        // 内存门禁（常驻进程按性能模式预算回收）。MemoryReclaimer 设计为静态单例（Default），
        // 构造器为 private，不能由 DI 直接 new；注册其单例实例，GetRequiredService 才能取到并 Start。
        services.AddSingleton(MemoryReclaimer.Default);

        // 全局快捷键（动作映射 + 冲突允许；见 HotkeyService）
        services.AddSingleton<HotkeyService>();
        services.AddSingleton<AutostartService>();

        // ViewModel 层
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SearchPageViewModel>();
        services.AddSingleton<FolderTreePageViewModel>();
        services.AddSingleton<TagsPageViewModel>();
        services.AddTransient<ActivityPageViewModel>();
        services.AddTransient<HiddenPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();

        Services = services.BuildServiceProvider();

        // 性能模式设置来源注入（MemoryReclaimer 经 PerformanceSettingsPolicy 读取预算）
        try { PerformanceSettingsPolicy.Provider = new SettingsStore(); }
        catch (Exception pex) { StarLog.Error("性能模式设置来源注入失败", pex); }

        // Everything 就绪流程：SDK DLL 缺失自动下载；主程序未运行时自动安装（用户要求默认安装；失败静默降级）
        _ = Task.Run(async () =>
        {
            try
            {
                await Services.GetRequiredService<StarMark.Integrations.Everything.EverythingSource>()
                    .EnsureReadyAsync();
            }
            catch { /* 内部已兜底 */ }
        });

        try
        {
            // 应用级主题（必须在首个窗口创建前设置）
            ThemeManager.ApplyAppLevelTheme(new SettingsStore().LoadTheme());

            // 2. 执行数据库迁移
            Services.GetRequiredService<StarMark.Data.MigrationRunner>().EnsureSchema();

            // 2.1 种子数据
            try
            {
                var repo = Services.GetRequiredService<IItemRepository>();
                Task.Run(() => SeedData.SeedIfEmptyAsync(repo, CancellationToken.None)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                StarLog.Error("种子数据失败", ex);
            }

            // 2.2 本地条目（待办/随记）迁移进统一 items 表（幂等，一次）
            try
            {
                var repo = Services.GetRequiredService<IItemRepository>();
                var migration = new LocalItemsMigration(new WidgetStorage(), repo);
                Task.Run(() => migration.MigrateAsync(CancellationToken.None)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                StarLog.Error("本地条目迁移失败", ex);
            }

            // 3. 显示主窗口
            _window = new MainWindow();
            MainWindow = _window as StarMark.UI.MainWindow;
            _window.Activate();

            // 4. 全局快捷键：在 MainWindow 句柄上子类化接收 WM_HOTKEY，绑定动作并应用设置
            try
            {
                var hotkey = Services.GetRequiredService<HotkeyService>();
                var widgetManager = Services.GetRequiredService<WidgetManager>();
                var settings = new SettingsStore();

                hotkey.RegisterHandler(HotkeyActions.MainToggle, () => { ToggleMainWindow(); return Task.CompletedTask; });
                hotkey.RegisterHandler(HotkeyActions.MainShow, () => { PresentMainWindow(); return Task.CompletedTask; });
                hotkey.RegisterHandler(HotkeyActions.MainHide, () => { HideMainWindow(); return Task.CompletedTask; });

                hotkey.RegisterHandler(HotkeyActions.WidgetsShowAll, () => widgetManager.ShowAllAsync());
                hotkey.RegisterHandler(HotkeyActions.WidgetsHideAll, () => widgetManager.HideAllAsync());
                hotkey.RegisterHandler(HotkeyActions.WidgetsToggleAll, () => widgetManager.ToggleAllInstancesAsync());
                foreach (var k in WidgetStorage.AllKinds)
                {
                    var kind = k;
                    hotkey.RegisterHandler(HotkeyActions.WidgetCreate(kind), () => widgetManager.AddInstanceAsync(kind));
                    hotkey.RegisterHandler(HotkeyActions.WidgetShow(kind), () => widgetManager.ShowKindAsync(kind));
                    hotkey.RegisterHandler(HotkeyActions.WidgetHide(kind), () => widgetManager.HideKindAsync(kind));
                    hotkey.RegisterHandler(HotkeyActions.WidgetToggle(kind), () => widgetManager.ToggleKindAsync(kind));
                }

                // 布局切换动作是动态的（用户随时新增/删除布局），用兜底处理器按需分派，
                // 免去每次布局变化都重新注册一圈 handler。
                hotkey.FallbackHandler = action =>
                {
                    if (!HotkeyActions.IsLayoutAction(action)) return Task.CompletedTask;
                    var id = HotkeyActions.LayoutIdOf(action);
                    return id is null ? Task.CompletedTask : widgetManager.ApplyLayoutAsync(id);
                };

                hotkey.Initialize(WindowInterop.GetHwnd(_window));
                var bindings = settings.LoadEnableGlobalHotKey()
                    ? settings.GetHotkeyBindings()
                    : new Dictionary<string, HotkeyGesture>();
                hotkey.ApplyBindings(bindings);

                // 内存门禁：常驻进程按性能模式预算回收（best-effort，失败不影响启动）
                try { Services.GetRequiredService<MemoryReclaimer>().Start(); }
                catch (Exception rex) { StarLog.Error("内存门禁启动失败", rex); }
            }
            catch (Exception ex)
            {
                StarLog.Error("全局快捷键初始化失败", ex);
            }
        }
        catch (Exception ex)
        {
            // 启动期异常若静默吞掉，用户会看到"双击无反应"。弹窗定位问题后退出。
            ShowStartupError(ex);
            Environment.Exit(1);
        }
    }

    /// <summary>唤起已有实例的主窗口（按窗口标题查找，组件窗口标题不同不会误匹配）。</summary>
    private static void TryActivateExistingInstance()
    {
        try
        {
            var hwnd = WindowInterop.FindWindowW(null, "StarMark");
            if (hwnd != IntPtr.Zero)
            {
                // 窗口可能处于隐藏（最小化到托盘）或最小化状态：先显示再还原
                WindowInterop.ShowWindow(hwnd, WindowInterop.SW_SHOW);
                WindowInterop.ShowWindow(hwnd, WindowInterop.SW_RESTORE);
                WindowInterop.SetForegroundWindow(hwnd);
            }
        }
        catch { /* 找不到就算了，让第二实例退出即可 */ }
    }

    /// <summary>托盘/组件唤起主窗口的统一入口。</summary>
    public static void PresentMainWindow(bool settings = false)
    {
        MainWindow?.Present(settings);
    }

    /// <summary>隐藏主界面（快捷键动作用）：主进程继续驻留托盘。</summary>
    public static void HideMainWindow()
    {
        var win = MainWindow;
        if (win is null) return;
        if (win.AppWindow.IsVisible) win.AppWindow.Hide();
    }

    /// <summary>呼出/关闭主界面（快捷键动作用）：可见则隐藏到托盘，否则显示并前置。</summary>
    public static void ToggleMainWindow()
    {
        var win = MainWindow;
        if (win is null) return;
        if (win.AppWindow.IsVisible) win.AppWindow.Hide();
        else win.Present(false);
    }
}
