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
        InitRssSection();   // 网址来源（RSS）那一栏：状态只服务它自己，见 SettingsPage.Rss.cs
        InitAiSection();      // AI 助手那一栏：同上，见 SettingsPage.Ai.cs
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

    /// <summary>
    /// 按<b>页签标题</b>选中一页（从别处跳进来时用，例如时钟组件右键「完整设置」→「健康与诊断」）。
    /// <para>用标题而不是下标：页签增删时调用方不该跟着改号。找不到就留在当前页并记一条日志——
    /// 改名是这条路径唯一的失效方式，而它该留下话，而不是让人以为"点了没反应"。</para>
    /// </summary>
    public void SelectTab(string? header)
    {
        if (string.IsNullOrEmpty(header)) return;
        foreach (var item in SettingsTabs.TabItems)
        {
            if (item is not TabViewItem tab) continue;
            if (!string.Equals(tab.Header as string, header, StringComparison.Ordinal)) continue;
            SettingsTabs.SelectedItem = tab;
            return;
        }
        StarMark.Abstractions.StarLog.Warn($"[设置页] 没有标题为「{header}」的页签（被改名了？）");
    }

    /// <summary>读取设置但不触发「实时保存」（加载本身产生的属性变更无须回写）。</summary>
    private void LoadFromStoreSilently()
    {
        _suppressSave = true;
        try
        {
            ViewModel.LoadFromStore();
            // Token 这一格是 PasswordBox：它的 Password 不可绑定（WinUI 刻意如此，批次 UI/P-146），
            // 所以"档里的值→界面"只能在这里手写一次。写在 _suppressSave 里是必须的——
            // 赋 Password 会当场触发 PasswordChanged，而那个处理器要往 ViewModel 里推值；
            // 不在抑制区内就会被去抖保存当成"用户改了"，于是每次打开设置页都整档重写一次凭据档。
            GithubTokenBox.Password = ViewModel.GithubToken;
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

    /// <summary>界面那一头的 Token → ViewModel（批次 UI/P-146）。<b>这是"读回"的唯一下方</b>：
    /// PasswordBox 不能双向绑定，所以这条链靠这一对赋值撑起来，两头各一处、不许分散到别处去读
    /// （分散读就会出现"改了但没进保存"的漏，与 <c>ReadAiSettings</c> 那条同一口径）。
    /// 值本身不落日志、不进诊断档——那一族的账在 P-30。</summary>
    private void GithubTokenBox_Changed(object sender, RoutedEventArgs e)
        => ViewModel.GithubToken = GithubTokenBox.Password;

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

    private WidgetManager? WidgetManager()
        => App.Services.GetRequiredService<WidgetManager>();

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (App.Services.GetRequiredService<HotkeyService>() is not { } svc) return;
        // 本页可能被反复导航回来：先退再订，避免同一个处理器挂多份（一份事件 → N 次重建 UI）。
        svc.RegistrationStateFlushed -= OnRegistrationStateFlushed;
        svc.RegistrationStateFlushed += OnRegistrationStateFlushed;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        if (App.Services.GetRequiredService<HotkeyService>() is { } svc)
            svc.RegistrationStateFlushed -= OnRegistrationStateFlushed;
    }

    /// <summary>
    /// 代码构建的行取画笔必须按<b>本页的实际主题</b>，不能传 <c>ElementTheme.Default</c>——
    /// Default 会把深浅判定交给 <c>IsSystemDark()</c>（OS 实时主题），于是「OS 深色 + 应用强制浅色」时
    /// 组件名/分组名解析到深色桶的近白画笔，浅底白字（审查报告 F3）。
    /// </summary>
    private Brush Brush(string key, Windows.UI.Color fallback) =>
        ThemeBrush.For(ActualTheme, key) ?? new SolidColorBrush(fallback);
}

/// <summary>
/// 只读键位一览的一行（设置页「屏幕画布」卡用它，画布上那块 ⌨ 面板是同一张表的另一副面孔）。
/// <para>刻意<b>不</b>复用 <see cref="HotkeyRow"/>：那一行带录制按钮与冲突标注，把可编辑的控件塞进
/// "这里显示的就是那边当前绑的键"那块只读卡，就等于在同一页摆出两个编辑入口（设置闸门会当场红）。</para>
/// </summary>
/// <param name="Label">动作名（取自 <c>HotkeyActions.DisplayName</c>，界面不自己写中文）。</param>
/// <param name="Gesture">键位文本（取自 <c>CanvasService.BindingText</c>，读的是真实绑定）。</param>
public sealed record CanvasHotkeyRow(string Label, string Gesture);

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
