#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 时钟组件：XAML + ViewModel，每秒那张表由本组件持有（宿主通过 <see cref="IWidgetTicker"/> 启停，
/// 隐藏即停表，常驻应用省电）。
/// <para>
/// 批次 RU 之后它还<b>带着这个实例的闹钟</b>：判据全在 <see cref="AlarmPolicy"/>，
/// 这里只做三件事——每秒问一次"该弹吗"、把"待确认"那一行说出来、把改动交给 <see cref="WidgetManager"/> 落盘。
/// 右键菜单是<b>第二个入口，不是第二个引擎</b>（它只通过 <see cref="IAlarmEditor"/> 问与改）。
/// </para>
/// </summary>
public sealed partial class ClockWidget : UserControl, IWidgetTicker, IAlarmEditor
{
    public ClockWidgetViewModel ViewModel { get; }

    private readonly WidgetManager _manager;
    private readonly string _instanceId;

    /// <summary>组件级文本缩放系数（由宿主 WidgetWindow 的「文本缩放」套用到此，
    /// 与自适应字号相乘：时间/日期字号 = 自适应基准 × 此系数。这样时钟时间也能随外观设置放大/缩小，
    /// 且「恢复全局」时系数回到 1 即还原（避免被 ApplyAdaptiveFontSize 覆盖后缩放失效）。</summary>
    public double TextScale
    {
        get => _textScale;
        set
        {
            if (Math.Abs(_textScale - value) < 1e-6) return;
            _textScale = value;
            ApplyAdaptiveFontSize();
            RebuildAlarmFace();          // 那一排不吃自适应高度，但仍旧跟着组件级文本缩放走
        }
    }

    private double _textScale = 1.0;
    private DispatcherQueueTimer? _timer;

    public ClockWidget(WidgetInstanceConfig config, WidgetManager manager)
    {
        ViewModel = new ClockWidgetViewModel();
        _manager = manager;
        _instanceId = config.Id;
        InitializeComponent();
        ViewModel.Load(config.Alarms);
        // 表面那一排只在内容真的变了时重画（签名判据在 Core）：<see cref="RebuildAlarmFace"/> 里是销毁重建。
        ViewModel.AlarmFaceChanged += RebuildAlarmFace;
        RebuildAlarmFace();
        ViewModel.AlarmReached += OnAlarmReached;
        Unloaded += (_, _) => { Stop(); ViewModel.AlarmFaceChanged -= RebuildAlarmFace; };
        Loaded += (_, _) => ApplyAdaptiveFontSize();
    }

    /// <summary>
    /// 到点那一发的出口：右下角提示卡。<b>卡片是"进程正好在跑"时的那一下，桌面上那行"待确认"是常态可见的那一份</b>
    /// ——两条缺一条就会变成"提醒没到"，所以同时做（与倒计时/番茄钟同口径）。
    /// 卡片贴不上屏幕时 TrayReporter 会自己留下日志，不会静默吞掉。
    /// <para>批次 VO 之后"去哪儿改"多了第一条路：桌面上那一排直接能停、能改，右键只是第二个入口——
    /// 这句话要跟着改，否则卡片把人支去一个更麻烦的地方（P-54 那一族：出口要指到最近的那一个）。</para>
    /// </summary>
    private static void OnAlarmReached(AlarmItem item)
        => TrayReporter.Report("闹钟", AlarmPolicy.LineOf(item), "到点了。点时钟上那一排的「停止待确认」就收，右键「闹钟」里也能改。");

    /// <summary>
    /// 时钟现在可以缩放：时间字号按窗口尺寸自适应（照搬 DeskBox Glance 的做法），
    /// 拉大窗口时间就大，缩小时自动收小，不会溢出行或缩成一团。
    /// </summary>
    private void ClockBody_SizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyAdaptiveFontSize();

    private void ApplyAdaptiveFontSize()
    {
        if (ClockBody is null || TimeBlock is null || DateBlock is null || RestBlock is null || AlarmBlock is null) return;
        var w = ClockBody.ActualWidth;
        var h = ClockBody.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // min(宽*0.19, 高*0.34) 再夹到 [22, 72]：宽窗口不至于字太小，高窗口不至于溢出。
        // 再乘组件级文本缩放系数（来自外观「文本缩放」），让时钟时间也能随组件外观放大/缩小。
        var size = Math.Clamp(Math.Min(w * 0.19, h * 0.34), 22, 72) * _textScale;
        TimeBlock.FontSize = Math.Round(size);
        // 日期行同样要跟着系数走：早先漏乘 _textScale，于是「放大文字」后时间变了日期却不动。
        // 上下限按倍率同步缩放，否则系数 1.8 时会被夹回 22，看起来像没生效。
        DateBlock.FontSize = Math.Clamp(Math.Round(size * 0.34), 10 * _textScale, 22 * _textScale);
        // 护眼与闹钟两行与日期行同大小：它们是"顺手看一眼"的信息，不该比日期更显眼。
        RestBlock.FontSize = DateBlock.FontSize;
        AlarmBlock.FontSize = DateBlock.FontSize;
    }

    /// <summary>按宿主窗口是否可见启停每秒刷新；启动时立即校准一次显示（包括弹掉正好过期那一发）。</summary>
    public async void UpdateRunning(bool windowVisible)
    {
        if (windowVisible)
        {
            _timer ??= DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick -= Timer_Tick;
            _timer.Tick += Timer_Tick;
            _timer.Start();
            await PersistIfNotifiedAsync();
        }
        else
        {
            _timer?.Stop();
        }
    }

    /// <summary>停止刷新（窗口关闭 / 组件移除时由宿主调用）。</summary>
    public void Stop() => _timer?.Stop();

    private async void Timer_Tick(DispatcherQueueTimer sender, object args) => await PersistIfNotifiedAsync();

    /// <summary>
    /// 弹掉任何一发都要落盘：<b>否则重启后同一轮会再弹一次</b>（到点标记存在条目里，只在存档里才作数）。
    /// 没弹东西时一次磁盘都不碰——这一句每秒都跑。<see cref="ClockWidgetViewModel.Update"/> 里要写界面属性，
    /// 所以它<b>必须留在 UI 线程</b>（不能丢进后台线程"图快"）。
    /// </summary>
    private async System.Threading.Tasks.Task PersistIfNotifiedAsync()
    {
        if (ViewModel.Update() > 0) await PersistAsync();
    }

    private System.Threading.Tasks.Task PersistAsync() => _manager.SaveAlarmsAsync(_instanceId, ViewModel.ToPersisted());

    // ────────── IAlarmEditor：右键菜单那一条链 ──────────

    public IReadOnlyList<AlarmItem> Alarms => ViewModel.Alarms;

    public bool IsFull => ViewModel.IsFull;

    /// <summary>
    /// 改动之后立刻刷新那一行并落盘。<b>菜单不等"下一秒"</b>：用户关掉菜单就该看见结果。
    /// 落盘失败要说出来（<c>WidgetStorage.Save</c> 那边已经有一道上报，这里再兜住"整条链抛了"——
    /// 静默失败的闹钟比不响更糟：他以为设上了）。
    /// </summary>
    private async void Apply(Action edit)
    {
        edit();
        try { await PersistAsync(); }
        catch (Exception ex) { StarLog.Error("闹钟改动没能落盘（改动已在内存中生效，重启后会丢）", ex); }
    }

    public void Add(int minute, string label, AlarmDays days) => Apply(() => ViewModel.Add(minute, label, days));

    public void SetEnabled(long id, bool enabled) => Apply(() => ViewModel.SetEnabled(id, enabled));

    /// <summary>
    /// 表面那颗点走这条：把 id 交回去、由 ViewModel 读条目本身再翻，<b>不拿界面上的快照回写</b>
    /// （同一条目有两个入口能改它，快照回写的症状是"按下去没反应，再按一下才关"）。
    /// </summary>
    public void ToggleEnabled(long id) => Apply(() => ViewModel.ToggleEnabled(id));

    public void SetDays(long id, AlarmDays days) => Apply(() => ViewModel.SetDays(id, days));

    public void SetTime(long id, int minute, string label) => Apply(() => ViewModel.SetTime(id, minute, label));

    public void Remove(long id) => Apply(() => ViewModel.Remove(id));

    public void ConfirmPending() => Apply(ViewModel.ConfirmPendingAlarms);

    /// <summary>试响：不改状态，所以不落盘（见 <see cref="ClockWidgetViewModel.TestFire"/>）。</summary>
    public void TestFire(long id) => ViewModel.TestFire(id);

    public int PendingCount => ViewModel.Alarms.Count(item => AlarmPolicy.IsPending(item, DateTimeOffset.Now));

    /// <summary>下一次该响的时刻（只看到着的条目；判据现读 Core，不在这儿另算一套）。</summary>
    public DateTimeOffset? NextDueAt
    {
        get
        {
            var now = DateTimeOffset.Now;
            DateTimeOffset? earliest = null;
            foreach (var item in ViewModel.Alarms.Where(each => each.Enabled))
            {
                if (AlarmPolicy.NextOccurrence(item, now) is not { } at) continue;
                if (earliest is not { } head || at < head) earliest = at;
            }
            return earliest;
        }
    }

    // ────────── 时钟表面那一排：闹钟看得见的那一个入口（批次 VO）──────────

    /// <summary>
    /// 重画那一排。<b>只在 ViewModel 说"内容真的变了"时进来</b>（比较用的是 Core 的签名，
    /// 见 <see cref="AlarmPolicy.FaceSignature"/>）：时钟每秒刷一整张表，若按那一趟重画，
    /// 就是每秒销毁重建一遍他正盯着的那几个控件——"改一行字"被升级成"一次布局重排"。
    /// 上限 8 条，用不着 DataTemplate 与虚拟化；行 id 直接进被点那个元素的 <c>Tag</c>，
    /// 于是也不再有"从 flyout 反查这一行是谁"那一环（坑表 #247 就是那颗颜色点）。
    /// </summary>
    private void RebuildAlarmFace()
    {
        var size = AlarmRowFontSize();
        AlarmList.Children.Clear();
        foreach (var row in ViewModel.FaceRows) AlarmList.Children.Add(BuildAlarmRow(row, size));

        // 待确认时把出口挂在排尾：只写出"待确认"却把人支去右键菜单，那一发的观感就是"它挂在那里关不掉"
        // （P-54：出口要指到最近的那一个）。
        if (PendingCount > 0)
            AlarmList.Children.Add(BuildTextButton($"停止待确认（{PendingCount}）", size, ConfirmPending,
                "确认掉所有还在等的轮次（本程序运行时才提醒）"));

        // 加一条的入口常驻在这一排末尾，一条闹钟也没有时更要在那里：那时这是唯一的出口。
        if (ViewModel.IsFull)
            AlarmList.Children.Add(BuildTextButton($"闹钟最多 {AlarmPolicy.MaxItems} 条", size, null, "先删掉一条再加"));
        else
            AlarmList.Children.Add(BuildTextButton(
                ViewModel.FaceRows.Count == 0 ? "＋ 加一条闹钟" : "＋ 闹钟", size, AddAlarmFromFace,
                "加一条，例如「7:30 起床」（本程序运行时才提醒）"));
    }

    /// <summary>
    /// 一行闹钟：左边那颗点管开关，右边那行字点开是这一条的动作表。
    /// <b>整行用横向 StackPanel，不用 <c>*</c> 列</b>：外层 StackPanel 给子元素的是无限宽，
    /// <c>*</c> 列在无限宽里量成 0，于是文字整行不见——只有真机看得见的那种形状错。
    /// </summary>
    private FrameworkElement BuildAlarmRow(AlarmPolicy.AlarmFaceRow row, double size)
    {
        var toggle = new CheckBox
        {
            IsChecked = row.Enabled,
            Tag = row.Id,                       // 点回来只认这个 id（见 <see cref="TryAlarmId"/>）
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(2, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(toggle, row.Enabled ? "开着：点一下关掉这一条" : "关着：点一下开回来");
        toggle.Click += AlarmToggle_Click;

        var label = new TextBlock
        {
            Text = row.Pending ? row.Text + " · 待确认" : row.Text,
            FontSize = size,
            MaxWidth = size * 18,               // 约十八个字：上限跟着字号走，不跟窗口高度走（那会自咬成回环）
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = row.Enabled ? 1 : 0.5,    // 关掉的那条照旧在场，只是退到背景："关了"与"删了"是两件事
            VerticalAlignment = VerticalAlignment.Center,
        };
        var open = new Button
        {
            Content = label,
            Tag = row.Id,
            Background = FlatBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 0, 4, 0),
            MinWidth = 0,
            MinHeight = 0,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontSize = size,
        };
        ToolTipService.SetToolTip(open, row.Text + "｜改时间、重复、试响或删掉");
        open.Click += AlarmRow_Click;

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(toggle);
        line.Children.Add(open);
        return line;
    }

    /// <summary>排尾那几颗（停止待确认／加一条／已满）：只有一行字，不带底板。</summary>
    private Button BuildTextButton(string text, double size, Action? click, string toolTip)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, FontSize = size, Opacity = click is null ? 0.5 : 0.8 },
            Background = FlatBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 0, 4, 0),
            MinWidth = 0,
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        // 满了那颗按不下去，但要说得出为什么：一句"最多 8 条"写在脸上，比一个灰掉的加号有用。
        if (click is { } handler) button.Click += (_, _) => handler();
        else button.IsEnabled = false;
        ToolTipService.SetToolTip(button, toolTip);
        return button;
    }

    /// <summary>那一排的字号：只跟组件级文本缩放，<b>刻意不吃窗口自适应</b>——它若也按 body 高度算，
    /// "行变多 → body 变高 → 字号变小 → 行变矮"就是自己咬自己，抖动的观感只由他看见。</summary>
    private double AlarmRowFontSize() => Math.Clamp(Math.Round(11.5 * _textScale), 10, 18);

    /// <summary>透明底，<b>不是 null</b>：null 会露出按钮自带的主题底板，那一行就变成一小块卡片。</summary>
    private static readonly SolidColorBrush FlatBrush = new(Microsoft.UI.Colors.Transparent);

    /// <summary>那颗点：只把 id 交回去翻一下，当前状态由 ViewModel 读条目本身（见 <see cref="ToggleEnabled"/>）。</summary>
    private void AlarmToggle_Click(object sender, RoutedEventArgs e)
    {
        if (TryAlarmId(sender, out var id)) ToggleEnabled(id);
    }

    /// <summary>那一行字：点开的是这一条的动作表，与宿主右键菜单<b>同一份</b>（<see cref="AlarmMenu"/>）。</summary>
    private void AlarmRow_Click(object sender, RoutedEventArgs e)
    {
        if (!TryAlarmId(sender, out var id)) return;
        if (ViewModel.Alarms.FirstOrDefault(item => item.Id == id) is not { } item)
        {
            StarLog.Warn($"[时钟] 闹钟条目 {id} 已经不在了（多半是另一个入口刚删掉它），这一次点击什么都没做");
            return;
        }
        AlarmMenu.ShowRow(this, item, (FrameworkElement)sender, _manager.WindowOf(_instanceId));
    }

    private void AddAlarmFromFace() => _ = AlarmMenu.AddAsync(this, _manager.WindowOf(_instanceId));

    /// <summary>
    /// id 只从<b>被点那个元素</b>身上拿（建行时写进 <c>Tag</c>）。拿不到必须说出来：
    /// 静默 return 的症状是"我点了没反应，也不知道为什么"，而且日志里没有那一行，人就永远说不清是哪一次。
    /// </summary>
    private static bool TryAlarmId(object sender, out long id)
    {
        if (sender is FrameworkElement { Tag: long v }) { id = v; return true; }
        StarLog.Warn("[时钟] 闹钟那一排的元素认不出自己的 id（Tag 不是 long），这次点击没有落库");
        id = 0;
        return false;
    }
}
