#nullable enable
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using StarMark.Abstractions;
using StarMark.Abstractions.Insights;
using StarMark.Integrations.Clipboard;
using StarMark.Core.Backup;
using StarMark.Core.Hotkeys;
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

    /// <summary>
    /// 本次会话是否以管理员权限运行（开本地磁盘搜索时会重启自己提权，见 P-46）。
    /// 提权进程跨完整性调不到中 IL 的系统文件对话框宿主 ⇒ 导出/导入的选择器稳定失败，
    /// 与其让用户以为"备份功能坏了"，不如在备份卡片里先说明。进程存续期间不会变，故不做通知。
    /// </summary>
    public bool IsElevatedSession { get; } = Privilege.IsElevated();

    /// <summary>列表里最多展示多少份（盘点本身另有上限，这里只是不让卡片被几百行撑爆）。</summary>
    public const int BackupListLimit = 20;

    /// <summary>
    /// 本机已有备份的清单。<b>存在的理由</b>：卡片一直写着"应用会自己备份""任何恢复都可回滚"，
    /// 却没有任何地方能看到"到底有哪些份、什么时候、多大"，也没有点一下就能恢复的入口——
    /// 名字暗示的能力停在"要自己敲文件名"。这里把它接成可见、可点。
    /// </summary>
    public ObservableCollection<BackupRow> Backups { get; } = new();

    public bool HasBackups => Backups.Count > 0;

    public string BackupsHeader => Backups.Count == 0
        ? "本机备份"
        : $"本机备份（列出最近 {Backups.Count} 份）";

    /// <summary>
    /// 重新扫描备份目录。<b>同步</b>文件枚举：件数被上限钉死在几十份以内、目录在本地盘，
    /// 走异步只会多出跳转与状态而没有收益（本仓库的教训：await ≠ 换线程，这里更不需要 await）。
    /// 读目录失败只写状态行、不抛出——列表空掉不能连带把整个设置页卡住。
    /// </summary>
    public void RefreshBackups()
    {
        Backups.Clear();
        try
        {
            foreach (var f in BackupService.EnumerateBackups(max: BackupListLimit))
                Backups.Add(new BackupRow(f));
        }
        catch (Exception ex)
        {
            StarLog.Warn($"列出本机备份失败：{ex.Message}");
            BackupStatus = $"未能列出备份文件：{ex.Message}";
        }
        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(BackupsHeader));
    }

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
        RefreshFileIndexRootsStatus();

        // 本地磁盘搜索开关：回灌初值时抑制副作用（见 _suppressLocalDiskApply）。
        _suppressLocalDiskApply = true;
        LocalDiskSearchEnabled = Safe(_settings.LoadLocalDiskSearchEnabled, false, "本地磁盘搜索");
        _suppressLocalDiskApply = false;
        RefreshLocalDiskSearchState();

        // 剪贴板历史：进页面只回灌状态，<b>不因为"显示"而去建监听窗口</b>；
        // 但"开关开着却没在记录"必须一眼看得见，否则用户会一直以为历史在长。
        _suppressClipboardApply = true;
        ClipboardHistoryEnabled = Safe(_settings.LoadClipboardHistoryEnabled, false, "剪贴板历史");
        _suppressClipboardApply = false;
        ClipboardHistoryStatus = !ClipboardHistoryEnabled
            ? "未开启：不读剪贴板、不落盘。开启后自动记录，不需要其它步骤。"
            : App.IsClipboardCollecting
                ? "正在记录本机复制的内容（密码管理器与私钥 / 令牌 / 卡号形态除外）。"
                : "开关是开着的，但本会话的剪贴板监听没建立起来，暂时不会记录新内容——关掉再打开本开关可重试。";

        // 图片那三项 + 导出带不带图片本体同样"只回灌、不因为显示而写盘"；但占用统计要真算一次：
        // §3-Q1 的第三层就是"让用户在填上限之前先看见现在占了多少"。
        _suppressClipboardImageApply = true;
        ClipboardImageEnabled = Safe(_settings.LoadClipboardImageEnabled, false, "剪贴板图片采集");
        ClipboardImageMaxValue = Safe(_settings.LoadClipboardImageMaxEntries,
            StarMark.Abstractions.Clipboard.ClipboardPolicy.DefaultImageMaxEntries, "图片条数上限");
        ClipboardTextMaxValue = Safe(_settings.LoadClipboardTextMaxEntries,
            StarMark.Abstractions.Clipboard.ClipboardPolicy.MaxEntries, "文本条数上限");
        BackupClipboardImagesEnabled = Safe(_settings.LoadBackupClipboardImagesEnabled, true, "导出带图片本体");
        _suppressClipboardImageApply = false;
        ClipboardImageStatus = ClipboardImageStatusText(App.IsClipboardCollecting, ClipboardImageEnabled);
        ComputeClipboardUsage();

        // 护眼 / 休息提醒：进页面只回灌四项当前值，<b>不因为"显示"而去起停定时器</b>
        // （否则每次打开设置页都等于把节拍表重建一遍，"下一次几点"会被悄悄推后）。
        _suppressEyeRestApply = true;
        EyeRestEnabled = Safe(_settings.LoadEyeRestEnabled, false, "护眼提醒");
        EyeRestIntervalIndex = StarMark.Core.Health.EyeRestPolicy.IntervalIndexOf(
            Safe(_settings.LoadEyeRestIntervalMinutes, StarMark.Core.Health.EyeRestPolicy.DefaultIntervalMinutes, "护眼间隔"));
        EyeRestEnforced = Safe(_settings.LoadEyeRestEnforced, false, "护眼强制模式");
        EyeRestDeferOnFullscreen = Safe(_settings.LoadEyeRestDeferOnFullscreen, true, "护眼全屏让路");
        _suppressEyeRestApply = false;
        EyeRestStatus = BuildEyeRestStatus();

        // GitHub 热榜：回灌两个开关的当前值（副作用同样在回灌期间抑制）。
        _suppressTrendingApply = true;
        TrendingEnabled = Safe(_settings.LoadTrendingEnabled, false, "GitHub 热榜");
        TrendingGlanceEnabled = Safe(_settings.LoadTrendingGlanceEnabled, false, "热榜·今日速览");
        _suppressTrendingApply = false;
        TrendingStatus = TrendingEnabled
            ? "已开启：导航栏「剪贴板」右侧有「热榜」。热榜本身不需要 Token，只有 Star 按钮需要。"
            : "未开启：不发请求、导航栏也没有「热榜」项。";

        // 屏幕画布：只回灌开关与那份只读键位一览。副作用在回灌期间抑制——
        // "进一趟设置页就把画布的热键全撤了"是最离谱的一种副作用。
        _suppressCanvasApply = true;
        CanvasEnabled = Safe(_settings.LoadCanvasEnabled, true, "屏幕画布");
        CanvasInScreenshots = Safe(_settings.LoadCanvasInScreenshots, true, "截图带画布");
        _suppressCanvasApply = false;
        CanvasHotkeySheet = BuildCanvasHotkeySheet();
        CanvasStatus = BuildCanvasStatus();

        // RSS 总开关：回灌当前判定值（从没表过态时按"有没有启用的源"算，见 RssActivation）。
        _suppressRssApply = true;
        RssEnabled = Safe(_settings.LoadRssEnabled, false, "RSS 订阅");
        _suppressRssApply = false;
        RssStatus = RssEnabled
            ? "已开启：导航栏有「RSS」这一栏，每个订阅源是它自己的一个文件夹。这一栏只管来源地址。"
            : "未开启：导航栏没有「RSS」项，也不会去抓任何地址。添加一个启用中的来源就会自动开启。";
    }

    /// <summary>
    /// 源列表改动后重算一次"这一栏算不算开着"，并<b>立刻</b>把导航栏跟上。
    /// <para>为什么必须由源列表的改动来调它：从没表过态的用户（升级来的、或一个源都没删过的）
    /// 按「添加」之后，导航栏就该出现「RSS」了——若还等他去翻那个开关，就是凭空多出来的一步。</para>
    /// <para>用户<b>明确关过</b>时这里不会擅自替他打开：<c>LoadRssEnabled</c> 在有表态时一律以表态为准。</para>
    /// </summary>
    public void RefreshRssEnabled()
    {
        _suppressRssApply = true;
        RssEnabled = _settings.LoadRssEnabled();
        _suppressRssApply = false;
        App.MainWindow?.ApplyRssNavVisibility(RssEnabled);
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

    /// <summary>
    /// 「剪贴板历史」总开关（默认关）。翻位即持久化 + <b>立刻</b>启停监听窗口，
    /// 并把"到底有没有开始记录"照实写进状态行——开成功与开失败必须区分得出来（P-53/P-54 同口径）。
    /// </summary>
    [ObservableProperty] private bool _clipboardHistoryEnabled;

    [ObservableProperty] private string _clipboardHistoryStatus = string.Empty;

    /// <summary>LoadFromStore 回灌初值期间抑制启停动作，免得每次进设置页就凭默认值建/拆监听窗口。</summary>
    private bool _suppressClipboardApply;

    partial void OnClipboardHistoryEnabledChanged(bool value)
    {
        if (_suppressClipboardApply) return;
        ApplyClipboardHistorySwitch(value);
    }

    private void ApplyClipboardHistorySwitch(bool enabled)
    {
        _settings.SaveClipboardHistoryEnabled(enabled);
        var collecting = App.ApplyClipboardHistory(enabled);
        ClipboardHistoryStatus = enabled
            ? collecting
                ? "已开始记录。密码管理器复制的内容、以及私钥 / 登录令牌 / 银行卡号形态一律不入库；"
                  + "想临时停一下，去「剪贴板」页点「暂停记录」。"
                : "开关已打开，但系统剪贴板监听窗口没建起来（原因见日志）——当前仍不会记录任何内容。"
            : "已停止记录。之前存下的历史仍在「剪贴板」页，可在那里一键清空。";
        ClipboardImageStatus = ClipboardImageStatusText(collecting, ClipboardImageEnabled);
    }

    // ===== 剪贴板图片（批次 ClipIMG-P1-1d）=====

    /// <summary>
    /// 图片采集分开关，<b>默认关</b>（决议 §4）。它与总开关是两件事：
    /// 总开关管"读不读剪贴板"，这一颗管"要不要把屏幕上的像素写成 PNG 留在机器上"。
    /// </summary>
    [ObservableProperty] private bool _clipboardImageEnabled;

    /// <summary>图片条数上限（NumberBox 的 Value 是 double，落盘时才取整并夹住）。</summary>
    [ObservableProperty] private double _clipboardImageMaxValue;

    /// <summary>文本条数上限（默认 500＝改之前的写死值，这一格只是把它变成看得见的东西）。</summary>
    [ObservableProperty] private double _clipboardTextMaxValue;

    [ObservableProperty] private string _clipboardImageStatus = string.Empty;

    /// <summary>当前图片目录的实际占用 + 按上限的预估（决议 §3-Q1 第三层：让用户"按需配置"有数字可依）。</summary>
    [ObservableProperty] private string _clipboardUsage = string.Empty;

    /// <summary>
    /// 「清理孤儿文件」能不能按。<b>只有真扫出东西才允许按</b>：扫不出东西时灰着，而旁边那行占用文字
    /// 就是它灰着的理由（"另有 N 个文件不在历史里"要么出现要么不出现，不会出现"灰着却看不出为什么"）。
    /// </summary>
    [ObservableProperty] private bool _canCleanClipboardAssets;

    /// <summary>清理那一步的结果行：删了几件、腾出多少、还有几件删不掉。<b>不许写成一句"清理完成"</b>。</summary>
    [ObservableProperty] private string _clipboardCleanupStatus = string.Empty;

    /// <summary>
    /// 手动导出是否带上剪贴板图片本体（决议 §4：<b>默认开</b>）。
    /// 它只改"下一次导出"的载体（<c>.json</c> ↔ <c>.zip</c>），不改采集、不改已有备份文件，
    /// 所以这条改动除了落盘不需要推给任何运行中的东西。
    /// </summary>
    [ObservableProperty] private bool _backupClipboardImagesEnabled;

    /// <summary>回灌初值期间不许落盘/推参数，否则每次进设置页都把默认值写回用户设置里。</summary>
    private bool _suppressClipboardImageApply;

    /// <summary>
    /// 图片那一段的状态行。<b>总开关与分开关的四种组合各有不同事实要说</b>，
    /// 尤其"分开关开着但总开关关着"这一格——用户会以为图片正在被记录，而其实一条都没存。
    /// </summary>
    private static string ClipboardImageStatusText(bool collecting, bool imageOn) => !imageOn
        ? "图片采集未开启：只记录文本与文件列表。"
        : !collecting
            ? "图片采集已打开，但剪贴板历史总开关没开（或监听没建立）——现在一条图片都不会记录。"
            : "已开启：复制到的图片会存成 PNG，并预生成 160px 缩略图。"
              + "单张超过 20 MB 或短边小于 16px 的不收；密码管理器在前台时一律不收。";

    partial void OnClipboardImageEnabledChanged(bool value)
    {
        if (_suppressClipboardImageApply) return;
        _settings.SaveClipboardImageEnabled(value);
        App.RefreshClipboardLimits();
        ClipboardImageStatus = ClipboardImageStatusText(App.IsClipboardCollecting, value);
    }

    partial void OnClipboardImageMaxValueChanged(double value)
    {
        if (_suppressClipboardImageApply) return;
        QueueClipboardLimitsSave();
    }

    partial void OnClipboardTextMaxValueChanged(double value)
    {
        if (_suppressClipboardImageApply) return;
        QueueClipboardLimitsSave();
    }

    partial void OnBackupClipboardImagesEnabledChanged(bool value)
    {
        if (_suppressClipboardImageApply) return;
        _settings.SaveBackupClipboardImagesEnabled(value);
    }

    /// <summary>两个数字框的合并窗口。400ms 是"手停下"的量级，不是"等一轮刷新"的量级。</summary>
    private const int ClipboardLimitsMergeMs = 400;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _clipLimitsTimer;

    /// <summary>
    /// 上限改动<b>延后一小段再落盘并推给采集器</b>。
    /// <para><c>NumberBox</c> 每敲一位数字都会变更：立刻生效的话，"想把 200 改成 2000"这件事的中间态
    /// 是 2 → 20 → 200 → 2000，而第一下就会被夹到 10 并推给采集器——下一次复制就会按 10 条轮转，
    /// 用户为了<b>调高</b>上限反而丢了一批历史。合并窗口同时也把"敲四次数字写四次整档"收成一次。</para>
    /// <para>主窗没有队列时（设置页刚关、进程在收尾）直接落，<b>不许把改动吞在等不到的定时器里</b>。</para>
    /// </summary>
    private void QueueClipboardLimitsSave()
    {
        var queue = App.MainWindow?.DispatcherQueue;
        if (queue is null)
        {
            ApplyClipboardLimitsSave();
            return;
        }

        if (_clipLimitsTimer is null)
        {
            _clipLimitsTimer = queue.CreateTimer();
            _clipLimitsTimer.Interval = TimeSpan.FromMilliseconds(ClipboardLimitsMergeMs);
            _clipLimitsTimer.IsRepeating = false;
            _clipLimitsTimer.Tick += (_, _) =>
            {
                _clipLimitsTimer!.Stop();
                ApplyClipboardLimitsSave();
            };
        }
        _clipLimitsTimer.Stop();
        _clipLimitsTimer.Start();
    }

    private void ApplyClipboardLimitsSave()
    {
        _settings.SaveClipboardMaxEntries((int)ClipboardImageMaxValue, (int)ClipboardTextMaxValue);
        App.RefreshClipboardLimits();
        ClipboardImageStatus = ClipboardImageStatusText(App.IsClipboardCollecting, ClipboardImageEnabled);
    }

    /// <summary>「重新统计」按钮：改动上限或清过历史之后，数字要能就地更新，而不是让人重开设置页。</summary>
    [RelayCommand]
    private void RefreshClipboardUsage() => ComputeClipboardUsage();

    /// <summary>
    /// 算一次目录占用。<b>放在池线程</b>：几百张图时的 stat 落到 UI 线程上，
    /// 症状就是"一打开设置页卡半秒"，而这一页本来已经有好几处要读盘。
    /// <para>顺带扫一次"图有行无"（§3-Q6 的第二类）：决议要的是<b>只数不删 + 数出来的看得见的</b>，
    /// 所以这一句直接接在占用那行后面，而旁边那颗按钮就是它的出口。</para>
    /// </summary>
    private void ComputeClipboardUsage()
    {
        ClipboardUsage = "正在统计图片目录…";
        _ = Task.Run(async () =>
        {
            string text;
            bool canClean;
            try
            {
                var scan = await Task.Run(() => ClipboardAssetAudit.ScanAsync(ClipboardRepo()));
                var files = scan?.Files ?? await Task.Run(StarMark.Integrations.Clipboard.ClipboardImageStore.ListFiles);
                var footprint = StarMark.Abstractions.Clipboard.ClipAssets.Summarize(files, out var tempBytes);
                text = StarMark.Abstractions.Clipboard.ClipAssets.DescribeUsage(footprint, tempBytes)
                     + " " + StarMark.Abstractions.Clipboard.ClipAssets
                         .DescribeProjection(footprint, _settings.LoadClipboardImageMaxEntries())
                     + " 文件就放在：" + StarMark.Abstractions.Clipboard.ClipAssets.Folder;
                canClean = false;
                if (scan is { } s)
                {
                    var (orphanBytes, _) = ClipboardAssetAudit.BytesOf(s);
                    var orphanSentence = StarMark.Abstractions.Clipboard.ClipAssets
                        .DescribeOrphans(s.Result.OrphanNames.Count, orphanBytes);
                    if (orphanSentence.Length > 0) text += " " + orphanSentence;
                    canClean = s.Result.OrphanNames.Count + s.Result.TempNames.Count > 0;
                }
                else
                {
                    // 扫不出差集时既不点亮按钮，也不写成"其实一个都没有"——那两种都会把用户引向一次错误的删除决定。
                    text += " 但没能核对哪些文件已不属于历史（对账没跑成，原因见日志），所以清理按钮暂时不可用。";
                }
            }
            catch (Exception ex)
            {
                StarLog.Error("统计剪贴板图片占用失败", ex);
                text = $"没能算出图片占用（{ex.Message}）——数字缺失比编一个数好。";
                canClean = false;
            }
            App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
            {
                CanCleanClipboardAssets = canClean;      // 在 UI 线程那一侧回灌，池线程不碰绑定
                ClipboardUsage = text;
            });
        });
    }

    /// <summary>
    /// 对账与清理都要问仓储。参数less 构造（DI 之外）拿不到注入的那一个，就地从容器取——
    /// <b>这里不许回退成"当没有孤儿"</b>：那会把一句"可以清掉"挂在一个永远清不掉的数字旁边。
    /// </summary>
    private StarMark.Abstractions.IItemRepository ClipboardRepo()
        => _repository ?? App.Services.GetRequiredService<StarMark.Abstractions.IItemRepository>();

    /// <summary>
    /// 「清理孤儿文件」：<b>先扫、再列给用户看、点了确认才删</b>（§3-Q6 明写"不静默删用户目录"）。
    /// <para>预览用的那份名单<b>不交给删除那一步</b>——<c>CleanAsync</c> 自己重扫一次：中间可能又有
    /// 一次"同图再复制"把某个名字认领回去，按旧名单删就是删活文件。</para>
    /// <para>结果必须分开说：删掉几件、腾出多少、还有几件删不掉（占用）——一句"清理完成"是不够的，
    /// 那 K 件确实还在那里。</para>
    /// </summary>
    [RelayCommand]
    private async Task CleanClipboardAssetsAsync()
    {
        try
        {
            var scan = await Task.Run(() => ClipboardAssetAudit.ScanAsync(ClipboardRepo()));
            if (scan is null)
            {
                ClipboardCleanupStatus = "没能核对目录与历史的差（原因见日志），一件都没动。";
                return;
            }
            var names = scan.Value.Result.OrphanNames;
            var temps = scan.Value.Result.TempNames;
            if (names.Count + temps.Count == 0)
            {
                ClipboardCleanupStatus = "现在没有需要清理的文件。";
                return;
            }

            var (orphanBytes, tempBytes) = ClipboardAssetAudit.BytesOf(scan.Value);
            var body = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = StarMark.Abstractions.Clipboard.ClipAssets
                    .CleanupConfirmBody(names, orphanBytes, temps.Count, tempBytes,
                        StarMark.Abstractions.Clipboard.ClipAssets.Folder),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
            var total = names.Count + temps.Count;
            var choice = await CenteredDialog.ShowContentAsync(
                $"清理 {total} 个不用的文件", body, owner: App.MainWindow,
                dedupeKey: "clip-asset-cleanup", width: 520, height: 420,
                primaryText: $"删掉这 {total} 个文件", cancelText: "先不删");
            if (choice != CenteredDialog.HostedDialogResult.Committed)
            {
                ClipboardCleanupStatus = "没删任何东西。";
                return;
            }

            var outcome = await Task.Run(() => ClipboardAssetAudit.CleanAsync(ClipboardRepo()));
            if (outcome is null)
            {
                ClipboardCleanupStatus = "清理没跑成（原因见日志），已删掉的部分不会回头补删——可以再点一次。";
                ComputeClipboardUsage();
                return;
            }
            var o = outcome.Value;
            ClipboardCleanupStatus = o.Deleted + o.TempDeleted == 0
                ? $"一件都没删掉：{o.Failed + o.TempFailed} 个文件正被别的程序占用。它们还在名单里，可以稍后再点一次。"
                : o.Failed + o.TempFailed == 0
                    ? $"已删掉 {o.Deleted} 个不属于历史的文件"
                      + (o.TempDeleted > 0 ? $"与 {o.TempDeleted} 个临时件" : "")
                      + $"，腾出约 {StarMark.Abstractions.Clipboard.ClipAssets.DescribeBytes(o.DeletedBytes + o.TempDeletedBytes)}。"
                    : $"已删掉 {o.Deleted + o.TempDeleted} 个，但还有 {o.Failed + o.TempFailed} 个正被占用没删掉——它们仍在名单里，可以稍后再点一次。";
            ComputeClipboardUsage();
        }
        catch (Exception ex)
        {
            StarLog.Error("清理剪贴板图片孤儿文件失败", ex);
            ClipboardCleanupStatus = $"清理失败：{ex.Message}";
        }
    }

    // ===== 护眼 / 休息提醒（批次 WA）=====

    /// <summary>
    /// 护眼总开关，<b>默认关</b>：不请自来的遮罩是最讨人嫌的一种"帮忙"。
    /// 翻位即起停那张节拍表（与剪贴板开关同一口径：回报<b>实际</b>在不在跑，而不是"用户点了开"）。
    /// </summary>
    [ObservableProperty] private bool _eyeRestEnabled;

    /// <summary>间隔档位在 <see cref="StarMark.Core.Health.EyeRestPolicy.IntervalOptions"/> 里的下标（是档位不是滑杆）。</summary>
    [ObservableProperty] private int _eyeRestIntervalIndex;

    /// <summary>强制模式：盖一层 20 秒暗幕（按规格 Esc 不跳过）。关＝只发托盘气泡。</summary>
    [ObservableProperty] private bool _eyeRestEnforced;

    /// <summary>前台是全屏应用时让路（放 PPT / 放映不被砸）。默认开。</summary>
    [ObservableProperty] private bool _eyeRestDeferOnFullscreen;

    [ObservableProperty] private string _eyeRestStatus = string.Empty;

    /// <summary>「试一试」的结果回报（属性名含 Status＝不进自动保存，见 <c>IsDisplayOnlyProperty</c>）。</summary>
    [ObservableProperty] private string _eyeRestPreviewStatus = string.Empty;

    /// <summary>下拉的档位文案，与 <see cref="EyeRestIntervalIndex"/> 同序（一处事实：档位与文案都在 Core）。</summary>
    public IReadOnlyList<string> EyeRestIntervalOptions => StarMark.Core.Health.EyeRestPolicy.IntervalLabels;

    private int EyeRestIntervalMinutes => StarMark.Core.Health.EyeRestPolicy.IntervalAt(EyeRestIntervalIndex);

    private bool _suppressEyeRestApply;

    partial void OnEyeRestEnabledChanged(bool value)
    {
        if (_suppressEyeRestApply) return;
        ApplyEyeRestSwitch();
    }

    partial void OnEyeRestIntervalIndexChanged(int value)
    {
        if (_suppressEyeRestApply) return;
        ApplyEyeRestSwitch();
    }

    partial void OnEyeRestEnforcedChanged(bool value)
    {
        if (_suppressEyeRestApply) return;
        ApplyEyeRestSwitch();
    }

    partial void OnEyeRestDeferOnFullscreenChanged(bool value)
    {
        if (_suppressEyeRestApply) return;
        ApplyEyeRestSwitch();
    }

    private void ApplyEyeRestSwitch()
    {
        _settings.SaveEyeRest(EyeRestEnabled, EyeRestIntervalMinutes, EyeRestEnforced, EyeRestDeferOnFullscreen);
        App.ApplyEyeRest(EyeRestEnabled);
        EyeRestStatus = BuildEyeRestStatus();
        // 设置一变，上一次"试一试"演出来的就已经不是当前配置了：留着那行会读成"刚验证过现在的设置"
        EyeRestPreviewStatus = string.Empty;
    }

    /// <summary>
    /// 状态行说三件事：怎么提醒、全屏时让不让路、下一次大约几点。
    /// "开着但表没挂上"必须自己承认——用户没法用眼睛验证一张定时器在不在跑。
    /// </summary>
    private string BuildEyeRestStatus()
    {
        if (!EyeRestEnabled)
            return "已关闭：不建定时器、不探测前台窗口，屏幕上不会出现任何东西。";
        var how = EyeRestEnforced
            ? $"连续工作约 {EyeRestIntervalMinutes} 分钟后盖一层 20 秒暗幕（倒数期间 Esc 与点击都不能提前跳过）"
            : $"连续工作约 {EyeRestIntervalMinutes} 分钟后发一条托盘气泡";
        var defer = EyeRestDeferOnFullscreen ? "；前台是全屏应用（放 PPT / 放映）时自己让路" : "；全屏应用下也照常提醒";
        var running = StarMark.UI.Services.EyeRestService.IsRunning;
        var next = running && StarMark.UI.Services.EyeRestService.NextDueAt is { } due
            ? $"下一次大约 {due:HH:mm}。"
            : string.Empty;
        var warning = running ? string.Empty : "开关是开着的，但节拍表没挂上（原因见日志）——当前不会提醒。";
        return $"{how}{defer}。{next}{warning}";
    }

    /// <summary>
    /// 「试一试」：按<b>当前设置</b>原样演一次。没有这条出口，用户只能等满间隔才知道自己配的到底是什么
    /// 效果——而"等 15 分钟验证一个开关"等于没给验证路径。演的内容不动节拍（见 <c>EyeRestService.Preview</c>）。
    /// </summary>
    [RelayCommand]
    private void PreviewEyeRest()
    {
        var did = StarMark.UI.Services.EyeRestService.Preview();
        EyeRestPreviewStatus = did switch
        {
            true when StarMark.UI.Services.EyeRestService.IsResting
                => "已演一次：20 秒暗幕盖屏，倒数期间按 Esc、点鼠标都不能提前跳过。",
            true => "已演一次：发了一条提醒（托盘在跑走气泡，否则走主窗提示条）。",
            false when StarMark.UI.Services.EyeRestService.IsResting
                => "幕布还盖着屏，等这一轮倒数完再按。",
            false => "没演成：护眼开关没打开时不建节拍表，也就没有可演的东西（先开启本卡片顶部的开关）。",
        };
    }

    // ===== RSS 订阅（批次 RB）=====

    /// <summary>
    /// RSS 总开关。翻位即持久化并<b>立刻</b>控制导航栏「RSS」项——"关掉之后项还在"就是没做到位，
    /// 也不需要重启（与 <see cref="OnTrendingEnabledChanged"/> 同一口径）。
    /// </summary>
    [ObservableProperty] private bool _rssEnabled;

    [ObservableProperty] private string _rssStatus = string.Empty;

    /// <summary>LoadFromStore 回灌初值期间抑制副作用（否则每次进设置页都会重设一次导航可见性）。</summary>
    private bool _suppressRssApply;

    partial void OnRssEnabledChanged(bool value)
    {
        if (_suppressRssApply) return;
        _settings.SaveRssEnabled(value);
        App.MainWindow?.ApplyRssNavVisibility(value);
        RssStatus = value
            ? "已开启：导航栏现在有「RSS」这一栏，每个订阅源是它自己的一个文件夹。抓取只在你按「刷新」时发生。"
            : "已关闭：导航栏的「RSS」项已移除，不再发起任何抓取。已收藏进库的条目不受影响（它们已经是普通书签了）。";
    }

    // ===== GitHub 热榜（批次 KG）=====

    /// <summary>
    /// 热榜总开关（<b>默认关</b>）。翻位即持久化并<b>立刻</b>控制导航栏「热榜」项的可见性——
    /// 用户裁决"关着就不显示"，那么"关掉了项还在"就是没做到位；也不需要重启。
    /// </summary>
    [ObservableProperty] private bool _trendingEnabled;

    /// <summary>是否在「今日速览」显示热榜块（开启热榜时弹窗问过，这里随时可改）。</summary>
    [ObservableProperty] private bool _trendingGlanceEnabled;

    [ObservableProperty] private string _trendingStatus = string.Empty;

    /// <summary>LoadFromStore 回灌初值期间抑制副作用（否则每次进设置页都重设可见性、甚至弹一次询问框）。</summary>
    private bool _suppressTrendingApply;

    // ────────── 屏幕画布（批次 WD-5：总开关 + 键位只读一览）──────────

    /// <summary>
    /// 画布总开关（<b>默认开</b>）。关掉之后三件事同时发生：托盘里那一项整条消失、画布内九条快捷键
    /// 不再注册（不替一个关掉的功能继续占着系统的 Ctrl+Alt+字母）、再按键位只会听见一句原因。
    /// <para>
    /// 正在画的时候关掉会<b>立刻收掉那块玻璃</b>——"我已经关了，屏幕上还压着一层吃鼠标的东西"是这条链
    /// 最坏的收尾（与护眼 Stop 立刻收幕同一口径）。
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _canvasEnabled = true;

    /// <summary>「截图带画布」（默认开＝与这条设置出现之前的行为一致：笔迹会进截图）。</summary>
    [ObservableProperty] private bool _canvasInScreenshots = true;

    /// <summary>开关当前含义的一句话（看得见"关掉会发生什么"，不用猜）。</summary>
    [ObservableProperty] private string _canvasStatus = string.Empty;

    /// <summary>十条画布动作与它们<b>当前真实绑定</b>的键位，一行一条——生成，不写死。</summary>
    [ObservableProperty] private string _canvasHotkeySheet = string.Empty;

    private bool _suppressCanvasApply;

    partial void OnCanvasEnabledChanged(bool value)
    {
        if (_suppressCanvasApply) return;
        _settings.SaveCanvasEnabled(value);
        if (!value) StarMark.UI.Services.CanvasService.Stop();
        // 注册表当场跟着改：不重启、也不要用户再去点一次「保存快捷键」（多余的步骤算缺陷）
        App.MainWindow?.ApplyTraySettings();
        CanvasStatus = BuildCanvasStatus();
    }

    /// <summary>
    /// 截图带不带画布。<b>只管"抓哪一帧时玻璃上不上屏"，不当擦笔迹的橡皮擦</b>：
    /// 关掉之后画布照旧显示、笔迹照旧留着，只是别人截走的图里没有它。
    /// </summary>
    partial void OnCanvasInScreenshotsChanged(bool value)
    {
        if (_suppressCanvasApply) return;
        _settings.SaveCanvasInScreenshots(value);
    }

    /// <summary>键位改了之后重算这一览（「保存快捷键」与「重试注册」两条路都调它，否则这里会显示旧键位）。</summary>
    public void RefreshCanvasHotkeySheet()
    {
        CanvasHotkeySheet = BuildCanvasHotkeySheet();
        CanvasStatus = BuildCanvasStatus();
    }

    private string BuildCanvasStatus()
    {
        var open = HotkeyText(HotkeyActions.CanvasToggle);
        return CanvasEnabled
            ? $"已开启：按 {open} 进入画布（进去是穿透态，下层应用照常操作，画布不会吃掉鼠标）。" +
              "画布上的工具条有那颗「⌨」，随时能把下面这张表原样调出来。"
            : $"已关闭：托盘里不再有「屏幕画布」，画布内那九条快捷键也不再占用系统组合键；" +
              $"按 {open} 只会提示一句“要先在设置里打开”，不会静默。";
    }

    private string BuildCanvasHotkeySheet()
        => string.Join("\n", HotkeyActions.Canvas.Select(a => $"{HotkeyActions.DisplayName(a)}　{HotkeyText(a)}"));

    /// <summary>一条动作当前的键位文本（与画布那块面板同一个出处：设置页显示的与真生效的是同一份）。</summary>
    private string HotkeyText(string action)
    {
        var gesture = _settings.GetHotkeyBindings().GetValueOrDefault(action);
        return gesture is { IsEmpty: false } bound ? HotkeyDisplay.Display(bound) : "未绑定";
    }

    partial void OnTrendingEnabledChanged(bool value)
    {
        if (_suppressTrendingApply) return;
        _settings.SaveTrendingEnabled(value);
        App.MainWindow?.ApplyTrendingNavVisibility(value);
        TrendingStatus = value
            ? "已开启：导航栏「剪贴板」右侧现在有「热榜」。热榜本身不需要 Token（读侧匿名），只有 Star 按钮需要。"
            : "已关闭：导航栏的「热榜」项已移除，不再发起任何抓取；已缓存的那份留在本机，重新开启时直接用。";
        if (value) _ = AskTrendingGlanceAsync();
    }

    partial void OnTrendingGlanceEnabledChanged(bool value)
    {
        if (_suppressTrendingApply) return;
        _settings.SaveTrendingGlanceEnabled(value);
        TrendingStatus = value
            ? "「今日速览」已加上热榜块（默认日榜），原「常看」排在它下面。"
            : "「今日速览」不再显示热榜块；导航栏的「热榜」页不受影响。";
        // 组件即时跟上：这条广播就是各组件"数据变了重载一次"的既有通道，不必等重启、也不新写一套通知。
        StarMark.Abstractions.DataChangeHub.Notify();
    }

    /// <summary>
    /// 开启热榜时征询一次"要不要在今日速览里也显示"（用户裁决 §5.1）。
    /// <para>刻意不阻塞开关本身：开关先落地、弹窗只是追加决定第二块，弹窗若抛错也不能把开关状态卡住。</para>
    /// </summary>
    private async System.Threading.Tasks.Task AskTrendingGlanceAsync()
    {
        try
        {
            if (App.MainWindow is not { } owner) return;
            if (_settings.LoadTrendingGlanceEnabled()) return;      // 之前已答过"是"就不再问
            var yes = await Helpers.CenteredDialog.ConfirmAsync(
                "要在今日速览里显示 GitHub 热榜吗？",
                "组件里会多一块热榜（默认日榜），原「常看」排到它的下面。" +
                "选「不显示」也没关系：导航栏的「热榜」页照常可用，这里随时能改。",
                primaryText: "显示", cancelText: "不显示", owner: owner, dedupeKey: "trending-glance-ask");
            if (!yes) return;
            TrendingGlanceEnabled = true;
        }
        catch (Exception ex) { StarLog.Error("热榜「今日速览」询问弹窗失败（开关已生效，可在设置里手动勾选）", ex); }
    }

    // ===== 本地文件索引（P0-1b）=====
    [ObservableProperty] private string _fileIndexRootsText = string.Empty;
    [ObservableProperty] private string _maxFileIndexCountText = string.Empty;

    /// <summary>
    /// 「已配置但当前不可用」的索引目录说明（空＝全部可用，控件据此隐藏）。
    /// 过去这类目录是被 <c>Directory.Exists</c> 静默剔除的：用户看到的文本框少了一行、
    /// 下次保存就永久没了，而搜索结果少了一批文件却无任何解释（P-56）。
    /// </summary>
    [ObservableProperty] private string _fileIndexRootsStatus = string.Empty;

    /// <summary>有无"当前不可用目录"要提示（XAML 用现成的 BoolToVisibility 控制该行的显示）。</summary>
    public bool HasFileIndexRootsWarning => !string.IsNullOrEmpty(FileIndexRootsStatus);

    partial void OnFileIndexRootsStatusChanged(string value) => OnPropertyChanged(nameof(HasFileIndexRootsWarning));

    private void RefreshFileIndexRootsStatus()
    {
        var unavailable = Safe(_settings.UnavailableFileIndexRoots, Array.Empty<string>(), "索引目录可用性");
        FileIndexRootsStatus = unavailable.Count == 0
            ? string.Empty
            : $"以下索引目录当前不可用（不存在 / 盘未插 / 无权限），配置已保留、恢复后无需重填，但暂时不会索引进库：{string.Join("、", unavailable)}";
    }

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
            CanRetryLocalDiskPrepare = false;
            LocalDiskSearchStatus = "已关闭：不再搜索本地文件（已安装的 Everything 不受影响）。";
            return;
        }

        // 开启：持久化 + 实时翻位单例（IsAvailable 实读）。
        options.Enabled = true;
        _settings.SaveLocalDiskSearchEnabled(true);
        PrepareLocalDiskSearch();
    }

    /// <summary>
    /// 「开关已开但本地搜索实际不通」时才出现的重试按钮（P-54）。
    /// </summary>
    [ObservableProperty] private bool _canRetryLocalDiskPrepare;

    /// <summary>按钮文字随会话权限而变：提权会话缺的是 Everything 起来，普通会话缺的是那一次 UAC。</summary>
    public string LocalDiskSearchRetryLabel => Privilege.IsElevated() ? "重试准备 Everything" : "重试提权重启";

    /// <summary>
    /// 开启本地磁盘搜索的准备流程：已提权就地拉起 Everything，未提权先请求提权重启（A 方案：
    /// 让新实例与以管理员运行的 Everything 同 IL，WM_COPYDATA IPC 不再被 UIPI 拦）。
    /// <para>
    /// 之所以从开关回调里抽出来：失败分支原先的文案是「请确认 Everything 正在运行后稍候再试」
    /// 「请用开始菜单『以管理员身份运行』重启 StarMark」——把程序自己能做的动作写成给用户布置的作业。
    /// 抽成方法后「重试」按钮点的就是这同一段代码，不留第二条需要人照做的路（P-54）。
    /// </para>
    /// </summary>
    private void PrepareLocalDiskSearch()
    {
        CanRetryLocalDiskPrepare = false;
        OnPropertyChanged(nameof(LocalDiskSearchRetryLabel));

        if (Privilege.IsElevated())
        {
            LocalDiskSearchStatus = "已开启（管理员）：正在准备 Everything（起 SDK / 拉起客户端）…";
            _ = Task.Run(async () =>
            {
                string status;
                bool needRetry;
                try
                {
                    var src = App.Services.GetRequiredService<StarMark.Integrations.Everything.EverythingSource>();
                    var outcome = await src.EnableAsync(CancellationToken.None);
                    needRetry = outcome != StarMark.Integrations.Everything.EverythingSource.LocalDiskSearchEnableOutcome.Ready;
                    status = needRetry
                        ? "已开启，但 Everything 尚未就绪（客户端没起来，或它正以另一种权限运行）。"
                        : "已开启：本地文件将参与全盘搜索（快捷搜索 / 主窗即时生效）。";
                }
                catch (Exception ex)
                {
                    StarLog.Error("本地磁盘搜索：准备 Everything 失败", ex);
                    needRetry = true;
                    status = $"已开启，但准备 Everything 失败：{ex.Message}";
                }
                App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
                {
                    LocalDiskSearchStatus = status;
                    CanRetryLocalDiskPrepare = needRetry;
                });
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
        LocalDiskSearchStatus = "已开启，但提权重启没有发生（UAC 被取消或被安全软件拦下），本地文件可能搜不到。";
        CanRetryLocalDiskPrepare = true;
    }

    [RelayCommand]
    private void RetryLocalDiskPrepare() => PrepareLocalDiskSearch();

    /// <summary>
    /// 进设置页时的初始状态文本。<b>只探测、不拉起</b>——拉起是开关与启动流程的职责，
    /// 不能因为用户打开设置页就在后台起一个 Everything 进程。
    /// <para>但"开关开着"≠"本地文件真的能搜"：App 启动那一次准备失败时用户是完全看不见的，
    /// 旧文案却一律写"已开启：本地文件会出现在搜索结果中"，属假安全感。探到不通就照实说，
    /// 并把重试按钮摆出来（P-54）。</para>
    /// </summary>
    private void RefreshLocalDiskSearchState()
    {
        if (!LocalDiskSearchEnabled)
        {
            CanRetryLocalDiskPrepare = false;
            LocalDiskSearchStatus = "已关闭：开启后可像 Everything 一样全盘秒搜本地文件（首次会自动准备 Everything）。";
            return;
        }

        bool ready;
        try
        {
            ready = App.Services.GetRequiredService<StarMark.Integrations.Everything.EverythingSource>().IsAvailable;
        }
        catch (Exception ex)
        {
            StarLog.Warn($"探测 Everything 可用性失败（状态按未知展示）：{ex.Message}");
            CanRetryLocalDiskPrepare = false;
            LocalDiskSearchStatus = "已开启：本地文件会出现在快捷搜索 / 主窗搜索结果中。";
            return;
        }

        if (ready)
        {
            CanRetryLocalDiskPrepare = false;
            LocalDiskSearchStatus = "已开启：本地文件会出现在快捷搜索 / 主窗搜索结果中。";
            return;
        }

        CanRetryLocalDiskPrepare = true;
        LocalDiskSearchStatus = Privilege.IsElevated()
            ? "已开启，但 Everything 当前没在运行，本地文件还搜不到。"
            : "已开启，但 Everything 未连上：它可能正以管理员权限运行，而 StarMark 是普通权限。";
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

                // 存完立刻把凭据推到运行中的实例：否则"填了 Token 点了 Star 却报未配置"，
                // 用户只能重启应用——而重启是我们在别处已判定为缺陷的那种多余步骤。
                try
                {
                    var live = App.Services.GetRequiredService<StarMark.Integrations.GitHub.GitHubOptions>();
                    live.Token = github.Token;
                    live.Username = github.Username;
                    App.Services.GetRequiredService<StarMark.Integrations.GitHub.GitHubClient>().SyncCredentials();
                }
                catch (Exception gx)
                {
                    StarLog.Error("GitHub 凭据推送到运行实例失败（重启应用后仍会生效）", gx);
                }

                // 本地文件索引（P0-1b）：上限需为正整数。
                // P-56：这里不再用 Directory.Exists 过滤目录——文本框读的是同一份配置，过滤即等于
                // "盘没插就把那条根删了"，而且全程没有一句话。暂不可用的根由索引侧逐根跳过，
                // 并在下方 FileIndexRootsStatus 里如实列出。
                var roots = FileIndexRootsText
                    .Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
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

            // 保存完立刻按磁盘现状复核"哪几条根当前不可用"，让用户当场看到而不是下次才发现少搜了目录。
            RefreshFileIndexRootsStatus();

            // 写盘失败原先只进日志（P-53）：SaveCore 吞异常 ⇒ 界面表现为"已保存"，用户下次启动
            // 发现设置全回退。日志不是用户能看到的反馈面，故这里把"未落盘"当成保存失败呈现，
            // 也不再谎报已保存。本次会话内的即时应用照旧生效（改动已体现在内存与界面上）。
            var writeError = _settings.LastWriteError;
            if (writeError is null)
            {
                StarMark.Abstractions.StarLog.Info($"设置已保存（主题={theme}, 托盘={EnableTray}）");
                HasSaveError = false;
                SaveErrorMessage = string.Empty;
            }
            else
            {
                HasSaveError = true;
                SaveErrorMessage = $"设置未能写入磁盘，重启后会回到旧值：{writeError}";
            }

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

/// <summary>
/// 「本机备份」列表的一行：显示文本 + 按钮要用的原始盘点结果。
/// 类别/时间的中文口径留在这里（Core 只管判类，不管怎么显示），
/// 但<b>判类本身不在这里重复</b>——直接读 <see cref="BackupService.BackupFile.Kind"/>，
/// 免得界面自己再按文件名前缀猜一次而和清理逻辑分叉。
/// </summary>
public sealed class BackupRow
{
    public BackupRow(BackupService.BackupFile file) => File = file;

    /// <summary>整条记录（按钮的 Tag 直接绑它，恢复时不再按名字回查目录）。</summary>
    public BackupService.BackupFile File { get; }

    public string NameText => File.FileName;

    public string KindLabel => File.Kind switch
    {
        AutoBackupPolicy.BackupKind.Auto => "自动备份",
        AutoBackupPolicy.BackupKind.PreRestore => "恢复前快照",
        _ => "手动导出",
    };

    /// <summary>列表一律说本地时间——用户记的是"我昨天下午导过一次"，不是 UTC。</summary>
    public string WhenText => File.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string SizeText => FileSizeText.Human(File.LengthBytes);

    public string MetaText => $"{WhenText} · {KindLabel} · {SizeText}";

    /// <summary>快照那一份要说"用它回滚"：用户心里的动作是"撤销刚才那次导入"，不是"恢复一份备份"。</summary>
    public string RestoreLabel => File.Kind == AutoBackupPolicy.BackupKind.PreRestore ? "用它回滚" : "恢复这份";
}