#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using StarMark.Abstractions;
using StarMark.Core.Health;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 单个桌面组件的独立无边框窗口（对标 DeskBox WidgetWindowBase + 各 FeatureWidgetWindow）：
/// 亚克力圆角、标题栏拖动、边缘吸附、右下角缩放、置顶、右键菜单增减组件、
/// 快捷启动格支持拖入文件/网址与 StarMark 置顶条目。
/// 位置/尺寸/置顶状态持久化到 widgets.json。
/// </summary>
public sealed partial class WidgetWindow : Window
{
    private readonly string _instanceId;
    private readonly WidgetKind _kind;
    private readonly WidgetStorage _storage;
    private readonly IItemRepository? _repo;
    private readonly WidgetManager _manager;

    private IWidgetTicker? _ticker;
    private bool _styled;
    private bool _shuttingDown;
    private WidgetInstanceConfig _config;
    private Border? _dropHint;
    private bool _dropEnterLogged;      // 拖放进来的那句证词只记一次（见 QuickLaunch_DragEnter）

    // ── 拖动/缩放状态（全部使用 Win32 物理像素，避免 DIP 与 AppWindow 物理坐标混用）──
    private bool _dragging;
    private bool _resizing;
    /// <summary>
    /// Ctrl+拖动协同移动（DeskBox CoordinatedMove）的参与者快照：同屏可见的其它组件及其起始矩形。
    /// 只在按下时招募一次——中途「按 Ctrl 松 Ctrl」不应改变参与者集合，否则会出现半截跟随。
    /// </summary>
    private List<(WidgetWindow Window, RectInt32 Start)> _coordPeers = new();
    private bool _coordinated;
    private string _resizeDir = "se";
    private WindowInterop.POINT _gestureStart;
    private RectInt32 _gestureStartRect;

    // 吸附会话状态：一次拖动内只采集一次候选目标与 DPI 缩放后的阈值，
    // 并保留上一帧的吸附结果做 sticky 迟滞（DeskBox ResizeGuideOverlayService 同款做法）。
    private WidgetSnapTarget[] _snapTargets = Array.Empty<WidgetSnapTarget>();
    private RectInt32? _snapWorkArea;
    private int _snapSpacing = WidgetSnapCalculator.DefaultSpacing;
    private int _snapEngage = WidgetSnapCalculator.DefaultEngageThreshold;
    private int _snapRelease = WidgetSnapCalculator.DefaultReleaseThreshold;
    private WidgetSnapMatch? _stickyHorizontal;
    private WidgetSnapMatch? _stickyVertical;
    private bool _layerAttached;

    // ── 胶囊模式（Phase B）：当前外壳呈现模式 + 缩放柄引用 + 右键菜单项引用 ──
    private WidgetChromeMode _chromeMode = WidgetChromeMode.Standard;
    private readonly List<ResizeGrip> _grips = new();
    /// <summary>收起为胶囊时的固定宽度（物理像素）。胶囊模式下任何尺寸变更都会被夹回此宽 × 标题高度。
    /// 宽度钳制在 [CapsuleMinWidth, CapsuleMaxWidth]，避免历史「胶囊拉伸」测试残留的超长宽度被持久化复用。</summary>
    private int _capsuleWidth;
    private const int CapsuleMinWidth = 160;
    private const int CapsuleMaxWidth = 360;
    /// <summary>收起为胶囊 / 悬停预览时统一采用的宽度（物理像素，DPI 无关；所有胶囊一致，避免宽窄不一）。
    /// 悬停预览也用此统一宽度，只有「点击展开」才恢复到组件原本的位置与尺寸。</summary>
    private const int UnifiedCapsuleWidth = 240;
    private MenuFlyout? _contextMenu;
    private MenuFlyoutItem? _collapseMenuItem;
    private MenuFlyoutItem? _hideChromeMenuItem;
    /// <summary>外观编辑器是否已在打开中（单例守护，避免多次右键「外观…」叠加多个浮层）。</summary>
    private bool _appearanceEditorOpen;

    // ── 胶囊三段式热区 / 悬停预览 / 隐私（B-10）──
    /// <summary>悬停预览中：此时窗口临时展开到正常尺寸，但外壳模式仍是 Compact，
    /// 离开窗口即收回胶囊；该标志让尺寸夹取（OnAppWindowChanged）临时放行。</summary>
    private bool _peeking;
    private bool _fgLoadedHooked;

    /// <summary>
    /// 当前生效的文本缩放系数（1.0 = 默认）。
    /// 除了驱动套用，还用于判断要不要给「晚到的文本」补扫 —— 见 <see cref="ContentHost_LayoutUpdated"/>。
    /// </summary>
    private double _textScale = 1.0;

    /// <summary>上一次补扫的时刻（补扫按 400ms 节流，避免布局抖动期被高频触发）。</summary>
    private DateTimeOffset _lastTextPassAt;

    /// <summary>标记曾被我们显式上过前景色的文本（弱引用），恢复全局（无前景覆盖）时只清这些，
    /// 不误动样式自带的灰度等前景。</summary>
    private readonly ConditionalWeakTable<TextBlock, object> _coloredMarker = new();
    /// <summary>曾被显式上前景色的文本弱引用快照（配合 _coloredMarker 用于恢复全局时精准清除）。</summary>
    private readonly List<WeakReference<TextBlock>> _coloredRefs = new();

    /// <summary>当前生效的圆角半径（物理像素），用于把「窗口本身」裁成圆角矩形
    /// （SetWindowRgn），真正圆化组件外形，而非只给内部 Border 加圆角。</summary>
    private double _currentCornerRadius = 8;

    // ── 稳定停靠位（修复悬停预览导致的堆叠漂移 / 展开后不恢复原位置）──
    /// <summary>胶囊稳定停靠位（物理像素）：仅在「首次进入胶囊」或「强制重排」时计算一次，
    /// 悬停预览收回时直接回到此位，不再重算堆叠（否则每次收起都会把同列胶囊往下推、最终移出屏幕）。</summary>
    private RectInt32? _capsuleRect;
    /// <summary>收起前记录的正常态位置/尺寸（物理像素），供「点击展开」恢复到收起前的原位置。</summary>
    private RectInt32? _expandedRect;

    public WidgetKind Kind => _kind;
    public bool IsVisible => AppWindow.IsVisible;

    /// <summary>正在被用户拖动 / 缩放 —— 协同移动招募参与者时要排除，避免两个手势互相打架。</summary>
    public bool IsDragBusy => _dragging || _resizing;

    /// <summary>当前窗口矩形（物理像素）。协同移动取起始矩形用。</summary>
    public RectInt32 CurrentRect => WindowInterop.GetWindowRect(this);

    /// <summary>
    /// 协同移动中的「跟随」：按 delta 平移到 start + delta。
    /// 协同意义上是整体搬家，因此**不做吸附**（吸附只作用于用户正在拖的那个窗口，
    /// 否则整排组件会被各自的吸附线拉扯抖动）。
    /// </summary>
    /// <param name="dx">相对协同起点的水平位移（物理像素）。</param>
    /// <param name="dy">相对协同起点的垂直位移。</param>
    /// <param name="start">本窗口在协同开始时的矩形。</param>
    public void MoveByCoordinated(int dx, int dy, RectInt32 start)
    {
        var r = new RectInt32(start.X + dx, start.Y + dy, start.Width, start.Height);
        // 胶囊态必须同步推进「稳定停靠位」，否则松手后再次收起会跳回旧位置
        if (_chromeMode == WidgetChromeMode.Compact && _capsuleRect is not null)
            _capsuleRect = r;
        AppWindow.MoveAndResize(r);
    }

    /// <summary>供组件内容工厂构造具体组件（如 QuickLaunchWidget）时取用依赖。</summary>
    internal WidgetStorage Storage => _storage;
    internal IItemRepository? Repository => _repo;
    internal WidgetManager Manager => _manager;

    public WidgetWindow(string instanceId, WidgetInstanceConfig config, WidgetStorage storage, IItemRepository? repo, WidgetManager manager)
    {
        _instanceId = instanceId;
        // 半透明亚克力外观：InitializeComponent 之后统一由 ApplyAppearanceCore 应用
        // （材质可选亚克力/云母/不透明，不透明度来自设置，见 Helpers/WidgetAppearance）
        _kind = config.Kind;
        _storage = storage;
        _repo = repo;
        _manager = manager;
        _config = config;

        // PO-2 的分段：一颗组件窗口从"要显示"到"点亮"实测 94~156 ms，22 个实例串在启动那一段里
        // （恢复共 ≈1.2~1.4 s）。哪一段是大头只能量，不能再猜——这几条尺子量出来的结论写在
        // WindowInterop.RemoveDefaultWindowFrame 的注释里（真凶是 SetBorderAndTitleBar，≈68 ms/颗）。
        StartupProfile.Measure($"组件 XAML 加载 {_kind}", InitializeComponent, logWhenMs: 10);
        WindowInterop.TrackWindow(this);   // 供弹窗按发起组件窗口所在显示器居中
        // 主题必须在首次渲染前就盖成<b>具体</b>的 Light/Dark，不能等到 Reveal：
        // 构造期根元素 RequestedTheme 仍是 Default ⇒ 继承"启动时已冻结"的应用级主题，
        // 于是「OS 深色 + 应用浅色」下标题栏（无显式前景的 WidgetTitle/WidgetGlyph）会按深色桶
        // 取到近白画笔——浅色组件上"组件名看不见"就是这么来的。Reveal 里仍会再套一次（设置可能已改）。
        ThemeManager.Apply(this, App.Services.GetRequiredService<SettingsStore>().LoadTheme());
        StartupProfile.Measure($"组件外观套用 {_kind}", ApplyAppearanceCore, logWhenMs: 10);
        // 主题解析完成后（Default → 浅/深）或运行期切换主题时，按真实主题重挂材质与重铺表面。
        // 否则构造期 ActualTheme 仍是 Default（被当作浅色），浅色模式下的材质/表面会一直用错；
        // 且主题翻转后控制器仍停在旧主题观感 —— 正是「浅色模式材质不正确」的根因之一。
        RootBorder.ActualThemeChanged += (_, _) => RefreshAppearance();

        WidgetGlyph.Text = KindGlyph(_kind);
        ApplyTitle();   // 标题栏文本 + 窗口标题（优先取用户重命名的名字）

        StartupProfile.Measure($"组件内容构建 {_kind}", BuildContent, logWhenMs: 10);
        WireChrome();
        SetupQuickLaunchDrop();

        // 内容里有一部分是**异步**才建出来的（天气指标格、速览的常看按钮、列表项容器），
        // 首次套用时它们还不存在。这里补一条节流扫描，让晚到的文本也能拿到当前缩放系数。
        ContentHost.LayoutUpdated += ContentHost_LayoutUpdated;

        // 可见性变化要驱动内容里的定时器（时钟/世界时钟…），但不必为此按类型分支：
        // 没实现 IWidgetTicker 的内容 _ticker 为 null，这一句本身就是空操作。
        AppWindow.Changed += (_, e) =>
        {
            if (e.DidVisibilityChange) _ticker?.UpdateRunning(AppWindow.IsVisible);
            };

        // 胶囊模式尺寸夹取：任何经由系统（原生边框 / Win+方向键吸附）的尺寸变更都夹回胶囊尺寸，
        // 与隐藏 grip + LockNativeResize 共同确保「收起为胶囊时禁止修改胶囊大小」。
        AppWindow.Changed += OnAppWindowChanged;

        // 记录展开态位置/尺寸，供「点击展开」恢复到收起前的原位（compact 态持久化的 X/Y 会被改写为胶囊停靠位，
        // 故此处单独以 _expandedRect 保存，避免展开后组件跳到屏幕边缘）。
        if (_config.Width > 0 && _config.Height > 0)
            _expandedRect = new RectInt32((int)_config.X, (int)_config.Y, (int)_config.Width, (int)_config.Height);

        Closed += WidgetWindow_Closed;
    }

    /// <summary>窗口尺寸变化：重算圆角区域（任意模式都保持圆角窗口），并夹回胶囊尺寸（仅胶囊态、非悬停预览）。</summary>
    private void OnAppWindowChanged(object? sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs e)
    {
        if (!e.DidSizeChange) return;
        // 圆角区域随窗口尺寸变化重算，使窗口真正保持圆角（悬停预览临时放大时也圆角）
        ApplyRoundedWindow();
        // 胶囊态且非悬停预览：任何非预期尺寸变更夹回胶囊尺寸（_peeking 期间放行）
        if (_chromeMode == WidgetChromeMode.Compact && !_peeking && _capsuleWidth > 0)
        {
            var scale = WindowInterop.GetScale(this);
            var capH = (int)(36 * scale);
            var r = WindowInterop.GetWindowRect(this);
            if (r.Height != capH || r.Width != _capsuleWidth)
                AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, _capsuleWidth, capH));
        }
    }

    /// <summary>
    /// 把「窗口本身」裁成圆角矩形（SetWindowRgn）：圆角半径来自当前外观
    /// （<see cref="_currentCornerRadius"/>）。半径 ≤ 0 时移除区域（直角窗口）。
    /// 这样圆角改变的是组件真实外形，而非仅内部 Border 形状。
    /// </summary>
    private void ApplyRoundedWindow()
    {
        // 真正圆化窗口：用 SetWindowRgn 把窗口裁成圆角矩形。半径取自当前外观
        // （_currentCornerRadius，逻辑像素）；尺寸用 GetWindowRect 的物理像素，按 DPI 换算，
        // 避免缩放屏上「修改前/后两种圆角叠加」或裁切错位（直接套用外观 / 尺寸变化 / 实时预览都走这里）。
        try { WindowInterop.SetRoundedWindowRegion(this, _currentCornerRadius); }
        catch { /* 取不到窗口句柄/尺寸时跳过，下次套用外观或尺寸变化会再算 */ }
    }

    /// <summary>实例唯一 ID（区分同类型多个组件）。</summary>
    public string InstanceId => _instanceId;

    /// <summary>该实例的持久化配置（位置/尺寸/置顶，引用自存储数据，原地改动后由 PersistBounds 落盘）。</summary>
    public WidgetInstanceConfig Config => _config;

    /// <summary>
    /// 取本窗口内的组件内容实例（跨组件联动用，如待办右键「开始专注」要把任务名交给番茄钟）。
    /// 类型不对时返回 null 而不是抛——调用方本来就该准备着"这个实例没开番茄钟"。
    /// 名字带 Find：WinUI 的 <c>Window.Content</c> 已被宿主占用，同名会隐藏它。
    /// </summary>
    public T? FindContent<T>() where T : UIElement => ContentHost.Children.OfType<T>().FirstOrDefault();

    /// <summary>显示窗口（首次显示时完成样式、位置、置顶初始化）。</summary>
    public void Reveal()
    {
        // 首装（样式/尺寸/外壳）与每次都要走的"点亮"分开量：PO-2 就是靠这条分段把"一颗组件 ≈100 ms"
        // 追到 <c>SetBorderAndTitleBar</c> 那一句（≈68 ms/颗），合并回去就只剩"就是慢"这一句可说。
        if (!_styled)
            StartupProfile.Measure($"组件首装样式 {_kind}", StyleForTheFirstTime, logWhenMs: 10);
        StartupProfile.Measure($"组件显示点亮 {_kind}", ShowOnDesktop, logWhenMs: 10);
        _ticker?.UpdateRunning(AppWindow.IsVisible);
    }

    private void StyleForTheFirstTime()
    {
        StartupProfile.Measure($"组件去边框 {_kind}", () =>
            WindowInterop.RemoveDefaultWindowFrame(this), logWhenMs: 8);
        StartupProfile.Measure($"组件圆角 {_kind}", () =>
            WindowInterop.ApplyRoundedCorners(this), logWhenMs: 8);
        var pref = App.Services.GetRequiredService<SettingsStore>().LoadTheme();
        StartupProfile.Measure($"组件主题套用 {_kind}", () => ThemeManager.Apply(this, pref), logWhenMs: 8);
        StartupProfile.Measure($"组件定位尺寸 {_kind}", ApplyInitialBounds, logWhenMs: 8);
        // 外壳模式（标准/胶囊/隐藏）必须在初始尺寸确定后再套用：收起态要把窗口缩到标题高度
        _chromeMode = _config.ChromeMode;
        StartupProfile.Measure($"组件外壳模式 {_kind}", () => ApplyChromeMode(_chromeMode), logWhenMs: 8);
        _styled = true;
        // 构造期 ActualTheme 可能仍是 Default，按真实主题重挂毛玻璃控制器
        StartupProfile.Measure($"组件外观重挂 {_kind}", RefreshAppearance, logWhenMs: 8);
    }

    private void ShowOnDesktop()
    {
        AppWindow.Show();
        // 关键：仅 AppWindow.Show() 对「挂在桌面图标层（所有者=Explorer SHELLDLL_DefView）」的窗口
        // 常常无法把窗口重新点亮 —— 这是 WinUI3 的已知坑（AppWindow.Show 不总触发真实的 SW_SHOW，
        // 跨进程 owner 的窗口尤甚），表现为「隐藏后再显示大多数时候点不出来」。DeskBox 同款做法：
        // 显式补一发原生 ShowWindow(SW_SHOWNOACTIVATE)，确保窗口真正点亮又不抢焦点。
        WindowInterop.ShowWindow(WindowInterop.GetHwnd(this), WindowInterop.SW_SHOWNOACTIVATE);
        // 置顶与"贴在桌面层"互斥：置顶时作为普通顶层窗口 + WS_EX_TOPMOST 真正常驻最前；
        // 默认未开启时挂到桌面图标层（落在应用窗口之下、桌面图标之上）。由 ApplyTopmost 决定挂载/脱离。
        ApplyTopmost();
    }

    /// <summary>
    /// 挂载到桌面图标层，使组件真正"贴在桌面上"：
    /// 位于所有应用窗口之下、桌面图标之上，Win+D 与点击桌面都不会挤走它。
    /// 挂载失败时静默降级为普通窗口（DeskBox 同样的 best-effort 策略）。
    /// </summary>
    private void AttachToDesktopLayer()
    {
        if (_layerAttached) return;
        _layerAttached = WidgetLayerService.AttachToDesktopLayer(WindowInterop.GetHwnd(this));
    }

    /// <summary>拖动/交互开始时瞬态浮起，避免被其他组件或窗口遮挡。</summary>
    private void RaiseTransient() =>
        WidgetLayerService.RaiseTransient(WindowInterop.GetHwnd(this));

    /// <summary>临时隐藏（实例保留，托盘/设置可一键恢复）。
    /// <b>不负责落盘</b>：几何由调用侧整批写好（<c>WidgetManager.HideTemporaryAll</c>）——
    /// 逐个 Save 时"隐藏全部组件"＝N 趟整档读写，而隐藏本身并不改几何，那一趟多半是白写的。</summary>
    public void HideTemporary()
    {
        // 与 Reveal 对称：AppWindow.Hide() 对跨进程 owner（桌面图标层）的窗口同样不可靠，
        // 显式补原生 ShowWindow(SW_HIDE) 确保真正隐藏，避免下次点亮时状态错乱。
        WindowInterop.ShowWindow(WindowInterop.GetHwnd(this), WindowInterop.SW_HIDE);
        AppWindow.Hide();
        _ticker?.UpdateRunning(AppWindow.IsVisible);
    }

    /// <summary>彻底关闭（组件被移除时调用）。<b>不负责落盘</b>，理由同 <c>HideTemporary</c>：
    /// 几何由调用侧整批写好（<c>WidgetManager.CloseAll</c>）。原先这里也各写一趟，于是"关闭全部组件"＝
    /// N 趟整档读写，而 <c>CloseInternal</c> 的 <c>persist</c> 旗标压根没人读——退出时该写的没写清、
    /// 应用快照时不该写的却写了。</summary>
    public void Shutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _ticker?.Stop();
        Close();
    }

    // ───────────────────────── 内容构建 ─────────────────────────

    /// <summary>组件图标；来源为 <see cref="WidgetRegistry"/>，避免各处重复 switch。</summary>
    public static string KindGlyph(WidgetKind kind) =>
        WidgetRegistry.Default.TryGet(kind, out var d) ? d.Glyph : "▦";

    private void BuildContent()
    {
        ContentHost.Children.Clear();
        var content = WidgetContentFactory.Default.Build(_kind, this);
        _ticker = content as IWidgetTicker;
        ContentHost.Children.Add(content);

        // 内容重建后必须补一次外观套用，原因有二：
        // ① 构造顺序是 InitializeComponent → ApplyAppearanceCore → BuildContent，上一次 ApplyAppearanceCore
        //    执行时 ContentHost 还是个空的 StackPanel（XAML 自带元素，已被 Loaded，所以 SetFg 立即执行却什么也遍历不到），
        //    于是新内容永远拿不到已保存的外观（含文本缩放）——持久化的字号在启动后会无声失效。
        // ② 基准字号记在文本元素自身（附加属性），随旧内容一起丢弃，新文本的基准天然是原始字号，
        //    不存在「缓存与内容错位」的可能；点「恢复全局」（系数回到 1.0）即精确还原初始大小。
        ResetTextStyleCache();
        try { ApplyAppearanceCore(); } catch { }
    }

    /// <summary>
    /// 清空「已上色文本」记录（内容重建时调用，与 ContentHost.Children.Clear 配套）。
    /// <para>
    /// 基准字号<b>不需要</b>在这里清：它记在元素自身的附加属性上（见 <see cref="WidgetTextScale"/>），
    /// 元素随内容一起被丢弃，基准自然一起消失。早先把它放在窗口级字典里，
    /// 「清字典」与「换内容」这两件事一旦不同步，基准就会错位到已缩放的字号上。
    /// </para>
    /// </summary>
    private void ResetTextStyleCache()
    {
        _coloredMarker.Clear();
        _coloredRefs.Clear();
    }

    /// <summary>按窗口当前主题从应用级主题字典解析组件画笔。</summary>
    private Brush WidgetBrush(string key)
        => ThemeBrush.For(RootBorder.ActualTheme, key)
           ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
}