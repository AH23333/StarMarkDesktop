#nullable enable
using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 倒计时 / 纪念日组件：多条命名倒计时，每年重复项按年推进，到点发一次托盘气泡。
/// <para>
/// 算术与提醒判定全在 <see cref="CountdownPolicy"/>；定时器走 <see cref="IWidgetTicker"/>
/// （隐藏即停表）。气泡是"进程正好在跑"时的那一下，桌面上的到点高亮是常态可见的那一份——
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
    }

    private void OnOccurrenceReached(CountdownItem item)
    {
        var delivered = App.MainWindow?.TryShowTrayNotification("倒计时到点", item.Title) ?? false;
        if (!delivered)
        {
            // 托盘没开时气泡发不出去 ⇒ 记一条日志说明"到点过、只是没能弹"，
            // 否则用户事后回看只会得到"它根本没提醒我"。界面上的到点高亮仍在。
            StarLog.Info($"倒计时到点（托盘未启用，仅组件内高亮）：{item.Title}");
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
