#nullable enable
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
    [ObservableProperty] private bool _mainWindowTranslucent = true;
    [ObservableProperty] private int _backdropIndex;
    [ObservableProperty] private double _widgetOpacity = 0.72;

    /// <summary>毛玻璃材质浓度（0–1，对应 DeskBox 的 WidgetMaterialIntensity）。</summary>
    [ObservableProperty] private double _widgetMaterialIntensity = 0.65;

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

    /// <summary>不透明度百分比文本（滑块右侧读数）。</summary>
    public string OpacityPercentText => $"{(int)Math.Round(WidgetOpacity * 100)}%";

    /// <summary>材质浓度百分比文本（滑块右侧读数）。</summary>
    public string MaterialIntensityPercentText => $"{(int)Math.Round(WidgetMaterialIntensity * 100)}%";

    partial void OnWidgetOpacityChanged(double value) => OnPropertyChanged(nameof(OpacityPercentText));

    partial void OnWidgetMaterialIntensityChanged(double value) => OnPropertyChanged(nameof(MaterialIntensityPercentText));
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
        MainWindowTranslucent = Safe(_settings.LoadMainWindowTranslucent, true, "主窗口材质");
        BackdropIndex = (int)Safe(_settings.LoadWidgetBackdrop, WidgetBackdropKind.Acrylic, "外观材质");
        WidgetOpacity = Safe(_settings.LoadWidgetOpacity, WidgetAppearance.DefaultOpacity, "不透明度");
        WidgetMaterialIntensity = Safe(_settings.LoadWidgetMaterialIntensity, 0.65, "材质浓度");
        EnableWidgetSnap = Safe(_settings.LoadWidgetSnapEnabled, true, "边缘磁吸");

        // 性能模式 / 内存门禁
        PerformanceModeIndex = (int)Safe(_settings.LoadPerformanceMode, PerformanceMode.Balanced, "性能模式");
        CacheBudgetMb = Safe(_settings.LoadCacheBudgetMb, 200.0, "缓存预算");
        MaxCacheCount = Safe(_settings.LoadMaxImageCacheCount, 256, "缓存上限");

        // 本地文件索引（P0-1b）：根目录每行一个；上限数字
        FileIndexRootsText = string.Join("\n", Safe(_settings.LoadFileIndexRoots, Array.Empty<string>(), "索引目录"));
        MaxFileIndexCountText = Safe(_settings.LoadMaxFileIndexCount, 5000, "索引上限").ToString();
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
            var items = await _repository.GetAllAsync(
                new BrowseFilter { IncludeHidden = false, Limit = int.MaxValue }, CancellationToken.None);
            HealthReport = InsightsService.BuildHealthReport(items);
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

    /// <summary>采集只读诊断信息（P2-8）。本地查询，零网络。</summary>
    public async Task LoadDiagnosticsAsync()
    {
        if (_diagnostics is null) return;
        HasDiagnosticsError = false;
        try
        {
            var entries = await _diagnostics.CollectAsync(CancellationToken.None);
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
            _settings.SaveTheme(theme);
            _settings.SaveEnableTray(EnableTray);
            _settings.SaveEnableGlobalHotKey(EnableGlobalHotKey);
            _settings.SaveMinimizeToTray(MinimizeToTray);
            _settings.SaveWidgetSnapEnabled(EnableWidgetSnap);
            _settings.SaveWidgetBackdrop((WidgetBackdropKind)BackdropIndex);
            _settings.SaveWidgetOpacity(WidgetOpacity);
            _settings.SaveWidgetMaterialIntensity(WidgetMaterialIntensity);
            _settings.SaveMainWindowTranslucent(MainWindowTranslucent);
            _settings.SavePerformanceMode((PerformanceMode)PerformanceModeIndex);
            _settings.SaveCacheBudgetMb(CacheBudgetMb);
            _settings.SaveMaxImageCacheCount(MaxCacheCount);

            var github = new StarMark.Integrations.GitHub.GitHubOptions();
            if (!string.IsNullOrWhiteSpace(GithubToken)) github.Token = GithubToken.Trim();
            if (!string.IsNullOrWhiteSpace(GithubUsername)) github.Username = GithubUsername.Trim();
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
                    if (App.Services.GetService(typeof(StarMark.UI.Services.WidgetManager))
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