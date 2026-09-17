#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using StarMark.Abstractions.Backup;
using StarMark.Core.Backup;
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
        _ = ViewModel.LoadHealthAsync();
        BuildWidgetRows();
        BuildHotkeyRows();
        BuildLayoutRows();
    }

    /// <summary>读取设置但不触发「实时保存」（加载本身产生的属性变更无须回写）。</summary>
    private void LoadFromStoreSilently()
    {
        _suppressSave = true;
        try { ViewModel.LoadFromStore(); }
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

    private static bool IsDisplayOnlyProperty(string name)
        => name.Contains("Busy") || name.Contains("Loading") || name.Contains("Error")
        || name.Contains("Status") || name.Contains("Report") || name.Contains("Diagnostic")
        || name.Contains("Trend") || name.Contains("Summary") || name.Contains("Score");

    private void ScheduleAutoSave()
    {
        _autoSaveTimer?.Stop();
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            ViewModel.SaveCommand.Execute(null);   // 实时落盘（含外观 / 磁吸）
            App.MainWindow?.ApplyTraySettings();   // 托盘/热键即时生效
        };
        _autoSaveTimer.Start();
    }

    private void SaveRoots_Click(object sender, RoutedEventArgs e)
    {
        // 多行目录列表属需编辑确认项：点击时输入框已失焦 TwoWay 回写，此处显式落盘
        _autoSaveTimer?.Stop();
        ViewModel.SaveCommand.Execute(null);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadFromStoreSilently();
        _ = ViewModel.LoadHealthAsync();
        _ = ViewModel.LoadDiagnosticsAsync();
        if (WidgetManager() is { } mgr)
        {
            mgr.InstancesChanged -= OnInstancesChanged;   // 防重复订阅
            mgr.InstancesChanged += OnInstancesChanged;
            mgr.LayoutsChanged -= OnLayoutsChanged;
            mgr.LayoutsChanged += OnLayoutsChanged;
        }
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
        => App.Services.GetService(typeof(WidgetManager)) as WidgetManager;

    private HotkeyService? HotkeySvc()
        => App.Services.GetService(typeof(HotkeyService)) as HotkeyService;

    /// <summary>
    /// 逐类型列出：类型标题 +「添加组件」按钮（可重复添加同类型），其下为该类型的每个实例一行
    /// （显示 / 移除）。置顶实例在标签后标注（置顶）。
    /// </summary>
    private void BuildWidgetRows()
    {
        var mgr = WidgetManager();
        if (mgr is null || WidgetRows is null) return;

        WidgetRows.Children.Clear();
        var instances = mgr.Instances;

        foreach (var kind in WidgetStorage.AllKinds)
        {
            // 类型标题行：名称 + 添加按钮
            var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = WidgetStorage.KindTitle(kind),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var addBtn = new Button
            {
                Content = "添加组件",
                Style = (Style)Application.Current.Resources["SecondaryButton"],
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var captured = kind;
            addBtn.Click += (_, _) => _ = mgr.AddInstanceAsync(captured);

            Grid.SetColumn(title, 0);
            Grid.SetColumn(addBtn, 1);
            header.Children.Add(title);
            header.Children.Add(addBtn);
            WidgetRows.Children.Add(header);

            // 该类型每个实例一行：显示 / 移除
            var kindInstances = instances.Where(i => i.Kind == kind).ToList();
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
                    Style = (Style)Application.Current.Resources["SecondaryButton"],
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                showBtn.Click += (_, _) => _ = mgr.ShowAsync(id);
                var removeBtn = new Button
                {
                    Content = "移除",
                    Style = (Style)Application.Current.Resources["SecondaryButton"],
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
                WidgetRows.Children.Add(row);
            }

            // 类型之间分隔
            WidgetRows.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 6, 0, 6),
                Background = ThemeBrush.For(ElementTheme.Default, "WidgetDividerBrush")
                             ?? new SolidColorBrush(Microsoft.UI.Colors.Gray),
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
        var dlg = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "删除布局",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = $"确定删除布局「{row?.Name ?? id}」？绑定给它切换的快捷键也会同时失效。",
                TextWrapping = TextWrapping.Wrap,
            },
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
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
        foreach (var action in HotkeyActions.All(_layouts))
        {
            var bound = _hotkeyBindings.TryGetValue(action, out var g) && !g.IsEmpty;
            HotkeyRowsItems.Add(new HotkeyRow
            {
                Action = action,
                ActionName = HotkeyActions.DisplayName(action, _layouts),
                BindingText = bound ? g!.Display : UnsetText,
            });
        }
        RefreshConflictMarks();
    }

    /// <summary>把「同一组合绑定了多个动作」就地标注到每一行（不弹窗，用户可直接忽略）。</summary>
    private void RefreshConflictMarks()
    {
        var byGesture = HotkeyService.GetConflicts(_hotkeyBindings)
            .ToDictionary(c => HotkeyGesture.GestureKey(c.Gesture), c => c.Actions);

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

    /// <summary>进入录制态：清空按钮文字（保持按钮样式，不高亮）、记录该动作是否原有绑定、安装底层钩子。</summary>
    private void BeginRecording(string action, Button btn, HotkeyRow row)
    {
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

        if (!_hkHook.TryStart())
            StarMark.Abstractions.StarLog.Warn("无法安装键盘钩子，录制可能不稳定");
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
        if (IsModifierKey((VirtualKey)vk))
        {
            var mod = ModifierOf(vk);
            if (mod == HotkeyModifiers.None || _recordedModifiers.HasFlag(mod)) { UpdateRecordingText(); return; }
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

    private static int CountModifiers(HotkeyModifiers m)
    {
        var n = 0;
        if (m.HasFlag(HotkeyModifiers.Control)) n++;
        if (m.HasFlag(HotkeyModifiers.Alt)) n++;
        if (m.HasFlag(HotkeyModifiers.Shift)) n++;
        if (m.HasFlag(HotkeyModifiers.Windows)) n++;
        return n;
    }

    private static HotkeyModifiers ModifierOf(uint vk) => vk switch
    {
        0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Control,
        0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
        0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
        0x5B or 0x5C => HotkeyModifiers.Windows,
        _ => HotkeyModifiers.None,
    };

    /// <summary>把已录入的键实时显示在按钮上（未录任何键时保持空白）。</summary>
    private void UpdateRecordingText()
    {
        if (_recordingRow is null) return;
        var mods = HotkeyGesture.ModifiersDisplay(_recordedModifiers);
        var text = _recordedMainKey == 0
            ? mods
            : (mods.Length == 0 ? HotkeyGesture.KeyName(_recordedMainKey) : $"{mods} + {HotkeyGesture.KeyName(_recordedMainKey)}");
        _recordingRow.BindingText = text;
    }

    /// <summary>结束录制：只落到内存待保存字典里，**不注册生效**（需点保存）。</summary>
    private void FinishRecording()
    {
        var action = _recordingAction;
        var row = _recordingRow;
        if (action is null || row is null) { CancelRecording(); return; }

        _hotkeyBindings[action] = new HotkeyGesture(_recordedModifiers | HotkeyModifiers.NoRepeat, _recordedMainKey);

        StopRecording(resetText: false);
        row.IsRecording = false;
        var g = _hotkeyBindings[action];
        row.BindingText = g.IsEmpty ? UnsetText : g.Display;
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

    /// <summary>清除当前动作的绑定（不立即生效，需保存）。</summary>
    private void ClearBinding()
    {
        if (_recordingAction is null) return;
        var row = _recordingRow;
        _hotkeyBindings.Remove(_recordingAction);
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
        // 破坏性操作：先确认
        if (this.XamlRoot is null) return;
        var dlg = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "恢复默认快捷键",
            PrimaryButtonText = "恢复默认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = "将清空所有自定义快捷键，只保留默认的「Ctrl + Alt + Space（切换主界面）」。此操作不可撤销。",
                TextWrapping = TextWrapping.Wrap,
            },
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        StopRecording(resetText: true);
        var defaults = SettingsStore.DefaultHotkeyBindings();
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

        if (App.Services.GetService(typeof(HotkeyService)) is HotkeyService hotkey)
            hotkey.ApplyBindings(ViewModel.EnableGlobalHotKey
                ? _hotkeyBindings
                : new Dictionary<string, HotkeyGesture>());
    }

    private void EnableGlobalHotKey_Toggled(object sender, RoutedEventArgs e)
    {
        // 即时生效：总开关关闭时清空所有热键，开启时应用当前绑定
        var settings = new SettingsStore();
        settings.SaveEnableGlobalHotKey(ViewModel.EnableGlobalHotKey);
        ApplyHotkeyBindings();
    }

    private static bool IsModifierKey(VirtualKey key) => key is
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
        VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu or
        VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or
        VirtualKey.LeftWindows or VirtualKey.RightWindows;

    // ==================== 底部操作 ====================

    private void Back_Click(object sender, RoutedEventArgs e)
        => App.MainWindow?.NavigateTo("tree");

    // ==================== 数据备份与恢复（P0-2） ====================

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBackupBusy) return;
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowInterop.GetHwnd(App.MainWindow!));
        picker.FileTypeChoices.Add("JSON 备份", new[] { ".json" });
        picker.SuggestedFileName = $"starmark-backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        ViewModel.IsBackupBusy = true;
        try
        {
            await _backup.ExportToFileAsync(file.Path, CancellationToken.None);
            ViewModel.BackupStatus = $"已导出备份到：{file.Path}";
        }
        catch (Exception ex)
        {
            ViewModel.BackupStatus = $"导出失败：{ex.Message}";
        }
        finally { ViewModel.IsBackupBusy = false; }
    }

    private async void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBackupBusy) return;
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowInterop.GetHwnd(App.MainWindow!));
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        ViewModel.IsBackupBusy = true;
        try
        {
            BackupEnvelope env;
            try
            {
                env = await BackupService.ReadAsync(file.Path, CancellationToken.None);
            }
            catch (BackupFormatException ex)
            {
                ViewModel.BackupStatus = ex.Message;
                return;
            }

            var summary = BackupService.Peek(file.Path);
            var detail = summary is not null
                ? $"导出时间：{DateTimeOffset.FromUnixTimeSeconds(summary.ExportedAt):yyyy-MM-dd HH:mm}\n" +
                  $"条目 {summary.ItemCount}　用户状态 {summary.UserStateCount}　标签 {summary.TagCount}" +
                  (summary.HasWidgets ? "　组件数据：有" : "")
                : "（无法读取摘要）";

            var dlg = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "导入备份",
                PrimaryButtonText = "合并导入",
                SecondaryButtonText = "覆盖导入",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                Content = new TextBlock
                {
                    Text = $"{detail}\n\n合并导入：保留现有条目，仅补充/覆盖用户元数据（安全、可重复）。\n" +
                           "覆盖导入：先清空再导入，精确还原到备份时刻（会丢掉备份之后新增的条目）。",
                    TextWrapping = TextWrapping.Wrap,
                },
            };

            var result = await dlg.ShowAsync();
            if (result != ContentDialogResult.Primary && result != ContentDialogResult.Secondary) return;

            var mode = result == ContentDialogResult.Secondary ? RestoreMode.Replace : RestoreMode.Merge;
            var rr = await _backup.RestoreAsync(env, mode, null, CancellationToken.None);
            ViewModel.BackupStatus = rr.Success
                ? $"{rr.Message}（恢复前快照：{rr.SnapshotPath}）"
                : $"导入失败：{rr.Message}";
        }
        catch (Exception ex)
        {
            ViewModel.BackupStatus = $"导入失败：{ex.Message}";
        }
        finally { ViewModel.IsBackupBusy = false; }
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
