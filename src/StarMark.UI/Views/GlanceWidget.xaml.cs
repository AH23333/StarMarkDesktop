#nullable enable
using System;
using System.Linq;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>热榜块在小组件里只放前几条（组件尺寸有限，多了挤掉「常看」）。</summary>
    private const int TrendingLimit = 5;

    private readonly IItemRepository? _repo;
    private DispatcherQueueTimer? _timer;
    private DateOnly _shownDate;

    /// <summary>
    /// 「常看」区的数据同步器：别处增删/改动条目后自动去抖重载。
    /// 日期区不靠它（那是纯日历计算），只有读库的那一半需要。
    /// </summary>
    private DataChangeReloader? _sync;

    public GlanceWidget(IItemRepository? repo)
    {
        _repo = repo;
        InitializeComponent();

        Unloaded += (_, _) =>
        {
            _timer?.Stop();
            _sync?.Dispose();
            _sync = null;
            // 静态事件是强引用：组件被移除后若不退订，整个控件（连同其下的行）会一直被钉住。
            Helpers.TrendingItemActions.NoticeRaised -= OnTrendingNotice;
        };
        Loaded += (_, _) =>
        {
            // Loaded 可能被多次触发（组件窗口反复显示），同步器只建一次，否则会重复订阅。
            _sync ??= new DataChangeReloader(ReloadAsync);
            // 动作结果先退再订：没有这一步，窗口每次隐藏/显示都会多挂一份同一个委托。
            Helpers.TrendingItemActions.NoticeRaised -= OnTrendingNotice;
            Helpers.TrendingItemActions.NoticeRaised += OnTrendingNotice;
            RefreshDate();
            StartTimer();
            _ = ReloadAsync();
        };
    }

    /// <summary>
    /// 热榜动作（Star / 收进收藏）的结果落到热榜块自己那一行。
    /// <para>组件上的右键菜单没有主窗那条状态行，而 ⭐Star 改的是远端、本机列表当下看不出变化 ⇒
    /// 不写这一行就是"点了没反应"（P-54）。通知可能来自别的窗口线程，统一切回本组件的 DispatcherQueue。</para>
    /// </summary>
    private void OnTrendingNotice(string message)
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (TrendingBlock.Visibility != Visibility.Visible) return;
            TrendingAction.Text = message;
            TrendingAction.Visibility = Visibility.Visible;
        });

    /// <summary>
    /// 一次重载两半：「常看」读本机库，热榜读缓存。放一起是因为点进热榜行的 🔖/置顶都会广播
    /// 数据变更 —— 收藏条目本身会改变「常看」的内容，而组件上的开关也可能刚被改过。
    /// </summary>
    private System.Threading.Tasks.Task ReloadAsync()
    {
        _ = LoadTrendingAsync();
        return LoadItemsAsync();
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

    // ==================== GitHub 热榜块（批次 KH）====================

    /// <summary>
    /// 拉一次热榜（走 <c>TrendingService</c> 的"同一本地日历日只抓一次"，所以每次重载都很便宜）。
    /// <para>
    /// 这一块必须自己说清"是不是上次的结果 / 是不是兜底来源 / 为什么是空的"：组件上没有主窗那条状态行，
    /// 抓失败却只留一个空块＝用户以为 GitHub 今天没有热榜（P-54/P-56 同一口径）。
    /// </para>
    /// </summary>
    private async System.Threading.Tasks.Task LoadTrendingAsync()
    {
        var settings = App.Services.GetRequiredService<Helpers.SettingsStore>();
        var on = settings.LoadTrendingEnabled() && settings.LoadTrendingGlanceEnabled();
        TrendingBlock.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) return;

        try
        {
            var service = App.Services.GetRequiredService<StarMark.Core.Trending.TrendingService>();
            var result = await service.GetAsync(
                StarMark.Abstractions.Trending.TrendingPeriod.Daily, null, force: false, CancellationToken.None);

            TrendingHost.Children.Clear();
            foreach (var repo in result.Repos.Take(TrendingLimit))
                TrendingHost.Children.Add(BuildTrendingRow(repo, result));

            var none = result.Repos.Count == 0;
            TrendingEmpty.Text = none ? "本期没有候选：去主窗「热榜」页看原因与「重试」。" : string.Empty;
            TrendingEmpty.Visibility = none ? Visibility.Visible : Visibility.Collapsed;

            var notes = new System.Collections.Generic.List<string>();
            if (result.Stale) notes.Add($"抓取失败·这是上次结果（{StarMark.Abstractions.Trending.TrendingCacheCodec.DescribeAge(result.FetchedAt, DateTimeOffset.Now)}）");
            if (result.Via == StarMark.Abstractions.Trending.TrendingSource.SearchApi) notes.Add("来源：GitHub 搜索接口兜底");
            if (!string.IsNullOrWhiteSpace(result.Notice)) notes.Add(result.Notice!);
            TrendingStatus.Text = string.Join("｜", notes);
            TrendingStatus.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            // 失败与"今天没有热榜"分开写：前者要给出可再试的信号，后者才谈数据本身。
            TrendingHost.Children.Clear();
            TrendingEmpty.Text = "热榜没拿到：" + ex.Message;
            TrendingEmpty.Visibility = Visibility.Visible;
            TrendingStatus.Text = string.Empty;
            TrendingStatus.Visibility = Visibility.Collapsed;
            StarLog.Warn($"今日速览热榜块加载失败：{ex.Message}");
        }
    }

    private Button BuildTrendingRow(StarMark.Abstractions.Trending.TrendingRepo repo,
        StarMark.Core.Trending.TrendingResult result)
    {
        var item = StarMark.Abstractions.Trending.TrendingRowDraft.ForRow(
            repo, StarMark.Abstractions.Trending.TrendingPeriod.Daily, result.Via ?? StarMark.Abstractions.Trending.TrendingSource.TrendingHtml);

        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock
        {
            Text = repo.FullName,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        panel.Children.Add(new TextBlock
        {
            Text = item.Subtitle ?? string.Empty,
            FontSize = 10,
            Opacity = 0.6,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var button = new Button
        {
            Content = panel,
            Tag = item,
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        ToolTipService.SetToolTip(button, "左键打开仓库 · 右键可 Star / 收进收藏");
        button.Click += (_, _) => _ = LauncherEx.OpenAsync(repo.Url);
        WireTrendingRowContext(button);
        return button;
    }

    /// <summary>
    /// 组件里的热榜行右键：与主窗逐项一致的菜单工厂（<see cref="ItemContextMenu"/>），Star 与
    /// "收进收藏"就在里面。必须拦 <b>ContextRequested</b> 并置 Handled：组件级菜单挂在 RootBorder 上，
    /// 不消费就会冒泡成"弹错菜单"（同 X1b 的结论）。候选行 Id=0 ⇒ 传 fallback 才能弹出。
    /// </summary>
    private static void WireTrendingRowContext(Button button)
        => button.ContextRequested += (s, args) =>
        {
            if (s is not FrameworkElement { Tag: Item item } anchor) return;
            args.Handled = true;
            ItemContextMenu.ShowForItem(item.Id, anchor, item);
        };

    private void TrendingRefresh_Click(object sender, RoutedEventArgs e)
        => _ = LoadTrendingForceAsync();

    /// <summary>「刷新」＝强制重抓今日榜（跳过当日短路）。抓完照旧回填两态由菜单在右键时现算。</summary>
    private async System.Threading.Tasks.Task LoadTrendingForceAsync()
    {
        TrendingRefreshButton.IsEnabled = false;
        try
        {
            var service = App.Services.GetRequiredService<StarMark.Core.Trending.TrendingService>();
            await service.GetAsync(StarMark.Abstractions.Trending.TrendingPeriod.Daily, null, force: true, CancellationToken.None);
        }
        catch (Exception ex) { StarLog.Warn($"今日速览热榜手动刷新失败：{ex.Message}"); }
        finally { TrendingRefreshButton.IsEnabled = true; }
        await LoadTrendingAsync();
    }

    private static Button BuildItemButton(Item item)
    {        var panel = new StackPanel { Spacing = 2 };
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
