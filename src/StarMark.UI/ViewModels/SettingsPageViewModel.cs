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
using StarMark.Abstractions.I18n;
using StarMark.Integrations.Clipboard;
using StarMark.Core.Appearance;
using StarMark.Core.Backup;
using StarMark.Core.Canvas;
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
    [ObservableProperty] private double _widgetOpacity = AppearanceSettingsPolicy.OpacityDefault;
    /// <summary>主窗口背景不透明度（独立于组件 <see cref="WidgetOpacity"/>）。批次 J 已拆材质，这里补齐拆不透明度。</summary>
    [ObservableProperty] private double _mainWindowOpacity = AppearanceSettingsPolicy.OpacityDefault;

    /// <summary>
    /// 三根滑杆的<b>量程与步进</b>，一律转发自 <see cref="AppearanceSettingsPolicy"/>（批次 SD，P-123 清单 #4）。
    /// <para>以前 XAML 自己写着 <c>0.3–1</c>／<c>4–64</c>／<c>0–40</c>，而仓储的 <c>Math.Clamp</c> 各写一遍、
    /// VM 的初值与读取兜底又各写一遍 ⇒ 同一个旋钮最多有五个主人（#175：SA 那批已经因为这种分岔真咬过一次）。
    /// 现在界面上写不出第二个量程（<c>Mode=OneTime</c> 绑这几个属性）。</para>
    /// </summary>
    public double OpacityFloor => AppearanceSettingsPolicy.OpacityFloor;
    public double OpacityCeiling => AppearanceSettingsPolicy.OpacityCeiling;
    public double OpacityStep => AppearanceSettingsPolicy.OpacityStep;

    /// <summary>磁吸强度（逻辑像素）滑杆的量程与步进。</summary>
    public int SnapStrengthFloor => AppearanceSettingsPolicy.SnapStrengthFloor;
    public int SnapStrengthCeiling => AppearanceSettingsPolicy.SnapStrengthCeiling;
    public int SnapStrengthStep => AppearanceSettingsPolicy.SnapStrengthStep;

    /// <summary>对齐间距（逻辑像素）滑杆的量程与步进。</summary>
    public int SnapSpacingFloor => AppearanceSettingsPolicy.SnapSpacingFloor;
    public int SnapSpacingCeiling => AppearanceSettingsPolicy.SnapSpacingCeiling;
    public int SnapSpacingStep => AppearanceSettingsPolicy.SnapSpacingStep;

    // 性能模式 / 内存门禁（Phase B-8）
    [ObservableProperty] private int _performanceModeIndex;
    // 初值与读取兜底都用判据那颗（批次 SA，P-123 第 3 条）：以前这里是 200/256 的第二份抄本
    [ObservableProperty] private double _cacheBudgetMb = PerformanceSettingsPolicy.BudgetMbDefault;
    [ObservableProperty] private int _maxCacheCount = PerformanceSettingsPolicy.CacheCountDefault;

    /// <summary>仅「自定义」性能模式显示预算 / 缓存上限控件。</summary>
    public bool CustomBudgetVisible => PerformanceModeIndex == (int)PerformanceMode.Custom;

    /// <summary>
    /// 两根滑杆的<b>范围与步进</b>，一律转发自 <see cref="PerformanceSettingsPolicy"/>。
    /// <para>XAML 以前自己写着 <c>Maximum="2048"</c> 与 <c>Maximum="1024"</c>，而仓储允许到 4096：
    /// 存档里一个合法且在生效的预算，在设置页<b>表达不出来</b>（滑杆指不到 2048 以上）。
    /// 范围现在只有一个主人，界面上再也写不出第二个上限（<c>Mode=OneTime</c> 绑这几个属性）。</para>
    /// </summary>
    public double BudgetMbFloor => PerformanceSettingsPolicy.BudgetMbFloor;

    /// <summary>进程内存预算上限（MB）。</summary>
    public double BudgetMbCeiling => PerformanceSettingsPolicy.BudgetMbCeiling;

    /// <summary>进程内存预算步进（MB）。</summary>
    public double BudgetMbStep => PerformanceSettingsPolicy.BudgetMbStep;

    /// <summary>缓存条目数下限。</summary>
    public int CacheCountFloor => PerformanceSettingsPolicy.CacheCountFloor;

    /// <summary>缓存条目数上限。</summary>
    public int CacheCountCeiling => PerformanceSettingsPolicy.CacheCountCeiling;

    /// <summary>缓存条目数步进。</summary>
    public int CacheCountStep => PerformanceSettingsPolicy.CacheCountStep;

    /// <summary>进程内存预算读数文本（滑块右侧）。</summary>
    public string CacheBudgetText => $"{CacheBudgetMb:0} MB";

    partial void OnPerformanceModeIndexChanged(int value) => OnPropertyChanged(nameof(CustomBudgetVisible));

    partial void OnCacheBudgetMbChanged(double value) => OnPropertyChanged(nameof(CacheBudgetText));

    /// <summary>组件边缘磁吸总开关（关闭后用户自由摆位）。</summary>
    [ObservableProperty] private bool _enableWidgetSnap = true;

    /// <summary>磁吸贴合后的目标间距（逻辑像素，0＝边边紧贴）。</summary>
    [ObservableProperty] private double _snapSpacing = AppearanceSettingsPolicy.SnapSpacingDefault;

    /// <summary>磁吸强度：距参照线多近即吸附（逻辑像素）。越大越容易吸上。</summary>
    [ObservableProperty] private double _snapStrength = AppearanceSettingsPolicy.SnapStrengthDefault;

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

    [ObservableProperty] private LocMessage? _saveErrorMessage;
    [ObservableProperty] private bool _hasSaveError;

    // 备份与恢复（P0-2）状态反馈。
    // ⚠ 这一颗从前是 `string`（批次 WI-62 起交身份）：它念的是 Core 供的那半句（`DeleteBackup`／`RestoreResult.Message`），
    // 只交 UI 这一半就是同一行半中半英——那两半在这一批一起进表（✅WI-61c 那颗跨层证人点名的正是这一行）。
    [ObservableProperty] private LocMessage? _backupStatus;
    [ObservableProperty] private bool _isBackupBusy;

    /// <summary>
    /// 本次会话是否以管理员权限运行。程序自己不再抬权限（P-108 改判：抬上去只会让拖进／拖出与系统对话框失灵），
    /// 所以这里为 true 只可能是用户手动"以管理员身份运行"的结果。
    /// 那种进程跨完整性调不到普通 IL 的系统文件对话框宿主 ⇒ 导出/导入的选择器稳定失败，
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

    public LocMessage? BackupsHeader => Backups.Count == 0
        ? LocMessage.Of("settings.data.backupsHeaderNone")
        : LocMessage.Of("settings.data.backupsHeaderCount", Backups.Count);

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
            BackupStatus = LocMessage.Of("settings.data.backup.listFailed", ex.Message);
        }
        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(BackupsHeader));
        // 「下一次大约什么时候」是按列表里最新的自动件算的：列表重扫了就要跟着重算，
        // 否则刚落盘的那份不会把状态行里的时间推后（导出/恢复/进页面三条路都走这里）。
        AutoBackupStatus = BuildAutoBackupStatus();
    }

    // ===== 自动备份排程（批次 BK）=====

    /// <summary>自动备份总开关。默认开＝加这颗开关之前的行为，关掉只是停止生成，不会删已有件。</summary>
    [ObservableProperty] private bool _autoBackupEnabled;

    /// <summary>间隔档位序号（持久化的是小时，界面只给序号）。</summary>
    [ObservableProperty] private int _autoBackupIntervalIndex;

    [ObservableProperty] private LocMessage? _autoBackupStatus;

    /// <summary>
    /// 下拉的档位（与 <see cref="AutoBackupIntervalIndex"/> 同序；一处事实：档位与键名都在 Core）。
    /// <para>批次 WI-62 起交 <see cref="LocOption"/> 而不是 Core 拼好的中文标签——闭着的那格显示的是选中那一刻
    /// 从 <c>Content</c> 抄走的快照，换语言时 <c>LocText</c> 只重贴"还活在可视树里、自己带着键"的元素（坑表 #272/#275，
    /// 护眼那一排在 ✅WI-37-B 就是这么改的）。<see cref="LocOption.Value"/> 交的是<b>小时数本身</b>⛔ 拿显示文字当身份。</para>
    /// </summary>
    public IReadOnlyList<LocOption> AutoBackupIntervalOptions { get; } =
        AutoBackupPolicy.IntervalOptions
            .Select((hours, index) => new LocOption(AutoBackupPolicy.IntervalKeys[index], string.Empty, hours))
            .ToList();

    private int AutoBackupIntervalHours => AutoBackupPolicy.IntervalAt(AutoBackupIntervalIndex);

    private bool _suppressAutoBackupApply;

    partial void OnAutoBackupEnabledChanged(bool value)
    {
        if (_suppressAutoBackupApply) return;
        ApplyAutoBackup();
    }

    partial void OnAutoBackupIntervalIndexChanged(int value)
    {
        if (_suppressAutoBackupApply) return;
        ApplyAutoBackup();
    }

    /// <summary>
    /// 落盘 + <b>当场</b>把巡查表按新设置重排。
    /// <para>间隔既然变成用户可调的东西，"改了要等下次启动才认"就等于一句隐藏的重启指令——
    /// 那正是本仓库反复定性的缺陷（与护眼、剪贴板、磁盘搜索那几条同一口径）。</para>
    /// </summary>
    private void ApplyAutoBackup()
    {
        _settings.SaveAutoBackup(AutoBackupEnabled, AutoBackupIntervalHours);
        AutoBackupScheduler.Start(_settings, App.Services.GetRequiredService<BackupService>());
        AutoBackupStatus = BuildAutoBackupStatus();
    }

    /// <summary>
    /// 状态行：开着就说清"多久一份、能回溯多久、下一次大约什么时候"，关着就说清代价。
    /// <para>保留份数仍是写死的 7，而间隔可调 ⇒ "最长能回到多久以前"跟着设置变（每 6 小时≈不到两天，
    /// 每 7 天≈七周）。不把这笔账算出来给用户看，他会在真需要旧备份的那天才发现回溯不了那么久。</para>
    /// </summary>
    private LocMessage? BuildAutoBackupStatus()
    {
        if (!AutoBackupEnabled)
            return LocMessage.Of("settings.data.autoBackupOff");
        var hours = AutoBackupIntervalHours;
        // 分钟数那颗读数原先靠插值串里的 `:0` 取整；句子的槽带不了格式串 ⇒ 交给 NumberText
        // （<1000 时与原读数逐字相同，✅P-122 那条"显示侧数字格式收一处"）。
        var probeMinutes = NumberText.Grouped(AutoBackupScheduler.ProbePeriod.TotalMinutes);
        var how = LocMessage.Of("settings.data.autoBackupHow", hours, AutoBackupPolicy.Keep,
            AutoBackupPolicy.MaxLookbackDays(hours));
        var newestAuto = Backups.FirstOrDefault(r => r.File.Kind == AutoBackupPolicy.BackupKind.Auto)?.File.ModifiedUtc;
        if (newestAuto is null)
            return Two(how, LocMessage.Of("settings.data.autoBackupNoAuto", probeMinutes));
        var nextDue = newestAuto.Value.AddHours(hours);
        return nextDue > DateTimeOffset.Now
            ? Two(how, LocMessage.Of("settings.data.autoBackupNext", DateTimeText.MonthDayMinute(nextDue)))
            : Two(how, LocMessage.Of("settings.data.autoBackupDue", probeMinutes));
    }

    /// <summary>「前一句 ＋ 后一句」这一形状也进表（`settings.data.autoBackupSentence`＝`{0}{1}`）：
    /// 两句话各自带自己的语言，代码里⛔ 再拼中文字符（✅WI-60b 那颗 `widget.mon.note.join` 同一口径）。</summary>
    private static LocMessage Two(LocMessage head, LocMessage tail)
        => LocMessage.Of("settings.data.autoBackupSentence", head, tail);

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
        WidgetOpacity = Safe(_settings.LoadWidgetOpacity, AppearanceSettingsPolicy.OpacityDefault, "不透明度");
        MainWindowOpacity = Safe(_settings.LoadMainWindowOpacity, AppearanceSettingsPolicy.OpacityDefault, "主窗口不透明度");
        EnableWidgetSnap = Safe(_settings.LoadWidgetSnapEnabled, true, "边缘磁吸");
        SnapSpacing = Safe(_settings.LoadWidgetSnapSpacing, AppearanceSettingsPolicy.SnapSpacingDefault, "磁吸间距");
        SnapStrength = Safe(_settings.LoadWidgetSnapStrength, AppearanceSettingsPolicy.SnapStrengthDefault, "磁吸强度");

        // 性能模式 / 内存门禁
        PerformanceModeIndex = (int)Safe(_settings.LoadPerformanceMode, PerformanceMode.Balanced, "性能模式");
        CacheBudgetMb = Safe(_settings.LoadCacheBudgetMb, PerformanceSettingsPolicy.BudgetMbDefault, "缓存预算");
        MaxCacheCount = Safe(_settings.LoadMaxImageCacheCount, PerformanceSettingsPolicy.CacheCountDefault, "缓存上限");

        // 「索引进库」的目录与上限输入格已在批次 VQ 随该功能一起拆掉（库里剩下的文件行只可能是用户自己登记的）。

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
            ? LocMessage.Of("settings.data.clipHistoryOff")
            : App.IsClipboardCollecting
                ? LocMessage.Of("settings.data.clipHistoryRecording")
                : LocMessage.Of("settings.data.clipHistoryWatchMissing");

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
        EyeRestNoticeIndex = StarMark.Core.Health.EyeRestPolicy.NoticeIndexOf(
            Safe(_settings.LoadEyeRestNotice, StarMark.Core.Health.EyeRestPolicy.DefaultNotice, "护眼提醒形式"));
        EyeRestDeferOnFullscreen = Safe(_settings.LoadEyeRestDeferOnFullscreen, true, "护眼全屏让路");
        _suppressEyeRestApply = false;
        EyeRestStatus = BuildEyeRestStatus();

        // 自动备份：回灌开关与间隔档位，副作用在回灌期间抑制——否则每次进设置页都把巡查定时器
        // 收掉再起（与护眼那条同理）。状态行在这里先算一版，紧接着的 RefreshBackups 会用真实列表再算一版。
        _suppressAutoBackupApply = true;
        AutoBackupEnabled = Safe(_settings.LoadAutoBackupEnabled, true, "自动备份");
        AutoBackupIntervalIndex = AutoBackupPolicy.IntervalIndexOf(
            Safe(_settings.LoadAutoBackupIntervalHours, AutoBackupPolicy.DefaultIntervalHours, "自动备份间隔"));
        _suppressAutoBackupApply = false;
        AutoBackupStatus = BuildAutoBackupStatus();

        // GitHub 热榜：回灌两个开关的当前值（副作用同样在回灌期间抑制）。
        _suppressTrendingApply = true;
        TrendingEnabled = Safe(_settings.LoadTrendingEnabled, false, "GitHub 热榜");
        TrendingGlanceEnabled = Safe(_settings.LoadTrendingGlanceEnabled, false, "热榜·今日速览");
        _suppressTrendingApply = false;
        // 这一对与开关回调那一对原先各写一句措辞（「右侧有」vs「右侧现在有」、
         // 「不需要 Token」vs「不需要 Token（读侧匿名）」）⇒ 同一行静默换措辞。现在两处同一颗键。
        TrendingStatus = TrendingEnabled
            ? LocMessage.Of("settings.extend.trendingOn")
            : LocMessage.Of("settings.extend.trendingOff");

        // 屏幕画布：只回灌开关与那份只读键位一览。副作用在回灌期间抑制——
        // "进一趟设置页就把画布的热键全撤了"是最离谱的一种副作用。
        _suppressCanvasApply = true;
        CanvasEnabled = Safe(_settings.LoadCanvasEnabled, true, "屏幕画布");
        CanvasInScreenshots = Safe(_settings.LoadCanvasInScreenshots, true, "截图带画布");
        CursorCircleRadiusDip = Safe(_settings.LoadCursorCircleRadiusDip, CursorCircle.DefaultRadiusDip, "光标圆半径");
        _suppressCanvasApply = false;

        // 截屏总开关（批次 RP，发起人点名"截屏功能总开关放入拓展功能设置页"）：只回灌那一位，
        // 副作用（写盘、重建托盘、重注册热键）在回灌期间抑制——否则进一趟设置页就把全局热键撤了再起一遍。
        // 排在下面那次刷新<b>之前</b>：那一行状态文字要按刚回灌的值算，先刷后回灌就会显示成默认档的说法。
        _suppressCaptureApply = true;
        CaptureEnabled = Safe(_settings.LoadCaptureEnabled, true, "截屏功能");
        _suppressCaptureApply = false;

        // 两张状态行 + 那份只读键位一览都从当前绑定现生成（回灌也要生成一次，否则第一次进设置页会看到空表）
        RefreshCanvasHotkeySheet();

        // RSS 总开关：回灌当前判定值（从没表过态时按"有没有启用的源"算，见 RssActivation）。
        _suppressRssApply = true;
        RssEnabled = Safe(_settings.LoadRssEnabled, false, "RSS 订阅");
        _suppressRssApply = false;
        RssStatus = RssEnabled
            ? LocMessage.Of("settings.extend.rssOnStatus")
            : LocMessage.Of("settings.extend.rssOffStatus");

        // 关于与更新（批次 UE）：只回灌那颗开关 + 上一次检查留下的那一句。
        // 这一屏打开时一个请求都不发——否则"想看一眼版本"就变成每进一次设置页上一次网。
        BackfillUpdateState();
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

                // 「索引进库」的目录与上限（旧 FileIndexRoots / MaxFileIndexCount）已在批次 VQ 随该功能一起拆掉：
                // 那颗按钮只落盘配置，真进库要等顶栏「同步」，而四条失败出口全都无声。
            }

            // 写盘失败原先只进日志（P-53）：SaveCore 吞异常 ⇒ 界面表现为"已保存"，用户下次启动
            // 发现设置全回退。日志不是用户能看到的反馈面，故这里把"未落盘"当成保存失败呈现，
            // 也不再谎报已保存。本次会话内的即时应用照旧生效（改动已体现在内存与界面上）。
            var writeError = _settings.LastWriteError;
            if (writeError is null)
            {
                StarMark.Abstractions.StarLog.Info($"设置已保存（主题={theme}, 托盘={EnableTray}）");
                HasSaveError = false;
                SaveErrorMessage = null;
            }
            else
            {
                HasSaveError = true;
                SaveErrorMessage = LocMessage.Of("settings.data.saveNotWritten", writeError);
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
            SaveErrorMessage = LocMessage.Of("settings.data.saveFailed", ex.Message);
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
/// 「本机备份」列表的一行：行身份（原始盘点结果）＋<b>没取词的</b>「键」＋显示侧现算的那几句。
/// <para>Core 只管判类、不管怎么显示，而<b>判类本身不在这里重复</b>——直接读 <see cref="BackupService.BackupFile.Kind"/>，
/// 免得界面自己再按文件名前缀猜一次而和清理逻辑分叉。</para>
/// <para>这一行住在 <c>DataTemplate</c> 上，走的是 ✅WI-40-3b 那条机制 (C)：行持 <see cref="LocMessage"/>，
/// <c>KindLabel</c>／<c>RestoreLabel</c>／<c>MetaText</c> 三颗 getter <b>当场念</b>，模板绑 <c>Mode=OneWay</c>，
/// 换语言那一刻由设置页逐行 <see cref="Repaste"/>。⛔ 在这里把成品串存进字段——那等于把语言冻在算出它的那一刻（P-175 缺口②）。</para>
/// </summary>
public sealed class BackupRow : ObservableObject
{
    public BackupRow(BackupService.BackupFile file)
    {
        File = file;
        // 键按身份挑（⛔ 在这里念），所以换语言时同一行只要重念一次就换成那一门语言的说法。
        KindMessage = file.Kind switch
        {
            AutoBackupPolicy.BackupKind.Auto => LocMessage.Of("settings.data.backup.kindAuto"),
            AutoBackupPolicy.BackupKind.PreRestore => LocMessage.Of("settings.data.backup.kindPreRestore"),
            _ => LocMessage.Of("settings.data.backup.kindManual"),
        };
        // 三元⛔ 写进 Of(…) 里：闸门按字面键扫调用点，包进条件里就看不见那两颗键各被问过几次。
        RestoreMessage = file.Kind == AutoBackupPolicy.BackupKind.PreRestore
            ? LocMessage.Of("settings.data.backup.rollback")
            : LocMessage.Of("settings.data.backup.restore");
    }

    /// <summary>整条记录（按钮的 Tag 直接绑它，恢复时不再按名字回查目录）。</summary>
    public BackupService.BackupFile File { get; }

    public string NameText => File.FileName;

    /// <summary>这一行是哪一类备份——交的是键，念在 <see cref="KindLabel"/>。</summary>
    public LocMessage KindMessage { get; }

    /// <summary>那颗按钮叫什么——快照说"用它回滚"：用户心里的动作是"撤销刚才那次导入"，不是"恢复一份备份"。</summary>
    public LocMessage RestoreMessage { get; }

    public string KindLabel => KindMessage.Render();

    /// <summary>列表一律说本地时间——用户记的是"我昨天下午导过一次"，不是 UTC。</summary>
    public string WhenText => DateTimeText.Minute(File.ModifiedUtc.ToLocalTime());

    public string SizeText => FileSizeText.Human(File.LengthBytes);

    public string MetaText => $"{WhenText} · {KindLabel} · {SizeText}";

    public string RestoreLabel => RestoreMessage.Render();

    /// <summary>换语言那一拍由宿主叫这一次：三颗显示属性都重念（⛔ 行自己订阅 <c>Locale.Changed</c>，坑表 #386）。</summary>
    public void Repaste()
    {
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(RestoreLabel));
        OnPropertyChanged(nameof(MetaText));
    }

    /// <summary>
    /// 「删除」确认框里那句代价说明——按类别分开说，因为三种类失去之后的后果完全不同：
    /// 自动件还会再生成，手动件与恢复前快照都不会。
    /// <para>写成静态判据（而不是行内字符串）是让确认框与列表用同一份说法：按钮的 <c>Tag</c> 绑的是
    /// <see cref="BackupService.BackupFile"/>，拿不到行对象，两边各写一份就一定会有分岔。</para>
    /// </summary>
    public static LocMessage DeleteWarningFor(AutoBackupPolicy.BackupKind kind) => kind switch
    {
        AutoBackupPolicy.BackupKind.Auto => LocMessage.Of("settings.data.deleteWarnAuto"),
        AutoBackupPolicy.BackupKind.PreRestore => LocMessage.Of("settings.data.deleteWarnPreRestore"),
        _ => LocMessage.Of("settings.data.deleteWarnManual"),
    };

    public LocMessage DeleteWarning => DeleteWarningFor(File.Kind);
}