#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        ViewModel.AlarmReached += OnAlarmReached;
        Unloaded += (_, _) => Stop();
        Loaded += (_, _) => ApplyAdaptiveFontSize();
    }

    /// <summary>
    /// 到点那一发的出口：右下角提示卡。<b>卡片是"进程正好在跑"时的那一下，桌面上那行"待确认"是常态可见的那一份</b>
    /// ——两条缺一条就会变成"提醒没到"，所以同时做（与倒计时/番茄钟同口径）。
    /// 卡片贴不上屏幕时 TrayReporter 会自己留下日志，不会静默吞掉。
    /// </summary>
    private static void OnAlarmReached(AlarmItem item)
        => TrayReporter.Report("闹钟", AlarmPolicy.LineOf(item), "到点了。在时钟组件右键「闹钟」里可以停止或改。");

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
}
