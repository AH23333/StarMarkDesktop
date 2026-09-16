#nullable enable
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
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

        // 集成适配层
        services.AddSingleton<StarMark.Integrations.Everything.EverythingQueryQueue>();
        services.AddSingleton<StarMark.Integrations.Everything.EverythingSource>();
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

        // 桌面组件（DeskBox 式独立小组件）
        services.AddSingleton<WidgetStorage>();
        services.AddSingleton<WidgetManager>();

        // ViewModel 层
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SearchPageViewModel>();
        services.AddSingleton<FolderTreePageViewModel>();
        services.AddTransient<TagsPageViewModel>();
        services.AddTransient<ActivityPageViewModel>();
        services.AddTransient<HiddenPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();

        Services = services.BuildServiceProvider();

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

        // 3. 显示主窗口
        _window = new MainWindow();
        MainWindow = _window as StarMark.UI.MainWindow;
        _window.Activate();
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
}
