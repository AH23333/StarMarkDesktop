#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
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
    private readonly WidgetKind _kind;
    private readonly WidgetStorage _storage;
    private readonly IItemRepository? _repo;
    private readonly WidgetManager _manager;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _clockTimer;
    private bool _styled;
    private bool _shuttingDown;
    private WidgetConfig _config;
    private Border? _dropHint;

    // ── 拖动/缩放状态（全部使用 Win32 物理像素，避免 DIP 与 AppWindow 物理坐标混用）──
    private bool _dragging;
    private bool _resizing;
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

    public WidgetWindow(WidgetKind kind, WidgetStorage storage, IItemRepository? repo, WidgetManager manager)
    {
        _kind = kind;
        _storage = storage;
        _repo = repo;
        _manager = manager;

        InitializeComponent();
        SystemBackdrop = new DesktopAcrylicBackdrop();
        Title = $"StarMark 组件 - {WidgetStorage.KindTitle(kind)}";

        WidgetGlyph.Text = KindGlyph(kind);
        WidgetTitle.Text = WidgetStorage.KindTitle(kind);

        var data = _storage.Load();
        _config = data.WindowConfigs.TryGetValue(kind.ToString(), out var saved)
            ? saved
            : _storage.GetConfig(data, kind, (int)kind);

        BuildContent();
        WireChrome();
        SetupQuickLaunchDrop();

        if (kind == WidgetKind.Clock)
            AppWindow.Changed += (_, e) =>
            {
                if (e.DidVisibilityChange) UpdateClockTimer();
            };

        Closed += WidgetWindow_Closed;
    }

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
        }

        AppWindow.Show();
        AttachToDesktopLayer();
        ApplyTopmost();
        if (_kind == WidgetKind.Clock) UpdateClockTimer();
        if (_kind == WidgetKind.Search) ActivateSearchBox();
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
        if (_kind == WidgetKind.Clock) UpdateClockTimer();
    }

    /// <summary>彻底关闭（组件被移除时调用）。</summary>
    public void Shutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        PersistBounds();
        _clockTimer?.Stop();
        Close();
    }

    // ───────────────────────── 窗口样式/位置 ─────────────────────────

    private void ApplyInitialBounds()
    {
        var scale = WindowInterop.GetScale(this);
        var data = _storage.Load();
        var hasSaved = data.WindowConfigs.ContainsKey(_kind.ToString());

        int w, h, x, y;
        if (hasSaved)
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
            _config.X = r.X;
            _config.Y = r.Y;
            _config.Width = r.Width;
            _config.Height = r.Height;
            var data = _storage.Load();
            data.WindowConfigs[_kind.ToString()] = _config;
            _storage.Save(data);
        }
        catch (Exception ex)
        {
            StarLog.Error($"组件位置持久化失败 ({_kind})", ex);
        }
    }

    private void ApplyTopmost()
    {
        WindowInterop.SetTopmost(this, _config.Topmost);
        var dark = RootBorder.ActualTheme == ElementTheme.Dark;
        PinIcon.Foreground = _config.Topmost ? ThemeBrush.Resolve(dark, "AppAccentBrush") : null;
        PinButton.Background = _config.Topmost
            ? ThemeBrush.Resolve(dark, "AppAccentSoftBrush")
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ToolTipService.SetToolTip(PinButton, _config.Topmost ? "取消置顶" : "置顶显示");
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
            ResizeThumb.PointerPressed += ResizeThumb_PointerPressed;
            ResizeThumb.PointerMoved += ResizeThumb_PointerMoved;
            ResizeThumb.PointerReleased += ResizeThumb_PointerReleased;
            ResizeThumb.PointerCanceled += ResizeThumb_PointerReleased;
        }
        else
        {
            ResizeThumb.Visibility = Visibility.Collapsed;
        }

        AppWindow.Closing += (_, e) =>
        {
            if (_shuttingDown) return;
            // 非代码主动关闭（标题栏已移除，正常不会触发）按“移除组件”处理；
            // 延迟到回调返回后执行，避免在 Closing 事件内重入 Close
            e.Cancel = true;
            DispatcherQueue.TryEnqueue(() => { var task = _manager.RemoveAsync(_kind); });
        };
    }

    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout();

        foreach (var kind in WidgetStorage.AllKinds)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = WidgetStorage.KindTitle(kind),
                IsChecked = _manager.IsEnabled(kind),
            };
            var captured = kind;
            item.Click += (_, _) => _ = _manager.SetEnabledAsync(captured, !_manager.IsEnabled(captured));
            menu.Items.Add(item);
        }

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

        menu.Opening += (_, _) =>
        {
            for (var i = 0; i < WidgetStorage.AllKinds.Count; i++)
            {
                if (menu.Items[i] is ToggleMenuFlyoutItem t)
                    t.IsChecked = _manager.IsEnabled(WidgetStorage.AllKinds[i]);
            }
        };
        return menu;
    }

    private void PinButton_Click(object sender, RoutedEventArgs e) => TogglePin();

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (DragBar.ContextFlyout is MenuFlyout menu)
            menu.ShowAt(AddButton, new Point(0, AddButton.ActualHeight));
    }

    private void HideButton_Click(object sender, RoutedEventArgs e) => _ = _manager.HideTemporaryAsync(_kind);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => _ = _manager.RemoveAsync(_kind);

    private void TogglePin()
    {
        _config.Topmost = !_config.Topmost;
        ApplyTopmost();
        PersistBounds();
    }

    private void WidgetWindow_Closed(object sender, WindowEventArgs args)
    {
        _clockTimer?.Stop();
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
        var scale = WindowInterop.GetScale(this);
        var others = _manager.GetOtherBounds(_kind);
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

    private void ResizeThumb_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        WindowInterop.GetCursorPos(out _gestureStart);
        _gestureStartRect = WindowInterop.GetWindowRect(this);
        _resizing = true;
        ResizeThumb.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ResizeThumb_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        WindowInterop.GetCursorPos(out var pt);
        var scale = WindowInterop.GetScale(this);
        var minW = (int)(200 * scale);
        var minH = (int)(120 * scale);
        var w = Math.Max(minW, _gestureStartRect.Width + pt.X - _gestureStart.X);
        var h = Math.Max(minH, _gestureStartRect.Height + pt.Y - _gestureStart.Y);
        AppWindow.MoveAndResize(new RectInt32(_gestureStartRect.X, _gestureStartRect.Y, w, h));
        e.Handled = true;
    }

    private void ResizeThumb_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        try { ResizeThumb.ReleasePointerCapture(e.Pointer); } catch { }
        PersistBounds();
        e.Handled = true;
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
        ContentHost.Children.Add(WidgetContentFactory.Default.Build(_kind, this));
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
                        if (await _manager.AddLinkAsync(item.Name, uri)) added++;
                    }
                }
            }
            else if (v.Contains(StandardDataFormats.WebLink))
            {
                var uri = await v.GetWebLinkAsync();
                if (await _manager.AddLinkAsync(uri.Host, uri.AbsoluteUri)) added++;
            }
            else if (v.Contains(StandardDataFormats.ApplicationLink))
            {
                var uri = await v.GetApplicationLinkAsync();
                if (await _manager.AddLinkAsync(uri.Host, uri.AbsoluteUri)) added++;
            }
            else if (v.Contains(StandardDataFormats.Text))
            {
                var text = (await v.GetTextAsync()).Trim();
                if (QuickLaunchWidgetViewModel.TryParseUri(text, out var uri) && uri is not null
                    && await _manager.AddLinkAsync(
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

    // ── 待办 ──

    internal UIElement BuildTodo()
    {
        var panel = new StackPanel { Padding = new Thickness(12, 8, 12, 12), Spacing = 4 };
        var input = new TextBox { PlaceholderText = "添加待办，回车确认…", FontSize = 12 };
        var listHost = new StackPanel();
        panel.Children.Add(input);
        panel.Children.Add(listHost);

        void Reload()
        {
            listHost.Children.Clear();
            var data = _storage.Load();
            foreach (var todo in data.Todos)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var check = new CheckBox { IsChecked = todo.Done, VerticalAlignment = VerticalAlignment.Center, MinWidth = 28 };
                var id = todo.Id;
                check.Checked += (_, _) => ToggleTodo(id, true);
                check.Unchecked += (_, _) => ToggleTodo(id, false);

                var text = new TextBlock
                {
                    Text = todo.Text,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = todo.Done ? 0.45 : 1.0,
                    TextDecorations = todo.Done ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None,
                };
                var del = new Button
                {
                    Content = "✕", FontSize = 9, Padding = new Thickness(5, 3, 5, 3),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                };
                del.Click += (_, _) => DeleteTodo(id);

                Grid.SetColumn(check, 0);
                Grid.SetColumn(text, 1);
                Grid.SetColumn(del, 2);
                row.Children.Add(check);
                row.Children.Add(text);
                row.Children.Add(del);
                listHost.Children.Add(row);
            }
        }

        input.KeyDown += (_, k) =>
        {
            if (k.Key != VirtualKey.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            var data = _storage.Load();
            data.Todos.Add(new TodoItem
            {
                Id = WidgetStorage.NewId(),
                Text = input.Text.Trim(),
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
            _storage.Save(data);
            input.Text = string.Empty;
            Reload();
        };

        Reload();
        return panel;
    }

    private void ToggleTodo(long id, bool done)
    {
        var data = _storage.Load();
        var todo = data.Todos.FirstOrDefault(t => t.Id == id);
        if (todo is null) return;
        todo.Done = done;
        _storage.Save(data);
        BuildContent();
    }

    private void DeleteTodo(long id)
    {
        var data = _storage.Load();
        data.Todos.RemoveAll(t => t.Id == id);
        _storage.Save(data);
        BuildContent();
    }

    // ── 随记 ──

    internal UIElement BuildQuickNote()
    {
        var panel = new StackPanel { Padding = new Thickness(12, 8, 12, 12), Spacing = 6 };
        var box = new TextBox
        {
            PlaceholderText = "随手记一笔，Ctrl+Enter 保存…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 70,
            FontSize = 12,
        };
        var saveBtn = new Button { Content = "保存随记", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 3, 12, 3) };
        var listHost = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(saveBtn);
        panel.Children.Add(listHost);

        void Reload()
        {
            listHost.Children.Clear();
            var data = _storage.Load();
            foreach (var note in data.Notes.Take(30))
            {
                var card = new Border
                {
                    Background = WidgetBrush("WidgetSubtleFillBrush"),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 5),
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new TextBlock { Text = note.Text, FontSize = 11, TextWrapping = TextWrapping.Wrap };
                var del = new Button
                {
                    Content = "✕", FontSize = 9, Padding = new Thickness(5, 3, 5, 3),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Top,
                };
                var id = note.Id;
                del.Click += (_, _) => DeleteNote(id);
                Grid.SetColumn(text, 0);
                Grid.SetColumn(del, 1);
                grid.Children.Add(text);
                grid.Children.Add(del);
                card.Child = grid;
                listHost.Children.Add(card);
            }
        }

        void Save()
        {
            if (string.IsNullOrWhiteSpace(box.Text)) return;
            var data = _storage.Load();
            data.Notes.Insert(0, new QuickNoteItem
            {
                Id = WidgetStorage.NewId(),
                Text = box.Text.Trim(),
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
            _storage.Save(data);
            box.Text = string.Empty;
            Reload();
        }

        saveBtn.Click += (_, _) => Save();
        box.KeyDown += (_, k) =>
        {
            var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (ctrl && k.Key == VirtualKey.Enter) Save();
        };

        Reload();
        return panel;
    }

    private void DeleteNote(long id)
    {
        var data = _storage.Load();
        data.Notes.RemoveAll(n => n.Id == id);
        _storage.Save(data);
        BuildContent();
    }

    // ── 时钟 ──

    private TextBlock? _clockTime;
    private TextBlock? _clockDate;

    internal UIElement BuildClock()
    {
        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 4,
            Padding = new Thickness(12, 10, 12, 12),
        };
        _clockTime = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm:ss"),
            FontSize = 34,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _clockDate = new TextBlock
        {
            Text = DateTime.Now.ToString("yyyy-MM-dd dddd"),
            FontSize = 12,
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        panel.Children.Add(_clockTime);
        panel.Children.Add(_clockDate);
        return panel;
    }

    private void UpdateClockTimer()
    {
        if (_kind != WidgetKind.Clock) return;
        if (AppWindow.IsVisible)
        {
            _clockTimer ??= DispatcherQueue.CreateTimer();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick -= ClockTick;
            _clockTimer.Tick += ClockTick;
            _clockTimer.Start();
            ClockTick(null, null);
        }
        else
        {
            _clockTimer?.Stop();
        }
    }

    private void ClockTick(object? sender, object? e)
    {
        if (_clockTime is not null) _clockTime.Text = DateTime.Now.ToString("HH:mm:ss");
        if (_clockDate is not null) _clockDate.Text = DateTime.Now.ToString("yyyy-MM-dd dddd");
    }

    // ── 快捷搜索 ──

    private TextBox? _searchBox;

    internal UIElement BuildSearch()
    {
        var panel = new StackPanel { Padding = new Thickness(12, 10, 12, 12), Spacing = 8 };
        _searchBox = new TextBox
        {
            PlaceholderText = "输入关键词，回车搜索 StarMark…",
            FontSize = 13,
            Padding = new Thickness(8, 6, 8, 6),
        };
        var btn = new Button
        {
            Content = "搜索",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 5, 8, 5),
        };
        void Submit()
        {
            var q = _searchBox.Text.Trim();
            _manager.RequestGlobalSearch(q);
            _searchBox.Text = string.Empty;
        }
        btn.Click += (_, _) => Submit();
        _searchBox.KeyDown += (_, k) => { if (k.Key == VirtualKey.Enter) Submit(); };
        panel.Children.Add(_searchBox);
        panel.Children.Add(btn);
        return panel;
    }

    private void ActivateSearchBox() => _searchBox?.Focus(FocusState.Programmatic);
}