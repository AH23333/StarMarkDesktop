#nullable enable
using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// Phase C 第一个新内容组件：**今日速览（Glance）**。
/// 上半屏是「今天」——大号日号 + 年月星期 + 农历（含闰月）+ 今日节日 + 下一个节日倒计时；
/// 下半屏是「常看」——最近更新的若干条目，点击直接打开。
/// <para>
/// 与时钟的区别：时钟是纯时间显示，Glance 是「日期语义 + 推荐入口」的组合卡片，
/// 抄 DeskBox <c>GlanceWidgetContent</c> 的定位，但内容取自 StarMark 统一的 items 表
/// （DeskBox 是文件收纳，这里是书签/文件/GitHub Star 的统一条目）。
/// 农历与节日算法见 <see cref="GlanceCalendar"/>（照搬 DeskBox 的 GlanceFestivalService）。
/// </para>
/// </summary>
public sealed partial class GlanceWidget : UserControl
{
    private const int ItemLimit = 6;

    private readonly IItemRepository? _repo;
    private DispatcherQueueTimer? _timer;
    private DateOnly _shownDate;

    public GlanceWidget(IItemRepository? repo)
    {
        _repo = repo;
        InitializeComponent();

        Unloaded += (_, _) => _timer?.Stop();
        Loaded += (_, _) =>
        {
            RefreshDate();
            StartTimer();
            _ = LoadItemsAsync();
        };
    }

    /// <summary>刷新日期区（跨天时由定时器调用，也用于首次渲染）。</summary>
    private void RefreshDate()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        _shownDate = today;

        DayBlock.Text = today.Day.ToString();
        MonthBlock.Text = $"{today.Year}年{today.Month}月";
        WeekBlock.Text = WeekdayFull(today.DayOfWeek);

        var lunar = GlanceCalendar.LunarText(today);
        var festival = GlanceCalendar.Festival(today);
        LunarBlock.Text = string.IsNullOrEmpty(lunar)
            ? (festival ?? string.Empty)
            : festival is null ? $"农历{lunar}" : $"农历{lunar} · {festival}";

        // 今天已经是节日时，倒计时展示「下一个」节日，避免与上方重复。
        var next = GlanceCalendar.NextFestival(today.AddDays(1));
        CountdownBlock.Text = next is null
            ? string.Empty
            : $"距「{next.Name}」还有 {next.Days + 1} 天（{next.Date.Month}月{next.Date.Day}日）";
    }

    private void StartTimer()
    {
        _timer ??= DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(30);
        _timer.Tick -= Timer_Tick;
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        // 只在跨天时重算（农历/节日查询涉及 ChineseLunisolarCalendar，没必要每帧算）
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today != _shownDate) RefreshDate();
    }

    private async System.Threading.Tasks.Task LoadItemsAsync()
    {
        if (_repo is null)
        {
            ShowEmpty(true);
            return;
        }

        try
        {
            var items = await _repo.GetRecentAsync(ItemLimit, CancellationToken.None);
            ItemsHost.Children.Clear();
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Uri)) continue;
                ItemsHost.Children.Add(BuildItemButton(item));
            }
            ShowEmpty(ItemsHost.Children.Count == 0);
        }
        catch (Exception ex)
        {
            StarLog.Error("今日速览加载常看条目失败", ex);
            ShowEmpty(true);
        }
    }

    private static Button BuildItemButton(Item item)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrWhiteSpace(item.Subtitle))
        {
            panel.Children.Add(new TextBlock
            {
                Text = item.Subtitle,
                FontSize = 10,
                Opacity = 0.6,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var button = new Button
        {
            Content = panel,
            Tag = item.Uri,
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Click += Item_Click;
        return button;
    }

    private static async void Item_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string uri })
            await LauncherEx.OpenAsync(uri);
    }

    private void ShowEmpty(bool empty)
    {
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ItemsHost.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string WeekdayFull(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        DayOfWeek.Sunday => "星期日",
        _ => string.Empty,
    };
}
