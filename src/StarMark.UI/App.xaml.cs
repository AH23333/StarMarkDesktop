#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using StarMark.Abstractions;
using StarMark.UI.ViewModels;

namespace StarMark.UI;

/// <summary>
/// WinUI 3 应用程序入口。DI 容器 + 数据库初始化。
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static MainWindow? MainWindow { get; private set; }

    private Window? _window;

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
        // 1. 构建 DI 容器
        var services = new ServiceCollection();

        // 数据访问层
        var dbFactory = new StarMark.Data.DbConnectionFactory();
        services.AddSingleton(dbFactory);
        services.AddSingleton(sp => new StarMark.Data.MigrationRunner(sp.GetRequiredService<StarMark.Data.DbConnectionFactory>()));
        services.AddSingleton<StarMark.Abstractions.IItemRepository, StarMark.Data.ItemRepository>();

        // 集成适配层
        services.AddSingleton<StarMark.Integrations.Everything.EverythingQueryQueue>();
        services.AddSingleton<StarMark.Integrations.Everything.EverythingSource>();
        services.AddSingleton<StarMark.Abstractions.IItemSource>(sp => sp.GetRequiredService<StarMark.Integrations.Everything.EverythingSource>());

        services.AddSingleton(sp =>
            StarMark.Integrations.GitHub.GitHubOptions.Load().WithEnvironmentOverrides());
        services.AddSingleton<StarMark.Integrations.GitHub.GitHubClient>();
        services.AddSingleton<StarMark.Integrations.GitHub.GitHubSource>();
        services.AddSingleton<StarMark.Abstractions.IItemSource>(sp => sp.GetRequiredService<StarMark.Integrations.GitHub.GitHubSource>());

        // 浏览器书签源（Chrome / Edge）
        services.AddSingleton<StarMark.Integrations.Bookmarks.ChromeBookmarksSource>();
        services.AddSingleton<StarMark.Abstractions.IItemSource>(sp => sp.GetRequiredService<StarMark.Integrations.Bookmarks.ChromeBookmarksSource>());
        services.AddSingleton<StarMark.Integrations.Bookmarks.EdgeBookmarksSource>();
        services.AddSingleton<StarMark.Abstractions.IItemSource>(sp => sp.GetRequiredService<StarMark.Integrations.Bookmarks.EdgeBookmarksSource>());

        // Ditto 剪贴板源（直读 DittoDB.db）
        services.AddSingleton<StarMark.Integrations.Ditto.DittoSource>();
        services.AddSingleton<StarMark.Abstractions.IItemSource>(sp => sp.GetRequiredService<StarMark.Integrations.Ditto.DittoSource>());

        // 应用服务层
        services.AddSingleton<StarMark.Core.Search.SearchService>();
        services.AddSingleton<StarMark.Core.Sync.SyncCoordinator>();

        // ViewModel 层
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SearchPageViewModel>();
        services.AddSingleton<FolderTreePageViewModel>();
        services.AddTransient<TagsPageViewModel>();
        services.AddTransient<ActivityPageViewModel>();
        services.AddTransient<HiddenPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();

        Services = services.BuildServiceProvider();

        // 2. 执行数据库迁移
        Services.GetRequiredService<StarMark.Data.MigrationRunner>().EnsureSchema();

        // 2.1 种子数据
        try
        {
            var repo = Services.GetRequiredService<StarMark.Abstractions.IItemRepository>();
            Task.Run(() => SeedData.SeedIfEmptyAsync(repo, CancellationToken.None)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            StarMark.Abstractions.StarLog.Error("种子数据失败", ex);
        }

        // 3. 显示主窗口
        _window = new MainWindow();
        MainWindow = _window as StarMark.UI.MainWindow;
        _window.Activate();
    }
}
