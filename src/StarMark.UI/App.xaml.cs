#nullable enable
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Core.Backup;
using StarMark.Core.Hotkeys;
using StarMark.Core.Widgets;
using StarMark.Core.Performance;
using StarMark.Core.Startup;
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

    /// <summary>
    /// 启动参数：<c>--resolve-ghost &lt;pid&gt;</c>＝"我是为了收掉一台残留进程才被提权重启的"。
    /// 提权实例在抢互斥体<b>之前</b>先按这个 pid 收一次（见 <see cref="ResolveStaleInstanceFromArgs"/>）。
    /// </summary>
    private const string ResolveGhostArg = "--resolve-ghost";

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

    /// <summary>
    /// 交接前让出单实例互斥体：提权/重启出的新实例会在数百毫秒内起跑并抢这把锁，
    /// 而旧进程要等 Environment.Exit 之后 OS 才释放句柄。抢锁失败的新实例会走
    /// 「激活已有窗口 + 自己退出」——它激活的正是那个马上消失的旧窗口，结果两个进程都没了，
    /// 用户看到的就是"双击没反应 / 闪退"。所有"拉起新进程后自己退出"的路径都必须先调这里。
    /// </summary>
    public static void ReleaseSingleInstanceForHandoff()
    {
        var m = _singleInstanceMutex;
        _singleInstanceMutex = null;
        if (m is null) return;
        try { m.ReleaseMutex(); } catch (ApplicationException) { }   // 非持有者释放会抛，交接场景下无所谓
        m.Dispose();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 会话边界标记：日志里此前没有任何"开始/结束"行，用户报"闪退"时无从判断一次运行
        // 是正常关闭还是被打断。ProcessExit 在正常返回与 Environment.Exit 两条路径都会触发，
        // 因此"有会话开始、无进程退出"就等价于异常终止，可直接把崩溃时刻与最后一次操作对齐。
        // build= 是这一行第二次被要求承担职责：真机反馈里出现过"修好的代码在树里、跑的是旧产物"
        // 而双方都不知道（VS 源码没变时 F5 不重编，产物时间戳看不出），于是"问题依旧"被当成了
        // "修复无效"。会话边界带上构建时刻，一句日志就能把这两种解释分开。
        StarLog.Info($"===== StarMark 会话开始 pid={Environment.ProcessId} elev={Privilege.IsElevated()} " +
            $"build={BuildInfo.Display} =====");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            StarLog.Info($"===== StarMark 进程退出 pid={Environment.ProcessId} =====");

        // 上一轮用户同意"提权收掉残留进程"时，这次就是来兑现的：必须在抢互斥体之前做，
        // 否则残留还攥着那把锁，我们自己就是那个"唤起不到窗口 → 自我退出"的新实例。
        ResolveStaleInstanceFromArgs();

        // 单实例：再次启动时唤起已有主窗口并退出新进程
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName,
            createdNew: out var createdNew);
        if (!createdNew)
        {
            if (TryActivateExistingInstance())
            {
                StarLog.Info("二次启动：已唤起已有实例，退出本进程");
                Environment.Exit(0);
                return;
            }
            // 唤不起：对面要么是一具没有窗口的残留（退出没结束进程留下的），要么正好在消失。
            // 这两种都不该让用户对着"双击没反应"猜，所以先试着把锁拿回来，拿不回才退。
            var problem = TryRecoverStaleInstance();
            if (problem is not null)
            {
                StarLog.Warn($"二次启动：既唤不起也没能回收残留，退出本进程。原因：{problem}");
                Environment.Exit(0);
                return;
            }
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
        // AI 通道网关（批次 A）。单例：它持有两个 HttpClient，按窗口创建会攒出连接池；
        // 批次 B 的批量整理也从这里取同一个实例，配置与"能不能用"的判定只此一份。
        services.AddSingleton<StarMark.Integrations.Ai.AiGateway>();
        services.AddSingleton<StarMark.UI.Services.AiClassifyService>();

        // 内置剪贴板历史：单例持有监听窗口与去重闸门。开关默认关 ⇒ 本会话绝不 TryStart（零读取、零落盘）。
        services.AddSingleton(sp => new StarMark.Integrations.Clipboard.ClipboardWatcher(
            sp.GetRequiredService<IItemRepository>()));

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

        // GitHub 热榜（不入库的候选流）：抓取在 Integrations、编排在 Core、缓存写在 sync_state（键前缀 trending:）。
        // Token 走 tokenProvider 现取而非构造期快照 ⇒ 设置里改了 Token 不需要重启就能按新配额抓取。
        services.AddSingleton<StarMark.Abstractions.Trending.ITrendingSource>(sp =>
            new StarMark.Integrations.Trending.TrendingFetcher(
                tokenProvider: () => sp.GetRequiredService<StarMark.Integrations.GitHub.GitHubOptions>().Token));
        services.AddSingleton(sp =>
            new StarMark.Core.Trending.TrendingService(
                sp.GetRequiredService<StarMark.Abstractions.Trending.ITrendingSource>(),
                (key, ct) => sp.GetRequiredService<IItemRepository>().GetSyncStateAsync(key, ct),
                (key, val, ct) => sp.GetRequiredService<IItemRepository>().SetSyncStateAsync(key, val, ct)));
        // 「已 Star / 已收藏」的会话态：远端 Star 成功后先记在这里，避免同步前刷新又显示成未 Star。
        services.AddSingleton<StarMark.Abstractions.Trending.TrendingStarState>();

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
        services.AddTransient<ClipboardPageViewModel>();
        // 热榜页 VM 用单例：页面每次进入都重读设置与缓存，抓取结果与两个动作的回填状态留在这一份上，
        // 切走再回来不必重抓（它同时是 TrendingItemActions.NoticeRaised 的唯一订阅者， transient 会累积委托）。
        services.AddSingleton<TrendingPageViewModel>();
        // RSS 页 VM 同样是单例：它持有这一轮抓到的条目，并且是 RssItemActions.NoticeRaised 的唯一订阅者
        // （transient 会让每次进页面都多挂一个委托，回报就会重复）。
        // 缓存档必须在它之前就能解析到——没注册的话 GetRequiredService 在构造 VM 时当场抛，
        // 表现是"点 RSS 这一页没反应"（SettingsStore 未入 DI 那次踩过同一处，批次 H1）。
        services.AddSingleton<StarMark.Core.Feed.RssCacheStore>();
        services.AddSingleton<RssPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();

        Services = services.BuildServiceProvider();

        // 性能模式设置来源注入（MemoryReclaimer 经 PerformanceSettingsPolicy 读取预算）
        try { PerformanceSettingsPolicy.Provider = fileSettings; }
        catch (Exception pex) { StarLog.Error("性能模式设置来源注入失败", pex); }

        // 本地磁盘搜索「A 方案：与提权 Everything 同权限」——若已开启且当前非提权，以管理员重启一次
        // （带 --elevate-retry 标记防死循环；用户取消 UAC 则继续普通运行，仅本地文件搜索用不了，其余不受影响）。
        // 每次启动记一行提权自检，便于定位 IPC(2) 究竟出在"StarMark 没真提权"还是"Everything 没提权"。
        // 冒烟/开发期要一台不提权的实例：带着 --elevate-retry 起就行（磁盘搜索开着时它就不再转提权），
        // 提权只服务于 Everything 那一条链，其余功能与权限无关。
        if (fileSettings.LoadLocalDiskSearchEnabled())
        {
            var alreadyRetried = false;
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "--elevate-retry", StringComparison.OrdinalIgnoreCase)) { alreadyRetried = true; break; }
            StarLog.Info($"本地磁盘搜索：提权自检 IsElevated={Privilege.IsElevated()} · elevateRetry={alreadyRetried} · pid={Environment.ProcessId}");
            if (!Privilege.IsElevated() && !alreadyRetried && Privilege.TryRelaunchSelfElevated("--elevate-retry"))
            {
                ReleaseSingleInstanceForHandoff();
                Environment.Exit(0);
            }
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
            StarMark.Abstractions.StartupProfile.Mark("DI 与数据库迁移完成");

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
            StarMark.Abstractions.StartupProfile.Mark("种子数据");

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
            StarMark.Abstractions.StartupProfile.Mark("本地条目迁移");

            // 3. 显示主窗口
            _window = new MainWindow();
            MainWindow = _window as StarMark.UI.MainWindow;
            StarMark.Abstractions.StartupProfile.Mark("主窗构造（含首屏导航）");
            _window.Activate();
            StarMark.Abstractions.StartupProfile.Mark("首帧提交");
            UIStallWatchdog.Start(_window.DispatcherQueue);   // 卡顿取证：把"卡死了"变成日志里的时长与当时的页面

            // 3.1 每日自动备份（P-51）：不可重建的笔记/标签/组件数据不能只靠用户记得手动导出。
            // 延后到首屏之后再起，避开与迁移、组件创建抢同一批磁盘 I/O；失败只进日志，不弹窗打断用户。
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    var svc = Services.GetRequiredService<BackupService>();
                    var written = await svc.RunAutoBackupAsync();
                    StarLog.Info(written is null
                        ? "自动备份：本次跳过（距上次不足 24 小时，或库里没有可备份的内容）"
                        : $"自动备份已落盘：{written}");
                }
                catch (Exception ex)
                {
                    StarLog.Error("自动备份失败（不影响使用，下次启动会再试）", ex);
                }
            });

            // 3.2 剪贴板历史（默认关）：开着才建监听窗口。必须在 UI 线程建——HWND_MESSAGE 的 WndProc
            // 由所属线程的消息队列驱动，线程池线程没有消息泵就永远收不到 WM_CLIPBOARDUPDATE。
            try
            {
                if (fileSettings.LoadClipboardHistoryEnabled())
                    ApplyClipboardHistory(true);
            }
            catch (Exception cex)
            {
                StarLog.Error("剪贴板历史启动失败（不影响其它功能）", cex);
            }

            // 3.3 护眼 / 休息提醒（默认关）：开着才挂那张 15 秒的节拍表。表挂在主窗的 DispatcherQueue 上，
            // 所以必须在主窗建好之后起（与剪贴板监听同一条顺序理由）。
            try
            {
                if (fileSettings.LoadEyeRestEnabled())
                    ApplyEyeRest(true);
            }
            catch (Exception ex)
            {
                StarLog.Error("护眼提醒启动失败（不影响其它功能）", ex);
            }

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
                hotkey.RegisterHandler(HotkeyActions.WidgetsToggleTopmostAll, () => widgetManager.ToggleAllTopmostAsync());
                hotkey.RegisterHandler(HotkeyActions.ScreenCapture, () => { StarMark.UI.Services.ScreenshotService.Start(StarMark.UI.Services.CaptureMode.Toolbar); return Task.CompletedTask; });
                hotkey.RegisterHandler(HotkeyActions.ScreenPin, () => { StarMark.UI.Services.ScreenshotService.Start(StarMark.UI.Services.CaptureMode.Pin); return Task.CompletedTask; });
                hotkey.RegisterHandler(HotkeyActions.ScreenOcr, () => { StarMark.UI.Services.ScreenshotService.Start(StarMark.UI.Services.CaptureMode.Ocr); return Task.CompletedTask; });
                hotkey.RegisterHandler(HotkeyActions.ScreenPinToggleHidden, () => { StarMark.UI.Services.PinManager.ToggleHidden(); return Task.CompletedTask; });
                hotkey.RegisterHandler(HotkeyActions.ScreenPinClickThrough, () => { StarMark.UI.Services.PinManager.ToggleClickThrough(); return Task.CompletedTask; });
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
            StarMark.Abstractions.StartupProfile.Mark("热键注册与内存门禁");
        }
        catch (Exception ex)
        {
            // 启动期异常若静默吞掉，用户会看到"双击无反应"。弹窗定位问题后退出。
            ShowStartupError(ex);
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// 唤起已有实例的主窗口（按窗口标题查找，组件窗口标题不同不会误匹配）。
    /// 返回是否真的唤起了——没唤起时调用方还要判断"对面是不是一具没窗口的残留"，
    /// 不能像早先那样不问缘由地自我退出（那正是"双击图标没反应"的成因）。
    /// <para>
    /// 必须校验属主进程：任意程序都能把自己的窗口标题设成 "StarMark"（浏览器标签页标题、同名小工具…），
    /// 不加校验时我们会对**别人的**窗口 ShowWindow/SetForegroundWindow——既把陌生人弹到用户面前、
    /// 又让自己这个实例退出，用户表现为"双击图标开了另一个程序，StarMark 没起来"，
    /// 且这是一个可被本地任意进程利用的前置抢占面。校验取不到结论时一律不动别人的窗口。
    /// </para>
    /// </summary>
    private static bool TryActivateExistingInstance()
    {
        try
        {
            var hwnd = WindowInterop.FindWindowW(null, AppConstants.AppName);
            if (hwnd == IntPtr.Zero) return false;

            if (!IsOurMainWindow(hwnd)) return false;

            // 窗口可能处于隐藏（最小化到托盘）或最小化状态：先显示再还原
            WindowInterop.ShowWindow(hwnd, WindowInterop.SW_SHOW);
            WindowInterop.ShowWindow(hwnd, WindowInterop.SW_RESTORE);
            WindowInterop.SetForegroundWindow(hwnd);
            return true;
        }
        catch (Exception ex)
        {
            StarLog.Warn($"唤起已有实例时出错（本实例接着判断残留）：{ex.Message}");
            return false;
        }
    }

    /// <summary>该 hwnd 是否属于"另一个 StarMark 进程"：进程名与自身所在 exe 同名且 pid 不是自己。</summary>
    private static bool IsOurMainWindow(IntPtr hwnd)
    {
        WindowInterop.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId)
        {
            StarLog.Warn($"按标题找到的「{AppConstants.AppName}」窗口属主 pid={pid} 不是另一实例，放弃转交");
            return false;
        }
        try
        {
            var name = System.Diagnostics.Process.GetProcessById(unchecked((int)pid)).ProcessName;
            if (string.Equals(name, InstanceProbe.OurProcessName, StringComparison.OrdinalIgnoreCase)) return true;
            StarLog.Warn($"按标题找到的「{AppConstants.AppName}」窗口属主是进程「{name}」(pid={pid})，非本应用，放弃转交");
            return false;
        }
        catch (Exception ex)
        {
            // 进程已退出 / 无权限读名字：宁可不动别人的窗口，也不要把陌生程序前置
            StarLog.Warn($"无法确认「{AppConstants.AppName}」窗口属主 pid={pid}（{ex.Message}），放弃转交");
            return false;
        }
    }

    /// <summary>
    /// 唤起不到窗口之后，判断并收掉"界面已经拆完、进程却没结束"的残留，把单实例锁拿回来。
    /// 返回 null＝锁已归本实例，可以照常启动；返回一段原因＝什么都不动、本进程退出（原因进日志）。
    /// </summary>
    private static string? TryRecoverStaleInstance()
    {
        var (action, pid, why) = InstanceHandoff.Decide(InstanceProbe.ListPeers());
        switch (action)
        {
            case HandoffAction.ActivatePeer:
                // 它还留着窗口，只是按标题没找到（主窗被关、只剩组件窗那类）：那不是残留，
                // 结束一个还在正常显示东西的进程比一次"双击没反应"严重得多。
                return $"{why}，但按标题没能唤起它的窗口";

            case HandoffAction.RecoverPeer:
                var (ok, permissionDenied, problem) = InstanceProbe.TryStop(pid);
                if (!ok)
                {
                    if (permissionDenied && !Privilege.IsElevated() && AcceptsElevatedCleanup(pid))
                    {
                        // 提权后的新实例会带着 --resolve-ghost 收掉它并照常启动。这里必须先把锁让出去
                        // 再退（见 ReleaseSingleInstanceForHandoff），否则新实例抢不到锁、又去唤起这个
                        // 马上要消失的进程，结果是两个都没了。
                        ReleaseSingleInstanceForHandoff();
                        Environment.Exit(0);
                    }
                    return problem;
                }
                return TryClaimMutex() ? null : $"{why}，但结束后互斥体仍没拿到";

            default:
                // 判定说"证据不足"时仍值得等一把：持有者可能正好在我们枚举的这几毫秒里结束——
                // 进程一死，锁会被"遗弃"，等到遗弃通知的这一方直接接管。
                return TryClaimMutex() ? null : why;
        }
    }

    /// <summary>再抢一次单实例锁（对面进程结束时锁会被遗弃，接到通知的一方即获得所有权）。</summary>
    private static bool TryClaimMutex(int waitMs = 2000)
    {
        if (_singleInstanceMutex is not { } m) return false;
        try
        {
            return m.WaitOne(waitMs);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
        catch (Exception ex)
        {
            StarLog.Warn($"等待单实例锁时出错：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 问一句要不要以管理员权限收掉那个残留。<b>这一句问不得省</b>：对面权限比我们高，是系统在拦
    /// （UIPI），程序自己没有任何合法路径越过去；而"打开任务管理器手动结束"是把活推给用户。
    /// 返回 true 表示提权实例已经拉起来，本进程要把锁让出去后退出。
    /// </summary>
    private static bool AcceptsElevatedCleanup(int pid)
    {
        const uint MB_YESNO = 0x04;
        const uint MB_ICONWARNING = 0x10;
        const int IDYES = 6;
        var answer = MessageBoxW(IntPtr.Zero,
            $"StarMark 发现上一次运行没收干净的进程（pid={pid}）。它以管理员权限运行，" +
            "当前这个实例没有权限结束它，所以既唤不起它、也抢不回单实例锁。\n\n" +
            "「是」＝以管理员权限重启 StarMark 并自动收掉那个残留（系统会再弹一次确认框）。\n" +
            "「否」＝本次启动直接退出，不动任何进程。",
            "StarMark 需要结束一个残留进程", MB_YESNO | MB_ICONWARNING);
        return answer == IDYES && Privilege.TryRelaunchSelfElevated($"{ResolveGhostArg} {pid}");
    }

    /// <summary>
    /// 兑现"提权收残留"那一句：只在带 <c>--resolve-ghost &lt;pid&gt;</c> 时做事，而且只结束
    /// <b>指名那台、同名、已过宽限期、确实一个窗口都没有</b>的进程——四个条件缺任何一个都不动手。
    /// 这里不再弹框、也不再递归提权（这次已是提权实例；失败只记一行日志）。
    /// </summary>
    private static void ResolveStaleInstanceFromArgs()
    {
        var argv = Environment.GetCommandLineArgs();
        var at = Array.FindIndex(argv, a => string.Equals(a, ResolveGhostArg, StringComparison.OrdinalIgnoreCase));
        if (at < 0) return;
        if (at + 1 >= argv.Length || !int.TryParse(argv[at + 1], out var pid) || pid <= 0)
        {
            StarLog.Warn($"{ResolveGhostArg} 后面没带上 pid，跳过清理");
            return;
        }
        if (!Privilege.IsElevated())
        {
            StarLog.Warn($"{ResolveGhostArg} pid={pid}：这次不是管理员权限，收不掉，跳过（不再重复弹框）");
            return;
        }
        if (!InstanceProbe.ListPeers().Any(p =>
                p.Pid == pid && p.NameMatches && !p.HasTopLevelWindow && p.PastGrace))
        {
            StarLog.Info($"{ResolveGhostArg} pid={pid} 已不是残留状态（窗口回来了／已经不在了），不用清理");
            return;
        }
        var (ok, _, problem) = InstanceProbe.TryStop(pid);
        StarLog.Info(ok ? $"{ResolveGhostArg}：已结束残留实例 pid={pid}" : $"{ResolveGhostArg}：{problem}");
    }

    /// <summary>托盘/组件唤起主窗口的统一入口。</summary>
    public static void PresentMainWindow(bool settings = false)
    {
        MainWindow?.Present(settings);
    }

    /// <summary>
    /// 按设置启停剪贴板采集，返回<b>实际</b>是否在采集（而不是"用户点了开"）。
    /// <para>必须在 UI 线程调用：监听窗口的消息泵属于调用线程。设置页的开关回调正在 UI 线程上，
    /// 因此直接同步调用即可——拿到 false 就把"没启起来"照实显示出来，不能默默假装有历史。</para>
    /// </summary>
    public static bool ApplyClipboardHistory(bool enabled)
    {
        try
        {
            var watcher = Services.GetRequiredService<StarMark.Integrations.Clipboard.ClipboardWatcher>();
            if (!enabled)
            {
                watcher.Stop();
                return false;
            }
            return watcher.TryStart();
        }
        catch (Exception ex)
        {
            StarLog.Error("切换剪贴板采集失败", ex);
            return false;
        }
    }

    /// <summary>
    /// 按设置起停护眼提醒，回报<b>实际</b>是否在跑（设置页显示的是这个，不是"用户点了开"）。
    /// 必须在 UI 线程调用：节拍表挂在主窗的 DispatcherQueue 上，而主窗可能在托盘里——
    /// 所以拿 <see cref="MainWindow"/> 的队列而不是"指望调用方在哪条线程"。
    /// </summary>
    public static bool ApplyEyeRest(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                StarMark.UI.Services.EyeRestService.Stop();
                return false;
            }
            var queue = MainWindow?.DispatcherQueue;
            if (queue is null)
            {
                StarLog.Warn("护眼提醒起不来：主窗还不存在（应用还没起完？）");
                return false;
            }
            StarMark.UI.Services.EyeRestService.Start(queue, Services.GetRequiredService<StarMark.UI.Helpers.SettingsStore>());
            return StarMark.UI.Services.EyeRestService.IsRunning;
        }
        catch (Exception ex)
        {
            StarLog.Error("切换护眼提醒失败", ex);
            return false;
        }
    }

    /// <summary>剪贴板采集当前是否真的在跑（监听窗口已建立）。</summary>
    public static bool IsClipboardCollecting
    {
        get
        {
            try
            {
                return Services.GetRequiredService<StarMark.Integrations.Clipboard.ClipboardWatcher>().IsRunning;
            }
            catch { return false; }
        }
    }

    /// <summary>用户是否开过「剪贴板历史」开关（默认关）。与"是否真的在采集"是两件事，分开问。</summary>
    public static bool IsClipboardHistoryEnabled()
    {
        try
        {
            return new StarMark.UI.Helpers.SettingsStore().LoadClipboardHistoryEnabled();
        }
        catch (Exception ex)
        {
            StarLog.Error("读取剪贴板历史开关失败（按未开启处理）", ex);
            return false;
        }
    }

    /// <summary>是否处于「暂停记录」（监听在跑但不落库）。取不到监听窗时按未暂停处理，不猜。</summary>
    public static bool IsClipboardPaused
    {
        get
        {
            try
            {
                var w = Services.GetRequiredService<StarMark.Integrations.Clipboard.ClipboardWatcher>();
                return w.IsRunning && w.Paused;
            }
            catch { return false; }
        }
    }

    /// <summary>暂停/恢复记录（临时粘贴私密内容用）。暂停期间监听仍在，只是不落库。</summary>
    public static void SetClipboardPaused(bool paused)
    {
        try
        {
            Services.GetRequiredService<StarMark.Integrations.Clipboard.ClipboardWatcher>().Paused = paused;
        }
        catch (Exception ex) { StarLog.Error("切换剪贴板暂停失败", ex); }
    }

    /// <summary>
    /// 登记"这段内容是 StarMark 自己写进剪贴板的"，采集时按回声挡掉一次。
    /// 任何往剪贴板写内容的地方（复制链接、历史页"再复制"）都必须在写之前调它，
    /// 否则用户点开历史页翻几下，列表就会自己重排——像是被别的东西动过。
    /// </summary>
    public static void NoteClipboardOwnWrite(string? text)
    {
        try
        {
            Services.GetRequiredService<StarMark.Integrations.Clipboard.ClipboardWatcher>().NoteOwnWrite(text);
        }
        catch { /* 登记失败最多导致多记一条回声，不该影响复制本身 */ }
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
