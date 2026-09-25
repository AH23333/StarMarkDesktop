#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.Feed;
using StarMark.Core.Feed;
using StarMark.Abstractions.Trending;
using StarMark.Core.Hotkeys;
using StarMark.Core.Performance;
using StarMark.Integrations.Weather;

namespace StarMark.UI.Helpers;

/// <summary>主题偏好。</summary>
public enum ThemePreference
{
    Default = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>
/// 用户设置持久化（目前仅主题偏好）。
/// 存储位置：STARMARK_SETTINGS_PATH > 数据库同目录(开发期跟随 STARMARK_DB_PATH) > %APPDATA%\StarMark\settings.json。
/// </summary>
public sealed class SettingsStore : IPerformanceSettingsSource
{
    private readonly string _path;
    private sealed class SettingsData
    {
        public int Theme { get; set; }
        public bool? EnableTray { get; set; }
        public bool? EnableGlobalHotKey { get; set; }
        public bool? MinimizeToTray { get; set; }
        /// <summary>本地文件索引根目录（P0-1b）。null/空表示使用默认（桌面/下载/文档）。</summary>
        public List<string>? FileIndexRoots { get; set; }
        /// <summary>每目录索引数量上限（P0-1b）。≤0 表示使用默认 5000。</summary>
        public int? MaxFileIndexCount { get; set; }
        /// <summary>
        /// 本地磁盘搜索（全盘文件索引）总开关。默认关：轻度用户零打扰、且后台提权服务/Everything 绝不启动、不加载索引（0 内存）。
        /// 旧配置无此字段时按未开启处理。
        /// </summary>
        public bool? LocalDiskSearchEnabled { get; set; }
        /// <summary>
        /// 内置剪贴板历史总开关。<b>默认关</b>：剪贴板是全机器敏感度最高的数据（密码、卡号、私钥都会路过它），
        /// 默认开等于在用户不知情时把这些抄进一个明文 SQLite 文件。开启后采集全自动，无需任何后续步骤。
        /// </summary>
        public bool? ClipboardHistoryEnabled { get; set; }
        /// <summary>
        /// GitHub 热榜浏览面总开关（<b>默认关</b>）。关时不发任何请求，且导航栏连「热榜」项都不显示——
        /// 这是用户主动开启的可选浏览面，留一个点进去只会说"未开启"的项更像故障
        /// （与剪贴板"入口常驻"刻意相反，两边的理由都写在蓝图 §5）。
        /// </summary>
        public bool? TrendingEnabled { get; set; }
        /// <summary>热榜页上次的周期（daily/weekly/monthly）；缺省 weekly（同扩展口径）。</summary>
        public string? TrendingPeriod { get; set; }
        /// <summary>是否在「今日速览」组件里显示热榜块（默认关；开启热榜时弹窗问过，设置里随时可改）。</summary>
        public bool? TrendingGlanceEnabled { get; set; }
        /// <summary>快捷键绑定（动作 id → 手势）的 JSON。缺省时使用 <see cref="HotkeyBindings.Defaults"/>。</summary>
        public string? HotkeyBindingsJson { get; set; }
        /// <summary>
        /// 网址来源（RSS / Atom）列表的 JSON。<b>默认空表</b>：这是用户主动添加的可选浏览面（D4），
        /// 空表时不发任何请求。与"快捷键从没配过"不同，这里没有默认值可回落——源只有用户自己知道。
        /// </summary>
        public string? RssSourcesJson { get; set; }
        /// <summary>
        /// RSS 总开关（批次 RB：开启后导航栏出现「RSS」页）。<b>三态是有意的</b>：
        /// <c>null</c> ＝ 从没表过态（这一版之前只有源列表、没有这个键），<c>true/false</c> ＝ 用户按过开关。
        /// 判据在 <c>RssActivation.IsOn</c>——把"没表过态"读成"关过"，升级后那一栏就凭空不见了。
        /// </summary>
        public bool? RssEnabled { get; set; }
        /// <summary>组件拖动 / 缩放时的边缘磁吸总开关（默认开启）。关闭后用户可自由摆位。</summary>
        public bool? WidgetSnapEnabled { get; set; }
        /// <summary>磁吸对齐间距（物理像素，默认 8）：两组件贴合时保留的间隙。</summary>
        public int? WidgetSnapSpacing { get; set; }
        /// <summary>磁吸吸附强度→进入阈值（物理像素，默认 24）：越大越早吸附（更易吸、更难微调）。</summary>
        public int? WidgetSnapStrength { get; set; }
        /// <summary>半透明材质：0=亚克力 1=云母 2=不透明（默认 0）。</summary>
        public int? WidgetBackdrop { get; set; }
        /// <summary>组件背景不透明度 0.3–1.0（默认 0.72），配合半透明材质使用。</summary>
        public double? WidgetOpacity { get; set; }
        /// <summary>毛玻璃材质浓度 0–1（默认 0.65，DeskBox 的 WidgetMaterialIntensity）。</summary>
        public double? WidgetMaterialIntensity { get; set; }
        /// <summary>主窗口是否也使用同一套半透明材质（默认开启）。已被 <see cref="MainWindowBackdrop"/> 取代，仅为旧配置迁移保留。</summary>
        public bool? MainWindowTranslucent { get; set; }
        /// <summary>
        /// 主窗口背景材质（独立于组件的 <see cref="WidgetBackdrop"/>）。null 表示从未单独设过，
        /// 迁移语义见 <see cref="LoadMainWindowBackdrop"/>：沿用旧的「主窗口使用同一材质」开关。
        /// </summary>
        public int? MainWindowBackdrop { get; set; }
        /// <summary>主窗口背景不透明度（独立于组件的 <see cref="WidgetOpacity"/>）。null = 从未单设，
        /// 迁移语义见 <see cref="LoadMainWindowOpacity"/>：沿用组件不透明度，保证升级观感不变。</summary>
        public double? MainWindowOpacity { get; set; }
        /// <summary>性能模式：0=均衡（默认）1=省资源 2=自定义。常驻应用的内存/缓存预算开关。</summary>
        public int? PerformanceMode { get; set; }
        /// <summary>自定义性能模式下的进程工作集预算（MB，默认 200）。超预算时 MemoryReclaimer 触发回收。</summary>
        public double? CacheBudgetMb { get; set; }
        /// <summary>自定义性能模式下有界缓存的最大条目数（默认 256）。</summary>
        public int? MaxImageCacheCount { get; set; }
        /// <summary>天气组件所选城市（JSON 序列化的 WeatherCity）。null 表示未选，组件会提示先选城市。</summary>
        public string? WeatherCityJson { get; set; }
        /// <summary>
        /// 天气温度单位：0=摄氏（默认）1=华氏。存 <see cref="int"/> 而非枚举字符串——
        /// 设置 JSON 是用户可手改的，数字比枚举名更不容易写错（解析见 WeatherUnits.Parse）。
        /// </summary>
        public int? WeatherUnit { get; set; }
        /// <summary>天气视图：0=未来三天（默认）1=今日逐时。</summary>
        public int? WeatherView { get; set; }

        // ===== AI 助手（批次 A：Provider 抽象 + 连接自检）=====
        // 六个 key 全部带 Ai 前缀且各自独立。扩展项目出过一次"AI 域四个 key 同名"的事故：
        // 第一批分类结果落盘就把 AI 设置整个冲掉了，而两处写的是同一个字符串，看代码看不出来。

        /// <summary>AI 通道总开关。<b>默认关闭</b>（null 与 false 都算关）：这功能会把库里的标题/摘要发出去，
        /// 未明确开启前不得处于工作状态。</summary>
        public bool? AiEnabled { get; set; }

        /// <summary>0=Ollama（本机，默认）1=OpenAI 兼容端点。<b>序号即持久化值，只能加不能改</b>
        /// （解析与兜底见 <c>AiSettings</c>）。</summary>
        public int? AiProvider { get; set; }

        /// <summary>模型名，原样发给服务。<b>不预置默认值</b>：本机装的是哪个模型只有用户知道，
        /// 猜一个只会换来一次"模型名不对"。</summary>
        public string? AiModel { get; set; }

        /// <summary>API Key。<b>Ollama 不需要它</b>（免 Key 豁免写在 <c>AiSettings.Problem</c> 里，
        /// 是唯一一处判"能不能用"的地方）。</summary>
        public string? AiApiKey { get; set; }

        /// <summary>Ollama 地址。null＝用默认 <c>http://127.0.0.1:11434</c>。</summary>
        public string? AiOllamaBaseUrl { get; set; }

        /// <summary>OpenAI 兼容端点地址（填到 <c>/v1</c> 那一层）。null＝用默认。</summary>
        public string? AiBaseUrl { get; set; }

        /// <summary>一轮整理<strong>还没应用</strong>的方案（JSON）。写它是为了"关窗口/进程被杀也不丢已整理出来的"，
        /// 应用完或用户明确丢弃时清空。与 <c>Ai*</c> 那六个一样保持独立 key，谁也不覆盖谁。</summary>
        public string? AiPendingPlanJson { get; set; }

        /// <summary>
        /// 护眼 / 休息提醒总开关（<b>默认关</b>）：默认开等于在谁都没要求的时候往屏幕上盖一层遮罩，
        /// 那是"程序替用户决定什么时候该休息"。关时连节拍定时器都不建（不占表、不探前台窗口）。
        /// </summary>
        public bool? EyeRestEnabled { get; set; }

        /// <summary>连续工作多少分钟算该休息一次。非法值由 <c>EyeRestPolicy.ClampInterval</c> 回落默认，
        /// <b>不夹到边界</b>：夹到 5 等于把一处存档损坏放大成"每 5 分钟打断一次"。</summary>
        public int? EyeRestIntervalMinutes { get; set; }

        /// <summary>强制模式：全屏遮罩 + 20 秒倒数（按规格 Esc 不跳过，防形同虚设）。
        /// <b>默认关</b>——刚打开护眼就吃一次锁屏是惊吓；先气泡，想要锁再勾这条。</summary>
        public bool? EyeRestEnforced { get; set; }

        /// <summary>前台是全屏应用时让路（放 PPT / 放映 / 全屏游戏不被遮罩砸）。默认开。</summary>
        public bool? EyeRestDeferOnFullscreen { get; set; }
    }

    public SettingsStore(string? path = null) => _path = path ?? ResolveSettingsPath();

    // 快照缓存：避免每个 LoadXxx 都 ReadAllText+整档反序列化（开一次设置页曾达十余次磁盘读）。
    // 以「最后写入时间 + 文件长度」为键，用户手改 settings.json 时仍能廉价感知并自动失效；Save 后主动失效。
    private readonly object _cacheGate = new();
    private SettingsData? _cached;
    private bool _hasCache;
    private DateTime _cachedStampUtc;
    private long _cachedLength;

    private SettingsData? Load()
    {
        lock (_cacheGate)
        {
            if (_batchDepth > 0) return _batchData;   // 批量区间内：读写都走那份待落盘快照
            return LoadCore();
        }
    }

    private SettingsData? LoadCore()
    {
        try
        {
            if (!File.Exists(_path))
            {
                _hasCache = false;
                return null;
            }

            var fi = new FileInfo(_path);
            if (_hasCache && fi.LastWriteTimeUtc == _cachedStampUtc && fi.Length == _cachedLength)
                return _cached;

            var data = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(_path));
            _cached = data;
            _cachedStampUtc = fi.LastWriteTimeUtc;
            _cachedLength = fi.Length;
            _hasCache = true;
            return data;
        }
        catch (Exception ex)
        {
            // 解析失败绝不能静默：它会让所有设置回落默认、看起来像"首次运行"。
            // 记日志并保留坏文件（.bad）供排查/恢复；移走原文件后 File.Exists 变 false，天然去重不刷屏。
            StarLog.Error($"读取设置文件失败，回落默认设置：{_path}", ex);
            TryPreserveCorruptSettings();
            _hasCache = false;
            return null;
        }
    }

    private void TryPreserveCorruptSettings()
    {
        try
        {
            if (File.Exists(_path))
                File.Move(_path, _path + ".bad", overwrite: true);
        }
        catch { /* 尽力保留，失败不影响回落默认 */ }
    }

    private void Save(SettingsData data)
    {
        lock (_cacheGate)
        {
            if (_batchDepth > 0) { _batchData = data; return; }   // 区间内只攒，Dispose 时一次落盘
            SaveCore(data);
        }
    }

    private int _batchDepth;
    private SettingsData? _batchData;

    /// <summary>
    /// 批量保存区间：区间内每个 <c>SaveXxx</c> 只改内存快照，Dispose 时一次落盘。
    /// <para>
    /// 为什么需要：每个 SaveXxx 单看都是"一读一写"，而设置页一次保存串了 16 项 ⇒ 16 次整档读
    /// + 16 次原子替换（写临时文件再 Move），而拖滑杆会按 350 ms 自动保存的节奏反复走这一轮。
    /// 区间内的<b>读</b>也返回同一份待落盘快照，所以跨字段回落（主窗不透明度沿用组件值、主窗材质
    /// 沿用旧布尔）在批量中看到的与落盘后完全一致。
    /// </para>
    /// <para>用法约束：同一线程的同步段内 using（可嵌套，按深度计数）；不要在区间里 await。</para>
    /// </summary>
    public IDisposable BeginBatch()
    {
        lock (_cacheGate)
        {
            if (_batchDepth++ == 0) _batchData = LoadCore() ?? new SettingsData();
            return new BatchScope(this);
        }
    }

    private void EndBatch()
    {
        lock (_cacheGate)
        {
            if (--_batchDepth > 0) return;
            var pending = _batchData;
            _batchData = null;
            if (pending is not null) SaveCore(pending);
        }
    }

    private sealed class BatchScope(SettingsStore owner) : IDisposable
    {
        private SettingsStore? _owner = owner;

        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            owner?.EndBatch();   // 只放行外层那一次落盘；重复 Dispose 不重复计数
        }
    }

    private void SaveCore(SettingsData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // 先写临时文件、再原子重命名覆盖：避免写盘中途崩溃把 settings.json 截断成非法 JSON
            // （那会触发上面的"回落默认"路径，把用户全部设置静默清空）。
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data));
            File.Move(tmp, _path, overwrite: true);
            _hasCache = false; // 落盘后失效，下次 Load 读到最新 mtime/len
            _lastWriteError = null;
        }
        catch (Exception ex)
        {
            StarLog.Error($"写入设置文件失败：{_path}", ex);
            _lastWriteError = ex.Message;   // 与成功分支同在 _cacheGate 下（Save/EndBatch 已持锁）
        }
    }

    private string? _lastWriteError;

    /// <summary>
    /// 最近一次写盘的失败原因（写成功即复位为 null）。
    /// <para>
    /// 为什么要有它：写盘失败原先只进日志，而日志不是用户能看到的反馈面 ⇒ 设置页表现"已保存"，
    /// %APPDATA% 被同步盘占用、被设为只读、磁盘满时用户当场毫无察觉，下次启动全部回退（P-53）。
    /// 异常不外抛是对的（保存路径上有一串即时应用，不该因写盘失败中断），但失败必须回传到能显示它的地方。
    /// </para>
    /// </summary>
    public string? LastWriteError
    {
        get { lock (_cacheGate) return _lastWriteError; }
    }

    public ThemePreference LoadTheme() => Load() is { } d && Enum.IsDefined(typeof(ThemePreference), d.Theme)
        ? (ThemePreference)d.Theme
        : ThemePreference.Default;

    public void SaveTheme(ThemePreference pref)
    {
        var d = Load() ?? new SettingsData();
        d.Theme = (int)pref;
        Save(d);
    }

    /// <summary>托盘常驻总开关（默认开启）。</summary>
    public bool LoadEnableTray() => Load() is { } d ? d.EnableTray ?? true : true;

    public void SaveEnableTray(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.EnableTray = enabled;
        Save(d);
    }

    /// <summary>全局呼出热键开关（默认开启，Ctrl+Alt+Space）。</summary>
    public bool LoadEnableGlobalHotKey() => Load() is { } d ? d.EnableGlobalHotKey ?? true : true;

    public void SaveEnableGlobalHotKey(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.EnableGlobalHotKey = enabled;
        Save(d);
    }

    /// <summary>关闭按钮最小化到托盘（默认开启）。</summary>
    public bool LoadMinimizeToTray() => Load() is { } d ? d.MinimizeToTray ?? true : true;

    public void SaveMinimizeToTray(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.MinimizeToTray = enabled;
        Save(d);
    }

    /// <summary>组件边缘磁吸总开关（默认开启）。关闭后拖动/缩放都不再自动贴合。</summary>
    public bool LoadWidgetSnapEnabled() => Load() is { } d ? d.WidgetSnapEnabled ?? true : true;

    public void SaveWidgetSnapEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetSnapEnabled = enabled;
        Save(d);
    }

    /// <summary>磁吸对齐间距（逻辑像素，默认 8，范围 0–40）：两组件贴合时保留的间隙。</summary>
    public int LoadWidgetSnapSpacing()
        => Load() is { } d && d.WidgetSnapSpacing is { } v
            ? Math.Clamp(v, 0, 40)
            : StarMark.Core.Widgets.WidgetSnapCalculator.DefaultSpacing;

    public void SaveWidgetSnapSpacing(int px)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetSnapSpacing = Math.Clamp(px, 0, 40);
        Save(d);
    }

    /// <summary>磁吸吸附强度＝进入吸附阈值（逻辑像素，默认 24，范围 4–64）：越大越早吸附。</summary>
    public int LoadWidgetSnapStrength()
        => Load() is { } d && d.WidgetSnapStrength is { } v
            ? Math.Clamp(v, 4, 64)
            : StarMark.Core.Widgets.WidgetSnapCalculator.DefaultEngageThreshold;

    public void SaveWidgetSnapStrength(int px)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetSnapStrength = Math.Clamp(px, 4, 64);
        Save(d);
    }

    /// <summary>
    /// 组件窗口背景材质（默认亚克力）。
    /// 注意：旧版 settings.json 没有该字段（WidgetBackdrop 为 null），
    /// 此处必须先取值的空合并结果再校验枚举，绝不能直接对可空值取 .Value
    /// （历史 bug：先 `Enum.IsDefined(typeof(...), d.WidgetBackdrop ?? 0)` 通过后再 `d.WidgetBackdrop!.Value`
    /// 会对 null 解包，抛 "Nullable object must have a value"，导致设置页导航与组件显示直接崩溃）。
    /// </summary>
    public WidgetBackdropKind LoadWidgetBackdrop()
    {
        if (Load() is { } d && d.WidgetBackdrop is { } raw
            && Enum.IsDefined(typeof(WidgetBackdropKind), raw))
            return (WidgetBackdropKind)raw;
        return WidgetBackdropKind.Acrylic;
    }

    public void SaveWidgetBackdrop(WidgetBackdropKind kind)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetBackdrop = (int)kind;
        Save(d);
    }

    /// <summary>组件背景不透明度（默认 0.72）。越接近 1 越不透明。</summary>
    public double LoadWidgetOpacity()
    {
        if (Load() is { } d && d.WidgetOpacity is > 0)
            return Math.Clamp(d.WidgetOpacity.Value, 0.3, 1.0);
        return WidgetAppearance.DefaultOpacity;
    }

    public void SaveWidgetOpacity(double opacity)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetOpacity = Math.Clamp(opacity, 0.3, 1.0);
        Save(d);
    }

    /// <summary>主窗口背景不透明度（独立于组件）。从未单设则回落到组件不透明度（升级观感不变）。</summary>
    public double LoadMainWindowOpacity()
    {
        if (Load() is { } d && d.MainWindowOpacity is > 0)
            return Math.Clamp(d.MainWindowOpacity.Value, 0.3, 1.0);
        return LoadWidgetOpacity();
    }

    public void SaveMainWindowOpacity(double opacity)
    {
        var d = Load() ?? new SettingsData();
        d.MainWindowOpacity = Math.Clamp(opacity, 0.3, 1.0);
        Save(d);
    }

    /// <summary>毛玻璃材质浓度（默认 0.65）：控制亚克力的染色/光亮度，值越大越「实」。</summary>
    public double LoadWidgetMaterialIntensity()
    {
        if (Load() is { } d && d.WidgetMaterialIntensity is >= 0)
            return Math.Clamp(d.WidgetMaterialIntensity.Value, 0.0, 1.0);
        return 0.65;
    }

    public void SaveWidgetMaterialIntensity(double intensity)
    {
        var d = Load() ?? new SettingsData();
        d.WidgetMaterialIntensity = Math.Clamp(intensity, 0.0, 1.0);
        Save(d);
    }

    /// <summary>
    /// 天气组件所选城市。未选过（或 JSON 损坏 / 旧版无此字段）时返回 null，
    /// 组件据此显示「点此选择城市」。解析失败一律兜底为 null —— 设置文件是用户可手改的，
    /// 坏数据绝不能冒异常到 UI 线程。
    /// </summary>
    public WeatherCity? LoadWeatherCity()
    {
        var json = Load()?.WeatherCityJson;
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<WeatherCity>(json);
        }
        catch
        {
            return null;
        }
    }

    public void SaveWeatherCity(WeatherCity? city)
    {
        var d = Load() ?? new SettingsData();
        try
        {
            d.WeatherCityJson = city is null ? null : System.Text.Json.JsonSerializer.Serialize(city);
        }
        catch
        {
            d.WeatherCityJson = null;
        }
        Save(d);
    }

    /// <summary>天气温度单位（默认摄氏）。非法值一律回落摄氏。</summary>
    public WeatherUnit LoadWeatherUnit() => WeatherUnits.Parse(Load()?.WeatherUnit);

    public void SaveWeatherUnit(WeatherUnit unit)
    {
        var d = Load() ?? new SettingsData();
        d.WeatherUnit = (int)unit;
        Save(d);
    }

    /// <summary>
    /// 天气视图（默认未来三天）。非法值回落为 0。
    /// 注意「先 is 判断再取值」：旧版 settings.json 没有这个字段时为 null，
    /// 直接写 <c>d.WeatherView ?? 0</c> 再 <c>.Value</c> 会对 null 取值抛 InvalidOperationException。
    /// </summary>
    public WeatherForecastView LoadWeatherView()
    {
        if (Load()?.WeatherView is { } raw &&
            Enum.IsDefined(typeof(WeatherForecastView), raw))
            return (WeatherForecastView)raw;
        return WeatherForecastView.Daily;
    }

    public void SaveWeatherView(WeatherForecastView view)
    {
        var d = Load() ?? new SettingsData();
        d.WeatherView = (int)view;
        Save(d);
    }

    /// <summary>主窗口是否跟随同样的半透明材质（默认开启）。</summary>
    public bool LoadMainWindowTranslucent() => Load() is { } d ? d.MainWindowTranslucent ?? true : true;

    public void SaveMainWindowTranslucent(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.MainWindowTranslucent = enabled;
        Save(d);
    }

    /// <summary>
    /// 主窗口背景材质（独立于组件）。新配置直接读 <see cref="SettingsData.MainWindowBackdrop"/>；
    /// 旧配置（从未单设过主窗材质、只有「主窗口使用同一材质」布尔）按原语义迁移：
    /// 开关开 → 沿用组件材质，关 → 实色不透明（None）。这样升级后主窗口观感与升级前完全一致。
    /// </summary>
    public WidgetBackdropKind LoadMainWindowBackdrop()
    {
        if (Load() is { } d && d.MainWindowBackdrop is { } raw
            && Enum.IsDefined(typeof(WidgetBackdropKind), raw))
            return (WidgetBackdropKind)raw;
        return LoadMainWindowTranslucent() ? LoadWidgetBackdrop() : WidgetBackdropKind.None;
    }

    public void SaveMainWindowBackdrop(WidgetBackdropKind kind)
    {
        var d = Load() ?? new SettingsData();
        d.MainWindowBackdrop = (int)kind;
        // 同步写旧布尔，保证仍以 MainWindowTranslucent 读取的历史路径（若有）语义不漂移。
        d.MainWindowTranslucent = kind != WidgetBackdropKind.None;
        Save(d);
    }

    /// <summary>性能模式（默认均衡）。旧版 settings.json 无该字段时回退均衡。</summary>
    public PerformanceMode LoadPerformanceMode()
    {
        if (Load() is { } d && d.PerformanceMode is { } raw && Enum.IsDefined(typeof(PerformanceMode), raw))
            return (PerformanceMode)raw;
        return PerformanceMode.Balanced;
    }

    public void SavePerformanceMode(PerformanceMode mode)
    {
        var d = Load() ?? new SettingsData();
        d.PerformanceMode = (int)mode;
        Save(d);
    }

    /// <summary>自定义模式下的进程工作集预算（MB，默认 200）。</summary>
    public double LoadCacheBudgetMb()
    {
        if (Load() is { } d && d.CacheBudgetMb is > 0)
            return Math.Clamp(d.CacheBudgetMb.Value, 32.0, 4096.0);
        return 200.0;
    }

    public void SaveCacheBudgetMb(double mb)
    {
        var d = Load() ?? new SettingsData();
        d.CacheBudgetMb = Math.Clamp(mb, 32.0, 4096.0);
        Save(d);
    }

    /// <summary>自定义模式下的有界缓存最大条目数（默认 256）。</summary>
    public int LoadMaxImageCacheCount()
    {
        if (Load() is { } d && d.MaxImageCacheCount is > 0)
            return Math.Clamp(d.MaxImageCacheCount.Value, 16, 4096);
        return 256;
    }

    public void SaveMaxImageCacheCount(int count)
    {
        var d = Load() ?? new SettingsData();
        d.MaxImageCacheCount = Math.Clamp(count, 16, 4096);
        Save(d);
    }

    /// <summary>本地磁盘搜索总开关（默认关）。关闭时后台提权服务/Everything 不启动、不加载索引（0 内存），搜索也不含本地文件。</summary>
    public bool LoadLocalDiskSearchEnabled() => Load() is { } d && d.LocalDiskSearchEnabled == true;

    public void SaveLocalDiskSearchEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.LocalDiskSearchEnabled = enabled;
        Save(d);
    }

    /// <summary>
    /// 内置剪贴板历史总开关（<b>默认关</b>）。关闭时连监听窗口都不创建——不读剪贴板、不落盘。
    /// </summary>
    public bool LoadClipboardHistoryEnabled() => Load() is { } d && d.ClipboardHistoryEnabled == true;

    public void SaveClipboardHistoryEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.ClipboardHistoryEnabled = enabled;
        Save(d);
    }

    // ────────── 护眼 / 休息提醒（批次 WA）──────────

    /// <summary>总开关（<b>默认关</b>）。关时不建定时器、不探前台窗口、屏幕上不会出现任何遮罩。</summary>
    public bool LoadEyeRestEnabled() => Load() is { } d && d.EyeRestEnabled == true;

    /// <summary>间隔（分钟）。缺省与非法值都走 <c>EyeRestPolicy</c> 的回落，界面上看到的与真用到的是同一个数。</summary>
    public int LoadEyeRestIntervalMinutes()
        => StarMark.Core.Health.EyeRestPolicy.ClampInterval(
            Load() is { } d
                ? d.EyeRestIntervalMinutes ?? StarMark.Core.Health.EyeRestPolicy.DefaultIntervalMinutes
                : StarMark.Core.Health.EyeRestPolicy.DefaultIntervalMinutes);

    /// <summary>强制模式（<b>默认关</b>：刚开护眼就吃一次锁屏是惊吓）。</summary>
    public bool LoadEyeRestEnforced() => Load() is { } d && d.EyeRestEnforced == true;

    /// <summary>全屏让路（默认开）。</summary>
    public bool LoadEyeRestDeferOnFullscreen() => Load() is not { } d || d.EyeRestDeferOnFullscreen != false;

    /// <summary>
    /// 四项一次落盘：这四条说的是同一件事（怎么提醒），分开写就是"改了间隔但没改开关"这类半套状态的来源，
    /// 而且设置页的自动保存按 350 ms 节奏整档读写，一次写完比四次省（P-43 同一口径）。
    /// </summary>
    public void SaveEyeRest(bool enabled, int intervalMinutes, bool enforced, bool deferOnFullscreen)
    {
        var d = Load() ?? new SettingsData();
        d.EyeRestEnabled = enabled;
        d.EyeRestIntervalMinutes = StarMark.Core.Health.EyeRestPolicy.ClampInterval(intervalMinutes);
        d.EyeRestEnforced = enforced;
        d.EyeRestDeferOnFullscreen = deferOnFullscreen;
        Save(d);
    }

    // ===== GitHub 热榜（批次 KA→）=====

    /// <summary>热榜总开关（<b>默认关</b>）。关着时导航项不显示、页面也不发任何请求。</summary>
    public bool LoadTrendingEnabled() => Load() is { } d && d.TrendingEnabled == true;

    public void SaveTrendingEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.TrendingEnabled = enabled;
        Save(d);
    }

    /// <summary>
    /// 上次的周期。<b>存了个不认识的值 ⇒ 回落默认周榜</b>：设置文件可被手改、也可能来自更老的版本，
    /// 把异常值一路传下去只会得到一个"点开是空白"的页面。
    /// </summary>
    public TrendingPeriod LoadTrendingPeriod()
        => TrendingPeriods.TryParse(Load()?.TrendingPeriod, out var p)
            ? p : TrendingPeriod.Weekly;

    /// <summary>周期代码（daily/weekly/monthly），与缓存键、界面筛选共用同一套写法。</summary>
    public string LoadTrendingPeriodCode() => TrendingPeriods.Code(LoadTrendingPeriod());

    public void SaveTrendingPeriod(string code)
    {
        if (!TrendingPeriods.TryParse(code, out var period)) return;
        var d = Load() ?? new SettingsData();
        d.TrendingPeriod = TrendingPeriods.Code(period);
        Save(d);
    }

    /// <summary>「今日速览」是否显示热榜块（默认关——开启热榜功能时由弹窗征询，之后设置里可改）。</summary>
    public bool LoadTrendingGlanceEnabled() => Load() is { } d && d.TrendingGlanceEnabled == true;

    public void SaveTrendingGlanceEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.TrendingGlanceEnabled = enabled;
        Save(d);
    }

    // ===== 网址来源（RSS / Atom，批次 NF）=====

    /// <summary>
    /// 已配置的网址来源。<b>解析不出来时返回空表而不是抛</b>：设置文件可被手改、也可能来自更老的版本，
    /// 而"设置页整页打不开"比"这一栏看着像没配过"严重得多（坏档仍原样留在磁盘上，不会被这次保存悄悄覆盖掉——
    /// 只有用户真的改动源列表时才会重写这一栏）。
    /// </summary>
    public List<RssSourceConfig> LoadRssSources() => DeserializeRssSources(Load()?.RssSourcesJson);

    /// <summary>读侧只有一条解析路径：<see cref="LoadRssEnabled"/> 也要看同一份源列表，
    /// 各写一遍就会出现"开关判定与列表内容对不上"的那种错。</summary>
    private static List<RssSourceConfig> DeserializeRssSources(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<RssSourceConfig>();
        try
        {
            var list = JsonSerializer.Deserialize<List<RssSourceConfig>>(json) ?? new List<RssSourceConfig>();
            // 剩下的清洗（空地址、重复地址、id 撞车）搬去了 Core 的 RssSourceList.Normalize：UI 层测试引用不到，判据只能放在引得到的那一侧
            return RssSourceList.Normalize(list);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[Settings] 网址来源列表读不出，按未配置处理：{ex.Message}");
            return new List<RssSourceConfig>();
        }
    }

    /// <summary>
    /// RSS 这一栏算不算开着。<b>一次读档</b>同时取开关与源列表：分两次 <c>Load()</c> 的话，
    /// 中间有人改了设置文件，就会出现"开关按的是 A 列表，判的是 B 列表"。
    /// </summary>
    public bool LoadRssEnabled()
    {
        var d = Load();
        return RssActivation.IsOn(d?.RssEnabled, DeserializeRssSources(d?.RssSourcesJson));
    }

    /// <summary>写下用户的表态（此后"没表过态"那条兜底不再生效，见 <c>RssActivation</c>）。</summary>
    public void SaveRssEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.RssEnabled = enabled;
        Save(d);
    }

    public void SaveRssSources(IReadOnlyList<RssSourceConfig> sources)
    {
        var d = Load() ?? new SettingsData();
        d.RssSourcesJson = JsonSerializer.Serialize(sources);
        Save(d);
    }

    /// <summary>读出 AI 通道配置。<b>档位序号认不出来时退回默认而不是抛</b>：这一格是用户可手改的
    /// JSON 数字，写错一个数字不该让设置页打不开。</summary>
    public AiSettings LoadAiSettings()
    {
        var d = Load();
        var kind = d?.AiProvider is int raw && Enum.IsDefined(typeof(AiProviderKind), raw)
            ? (AiProviderKind)raw
            : AiProviderKind.Ollama;
        return new AiSettings(
            Enabled: d?.AiEnabled == true,
            Provider: kind,
            Model: d?.AiModel,
            ApiKey: d?.AiApiKey,
            OllamaBaseUrl: d?.AiOllamaBaseUrl,
            BaseUrl: d?.AiBaseUrl);
    }

    /// <summary>整组一次写入。<b>刻意不提供"只改一个字段"的写法</b>：这一组字段互相才有意义
    /// （通道换了，Key 与地址的必填性跟着变），分开写会出现"Ollama 却带着 https 校验"的中间态。</summary>
    public void SaveAiSettings(AiSettings settings)
    {
        var d = Load() ?? new SettingsData();
        d.AiEnabled = settings.Enabled;
        d.AiProvider = (int)settings.Provider;
        d.AiModel = string.IsNullOrWhiteSpace(settings.Model) ? null : settings.Model.Trim();
        d.AiApiKey = string.IsNullOrWhiteSpace(settings.ApiKey) ? null : settings.ApiKey.Trim();
        d.AiOllamaBaseUrl = string.IsNullOrWhiteSpace(settings.OllamaBaseUrl) ? null : settings.OllamaBaseUrl.Trim();
        d.AiBaseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl.Trim();
        Save(d);
    }

    /// <summary>读出还没应用的整理方案。读不出来按"没有方案"处理：<b>这一栏只是缓存性质，
    /// 坏了不该让设置页打不开</b>（真正的数据——条目与标签——都在库里，没写进来过）。</summary>
    public ClassifyPlan LoadAiPlan()
    {
        var json = Load()?.AiPendingPlanJson;
        if (string.IsNullOrWhiteSpace(json)) return ClassifyPlan.Empty;
        try
        {
            var plan = JsonSerializer.Deserialize<AiPlanRow>(json);
            if (plan is null) return ClassifyPlan.Empty;
            var proposals = new List<TagProposal>();
            foreach (var row in plan.Items ?? new List<AiProposalRow>())
            {
                if (row is not { Id: > 0 } || row.Tags is not { Count: > 0 } tags) continue;
                var clean = TagText.Sanitize(tags);          // 存档里的标签再过一次闸门：那是用户可以手改的文件
                if (clean.Count > 0) proposals.Add(new TagProposal(row.Id, clean));
            }
            return new ClassifyPlan(proposals, plan.At == default ? DateTimeOffset.UtcNow : plan.At);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[AI] 待应用的整理方案读不出，按没有方案处理：{ex.Message}");
            return ClassifyPlan.Empty;
        }
    }

    public void SaveAiPlan(ClassifyPlan plan)
    {
        var d = Load() ?? new SettingsData();
        if (plan.IsEmpty) d.AiPendingPlanJson = null;
        else d.AiPendingPlanJson = JsonSerializer.Serialize(new AiPlanRow
        {
            At = plan.CreatedAt,
            Items = plan.Proposals.Select(proposal => new AiProposalRow
            {
                Id = proposal.Id,
                Tags = proposal.Tags.ToList(),
            }).ToList(),
        });
        Save(d);
    }

    public void ClearAiPlan()
    {
        var d = Load() ?? new SettingsData();
        d.AiPendingPlanJson = null;
        Save(d);
    }

    /// <summary>方案的落盘形状。<b>不直接序列化 <see cref="ClassifyPlan"/> 本身</b>：那会让记录类型的
    /// 内部结构变成存档格式，将来给方案加一个字段就会读到旧档里的 null 集合。</summary>
    private sealed class AiPlanRow
    {
        public DateTimeOffset At { get; set; }
        public List<AiProposalRow>? Items { get; set; }
    }

    private sealed class AiProposalRow
    {
        public long Id { get; set; }
        public List<string>? Tags { get; set; }
    }

    /// <summary>
    /// 本地文件索引根目录（P0-1b）。未配置时返回默认（桌面/下载/文档中存在的目录）。
    /// <para>
    /// P-56：<b>这里不再按 <c>Directory.Exists</c> 过滤</b>。旧行为会在盘没插时把"移动盘/U 盘上的目录"
    /// 从返回结果里抹掉，而设置页那个多行文本框正是读这份结果再写回去的 ⇒ 用户在插回盘之前只要保存过
    /// 任意一项设置，那条根就永久消失了，全程零提示。暂不可用的目录由索引侧逐根跳过
    /// （<c>EverythingSource.FetchAsync</c> 早有 Exists 闸门），配置本身保持用户写下的原样。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> LoadFileIndexRoots()
    {
        var d = Load();
        if (d?.FileIndexRoots is { Count: > 0 } list)
            return NormalizeRoots(list);
        return DefaultFileIndexRoots();
    }

    /// <summary>
    /// 已配置但<b>当前</b>不可用的根目录（不存在 / 无权限探测）。设置页据此照实说明"这条暂时不索引进库"，
    /// 而不是像过去那样悄悄从列表里删掉它（P-56）。
    /// </summary>
    public IReadOnlyList<string> UnavailableFileIndexRoots()
        => LoadFileIndexRoots().Where(r => !IsDirectoryUsable(r)).ToList();

    private static bool IsDirectoryUsable(string path)
    {
        try { return Directory.Exists(path); }
        catch { return false; }   // 探测本身就抛（无权限、坏网络路径）同样按"当前不可用"处理
    }

    private static List<string> NormalizeRoots(IEnumerable<string> roots)
        => roots.Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                // Windows 路径大小写不敏感：OrdinalIgnoreCase 去重，否则 "d:\Docs" 与 "D:\Docs" 算两条。
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>保存用户写下的根目录：只做 trim / 去空行 / 去重，<b>不</b>以"当前是否存在"为准入门（见 <see cref="LoadFileIndexRoots"/>）。</summary>
    public void SaveFileIndexRoots(IReadOnlyList<string> roots)
    {
        var d = Load() ?? new SettingsData();
        d.FileIndexRoots = NormalizeRoots(roots);
        Save(d);
    }

    /// <summary>每目录索引数量上限（P0-1b）。未配置或非法时返回默认 5000。</summary>
    public int LoadMaxFileIndexCount()
        => Load() is { } d && d.MaxFileIndexCount is > 0 ? d.MaxFileIndexCount.Value : 5000;

    public void SaveMaxFileIndexCount(int count)
    {
        var d = Load() ?? new SettingsData();
        d.MaxFileIndexCount = count > 0 ? count : 5000;
        Save(d);
    }

    private static List<string> DefaultFileIndexRoots()
    {
        // P-50：下载目录以前写死 %USERPROFILE%\Downloads。开了 OneDrive「已知文件夹移动」的机器上
        // 真实下载目录是 …\OneDrive\Downloads ⇒ 该根恒不存在，被下面的 Exists 过滤静默剔除，
        // 用户表现为"下载里的文件永远搜不到"（桌面/文档走 GetFolderPath 会自动跟随，只有这条不会）。
        // 口径：.NET 没暴露 Downloads 这个已知文件夹（只有 Desktop/Documents 等），而 OneDrive
        // 「已知文件夹移动」就是把 Downloads 挪到用户 OneDrive 根下 ⇒ 显式收三种 OneDrive 形态
        // （个人版/企业版环境变量 + 配置文件目录下的 OneDrive）再兜老路径，全部按存在与否过滤。
        var candidates = new List<string>();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var oneDrive in new[] { "OneDrive", "OneDriveCommercial" })
        {
            var root = Environment.GetEnvironmentVariable(oneDrive);
            if (!string.IsNullOrWhiteSpace(root)) AddIfUsable(candidates, () => Path.Combine(root, "Downloads"));
        }
        AddIfUsable(candidates, () => Path.Combine(profile, "OneDrive", "Downloads"));
        AddIfUsable(candidates, () => Path.Combine(profile, "Downloads"));
        AddIfUsable(candidates, () => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddIfUsable(candidates, () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        return candidates;
    }

    private static void AddIfUsable(List<string> list, Func<string> pick)
    {
        string path;
        try { path = pick(); } catch { return; }        // 个别机器上已知文件夹解析会抛
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (Directory.Exists(path) && !list.Contains(path, StringComparer.OrdinalIgnoreCase)) list.Add(path); }
        catch { /* 无权限探测的目录（网络盘/重定向被拦）：跳过它，不影响其余根目录 */ }
    }

    /// <summary>
    /// 快捷键绑定：默认 + 已保存的合并结果。合并规则本身在 <see cref="HotkeyBindings"/>
    /// （Core 层纯函数，可单测）：已保存的每一项都覆盖默认，而"空手势"是用户显式清掉该动作的
    /// 记号 ⇒ 清除必须写成空手势而不是删键，否则会被默认值复活。
    /// </summary>
    public IReadOnlyDictionary<string, HotkeyGesture> GetHotkeyBindings()
    {
        IReadOnlyDictionary<string, HotkeyGesture>? saved = null;
        var d = Load();
        if (d?.HotkeyBindingsJson is { } json)
        {
            try { saved = JsonSerializer.Deserialize<Dictionary<string, HotkeyGesture>>(json); }
            catch { /* 绑定 JSON 损坏：整表按默认，坏一次设置不该让程序起不来 */ }
        }
        return HotkeyBindings.MergeWithDefaults(saved);
    }

    /// <summary>保存快捷键绑定（动作 id → 手势）。</summary>
    public void SaveHotkeyBindings(IReadOnlyDictionary<string, HotkeyGesture> bindings)
    {
        var d = Load() ?? new SettingsData();
        d.HotkeyBindingsJson = JsonSerializer.Serialize(bindings);
        Save(d);
    }

    public static string ResolveSettingsPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("STARMARK_SETTINGS_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        // 开发期跟随数据库路径所在目录，避免沙盒拒绝 %APPDATA% 写入
        var dbPath = Environment.GetEnvironmentVariable("STARMARK_DB_PATH");
        if (!string.IsNullOrWhiteSpace(dbPath))
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dir)) return Path.Combine(dir, "settings.json");
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, StarMark.Abstractions.AppConstants.AppName, "settings.json");
    }
}