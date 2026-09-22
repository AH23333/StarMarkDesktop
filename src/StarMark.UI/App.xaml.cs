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
        // 全局未处理异常：写日志并置 Handled=true 兜住，绝不让单次 UI 异常杀掉整个进程。
        // 关键背景：WinUI 3 中若这里不设 e.Handled，任何从点击处理里冒出的未处理异常
        // （例如组件落盘时 WidgetStorage.Save 撞上 OneDrive/杀软文件锁抛 IOException，
        //  该调用经 OnUiAsync 在 UI 线程同步内联执行，直接逃出点击处理）都会让应用崩溃。
        // 组件/主界面是常驻桌面工具，一次操作失败应降级为"这条没存上"而非整个应用闪退。
        // 注：真正的原生 AccessViolation（踩坑 #60 那类）非托管异常，本兜不住，靠调用点自身规避。
        UnhandledException += (_, e) =>
        {
            StarLog.Error($"UI 未处理异常（已拦截，进程继续）: {e.Exception}");
            e.Handled = true;
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
        // SettingsStore 必须作为单例登记：WidgetWindow.Reveal 等处通过 App.Services.GetRequiredService<SettingsStore>()
        // 取用它。此前只 new 了本地实例却未注册，导致每次显示组件都在 Reveal 早期抛
        // InvalidOperationException（No service for type ... SettingsStore），被 UI 线程未处理异常安全网吞掉后
        // 组件既不显示也不套主题/材质——表现为"材质不切换、数据不同步、四种材质看不出区别"。
        services.AddSingleton(fileSettings);
        services.AddSingleton(new StarMark.Integrations.Everything.FileIndexOptions
        {
            Roots = fileSettings.LoadFileIndexRoots().ToList(),
            MaxCount = fileSettings.LoadMaxFileIndexCount(),
            Enabled = fileSettings.LoadLocalDiskSearchEnabled(),
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
        try { PerformanceSettingsPolicy.Provider = fileSettings; }
        catch (Exception pex) { StarLog.Error("性能模式设置来源注入失败", pex); }

        // 本地磁盘搜索「A 方案：与提权 Everything 同权限」——若已开启且当前非提权，以管理员重启一次
        // （带 --elevate-retry 标记防死循环；用户取消 UAC 则继续普通运行，仅本地文件搜索用不了，其余不受影响）。
        // 放在 DI 建好之后、拉起 Everything 之前，让提权实例去走下面正常的 SDK/IPC 就绪流程。
        if (fileSettings.LoadLocalDiskSearchEnabled() && !Privilege.IsElevated())
        {
            var alreadyRetried = false;
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "--elevate-retry", StringComparison.OrdinalIgnoreCase)) { alreadyRetried = true; break; }
            if (!alreadyRetried && Privilege.TryRelaunchSelfElevated("--elevate-retry"))
                Environment.Exit(0);
        }

        // Everything 就绪流程（下载 SDK / 主程序未运行时自动安装）——仅在用户开启「本地磁盘搜索」后执行。
        // 默认关时绝不在此下载/安装/拉起 Everything，满足"轻度用户零打扰、默认零内存"。
        if (fileSettings.LoadLocalDiskSearchEnabled())
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Services.GetRequiredService<StarMark.Integrations.Everything.EverythingSource>()
                        .EnsureReadyAsync();
                }
                catch { /* 内部已兜底 */ }
            });
        }

        try
        {
            // 应用级主题（必须在首个窗口创建前设置）
            ThemeManager.ApplyAppLevelTheme(fileSettings.LoadTheme());

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
                var settings = fileSettings;

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
