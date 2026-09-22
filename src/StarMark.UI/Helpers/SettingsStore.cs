#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions;
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
        /// <summary>快捷键绑定（动作 id → 手势）的 JSON。缺省时使用 <see cref="DefaultHotkeyBindings"/>。</summary>
        public string? HotkeyBindingsJson { get; set; }
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

    /// <summary>快捷键绑定：默认 + 已保存合并。未配置动作回退到默认手势（缺省为无）。</summary>
    public IReadOnlyDictionary<string, HotkeyGesture> GetHotkeyBindings()
    {
        var merged = DefaultHotkeyBindings();
        var d = Load();
        if (d?.HotkeyBindingsJson is { } json)
        {
            try
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, HotkeyGesture>>(json);
                if (saved is not null) foreach (var kv in saved) merged[kv.Key] = kv.Value;
            }
            catch { }
        }
        return merged;
    }

    /// <summary>保存快捷键绑定（动作 id → 手势）。</summary>
    public void SaveHotkeyBindings(IReadOnlyDictionary<string, HotkeyGesture> bindings)
    {
        var d = Load() ?? new SettingsData();
        d.HotkeyBindingsJson = JsonSerializer.Serialize(bindings);
        Save(d);
    }

    /// <summary>默认快捷键：主界面呼出/关闭 = Ctrl+Alt+Space（沿用原有全局热键）。</summary>
    public static Dictionary<string, HotkeyGesture> DefaultHotkeyBindings()
    {
        var m = new Dictionary<string, HotkeyGesture>
        {
            [HotkeyActions.MainToggle] = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, (uint)Windows.System.VirtualKey.Space),
        };
        return m;
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