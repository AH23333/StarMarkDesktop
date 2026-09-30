#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StarMark.Abstractions;

namespace StarMark.Core.Widgets;

/// <summary>桌面组件类型（对标 DeskBox：快捷启动/待办/随记/时钟/搜索 + 差异化条目组件）。</summary>
public enum WidgetKind
{
    QuickLaunch = 0, // 快捷启动：仅用户自定义快捷入口（置顶条目改由 Pinned 组件负责，批次 IX）
    Todo = 1,        // 待办
    QuickNote = 2,   // 随记
    Clock = 3,       // 时钟/日期
    Search = 4,      // 快捷搜索（唤起主窗口并搜索）
    TagGrid = 5,     // 标签：某标签条目常驻桌面（差异化护城河）
    Clipboard = 6,   // 剪贴板：本机复制历史常驻（前身是「搜索结果格」，与快捷搜索定位重叠 ⇒ 经用户裁决取代；wire 值 6 不变 ⇒ 老配置自动变成本组件）
    Activity = 7,    // 最近活动：用户主动增/删/改的事件流（#51），不是"最近更新的条目"
    Pinned = 8,      // 置顶条目：pinned=1 的条目
    Glance = 9,      // 今日速览（Glance）：日期 + 农历/节日 + 下一个节日倒计时 + 常看条目
    Weather = 10,    // 天气：Open-Meteo 实况 + 未来三天预报（免费无 Key）
    Music = 11,      // 音乐：Windows 系统媒体传输控制（SMTC）的播放控制与曲目显示
    Calc = 12,       // 计算器：表达式求值 + 单位换算 + 时间戳换算（离线静态表，无汇率）
    WorldClock = 13, // 世界时钟：多城市并列秒级刷新（桌面日历按裁决并入今日速览，不另立组件）
    Countdown = 14,  // 倒计时/纪念日：多条命名倒计时 + 每年重复 + 到点提醒
    Focus = 15,      // 番茄钟：可自定义时长的专注/休息循环，可带本轮任务名（第一版不做统计）
    SystemMonitor = 16, // 系统监控：CPU/内存/网速实时读数（仅在有可见实例时采样）
}

/// <summary>计算器历史带的一行：算式原文 + 当次算出的答案（答案只作显示，重新计算以引擎为准）。</summary>
public sealed class CalcHistoryItem
{
    public string Expression { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

/// <summary>待办条目。</summary>
public sealed class TodoItem
{
    public long Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool Done { get; set; }
    public long CreatedAt { get; set; }
}

/// <summary>随记条目。</summary>
public sealed class QuickNoteItem
{
    public long Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
}

/// <summary>快捷入口条目（用户在快捷启动格内手动添加 / 拖入 / 从卡片发送）。</summary>
public sealed class LinkItem
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Uri { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
}

/// <summary>单个组件窗口配置（位置/尺寸为物理像素，置顶为窗口状态）。</summary>
public sealed class WidgetConfig
{
    public double X { get; set; } = 120;
    public double Y { get; set; } = 120;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 360;

    /// <summary>窗口是否常驻最顶层（WS_EX_TOPMOST / HWND_TOPMOST）。</summary>
    public bool Topmost { get; set; }

    /// <summary>仅 v1 迁移用：旧版开关嵌在 Config 内。</summary>
    [JsonPropertyName("ShowOnStartup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyShowOnStartupInConfig { get; set; }
}

/// <summary>单个组件实例配置（v3 起按实例管理：同一类型可重复添加多个）。</summary>
public sealed class WidgetInstanceConfig
{
    /// <summary>实例唯一 ID（区分同类型的多个组件）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public WidgetKind Kind { get; set; }

    public double X { get; set; } = 120;
    public double Y { get; set; } = 120;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 360;

    /// <summary>窗口是否常驻最顶层（WS_EX_TOPMOST / HWND_TOPMOST）。</summary>
    public bool Topmost { get; set; }

    /// <summary>
    /// 外壳呈现模式：标准 / 收起为胶囊 / 隐藏外壳（Phase B 胶囊模式）。
    /// 旧实例缺此字段反序列化为 Standard，不影响其它类型。
    /// </summary>
    public WidgetChromeMode ChromeMode { get; set; } = WidgetChromeMode.Standard;

    /// <summary>
    /// 隐私模式（B-10）：收起为胶囊时隐藏标题，避免他人从胶囊窥见组件身份/内容。
    /// 仅影响 Compact 态显示，不改变功能与持久化。旧实例缺字段默认 false（零迁移）。
    /// </summary>
    public bool PrivacyMode { get; set; }

    // ── 每显示器拓扑布局（Phase B）：位置以 DIP 存为「所在显示器工作区左上角」的偏移，
    //    配合 MonitorDevice 在恢复时按该显示器当前 DPI 重新换算物理像素；
    //    MonitorDevice 为空（旧实例/降级路径）时回退物理像素 X/Y/Width/Height 并做越界回收。
    //    全部有默认值，旧 instances.json 反序列化缺字段零影响。 ──

    /// <summary>组件所在显示器的稳定设备名（如 \\.\DISPLAY1，来自 Win32 MONITORINFOEX.szDevice）。空串表示未记录。</summary>
    public string MonitorDevice { get; set; } = string.Empty;

    /// <summary>组件左上角相对所在显示器工作区左上角的 DIP 偏移（X）。</summary>
    public double MonitorLeft { get; set; }

    /// <summary>组件左上角相对所在显示器工作区左上角的 DIP 偏移（Y）。</summary>
    public double MonitorTop { get; set; }

    /// <summary>组件宽度（DIP）。</summary>
    public double MonitorWidth { get; set; }

    /// <summary>
    /// 用户自定义的组件名（右键「重命名…」设置），显示在标题栏与胶囊标题上。
    /// 为 null / 空白时回退到组件类型的默认标题。旧实例缺该字段反序列化为 null，向后兼容、无需迁移。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    /// <summary>组件高度（DIP）。</summary>
    public double MonitorHeight { get; set; }

    /// <summary>该实例自己的待办内容（多实例互不干扰）。</summary>
    public List<TodoItem> Todos { get; set; } = new();

    /// <summary>该实例自己的随记内容。</summary>
    public List<QuickNoteItem> Notes { get; set; } = new();

    /// <summary>该实例自己的快捷入口（置顶条目来自数据库，仍共享）。</summary>
    public List<LinkItem> Links { get; set; } = new();

    /// <summary>
    /// 该实例自己的计算器历史带（最新在前，条数上限由组件侧守住）。
    /// 可空：旧实例缺此字段反序列化为 null ⇒ 空历史，零迁移。按实例存 ⇒ 两个计算器互不干扰。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CalcHistoryItem>? CalcHistory { get; set; }

    /// <summary>
    /// 世界时钟的点位（Windows 时区 Id + 显示名）。
    /// <b>null 与空串列表语义不同</b>：null = 这台机器上从没配过 ⇒ 组件给默认四城；
    /// 空列表 = 用户自己删光了 ⇒ 保持空。判据取 null 而非 Count（首启与"删光"必须可分辨）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WorldClockCity>? WorldClockZones { get; set; }

    /// <summary>该实例自己的倒计时/纪念日列表。空与 null 都表示"没有项目"（这类内容没有合理的默认值可预置）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CountdownItem>? Countdowns { get; set; }

    /// <summary>
    /// 时钟组件上的闹钟（批次 RU）。<b>按实例存</b>：与"闹钟住在时钟组件里"这条裁决一致——
    /// 两个时钟实例各自一套，删掉那个组件时这一套跟着走，不留一份没人显示的真值。
    /// null 与空列表都是"没有闹钟"（这类内容没有合理的默认值可预置：预置一个会在半夜响的东西是最坏的默认）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AlarmItem>? Alarms { get; set; }

    /// <summary>番茄钟时长设置（只存时长；进行中的轮次与统计刻意不存，见 <c>FocusTimer</c>）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FocusTimerConfig? Focus { get; set; }

    /// <summary>
    /// 系统监控要显示哪些指标（<c>MonitorMetric</c> 的整数形态）。
    /// <b>null＝这台机器上从没配过 ⇒ 三项全开；0＝用户自己把勾都取消了 ⇒ 保持空</b>（判据是 null 与否）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MonitorMetrics { get; set; }

    // ── 差异化条目格的每实例查询配置 ──
    // 可空、向后兼容：旧实例缺这些字段时反序列化为 null，不影响其它类型。

    /// <summary>标签格所钉的标签名（TagGrid 用）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GridTag { get; set; }

    /// <summary>每实例外观覆盖（材质/颜色/边框/圆角/文本缩放）。为 null 时本实例沿用全局外观设置。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WidgetAppearanceOverride? Appearance { get; set; }

    /// <summary>
    /// 收起为胶囊时的停靠位（物理像素）。与 X/Y/Width/Height（展开态位置，供点击展开恢复）分离保存，
    /// 使得「悬停预览」反复收起不会重写展开位置，也不会因每次重算堆叠而把同列胶囊推离原位。
    /// 旧实例缺字段反序列化为 null，运行时回退为「吸附最近垂直边缘 + 向下堆叠」自动分配。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CapsuleX { get; set; }

    /// <summary>收起为胶囊时的停靠位 Y（物理像素）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CapsuleY { get; set; }
}

/// <summary>每实例外观覆盖（任一字段为 null 即回退到全局设置）。</summary>
public sealed class WidgetAppearanceOverride
{
    /// <summary>材质覆盖（亚克力/云母/不透明）。null = 用全局。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StarMark.Abstractions.WidgetBackdropKind? Backdrop { get; set; }

    /// <summary>背景色（#RRGGBB 或 #AARRGGBB）。null = 用全局材质表面（透明/实色随主题）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackgroundColor { get; set; }

    /// <summary>前景（文本）色（#RRGGBB）。null = 用主题默认。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ForegroundColor { get; set; }

    /// <summary>边框色（#RRGGBB）。null = 用主题默认（1px 分隔色）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BorderColor { get; set; }

    /// <summary>边框粗细（物理 px）。null = 用全局（1）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? BorderThickness { get; set; }

    /// <summary>圆角半径（物理 px）。null = 用全局（8）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CornerRadius { get; set; }

    /// <summary>文本缩放（1.0 = 100%）。null = 用全局（1.0）。范围建议 0.7–1.5。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TextScale { get; set; }
}

/// <summary>组件存储根。</summary>
public sealed class WidgetStoreData
{
    public int Version { get; set; } = 3;

    /// <summary>全部组件实例（v3 起唯一真源；同一类型可多个）。</summary>
    public List<WidgetInstanceConfig> Instances { get; set; } = new();

    /// <summary>用户保存的组件布局方案（同一时刻只套用一套；切换即隐藏不属于该布局的实例）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WidgetLayout>? Layouts { get; set; }

    /// <summary>
    /// 用户保存的「布局 + 组件数据」快照点（#53）。与 <see cref="Layouts"/> 的**纯模板**相对：
    /// 快照额外带快捷入口/待办/随记/条目格查询等组件数据，且**不可变**（一次一点、永不就地覆盖）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WidgetSnapshot>? Snapshots { get; set; }

    /// <summary>
    /// 默认布局（上次「选中/应用」的布局方案 Id）。
    /// 用户每次应用某套布局即把它记为此字段；启动恢复时若此字段存在则自动套用该布局，
    /// 使「最后一次选择的布局」成为组件默认状态。为 null 时回退为逐个显示全部实例。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultLayoutId { get; set; }

    /// <summary>本地条目（待办/随记）是否已迁移进统一 items 表。为 true 时迁移跳过（幂等）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LocalItemsMigrated { get; set; }

    // ── v2（每类型一个实例）遗留字段，仅用于迁移，迁移后清空不再落盘 ──

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WidgetKind>? Enabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, WidgetConfig>? WindowConfigs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TodoItem>? Todos { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<QuickNoteItem>? Notes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LinkItem>? Links { get; set; }

    // ── v1（单面板 WidgetHostWindow 时代）遗留字段，仅用于迁移 ──

    [JsonPropertyName("Config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WidgetConfig? LegacyConfig { get; set; }

    [JsonPropertyName("ShowOnStartup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyShowOnStartup { get; set; }
}

/// <summary>
/// 桌面组件持久化（对标 DeskBox 的 ResilientJsonStore/TodoWidgetStore 模式）：
/// 单个 JSON 文件，损坏时静默回退默认值，写入先落临时文件再原子替换。
/// 纯逻辑、无 UI 依赖，便于单元测试与 SmokeTest 无头验证。
/// </summary>
public sealed class WidgetStorage
{
    private readonly string _path;
    private readonly object _gate = new();

    /// <summary>
    /// 上一次 <see cref="Load"/> 是否因**瞬时** IO/权限错误（OneDrive 同步、杀软实时扫描短暂锁定 widgets.json）
    /// 而未能读到磁盘内容。为 true 时内存态不代表磁盘态，<see cref="Save"/> 拒绝落盘，避免用空数据覆盖真实配置。
    /// </summary>
    private bool _loadDegraded;

    /// <summary>
    /// 快照缓存的有效期。<b>取这么短是为了让陈旧有上界</b>：绕过 <see cref="Save"/> 的改写
    /// （备份还原、用户手改、另一个实例写入）最坏只晚 250 ms 被看到。
    /// 真实读盘远快于这个数，所以"一次交互内的多次询问"必然全部命中——省的正是那部分。
    /// </summary>
    public static readonly TimeSpan DefaultCacheValidity = TimeSpan.FromMilliseconds(250);

    /// <summary><paramref name="cacheValidity"/> 只给测试和"必须立刻反映外部改动"的调用方用；
    /// 传 null 就是 <see cref="DefaultCacheValidity"/>。</summary>
    public WidgetStorage(string? path = null, TimeSpan? cacheValidity = null)
    {
        _path = path ?? DefaultPath();
        _cacheValidityMs = (cacheValidity ?? DefaultCacheValidity).TotalMilliseconds;
    }

    public string StorePath => _path;

    /// <summary>与 settings.json / github.json 同目录；整条优先规则只有一处（<see cref="StarMark.Abstractions.UserDataPaths"/>）。</summary>
    public static string DefaultPath() => StarMark.Abstractions.UserDataPaths.Sibling("widgets.json");

    /// <summary>全部组件类型（设置页 / 托盘菜单遍历用），来源为 <see cref="WidgetRegistry"/>。</summary>
    public static IReadOnlyList<WidgetKind> AllKinds { get; } =
        WidgetRegistry.Default.GetWindowDescriptors().Select(d => d.Kind).ToArray();

    /// <summary>
    /// v1（单面板时代）真实存在的五种组件。v1→v2 迁移只认这份历史清单，<b>不认</b> <see cref="AllKinds"/>：
    /// 迁移的答案是"当时有什么"，而 AllKinds 会随注册表增长——写它的后果是每加一种组件，
    /// 所有还没升级过的 v1 用户一升级就多开一个窗口（今天会是 12 个，含要联网的天气）。
    /// </summary>
    public static readonly IReadOnlyList<WidgetKind> LegacyV1Kinds = new[]
    {
        WidgetKind.QuickLaunch, WidgetKind.Todo, WidgetKind.QuickNote, WidgetKind.Clock, WidgetKind.Search,
    };

    /// <summary>
    /// 首次运行预置哪些组件（P-66 由用户裁决：只给时钟）。只在"磁盘上没有配置文件"那一次生效，
    /// 用户主动删光组件后不会被重新塞回来（见 <see cref="Normalize"/> 的 firstRun 说明）。
    /// </summary>
    public static readonly IReadOnlyList<WidgetKind> FirstRunKinds = new[] { WidgetKind.Clock };

    /// <summary>组件展示名（含图标）；未知类型回退为类型名。</summary>
    public static string KindTitle(WidgetKind kind) =>
        WidgetRegistry.Default.TryGet(kind, out var d) ? d.DisplayTitle : kind.ToString();

    /// <summary>取某组件的窗口配置（无记录时按类型给默认尺寸并级联摆放）。</summary>
    public WidgetConfig GetConfig(WidgetStoreData data, WidgetKind kind, int index)
    {
        if (data.WindowConfigs != null && data.WindowConfigs.TryGetValue(kind.ToString(), out var c) && c != null)
            return c;
        return new WidgetConfig
        {
            X = 160 + index * 40,
            Y = 140 + index * 40,
            Width = DefaultWidth(kind),
            Height = DefaultHeight(kind),
        };
    }

    /// <summary>默认尺寸（DIP 逻辑像素，窗口首次创建时按显示器 DPI 换算为物理像素）。</summary>
    public static int DefaultWidth(WidgetKind kind) =>
        WidgetRegistry.Default.TryGet(kind, out var d) ? d.DefaultWidth : 320;

    public static int DefaultHeight(WidgetKind kind) =>
        WidgetRegistry.Default.TryGet(kind, out var d) ? d.DefaultHeight : 400;

    public static bool IsResizable(WidgetKind kind) =>
        !WidgetRegistry.Default.TryGet(kind, out var d) || d.IsResizable;

    /// <summary>
    /// 上一次成功解析的快照。<b>能进缓存的只有"完整读到并解析成功"这一种结果</b>：
    /// 文件不存在（要写预置）、内容损坏（要留 .bak）、读盘被瞬时锁定（要等锁释放重试并禁止落盘）
    /// 这三种各自带一个后续动作，缓存把它们挡住就等于把恢复路径堵死。
    /// </summary>
    private WidgetStoreData? _cached;
    private long _cachedAtMs;
    private readonly double _cacheValidityMs;

    /// <summary>真正读盘并解析的次数（诊断/测试用）。缓存生效的证据就靠它，而不是靠"感觉快了"。</summary>
    internal int DiskReads;

    /// <summary>命中快照缓存的次数（诊断/测试用）。</summary>
    internal int CacheHits;

    /// <summary>真正整档写盘的次数（诊断/测试用）。与 <see cref="DiskReads"/> 一起才是"省了几趟"的证据。</summary>
    internal int DiskWrites;

    public WidgetStoreData Load()
    {
        lock (_gate)
        {
            // 命中判据只有一句话："距上次成功读盘有没有超过有效期"——连文件都不问。
            // 一次托盘右键菜单要问 17 种组件的启用状态，不缓存就是 17 次整档读 + 17 次反序列化；
            // 而 stat 在 Windows 上同样是一趟 IO（widgets.json 常放在 OneDrive 目录下，属性查询还会
            // 牵出占位符），所以这里不用"指纹更便宜"的那条路，只用自己进程内的时钟。
            if (_cached is not null && Environment.TickCount64 - _cachedAtMs <= _cacheValidityMs)
            {
                CacheHits++;
                return _cached;
            }
            _cached = null;

            if (!File.Exists(_path))
            {
                // 首启/文件确被删除：空态是可信的，允许后续 Save 落盘。
                _loadDegraded = false;
                var fresh = Normalize(null, firstRun: true);
                // 预置实例必须当场落盘：实例 ID 是随机 Guid，不落盘则每次 Load 都换一个，
                // 上层按 ID 管窗口会重复建窗 / 找不到窗（以前首启是空列表，没有这个问题）。
                if (fresh.Instances.Count > 0) Save(fresh);
                _cached = null;                       // 刚写过（或决定不写）：下一次按磁盘事实重新判断
                return fresh;
            }
            try
            {
                DiskReads++;
                var data = JsonSerializer.Deserialize<WidgetStoreData>(File.ReadAllText(_path));
                _loadDegraded = false;
                var parsed = Normalize(data);

                // 读完不回问指纹，直接缓存：写盘走的是"先写 .tmp 再 File.Move"，读者要么拿到旧版、
                // 要么拿到新版，不存在读到半档。至于"读的这一瞬恰好被改写"，最坏也只陈旧一个有效期——
                // 拿指纹换"永不陈旧"是不划算的：NTFS 的时间戳粒度让等长改写根本问不出来（用例里实测过）。
                _cached = parsed;
                _cachedAtMs = Environment.TickCount64;
                return parsed;
            }
            catch (JsonException ex)
            {
                // 内容损坏（读得到字节、只是 JSON 非法）：磁盘上本就无可信数据，回退默认并允许后续 Save 覆盖掉坏文件。
                // 覆盖前把损坏原文留一份 .bak，给用户最后的挽回机会。
                _loadDegraded = false;
                StarLog.Error($"组件配置损坏，已回退默认并尝试备份原文件 ({_path})", ex);
                try { File.Copy(_path, _path + ".bak", overwrite: true); } catch { }
                _cached = null;                       // 回退出来的默认态不是磁盘事实，不许被下一次 Load 当成缓存复用
                return Normalize(null);
            }
            catch (Exception ex)
            {
                // 未能读到内容（OneDrive 同步 / 杀软实时扫描 / 索引器瞬时锁定 → IOException；权限/占用 → UnauthorizedAccessException 等）：
                // 磁盘数据其实完好，只是这一瞬拿不到。此时若返回空并让随后的 Save 落盘，会用空数据**覆盖真实配置**——最坏的数据丢失。
                // 置降级位，令 Save 拒绝写入；锁定解除后的下一次成功 Load 会自动清除该位。
                _loadDegraded = true;
                // 节流：锁定期内每次 Load 都会撞这一句（60 s 窗口内同路径只留首条 + 累计数）
                StarLog.WarnThrottled($"widget-read:{_path}",
                    $"读取组件配置失败（疑似被临时占用），本次不落盘以免覆盖真实数据 ({_path})：{ex.Message}");
                _cached = null;                       // 降级态必须在下次调用重试，缓存住它就把"锁释放后自动恢复"堵了
                return Normalize(null);
            }
        }
    }

    /// <summary>整档写盘。<b>返回是否真的落盘了</b>：降级态（读取曾被占用）与写/搬失败都返回 false，
    /// 调用方据此才能说实话——否则"我保存好了"是一句谎话。</summary>
    public bool Save(WidgetStoreData data)
    {
        lock (_gate)
        {
            // 降级态：内存里是读不到磁盘时回退出的空数据，绝不能拿它覆盖磁盘上其实完好的真实配置。
            // 跳过本次写入（改动丢失远好于全量清空）；锁定解除后的下一次成功 Load 会自动清除该位。
            if (_loadDegraded)
            {
                // 节流：降级期间每次保存都撞这一句（摆位/改标题都会保存），留首条 + 累计数即可
                StarLog.WarnThrottled($"widget-degraded:{_path}",
                    $"组件配置处于降级态（读取曾被临时占用），跳过本次保存以保护磁盘数据 ({_path})");
                return false;
            }

            // 落盘绝不向外抛：本方法经 WidgetManager.OnUiAsync 在 UI 线程同步内联执行，
            // 一旦从点击/菜单处理里抛出 IOException（widgets.json 位于 %APPDATA%，常被 OneDrive
            // 同步或杀软实时扫描短暂锁定 → File.WriteAllText/File.Move 失败），整个应用会闪退。
            // 失败时保留旧文件不动（先写 .tmp 再 Move，写/搬失败原文件仍是上一版），仅记日志。
            var tmp = _path + ".tmp";
            try
            {
                var normalized = Normalize(data);
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmp, JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, _path, overwrite: true);
                DiskWrites++;
                // 只有真的搬成功了才作废缓存：失败时磁盘仍是上一版，缓存照样有效
                // （反过来若在这里也作废，就成了"写失败却重读一遍旧内容"，白做一次还多一处会错的地方）。
                _cached = null;
                return true;
            }
            catch (Exception ex)
            {
                StarLog.Error($"保存组件配置失败，已保留上一次内容不变 ({_path})", ex);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return false;
            }
        }
    }

    /// <summary>
    /// "一次读档 → 就地改 N 处 → 最多一次落盘"的出口。<b>循环里逐个 <see cref="Save"/> 正是这条路要消灭的形状</b>：
    /// 每次 Save 都会作废 <see cref="Load"/> 缓存的快照，于是"读→写→读→写"逐轮整档往返——
    /// 隐藏/关闭 8 个组件窗口就是 8 趟全档读写，而 widgets.json 还连带存着全部布局与快照，会随用户积累变大。
    /// </summary>
    /// <param name="mutate">就地改这份存档，返回<b>是否需要落盘</b>；返回 false 时一次写盘都不发生。</param>
    /// <returns>是否真的写了盘。</returns>
    public bool Mutate(Func<WidgetStoreData, bool> mutate)
    {
        lock (_gate)
        {
            var data = Load();
            if (!mutate(data)) return false;
            return Save(data);
        }
    }

    /// <summary>
    /// 规范数据：去 null/空文本、排序（待办未完成在前按时间倒序，随记/入口按时间倒序）、限量、v1 迁移。
    /// <paramref name="firstRun"/> 仅在"磁盘上确实没有配置文件"时由 <see cref="Load"/> 传 true，
    /// 用于预置首启组件（P-66）；<b>已存在但实例为空的文件不算首启</b>——那是用户主动删光的结果，
    /// 再塞回去就是"删不掉的组件"。
    /// </summary>
    public static WidgetStoreData Normalize(WidgetStoreData? data, bool firstRun = false)
    {
        data ??= new WidgetStoreData();

        // v1 → v2 迁移：旧版是单面板（Config + ShowOnStartup，开关可能在根上也可能嵌在 Config 内）。
        // 旧用户若勾选了“启动时显示”，迁移为启用 v1 时代的那五种组件；旧面板位置交给快捷启动格。
        if (data.Version < 2)
        {
            var showOnStartup = data.LegacyShowOnStartup == true
                || data.LegacyConfig?.LegacyShowOnStartupInConfig == true;
            if (showOnStartup)
            {
                data.Enabled ??= new List<WidgetKind>();
                if (data.Enabled.Count == 0)
                {
                    // 刻意不写 AllKinds：迁移的答案是"当时有什么"，不是"现在有什么"。
                    // 这句早年写的是 AllKinds（那时 5 条），注册表长到 12 种后同一条路径会跟着膨胀
                    // ⇒ v1 老用户升级回来，桌面凭空多出天气/今日速览/音乐/四张条目格共 7 个窗口。
                    data.Enabled.AddRange(LegacyV1Kinds);
                    if (data.LegacyConfig is { } legacy)
                    {
                        legacy.LegacyShowOnStartupInConfig = null;
                        data.WindowConfigs ??= new Dictionary<string, WidgetConfig>();
                        data.WindowConfigs[WidgetKind.QuickLaunch.ToString()] = legacy;
                    }
                }
            }
            data.LegacyConfig = null;
            data.LegacyShowOnStartup = null;
        }

        // v2 → v3 迁移：每类型一个实例 → 多实例（按实例管理，同一类型可重复添加）。
        // 把 v2 的 Enabled + WindowConfigs + 全局 Todos/Notes/Links 归并为若干实例；
        // 全局内容归并到该类型的首个实例，其余新实例各自为空（多组件内容互不覆盖）。
        if (data.Version < 3)
        {
            data.Instances ??= new List<WidgetInstanceConfig>();
            if (data.Enabled is { Count: > 0 })
            {
                foreach (var kind in data.Enabled)
                {
                    var cfg = (data.WindowConfigs != null
                               && data.WindowConfigs.TryGetValue(kind.ToString(), out var w)
                               && w is not null)
                        ? w
                        : new WidgetConfig();
                    var inst = new WidgetInstanceConfig
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Kind = kind,
                        X = cfg.X, Y = cfg.Y, Width = cfg.Width, Height = cfg.Height,
                        Topmost = cfg.Topmost,
                    };
                    if (kind == WidgetKind.Todo && data.Todos is not null) inst.Todos = data.Todos;
                    else if (kind == WidgetKind.QuickNote && data.Notes is not null) inst.Notes = data.Notes;
                    else if (kind == WidgetKind.QuickLaunch && data.Links is not null) inst.Links = data.Links;
                    data.Instances.Add(inst);
                }
            }
            data.Enabled = null;
            data.WindowConfigs = null;
            data.Todos = null;
            data.Notes = null;
            data.Links = null;
        }

        data.Version = 3;
        data.Instances ??= new List<WidgetInstanceConfig>();

        // 首次运行预置（P-66）：只给时钟，让"桌面组件"在第一屏就被看见，其余靠新建入口 / 托盘发现。
        if (firstRun && data.Instances.Count == 0)
            foreach (var kind in FirstRunKinds)
                data.Instances.Add(new WidgetInstanceConfig
                {
                    Kind = kind,
                    Width = DefaultWidth(kind),
                    Height = DefaultHeight(kind),
                });

        data.Layouts = WidgetLayoutCollection.Normalize(data.Layouts);
        data.Snapshots = WidgetSnapshotCollection.Normalize(data.Snapshots);

        // 每个实例的内容分别规范化（滤脏 + 排序），避免多实例数据相互覆盖。
        // 先剔除实例数组里的显式 null 元素（坏写入 / 手改 JSON / OneDrive 截断可达），与下方 Todos/Notes/
        // Links 及 Layouts/Snapshots 各兄弟集合一律滤 null 的口径一致。缺此过滤时，紧接 foreach 里的
        // inst.Todos 取属性即抛 NullReferenceException——它落在 Load 的 try 内、被 catch-all 当作「文件被
        // 临时占用」，于是 _loadDegraded 被永久置位（每次重载重抛同一形状错误，成功 Load 永不再发生），
        // 反把整个组件持久化锁死且恒返回空。属确定性、非 IO 的缺陷，不在 AA/R10-1 的「损坏 vs 瞬时占用」二分内。
        //
        // <b>这里刻意不限量</b>（P-10）：<see cref="Normalize"/> 同时被 <see cref="Load"/> 与 <see cref="Save"/> 调用，
        // 所以任何一道 `Take(n)` 都是<b>读一次少几条</b>——`Load()` 交出去的内存模型已被裁，
        // 随后任何 `Load()→改→Save()` 都把超出部分当作从来不存在。
        // 丢的时候<b>一声不响</b>：常规 Save 不留 `.bak`、日志里一个字都没有、界面上那条只是"没了"。
        // 唯一的挽回面是<b>更早的一次备份导出</b>（备份信封连着存 widgets.json 原文，见 BackupEnvelope.WidgetsJson），
        // 而且回档是把整套组件状态退到那一次；没导过就没处找回。
        // 今天最现实的触发面是快捷启动格：攒到 101 条后"发送到快捷启动"再多一条，就静默删掉最旧一条。
        //
        // "要不要给用户内容设硬顶、顶在哪里"是产品口径，但<b>答案不该由读路径来实现</b>：
        // 展示层可以有自己的窗口（随记 `QuickNoteWidgetViewModel.DisplayLimit = 30`；快捷启动今天没顶＝加多少摆多少），
        // 真要收，做的应是<b>看得见、要先确认的清理入口</b>（同 ClipIMG-3a 那条"孤儿只数不删"的口径），
        // 而不是让一次读取替用户做决定。要恢复限量也必须只挂在写路径上，并配一条"裁掉了什么"的可见告知。
        data.Instances.RemoveAll(inst => inst is null);
        foreach (var inst in data.Instances)
        {
            inst.Todos = inst.Todos ?? new List<TodoItem>();
            inst.Notes = inst.Notes ?? new List<QuickNoteItem>();
            inst.Links = inst.Links ?? new List<LinkItem>();

            inst.Todos = inst.Todos
                .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Text))
                .OrderBy(t => t.Done)                    // 未完成(false)在前，已完成在后
                .ThenByDescending(t => t.CreatedAt)
                .ToList();
            inst.Notes = inst.Notes
                .Where(n => n is not null && !string.IsNullOrWhiteSpace(n.Text))
                .OrderByDescending(n => n.CreatedAt)
                .ToList();
            inst.Links = inst.Links
                .Where(l => l is not null && !string.IsNullOrWhiteSpace(l.Uri))
                .OrderByDescending(l => l.CreatedAt)
                .ToList();
        }
        return data;
    }

    private static readonly object _idGate = new();
    private static long _lastId;

    /// <summary>
    /// 进程内<b>严格递增</b>的本地 Id：常态取「当前毫秒×1000」作号段基址，同一毫秒内（或时钟回退时）
    /// 退回「上一个 Id + 1」保证绝不重号。
    /// <para>
    /// <b>为何不能用毫秒内随机数</b>：<c>source_id = EncodeSourceId(instanceId, NewId())</c> 落在
    /// <c>UNIQUE(items.source, source_id)</c> 上。旧实现 <c>ms*1000 + Random(0..998)</c> 在同一毫秒批量取号时
    /// 概率性撞号，两笔不同待办/笔记同号即被 <c>ON CONFLICT(source, source_id) DO UPDATE</c> 静默覆盖（丢数据）。
    /// 单调号从构造上杜绝重号，且无人解码 Id 的毫秒含义，故进位越界无害。
    /// </para>
    /// </summary>
    public static long NewId()
    {
        lock (_idGate)
        {
            var floor = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
            _lastId = floor > _lastId ? floor : _lastId + 1;
            return _lastId;
        }
    }

    /// <summary>取某实例配置（按 Id）。</summary>
    public WidgetInstanceConfig? FindInstance(string id)
    {
        lock (_gate)
        {
            var data = Load();
            return data.Instances.FirstOrDefault(i => i.Id == id);
        }
    }

    /// <summary>在已加载的数据中按 Id 取或新建一个实例（供视图模型就地改集合后回写）。</summary>
    public static WidgetInstanceConfig GetOrAddInstance(WidgetStoreData data, string id, WidgetKind kind)
    {
        var inst = data.Instances.FirstOrDefault(i => i.Id == id);
        if (inst is null)
        {
            inst = new WidgetInstanceConfig { Id = id, Kind = kind };
            data.Instances.Add(inst);
        }
        return inst;
    }

    // ───────────────────────── 布局方案 ─────────────────────────

    /// <summary>全部已保存布局（按名称排序，名称保证唯一）。</summary>
    public IReadOnlyList<WidgetLayout> GetLayouts()
    {
        lock (_gate) return Load().Layouts ?? new List<WidgetLayout>();
    }

    /// <summary>按 Id 查找布局。</summary>
    public WidgetLayout? FindLayout(string id)
    {
        lock (_gate) return Load().Layouts?.FirstOrDefault(l => l.Id == id);
    }

    /// <summary>新增或覆盖同名布局（按 Id 判定）；返回去重后的最终名称。</summary>
    public string SaveLayout(WidgetLayout layout)
    {
        lock (_gate)
        {
            var data = Load();
            var layouts = data.Layouts ??= new List<WidgetLayout>();
            var existing = layouts.FirstOrDefault(l => l.Id == layout.Id);
            if (existing is not null) layouts.Remove(existing);
            layouts.Add(layout);
            this.Save(data);
            return layout.Name;
        }
    }

    /// <summary>删除布局；返回是否删除成功。</summary>
    public bool DeleteLayout(string id)
    {
        lock (_gate)
        {
            var data = Load();
            if (data.Layouts is not { Count: > 0 } layouts) return false;
            var target = layouts.FirstOrDefault(l => l.Id == id);
            if (target is null) return false;
            layouts.Remove(target);
            this.Save(data);
            return true;
        }
    }

    // ───────────────────────── 布局与数据快照（#53） ─────────────────────────
    // 快照与布局的本质区别：布局是可复用的**纯模板**且按 Id 就地覆盖；快照是**不可变历史点**、
    // 带组件数据、一次一点永不覆盖（唯一例外是用户主动删除）。故此处刻意不提供 SaveSnapshot。

    /// <summary>全部快照点（新的在前；名称唯一，由 <see cref="WidgetSnapshotCollection.Normalize"/> 保证）。</summary>
    public IReadOnlyList<WidgetSnapshot> GetSnapshots()
    {
        lock (_gate) return Load().Snapshots ?? new List<WidgetSnapshot>();
    }

    /// <summary>按 Id 查找快照点。</summary>
    public WidgetSnapshot? FindSnapshot(string id)
    {
        lock (_gate) return Load().Snapshots?.FirstOrDefault(s => s.Id == id);
    }

    /// <summary>
    /// 追加一个快照点（**不可变**：永不按 Id 覆盖既有点）。
    /// 名称自动去重、Id 冲突时重新生成后追加；返回落盘后的快照（含最终名称）。
    /// </summary>
    public WidgetSnapshot AppendSnapshot(WidgetSnapshot snapshot)
    {
        lock (_gate)
        {
            var data = Load();
            var snaps = data.Snapshots ??= new List<WidgetSnapshot>();

            // 不可变：Id 若与既有点相同（理论上不该发生，除非手工构造），换新 Id 而不是覆盖。
            if (snaps.Any(s => s.Id == snapshot.Id))
                snapshot.Id = Guid.NewGuid().ToString("N");

            snapshot.Name = WidgetSnapshotCollection.MakeUniqueName(snaps, snapshot.Name);
            snaps.Add(snapshot);
            this.Save(data);   // Save→Normalize 会补全排序/去重，返回的 snapshot 即最终落盘对象
            return snapshot;
        }
    }

    /// <summary>删除快照点；返回是否删除成功。</summary>
    public bool DeleteSnapshot(string id)
    {
        lock (_gate)
        {
            var data = Load();
            if (data.Snapshots is not { Count: > 0 } snaps) return false;
            var target = snaps.FirstOrDefault(s => s.Id == id);
            if (target is null) return false;
            snaps.Remove(target);
            this.Save(data);
            return true;
        }
    }
}
