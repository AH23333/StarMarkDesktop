#nullable enable
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

    private ClockWidget? _clockWidget;
    private bool _styled;
    private bool _shuttingDown;
    private WidgetInstanceConfig _config;
    private Border? _dropHint;

    // ── 拖动/缩放状态（全部使用 Win32 物理像素，避免 DIP 与 AppWindow 物理坐标混用）──
    private bool _dragging;
    private bool _resizing;
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

    public WidgetKind Kind => _kind;
    public bool IsVisible => AppWindow.IsVisible;

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

        InitializeComponent();
        ApplyAppearanceCore();
        Title = $"StarMark 组件 - {WidgetStorage.KindTitle(_kind)}";

        WidgetGlyph.Text = KindGlyph(_kind);
        WidgetTitle.Text = WidgetRegistry.Default.TryGet(_kind, out var d) ? d.Title : _kind.ToString();

        BuildContent();
        WireChrome();
        SetupQuickLaunchDrop();

        if (_kind == WidgetKind.Clock)
            AppWindow.Changed += (_, e) =>
            {
                if (e.DidVisibilityChange) _clockWidget?.UpdateRunning(AppWindow.IsVisible);
            };

        Closed += WidgetWindow_Closed;
    }

    /// <summary>实例唯一 ID（区分同类型多个组件）。</summary>
    public string InstanceId => _instanceId;

    /// <summary>该实例的持久化配置（位置/尺寸/置顶，引用自存储数据，原地改动后由 PersistBounds 落盘）。</summary>
    public WidgetInstanceConfig Config => _config;

    /// <summary>显示窗口（首次显示时完成样式、位置、置顶初始化）。</summary>
    public void Reveal()
    {
        if (!_styled)
        {
            WindowInterop.RemoveDefaultWindowFrame(this);
            WindowInterop.ApplyRoundedCorners(this);
            var pref = App.Services.GetService(typeof(SettingsStore)) is SettingsStore s
                ? s.LoadTheme()
                : new SettingsStore().LoadTheme();
            ThemeManager.Apply(this, pref);
            ApplyInitialBounds();
            _styled = true;
            RefreshAppearance();   // 构造期 ActualTheme 可能仍是 Default，按真实主题重挂毛玻璃控制器
        }

        AppWindow.Show();
        // 置顶与"贴在桌面层"互斥：置顶时作为普通顶层窗口 + WS_EX_TOPMOST 真正常驻最前；
        // 默认未开启时挂到桌面图标层（落在应用窗口之下、桌面图标之上）。由 ApplyTopmost 决定挂载/脱离。
        ApplyTopmost();
        if (_kind == WidgetKind.Clock) _clockWidget?.UpdateRunning(AppWindow.IsVisible);
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

    /// <summary>临时隐藏（实例保留，托盘/设置可一键恢复）。</summary>
    public void HideTemporary()
    {
        PersistBounds();
        AppWindow.Hide();
        if (_kind == WidgetKind.Clock) _clockWidget?.UpdateRunning(AppWindow.IsVisible);
    }

    /// <summary>彻底关闭（组件被移除时调用）。</summary>
    public void Shutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        PersistBounds();
        _clockWidget?.Stop();
        Close();
    }

    // ───────────────────────── 窗口样式/位置 ─────────────────────────

    private void ApplyInitialBounds()
    {
        var scale = WindowInterop.GetScale(this);

        int w, h, x, y;
        if (_config.Width > 0 && _config.Height > 0)
        {
            w = (int)_config.Width;
            h = (int)_config.Height;
            x = (int)_config.X;
            y = (int)_config.Y;
        }
        else
        {
            w = (int)(WidgetStorage.DefaultWidth(_kind) * scale);
            h = (int)(WidgetStorage.DefaultHeight(_kind) * scale);
            var index = (int)_kind;
            x = (int)((120 + index * 28) * scale);
            y = (int)((90 + index * 28) * scale);
        }

        // 夹到所在显示器工作区内
        var work = WindowInterop.GetWorkArea(this);
        if (x < work.X) x = work.X + 8;
        if (y < work.Y) y = work.Y + 8;
        if (x + w > work.X + work.Width) x = Math.Max(work.X + 8, work.X + work.Width - w - 8);
        if (y + h > work.Y + work.Height) y = Math.Max(work.Y + 8, work.Y + work.Height - h - 8);

        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    private void PersistBounds()
    {
        try
        {
            var r = WindowInterop.GetWindowRect(this);
            if (r.Width <= 0 || r.Height <= 0) return;
            var data = _storage.Load();
            var inst = data.Instances.FirstOrDefault(i => i.Id == _instanceId);
            if (inst is null) return;
            inst.X = r.X;
            inst.Y = r.Y;
            inst.Width = r.Width;
            inst.Height = r.Height;
            inst.Topmost = _config.Topmost;
            _storage.Save(data);
        }
        catch (Exception ex)
        {
            StarLog.Error($"组件位置持久化失败 ({_kind})", ex);
        }
    }

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
    /// 应用「毛玻璃材质 + 用户设定的表面不透明度」（构造时与设置变更后共用）。
    /// 原生亚克力 / 云母用 DesktopAcrylicController / MicaController 接管窗口背景，内容背景透明；
    /// 不透明材质回到实色。材质可在设置页「常规 → 外观」切换。
    /// </summary>
    private void ApplyAppearanceCore()
    {
        try
        {
            var kind = WidgetAppearance.Backdrop();
            WidgetAppearance.ApplyBackdrop(
                this, kind, WidgetAppearance.Opacity(), WidgetAppearance.MaterialIntensity(), RootBorder.ActualTheme);
            var surface = WidgetAppearance.SurfaceBrush(RootBorder.ActualTheme, kind);
            RootBorder.Background = surface;
            DragBar.Background = surface;
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

    /// <summary>设置变更后重新套用外观（材质 / 不透明度），由 WidgetManager 统一调用。</summary>
    public void RefreshAppearance()
    {
        if (!_styled) return;   // 尚未完成首次样式化的窗口（未 Show）无需刷
        ApplyAppearanceCore();
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
        AppWindow.MoveAndResize(new RectInt32((int)x, (int)y, (int)width, (int)height));
        ApplyTopmost();
    }

    // ───────────────────────── 标题栏交互 ─────────────────────────

    private void WireChrome()
    {
        DragBar.PointerPressed += DragBar_PointerPressed;
        DragBar.PointerMoved += DragBar_PointerMoved;
        DragBar.PointerReleased += DragBar_PointerReleased;
        DragBar.PointerCanceled += DragBar_PointerReleased;
        DragBar.DoubleTapped += (_, _) => TogglePin();
        DragBar.ContextFlyout = BuildMenu();

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

    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout();

        // 每种组件一个「添加」项：可重复添加同类型组件（对标 DeskBox 多实例）
        foreach (var kind in WidgetStorage.AllKinds)
        {
            var add = new MenuFlyoutItem
            {
                Text = $"添加 {WidgetStorage.KindTitle(kind)}",
                Icon = new FontIcon { Glyph = "\uE710", FontSize = 12 },
            };
            var captured = kind;
            add.Click += (_, _) => _ = _manager.AddInstanceAsync(captured);
            menu.Items.Add(add);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var removeThis = new MenuFlyoutItem
        {
            Text = "移除本组件",
            Icon = new FontIcon { Glyph = "\uE8BB", FontSize = 12 },
        };
        removeThis.Click += (_, _) => _ = _manager.RemoveAsync(_instanceId);
        menu.Items.Add(removeThis);

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

        // 布局方案：保存当前这一屏，或切换到已保存的布局（同一时刻只显示一套）
        menu.Items.Add(new MenuFlyoutSeparator());
        var saveLayout = new MenuFlyoutItem
        {
            Text = "保存当前组件布局…",
            Icon = new FontIcon { Glyph = "\uE78C", FontSize = 12 },
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

        return menu;
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
            message: "将记下当前屏幕上所有可见组件的位置与大小。应用布局时，不属于该布局的组件会被隐藏（内容保留）。",
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

    private void PinButton_Click(object sender, RoutedEventArgs e) => TogglePin();

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (DragBar.ContextFlyout is MenuFlyout menu)
            menu.ShowAt(AddButton, new Point(0, AddButton.ActualHeight));
    }

    private void HideButton_Click(object sender, RoutedEventArgs e) => _ = _manager.HideTemporaryAsync(_instanceId);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => _ = _manager.RemoveAsync(_instanceId);

    private void TogglePin()
    {
        _config.Topmost = !_config.Topmost;
        ApplyTopmost();
        PersistBounds();
    }

    private void WidgetWindow_Closed(object sender, WindowEventArgs args)
    {
        _clockWidget?.Stop();
        // 必须先脱离桌面层，否则会残留指向 SHELLDLL_DefView 的悬挂所有者
        WidgetLayerService.DetachFromDesktopLayer(WindowInterop.GetHwnd(this));
        _layerAttached = false;
    }

    // ───────────────────────── 拖动 + 吸附（DeskBox CoordinatedMove 同款物理像素方案）─────────────────────────

    private void DragBar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(DragBar).Properties.IsRightButtonPressed) return;
        if (FindAncestorButton(e.OriginalSource as DependencyObject)) return;

        WindowInterop.GetCursorPos(out _gestureStart);
        _gestureStartRect = WindowInterop.GetWindowRect(this);
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
        e.Handled = true;
    }

    private void DragBar_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        EndSnapSession();
        try { DragBar.ReleasePointerCapture(e.Pointer); } catch { }
        PersistBounds();
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
        _snapSpacing = (int)Math.Round(WidgetSnapCalculator.DefaultSpacing * scale);
        _snapEngage = Math.Max(1, (int)Math.Round(WidgetSnapCalculator.DefaultEngageThreshold * scale));
        _snapRelease = Math.Max(
            _snapEngage,
            (int)Math.Round(WidgetSnapCalculator.DefaultReleaseThreshold * scale));
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

        if (WidgetAppearance.SnapEnabled())
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
        _clockWidget = content as ClockWidget;
        ContentHost.Children.Add(content);
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
            var added = 0;

            if (v.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await v.GetStorageItemsAsync();
                    foreach (var item in items)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Path))
                        {
                            var uri = new Uri(item.Path).AbsoluteUri;
                            if (await _manager.AddLinkAsync(_instanceId, item.Name, uri)) added++;
                        }
                    }
                }
                else if (v.Contains(StandardDataFormats.WebLink))
                {
                    var uri = await v.GetWebLinkAsync();
                    if (await _manager.AddLinkAsync(_instanceId, uri.Host, uri.AbsoluteUri)) added++;
                }
                else if (v.Contains(StandardDataFormats.ApplicationLink))
                {
                    var uri = await v.GetApplicationLinkAsync();
                    if (await _manager.AddLinkAsync(_instanceId, uri.Host, uri.AbsoluteUri)) added++;
                }
                else if (v.Contains(StandardDataFormats.Text))
                {
                    var text = (await v.GetTextAsync()).Trim();
                    if (QuickLaunchWidgetViewModel.TryParseUri(text, out var uri) && uri is not null
                        && await _manager.AddLinkAsync(_instanceId,
                            uri.IsFile ? System.IO.Path.GetFileName(uri.LocalPath) : uri.Host,
                            uri.AbsoluteUri))
                    {
                        added++;
                    }
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
    // 通过 _clockWidget.UpdateRunning / Stop 启停刷新。

    // ── 快捷搜索 ──

    // 搜索组件已迁移至 SearchWidget（XAML + ViewModel + 标签 chip 多选，R2 试点），
    // 由 WidgetContentFactory 直接构造；不再需要本类内的 BuildSearch / ActivateSearchBox。
}