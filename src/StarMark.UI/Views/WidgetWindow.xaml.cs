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

    // ───────────────────────── 窗口样式/位置 ─────────────────────────

    private void ApplyInitialBounds()
    {
        var (x, y, w, h) = ResolveInitialRect();
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    /// <summary>
    /// 解析组件初始物理像素矩形（每显示器拓扑布局，Phase B）：
    /// 优先用 <see cref="WidgetInstanceConfig.MonitorId"/> + DIP 偏移按该显示器当前 DPI 重新换算；
    /// 显示器已断开（MonitorId 找不到）或旧实例无记录时，回退物理像素并做越界回收（落到主显示器工作区）。
    /// </summary>
    private (int X, int Y, int W, int H) ResolveInitialRect()
    {
        var scale = WindowInterop.GetScale(this);

        if (_config.Width > 0 && _config.Height > 0)
        {
            // ── 拓扑记忆：有显示器记录 → 按该显示器当前 DPI 还原 ──
            if (!string.IsNullOrEmpty(_config.MonitorDevice) &&
                WindowInterop.FindMonitorWorkAreaByDevice(_config.MonitorDevice) is { } work)
            {
                var s = WindowInterop.GetMonitorScaleByDevice(_config.MonitorDevice);
                var left = work.X + (int)(_config.MonitorLeft * s);
                var top = work.Y + (int)(_config.MonitorTop * s);
                var w = (int)(_config.MonitorWidth * s);
                var h = (int)(_config.MonitorHeight * s);
                return ClampToWorkArea(left, top, w, h, work);
            }

            // ── 旧实例 / 显示器已断开：物理像素 + 越界回收 ──
            var px = (int)_config.X;
            var py = (int)_config.Y;
            var pw = (int)_config.Width;
            var ph = (int)_config.Height;
            if (WindowInterop.IsOffScreen(new RectInt32(px, py, pw, ph)))
            {
                // 原显示器没了：落到主显示器工作区，避免组件消失
                var primary = WindowInterop.PrimaryWorkArea();
                px = primary.X + 24;
                py = primary.Y + 24;
            }
            var target = WindowInterop.MonitorWorkAreaContaining(px, py, pw, ph) ?? WindowInterop.PrimaryWorkArea();
            return ClampToWorkArea(px, py, pw, ph, target);
        }

        // 无尺寸记录：用类型默认尺寸（物理像素），夹到当前显示器
        var w0 = (int)(WidgetStorage.DefaultWidth(_kind) * scale);
        var h0 = (int)(WidgetStorage.DefaultHeight(_kind) * scale);
        var index = (int)_kind;
        var x0 = (int)((120 + index * 28) * scale);
        var y0 = (int)((90 + index * 28) * scale);
        return ClampToWorkArea(x0, y0, w0, h0, WindowInterop.GetWorkArea(this));
    }

    private static (int X, int Y, int W, int H) ClampToWorkArea(int x, int y, int w, int h, RectInt32 work)
    {
        if (x < work.X) x = work.X + 8;
        if (y < work.Y) y = work.Y + 8;
        if (x + w > work.X + work.Width) x = Math.Max(work.X + 8, work.X + work.Width - w - 8);
        if (y + h > work.Y + work.Height) y = Math.Max(work.Y + 8, work.Y + work.Height - h - 8);
        return (x, y, w, h);
    }

    /// <summary>把本窗口当前几何单独落盘（一次读 + 最多一次写）。</summary>
    internal void PersistBounds() => _storage.Mutate(WriteBoundsInto);

    /// <summary>
    /// 把本窗口当前几何<b>写进给定的存档</b>——不读盘、不落盘。整批窗口共用一次读档一次落盘时走这里
    /// （<see cref="WidgetManager"/> 的 PersistBoundsFor，以及本类的 <see cref="PersistPositionsAfterDrag"/>）。
    /// </summary>
    /// <returns>有没有写到东西。<b>实例已从存档里消失（正在被移除）时返回 false，一次盘都不该写</b>。</returns>
    internal bool WriteBoundsInto(WidgetStoreData data)
    {
        try
        {
            var r = WindowInterop.GetWindowRect(this);
            if (r.Width <= 0 || r.Height <= 0) return false;
            var inst = data.Instances.FirstOrDefault(i => i.Id == _instanceId);
            if (inst is null) return false;

            // 收起为胶囊时，窗口当前显示的是「胶囊」而非展开态：
            //  - 展开态位置/尺寸必须保持（用 _expandedRect，其次 _config），否则点击展开会跳到屏幕边缘；
            //  - 胶囊停靠位单独写入 CapsuleX/CapsuleY（供下次启动/悬停收回稳定回到原位）。
            if (_chromeMode == WidgetChromeMode.Compact)
            {
                var scale = WindowInterop.GetScale(this);
                var capH = (int)(36 * scale);
                var ex = _expandedRect
                         ?? new RectInt32((int)_config.X, (int)_config.Y, (int)_config.Width, (int)_config.Height);
                inst.X = ex.X;
                inst.Y = ex.Y;
                inst.Width = ex.Width;
                inst.Height = ex.Height;
                // 同步胶囊停靠位（拖动胶囊后此处即最新位置，悬停收回/重启都回到这里）
                _capsuleRect = new RectInt32(r.X, r.Y, _capsuleWidth > 0 ? _capsuleWidth : r.Width, capH);
                inst.CapsuleX = r.X;
                inst.CapsuleY = r.Y;
            }
            else
            {
                inst.X = r.X;
                inst.Y = r.Y;
                inst.Width = r.Width;
                inst.Height = r.Height;
            }
            inst.Topmost = _config.Topmost;
            inst.ChromeMode = _chromeMode;
            inst.PrivacyMode = _config.PrivacyMode;

            // 每显示器拓扑：记录所在显示器设备名 + DIP 偏移/尺寸（恢复时按当前 DPI 还原）
            try
            {
                var hwnd = WindowInterop.GetHwnd(this);
                var (device, work, s) = WindowInterop.GetMonitorForWindow(hwnd);
                if (!string.IsNullOrEmpty(device))
                {
                    inst.MonitorDevice = device;
                    inst.MonitorLeft = (r.X - work.X) / s;
                    inst.MonitorTop = (r.Y - work.Y) / s;
                    var dipW = (_chromeMode == WidgetChromeMode.Compact ? _config.Width : r.Width) / s;
                    var dipH = (_chromeMode == WidgetChromeMode.Compact ? _config.Height : r.Height) / s;
                    inst.MonitorWidth = dipW;
                    inst.MonitorHeight = dipH;
                }
            }
            catch { /* 拿不到显示器信息时不写拓扑字段，回退物理像素路径 */ }

            return true;
        }
        catch (Exception ex)
        {
            StarLog.Error($"组件位置持久化失败 ({_kind})", ex);
            return false;
        }
    }

    /// <summary>
    /// 该窗口当前是否收起为胶囊。<see cref="WidgetChromeMode.Compact"/> 时窗口实际显示的是胶囊矩形，
    /// 读到的 <c>GetWindowRect</c> 是胶囊尺寸而非展开尺寸——布局/快照取几何须避开它、改用已持久化的展开位（inst.X/Y/W/H）。
    /// </summary>
    public bool IsCollapsed => _chromeMode == WidgetChromeMode.Compact;

    private void ApplyTopmost()
    {
        var dark = RootBorder.ActualTheme == ElementTheme.Dark;
        if (_config.Topmost)
        {
            // 置顶：必须是普通顶层窗口（脱离桌面层），再用 WS_EX_TOPMOST 真正常驻最前。
            if (_layerAttached)
            {
                WidgetLayerService.DetachFromDesktopLayer(WindowInterop.GetHwnd(this));
                _layerAttached = false;
            }
            WindowInterop.SetTopmost(this, true);
            // 按钮始终可见：置顶时高亮强调色
            PinIcon.Foreground = ThemeBrush.Resolve(dark, "AppAccentBrush");
            PinButton.Background = ThemeBrush.Resolve(dark, "AppAccentSoftBrush");
            ToolTipService.SetToolTip(PinButton, "取消置顶");
        }
        else
        {
            WindowInterop.SetTopmost(this, false);
            // 默认未开启：挂到桌面层（贴在桌面上）；按钮仍清晰可见（次级前景色，不再透明不可见）。
            if (!_layerAttached) AttachToDesktopLayer();
            PinIcon.Foreground = ThemeBrush.Resolve(dark, "TextFillColorSecondaryBrush");
            PinButton.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            ToolTipService.SetToolTip(PinButton, "置顶显示");
        }
    }

    /// <summary>
    /// 应用「毛玻璃材质 + 表面不透明度 + 每实例外观覆盖」（构造时与设置变更后共用）。
    /// 每实例可在 <see cref="WidgetInstanceConfig.Appearance"/> 覆盖材质/背景/前景/边框/圆角/文本缩放，
    /// 任一字段为 null 即回退到全局设置（设置页「常规 → 外观」）。
    /// </summary>
    private void ApplyAppearanceCore()
    {
        try
        {
            var ov = _config.Appearance;
            var globalKind = WidgetAppearance.Backdrop();

            // 自定义背景色优先覆盖材质（实色铺满会盖住霜化背景，故强制 None）
            var useCustomBg = !string.IsNullOrWhiteSpace(ov?.BackgroundColor);
            var kind = useCustomBg ? StarMark.Abstractions.WidgetBackdropKind.None : (ov?.Backdrop ?? globalKind);
            StartupProfile.Measure($"组件材质挂载 {_kind}", () => WidgetAppearance.ApplyBackdrop(
                this, kind, WidgetAppearance.Opacity(), RootBorder.ActualTheme), logWhenMs: 10);

            Brush surface = useCustomBg
                ? (WidgetAppearance.ParseColorBrush(ov!.BackgroundColor!) ?? WidgetAppearance.SurfaceBrush(RootBorder.ActualTheme, globalKind))
                : WidgetAppearance.SurfaceBrush(RootBorder.ActualTheme, kind);

            // 背衬画笔已接管整窗着色（除实色 None 外的所有材质：亚克力/云母/纯色）时，内容表面必须透明，
            // 让这一层扁平背衬单独着色——若再叠一层内容实色就会两次 alpha 叠加导致过实/发灰。
            // 背衬未生效（理论上不会，实色 None 走 else）时保留 SurfaceBrush 的实色兜底，绝不变透明幽灵窗。
            if (!useCustomBg &&
                kind != StarMark.Abstractions.WidgetBackdropKind.None &&
                WidgetAppearance.IsBackdropSurfaceActive(this))
            {
                surface = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }

            RootBorder.Background = surface;
            DragBar.Background = surface;

            // 前景（文本）色 + 文本缩放
            ApplyTextAppearanceCore();

            // 边框色 / 粗细
            RootBorder.BorderBrush = !string.IsNullOrWhiteSpace(ov?.BorderColor)
                ? WidgetAppearance.ParseColorBrush(ov.BorderColor!) ?? RootBorder.BorderBrush
                : (ThemeBrush.For(RootBorder.ActualTheme, "WidgetBorderBrush") ?? new SolidColorBrush(Microsoft.UI.Colors.Gray));
            RootBorder.BorderThickness = ov?.BorderThickness is { } bt
                ? new Thickness(Math.Clamp(bt, 0, 12))
                : new Thickness(1);

            // 圆角：内部 Border 跟随圆角（内容裁进圆角矩形），同时把「窗口本身」用 SetWindowRgn
            // 裁成圆角矩形——这样圆角改变的是组件真实外形（半径 0 即直角窗口），而不只是内部形状。
            var radius = ov?.CornerRadius is { } cr ? Math.Clamp(cr, 0, 48) : 8;
            _currentCornerRadius = radius;
            RootBorder.CornerRadius = new CornerRadius(radius);
            if (DragBar is not null)
                DragBar.CornerRadius = new CornerRadius(radius, radius, 0, 0);
            if (ContentScroll is not null)
                ContentScroll.CornerRadius = new CornerRadius(radius);
            ApplyRoundedWindow();   // 真正圆化窗口（任意模式下都生效）

            // 文本缩放：改为「直接缩放文本字号」（文本本身缩放），而非整块内容相对中心放缩，
            // 故放大时文本仍留在布局内、ScrollViewer 可滚动查看，不会因超出组件范围被裁切而消失。
            // 实际遍历在 ApplyForeground 的 SetFg 中与前景色一起套用（二者共享一次可视树遍历）。
        }
        catch (Exception ex)
        {
            // 兜底：外观相关的任何异常都不能冒出构造函数——组件窗口在构造期抛错会让
            // 「全部显示」「恢复组件」等操作直接演变成 UI 线程未处理异常（应用卡死后崩溃）。
            StarLog.Error($"应用组件外观失败 ({_kind})", ex);
            try
            {
                var fallback = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                RootBorder.Background = fallback;
                DragBar.Background = fallback;
            }
            catch { }
        }
    }

    /// <summary>
    /// 只套用「文本相关」的外观（前景色 + 文本缩放），<b>不碰材质与背景</b>。
    /// <para>
    /// 调文本缩放时若走整块 <see cref="ApplyAppearanceCore"/>，会顺带把材质/背景重挂一遍 ——
    /// 用户看到的就是「我只是改了字号，背景颜色却变了」，且在材质设置未即时生效时尤为明显。
    /// 编辑器里拖字号滑杆只走这条路径。
    /// </para>
    /// </summary>
    private void ApplyTextAppearanceCore()
    {
        try { ApplyForeground(_config.Appearance); }
        catch (Exception ex) { StarLog.Error($"应用组件文本外观失败 ({_kind})", ex); }
    }

    /// <summary>
    /// 应用每实例前景（文本）色 + 文本缩放。关键约束（踩坑 #60）：WinUI 3 中任何“非 TextElement 容器”
    /// （Border / Panel / StackPanel 等）直接调用 SetValue/ClearValue(TextElement.ForegroundProperty)
    /// 都会触发原生 AccessViolation（0xc0000005，Corrupted-State，try/catch 捕获不到，直接杀进程）。
    /// 因此：
    ///  - 有前景色覆盖时：安全地遍历 ContentHost 可视树，仅对真正的文本元素（TextBlock 等 TextElement 子类）
    ///    通过其标准 Foreground setter 上色——绝不触碰容器的 TextElement.ForegroundProperty 附加属性；
    ///  - 无前景色覆盖时（恢复全局）：仅清除我们此前显式上过色的文本（_coloredMarker），让它们回到样式自带前景，
    ///    绝不误动未改过的文本（如 MutedText 的灰度）；
    ///  - 文本缩放：直接改 TextBlock.FontSize（文本本身缩放），而非对内容区做 RenderTransform（否则放大后文本
    ///    相对组件中心放缩、超出组件范围被裁切而消失）。基准字号记在文本元素自身（Helpers/WidgetTextScale），
    ///    保证反复套用不累加、系数回到 1.0 时能精确还原到初始加载大小。
    /// 延迟到 Loaded 之后执行，确保内容子元素已生成。
    /// </summary>
    private void ApplyForeground(WidgetAppearanceOverride? ov)
    {
        if (ContentHost is null) return;
        var panel = ContentHost;
        void SetFg()
        {
            try
            {
                if (WidgetAppearance.ParseColorBrush(ov?.ForegroundColor) is { } fg)
                {
                    SetForegroundDeep(panel, fg);
                }
                else
                {
                    // 恢复全局前景：清掉我们此前列过前景的文本，回到样式默认（ClearValue 安全，TextBlock.Foreground 是标准属性）
                    foreach (var (tb, _) in EnumerateColored())
                        tb.ClearValue(TextBlock.ForegroundProperty);
                    _coloredMarker.Clear();
                }
                // 文本缩放（与前景共享一次遍历）
                _textScale = ov?.TextScale is { } ts ? Math.Clamp(ts, 0.6, 1.8) : 1.0;
                ApplyTextScale(panel, _textScale);
            }
            catch (Exception ex)
            {
                StarLog.Error($"应用组件前景色/文本缩放失败 ({_kind})", ex);
            }
        }
        if (panel.IsLoaded) SetFg();
        else if (!_fgLoadedHooked) { _fgLoadedHooked = true; panel.Loaded += (_, _) => SetFg(); }
    }

    /// <summary>枚举曾被我们显式上过前景色的文本（跳过已回收的弱引用）。</summary>
    private IEnumerable<(TextBlock Tb, object _)> EnumerateColored()
    {
        // ConditionalWeakTable 无枚举 API，改用存活的弱引用快照（内容重建后旧引用自然失效）
        foreach (var weak in _coloredRefs.ToArray())
            if (weak.TryGetTarget(out var tb)) yield return (tb, null!);
    }

    /// <summary>
    /// 递归遍历可视树，仅给文本元素（TextBlock 等 TextElement 子类）设置前景色并记录标记
    /// （用于恢复全局时精准清除）。TextBlock.Foreground 是标准安全 setter，不会像容器上的
    /// SetValue(TextElement.ForegroundProperty) 那样 AV。
    /// </summary>
    private void SetForegroundDeep(DependencyObject parent, Brush fg)
    {
        var n = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock tb)
            {
                tb.Foreground = fg;
                if (!_coloredMarker.TryGetValue(tb, out _))
                {
                    _coloredMarker.AddOrUpdate(tb, new object());
                    _coloredRefs.Add(new WeakReference<TextBlock>(tb));
                }
            }
            SetForegroundDeep(child, fg);
        }
    }

    /// <summary>
    /// 递归遍历可视树，对文本元素按「基准字号 × 系数」设置 FontSize（文本本身缩放，留在布局内、可滚动）。
    /// 基准字号首次见到的 TextBlock 时记录（用其当前有效字号），后续均基于基准计算，故反复套用不累加。
    /// 时钟组件（ClockWidget）自行管理时间/日期字号（自适应 + 文本缩放系数），故在此跳过其内部文本、
    /// 直接把系数下发给它的 <see cref="ClockWidget.TextScale"/>，避免被这里再乘一次导致双重缩放。
    /// </summary>
    private void ApplyTextScale(DependencyObject parent, double scale)
    {
        var n = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ClockWidget cw)
            {
                // 时钟时间字号由组件自适应计算，缩放只通过系数下发，不在此直接改其内部 TextBlock
                cw.TextScale = scale;
                continue;
            }
            if (child is TextBlock tb)
            {
                // 基准只在此元素首次出现时记录一次；之后无论套用多少遍都基于同一个基准，
                // 因此不会出现「缩放后的值被当成新基准」这种越调越偏的问题。
                WidgetTextScale.CaptureBase(tb);
                WidgetTextScale.Apply(tb, scale);
            }
            ApplyTextScale(child, scale);
        }
    }

    /// <summary>
    /// 给「晚到」的文本补一次缩放套用。
    /// <para>
    /// 组件的不少文本是异步建出来的：天气的指标格与逐时格要等数据回来，速览的常看按钮要等查库，
    /// 列表项容器更是虚拟化、滚动到才创建。首次套用时它们根本不在树上，于是永远保持原始字号——
    /// 用户看到的就是「同一块组件里字号忽大忽小」。
    /// </para>
    /// <para>
    /// 只在系数<b>不等于 1.0</b> 时才需要补扫：默认系数下新文本本就是原始字号，不扫也正确，
    /// 白扫反而会在拖动/缩放的布局抖动期反复遍历可视树。
    /// </para>
    /// </summary>
    private void ContentHost_LayoutUpdated(object? sender, object e)
    {
        if (ContentHost is null) return;
        if (WidgetTextScale.IsDefault(_textScale)) return;

        var now = DateTimeOffset.Now;
        if ((now - _lastTextPassAt) < TimeSpan.FromMilliseconds(400)) return;
        _lastTextPassAt = now;

        try { ApplyTextScale(ContentHost, _textScale); }
        catch (Exception ex) { StarLog.Error($"补套用文本缩放失败 ({_kind})", ex); }
    }

    /// <summary>设置变更后重新套用外观（材质 / 不透明度），由 WidgetManager 统一调用。</summary>
    public void RefreshAppearance()
    {
        if (!_styled) return;   // 尚未完成首次样式化的窗口（未 Show）无需刷
        ApplyAppearanceCore();
    }

    /// <summary>
    /// 主窗口切换主题后，把同一主题偏好下发到本组件并立即重挂材质/表面。
    /// 组件是独立窗口，主窗口 <c>ThemeManager.Apply</c> 不会自动传导；若只改材质而不改主题，
    /// 组件的 <see cref="RefreshAppearance"/> 仍会用旧的实际主题 → 组件主题与主界面不同步。
    /// </summary>
    public void ApplyTheme(StarMark.UI.Helpers.ThemePreference pref)
    {
        try { ThemeManager.Apply(this, pref); } catch { }
        RefreshAppearance();
    }

    /// <summary>
    /// 按给定位置 / 尺寸重新摆位（套用布局方案时用）。
    /// 注意：<see cref="WidgetStorage.Load"/> 每次反序列化都是新对象，窗口缓存的 _config
    /// 不会自动跟着变，因此必须由调用方显式下发并回写。
    /// </summary>
    public void ApplyBounds(double x, double y, double width, double height, bool topmost)
    {
        _config.X = x;
        _config.Y = y;
        _config.Width = width;
        _config.Height = height;
        _config.Topmost = topmost;

        if (width <= 0 || height <= 0) return;
        // 关键：布局方案下发的尺寸/位置必须先写进 _expandedRect，再走 ApplyChromeMode。
        // 否则 ApplyChromeMode(Standard) → ExpandToNormal() 会用构造期缓存的陈旧 _expandedRect
        // 把刚下发的尺寸/位置覆盖掉，表现为「应用布局后组件又跳回原来的大小/位置」。
        _expandedRect = new RectInt32((int)x, (int)y, (int)width, (int)height);
        AppWindow.MoveAndResize(new RectInt32((int)x, (int)y, (int)width, (int)height));
        ApplyTopmost();
        // 重新套用外壳模式：保证布局方案下发的尺寸/位置与胶囊/隐藏态一致（不会引起递归）
        ApplyChromeMode(_chromeMode);
    }

    /// <summary>
    /// 布局方案下发的"每实例自定义配置"：把外观覆盖写回窗口缓存的 config 并立即重挂材质/表面。
    /// 与 <see cref="ApplyBounds"/> 同理 —— ApplyLayoutCore 改的是磁盘侧新 Load 的对象，
    /// 窗口 _config 是另一份引用，必须显式下发，否则"应用布局后自定义外观不生效"。
    /// appearance 为 null 表示清除覆盖、回退当前全局设置主题。
    /// </summary>
    public void ApplyAppearance(WidgetAppearanceOverride? appearance)
    {
        if (ReferenceEquals(_config.Appearance, appearance)) return;
        _config.Appearance = appearance;
        // 材质套用是启动里头号嫌疑（日志里 5 秒内出现过 38 次 backdrop 应用），超过 30 ms 就留下证据
        StarMark.Abstractions.StartupProfile.Measure($"组件材质套用 {InstanceId}", RefreshAppearance, logWhenMs: 30);
    }

    // ───────────────────────── 标题栏交互 ─────────────────────────

    private void WireChrome()
    {
        DragBar.PointerPressed += DragBar_PointerPressed;
        DragBar.PointerMoved += DragBar_PointerMoved;
        DragBar.PointerReleased += DragBar_PointerReleased;
        DragBar.PointerCanceled += DragBar_PointerReleased;
        DragBar.DoubleTapped += (_, _) => TogglePin();
        // 右键菜单挂到标题栏与根边框：Hidden 态标题栏不可见，
        // 此时右键内容区仍能唤起同一份菜单切换回标准/胶囊。
        // 注意：菜单必须在每次 Opening 时重建（见 OnContextMenuOpening），因为「应用布局」子菜单
        // 依赖当前已保存的布局列表 —— 用户新增 / 删除布局后若仍用旧菜单，子项不会更新。
        _contextMenu = new MenuFlyout();
        DragBar.ContextFlyout = _contextMenu;
        RootBorder.ContextFlyout = _contextMenu;

        // 右键胶囊时：先进入悬停预览 → 右键唤起菜单的过程中，鼠标离开胶囊会触发收起，
        // 导致菜单随胶囊消失。修复：菜单打开即收回预览（回到胶囊位），且菜单打开期间禁止
        // 任何收起/重新预览，菜单关闭后才允许（见 RootBorder_PointerEntered/Exited 的 _contextMenu.IsOpen 守卫）。
        _contextMenu.Opening += OnContextMenuOpening;
        _contextMenu.Closed += (_, _) => { if (_peeking) CollapsePeek(); };
        PopulateMenu(_contextMenu);

        // 胶囊三段式热区 + 悬停预览（B-10）：仅在 Compact 态生效，标准态走原有标题栏按钮
        DragBar.Tapped += DragBar_Tapped;
        RootBorder.PointerEntered += RootBorder_PointerEntered;
        RootBorder.PointerExited += RootBorder_PointerExited;

        // 键盘操作（对齐 DeskBox 的 WindowInteraction）：Esc 收起悬停预览、Enter/Space 展开胶囊、F2 重命名。
        // 挂 RootGrid 而不是 Window —— WinUI 3 的 Window 没有 KeyDown，路由事件从焦点元素冒泡到内容根。
        RootGrid.KeyDown += RootGrid_KeyDown;

        if (WidgetStorage.IsResizable(_kind))
        {
            AddResizeGrips();
        }

        AppWindow.Closing += (_, e) =>
        {
            if (_shuttingDown) return;
            // 非代码主动关闭（标题栏已移除，正常不会触发）按“移除组件”处理；
            // 延迟到回调返回后执行，避免在 Closing 事件内重入 Close
            e.Cancel = true;
            DispatcherQueue.TryEnqueue(() => { var task = _manager.RemoveAsync(_instanceId); });
        };
    }

    /// <summary>
    /// 每次右键菜单打开时重建：动态内容（布局列表、隐私模式勾选）必须反映最新状态，
    /// 否则新增 / 删除布局后「应用布局」子菜单仍是旧列表。复用同一个 <see cref="_contextMenu"/> 实例，
    /// 仅清空并重新填充条目，避免替换 Flyout 导致正在打开的菜单失效。
    /// </summary>
    private void OnContextMenuOpening(object? sender, object e)
    {
        if (_peeking) CollapsePeek();
        if (_contextMenu is { } menu) PopulateMenu(menu);
    }

    /// <summary>
    /// 时钟组件右键菜单里的「护眼 · 休息提醒」一节（批次 WC-3）。
    /// <para>
    /// 引擎<b>仍然只有一个</b> <see cref="EyeRestService"/>：这里给的是"就地开关 + 改间隔"的入口，
    /// 写盘与起停都走设置页同一对出口（<c>SaveEyeRest</c> / <c>App.ApplyEyeRest</c>），
    /// 所以两处不会各自攒一份状态——第二份事实迟早对不上，而对不上的那次是用户先看见。
    /// </para>
    /// <para>菜单每次打开都重建（<see cref="OnContextMenuOpening"/>），勾选与"下一次几点"因此总是当前值。</para>
    /// </summary>
    private void BuildEyeRestSection(MenuFlyout menu)
    {
        var settings = App.Services.GetRequiredService<SettingsStore>();
        var enabled = settings.LoadEyeRestEnabled();
        var interval = settings.LoadEyeRestIntervalMinutes();

        menu.Items.Add(new MenuFlyoutSeparator());
        var section = new MenuFlyoutSubItem
        {
            Text = "护眼 · 休息提醒",
            Icon = new FontIcon { Glyph = "\uE7BA", FontSize = 14 },   // TouchPointer：示意"该歇一下"
        };

        var toggle = new ToggleMenuFlyoutItem { Text = "开启休息提醒", IsChecked = enabled };
        toggle.Click += (_, _) =>
        {
            // 强制休息 / 全屏让路这两项不在这里改，但要原样带过去，否则一按开关就把它们重置回默认
            settings.SaveEyeRest(toggle.IsChecked, interval,
                settings.LoadEyeRestEnforced(), settings.LoadEyeRestDeferOnFullscreen());
            App.ApplyEyeRest(toggle.IsChecked);
        };
        section.Items.Add(toggle);

        var gap = new MenuFlyoutSubItem { Text = $"间隔：{interval} 分钟", IsEnabled = enabled };
        // 档位与文案都取 Core 的那一份（IntervalLabels 与 IntervalOptions 同序）：
        // 这里自己拼"X 分钟"就会与设置页的下拉分岔成两处事实。
        for (var i = 0; i < EyeRestPolicy.IntervalOptions.Length; i++)
        {
            var minutes = EyeRestPolicy.IntervalOptions[i];
            var item = new RadioMenuFlyoutItem
            {
                Text = EyeRestPolicy.IntervalLabels[i],
                IsChecked = minutes == interval,
            };
            item.Click += (_, _) =>
            {
                settings.SaveEyeRest(settings.LoadEyeRestEnabled(), minutes,
                    settings.LoadEyeRestEnforced(), settings.LoadEyeRestDeferOnFullscreen());
                App.ApplyEyeRest(settings.LoadEyeRestEnabled());
            };
            gap.Items.Add(item);
        }
        section.Items.Add(gap);

        // 状态常驻一行：按完开关要立刻看得见"下一次是几点"，不然"按了没反应"与"生效了但没到点"分不开
        var next = EyeRestService.NextDueAt;
        section.Items.Add(new MenuFlyoutItem
        {
            Text = next is { } due
                ? $"下一次大约 {due.ToLocalTime():HH:mm}（到点前还会看一眼前台是否全屏）"
                : enabled ? "开关已开，但计时器没起来（设置页「健康与诊断」有原因）" : "未开启",
            IsEnabled = false,
        });

        var full = new MenuFlyoutItem { Text = "完整设置（强制休息 / 全屏让路）…" };
        // 直接落在「健康与诊断」页：跳进设置页却停在常规页，等于让人到了门口再自己找房间
        full.Click += (_, _) => App.MainWindow?.Present(true, "健康与诊断");
        section.Items.Add(full);

        menu.Items.Add(section);
    }

    private void PopulateMenu(MenuFlyout menu)
    {
        menu.Items.Clear();

        // 「添加」收进二级菜单：每种组件一个子项，可重复添加同类型组件（对标 DeskBox 多实例）。
        // 避免主菜单被一长串「添加 X」撑爆、与「移除本组件」混在一起难以区分。
        var addSub = new MenuFlyoutSubItem
        {
            Text = "添加组件",
            Icon = new FontIcon { Glyph = "\uE710", FontSize = 14 },
        };
        foreach (var kind in WidgetStorage.AllKinds)
        {
            var add = new MenuFlyoutItem { Text = WidgetStorage.KindTitle(kind) };
            var captured = kind;
            add.Click += (_, _) => _ = _manager.AddInstanceAsync(captured);
            addSub.Items.Add(add);
        }
        menu.Items.Add(addSub);

        menu.Items.Add(new MenuFlyoutSeparator());
        var removeThis = new MenuFlyoutItem
        {
            Text = "移除本组件",
            Icon = new FontIcon { Glyph = "\uE711", FontSize = 14 },   // Cancel（X），与其它图标视觉一致
        };
        removeThis.Click += (_, _) => _ = _manager.RemoveAsync(_instanceId);
        menu.Items.Add(removeThis);

        // 护眼 · 休息提醒（批次 WC-3，用户裁决"护眼建议和时钟组件结合"）：只有时钟组件挂这一节。
        if (_kind == WidgetKind.Clock) BuildEyeRestSection(menu);

        // 胶囊模式入口（Phase B）：受描述符 CanHideChrome 控制；Hidden 态标题栏不可见时仍可经根边框菜单切换
        menu.Items.Add(new MenuFlyoutSeparator());
        var canHide = WidgetRegistry.Default.TryGet(_kind, out var desc) && desc.CanHideChrome;
        _collapseMenuItem = new MenuFlyoutItem
        {
            Text = "收起为胶囊",
            Icon = new FontIcon { Glyph = "\uE70E", FontSize = 14 }, // ChevronUp：收起
            IsEnabled = canHide,
        };
        _collapseMenuItem.Click += (_, _) => ToggleCompact();
        menu.Items.Add(_collapseMenuItem);

        _hideChromeMenuItem = new MenuFlyoutItem
        {
            Text = "隐藏外壳（仅内容）",
            Icon = new FontIcon { Glyph = "\uE921", FontSize = 14 },   // Hide（有效字形，避免显示错误方框）
            IsEnabled = canHide,
        };
        _hideChromeMenuItem.Click += (_, _) => ToggleHidden();
        menu.Items.Add(_hideChromeMenuItem);

        // 隐私模式（B-10）：收起为胶囊时隐藏标题，避免胶囊泄露组件身份/内容
        var privacyItem = new ToggleMenuFlyoutItem
        {
            Text = "隐私模式（胶囊态隐藏标题）",
            Icon = new FontIcon { Glyph = "\uE72E", FontSize = 14 }, // 锁
            IsChecked = _config.PrivacyMode,
        };
        privacyItem.Click += (_, _) =>
        {
            _config.PrivacyMode = privacyItem.IsChecked;
            ApplyChromeMode(_chromeMode);   // 刷新标题可见性
            PersistBounds();                // 持久化开关
        };
        menu.Items.Add(privacyItem);

        menu.Items.Add(new MenuFlyoutSeparator());
        var showAll = new MenuFlyoutItem { Text = "全部显示" };
        showAll.Click += (_, _) => _ = _manager.ShowAllAsync();
        var hideAll = new MenuFlyoutItem { Text = "全部隐藏" };
        hideAll.Click += (_, _) => _ = _manager.HideAllAsync();
        menu.Items.Add(showAll);
        menu.Items.Add(hideAll);

        menu.Items.Add(new MenuFlyoutSeparator());
        var main = new MenuFlyoutItem { Text = "打开 StarMark 主窗口" };
        main.Click += (_, _) => _manager.OpenMainWindow();
        var settings = new MenuFlyoutItem { Text = "管理组件…" };
        settings.Click += (_, _) => _manager.OpenWidgetSettings();
        menu.Items.Add(main);
        menu.Items.Add(settings);

        // 重命名本组件实例（名字显示在标题栏 / 胶囊标题上）
        var rename = new MenuFlyoutItem
        {
            Text = "重命名…",
            Icon = new FontIcon { Glyph = "\uE8AC", FontSize = 14 },   // Rename，与其它图标视觉一致
        };
        rename.Click += async (_, _) => await RenameAsync();
        menu.Items.Add(rename);

        // 每实例外观编辑（B-9）：材质/颜色/边框/圆角/文本缩放，可一键恢复全局
        var appearance = new MenuFlyoutItem
        {
            Text = "外观…",
            Icon = new FontIcon { Glyph = "\uE790", FontSize = 14 },
        };
        appearance.Click += async (_, _) => await EditAppearanceAsync();
        menu.Items.Add(appearance);

        // 布局方案：保存当前这一屏，或切换到已保存的布局（同一时刻只显示一套）
        menu.Items.Add(new MenuFlyoutSeparator());
        var saveLayout = new MenuFlyoutItem
        {
            Text = "保存当前组件布局…",
            Icon = new FontIcon { Glyph = "\uE78C", FontSize = 14 },
        };
        saveLayout.Click += (_, _) => _ = SaveLayoutByNameAsync();
        menu.Items.Add(saveLayout);

        var layouts = _manager.GetLayouts();
        if (layouts.Count > 0)
        {
            var sub = new MenuFlyoutSubItem { Text = "应用布局" };
            foreach (var l in layouts)
            {
                var id = l.Id;
                var apply = new MenuFlyoutItem { Text = $"{l.Name}（{l.Summary}）" };
                apply.Click += (_, _) => _ = _manager.ApplyLayoutAsync(id);
                sub.Items.Add(apply);
            }
            menu.Items.Add(sub);
        }

        // 布局与数据快照（#53）：连组件数据一起存成不可变历史点；应用 = 回到那一刻（带自动回滚）。
        var saveSnapshot = new MenuFlyoutItem
        {
            Text = "保存当前布局与数据…",
            Icon = new FontIcon { Glyph = "\uE787", FontSize = 14 },
        };
        saveSnapshot.Click += (_, _) => _ = SaveSnapshotByNameAsync();
        menu.Items.Add(saveSnapshot);

        var snapshots = _manager.GetSnapshots();
        if (snapshots.Count > 0)
        {
            var snapSub = new MenuFlyoutSubItem { Text = "应用快照" };
            foreach (var s in snapshots)
            {
                var id = s.Id;
                var apply = new MenuFlyoutItem { Text = $"{s.Name}（{s.Summary}）" };
                // 快照应用刻意与「应用布局」一致：直接应用、不再二次确认，因为应用前会自动留回滚点。
                // 但回滚点没存成时 Apply 会中止（返回 false）——不静默吞掉，给一条提示说明"未应用"。
                apply.Click += async (_, _) =>
                {
                    if (!await _manager.ApplySnapshotAsync(id))
                        await ShowTipAsync("未能应用快照",
                            $"{_manager.LastApplyError ?? "生成「应用前」回滚点失败"}——为防数据丢失已中止，当前状态未改动。");
                };
                snapSub.Items.Add(apply);
            }
            menu.Items.Add(snapSub);
        }

        var manageSnapshots = new MenuFlyoutItem { Text = "管理快照…" };
        manageSnapshots.Click += (_, _) =>
        {
            App.PresentMainWindow();
            App.MainWindow?.NavigateTo("snapshot");
        };
        menu.Items.Add(manageSnapshots);
    }

    /// <summary>
    /// 询问名称并把「当前所有组件的布局 + 各自的数据」存为一个不可变快照点。
    /// 与 <see cref="SaveLayoutByNameAsync"/> 的分工：那条纯模板不含数据，本条含数据（#52/#53）。
    /// </summary>
    private async System.Threading.Tasks.Task SaveSnapshotByNameAsync()
    {
        var name = await CenteredDialog.PromptAsync(
            title: "保存当前布局与数据",
            message: "把当前屏幕上所有组件的位置、外观，连同各自的数据（快捷入口 / 待办 / 随记 / 条目格查询）一起存成一个不可变快照点。之后「应用快照」即回到这一刻，并会先自动生成一个「应用前」回滚点。",
            placeholder: "例如：上线前",
            primaryText: "保存",
            cancelText: "取消",
            owner: this);

        if (name is null) return;

        // CaptureSnapshotAsync 现任一条目取数失败即整体抛出——绝不能落一张静默缺数据的快照，
        // 故此处捕获并明确告知"保存失败"，避免用户误以为已备份。
        try
        {
            var saved = await _manager.CaptureSnapshotAsync(name);
            if (saved is null) await ShowTipAsync("当前没有组件", "没有可保存的快照内容。");
            else await ShowTipAsync("已保存快照", $"「{saved.Name}」已记录（{saved.Summary}）。可在主窗口「快照」页或右键「应用快照」里回到这一刻。");
        }
        catch (Exception ex)
        {
            StarLog.Error("保存布局与数据快照失败", ex);
            await ShowTipAsync("保存快照失败", $"读取组件数据时出错，快照未保存：{ex.Message}");
        }
    }

    /// <summary>
    /// 询问名称并把当前可见组件保存为一整套布局方案。
    /// 弹窗走 <see cref="CenteredDialog"/>（独立居中顶层窗口）：组件窗口可能只有 200×150，
    /// 挂在组件 XamlRoot 上的 ContentDialog 会被窗口裁掉，用户根本看不见。
    /// </summary>
    private async System.Threading.Tasks.Task SaveLayoutByNameAsync()
    {
        var name = await CenteredDialog.PromptAsync(
            title: "保存当前组件布局",
            message: "记下当前屏幕上所有可见组件的位置、大小与各自的外观配置（材质/颜色等），形成一套可复用的模板。不含任何条目内容数据（待办/随记/快捷入口/笔记）。应用布局时，不属于该布局的组件会被隐藏（内容保留）。",
            placeholder: "例如：工作模式",
            primaryText: "保存",
            cancelText: "取消",
            owner: this);

        if (name is null) return;

        var saved = await _manager.SaveCurrentLayoutAsync(name);
        if (saved is null) await ShowTipAsync("当前没有可见的组件", "没有可保存的布局内容。");
    }

    private async System.Threading.Tasks.Task ShowTipAsync(string title, string message)
    {
        try { await CenteredDialog.MessageAsync(title, message, owner: this); }
        catch { /* 窗口正在关闭 */ }
    }

    /// <summary>打开每实例外观编辑浮层（B-9）：编辑中实时预览到本组件，确定后持久化，取消则还原。
    /// 同一组件若已有一个编辑器在打开，直接忽略后续点击（避免叠加多个浮层）。</summary>
    private async System.Threading.Tasks.Task EditAppearanceAsync()
    {
        if (_appearanceEditorOpen) return;   // 单例守护：防止多次右键「外观…」叠加多个编辑器
        _appearanceEditorOpen = true;
        try
        {
            var original = _config.Appearance;   // 取消时按此还原实时预览
            var result = await WidgetAppearanceEditor.ShowAsync(
                this,
                _config,
                preview =>
                {
                    // 实时预览：直接套用临时覆盖，不落盘
                    _config.Appearance = preview;
                    ApplyAppearanceCore();
                },
                textPreview =>
                {
                    // 只动字号的预览：不重挂材质 / 不重铺背景（见 ApplyTextAppearanceCore 的说明）
                    _config.Appearance = textPreview;
                    ApplyTextAppearanceCore();
                });
            if (result.Saved)
            {
                _config.Appearance = result.Override;     // 确定：写回并持久化
                ApplyAppearanceCore();
                await _manager.SaveInstanceAppearanceAsync(_instanceId, result.Override);
            }
            else
            {
                // 取消：还原为打开前的外观（编辑器内部已回退实时预览，这里兜底确保一致）
                _config.Appearance = original;
                ApplyAppearanceCore();
            }
        }
        catch (Exception ex)
        {
            StarLog.Error("编辑组件外观失败", ex);
        }
        finally
        {
            _appearanceEditorOpen = false;
        }
    }

    /// <summary>组件类型的默认标题（未重命名时的显示名）。</summary>
    private string DefaultKindTitle()
        => WidgetRegistry.Default.TryGet(_kind, out var d) ? d.Title : _kind.ToString();

    /// <summary>当前显示名：优先用户重命名的名字，未命名/null 时回退到类型默认标题。</summary>
    private string DisplayTitle()
        => string.IsNullOrWhiteSpace(_config.Title) ? DefaultKindTitle() : _config.Title!;

    /// <summary>把显示名同步到标题栏文本与窗口标题（重命名后立即刷新）。</summary>
    private void ApplyTitle()
    {
        try
        {
            var t = DisplayTitle();
            WidgetTitle.Text = t;
            Title = $"StarMark 组件 - {t}";
        }
        catch { /* 标题不是关键路径，失败不影响窗口可用 */ }
    }

    /// <summary>
    /// 右键「重命名…」：弹出输入框修改本组件实例的名字并持久化。
    /// 提交空字符串即恢复组件类型的默认标题（存 null，不占磁盘、与旧版本配置兼容）。
    /// </summary>
    private async System.Threading.Tasks.Task RenameAsync()
    {
        try
        {
            var def = DefaultKindTitle();
            var input = await CenteredDialog.PromptAsync(
                title: "重命名组件",
                message: $"给这个组件取个名字（留空则恢复默认名称「{def}」）。",
                placeholder: def,
                defaultText: string.IsNullOrWhiteSpace(_config.Title) ? null : _config.Title,
                owner: this);
            if (input is null) return;   // 用户取消

            _config.Title = string.IsNullOrWhiteSpace(input) ? null : input.Trim();
            ApplyTitle();
            await _manager.SaveInstanceTitleAsync(_instanceId, _config.Title);
        }
        catch (Exception ex)
        {
            StarLog.Error("重命名组件失败", ex);
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e) => TogglePin();

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (DragBar.ContextFlyout is MenuFlyout menu)
            menu.ShowAt(AddButton, new Point(0, AddButton.ActualHeight));
    }

    private void HideButton_Click(object sender, RoutedEventArgs e) => _ = _manager.HideTemporaryAsync(_instanceId);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => _ = _manager.RemoveAsync(_instanceId);

    private void CollapseButton_Click(object sender, RoutedEventArgs e) => ToggleCompact();

    /// <summary>
    /// 应用外壳呈现模式（Phase B 胶囊模式）：
    /// Standard=标准标题栏+内容；Compact=收起为胶囊（仅标题栏、内容隐藏、窗口缩到标题高度、缩放柄隐）；
    /// Hidden=隐藏外壳（标题栏/按钮/缩放柄均隐、内容铺满，作叠加浮层）。结果持久化到实例配置。
    /// </summary>
    private void ApplyChromeMode(WidgetChromeMode mode)
    {
        try
        {
            // 进入任何外壳模式都取消悬停预览（_peeking 仅在 Compact 临时展开期间为真）
            _peeking = false;

            // 该类型不允许收起外壳时，Hidden 回退为 Standard（Compact 仍允许，因为仅缩标题高度）
            if (mode == WidgetChromeMode.Hidden &&
                !(WidgetRegistry.Default.TryGet(_kind, out var desc) && desc.CanHideChrome))
            {
                mode = WidgetChromeMode.Standard;
            }

            // 仅「从非收起态切到胶囊态」时记录当前位置为展开态原位置，供点击展开恢复；
            // 重复切到胶囊（如已在胶囊态）不覆盖，避免把胶囊停靠位误记为展开位。
            if (mode == WidgetChromeMode.Compact && _chromeMode != WidgetChromeMode.Compact)
            {
                var cur = WindowInterop.GetWindowRect(this);
                if (cur.Width > 0 && cur.Height > 0) _expandedRect = cur;
            }

            _chromeMode = mode;
            _config.ChromeMode = mode;

            var compact = mode == WidgetChromeMode.Compact;
            var hidden = mode == WidgetChromeMode.Hidden;

            RootGrid.RowDefinitions[0].Height = hidden ? new GridLength(0) : new GridLength(36);
            // 胶囊模式下内容行高夹 0：即便窗口被系统强制拉高，也绝不露出「无内容页」
            RootGrid.RowDefinitions[1].Height = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            DragBar.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            ChromeButtons.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            ContentScroll.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            SetGripsVisible(!compact);

            // 隐私模式：Compact 态隐藏标题（正文已隐含隐藏），避免胶囊泄露组件身份/内容；
            // 标准态不隐藏（正文可见，隐私无意义），仅收起态生效。
            var hideTitle = compact && _config.PrivacyMode;
            WidgetTitle.Visibility = hideTitle ? Visibility.Collapsed : Visibility.Visible;

            if (compact) MoveToCapsule();      // 回到稳定胶囊停靠位（不重算堆叠）
            else ExpandToNormal();             // 点击展开 / 隐藏外壳 → 恢复到收起前的展开态位置

            UpdateChromeControls();
            PersistBounds();
        }
        catch (Exception ex)
        {
            // 外壳模式切换绝不冒泡到点击处理（否则「全部显示」等入口可能演变成未处理异常致应用崩溃）
            StarLog.Error($"应用组件外壳模式失败 ({_kind},{mode})", ex);
        }
    }

    /// <summary>
    /// 收起为胶囊：移到稳定停靠位 <see cref="_capsuleRect"/>（已分配则直接用，不重算堆叠）。
    /// 优先用持久化的 <see cref="WidgetInstanceConfig.CapsuleX/Y"/>（重启后同列胶囊不再错位），
    /// 否则首次进入胶囊时调用 <see cref="AssignCapsuleSlot"/> 计算一次。
    /// 锁定宽度到胶囊宽、高度缩到标题栏高度（36 DIP 物理像素）。
    /// </summary>
    private void MoveToCapsule()
    {
        if (_capsuleRect is null)
        {
            if (_config.CapsuleX is { } cx && _config.CapsuleY is { } cy)
            {
                // 持久化停靠位可能来自旧版本 / 不同分辨率 / 历史「胶囊拉伸」bug，坐标为越界或 (0,0) 角点；
                // 落在工作区外则视为失效，改走 AssignCapsuleSlot 重新吸附最近垂直边缘。
                var wa = WindowInterop.GetWorkArea(this);
                var sane = cx >= wa.X && cx <= wa.X + wa.Width && cy >= wa.Y && cy <= wa.Y + wa.Height;
                if (sane)
                {
                    var scale = WindowInterop.GetScale(this);
                    // 胶囊统一宽度（不随组件原始宽度变化），保证角落胶囊整齐一致
                    _capsuleWidth = UnifiedCapsuleWidth;
                    _capsuleRect = new RectInt32(cx, cy, _capsuleWidth, (int)(36 * scale));
                }
                else
                {
                    AssignCapsuleSlot();
                }
            }
            else
            {
                AssignCapsuleSlot();
            }
        }
        if (_capsuleRect is { } r)
        {
            _capsuleWidth = r.Width;
            AppWindow.MoveAndResize(r);
        }
    }

    /// <summary>
    /// 分配胶囊停靠位（仅首次 / 强制重排时调用一次）：吸附到最近的屏幕垂直边缘（左/右），
    /// 并与同边缘已停靠（高度≈胶囊）的其它胶囊向下堆叠，形成一列停靠栏。
    /// 结果写入 <see cref="_capsuleRect"/> 并随实例持久化（CapsuleX/CapsuleY），后续悬停预览收回时
    /// 直接回到该位，绝不再重算——这正是修复「悬停后同列胶囊依次下移、最终移出屏幕」的关键。
    /// </summary>
    private void AssignCapsuleSlot()
    {
        var scale = WindowInterop.GetScale(this);
        var r = WindowInterop.GetWindowRect(this);
        if (r.Width <= 0 || r.Height <= 0) return;
        // 统一胶囊宽度：所有胶囊一致（不随各组件原始宽度变化），避免宽窄不一、也杜绝历史「胶囊拉伸」残留
        _capsuleWidth = UnifiedCapsuleWidth;
        var capH = (int)(36 * scale);

        int dockX, y;
        try
        {
            var wa = WindowInterop.GetWorkArea(this);
            var toLeft = (r.X + r.Width / 2) < (wa.X + wa.Width / 2);
            dockX = toLeft ? wa.X + 8 : wa.X + wa.Width - _capsuleWidth - 8;
            y = wa.Y + 8;
            // 与同边缘（X 对齐到 dockX）已停靠（高度≈胶囊）的其它组件堆叠，避免重叠
            foreach (var o in _manager.GetOtherBounds(_instanceId))
            {
                if (Math.Abs(o.X - dockX) <= 8 && o.Height <= capH + 6)
                {
                    var bottom = o.Y + o.Height + 8;
                    if (bottom > y) y = bottom;
                }
            }
        }
        catch
        {
            // 拿不到工作区/其它实例边界时回退到当前位置
            dockX = r.X;
            y = r.Y;
        }

        _capsuleRect = new RectInt32(dockX, y, _capsuleWidth, capH);
    }

    /// <summary>展开为正常：恢复到收起前记录的展开态位置/尺寸（<see cref="_expandedRect"/>）。</summary>
    private void ExpandToNormal()
    {
        if (_expandedRect is { } er && er.Width > 0 && er.Height > 0)
        {
            AppWindow.MoveAndResize(new RectInt32(er.X, er.Y, er.Width, er.Height));
            return;
        }
        var r = WindowInterop.GetWindowRect(this);
        if (r.Width <= 0 || r.Height <= 0) return;
        var w = (int)_config.Width > 0 ? (int)_config.Width : r.Width;
        var h = (int)_config.Height > 0 ? (int)_config.Height : r.Height;
        AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, w, h));
    }

    private void SetGripsVisible(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var g in _grips) g.Visibility = v;
    }

    private void ToggleCompact() =>
        ApplyChromeMode(_chromeMode == WidgetChromeMode.Compact ? WidgetChromeMode.Standard : WidgetChromeMode.Compact);

    private void ToggleHidden() =>
        ApplyChromeMode(_chromeMode == WidgetChromeMode.Hidden ? WidgetChromeMode.Standard : WidgetChromeMode.Hidden);

    // ── 胶囊三段式热区 + 悬停预览（B-10）──

    /// <summary>
    /// 胶囊态标题栏的点击分区（仅在 Compact 生效）：左 1/3 = 主操作、中 1/3 = 展开、右 1/3 = 弹出菜单。
    /// 拖动用 PointerPressed/Moved/Released 处理，Tapped 只在「未拖动」时触发，两者不冲突。
    /// </summary>
    private void DragBar_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_chromeMode != WidgetChromeMode.Compact) return;
        var w = DragBar.ActualWidth;
        if (w <= 0) return;
        var x = e.GetPosition(DragBar).X;
        if (x < w / 3.0) ActivatePrimary();
        else if (x < 2.0 * w / 3.0) ToggleCompact();
        else _contextMenu?.ShowAt(DragBar, e.GetPosition(DragBar));
    }

    /// <summary>主操作（胶囊左区）：搜索/快捷启动/各条目格 → 唤起主窗口；剪贴板格直接落到「剪贴板」页；时钟/待办/随记 → 就地展开交互。</summary>
    private void ActivatePrimary()
    {
        if (_kind is WidgetKind.Search or WidgetKind.QuickLaunch
            or WidgetKind.Clipboard or WidgetKind.TagGrid
            or WidgetKind.Activity or WidgetKind.Pinned)
        {
            // 剪贴板格点名要去的就是那一页：停在「全部」列表上等于把用户带到医院却不挂号。
            // NavigateTo 不可用（主窗尚未就绪）时退回"唤起主窗"，至少窗口来到前台。
            if (_kind == WidgetKind.Clipboard && App.MainWindow is { } mw) { mw.NavigateTo("clipboard"); return; }
            _manager.OpenMainWindow();
        }
        else if (_chromeMode == WidgetChromeMode.Compact)
        {
            ToggleCompact();   // 时钟/待办/随记：展开到正常态以便直接操作
        }
    }

    /// <summary>鼠标进入组件窗口（Compact 态且非拖动/预览中、且右键菜单未打开）→ 临时展开预览内容，离开即收回。</summary>
    private void RootBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // 右键菜单打开期间禁止预览/收起，否则菜单会随胶囊收起而消失（见 _contextMenu.Opening/Closed 守卫）
        if (_contextMenu?.IsOpen == true) return;
        if (_chromeMode == WidgetChromeMode.Compact && !_dragging && !_peeking) PeekExpand();
    }

    /// <summary>
    /// 组件窗口键盘操作（对齐 DeskBox <c>Views/ContentWidgetWindow.WindowInteraction.cs</c>）。
    /// <para>
    /// 只处理「窗口级」语义，且<b>必须先排除输入类控件</b>：待办/随记的输入框里按 Esc/Space
    /// 属于编辑语境（取消输入、打空格），被窗口抢走会直接破坏输入体验。
    /// </para>
    /// </summary>
    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsEditingSource(e.OriginalSource)) return;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape:
                // 悬停预览时 Esc = 收起预览回到胶囊（DeskBox 同名语义）。
                // 未预览时 Esc 不做任何事——避免用户按 Esc 意外把展开的组件收成胶囊。
                if (_peeking)
                {
                    CollapsePeek();
                    e.Handled = true;
                }
                break;

            case Windows.System.VirtualKey.Enter:
            case Windows.System.VirtualKey.Space:
                // 胶囊态 Enter/Space = 展开到正常态（等同点击胶囊中区）
                if (_chromeMode == WidgetChromeMode.Compact)
                {
                    ApplyChromeMode(WidgetChromeMode.Standard);
                    e.Handled = true;
                }
                break;

            case Windows.System.VirtualKey.F2:
                // 双击已被「切换置顶」占用（README 已写明该手势），故重命名走 Windows 惯例的 F2
                _ = RenameAsync();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// 按键来源是否属于「正在编辑文本」——这类按键窗口一律不拦截。
    /// </summary>
    private static bool IsEditingSource(object? source) => source is
        TextBox or RichEditBox or PasswordBox or AutoSuggestBox or NumberBox or ComboBox or RichTextBlock;

    private void RootBorder_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // 菜单仍打开时（鼠标移到菜单上会离开胶囊）不收起，避免菜单被吞掉
        if (_contextMenu?.IsOpen == true) return;
        if (_peeking) CollapsePeek();
    }

    /// <summary>悬停预览：临时把胶囊展开到正常尺寸并显示内容（外壳模式仍为 Compact，故尺寸夹取放行）。
    /// 预览锚定在胶囊当前停靠位（不恢复展开态位置），离开即收回到同一胶囊位 —— 因此预览不会挪动胶囊、
    /// 也不会触发堆叠重算（修复同列胶囊被推离原位/移出屏幕）。</summary>
    private void PeekExpand()
    {
        if (_chromeMode != WidgetChromeMode.Compact || _peeking || _dragging) return;
        _peeking = true;
        try
        {
            var scale = WindowInterop.GetScale(this);
            // 悬停预览也用「统一胶囊宽度」：宽与收起态一致，仅向下展开内容高度，外观上「原地预览」；
            // 只有「点击展开」(ToggleCompact→Standard) 才经 ExpandToNormal 恢复到组件原本的位置与尺寸。
            var w = _capsuleWidth > 0 ? _capsuleWidth : UnifiedCapsuleWidth;
            var h = (int)_config.Height > 0 ? (int)_config.Height : (int)(WidgetStorage.DefaultHeight(_kind) * scale);
            // 以胶囊停靠位为锚点展开，保持 X/Y 不变（仅向下放大内容），外观上「原地预览」
            var anchor = _capsuleRect ?? WindowInterop.GetWindowRect(this);
            if (RootGrid.RowDefinitions.Count > 1)
                RootGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            ContentScroll.Visibility = Visibility.Visible;
            AppWindow.MoveAndResize(new RectInt32(anchor.X, anchor.Y, w, h));
        }
        catch (Exception ex)
        {
            // 预览期间内容首次参与布局；内容自身渲染失败（如 x:Bind 资源缺失抛在 Measure 阶段）
            // 会把异常甩回这里。必须兜住并立刻收回：否则 _peeking 卡在 true，
            // 胶囊再也收不回去，下一次悬停还会再炸一次（踩坑 #83）。
            StarLog.Error($"展开胶囊悬停预览失败（{_kind}）", ex);
            SafeCollapsePeek();
        }
    }

    /// <summary>收回悬停预览：恢复胶囊尺寸与隐藏内容，并回到稳定停靠位（不再重算堆叠）。</summary>
    private void CollapsePeek()
    {
        if (!_peeking) return;
        SafeCollapsePeek();
    }

    /// <summary>无条件收回预览（内部实现；预览态本身出问题时也要能把它收回去）。</summary>
    private void SafeCollapsePeek()
    {
        _peeking = false;
        try
        {
            if (RootGrid.RowDefinitions.Count > 1)
                RootGrid.RowDefinitions[1].Height = new GridLength(0);
            ContentScroll.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StarLog.Error($"隐藏胶囊预览内容失败（{_kind}）", ex);
        }

        try { MoveToCapsule(); }   // 直接回到 _capsuleRect，不重算堆叠 → 不会把同列胶囊推走
        catch (Exception ex) { StarLog.Error($"收回胶囊失败（{_kind}）", ex); }
    }

    /// <summary>同步折叠按钮图标与右键菜单文案到当前外壳模式。</summary>
    private void UpdateChromeControls()
    {
        if (CollapseIcon is not null)
            CollapseIcon.Glyph = _chromeMode == WidgetChromeMode.Compact ? "\uE70D" : "\uE70E"; // 展开/收起
        if (_collapseMenuItem is not null)
            _collapseMenuItem.Text = _chromeMode == WidgetChromeMode.Compact ? "展开组件" : "收起为胶囊";
        if (_hideChromeMenuItem is not null)
            _hideChromeMenuItem.Text = _chromeMode == WidgetChromeMode.Hidden ? "显示外壳（标题栏）" : "隐藏外壳（仅内容）";
    }

    private void TogglePin()
    {
        _config.Topmost = !_config.Topmost;
        ApplyTopmost();
        PersistBounds();
    }

    /// <summary>本窗口当前是否置顶（供「组件总控」判断这一批的整体方向）。</summary>
    public bool IsTopmost => _config.Topmost;

    /// <summary>
    /// 由「切换所有组件置顶」快捷键统一下发：<b>只改层序、不落盘</b>。
    /// 落盘交给 WidgetManager 一次写完 —— 逐窗口各自 Load/Save 会把一次按键变成 N 次整档读写。
    /// </summary>
    public void ApplyGlobalTopmost(bool topmost)
    {
        _config.Topmost = topmost;
        ApplyTopmost();
    }

    private void WidgetWindow_Closed(object sender, WindowEventArgs args)
    {
        _ticker?.Stop();
        // 必须先脱离桌面层，否则会残留指向 SHELLDLL_DefView 的悬挂所有者
        WidgetLayerService.DetachFromDesktopLayer(WindowInterop.GetHwnd(this));
        _layerAttached = false;
        // 释放本窗口的亚克力/云母控制器：它们持有原生合成资源与 DWM 句柄，
        // 只靠 ConditionalWeakTable 不会主动 Dispose（复用策略下控制器是长期存活的）。
        WidgetAppearance.ReleaseBackdrop(this);
    }

    // ───────────────────────── 拖动 + 吸附（DeskBox CoordinatedMove 同款物理像素方案）─────────────────────────

    private void DragBar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(DragBar).Properties.IsRightButtonPressed) return;
        if (FindAncestorButton(e.OriginalSource as DependencyObject)) return;
        if (_peeking) CollapsePeek();   // 悬停预览期间按下即先收回胶囊，再按胶囊拖动

        WindowInterop.GetCursorPos(out _gestureStart);
        _gestureStartRect = WindowInterop.GetWindowRect(this);

        // Ctrl+拖动 → 同屏所有可见组件一起移动（DeskBox CoordinatedMove）。
        // 修饰键状态只在**按下瞬间**采样一次：拖动过程中松/按 Ctrl 都不改变参与者，
        // 否则会出现「跟到一半不跟了」的半截跟随。
        _coordinated = IsCtrlDown();
        BeginCoordinatedMove();

        BeginSnapSession();
        RaiseTransient();
        _dragging = true;
        DragBar.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DragBar_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        WindowInterop.GetCursorPos(out var pt);
        var proposed = new RectInt32(
            _gestureStartRect.X + pt.X - _gestureStart.X,
            _gestureStartRect.Y + pt.Y - _gestureStart.Y,
            _gestureStartRect.Width, _gestureStartRect.Height);

        var result = WidgetSnapCalculator.ResolveMove(
            proposed,
            _snapTargets,
            _snapWorkArea,
            _snapSpacing,
            _snapEngage,
            _snapRelease,
            _stickyHorizontal,
            _stickyVertical);
        _stickyHorizontal = result.HorizontalMatch;
        _stickyVertical = result.VerticalMatch;
        AppWindow.MoveAndResize(result.Bounds);

        ApplyCoordinatedMove(pt.X - _gestureStart.X, pt.Y - _gestureStart.Y);
        e.Handled = true;
    }

    private static bool IsCtrlDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>
    /// 招募协同移动参与者：同一显示器上的其它可见组件。
    /// 用「显示器设备名」判定同屏（比比较矩形相交更准），
    /// 眼睛坐标为 MONITOR_DEFAULTTONEAREST 的 hwnd 判定与本组件一致。
    /// </summary>
    private void BeginCoordinatedMove()
    {
        ClearCoordinatedMove();
        if (!_coordinated) return;

        try
        {
            var selfMonitor = WindowInterop.GetMonitorForWindow(WindowInterop.GetHwnd(this)).Device;
            foreach (var w in _manager.VisibleWindowsExcept(_instanceId))
            {
                var device = WindowInterop.GetMonitorForWindow(WindowInterop.GetHwnd(w)).Device;
                if (device == selfMonitor) _coordPeers.Add((w, w.CurrentRect));
            }
        }
        catch (Exception ex)
        {
            // 招募失败不该拖累主手势：退化成普通单窗口拖动
            StarLog.Error("Ctrl+拖动协同移动招募参与者失败", ex);
            ClearCoordinatedMove();
        }
    }

    private void ApplyCoordinatedMove(int dx, int dy)
    {
        if (!_coordinated || _coordPeers.Count == 0) return;
        foreach (var (w, start) in _coordPeers)
        {
            try { w.MoveByCoordinated(dx, dy, start); }
            catch (Exception ex) { StarLog.Error("协同移动跟随失败", ex); }
        }
    }

    /// <summary>
    /// 拖动收尾的持久化：自己 + 协同移动的每个参与者，<b>一次读档、最多一次落盘</b>。
    /// 逐窗 PersistBounds 的话，一次 Ctrl+拖动要写 N+1 趟整档（N＝同屏参与组件数），
    /// 而这正是"多个组件一起摆位"的常用手势。单个窗口写失败不该拖住其它窗口，所以逐个兜住。
    /// </summary>
    private void PersistPositionsAfterDrag()
    {
        var targets = new List<WidgetWindow> { this };
        targets.AddRange(_coordPeers.Select(p => p.Window));
        _storage.Mutate(data =>
        {
            var wrote = false;
            foreach (var w in targets)
            {
                try { wrote |= w.WriteBoundsInto(data); }
                catch (Exception ex) { StarLog.Error("拖动后持久化组件位置失败", ex); }
            }
            return wrote;
        });
        ClearCoordinatedMove();
    }

    private void ClearCoordinatedMove()
    {
        _coordPeers.Clear();
        _coordinated = false;
    }

    private void DragBar_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        EndSnapSession();
        try { DragBar.ReleasePointerCapture(e.Pointer); } catch { }
        PersistPositionsAfterDrag();
        e.Handled = true;
    }

    /// <summary>
    /// 开始一次吸附会话：一次性采集候选目标并按 DPI 换算阈值。
    /// 对应 DeskBox ResizeGuideOverlayService.BeginDrag 的会话准备。
    /// </summary>
    private void BeginSnapSession()
    {
        _stickyHorizontal = null;
        _stickyVertical = null;

        // 磁吸总开关（设置页「组件 → 边缘磁吸」）：关闭后用户在桌面自由摆位，不做任何自动贴合。
        if (!WidgetAppearance.SnapEnabled())
        {
            _snapTargets = Array.Empty<WidgetSnapTarget>();
            _snapWorkArea = null;
            return;
        }

        var scale = WindowInterop.GetScale(this);
        var others = _manager.GetOtherBounds(_instanceId);
        var targets = new WidgetSnapTarget[others.Count];
        for (int i = 0; i < others.Count; i++) targets[i] = new WidgetSnapTarget(others[i]);

        _snapTargets = targets;
        _snapWorkArea = WidgetSnapCalculator.InsetWorkArea(
            WindowInterop.GetWorkArea(this),
            (int)Math.Round(WidgetSnapCalculator.DefaultScreenMargin * scale));
        _snapSpacing = (int)Math.Round(WidgetAppearance.SnapSpacing() * scale);
        _snapEngage = Math.Max(1, (int)Math.Round(WidgetAppearance.SnapEngageThreshold() * scale));
        // 迟滞：release 恒比 engage 宽一档（沿用默认 24→32 的 +8px 关系），避免拖动时抖动。
        _snapRelease = Math.Max(
            _snapEngage,
            (int)Math.Round((WidgetAppearance.SnapEngageThreshold()
                + (WidgetSnapCalculator.DefaultReleaseThreshold - WidgetSnapCalculator.DefaultEngageThreshold)) * scale));
        _stickyHorizontal = null;
        _stickyVertical = null;
    }

    private void EndSnapSession()
    {
        _snapTargets = Array.Empty<WidgetSnapTarget>();
        _snapWorkArea = null;
        _stickyHorizontal = null;
        _stickyVertical = null;
    }

    // ───────────────────────── 右下角缩放 ─────────────────────────

    // ───────────────────── 边缘/四角缩放（8 向，替代原右下角手柄）─────────────────────────

    /// <summary>沿窗口四边 + 四角布置透明缩放 grip，方向以 n/s/e/w 组合标记在 Tag 上。</summary>
    private void AddResizeGrips()
    {
        if (RootBorder.Child is not Grid rootGrid) return;

        // 层级策略（避免「右上角关闭按钮被 grip 盖住」）：
        // - 四边四角 grip 统一 ZIndex=100，压在标题栏(DragBar, 默认 0)与内容区之上 → 四边四角均可拉伸，
        //   上边/左上角 grip 也因此在标题栏之上仍可拉伸。
        // - 仅 ChromeButtons（关闭/隐藏/添加/置顶按钮区，XAML 中 ZIndex=200）置于 grip 之上，
        //   让出右上角按钮区，保证按钮始终可点；代价是右上角那一点不再触发 ne 拉伸（按需求自行处理）。

        void AddGrip(string dir, double width, double height,
            HorizontalAlignment hAlign, VerticalAlignment vAlign, InputSystemCursorShape shape)
        {
            var grip = new ResizeGrip(dir, InputSystemCursor.Create(shape))
            {
                Width = width,
                Height = height,
                HorizontalAlignment = hAlign,
                VerticalAlignment = vAlign,
            };
            Grid.SetRowSpan(grip, 2);
            // 显式提到最上层：确保 8 个 grip 始终压在标题栏/内容区之上，
            // 避免被 DragBar / ScrollViewer 的命中测试吞掉（否则只有右下角 se 能命中）。
            Canvas.SetZIndex(grip, 100);
            grip.PointerPressed += ResizeGrip_PointerPressed;
            grip.PointerMoved += ResizeGrip_PointerMoved;
            grip.PointerReleased += ResizeGrip_PointerReleased;
            grip.PointerCanceled += ResizeGrip_PointerReleased;
            rootGrid.Children.Add(grip);
        }

        // 命中带加宽（边 8px、角 18px）以便精准命中；全部 RowSpan=2 + z=100（见 AddGrip），
        // 四边四角均可从窗口边缘直接拉伸，不再只有右下角 se 生效。
        AddGrip("n", double.NaN, 8, HorizontalAlignment.Stretch, VerticalAlignment.Top, InputSystemCursorShape.SizeNorthSouth);
        AddGrip("s", double.NaN, 8, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNorthSouth);
        AddGrip("w", 8, double.NaN, HorizontalAlignment.Left, VerticalAlignment.Stretch, InputSystemCursorShape.SizeWestEast);
        AddGrip("e", 8, double.NaN, HorizontalAlignment.Right, VerticalAlignment.Stretch, InputSystemCursorShape.SizeWestEast);
        AddGrip("nw", 18, 18, HorizontalAlignment.Left, VerticalAlignment.Top, InputSystemCursorShape.SizeNorthwestSoutheast);
        AddGrip("ne", 18, 18, HorizontalAlignment.Right, VerticalAlignment.Top, InputSystemCursorShape.SizeNortheastSouthwest);
        AddGrip("sw", 18, 18, HorizontalAlignment.Left, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNortheastSouthwest);
        AddGrip("se", 18, 18, HorizontalAlignment.Right, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNorthwestSoutheast);
    }

    private void ResizeGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // 胶囊模式禁止缩放：即便 grip 意外可见也不响应
        if (_chromeMode == WidgetChromeMode.Compact) return;
        if (sender is not FrameworkElement { Tag: string dir }) return;
        _resizeDir = dir;
        WindowInterop.GetCursorPos(out _gestureStart);
        _gestureStartRect = WindowInterop.GetWindowRect(this);
        BeginSnapSession();     // 缩放同样需要候选目标：用于边缘对齐与宽高对齐
        _resizing = true;
        RaiseTransient();
        ((UIElement)sender).CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ResizeGrip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        // 胶囊模式禁止缩放（防御：grip 被隐藏后不应触发，但保险拦截）
        if (_chromeMode == WidgetChromeMode.Compact) return;
        WindowInterop.GetCursorPos(out var pt);
        var scale = WindowInterop.GetScale(this);
        var minW = (int)(200 * scale);
        var minH = (int)(120 * scale);
        var dx = pt.X - _gestureStart.X;
        var dy = pt.Y - _gestureStart.Y;
        var x = _gestureStartRect.X;
        var y = _gestureStartRect.Y;
        var w = _gestureStartRect.Width;
        var h = _gestureStartRect.Height;

        // 拖左/上边缘时对侧边固定：先算新尺寸，再反推新位置（夹到最小尺寸时不跟手）
        if (_resizeDir.Contains('w')) { var nw = Math.Max(minW, w - dx); x += w - nw; w = nw; }
        if (_resizeDir.Contains('e')) { w = Math.Max(minW, w + dx); }
        if (_resizeDir.Contains('n')) { var nh = Math.Max(minH, h - dy); y += h - nh; h = nh; }
        if (_resizeDir.Contains('s')) { h = Math.Max(minH, h + dy); }

        var proposed = new RectInt32(x, y, w, h);

        // 用会话快照判定，不再每次指针移动重读设置：BeginSnapSession（缩放起点）已经把
        // "磁吸是否开启"折进 _snapWorkArea——关着时它是 null，开着时非 null。
        // 旧的 WidgetAppearance.SnapEnabled() 在这里按 60–120 Hz 触发一次 settings.json 读+反序列化。
        if (_snapWorkArea is not null)
        {
            // 1) 被拖动的边对齐邻居/屏幕边缘（保证边线与其他组件齐平）
            proposed = SnapResizedEdges(proposed, minW, minH);
            // 2) 尺寸对齐邻居的宽 / 高（用户很难手动把宽高调得完全一致）
            proposed = AlignSizeToNeighbors(proposed);
        }

        AppWindow.MoveAndResize(proposed);
        e.Handled = true;
    }

    /// <summary>
    /// 缩放时被拖动的那些边参与吸附：把边线贴到邻居的同向边（相邻时保留间距）或屏幕边界，
    /// 让多个组件的边缘能整体对齐。
    /// </summary>
    private RectInt32 SnapResizedEdges(RectInt32 proposed, int minW, int minH)
    {
        int left = proposed.X;
        int top = proposed.Y;
        int right = proposed.X + proposed.Width;
        int bottom = proposed.Y + proposed.Height;

        if (_resizeDir.Contains('w') &&
            WidgetSnapCalculator.ResolveResizeEdge(proposed, WidgetSnapEdge.Left, _snapTargets, _snapWorkArea, _snapSpacing, _snapEngage) is { } lm)
            left = lm.Coordinate;

        if (_resizeDir.Contains('e') &&
            WidgetSnapCalculator.ResolveResizeEdge(proposed, WidgetSnapEdge.Right, _snapTargets, _snapWorkArea, _snapSpacing, _snapEngage) is { } rm)
            right = rm.Coordinate;

        if (_resizeDir.Contains('n') &&
            WidgetSnapCalculator.ResolveResizeEdge(proposed, WidgetSnapEdge.Top, _snapTargets, _snapWorkArea, _snapSpacing, _snapEngage) is { } tm)
            top = tm.Coordinate;

        if (_resizeDir.Contains('s') &&
            WidgetSnapCalculator.ResolveResizeEdge(proposed, WidgetSnapEdge.Bottom, _snapTargets, _snapWorkArea, _snapSpacing, _snapEngage) is { } bm)
            bottom = bm.Coordinate;

        // 对侧边固定：改了宽/高就不能改同源坐标；比最小尺寸还小则放弃该轴的吸附
        return new RectInt32(
            left,
            top,
            Math.Max(minW, right - left),
            Math.Max(minH, bottom - top));
    }

    /// <summary>
    /// 把正在改变的宽 / 高对齐到邻居组件的尺寸：用户想让几个组件"一样宽 / 一样高"时，
    /// 手动拖边缘几乎不可能精确到像素，这里在阈值内直接拉齐。
    /// </summary>
    private RectInt32 AlignSizeToNeighbors(RectInt32 proposed)
    {
        const int sizeThreshold = 16;

        var result = proposed;
        var horizontal = _resizeDir.Contains('w') || _resizeDir.Contains('e');
        var vertical = _resizeDir.Contains('n') || _resizeDir.Contains('s');

        if (horizontal)
        {
            var bestDelta = sizeThreshold;
            foreach (var t in _snapTargets)
            {
                var delta = Math.Abs(t.Bounds.Width - proposed.Width);
                if (delta <= bestDelta) { bestDelta = delta; result.Width = t.Bounds.Width; }
            }
            // 拖左/北边时保持对侧位置不变
            if (_resizeDir.Contains('w')) result.X = proposed.X + proposed.Width - result.Width;
        }

        if (vertical)
        {
            var bestDelta = sizeThreshold;
            foreach (var t in _snapTargets)
            {
                var delta = Math.Abs(t.Bounds.Height - proposed.Height);
                if (delta <= bestDelta) { bestDelta = delta; result.Height = t.Bounds.Height; }
            }
            if (_resizeDir.Contains('n')) result.Y = proposed.Y + proposed.Height - result.Height;
        }

        return result;
    }

    private void ResizeGrip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        EndSnapSession();
        try { ((UIElement)sender).ReleasePointerCapture(e.Pointer); } catch { }
        PersistBounds();
        e.Handled = true;
    }

    /// <summary>
    /// 8 向缩放 grip：四边 + 四角透明命中区，进入时切换为对应方向尺寸光标、离开恢复。
    /// 基类选用 Panel（非 sealed，可承载 Background 命中；ProtectedCursor 为受保护成员，
    /// 只能在派生类的实例方法中访问，故在构造函数内订阅自身 PointerEntered/Exited 事件来设置）。
    /// Border/Grid/Canvas/StackPanel 在 WinUI 3 均 sealed，无法派生；UIElement 亦无 OnPointerEntered 虚方法。
    /// </summary>
    private sealed class ResizeGrip : Panel
    {
        private readonly InputSystemCursor _cursor;

        public ResizeGrip(string direction, InputSystemCursor cursor)
        {
            Tag = direction;
            _cursor = cursor;
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            PointerEntered += ResizeGrip_PointerEntered;
            PointerExited += ResizeGrip_PointerExited;
        }

        private void ResizeGrip_PointerEntered(object sender, PointerRoutedEventArgs e)
            => ProtectedCursor = _cursor;

        private void ResizeGrip_PointerExited(object sender, PointerRoutedEventArgs e)
            => ProtectedCursor = null;
    }

    private static bool FindAncestorButton(DependencyObject? start)
    {
        while (start is not null)
        {
            if (start is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) return true;
            start = VisualTreeHelper.GetParent(start);
        }
        return false;
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

    // ── 快捷启动格 ──

    /// <summary>拖放接收只挂接一次（重建内容时不会重复订阅/叠加遮罩）。</summary>
    private void SetupQuickLaunchDrop()
    {
        if (_kind != WidgetKind.QuickLaunch) return;

        RootBorder.AllowDrop = true;
        RootBorder.DragOver += QuickLaunch_DragOver;
        RootBorder.Drop += QuickLaunch_Drop;
        RootBorder.DragEnter += (_, _) => SetDropHintVisible(true);
        RootBorder.DragLeave += (_, _) => SetDropHintVisible(false);

        _dropHint = new Border
        {
            Background = WidgetBrush("WidgetDropHintBrush"),
            BorderBrush = ThemeBrush.For(RootBorder.ActualTheme, "AppAccentBrush"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "松开以添加到快捷启动",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            },
        };
        Grid.SetRowSpan(_dropHint, 2);
        if (RootBorder.Child is Grid rootGrid) rootGrid.Children.Add(_dropHint);
    }

    private void SetDropHintVisible(bool visible)
    {
        if (_dropHint is not null) _dropHint.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void QuickLaunch_DragOver(object sender, DragEventArgs e)
    {
        var v = e.DataView;
        var ok = v.Contains(StandardDataFormats.WebLink)
                 || v.Contains(StandardDataFormats.ApplicationLink)
                 || v.Contains(StandardDataFormats.StorageItems)
                 || v.Contains(StandardDataFormats.Text);
        e.AcceptedOperation = ok ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.Caption = "添加到快捷启动";
        SetDropHintVisible(ok);
        await Task.CompletedTask;
    }

    private async void QuickLaunch_Drop(object sender, DragEventArgs e)
    {
        SetDropHintVisible(false);
        try
        {
            var v = e.DataView;

            if (v.Contains(StandardDataFormats.StorageItems))
            {
                // 一次拖入可能有几十项：先只收集，再交 AddPathsToLauncherAsync 整批登记 + 整批添加。
                // 逐项 await 的话，每一项都要开一次库、并把 widgets.json 整档读一遍写一遍（见 AddLinksAsync）。
                // 那条批处理与「选择文件」出口共用同一份（触发器2「拖入即入库」＝只写索引，绝不动磁盘文件）。
                var items = await v.GetStorageItemsAsync();
                var paths = new List<(string? Title, string Path)>();
                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.Path)) continue;
                    paths.Add((item.Name, item.Path));
                }
                await _manager.AddPathsToLauncherAsync(_instanceId, paths);
            }
            else if (v.Contains(StandardDataFormats.WebLink))
            {
                var uri = await v.GetWebLinkAsync();
                await _manager.AddLinkAsync(_instanceId, uri.Host, uri.AbsoluteUri);
            }
            else if (v.Contains(StandardDataFormats.ApplicationLink))
            {
                var uri = await v.GetApplicationLinkAsync();
                await _manager.AddLinkAsync(_instanceId, uri.Host, uri.AbsoluteUri);
            }
            else if (v.Contains(StandardDataFormats.Text))
            {
                var text = (await v.GetTextAsync()).Trim();
                if (QuickLaunchWidgetViewModel.TryParseUri(text, out var uri) && uri is not null)
                    await _manager.AddLinkAsync(_instanceId,
                        // 含 '#' 的本地文件名：TryPathFromUri 保留 '#'（LocalPath 会截断成 "C"）。
                        uri.IsFile ? System.IO.Path.GetFileName(
                            StarMark.Abstractions.LocalFileIdentity.TryPathFromUri(uri.AbsoluteUri, out var fp) ? fp : uri.LocalPath) : uri.Host,
                        uri.AbsoluteUri);
            }

            // 新增后 WidgetManager 触发 LinksChanged，QuickLaunchWidget 订阅后增量刷新 Links（R3）。
        }
        catch (Exception ex)
        {
            StarLog.Error("拖放添加快捷入口失败", ex);
        }
    }

    // 以下快捷启动格的手动构建方法（BuildQuickLaunch / LinkRow / ReloadLinks /
    // LoadPinnedAsync / AddLinkForm / ToggleAddLinkForm / SectionHeader / EmptyHint /
    // TryParseUri / OnLinksChanged / RebuildQuickLaunch）已迁移至 QuickLaunchWidget
    // （XAML + ViewModel + ItemsRepeater，R1 试点）；增量刷新由 LinksChanged 驱动。

    // 待办组件已迁移至 TodoWidget（XAML + ViewModel + ItemsRepeater，R3 收尾），
    // 由 WidgetContentFactory 直接构造；不再需要本类内的 BuildTodo / ToggleTodo / DeleteTodo。

    // 随记组件已迁移至 QuickNoteWidget（XAML + ViewModel + ItemsRepeater，R3 收尾），
    // 由 WidgetContentFactory 直接构造；不再需要本类内的 BuildQuickNote / DeleteNote。

    // 时钟组件已迁移至 ClockWidget（XAML + ViewModel，R3 收尾）：手工构建与每秒定时器
    // 均迁入组件内部，本类只在 Reveal / HideTemporary / 可见性变化 / 关闭时
    // 通过 IWidgetTicker（_ticker）启停刷新——不再按 WidgetKind 分支。

    // ── 快捷搜索 ──

    // 搜索组件已迁移至 SearchWidget（XAML + ViewModel + 标签 chip 多选，R2 试点），
    // 由 WidgetContentFactory 直接构造；不再需要本类内的 BuildSearch / ActivateSearchBox。
}