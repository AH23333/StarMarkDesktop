#nullable enable
using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 世界时钟组件：多城市并列秒级刷新。
/// <para>
/// 定时器走 <see cref="IWidgetTicker"/>：宿主在窗口显示/隐藏/关闭时启停，隐藏期间完全停表
/// （常驻应用省电；与踩坑 #11 的单实例定时器一致——只创建一次，反复启停而不新建）。
/// </para>
/// </summary>
public sealed partial class WorldClockWidget : UserControl, IWidgetTicker
{
    public WorldClockViewModel ViewModel { get; }

    private readonly WidgetManager _manager;
    private readonly string _instanceId;
    private DispatcherQueueTimer? _timer;
    private bool _ready;

    public WorldClockWidget(WidgetInstanceConfig config, WidgetManager manager)
    {
        ViewModel = new WorldClockViewModel();
        _manager = manager;
        _instanceId = config.Id;
        InitializeComponent();
        // 空态那句由 VM 交「键＋实参」，这里只负责贴（换语言的重贴归 LocText 那份名单，⛔ 再造一位主人）。
        ViewModel.PropertyChanged += OnEmptyHintChanged;
        // 那一行的原因写在行模板上（每行一颗）⇒ ⛔ 挂 LocText 名单，由组件在换语言那一刻逐行重念（✅WI-40-3b）。
        Locale.Changed += RepasteZoneRows;
        Unloaded += (_, _) => { ViewModel.PropertyChanged -= OnEmptyHintChanged; Locale.Changed -= RepasteZoneRows; };
        // 配置里存的是点位（含名字）；名字随区号一起存，避免每次刷新都去查系统显示名
        ViewModel.Load(config.WorldClockZones);
        ApplyEmptyHint();
        _ready = true;
    }

    private void OnEmptyHintChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorldClockViewModel.EmptyHint)) ApplyEmptyHint();
    }

    /// <summary>「没话了」也走这一颗门：SetMessage(… null) 把重贴名单那条一起摘掉，否则换语言把旧句子贴回来。</summary>
    private void ApplyEmptyHint() => LocText.SetMessage(EmptyHintText, ViewModel.EmptyHint);

    /// <summary>换语言那一刻：每个点位那一行「本机没有这个时区」各重念一次（实参还是那颗点位，⛔ 重新拼）。</summary>
    private void RepasteZoneRows() { foreach (var row in ViewModel.Rows) row.Repaste(); }

    private async void AddBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (AddBox.SelectedItem is not TimeZoneOption option) return;
        // 先复位再判断成败：否则同一个 Id 第二次选不中（SelectedIndex 没变就不触发），
        // 而且添加失败时下拉会一直挂着那个没成功的名字。
        var added = ViewModel.Add(option.ZoneId);
        AddBox.SelectedItem = null;
        if (!added) return;
        await PersistAsync();
    }

    private async void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WorldClockRow row }) return;
        ViewModel.Remove(row);
        await PersistAsync();
    }

    private System.Threading.Tasks.Task PersistAsync()
        => _manager.SaveWorldClockZonesAsync(_instanceId, ViewModel.ToPersisted());

    public void UpdateRunning(bool windowVisible)
    {
        if (windowVisible)
        {
            _timer ??= DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick -= Tick;
            _timer.Tick += Tick;
            _timer.Start();
            ViewModel.Tick();   // 立即校准一次：否则刚显示的表要先空一拍
        }
        else
        {
            _timer?.Stop();
        }
    }

    public void Stop() => _timer?.Stop();

    private void Tick(DispatcherQueueTimer sender, object args) => ViewModel.Tick();
}
