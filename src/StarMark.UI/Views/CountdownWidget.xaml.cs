#nullable enable
using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

using StarMark.Abstractions.I18n;

namespace StarMark.UI.Views;

/// <summary>
/// 倒计时 / 纪念日组件：多条命名倒计时，每年重复项按年推进，到点发一次右下角提示卡。
/// <para>
/// 算术与提醒判定全在 <see cref="CountdownPolicy"/>；定时器走 <see cref="IWidgetTicker"/>
/// （隐藏即停表）。提示卡是"进程正好在跑"时的那一下，桌面上的到点高亮是常态可见的那一份——
/// 两条都缺一条就会变成"提醒没到"，所以两者同时做。
/// </para>
/// </summary>
public sealed partial class CountdownWidget : UserControl, IWidgetTicker
{
    public CountdownViewModel ViewModel { get; }

    private readonly WidgetManager _manager;
    private readonly string _instanceId;
    private DispatcherQueueTimer? _timer;

    public CountdownWidget(WidgetInstanceConfig config, WidgetManager manager)
    {
        ViewModel = new CountdownViewModel();
        _manager = manager;
        _instanceId = config.Id;
        InitializeComponent();
        ViewModel.Load(config.Countdowns);
        ViewModel.OccurrenceReached += OnOccurrenceReached;
        ViewModel.Tick(DateTimeOffset.Now);
        // 换语言那一拍⛔ 在组件自己的节拍里：那一秒一秒重算的是"还剩多久"，而"语言换了"这件事它自己不知道
        // ⇒ 与 ✅WI-53 那颗天气组件同一条路：订阅一次、离屏成对摘掉（坑表 #386 第三次落到长命宿主上）。
        Loaded += (_, _) => Locale.Changed += RebuildWords;
        Unloaded += (_, _) => Locale.Changed -= RebuildWords;
    }

    /// <summary>
    /// 换语言时重贴这一扇宿主：只把<b>本来每秒就在算的那几行</b>再算一遍（主行／次行／每行的名字／空态提示／错误句）。
    /// <para>⛔ 在这里读设置档、⛔ 在这里重建集合、⛔ 在这里发提醒——那些都不属于"重贴"这一件事。</para>
    /// </summary>
    private void RebuildWords() => ViewModel.RebuildWords(DateTimeOffset.Now);

    private void OnOccurrenceReached(CountdownItem item)
    {
        // 正文念的是"这一行现在该叫什么"（用户没起名字时那是按当前语言现算的派生名）：
        // ⛔ 直接把 <c>item.Title</c> 递出去——批次 WI-54 之后空白标题是合法的"用派生名"，那样卡片会只剩一个抬头。
        var delivered = NoticeCard.Show(LocMessage.Of("widget.countdown.dueTitle"),
            LocMessage.Of("common.rawValue", CountdownPolicy.TitleText(item)));
        if (!delivered)
        {
            // 卡片贴不上屏幕（不在 UI 线程、拿不到显示器）⇒ 记一条日志说明"到点过、只是没能弹"，
            // 否则用户事后回看只会得到"它根本没提醒我"。界面上的到点高亮仍在。
            StarLog.Info($"倒计时到点（提示卡没能贴上屏幕，仅组件内高亮）：{CountdownPolicy.TitleText(item)}");
        }
    }

    private void BeginAdd_Click(object sender, RoutedEventArgs e) => ViewModel.BeginAdd();

    private void CancelAdd_Click(object sender, RoutedEventArgs e) => ViewModel.CancelAdd();

    private async void SaveNew_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CommitAdd(TimeZoneInfo.Local.Id, DateTimeOffset.Now)) return;   // 原因已在 AddError 里
        await PersistAsync();
    }

    private async void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CountdownRow row }) return;
        if (ViewModel.Remove(row) is null) return;
        await PersistAsync();
    }

    private System.Threading.Tasks.Task PersistAsync()
        => _manager.SaveCountdownsAsync(_instanceId, ViewModel.ToPersisted());

    public void UpdateRunning(bool windowVisible)
    {
        if (windowVisible)
        {
            _timer ??= DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick -= Tick;
            _timer.Tick += Tick;
            _timer.Start();
            // 立即校准一次显示（不等落盘）：万一这一拍正好触发提醒，下一秒的 Tick 会把键写下去
            ViewModel.Tick(DateTimeOffset.Now);
        }
        else
        {
            _timer?.Stop();
        }
    }

    public void Stop() => _timer?.Stop();

    private async void Tick(DispatcherQueueTimer sender, object args)
    {
        // 有项目刚跨过本轮时刻时提醒键已写进条目，必须落盘，否则重启后会再弹一次
        if (ViewModel.Tick(DateTimeOffset.Now) > 0) await PersistAsync();
    }
}
