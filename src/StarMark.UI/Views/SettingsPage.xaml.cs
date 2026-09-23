#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Core.Backup;
using StarMark.Core.Hotkeys;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace StarMark.UI.Views;

public sealed partial class SettingsPage : Page, INotifyPropertyChanged
{
    public SettingsPageViewModel ViewModel { get; }

    private readonly BackupService _backup;

    // ===== 快捷键录制状态 =====
    private readonly Dictionary<string, HotkeyGesture> _hotkeyBindings = new();
    private string? _recordingAction;
    private Button? _recordingButton;
    private HotkeyRow? _recordingRow;
    private string? _recordingPrevText;
    private bool _recordingWasSet;      // 进入录制前该动作是否已有绑定（用于「Esc 清除」判定）
    private DateTimeOffset _ignoreClickUntil;
    private IReadOnlyList<WidgetLayout> _layouts = Array.Empty<WidgetLayout>();
    // 用低层级键盘钩子录制：不受 XAML 焦点路由影响，且能录到 Win 组合键
    private readonly KeyboardHookService _hkHook = new();

    // 录制会话内累计的按键：修饰键按位累计 + 一个主键（RegisterHotKey 的固有限制）
    private HotkeyModifiers _recordedModifiers;
    private uint _recordedMainKey;

    /// <summary>录制/清除后尚未保存（未生效）的快捷键改动。</summary>
    private bool _hotkeysDirty;

    public ObservableCollection<HotkeyRow> HotkeyRowsItems { get; } = new();
    public ObservableCollection<WidgetLayoutRow> LayoutRowsItems { get; } = new();

    public bool HasNoLayouts => LayoutRowsItems.Count == 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaisePropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsPageViewModel>();
        _backup = App.Services.GetRequiredService<BackupService>();
        _hkHook.KeyDown += OnHookKeyDown;   // 底层键盘钩子录制组合键
        _hkHook.KeyUp += OnHookKeyUp;       // 修饰键抬起时回显同步
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;  // 实时生效：属性变更去抖落盘
        LoadFromStoreSilently();
        // 开机自启开关初始态以注册表为准（SetEnabled 幂等，程序化置位不会误写）。
        // 它归在「常规 → 托盘与呼出」，所以由页面构造时读一次，不再挂在组件列表的构建里。
        AutostartToggle.IsOn = AutostartService().IsEnabled();
        _ = ViewModel.LoadHealthAsync();
        BuildWidgetRows();
        BuildHotkeyRows();
        BuildLayoutRows();
    }

    /// <summary>读取设置但不触发「实时保存」（加载本身产生的属性变更无须回写）。</summary>
    private void LoadFromStoreSilently()
    {
        _suppressSave = true;
        try
        {
            ViewModel.LoadFromStore();
            // 进设置页就重扫一次备份目录：列表要反映"刚刚那份自动备份真的落盘了没"，
            // 只看启动时缓存的话，用户永远看不到今天的新件。
            ViewModel.RefreshBackups();
        }
        catch (Exception ex)
        {
            // 绝不让读取设置的异常冒出 SettingsPage 构造函数：
            // 那会让 Frame.Navigate 失败，用户点「设置」即卡死崩溃（历史事故）。
            StarMark.Abstractions.StarLog.Error("加载设置到设置页失败", ex);
        }
        finally { _suppressSave = false; }
    }

    // ───────── 实时生效（去抖保存）─────────

    private bool _suppressSave;
    private DispatcherTimer? _autoSaveTimer;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressSave || string.IsNullOrEmpty(e.PropertyName)) return;
        // 健康度/备份/诊断等异步展示态不参与保存，避免加载过程中反复写盘
        if (IsDisplayOnlyProperty(e.PropertyName)) return;
        ScheduleAutoSave();
    }

    /// <summary>主窗口右上角快捷切换主题时，把本页主题选择同步过来。
    /// 用 <see cref="_suppressSave"/> 包住，避免触发自动保存（该值已由主窗口落盘，无需重复写）。</summary>
    private void OnExternalThemeSwitch(ThemePreference pref)
    {
        _suppressSave = true;
        try
        {
            ViewModel.ThemeIndex = pref switch
            {
                ThemePreference.Light => 1,
                ThemePreference.Dark => 2,
                _ => 0,
            };
        }
        finally
        {
            _suppressSave = false;
        }
    }

    private static bool IsDisplayOnlyProperty(string name)
        => name.Contains("Busy") || name.Contains("Loading") || name.Contains("Error")
        || name.Contains("Status") || name.Contains("Report") || name.Contains("Diagnostic")
        || name.Contains("Trend") || name.Contains("Summary") || name.Contains("Score");

    private void ScheduleAutoSave()
    {
        // 单实例：滑杆拖动时本方法按指针频率触发，续期只需 Stop/Start。
        var timer = _autoSaveTimer ??= BuildAutoSaveTimer();
        timer.Stop();
        timer.Start();
    }

    private DispatcherTimer BuildAutoSaveTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ViewModel.SaveCommand.Execute(null);   // 实时落盘（含外观 / 磁吸）
            App.MainWindow?.ApplyTraySettings();   // 托盘/热键即时生效
            // 材质 / 背景不透明度即时预览：主窗口与所有已打开组件重新套用外观
            App.MainWindow?.RefreshAppearance();
            _ = WidgetManager()?.RefreshAppearanceAsync();
        };
        return timer;
    }

    private void SaveRoots_Click(object sender, RoutedEventArgs e)
    {
        // 多行目录列表属需编辑确认项：点击时输入框已失焦 TwoWay 回写，此处显式落盘
        _autoSaveTimer?.Stop();
        ViewModel.SaveCommand.Execute(null);
    }

    // ===== 本地搜索引擎管理（占用 / 打开所在目录 / 删除）=====
    private void OpenEngineFolder_Click(object sender, RoutedEventArgs e)
        => StarMark.Integrations.Everything.EverythingSource.OpenEngineFolder();

    private async void DeleteEngine_Click(object sender, RoutedEventArgs e)
    {
        var bytes = StarMark.Integrations.Everything.EverythingSource.GetEngineOccupancyBytes();
        var sizeHint = bytes > 0 ? $"（约 {FormatBytes(bytes)}）" : "";
        var confirm = await CenteredDialog.ConfirmAsync(
            "删除本地搜索引擎",
            $"将删除本地磁盘搜索所用的 Everything 引擎及其 SDK{sizeHint}。书签 / Star / 标签数据不受影响；" +
            "下次开启本地磁盘搜索会自动重新下载。确定删除？",
            primaryText: "删除", cancelText: "取消",
            owner: App.MainWindow, dedupeKey: "deleteengine");
        if (!confirm) return;
        StarMark.Integrations.Everything.EverythingSource.DeleteEngine();
        RefreshEngineSize();
        ViewModel.LocalDiskSearchStatus = "已删除本地搜索引擎。下次开启本地磁盘搜索（或重启应用）会自动重新下载。";
    }

    private void RefreshEngineSize()
    {
        var bytes = StarMark.Integrations.Everything.EverythingSource.GetEngineOccupancyBytes();
        EngineSizeText.Text = bytes > 0 ? $"约 {FormatBytes(bytes)}" : "未安装";
    }

    private static string FormatBytes(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{b / (1024.0 * 1024):0.#} MB",
        _ => $"{b / (1024.0 * 1024 * 1024):0.##} GB",
    };

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadFromStoreSilently();
        _ = ViewModel.LoadHealthAsync();
        _ = ViewModel.LoadDiagnosticsAsync();
        RefreshEngineSize();
        if (WidgetManager() is { } mgr)
        {
            mgr.InstancesChanged -= OnInstancesChanged;   // 防重复订阅
            mgr.InstancesChanged += OnInstancesChanged;
            mgr.LayoutsChanged -= OnLayoutsChanged;
            mgr.LayoutsChanged += OnLayoutsChanged;
        }
        // 订阅主窗口快捷切主题：用户在设置页内用主界面右上角切主题时，把本页主题选择同步过来，
        // 避免离开设置页时兜底保存用过期的 ThemeIndex 把主题强制切回。
        MainWindow.ThemePreferenceQuickSwitched -= OnExternalThemeSwitch;
        MainWindow.ThemePreferenceQuickSwitched += OnExternalThemeSwitch;
        BuildWidgetRows();
        BuildHotkeyRows();
        BuildLayoutRows();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // 必须退订：WidgetManager 是单例服务，不退订会让死页面持续重建 UI（泄漏）
        if (WidgetManager() is { } mgr)
        {
            mgr.InstancesChanged -= OnInstancesChanged;
            mgr.LayoutsChanged -= OnLayoutsChanged;
        }
        // 退订主窗口快捷切主题（MainWindow 是单例，不退订会让死页面持续响应）
        MainWindow.ThemePreferenceQuickSwitched -= OnExternalThemeSwitch;
        StopRecording(resetText: true);      // 离开页面停止键盘钩子
        // 快捷键属「确认后才生效」项：离开页面时把改动落盘并注册，避免用户以为改了却没生效
        if (_hotkeysDirty)
        {
            ApplyHotkeyBindings();
            _hotkeysDirty = false;
            RaiseHotkeyStateChanged();
        }
        ViewModel.SaveCommand.Execute(null); // 离开时兜底落盘
    }

    /// <summary>组件实例增删后：延后一帧安全重建行（避免命中正在测量的元素）。</summary>
    private void OnInstancesChanged()
        => DispatcherQueue.TryEnqueue(BuildWidgetRows);

    /// <summary>布局增删后：布局列表与其快捷键行都要跟着变。</summary>
    private void OnLayoutsChanged()
        => DispatcherQueue.TryEnqueue(() => { BuildLayoutRows(); BuildHotkeyRows(); });

    private WidgetManager? WidgetManager()
        => App.Services.GetRequiredService<WidgetManager>();

    private HotkeyService? HotkeySvc()
        => App.Services.GetRequiredService<HotkeyService>();

    /// <summary>
    /// 逐类型列出：类型标题 +「添加组件」按钮（可重复添加同类型），其下为该类型的每个实例一行
    /// （显示 / 移除）。置顶实例在标签后标注（置顶）。
    /// </summary>
    private AutostartService AutostartService()
        => App.Services.GetRequiredService<AutostartService>();

    private void AutostartToggle_Toggled(object sender, RoutedEventArgs e)
    {
        try { AutostartService().SetEnabled(AutostartToggle.IsOn); }
        catch (Exception ex) { StarLog.Error("设置开机自启失败", ex); }
    }

    /// <summary>
    /// 「桌面组件」卡片：一种组件一个可折叠 <see cref="Expander"/>（与「快捷键」页同形状）。
    /// <para>
    /// 此前是把 12 种类型连同各自的全部实例一次性铺开：类型越加越多，这张卡片越长，
    /// 而用户绝大多数时候只想找某一类。现在标题上直接写"已添加 N 个 / 未添加"，
    /// 展开后才看到「添加组件」与各实例的显示 / 移除。
    /// </para>
    /// </summary>
    private void BuildWidgetRows()
    {
        var mgr = WidgetManager();
        if (mgr is null || WidgetRows is null) return;

        WidgetRows.Children.Clear();
        var instances = mgr.Instances;

        foreach (var kind in WidgetStorage.AllKinds)
        {
            var kindInstances = instances.Where(i => i.Kind == kind).ToList();

            var content = new StackPanel { Spacing = 2 };

            var addBtn = new Button
            {
                Content = "添加组件",
                Style = (Style)Application.Current.Resources["SecondaryButton"], // 仅 Style 查找（非画笔），不受主题冻结影响
                Padding = new Thickness(10, 3, 10, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var captured = kind;
            addBtn.Click += (_, _) => _ = mgr.AddInstanceAsync(captured);
            content.Children.Add(addBtn);

            // 该类型每个实例一行：显示 / 移除
            for (var idx = 0; idx < kindInstances.Count; idx++)
            {
                var inst = kindInstances[idx];
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = $"实例 {idx + 1}" + (inst.Topmost ? "（置顶）" : string.Empty),
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.8,
                };
                var id = inst.Id;
                var showBtn = new Button
                {
                    Content = "显示",
                    Style = (Style)Application.Current.Resources["SecondaryButton"], // 仅 Style 查找（非画笔），不受主题冻结影响
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                showBtn.Click += (_, _) => _ = mgr.ShowAsync(id);
                var removeBtn = new Button
                {
                    Content = "移除",
                    Style = (Style)Application.Current.Resources["SecondaryButton"], // 仅 Style 查找（非画笔），不受主题冻结影响
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                removeBtn.Click += (_, _) => _ = mgr.RemoveAsync(id);

                Grid.SetColumn(label, 0);
                Grid.SetColumn(showBtn, 1);
                Grid.SetColumn(removeBtn, 2);
                row.Children.Add(label);
                row.Children.Add(showBtn);
                row.Children.Add(removeBtn);
                content.Children.Add(row);
            }

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            header.Children.Add(new TextBlock
            {
                Text = WidgetStorage.KindTitle(kind),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush("TextFillColorPrimaryBrush", Microsoft.UI.Colors.Black),
            });
            header.Children.Add(new TextBlock
            {
                Text = kindInstances.Count == 0 ? "未添加" : $"已添加 {kindInstances.Count} 个",
                FontSize = 11,
                Opacity = 0.7,
                Foreground = Brush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
                VerticalAlignment = VerticalAlignment.Center,
            });

            WidgetRows.Children.Add(new Expander
            {
                Header = header,
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 4),
                // 一律收起：计数已写在标题上，展开后才出现实例行——这正是"不要始终显示所有实例"的落点。
                IsExpanded = false,
            });
        }
    }

    private void WidgetShowAll_Click(object sender, RoutedEventArgs e)
        => _ = WidgetManager()?.ShowAllAsync();

    private void WidgetHideAll_Click(object sender, RoutedEventArgs e)
        => _ = WidgetManager()?.HideAllAsync();

    // ==================== 布局方案 ====================

    private void BuildLayoutRows()
    {
        LayoutRowsItems.Clear();
        if (WidgetManager() is { } mgr)
            foreach (var l in mgr.GetLayouts())
                LayoutRowsItems.Add(new WidgetLayoutRow { Id = l.Id, Name = l.Name, Summary = l.Summary });
        RaisePropertyChanged(nameof(HasNoLayouts));
    }

    private async void ApplyLayout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && WidgetManager() is { } mgr)
            await mgr.ApplyLayoutAsync(id);
    }

    private async void DeleteLayout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        if (WidgetManager() is not { } mgr) return;

        var row = LayoutRowsItems.FirstOrDefault(r => r.Id == id);
        // 统一走外部居中窗口（非 ContentDialog）：按用户主题着色、可拖动、不可重复。
        var confirm = await CenteredDialog.ConfirmAsync(
            "删除布局",
            $"确定删除布局「{row?.Name ?? id}」？绑定给它切换的快捷键也会同时失效。",
            primaryText: "删除", cancelText: "取消",
            owner: App.MainWindow, dedupeKey: $"deletelayout:{id}");
        if (!confirm) return;
        await mgr.DeleteLayoutAsync(id);
    }

    // ==================== 快捷键录制 ====================
    // 组合键的修饰键状态由底层钩子累计维护（KeyboardHookService.CurrentModifiers），
    // 因此 Ctrl/Alt/Shift/Win 任意组合都能录到，且不依赖 XAML 焦点。
    //
    // 录制规则（按用户要求）：
    // · 按钮未设置时只显示「未设置」，点击后**清空按钮文字**（保留按钮样式、不高亮）进入录制态；
    // · 依次按下按键，最多录 3 个键（修饰键 + 主键）：录到第 3 个键自动结束；
    // · 只录了 1~2 个键时不自动结束，需按 Esc 表示录入完成；一个键都没录时按 Esc = 取消；
    // · 录制结束**不立即生效**（避免录完「隐藏主界面」当场就把主界面藏了），
    //   必须点「保存快捷键」（或离开本页自动保存）后才注册生效；
    // · 点击已设置的按钮 = 重新录制（清空文字后再录）；
    // · **点已设置的按钮后直接按 Esc（未录入任何键）即表示「清除该快捷键」**；
    //   点「未设置」按钮后按 Esc 仅取消（无改动）；
    // · Backspace / Delete 同样可清除该动作的绑定（均需保存后生效）。

    /// <summary>一条快捷键最多允许几个按键（修饰键 + 主键一起算）。</summary>
    private const int MaxHotkeyKeys = 3;

    private const string UnsetText = "未设置";

    private void BuildHotkeyRows()
    {
        // 有未保存的录制结果时保留内存副本，避免布局增删等重建把用户刚录的键冲掉
        if (!_hotkeysDirty)
        {
            _hotkeyBindings.Clear();
            foreach (var kv in new SettingsStore().GetHotkeyBindings())
                _hotkeyBindings[kv.Key] = kv.Value;
        }

        _layouts = WidgetManager()?.GetLayouts() ?? new List<WidgetLayout>();
        HotkeyRowsItems.Clear();
        if (HotkeyGroups is not null) HotkeyGroups.Children.Clear();

        // 先按动作建行（同时进扁平列表供冲突标注），再按分类分组
        var byCat = new Dictionary<string, List<HotkeyRow>>();
        foreach (var action in HotkeyActions.All(_layouts))
        {
            var bound = _hotkeyBindings.TryGetValue(action, out var g) && !g.IsEmpty;
            var row = new HotkeyRow
            {
                Action = action,
                ActionName = HotkeyActions.DisplayName(action, _layouts),
                BindingText = bound ? HotkeyDisplay.Display(g!) : UnsetText,
            };
            HotkeyRowsItems.Add(row);
            var cat = HotkeyActions.CategoryOf(action, _layouts);
            if (!byCat.TryGetValue(cat, out var list)) { list = new List<HotkeyRow>(); byCat[cat] = list; }
            list.Add(row);
        }

        // 按固定顺序把每个分类渲染为一个可折叠的 Expander（多级菜单），消除扁平长列表的重复感
        if (HotkeyGroups is not null)
        {
            foreach (var cat in HotkeyActions.CategoryOrder)
                if (byCat.TryGetValue(cat, out var rows))
                    HotkeyGroups.Children.Add(BuildCategoryExpander(cat, rows));
        }
        RefreshConflictMarks();
        MarkRegistrationFailures();   // 行是新建的，注册失败要在建行时就标出来（否则开页看不到原因）
    }

    /// <summary>把一个分类的动作行装进一个可折叠 <see cref="Expander"/>（标题=分类名，内容=该类的快捷键行）。</summary>
    private Expander BuildCategoryExpander(string category, IList<HotkeyRow> rows)
    {
        var repeater = new ItemsRepeater
        {
            ItemsSource = rows,
            ItemTemplate = (DataTemplate)Resources["HotkeyRowTemplate"],
        };
        repeater.Layout = new StackLayout { Spacing = 4 };

        var count = rows.Count(r => !string.Equals(r.BindingText, UnsetText, StringComparison.Ordinal));
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(new TextBlock
        {
            Text = category,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("TextFillColorPrimaryBrush", Microsoft.UI.Colors.Black),
        });
        header.Children.Add(new TextBlock
        {
            Text = $"（{count} 项已绑定）",
            FontSize = 11,
            Opacity = 0.7,
            Foreground = Brush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
            VerticalAlignment = VerticalAlignment.Center,
        });

        return new Expander
        {
            Header = header,
            Content = repeater,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 4),
            IsExpanded = category == "主界面" || category == "组件总控",   // 常用的两类默认展开
        };
    }

    private static Brush Brush(string key, Windows.UI.Color fallback) =>
        ThemeBrush.For(ElementTheme.Default, key) ?? new SolidColorBrush(fallback);

    /// <summary>把「同一组合绑定了多个动作」就地标注到每一行（不弹窗，用户可直接忽略）。</summary>
    private void RefreshConflictMarks()
    {
        var byGesture = HotkeyBindings.GetConflicts(_hotkeyBindings)
            .ToDictionary(c => c.Key, c => c.Actions);

        foreach (var row in HotkeyRowsItems)
        {
            if (!_hotkeyBindings.TryGetValue(row.Action, out var g) || g.IsEmpty)
            {
                row.ConflictText = string.Empty;
                continue;
            }
            if (!byGesture.TryGetValue(HotkeyGesture.GestureKey(g), out var actions) || actions.Count <= 1)
            {
                row.ConflictText = string.Empty;
                continue;
            }
            var others = actions.Where(a => a != row.Action)
                                .Select(a => HotkeyActions.DisplayName(a, _layouts))
                                .ToList();
            row.ConflictText = others.Count == 0
                ? string.Empty
                : "⚠ 与其它动作共用该组合：" + string.Join("、", others);
        }
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not string action) return;

        // 录制按钮保留焦点，若用户录的正是 Space / Enter，按键会顺带再次触发按钮的 Click。
        // 这里在录制结束后的短时间内忽略重复点击，避免「录完立刻又进入录制」。
        if (DateTimeOffset.UtcNow < _ignoreClickUntil) return;

        var row = HotkeyRowsItems.FirstOrDefault(r => r.Action == action);
        if (row is null) return;

        // 再次点击同一项：已录了键 → 当作「确认结束」；一个键都没录 → 取消录制
        if (_recordingAction == action)
        {
            if (RecordedKeyCount > 0) FinishRecording();
            else CancelRecording();
            return;
        }

        StopRecording(resetText: true);      // 先把上一个录制还原
        BeginRecording(action, btn, row);
    }

    /// <summary>进入录制态：先装底层钩子（装不上就说明原因、保持原显示），再清空按钮文字（不高亮）并开始回显按键。</summary>
    private void BeginRecording(string action, Button btn, HotkeyRow row)
    {
        // 先装钩子、再进录制态：钩子装不上就一个按键都收不到，而原先的顺序会先把按钮文字清空，
        // 用户看到的是"按了没反应、原来的快捷键也不显示了"，而不是一句"录制现在不可用"。
        if (!_hkHook.TryStart(out var hookError))
        {
            var why = HotkeyErrorText.HookInstallFailure(hookError);
            StarMark.Abstractions.StarLog.Warn($"无法安装键盘钩子：{why} ⇒ 快捷键录制不可用");
            RecordingAlert = why + "——因此收不到你按下的键。再点一次这个按钮即可重试。";
            return;
        }
        RecordingAlert = string.Empty;

        _recordingAction = action;
        _recordingButton = btn;
        _recordingRow = row;
        _recordingPrevText = row.BindingText;
        _recordingWasSet = _hotkeyBindings.TryGetValue(action, out var g) && !g.IsEmpty;
        _recordedModifiers = HotkeyModifiers.None;
        _recordedMainKey = 0;

        row.IsRecording = true;
        row.BindingText = string.Empty;                 // 清空文字，保留按钮样式——用户可实时看到按下的键
        row.ConflictText = string.Empty;
        // 注意：不设置 btn.Background，保持默认按钮样式（按用户要求不高亮）

        // 录制期间挂起全局热键，避免按下的键命中已生效的热键（如正在重录「隐藏主界面」
        // 却按下了它的旧键）导致当场触发动作、打断录制。
        HotkeySvc()?.Suspend();
        btn.Focus(FocusState.Programmatic);
    }

    private void OnHookKeyDown(uint vk)
    {
        if (_recordingAction is null || _recordingRow is null) return;
        var row = _recordingRow;

        // Esc：
        // · 已录入若干键 → 确认结束（定稿）；
        // · 一个键都没录、且该动作原本就有绑定 → 视为「清除该快捷键」（按用户要求）；
        // · 一个键都没录、且原本未设置 → 取消录制（无改动）。
        if (vk == (uint)VirtualKey.Escape)
        {
            if (RecordedKeyCount > 0) FinishRecording();
            else if (_recordingWasSet) ClearBinding();
            else CancelRecording();
            return;
        }

        // Backspace / Delete：清除该动作的快捷键（同样需保存后生效）
        if (vk == (uint)VirtualKey.Back || vk == (uint)VirtualKey.Delete) { ClearBinding(); return; }

        // 修饰键：累加到已录组合；仅当已有主键且总键数达到上限才自动结束
        // （纯修饰键不能单独构成热键，不结束，避免录成空绑定）
        if (HotkeyKeys.IsModifier(vk))
        {
            var mod = HotkeyKeys.ModifierOf(vk);
            // 走到这里 mod 必非 None（IsModifier 与取位出自同一张表），只需处理"重复按下同一修饰键"
            if (_recordedModifiers.HasFlag(mod)) { UpdateRecordingText(); return; }
            _recordedModifiers |= mod;
            UpdateRecordingText();
            if (_recordedMainKey != 0 && RecordedKeyCount >= MaxHotkeyKeys) FinishRecording();
            return;
        }

        // 主键：最后一个按下的非修饰键即主键
        _recordedModifiers |= _hkHook.CurrentModifiers;   // 钩子漏记的修饰键补上
        _recordedMainKey = vk;
        UpdateRecordingText();
        if (_recordedMainKey != 0 && RecordedKeyCount >= MaxHotkeyKeys) FinishRecording();
    }

    private void OnHookKeyUp(uint vk)
    {
        if (_recordingAction is null || _recordingRow is null) return;
        UpdateRecordingText();
    }

    /// <summary>当前已录入的按键个数（修饰键个数 + 主键）。</summary>
    private int RecordedKeyCount => CountModifiers(_recordedModifiers) + (_recordedMainKey != 0 ? 1 : 0);

    private static int CountModifiers(HotkeyModifiers m) => HotkeyKeys.ModifierBits.Count(bit => m.HasFlag(bit));

    /// <summary>把已录入的键实时显示在按钮上（未录任何键时保持空白）。</summary>
    private void UpdateRecordingText()
    {
        if (_recordingRow is null) return;
        var mods = HotkeyDisplay.ModifiersDisplay(_recordedModifiers);
        var text = _recordedMainKey == 0
            ? mods
            : (mods.Length == 0 ? HotkeyDisplay.KeyName(_recordedMainKey) : $"{mods} + {HotkeyDisplay.KeyName(_recordedMainKey)}");
        _recordingRow.BindingText = text;
    }

    /// <summary>结束录制：只落到内存待保存字典里，**不注册生效**（需点保存）。</summary>
    private void FinishRecording()
    {
        var action = _recordingAction;
        var row = _recordingRow;
        if (action is null || row is null) { CancelRecording(); return; }

        // 定稿常常就由这最后一次按下触发，而 StopRecording 会在同一个钩子回调里把热键 Resume
        // 回注册态 ⇒ 这次按键若继续派发给系统，会当场命中（可能是别的动作已绑的）热键并执行，
        // 表现为"刚设置完就触发一次"。把这一键吞掉，动作只能等用户点「保存快捷键」后由新键触发。
        _hkHook.SwallowCurrentKeyDown();

        _hotkeyBindings[action] = new HotkeyGesture(_recordedModifiers | HotkeyModifiers.NoRepeat, _recordedMainKey);

        StopRecording(resetText: false);
        row.IsRecording = false;
        var g = _hotkeyBindings[action];
        row.BindingText = g.IsEmpty ? UnsetText : HotkeyDisplay.Display(g);
        _ignoreClickUntil = DateTimeOffset.UtcNow.AddMilliseconds(400);  // 吞掉本键附带的那次 Click
        _hotkeysDirty = true;
        RaiseHotkeyStateChanged();
        RefreshConflictMarks();
    }

    /// <summary>停止钩子并复位录制态；resetText 时把该行显示还原到录制前。</summary>
    private void StopRecording(bool resetText)
    {
        _hkHook.Stop();
        // 录制结束（完成 / 取消 / 切换 / 离开页面）即恢复全局热键
        HotkeySvc()?.Resume();
        if (resetText && _recordingRow is not null)
        {
            _recordingRow.IsRecording = false;
            _recordingRow.BindingText = _recordingPrevText ?? UnsetText;
        }
        _recordingAction = null;
        _recordingButton = null;
        _recordingRow = null;
        _recordingPrevText = null;
        _recordingWasSet = false;
        _recordedModifiers = HotkeyModifiers.None;
        _recordedMainKey = 0;
    }

    private void CancelRecording()
    {
        StopRecording(resetText: true);
        RefreshConflictMarks();
    }

    /// <summary>
    /// 清除当前动作的绑定（不立即生效，需保存）。
    /// <para>
    /// 写的是<b>空手势</b>而不是从字典里 Remove：磁盘上必须留下"用户明确清过这一条"的痕迹，
    /// 否则 <c>GetHotkeyBindings</c> 的"默认 + 已存"合并会因为键缺失而把默认手势补回来 ⇒
    /// 表现为「Ctrl+Alt+Space（切换主界面）清不掉」，且界面显示与真实注册状态还会不一致。
    /// </para>
    /// </summary>
    private void ClearBinding()
    {
        if (_recordingAction is null) return;
        var row = _recordingRow;
        // 同 FinishRecording：清除也由一次按下（Esc / Backspace / Delete）触发，而 StopRecording
        // 会在同一回调里恢复热键注册 ⇒ 不吞掉就会让这一键当场命中某个已生效的组合并执行动作。
        _hkHook.SwallowCurrentKeyDown();
        _hotkeyBindings[_recordingAction] = new HotkeyGesture(HotkeyModifiers.NoRepeat, 0);
        StopRecording(resetText: false);
        if (row is not null) row.BindingText = UnsetText;
        _hotkeysDirty = true;
        RaiseHotkeyStateChanged();
        RefreshConflictMarks();
    }

    // ── 保存 / 撤销 / 恢复默认 ──

    /// <summary>把内存里的绑定真正写盘并注册生效（快捷键录制结果必须走这一步）。</summary>
    private void SaveHotkeys_Click(object sender, RoutedEventArgs e)
    {
        StopRecording(resetText: true);
        ApplyHotkeyBindings();
        _hotkeysDirty = false;
        RaiseHotkeyStateChanged();
        BuildHotkeyRows();
    }

    /// <summary>放弃未保存的录制结果，回到磁盘上的绑定。</summary>
    private void DiscardHotkeys_Click(object sender, RoutedEventArgs e)
    {
        StopRecording(resetText: true);
        _hotkeysDirty = false;
        RaiseHotkeyStateChanged();
        BuildHotkeyRows();
    }

    private async void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        // 破坏性操作：先确认（统一走外部居中窗口：按用户主题着色、可拖动、不可重复）
        var confirm = await CenteredDialog.ConfirmAsync(
            "恢复默认快捷键",
            "将清空所有自定义快捷键，只保留默认的「Ctrl + Alt + Space（切换主界面）」。此操作不可撤销。",
            primaryText: "恢复默认", cancelText: "取消",
            owner: App.MainWindow, dedupeKey: "resethotkeys");
        if (!confirm) return;

        StopRecording(resetText: true);
        var defaults = HotkeyBindings.Defaults();
        _hotkeyBindings.Clear();
        foreach (var kv in defaults) _hotkeyBindings[kv.Key] = kv.Value;
        ApplyHotkeyBindings();      // 确认后立即生效（用户显式确认过）
        _hotkeysDirty = false;
        RaiseHotkeyStateChanged();
        BuildHotkeyRows();
    }

    /// <summary>存在尚未保存的快捷键改动（录制完成 / 清除后为 true）。</summary>
    public bool HasUnsavedHotkeys => _hotkeysDirty;

    private void RaiseHotkeyStateChanged() => RaisePropertyChanged(nameof(HasUnsavedHotkeys));

    /// <summary>保存绑定 → 热更新 HotkeyService（冲突信息由行内标记呈现，不弹窗）。</summary>
    private void ApplyHotkeyBindings()
    {
        var settings = new SettingsStore();
        settings.SaveHotkeyBindings(_hotkeyBindings);

        if (App.Services.GetRequiredService<HotkeyService>() is HotkeyService hotkey)
            hotkey.ApplyBindings(ViewModel.EnableGlobalHotKey
                ? _hotkeyBindings
                : new Dictionary<string, HotkeyGesture>());
        MarkRegistrationFailures();   // 保存后立刻把"没注册上"的那几条说清楚
    }

    private void EnableGlobalHotKey_Toggled(object sender, RoutedEventArgs e)
    {
        // 即时生效：总开关关闭时清空所有热键，开启时应用当前绑定
        var settings = new SettingsStore();
        settings.SaveEnableGlobalHotKey(ViewModel.EnableGlobalHotKey);
        ApplyHotkeyBindings();
    }

    /// <summary>
    /// 把"注册失败（组合键已被其它程序占用）"说出来：逐行标原因 + 一条带「重试注册」的汇总。
    /// <para>
    /// 原先这一失败只写 <c>StarLog.Warn</c>：那一行仍显示用户刚录的组合、「保存快捷键」也算成功，
    /// 用户看到的只是"设了却永远不生效"，且没有任何地方告诉他为什么（P-54：失败要看得见、点得动）。
    /// </para>
    /// </summary>
    private void MarkRegistrationFailures()
    {
        var failed = HotkeySvc()?.RegistrationFailures ?? Array.Empty<HotkeyRegistrationFailure>();
        var reasons = new Dictionary<string, string>();
        foreach (var f in failed) reasons[HotkeyGesture.GestureKey(f.Gesture)] = f.Reason;

        foreach (var row in HotkeyRowsItems)
        {
            row.RegisterErrorText =
                _hotkeyBindings.TryGetValue(row.Action, out var g) && !g.IsEmpty
                && reasons.TryGetValue(HotkeyGesture.GestureKey(g), out var why) ? "未注册：" + why : string.Empty;
        }

        RegisterErrorSummary = failed.Count == 0
            ? string.Empty
            : $"有 {failed.Count} 个组合键未能注册：{string.Join("、", failed.Select(f => $"{HotkeyDisplay.Display(f.Gesture)}（{f.Reason}）"))}。"
              + "占用它的程序退出后点「重试注册」；或给这些动作换一个组合再点「保存快捷键」。";
    }

    /// <summary>重试注册：不改绑定内容，只再向系统注册一次（用户可能刚关掉占用该组合键的程序）。</summary>
    private void RetryHotkeyRegister_Click(object sender, RoutedEventArgs e)
    {
        if (HotkeySvc() is { } hotkey && ViewModel.EnableGlobalHotKey)
            hotkey.ApplyBindings(new SettingsStore().GetHotkeyBindings());   // 注册的是磁盘上的绑定，不含未保存的录制
        MarkRegistrationFailures();
    }

    private string _registerErrorSummary = string.Empty;

    /// <summary>未能注册的组合键汇总（空＝全部注册成功，提示与按钮随之隐藏）。</summary>
    public string RegisterErrorSummary
    {
        get => _registerErrorSummary;
        set
        {
            if (_registerErrorSummary == value) return;
            _registerErrorSummary = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(HasRegisterErrors));
        }
    }

    public bool HasRegisterErrors => !string.IsNullOrEmpty(_registerErrorSummary);

    private string _recordingAlert = string.Empty;

    /// <summary>键盘钩子装不上时的一次性说明（成功后自动清空）。装不上＝一个按键都收不到。</summary>
    public string RecordingAlert
    {
        get => _recordingAlert;
        set
        {
            if (_recordingAlert == value) return;
            _recordingAlert = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(HasRecordingAlert));
        }
    }

    public bool HasRecordingAlert => !string.IsNullOrEmpty(_recordingAlert);

    // ==================== 底部操作 ====================

    private void Back_Click(object sender, RoutedEventArgs e)
        => App.MainWindow?.NavigateTo("tree");

    /// <summary>「打开热榜页」：刚开启开关的人不必自己回导航栏找那一项（就地入口，P-54 口径）。</summary>
    private void OpenTrending_Click(object sender, RoutedEventArgs e)
        => App.MainWindow?.NavigateTo("trending");

    // ==================== 数据备份与恢复（P0-2） ====================

    // WinRT 的文件选择器要走系统对话框宿主（中 IL）。本地磁盘搜索会让 StarMark 以管理员运行，
    // 此时 PickSaveFileAsync/PickSingleFileAsync 稳定抛 COMException E_FAIL（真机日志：提权 pid 内连挂 4 次），
    // 且 try 若从 picker 之后才开始，异常就冲到 UI 兜底网、用户只看到"点了没反应"。
    // 处置：picker 调用本身进 try；失败**不再要求用户换权限重启**，而是退回应用内路径输入框（见 RequestBackupPathAsync）。
    private const string PickerBlockedHint =
        "系统文件对话框在当前会话调不起来（StarMark 以管理员身份运行，而对话框宿主在普通权限）——改用路径输入框。";

    /// <summary>调起 WinRT 选择器；返回 null 表示用户取消。（提权下抛 InvalidOperationException＝对话框宿主不可用。）</summary>
    private static async Task<Windows.Storage.IStorageItem?> PickAsync(
        Windows.Storage.Pickers.FileOpenPicker picker)
    {
        try { return await picker.PickSingleFileAsync(); }
        catch (Exception ex)
        {
            StarLog.Error("文件选择器调起失败（多为提权进程跨完整性级别访问系统对话框宿主被拦）", ex);
            throw new InvalidOperationException(PickerBlockedHint, ex);
        }
    }

    private static async Task<Windows.Storage.IStorageItem?> PickAsync(
        Windows.Storage.Pickers.FileSavePicker picker)
    {
        try { return await picker.PickSaveFileAsync(); }
        catch (Exception ex)
        {
            StarLog.Error("文件保存对话框调起失败（多为提权进程跨完整性级别访问系统对话框宿主被拦）", ex);
            throw new InvalidOperationException(PickerBlockedHint, ex);
        }
    }

    private static async Task<string?> PickNativeAsync(bool save, string fileName)
    {
        var hwnd = WindowInterop.GetHwnd(App.MainWindow!);
        if (save)
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeChoices.Add("JSON 备份", new[] { ".json" });
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(fileName);
            return (await PickAsync(picker))?.Path;
        }
        var open = new FileOpenPicker();
        InitializeWithWindow.Initialize(open, hwnd);
        open.FileTypeFilter.Add(".json");
        return (await PickAsync(open))?.Path;
    }

    /// <summary>
    /// 取得备份文件路径。普通权限会话仍用系统选择器（用户熟悉、能浏览）；提权会话里宿主调不通，
    /// 直接退回应用内输入框——**不让用户为了备份去关开关、降权限、重启**。
    /// 输入非法时带着原因再问一次（就地改正），而不是失败退出后再点一遍。
    /// </summary>
    private static async Task<string?> RequestBackupPathAsync(bool save, string suggestedPath)
    {
        if (!Privilege.IsElevated())
        {
            try { return await PickNativeAsync(save, Path.GetFileName(suggestedPath)); }
            catch (InvalidOperationException) { /* 宿主不可用（不止提权一种成因）：落到输入框 */ }
        }

        string? error = null;
        var text = suggestedPath;
        while (true)
        {
            var input = await CenteredDialog.PromptAsync(
                save ? "导出备份到" : "导入备份自",
                message: error ?? PickerBlockedHint,
                placeholder: $"完整路径，例：{suggestedPath}",
                defaultText: text,
                primaryText: save ? "导出" : "导入",
                owner: App.MainWindow);
            if (input is null) return null;   // 取消

            var (path, validation) = save
                ? BackupPathPolicy.ForExport(input, BackupService.SnapshotDirectory)
                : BackupPathPolicy.ForImport(input);
            if (validation is null) return path;

            // 校验不过就带着原因重问，且保留用户刚敲的内容——他要改的只是一个字符，不该从头再来。
            error = validation;
            text = input;
        }
    }

    /// <summary>备份目录里最近改动过的一份 .json（目录不存在/被占用时返回 null，不打断导入流程）。</summary>
    private static string? NewestBackupPath()
    {
        try
        {
            var dir = BackupService.SnapshotDirectory;
            if (!Directory.Exists(dir)) return null;
            return new DirectoryInfo(dir).EnumerateFiles("*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            // 只是"默认值取不到"，不是失败：用户照样能手输路径，故只记一行不弹提示。
            StarLog.Warn($"扫描备份目录失败，导入对话框将退回目录本身作默认值：{ex.Message}");
            return null;
        }
    }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBackupBusy) return;
        ViewModel.IsBackupBusy = true;      // 必须覆盖"弹对话框"阶段：否则等待期间可重复点，多个 picker 并存在提权下更是必挂
        try
        {
            // 默认落在备份目录（那里已有"恢复前快照"），用户回车即接受，不必从 C:\ 一路敲过来。
            var suggested = Path.Combine(BackupService.SnapshotDirectory,
                $"starmark-backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
            var target = await RequestBackupPathAsync(save: true, suggested);
            if (target is null) return;

            await Task.Run(() => _backup.ExportToFileAsync(target, CancellationToken.None));
            ViewModel.BackupStatus = $"已导出备份到：{target}";
            ViewModel.RefreshBackups();     // 刚导出那份必须立刻出现在下面列表里，否则用户会以为没写成
        }
        catch (Exception ex)
        {
            ViewModel.BackupStatus = ex.Message;
        }
        finally { ViewModel.IsBackupBusy = false; }
    }

    private async void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBackupBusy) return;
        ViewModel.IsBackupBusy = true;      // 同导出：选择器阶段也要占住忙碌位，避免重复点击叠出第二个 picker
        try
        {
            // 默认指向最近一份备份（导入最常就是"回到上一次"），没有就直接给备份目录当输入起点。
            var suggested = NewestBackupPath()
                ?? BackupService.SnapshotDirectory + Path.DirectorySeparatorChar;
            var source = await RequestBackupPathAsync(save: false, suggested);
            if (source is null) return;
            await ImportFromPathAsync(source);
        }
        catch (Exception ex)
        {
            ViewModel.BackupStatus = $"导入失败：{ex.Message}";
        }
        finally { ViewModel.IsBackupBusy = false; }
    }

    /// <summary>列表里某一行的「恢复这份 / 用它回滚」：跳过选择器，直接走与导入完全相同的那条链。</summary>
    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBackupBusy) return;
        if (sender is not Microsoft.UI.Xaml.Controls.Button { Tag: BackupService.BackupFile file }) return;
        ViewModel.IsBackupBusy = true;
        try
        {
            await ImportFromPathAsync(file.Path);
        }
        catch (Exception ex)
        {
            ViewModel.BackupStatus = $"恢复失败：{ex.Message}";
        }
        finally { ViewModel.IsBackupBusy = false; }
    }

    /// <summary>打开备份目录（备份就在本机，"去看一眼/自己拷走"不该让用户去地址栏敲路径）。</summary>
    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = BackupService.SnapshotDirectory;
            Directory.CreateDirectory(dir);     // 首次使用时目录可能还没建；开着空目录也比报错有用
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"")
            { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ViewModel.BackupStatus = $"打不开备份目录：{ex.Message}";
            StarMark.Abstractions.StarLog.Warn($"打开备份目录失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 从一份已知路径导入。<b>「导入备份…」与列表里的「恢复这份」共用这一条</b>——
    /// 两条路径若各写一遍，摘要/合并-覆盖确认/恢复前快照这些护栏很容易只存在于其中一条。
    /// 调用方负责 <see cref="SettingsPageViewModel.IsBackupBusy"/> 的置位与异常兜底。
    /// </summary>
    private async Task ImportFromPathAsync(string source)
    {
        // 备份文件可达数十 MB：整份读盘 + 反序列化必须在后台线程做（Microsoft.Data.Sqlite 与
        // File/Json 都是同步实现，await 并不让出），否则"点导入备份"就是先冻住整个界面几秒。
        BackupEnvelope env;
        try
        {
            env = await Task.Run(() => BackupService.ReadAsync(source, CancellationToken.None));
        }
        catch (BackupFormatException ex)
        {
            ViewModel.BackupStatus = ex.Message;
            return;
        }

        // 摘要直接从已解析的 env 算：旧代码再调 Peek(file.Path)，等于把整份备份第二次读盘+反序列化。
        var summary = BackupService.Summarize(env);
        var detail = $"文件：{Path.GetFileName(source)}\n"
            + $"导出时间：{DateTimeOffset.FromUnixTimeSeconds(summary.ExportedAt):yyyy-MM-dd HH:mm}\n"
            + $"条目 {summary.ItemCount}　用户状态 {summary.UserStateCount}　标签 {summary.TagCount}"
            + (summary.HasWidgets ? "　组件数据：有" : "");

        // 统一走外部居中窗口（非 ContentDialog）：按用户主题着色、可拖动、不可重复。
        // 三选一场景（合并导入 / 覆盖导入 / 取消）用 ShowContentAsync 的 primary+secondary 双按钮。
        var detailBlock = new TextBlock
        {
            Text = $"{detail}\n\n合并导入：保留现有条目，仅补充/覆盖用户元数据（安全、可重复）。\n" +
                   "覆盖导入：先清空再导入，精确还原到备份时刻（会丢掉备份之后新增的条目）。\n" +
                   "两种都会先自动留下一份「恢复前快照」，想撤销就在列表里点它的「用它回滚」。",
            TextWrapping = TextWrapping.Wrap,
        };
        var choice = await CenteredDialog.ShowContentAsync(
            "导入备份", detailBlock, owner: App.MainWindow, dedupeKey: "importbackup",
            width: 480, height: 340,
            primaryText: "合并导入", secondaryText: "覆盖导入", cancelText: "取消");

        if (choice != CenteredDialog.HostedDialogResult.Committed
            && choice != CenteredDialog.HostedDialogResult.Secondary) return;

        var mode = choice == CenteredDialog.HostedDialogResult.Secondary ? RestoreMode.Replace : RestoreMode.Merge;
        var rr = await Task.Run(() => _backup.RestoreAsync(env, mode, null, CancellationToken.None));
        ViewModel.BackupStatus = rr.Success
            ? $"{rr.Message}（恢复前快照：{rr.SnapshotPath}）"
            : $"导入失败：{rr.Message}";
        ViewModel.RefreshBackups();     // 这次恢复留下的快照要立刻可见——它就是"撤销"的入口
    }
}

/// <summary>快捷键录制列表的单行模型（动作名 + 当前绑定显示 + 录制态 + 冲突标注）。</summary>
public sealed class HotkeyRow : INotifyPropertyChanged
{
    public string Action { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;

    private string _bindingText = string.Empty;
    public string BindingText
    {
        get => _bindingText;
        set { if (_bindingText != value) { _bindingText = value; OnChanged(); } }
    }

    private string _conflictText = string.Empty;
    public string ConflictText
    {
        get => _conflictText;
        set { if (_conflictText != value) { _conflictText = value; OnChanged(); OnChanged(nameof(HasConflict)); } }
    }

    /// <summary>是否与其它动作共用同一组合（仅提示，允许保留）。</summary>
    public bool HasConflict => !string.IsNullOrEmpty(_conflictText);

    private string _registerErrorText = string.Empty;

    /// <summary>该组合未能注册时的原因；空＝已注册或不适用（未绑定）。</summary>
    public string RegisterErrorText
    {
        get => _registerErrorText;
        set { if (_registerErrorText != value) { _registerErrorText = value; OnChanged(); OnChanged(nameof(HasRegisterError)); } }
    }

    public bool HasRegisterError => !string.IsNullOrEmpty(_registerErrorText);

    private bool _isRecording;
    public bool IsRecording
    {
        get => _isRecording;
        set { if (_isRecording != value) { _isRecording = value; OnChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

/// <summary>布局方案列表的单行模型（名称 + 摘要 + 应用/删除）。</summary>
public sealed class WidgetLayoutRow
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}
