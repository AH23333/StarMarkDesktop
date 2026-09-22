#nullable enable
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using StarMark.Abstractions;
using StarMark.Abstractions.Insights;
using StarMark.Core.Insights;
using StarMark.Core.Performance;
using StarMark.UI.Helpers;
using Windows.UI;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 设置页 ViewModel。主题 / 托盘 / 热键 / GitHub 配置的读取与保存。
/// 主题变更即时生效（通过 <see cref="MainWindow"/> 应用），其余重启后生效。
/// </summary>
public partial class SettingsPageViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly IItemRepository? _repository;
    private readonly StarMark.Core.Diagnostics.DiagnosticsService? _diagnostics;

    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private bool _enableTray = true;
    [ObservableProperty] private bool _enableGlobalHotKey = true;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private string _githubToken = string.Empty;
    [ObservableProperty] private string _githubUsername = string.Empty;

    // 外观（半透明亚克力 / 云母 / 不透明 + 不透明度）
    /// <summary>组件材质下拉索引（= <see cref="WidgetBackdropKind"/> 整数值）。</summary>
    [ObservableProperty] private int _backdropIndex;
    /// <summary>主窗口材质下拉索引（批次 J：与组件材质各自独立；= <see cref="WidgetBackdropKind"/> 整数值，None=不透明）。</summary>
    [ObservableProperty] private int _mainWindowBackdropIndex;
    [ObservableProperty] private double _widgetOpacity = 0.72;
    /// <summary>主窗口背景不透明度（独立于组件 <see cref="WidgetOpacity"/>）。批次 J 已拆材质，这里补齐拆不透明度。</summary>
    [ObservableProperty] private double _mainWindowOpacity = 0.72;

    // 性能模式 / 内存门禁（Phase B-8）
    [ObservableProperty] private int _performanceModeIndex;
    [ObservableProperty] private double _cacheBudgetMb = 200;
    [ObservableProperty] private int _maxCacheCount = 256;

    /// <summary>仅「自定义」性能模式显示预算 / 缓存上限控件。</summary>
    public bool CustomBudgetVisible => PerformanceModeIndex == (int)PerformanceMode.Custom;

    /// <summary>进程内存预算读数文本（滑块右侧）。</summary>
    public string CacheBudgetText => $"{CacheBudgetMb:0} MB";

    partial void OnPerformanceModeIndexChanged(int value) => OnPropertyChanged(nameof(CustomBudgetVisible));

    partial void OnCacheBudgetMbChanged(double value) => OnPropertyChanged(nameof(CacheBudgetText));

    /// <summary>组件边缘磁吸总开关（关闭后用户自由摆位）。</summary>
    [ObservableProperty] private bool _enableWidgetSnap = true;

    /// <summary>磁吸贴合后的目标间距（逻辑像素，0＝边边紧贴）。</summary>
    [ObservableProperty] private double _snapSpacing = 8;

    /// <summary>磁吸强度：距参照线多近即吸附（逻辑像素）。越大越容易吸上。</summary>
    [ObservableProperty] private double _snapStrength = 24;

    public string SnapSpacingText => $"{(int)SnapSpacing} px";
    public string SnapStrengthText => $"{(int)SnapStrength} px";

    partial void OnSnapSpacingChanged(double value) => OnPropertyChanged(nameof(SnapSpacingText));
    partial void OnSnapStrengthChanged(double value) => OnPropertyChanged(nameof(SnapStrengthText));

    /// <summary>组件不透明度百分比文本（滑块右侧读数）。</summary>
    public string OpacityPercentText => $"{(int)Math.Round(WidgetOpacity * 100)}%";

    /// <summary>主窗口不透明度百分比文本（滑块右侧读数）。</summary>
    public string MainWindowOpacityPercentText => $"{(int)Math.Round(MainWindowOpacity * 100)}%";

    partial void OnWidgetOpacityChanged(double value) => OnPropertyChanged(nameof(OpacityPercentText));
    partial void OnMainWindowOpacityChanged(double value) => OnPropertyChanged(nameof(MainWindowOpacityPercentText));

    [ObservableProperty] private string _saveErrorMessage = string.Empty;
    [ObservableProperty] private bool _hasSaveError;

    // 备份与恢复（P0-2）状态反馈
    [ObservableProperty] private string _backupStatus = string.Empty;
    [ObservableProperty] private bool _isBackupBusy;

    public SettingsPageViewModel()
    {
        // 与 MainWindow 保持同一实例语义：settings 文件路径由环境变量决定
        _settings = new SettingsStore();
    }

    /// <summary>带仓储的构造函数（DI 注入），用于加载收藏健康度报告与诊断信息。</summary>
    public SettingsPageViewModel(IItemRepository repository, StarMark.Core.Diagnostics.DiagnosticsService? diagnostics = null) : this()
    {
        _repository = repository;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// 逐项带兜底地读取设置：任何一项读取失败都只让它回退默认值并写日志，
    /// 绝不把异常抛出去——LoadFromStore 在 SettingsPage 构造函数里调用，
    /// 抛错会让 Frame.Navigate 失败，用户点「设置」就整应用卡死崩溃（历史事故）。
    /// </summary>
    private static T Safe<T>(Func<T> read, T fallback, string what)
    {
        try { return read(); }
        catch (Exception ex)
        {
            StarLog.Error($"读取设置失败（{what}），已回退默认值", ex);
            return fallback;
        }
    }

    public void LoadFromStore()
    {
        var github = Safe(() => StarMark.Integrations.GitHub.GitHubOptions.Load(),
            new StarMark.Integrations.GitHub.GitHubOptions(), "GitHub 配置");
        ThemeIndex = Safe(_settings.LoadTheme, ThemePreference.Default, "主题") switch
        {
            ThemePreference.Light => 1,
            ThemePreference.Dark => 2,
            _ => 0,
        };
        EnableTray = Safe(_settings.LoadEnableTray, true, "托盘");
        EnableGlobalHotKey = Safe(_settings.LoadEnableGlobalHotKey, true, "全局热键");
        MinimizeToTray = Safe(_settings.LoadMinimizeToTray, true, "最小化到托盘");
        GithubToken = github.Token ?? string.Empty;
        GithubUsername = github.Username ?? string.Empty;

        // 外观 + 磁吸
        BackdropIndex = (int)Safe(_settings.LoadWidgetBackdrop, WidgetBackdropKind.Acrylic, "组件材质");
        MainWindowBackdropIndex = (int)Safe(_settings.LoadMainWindowBackdrop, WidgetBackdropKind.Acrylic, "主窗口材质");
        WidgetOpacity = Safe(_settings.LoadWidgetOpacity, WidgetAppearance.DefaultOpacity, "不透明度");
        MainWindowOpacity = Safe(_settings.LoadMainWindowOpacity, WidgetAppearance.DefaultOpacity, "主窗口不透明度");
        EnableWidgetSnap = Safe(_settings.LoadWidgetSnapEnabled, true, "边缘磁吸");
        SnapSpacing = Safe(_settings.LoadWidgetSnapSpacing, 8, "磁吸间距");
        SnapStrength = Safe(_settings.LoadWidgetSnapStrength, 24, "磁吸强度");

        // 性能模式 / 内存门禁
        PerformanceModeIndex = (int)Safe(_settings.LoadPerformanceMode, PerformanceMode.Balanced, "性能模式");
        CacheBudgetMb = Safe(_settings.LoadCacheBudgetMb, 200.0, "缓存预算");
        MaxCacheCount = Safe(_settings.LoadMaxImageCacheCount, 256, "缓存上限");

        // 本地文件索引（P0-1b）：根目录每行一个；上限数字
        FileIndexRootsText = string.Join("\n", Safe(_settings.LoadFileIndexRoots, Array.Empty<string>(), "索引目录"));
        MaxFileIndexCountText = Safe(_settings.LoadMaxFileIndexCount, 5000, "索引上限").ToString();

        // 本地磁盘搜索开关：回灌初值时抑制副作用（见 _suppressLocalDiskApply）。
        _suppressLocalDiskApply = true;
        LocalDiskSearchEnabled = Safe(_settings.LoadLocalDiskSearchEnabled, false, "本地磁盘搜索");
        _suppressLocalDiskApply = false;
        LocalDiskSearchStatus = LocalDiskSearchEnabled
            ? "已开启：本地文件会出现在快捷搜索 / 主窗搜索结果中。"
            : "已关闭：开启后可像 Everything 一样全盘秒搜本地文件（首次会自动准备 Everything）。";
    }

    // ===== 收藏健康度（P2-6）=====
    [ObservableProperty] private HealthReport _healthReport = new();
    [ObservableProperty] private bool _isHealthLoading;
    [ObservableProperty] private bool _hasHealthError;
    [ObservableProperty] private string _healthError = string.Empty;
    [ObservableProperty] private string _scoreText = "—";
    [ObservableProperty] private string _scoreGrade = string.Empty;
    [ObservableProperty] private Brush _scoreBrush = new SolidColorBrush(Color.FromArgb(255, 16, 124, 16));
    [ObservableProperty] private string _scoreSummary = string.Empty;
    [ObservableProperty] private string _languageSummary = string.Empty;
    [ObservableProperty] private string _duplicateSummary = string.Empty;
    [ObservableProperty] private string _tagSummary = string.Empty;
    [ObservableProperty] private List<HealthTrendBar> _trendBars = new();

    /// <summary>拉取全部条目并构建健康度报告（P2-6）。本地聚合，零网络。</summary>
    public async Task LoadHealthAsync()
    {
        if (_repository is null) return;
        IsHealthLoading = true;
        HealthError = string.Empty;
        HasHealthError = false;
        try
        {
            // 整表读 + 全量健康度聚合都是同步跑完的（Microsoft.Data.Sqlite 无真异步 I/O），
            // 而这里是"进设置页"的直接 await 目标 ⇒ 不 offload 就是一次点设置页冻结整窗。
            // await 不加 ConfigureAwait(false)：Task.Run 之后仍回到 UI 线程刷 HealthReport/视图。
            var report = await Task.Run(async () =>
            {
                var items = await _repository.GetAllAsync(
                    new BrowseFilter { IncludeHidden = false, Limit = int.MaxValue }, CancellationToken.None);
                return InsightsService.BuildHealthReport(items);
            });
            HealthReport = report;
            RefreshHealthView();
        }
        catch (Exception ex)
        {
            HasHealthError = true;
            HealthError = $"健康度计算失败：{ex.Message}";
            StarLog.Error($"健康度计算失败: {ex}");
        }
        finally { IsHealthLoading = false; }
    }

    private void RefreshHealthView()
    {
        var r = HealthReport;
        ScoreText = r.Score.ToString();
        ScoreGrade = r.Score >= 80 ? "健康" : r.Score >= 50 ? "一般，可优化" : "需整理";
        ScoreBrush = r.Score >= 80
            ? new SolidColorBrush(Color.FromArgb(255, 16, 124, 16))   // 绿
            : r.Score >= 50
                ? new SolidColorBrush(Color.FromArgb(255, 214, 137, 16)) // 琥珀
                : new SolidColorBrush(Color.FromArgb(255, 196, 43, 28));  // 红

        ScoreSummary = r.Factors.Count == 0
            ? "收藏整理良好，继续保持"
            : "待优化：" + string.Join("、", r.Factors.Select(f => f.Label));

        LanguageSummary = r.LanguageTop.Count == 0
            ? "（无）"
            : string.Join(" · ", r.LanguageTop.Take(5).Select(s => $"{s.Language} {s.Count}"));
        DuplicateSummary = r.DuplicateTop.Count == 0
            ? "（无）"
            : string.Join("、", r.DuplicateTop.Take(5).Select(d => $"{d.Title}（{d.Count}）"));
        TagSummary = r.TagHistogram.Count == 0
            ? "（无）"
            : string.Join(" · ", r.TagHistogram.Take(5).Select(t => $"{t.Tag} {t.Count}"));

        var max = r.NewTrend.Count == 0 ? 0 : r.NewTrend.Max(t => t.Count);
        TrendBars = r.NewTrend
            .Select(t => new HealthTrendBar
            {
                Count = t.Count,
                DateLabel = t.Date,
                Height = max > 0 && t.Count > 0 ? Math.Max(3, t.Count * 80.0 / max) : 0,
            })
            .ToList();
    }

    // ===== 诊断（P2-8）=====
    public System.Collections.ObjectModel.ObservableCollection<StarMark.Core.Diagnostics.DiagnosticEntry> DiagnosticEntries { get; }
        = new();
    [ObservableProperty] private bool _hasDiagnosticsError;
    [ObservableProperty] private string _diagnosticsError = string.Empty;

    // ===== 本地文件索引（P0-1b）=====
    [ObservableProperty] private string _fileIndexRootsText = string.Empty;
    [ObservableProperty] private string _maxFileIndexCountText = string.Empty;

    /// <summary>
    /// 本地磁盘搜索总开关。开启即把 <see cref="StarMark.Integrations.Everything.FileIndexOptions.Enabled"/>
    /// 单例实时翻位——<c>EverythingSource.IsAvailable</c> 每次查询都实读该属性，故统一搜索立刻纳入/剔除
    /// 本地文件源，无需重启（收束待决策 P-2 方案 B）。开启时后台懒起 <c>EnsureReadyAsync</c> 准备 Everything。
    /// </summary>
    [ObservableProperty] private bool _localDiskSearchEnabled;

    /// <summary>开关下方的一行状态提示文本。</summary>
    [ObservableProperty] private string _localDiskSearchStatus = string.Empty;

    /// <summary>LoadFromStore 回灌初值期间抑制 <see cref="OnLocalDiskSearchEnabledChanged"/> 副作用，
    /// 免得每次进入设置页就把默认值再存一遍、甚至误触发 Everything 拉起。</summary>
    private bool _suppressLocalDiskApply;

    partial void OnLocalDiskSearchEnabledChanged(bool value)
    {
        if (_suppressLocalDiskApply) return;
        var options = App.Services.GetRequiredService<StarMark.Integrations.Everything.FileIndexOptions>();

        // 关闭：即时翻位单例（IsAvailable 实读），统一搜索立刻剔除本地文件源；已装的 Everything / 服务不动。
        if (!value)
        {
            options.Enabled = false;
            _settings.SaveLocalDiskSearchEnabled(false);
            LocalDiskSearchStatus = "已关闭：不再搜索本地文件（已安装的 Everything 不受影响）。";
            return;
        }

        // 开启：持久化 + 实时翻位单例（IsAvailable 实读）。若已是管理员权限，就地后台准备 Everything；
        // 否则走「A 方案」——请求以管理员重启一次，让新实例与提权运行的 Everything 同 IL，WM_COPYDATA IPC 不再被 UIPI 拦。
        options.Enabled = true;
        _settings.SaveLocalDiskSearchEnabled(true);

        if (Privilege.IsElevated())
        {
            LocalDiskSearchStatus = "已开启（管理员）：正在准备 Everything（起 SDK / 拉起客户端）…";
            _ = Task.Run(async () =>
            {
                try
                {
                    var src = App.Services.GetRequiredService<StarMark.Integrations.Everything.EverythingSource>();
                    var outcome = await src.EnableAsync(CancellationToken.None);
                    App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
                        LocalDiskSearchStatus = outcome == StarMark.Integrations.Everything.EverythingSource.LocalDiskSearchEnableOutcome.Ready
                            ? "已开启：本地文件将参与全盘搜索（快捷搜索 / 主窗即时生效）。"
                            : "已开启，但 Everything 尚未就绪——请确认 Everything 正在运行后稍候再试。");
                }
                catch (Exception ex)
                {
                    StarLog.Error("本地磁盘搜索：准备 Everything 失败", ex);
                    App.MainWindow?.DispatcherQueue?.TryEnqueue(
                        () => LocalDiskSearchStatus = "已开启，但准备 Everything 失败（离线 / 未装成），可稍后重试。");
                }
            });
            return;
        }

        LocalDiskSearchStatus = "已开启。正请求以管理员身份重启 StarMark（以便连上以管理员运行的 Everything）——请在 UAC 点「是」…";
        if (Privilege.TryRelaunchSelfElevated("--elevate-retry"))
        {
            // 与 App 的启动期提权重启同一个坑：先让出单实例互斥体再 Exit，否则提权新实例抢锁失败
            // 会去"激活"这个马上消失的旧窗口然后自己退出 ⇒ 两个进程都没了（表现为点开关于就闪退）。
            App.ReleaseSingleInstanceForHandoff();
            Environment.Exit(0);   // 交给提权实例（其启动自带 --elevate-retry，不再二次弹窗）
            return;
        }
        LocalDiskSearchStatus = "已开启，但你取消了提权重启。若 Everything 以管理员运行，请用开始菜单「以管理员身份运行」重启 StarMark，否则本地文件可能搜不到。";
    }

    /// <summary>采集只读诊断信息（P2-8）。本地查询，零网络。</summary>
    public async Task LoadDiagnosticsAsync()
    {
        if (_diagnostics is null) return;
        HasDiagnosticsError = false;
        try
        {
            // 诊断里含 COUNT(*) 全量扫描与逐表统计：同样是"进设置页即同步跑完"的站点，offload 后回 UI 填集合。
            var entries = await Task.Run(() => _diagnostics.CollectAsync(CancellationToken.None));
            DiagnosticEntries.Clear();
            foreach (var e in entries) DiagnosticEntries.Add(e);
        }
        catch (Exception ex)
        {
            HasDiagnosticsError = true;
            DiagnosticsError = $"诊断信息采集失败：{ex.Message}";
            StarLog.Error($"诊断信息采集失败: {ex}");
        }
    }

    [RelayCommand]
    private void Save()
    {
        HasSaveError = false;
        try
        {
            var theme = ThemeIndex switch
            {
                1 => ThemePreference.Light,
                2 => ThemePreference.Dark,
                _ => ThemePreference.Default,
            };
            // 一次保存串 16 项：不在批量区间里就是 16 次整档读 + 16 次原子替换，而拖滑杆会按
            // 350 ms 自动保存的节奏反复走这一轮（P-43）。区间内读写同一份待落盘快照。
            using (_settings.BeginBatch())
            {
                _settings.SaveTheme(theme);
                _settings.SaveEnableTray(EnableTray);
                _settings.SaveEnableGlobalHotKey(EnableGlobalHotKey);
                _settings.SaveMinimizeToTray(MinimizeToTray);
                _settings.SaveWidgetSnapEnabled(EnableWidgetSnap);
                _settings.SaveWidgetSnapSpacing((int)SnapSpacing);
                _settings.SaveWidgetSnapStrength((int)SnapStrength);
                _settings.SaveWidgetBackdrop((WidgetBackdropKind)BackdropIndex);
                _settings.SaveWidgetOpacity(WidgetOpacity);
                _settings.SaveMainWindowBackdrop((WidgetBackdropKind)MainWindowBackdropIndex);
                _settings.SaveMainWindowOpacity(MainWindowOpacity);
                _settings.SavePerformanceMode((PerformanceMode)PerformanceModeIndex);
                _settings.SaveCacheBudgetMb(CacheBudgetMb);
                _settings.SaveMaxImageCacheCount(MaxCacheCount);

                // 载入现有配置再改：旧写法 new GitHubOptions() 整档重写会把本面板不出现的
                // SyncIntervalSeconds / PageSize 等字段静默重置为默认；Load→改→Save 仅覆盖 Token/Username。
                var github = StarMark.Integrations.GitHub.GitHubOptions.Load();
                github.Token = string.IsNullOrWhiteSpace(GithubToken) ? null : GithubToken.Trim();
                github.Username = string.IsNullOrWhiteSpace(GithubUsername) ? null : GithubUsername.Trim();
                github.Save();

                // 本地文件索引（P0-1b）：只保留存在的目录；上限需为正整数
                var roots = FileIndexRootsText
                    .Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0 && Directory.Exists(s))
                    .Distinct()
                    .ToList();
                _settings.SaveFileIndexRoots(roots);
                if (int.TryParse(MaxFileIndexCountText, out var cap) && cap > 0)
                    _settings.SaveMaxFileIndexCount(cap);
            }

            // 即时把根目录 / 上限推给 FileIndexOptions 单例，令后台重扫无需重启即生效（与开关同为方案 B）。
            // 读回持久化值而非直接用 roots：空 roots 时 LoadFileIndexRoots 会回退默认（桌面/下载/文档），与建库时口径一致。
            try
            {
                var fo = App.Services.GetRequiredService<StarMark.Integrations.Everything.FileIndexOptions>();
                fo.Roots = _settings.LoadFileIndexRoots().ToList();
                fo.MaxCount = _settings.LoadMaxFileIndexCount();
            }
            catch (Exception fox)
            {
                StarLog.Error("本地文件索引配置即时应用失败（下次重启仍会生效）", fox);
            }

            StarMark.Abstractions.StarLog.Info($"设置已保存（主题={theme}, 托盘={EnableTray}）");
            SaveErrorMessage = string.Empty;

            // 主题即时应用
            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                if (App.MainWindow != null)
                {
                    Helpers.ThemeManager.Apply(App.MainWindow, theme);
                    App.MainWindow.RefreshThemeIcon(theme);
                }
            });

            // 半透明外观即时应用：主窗口 + 所有已打开的组件窗口
            try
            {
                App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    App.MainWindow?.RefreshAppearance();
                    if (App.Services.GetRequiredService<StarMark.UI.Services.WidgetManager>()
                        is StarMark.UI.Services.WidgetManager mgr)
                        _ = mgr.RefreshAppearanceAsync();
                });
            }
            catch (Exception ex)
            {
                StarMark.Abstractions.StarLog.Error("应用外观设置失败", ex);
            }
        }
        catch (Exception ex)
        {
            HasSaveError = true;
            SaveErrorMessage = $"保存失败: {ex.Message}";
            StarMark.Abstractions.StarLog.Error($"设置保存失败: {ex}");
        }
    }
}

/// <summary>健康度趋势柱状图的单根柱（P2-6）。高度已按最大值归一化。</summary>
public sealed class HealthTrendBar
{
    public double Height { get; init; }
    public int Count { get; init; }
    public string DateLabel { get; init; } = string.Empty;
}