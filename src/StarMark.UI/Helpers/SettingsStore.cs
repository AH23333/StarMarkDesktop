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
using StarMark.UI.Services;   // 热键注册投影要问"此刻的会话态"（架构方案 §6.1，投影只有这一处）

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
public sealed partial class SettingsStore : IPerformanceSettingsSource
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
        /// 「开机自动加载组件」。<b>默认开</b>——这一条与上面那些总开关相反是有意的：
        /// 组件本来就摆在桌面上，把默认改成关会让一次软件更新<b>悄悄收起用户的桌面</b>，那是观感事故不是性能优化。
        /// 省下的是每颗约 8 MB 私有内存（实测见报告 §二百一十），所以取舍交给用户点这一下。
        /// </summary>
        public bool? WidgetsLoadOnStartup { get; set; }
        /// <summary>
        /// 内置剪贴板历史总开关。<b>默认关</b>：剪贴板是全机器敏感度最高的数据（密码、卡号、私钥都会路过它），
        /// 默认开等于在用户不知情时把这些抄进一个明文 SQLite 文件。开启后采集全自动，无需任何后续步骤。
        /// </summary>
        public bool? ClipboardHistoryEnabled { get; set; }
        /// <summary>
        /// 剪贴板<b>图片</b>采集分开关。<b>默认关</b>（决议 §4）：图片做不了文本那种敏感扫描（私钥/卡号/JWT
        /// 都在像素里），所以"开不开"必须是用户自己点的一下，不能随总开关一起默认生效。
        /// 关着时连剪贴板图片格式都不去读——"没开"与"没装这个功能"在磁盘上完全一样。
        /// </summary>
        public bool? ClipboardImageEnabled { get; set; }
        /// <summary>图片条数上限（决议 §4：默认 200，夹 10–2000）。null＝从没调过＝用默认。
        /// 与文本分开一格：一张 4K 截图的 PNG 常有几百 KB，与一段文字共用一个上限等于让文字把图挤掉。</summary>
        public int? ClipboardImageMaxEntries { get; set; }
        /// <summary>文本条数上限（决议 §4：默认 500，夹 10–10000）。<b>默认值就是改之前的行为</b>，
        /// 这一格只是把原来写死的常量变成用户看得见、可调的东西。</summary>
        public int? ClipboardTextMaxEntries { get; set; }
        /// <summary>
        /// 备份是否携带剪贴板图片本体。<b>这一格是 a 期预埋，b 期没有任何副作用</b>（当前备份格式不含附件），
        /// 所以<b>刻意不在设置页出现控件</b>：一个点了什么都不改变的开关比没有开关更糟。
        /// 默认 true 与决议 §4 一致，等 a 期接入导出/恢复时直接读它，用户当时的选择不会跨版本变味。
        /// </summary>
        public bool? BackupClipboardImagesEnabled { get; set; }
        /// <summary>
        /// 自动备份总开关。<b>缺省＝开</b>：设置页出现之前它就是"每次都跑"，
        /// 加开关是为了让用户能关掉，不是为了顺手改掉既有行为（默认值就是改之前的行为）。
        /// </summary>
        public bool? AutoBackupEnabled { get; set; }
        /// <summary>自动备份间隔（小时）。只认 <c>AutoBackupPolicy.IntervalOptions</c> 那几档，
        /// 脏值/缺省都回默认 24；<b>存的是小时数而不是档位序号</b>，加一档不会让旧配置被读成别的间隔。</summary>
        public int? AutoBackupIntervalHours { get; set; }
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
        /// <summary>磁吸对齐间距（<b>逻辑像素</b>，拖动时才按缩放比换成物理像素）：两组件贴合时保留的间隙。
        /// 默认与量程都在 <see cref="StarMark.Core.Appearance.AppearanceSettingsPolicy"/>，这里不重述数字。</summary>
        public int? WidgetSnapSpacing { get; set; }
        /// <summary>磁吸吸附强度→进入阈值（<b>逻辑像素</b>）：越大越早吸附（更易吸、更难微调）。默认与量程同上。</summary>
        public int? WidgetSnapStrength { get; set; }
        /// <summary>半透明材质：0=亚克力 1=云母 2=不透明（默认 0）。</summary>
        public int? WidgetBackdrop { get; set; }
        /// <summary>组件背景不透明度（配合半透明材质使用）。默认与量程见
        /// <see cref="StarMark.Core.Appearance.AppearanceSettingsPolicy"/>。</summary>
        public double? WidgetOpacity { get; set; }
        /// <summary>毛玻璃材质浓度（DeskBox 的 WidgetMaterialIntensity）。默认与量程同上。</summary>
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
        // 这一组的 key 全部带 Ai 前缀且各自独立——<b>一格只管一件事</b>是硬性形状，加一格就多一个 key，
        // 谁也不许覆盖谁。扩展项目出过一次"AI 域四个 key 同名"的事故：
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

        /// <summary>月度 token 预算（§20.2）。null＝从没设过＝用出厂默认 500K；
        /// <b>不做"0＝无限"</b>：预算要关到最低有 MinMonthlyTokens 兜底，想彻底停 AI 该关总开关，
        /// 一格只管一件事（这是扩展侧"storage key 语义化"教训的延续——同一份文件里
        /// AiEnabled 和 AiTokenBudget 若混出一个"budget=0 也关 AI"的暗含义，两处判断迟早分岔）。</summary>
        public long? AiTokenBudget { get; set; }

        /// <summary>预算熔断位（跨会话保持）。true＝上次越线后被暂停，<b>窗口滚回去也不会自动放行</b>——
        /// §20.2 明写"重置＝手动点击"，防的就是"用户不知道为什么半夜恢复的调用在烧钱"。</summary>
        public bool? AiBudgetPaused { get; set; }

        /// <summary>预警线（<b>小数比例</b>：0.8＝用到预算的 80% 时先提醒，界面上按百分数说话）。
        /// null＝从没设过＝出厂 0.8；越界值（0 与 1 之外）在读取侧就回默认，
        /// 判据只住在 <c>AiBudget.IsLegalWarnRatio</c> 一处。</summary>
        public double? AiWarnRatio { get; set; }

        /// <summary>「AI 整理」规则预分类的用户追加规则（JSON 数组，§19 O2）。null/解析失败＝只有内置规则
        /// （<c>ClassifyRules.Merged</c> 一条路管两种情况）。放得下也读得回的失败必须出声：<b>这条链的失败
        /// 形态是"我明明加了规则却没生效"，所以坏 JSON 在读取侧就回报，而不是静默吞掉。</b></summary>
        public string? AiClassifyRulesJson { get; set; }

        /// <summary>分类专用模型（§19 O6）。null/空白＝沿用主模型。与 <see cref="AiModel"/> 分格是刻意的：
        /// 一格只绑架一件事——换分类小模型不许顺手把将来的生成任务也换小。</summary>
        public string? AiClassifyModel { get; set; }

        /// <summary>一批问模型的时限（秒）。null＝从没设过＝走 <c>ClassifyRunner.DefaultTimeoutSeconds</c>；
        /// <b>越界值在读取侧回默认并留一句原因</b>（判据住在 <c>ClassifyRunner.NormalizeTimeoutSeconds</c>，
        /// 这一格是用户可手改的 JSON，静默采纳 0 秒等于"每批都立刻超时"）。</summary>
        public int? AiBatchTimeoutSeconds { get; set; }

        /// <summary>收藏即时分类开关（§19 O5）。<b>默认关</b>：它会"用户没按下任何 AI 按键就发一个请求"——
        /// 合法性由 §20.4 的裁定撑腰（收藏动作本身是用户显式触发），但"裁定合法"不等于"值得默认开"：
        /// 只有 AI 总开关打开的人才会看见这格，打开后每次收藏多一发单条分类。</summary>
        public bool? AiInstantClassify { get; set; }

        /// <summary>
        /// 护眼 / 休息提醒总开关（<b>默认关</b>）：默认开等于在谁都没要求的时候往屏幕上盖一层遮罩，
        /// 那是"程序替用户决定什么时候该休息"。关时连节拍定时器都不建（不占表、不探前台窗口）。
        /// </summary>
        public bool? EyeRestEnabled { get; set; }

        /// <summary>连续工作多少分钟算该休息一次。非法值由 <c>EyeRestPolicy.ClampInterval</c> 回落默认，
        /// <b>不夹到边界</b>：夹到 5 等于把一处存档损坏放大成"每 5 分钟打断一次"。</summary>
        public int? EyeRestIntervalMinutes { get; set; }

        /// <summary>提醒形式（0 提示卡／1 暗幕可跳／2 强制不可跳，见 <c>EyeRestNotice</c>）。缺省与认不得的数值
        /// 都由 <c>EyeRestPolicy.ClampNotice</c> 回落默认档（暗幕可跳），<b>不夹到最近一端</b>：
        /// 把一处存档损坏静默放大成"强制不可跳"＝替它把用户所有退出出口关掉。</summary>
        public int? EyeRestNotice { get; set; }

        /// <summary>前台是全屏应用时让路（放 PPT / 放映 / 全屏游戏不被遮罩砸）。默认开。</summary>
        public bool? EyeRestDeferOnFullscreen { get; set; }

        /// <summary>
        /// 屏幕画布总开关（<b>默认开</b>）：这是一条已经做完的功能，"从没表过态"不该被读成"用户关过"。
        /// 关掉之后：画布那批快捷键不再注册（不去抢别的软件的键位）、托盘那一项整条消失，
        /// 而按习惯键位仍会给一句"要先在设置里打开"——留一条哑键是最坏的做法。
        /// </summary>
        public bool? CanvasEnabled { get; set; }

        /// <summary>
        /// 「截图带画布」（<b>默认开</b>＝截图里能看到画布上的笔迹，与批次 WH 之前的行为一致）。
        /// <para>
        /// 关掉之后：抓那一帧之前先把画布那块玻璃收起来，抓完立刻还回去——于是"讲解时随手画的圈"
        /// 不会跟着进图。要的是这张图给别人看、圈是给自己看的场合。
        /// </para>
        /// </summary>
        public bool? CanvasInScreenshots { get; set; }

        /// <summary>
        /// 光标那块圆的半径（<b>DIP</b>，不是物理像素；批次 RN 应发起人点名"光晕半径要能调"）。
        /// <para>
        /// 一个数管两块圆：关掉幕布时它是光标光晕，开着幕布时它是那块亮区（批次 S4-⑥ 把它们合成了一块）。
        /// 刻意<b>不</b>拆成两根滑杆——拆了就等于把那次合并重新拆开，还能调出"亮区比光晕大"这种两值同时成立的形状。
        /// </para>
        /// <para>读写两侧都过 <c>CursorCircle.ClampRadiusDip</c>（档里出现 0／负／NaN 都不许换算出"屏幕上什么都没有"的那块圆）。</para>
        /// </summary>
        public double? CursorCircleRadiusDip { get; set; }

        /// <summary>
        /// 截屏这一族的总开关（<b>默认开</b>：截图 / 贴图 / 识字是一条已经做完的功能，
        /// "从没表过态"不该被读成"用户关过"——与 <see cref="CanvasEnabled"/> 同一条判据）。
        /// <para>关掉之后：三条"发起框选"的全局热键<b>整条不注册</b>（F1／F3 是裸功能键，
        /// 替一个关掉的功能继续占着＝让所有软件永久失去 Help／截图键），托盘里那三项也整条消失。
        /// 两条"管理已经贴在那里的图"的动作（显隐／穿透）<b>不受这条影响</b>——贴图窗可能还钉在桌面上，
        /// 而显隐那条是"贴图忽然点不动了"唯一的键盘出口。判据在 <c>Core/Hotkeys/CaptureGate</c>。</para>
        /// </summary>
        public bool? CaptureEnabled { get; set; }
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

    /// <summary>落点规则本体在 <see cref="StarMark.Abstractions.UserDataPaths"/>（五份用户数据档共用一条）。</summary>
    public static string ResolveSettingsPath() => StarMark.Abstractions.UserDataPaths.Settings();
}