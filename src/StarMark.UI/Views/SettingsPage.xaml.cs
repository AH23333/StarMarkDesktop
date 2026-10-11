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
using StarMark.Abstractions.I18n;
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
    private LocMessage? _recordingPrevMessage;   // 进入录制前那一格"还没取词的那一句"（取消录制时原样交回去）
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
        InitLanguageSection();   // 「界面语言」那一格：选项按 AppLanguages.Pickable 现场生成，见 SettingsPage.I18n.cs
        _backup = App.Services.GetRequiredService<BackupService>();
        _hkHook.KeyDown += OnHookKeyDown;   // 底层键盘钩子录制组合键
        _hkHook.KeyUp += OnHookKeyUp;       // 修饰键抬起时回显同步
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;  // 实时生效：属性变更去抖落盘
        LoadFromStoreSilently();
        ApplyEyeRestMessages();   // 回灌出来的那两句也要当场贴上去（换语言重贴走同一条门，见下面那颗方法）
        ApplyExtensionStatuses(); // 「拓展功能」那一屏的七行同理：VM 交的是身份，贴上屏幕才取词
        ApplyDataStatuses();      // 「数据」那一屏的五行同理（批次 WI-61c）
        // 开机自启开关初始态以注册表为准（SetEnabled 幂等，程序化置位不会误写）。
        // 它归在「常规 → 托盘与呼出」，所以由页面构造时读一次，不再挂在组件列表的构建里。
        AutostartToggle.IsOn = AutostartService().IsEnabled();
        _ = ViewModel.LoadHealthAsync();
        BuildWidgetRows();
        BuildHotkeyRows();
        BuildLayoutRows();
        BuildGuideTab();      // 「使用说明」那一栏：正文在 Core 的 PageGuides，这里只是宿主
    }

    /// <summary>
    /// 按<b>页签身份</b>（<see cref="SettingsTabIds"/>）选中一页，<b>不按屏幕上那几个字</b>。
    /// <para>批次 WI-2 起标题进了语言表：中文档叫「健康与诊断」、英文档叫 "Health &amp; diagnostics"，
    /// 而调用方编译期就写死了。用标题当地址，切语言的那一刻"完整设置"就会安静地落在默认页——
    /// 不报错、不出声，用户到了门口自己找房间（这正是当初做"跳到那一栏"要消灭的样子）。</para>
    /// <para>找不到就留在当前页并记一条日志：漏写 <c>Tag</c> 与删掉一栏是这条路径仅有的失效方式，
    /// 两种都该留下话。</para>
    /// </summary>
    public void SelectTab(string? tabId)
    {
        if (string.IsNullOrEmpty(tabId)) return;
        foreach (var item in SettingsTabs.TabItems)
        {
            if (item is not TabViewItem tab) continue;
            if (!string.Equals(tab.Tag as string, tabId, StringComparison.Ordinal)) continue;
            SettingsTabs.SelectedItem = tab;
            return;
        }
        StarMark.Abstractions.StarLog.Warn($"[设置页] 没有身份为「{tabId}」的页签（那一栏的 Tag 漏写了，还是整栏被删了？）");
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
        // 护眼那两句先推再判保存：它们是"VM 算出来的话"，交回的是「键＋读数」，
        // 过一次 LocText 那颗门才会跟着语言走（⛔ 不绑成品串，也不参与自动保存）。
        if (e.PropertyName is nameof(SettingsPageViewModel.EyeRestStatus)
                         or nameof(SettingsPageViewModel.EyeRestPreviewStatus))
            ApplyEyeRestMessages();
        // 拓展功能那一屏的七行：一颗变了就把**七行一起重贴**。分开贴会留下"这一行刚换语言、
        // 隔壁那行还停在上一门"那种自相矛盾（与上面护眼那两句同一条理由，批次 WI-37-B）。
        if (e.PropertyName is nameof(SettingsPageViewModel.TrendingStatus)
                         or nameof(SettingsPageViewModel.RssStatus)
                         or nameof(SettingsPageViewModel.CanvasStatus)
                         or nameof(SettingsPageViewModel.CaptureStatus)
                         or nameof(SettingsPageViewModel.LocalDiskSearchStatus)
                         or nameof(SettingsPageViewModel.MissingFileRowsStatus))
            ApplyExtensionStatuses();
        // 「数据」那一屏的六行同理：一颗变了就把**六行一起重贴**（第一批＝批次 WI-61c 那五行，第六行＝WI-62 的备份回执）。
        // 这一屏互相解释的话比拓展功能那一屏更明显——「自动备份」那行说"多久一份、能回到几天前"，
        // 下面那行标题说"列出了几份"，分开贴就会出现同一张卡片里两行两种语言。
        if (e.PropertyName is nameof(SettingsPageViewModel.AutoBackupStatus)
                         or nameof(SettingsPageViewModel.BackupsHeader)
                         or nameof(SettingsPageViewModel.BackupStatus)
                         or nameof(SettingsPageViewModel.ClipboardHistoryStatus)
                         or nameof(SettingsPageViewModel.ClipboardCleanupStatus)
                         or nameof(SettingsPageViewModel.ClipboardImageStatus)
                         or nameof(SettingsPageViewModel.ClipboardUsage)
                         or nameof(SettingsPageViewModel.SaveErrorMessage))
            ApplyDataStatuses();
        if (_suppressSave || string.IsNullOrEmpty(e.PropertyName)) return;
        // 健康度/备份/诊断等异步展示态不参与保存，避免加载过程中反复写盘
        if (IsDisplayOnlyProperty(e.PropertyName)) return;
        ScheduleAutoSave();
    }

    /// <summary>
    /// 把护眼那一卡片的两句状态交进 <see cref="LocText.SetMessage"/> 那一颗门（批次 WI-37-B）。
    /// <para>两句一起推、且不问"是哪一句变了"：这两行同属一张卡片，分开推就会有一行停在上一版语言上——
    /// 而"幕布上写着点一下可结束、下面那行写着强制不可跳"正是这一族最容易被看见的自相矛盾。</para>
    /// </summary>
    private void ApplyEyeRestMessages()
    {
        LocText.SetMessage(EyeRestStatusText, ViewModel.EyeRestStatus);
        LocText.SetMessage(EyeRestPreviewText, ViewModel.EyeRestPreviewStatus);
    }

    /// <summary>
    /// 「拓展功能」那一屏的七行状态贴到屏幕上（批次 WI-61b，P-188 第三刀·第一片）。
    /// <para>VM 交回的是<b>没取词的</b>「键＋实参」，所以这一颗方法是它到屏幕上的唯一出口：
    /// 键位（F1／Ctrl+…）、条数、字节读数都在实参里，取词发生在贴上去这一刻，
    /// 换语言那一刻由 <see cref="LocText"/> 那份名单重贴（本页⛔ 再挂第二份订阅）。</para>
    /// <para><b>七行一起推</b>：它们是同一屏互相解释的话——「热榜」那行说"导航栏有这一项"，
    /// 旁边的开关行说"已开启"，分开推就会出现一行英文一行中文那种当场可见的自相矛盾。</para>
    /// </summary>
    private void ApplyExtensionStatuses()
    {
        LocText.SetMessage(TrendingStatusText, ViewModel.TrendingStatus);
        LocText.SetMessage(RssSwitchStatusText, ViewModel.RssStatus);
        LocText.SetMessage(CanvasStatusText, ViewModel.CanvasStatus);
        LocText.SetMessage(CaptureStatusText, ViewModel.CaptureStatus);
        LocText.SetMessage(LocalDiskSearchStatusText, ViewModel.LocalDiskSearchStatus);
        LocText.SetMessage(MissingFileRowsStatusText, ViewModel.MissingFileRowsStatus);
    }

    /// <summary>
    /// 设置页「数据」那一屏那<b>八行</b>状态贴到屏幕上（第一批＝WI-61c 那五行；第六行＝WI-62 的备份回执；
    /// 第七、八行＝✅WI-64 的「图片占用」与「图片采集状态」——它们的话的一半住在 <c>ClipAssets</c>／<c>ClipboardPolicy</c>，
    /// 而「图片占用」那一行自己就是四段拼出来的，⛔ 只贴一段＝同一行两种语言）。
    /// <para>与 ✅WI-61b 那颗 <see cref="ApplyExtensionStatuses"/> 同一机制、同一条理由：VM 交回的是
    /// <b>没取词的</b>「键＋实参」，件数／小时数／回溯天数／腾出多少字节／异常消息全在实参里。</para>
    /// <para><b>第六行「备份与恢复」为什么等到这一批</b>：它那半句由 Core 供（<c>DeleteBackup</c>／
    /// <c>RestoreResult.Message</c>／<c>BackupPathPolicy</c>／<c>BackupFormatException</c>），只交 UI 那一半就是
    /// 同一行半中半英——那两半在 WI-62 一起进表（✅WI-61c 登记的就是这一条前置）。
    /// ⚠ 这一屏只剩<b>一行</b>同形状：<c>UpdateStatus</c>（话的一半在 <c>UpdatePolicy.Describe*</c>＝P-194），
    /// 那两行（<c>ClipAssets.DescribeUsage</c>／<c>ClipboardPolicy.DescribeImageIntake</c>＝P-193 的 Abstractions 半族）
    /// 已在 ✅WI-64 两层同批走完——它们从前是 <c>{x:Bind}</c> 绑<b>成品串</b>，所以"换语言即重贴"对它们从不发生。</para>
    /// </summary>
    private void ApplyDataStatuses()
    {
        LocText.SetMessage(AutoBackupStatusText, ViewModel.AutoBackupStatus);
        LocText.SetMessage(BackupsHeaderText, ViewModel.BackupsHeader);
        LocText.SetMessage(BackupStatusText, ViewModel.BackupStatus);
        LocText.SetMessage(ClipboardHistoryStatusText, ViewModel.ClipboardHistoryStatus);
        LocText.SetMessage(ClipboardCleanupStatusText, ViewModel.ClipboardCleanupStatus);
        LocText.SetMessage(ClipboardImageStatusText, ViewModel.ClipboardImageStatus);
        LocText.SetMessage(ClipboardUsageText, ViewModel.ClipboardUsage);
        LocText.SetMessage(SaveErrorMessageText, ViewModel.SaveErrorMessage);
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
        // 诊断那一行念的是"进页时采回来的事实"（键＋实参），而换语言⛔ 重新采集——那一趟里有 COUNT(*) 全表扫描。
        // 所以本页自己订阅重贴，并在 Unloaded 成对摘掉（坑表 #386）：这颗 VM 是 AddTransient，
        // 挂着不摘就是每进一次设置页多一份订阅，而那些页面早就不在了。
        Locale.Changed -= ViewModel.PaintDiagnostics;
        Locale.Changed += ViewModel.PaintDiagnostics;
        // 「快捷键」那一栏每一行的四句话都住在 DataTemplate 里：模板上的挂键只能带一颗固定键，带不了"这一行自己的那颗"，
        // 所以换语言那一刻必须由这里把四颗显示属性重念一次（P-175 缺口②）。同一颗订阅、同一个成对块——⛔ 为这件事再开第十位主人。
        Locale.Changed -= RepasteHotkeyRows;
        Locale.Changed += RepasteHotkeyRows;
        // 同一栏里那两行「AI 整理预览」的组行也是「数据项＋模板」：换语言那一刻⛔ 有人回去重念就停在上一门（P-175 缺口②·第二位宿主）。
        // 排在下面那颗早退之前——那一颗是热键服务解析失败时的出口，⛔ 让 AI 这一栏的重贴跟着它一起被跳过。
        Locale.Changed -= RepasteAiGroupRows;
        Locale.Changed += RepasteAiGroupRows;
        if (App.Services.GetRequiredService<HotkeyService>() is not { } svc) return;
        // 本页可能被反复导航回来：先退再订，避免同一个处理器挂多份（一份事件 → N 次重建 UI）。
        svc.RegistrationStateFlushed -= OnRegistrationStateFlushed;
        svc.RegistrationStateFlushed += OnRegistrationStateFlushed;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        Locale.Changed -= ViewModel.PaintDiagnostics;   // 与 Loaded 那颗成对
        Locale.Changed -= RepasteHotkeyRows;            // 同上：那一栏的重贴手臂也一起摘
        Locale.Changed -= RepasteAiGroupRows;           // 同上：AI 预览那两行同样⛔ 留下挂着的订阅
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

/// <summary>
/// 快捷键录制列表的单行模型（动作名 + 当前绑定显示 + 录制态 + 冲突标注）。
/// <para><b>批次 WI-40-3b：四颗槽存的是「键＋实参」（<see cref="LocMessage"/>），屏幕上那句由显示侧 getter 现算。</b>
/// 这一行住在 <c>DataTemplate</c> 里，而模板上的 <c>i18n:LocText.Key</c> 只能带一颗<b>固定</b>键、带不了
/// "这一行自己的那颗键"——旧写法把成品串存进字段，换语言那一刻<b>一个字都不动</b>，要重进这一栏才更新（P-175 缺口②）。
/// 现在由设置页那一拍（挂在它<b>已有</b>的那颗 <c>Locale.Changed</c> 订阅旁、与摘掉成对）把四颗显示属性重念一遍，
/// 取词时刻回到"语言改变的那一刻"。</para>
/// <para>⚠ 显示侧那四颗 getter 只服务首帧与重念，⛔ 有人拿它们当真值判据（坑表 #354）：
/// 绑没绑读 <see cref="Bound"/>，有没有冲突／有没有占用读 <see cref="HasConflict"/>／<see cref="HasRegisterError"/>，
/// 后两颗问的是"这句有没有要说"，⛔ 去比句子里的字。</para>
/// </summary>
public sealed class HotkeyRow : INotifyPropertyChanged
{
    public string Action { get; set; } = string.Empty;

    private LocMessage? _actionName;

    /// <summary>动作名交回 Core 那颗 <c>HotkeyActions.DisplayMessage</c>：布局名与组件名是<b>实参</b>，不是第二句语言。</summary>
    public LocMessage? ActionNameMessage
    {
        get => _actionName;
        set => Set(ref _actionName, value, nameof(ActionName));
    }

    public string ActionName => _actionName?.Render() ?? string.Empty;

    private bool _bound;

    /// <summary>
    /// 这一行到底绑没绑——分类标题上那个"N 项已绑定"读的是它。
    /// <para>⚠ 它⛔ 由 <see cref="BindingText"/> 反推：那颗是给人看的那一句（「未设置」在表里，换语言会跟着换），
    /// 拿它当判据＝用某一档语言的词做真值比较，换语言那一刻每组的数都会归零（坑表 #354）。</para>
    /// </summary>
    public bool Bound
    {
        get => _bound;
        set { if (_bound != value) { _bound = value; OnChanged(); } }
    }

    private LocMessage? _binding;

    /// <summary>
    /// 那颗按钮上显示的东西。<b>三态而⛔ 两态</b>：已绑＝键位（<b>数据</b>，走 <c>common.rawValue</c> 那道门），
    /// 没绑＝表里那句 <c>hotkey.unset</c>，正在录且一个键都没按＝<c>null</c>（这一格就是要空着）。
    /// 把"没绑"与"此刻没话要说"并成一颗 <code>null</code> 就等于让一句表里的话失去出处。
    /// </summary>
    public LocMessage? BindingMessage
    {
        get => _binding;
        set => Set(ref _binding, value, nameof(BindingText));
    }

    public string BindingText => _binding?.Render() ?? string.Empty;

    private LocMessage? _conflict;

    /// <summary>与别的动作共用同一组合（<c>null</c>＝没共用）。</summary>
    public LocMessage? ConflictMessage
    {
        get => _conflict;
        set => Set(ref _conflict, value, nameof(ConflictText), nameof(HasConflict));
    }

    public string ConflictText => _conflict?.Render() ?? string.Empty;

    /// <summary>是否与其它动作共用同一组合（仅提示，允许保留）。读的是"这句有没有要说"，⛔ 读句子。</summary>
    public bool HasConflict => _conflict is not null;

    private LocMessage? _registerError;

    /// <summary>该组合未能注册时的原因（<c>null</c>＝已注册或不适用／未绑定）。原因由 Core 交键，界面这一拍才取词。</summary>
    public LocMessage? RegisterErrorMessage
    {
        get => _registerError;
        set => Set(ref _registerError, value, nameof(RegisterErrorText), nameof(HasRegisterError));
    }

    public string RegisterErrorText => _registerError?.Render() ?? string.Empty;

    public bool HasRegisterError => _registerError is not null;

    private bool _isRecording;
    public bool IsRecording
    {
        get => _isRecording;
        set { if (_isRecording != value) { _isRecording = value; OnChanged(); } }
    }

    /// <summary>
    /// 换语言那一拍：四颗"给人看的那一句"各重念一次。
    /// <para>数据一个字都没改，改的是<b>取词用的那一版语言</b>——所以这里只发通知，⛔ 去重算任何句子。</para>
    /// </summary>
    public void Repaste()
    {
        OnChanged(nameof(ActionName));
        OnChanged(nameof(BindingText));
        OnChanged(nameof(ConflictText));
        OnChanged(nameof(RegisterErrorText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    /// <summary>存"还没取词的那一句"，并把它在屏幕上对应的那格（以及可见性那颗）一起通知出去。</summary>
    private void Set(ref LocMessage? slot, LocMessage? value, string display, string? flag = null)
    {
        if (slot == value) return;
        slot = value;
        OnChanged(display);
        if (flag is not null) OnChanged(flag);
    }
}

/// <summary>布局方案列表的单行模型（名称 + 摘要 + 应用/删除）。</summary>
public sealed class WidgetLayoutRow
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}
