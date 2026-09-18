#nullable enable
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.Integrations.Weather;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// Phase C 第二个新内容组件：**天气**。
/// 数据源 Open-Meteo（forecast + geocoding 都免费、无需 API Key），
/// WMO 天气码到中文/图标的映射见 <see cref="WeatherCode"/>（照搬 DeskBox 的 WeatherCodeMapper）。
/// <para>
/// 与 DeskBox 的差异：DeskBox 默认走 MSN Weather 并把一个私有 API key 硬编码进客户端，
/// 再回落到 Open-Meteo。StarMark 只保留 Open-Meteo 一条链路——不内置第三方凭证。
/// </para>
/// </summary>
public sealed partial class WeatherWidget : UserControl
{
    /// <summary>刷新间隔。天气数据本身 hourly 级更新，桌面组件半小时一次足够。</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);

    // 客户端与缓存做成静态：同一个城市可能开多个天气组件，共享一份才不会重复打接口。
    private static readonly OpenMeteoClient s_client = new();
    private static WeatherReport? s_cached;
    private static DateTimeOffset s_cachedAt;
    private static string s_cachedCityKey = string.Empty;
    private static readonly SemaphoreSlim s_gate = new(1, 1);

    private DispatcherQueueTimer? _timer;
    private bool _loading;

    /// <summary>温度单位（°C / °F）。组件内可读，切换按钮据此回写设置。</summary>
    private WeatherUnit _unit = WeatherUnit.Celsius;
    /// <summary>当前预报视图：多日 / 逐时。</summary>
    private WeatherForecastView _view = WeatherForecastView.Daily;

    public WeatherWidget()
    {
        InitializeComponent();
        Unloaded += (_, _) => _timer?.Stop();
        Loaded += (_, _) =>
        {
            LoadPreferences();
            ApplyToggleLabels();
            RenderCachedOrPlaceholder();
            StartTimer();
            _ = RefreshAsync(force: false);
        };
    }

    /// <summary>
    /// 读取单位/视图偏好。设置文件是用户可手改的，读失败一律走默认值，
    /// 绝不让异常冒出构造函数（参考 P0 事故：WidgetWindow 构造抛异常 = 整个组件白屏）。
    /// </summary>
    private void LoadPreferences()
    {
        try
        {
            var store = new SettingsStore();
            _unit = store.LoadWeatherUnit();
            _view = store.LoadWeatherView();
        }
        catch (Exception ex)
        {
            StarLog.Error("读取天气偏好失败，回落默认（摄氏 / 多日）", ex);
            _unit = WeatherUnit.Celsius;
            _view = WeatherForecastView.Daily;
        }
    }

    private void ApplyToggleLabels()
    {
        UnitButton.Content = WeatherUnits.UnitSuffix(_unit);
        ViewButton.Content = _view == WeatherForecastView.Hourly ? "未来三天" : "今日逐时";
        ForecastTitle.Text = _view == WeatherForecastView.Hourly ? "今日逐时" : "未来三天";
        ApplyForecastVisibility(s_cached is null);
    }

    private void UnitButton_Click(object sender, RoutedEventArgs e)
    {
        _unit = _unit == WeatherUnit.Celsius ? WeatherUnit.Fahrenheit : WeatherUnit.Celsius;
        SavePreference(store => store.SaveWeatherUnit(_unit));
        ApplyToggleLabels();
        if (s_cached is { } r) Render(r);
    }

    private void ViewButton_Click(object sender, RoutedEventArgs e)
    {
        _view = _view == WeatherForecastView.Daily ? WeatherForecastView.Hourly : WeatherForecastView.Daily;
        SavePreference(store => store.SaveWeatherView(_view));
        ApplyToggleLabels();
        if (s_cached is { } r) Render(r);
    }

    private void StartTimer()
    {
        _timer ??= DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(60);
        _timer.Tick -= Timer_Tick;
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (DateTimeOffset.Now - s_cachedAt >= RefreshInterval) _ = RefreshAsync(force: true);
    }

    /// <summary>把已有缓存先画出来（切换组件页/新建窗口时立刻有内容，不等网络）。</summary>
    private void RenderCachedOrPlaceholder()
    {
        if (s_cached is { } r) Render(r);
        else ShowEmpty(true);
    }

    private async Task RefreshAsync(bool force)
    {
        var city = Try(() => new SettingsStore().LoadWeatherCity(), null);
        if (city is null)
        {
            ShowEmpty(true);
            return;
        }

        var key = $"{city.Latitude:F3},{city.Longitude:F3}";
        if (!force && s_cached is not null && s_cachedCityKey == key) return;

        // 多实例同时创建时会并发打同一接口，用信号量合并成一次
        if (_loading) return;
        _loading = true;
        try
        {
            await s_gate.WaitAsync();
            try
            {
                if (!force && s_cached is not null && s_cachedCityKey == key) return;
                var report = await s_client.GetReportAsync(city, 4, CancellationToken.None);
                if (report is null)
                {
                    ShowError();
                    return;
                }
                s_cached = report;
                s_cachedAt = DateTimeOffset.Now;
                s_cachedCityKey = key;
                Render(report);
            }
            finally
            {
                s_gate.Release();
            }
        }
        catch (Exception ex)
        {
            StarLog.Error("天气组件刷新失败", ex);
            ShowError();
        }
        finally
        {
            _loading = false;
        }
    }

    private void Render(WeatherReport report)
    {
        ShowEmpty(false);

        CityBlock.Text = report.City.Display;
        UpdatedBlock.Text = report.FetchedAt.ToString("HH:mm", CultureInfo.InvariantCulture) + " 更新";

        TempBlock.Text = WeatherUnits.TemperatureText(report.Now.TemperatureC, _unit);
        DescBlock.Text = WeatherCode.Describe(report.Now.Code);
        IconBlock.Text = WeatherCode.Emoji(report.Now.Code, report.Now.IsDay);

        var feels = WeatherUnits.TemperatureValue(report.Now.FeelsLikeC, _unit);
        FeelsBlock.Text = report.Now.Humidity > 0
            ? $"体感 {feels}° · 湿度 {report.Now.Humidity}% · 风 {WeatherUnits.WindText(report.Now.WindSpeedKmh, _unit)}"
            : $"体感 {feels}°";

        // 第 0 天是今天，跳过；只展示未来三天
        ForecastHost.Children.Clear();
        var upcoming = report.Days.Count > 1 ? report.Days.GetRange(1, Math.Min(3, report.Days.Count - 1)) : [];
        foreach (var day in upcoming)
        {
            ForecastHost.Children.Add(BuildForecastRow(day));
        }

        BuildHourly(report);
    }

    /// <summary>
    /// 今日逐时：只取「当前时刻之后」的 12 个小时。过去的小时对用户没用，
    /// 留着反而要把列表横向拖半天才找得到现在。
    /// </summary>
    private void BuildHourly(WeatherReport report)
    {
        HourlyHost.Children.Clear();
        var now = DateTimeOffset.Now;
        var hours = report.Hours
            .Where(h => h.Time >= now.AddMinutes(-30))
            .Take(12)
            .ToList();

        foreach (var hour in hours)
        {
            var cell = new StackPanel { Spacing = 2, Width = 42 };
            cell.Children.Add(new TextBlock
            {
                Text = hour.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
                FontSize = 10,
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            cell.Children.Add(new TextBlock
            {
                Text = WeatherCode.Emoji(hour.Code, true),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            cell.Children.Add(new TextBlock
            {
                Text = WeatherUnits.TemperatureText(hour.TemperatureC, _unit),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            HourlyHost.Children.Add(cell);
        }
    }

    private Grid BuildForecastRow(WeatherDay day)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(64) },
                new ColumnDefinition { Width = new GridLength(28) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        var date = new TextBlock
        {
            Text = day.Date == DateOnly.FromDateTime(DateTime.Now) ? "今天" : $"{day.Date.Month}/{day.Date.Day}",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var icon = new TextBlock
        {
            Text = WeatherCode.Emoji(day.Code),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var desc = new TextBlock
        {
            Text = WeatherCode.Describe(day.Code),
            FontSize = 12,
            Opacity = 0.8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var temp = new TextBlock
        {
            Text = $"{WeatherUnits.TemperatureValue(day.MinC, _unit)}° / {WeatherUnits.TemperatureValue(day.MaxC, _unit)}°",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(date, 0);
        Grid.SetColumn(icon, 1);
        Grid.SetColumn(desc, 2);
        Grid.SetColumn(temp, 3);
        grid.Children.Add(date);
        grid.Children.Add(icon);
        grid.Children.Add(desc);
        grid.Children.Add(temp);
        return grid;
    }

    /// <summary>
    /// 持久化单位/视图偏好。写设置失败只记日志——组件是常驻 UI，
    /// 保存失败不该影响本次渲染（下次启动回到默认值，损失可接受）。
    /// </summary>
    private static void SavePreference(Action<SettingsStore> save)
    {
        try { save(new SettingsStore()); }
        catch (Exception ex) { StarLog.Error("保存天气偏好失败", ex); }
    }
    private async void CityButton_Click(object sender, RoutedEventArgs e)
    {
        var current = Try(() => new SettingsStore().LoadWeatherCity(), null);
        var input = await CenteredDialog.PromptAsync(
            "选择城市",
            "输入城市名（支持中文），将从 Open-Meteo 查询并记住选择。",
            placeholder: "例如：北京",
            defaultText: current?.Name);

        if (string.IsNullOrWhiteSpace(input)) return;

        var matches = await s_client.SearchCityAsync(input.Trim(), 8, "zh", CancellationToken.None);
        if (matches.Count == 0)
        {
            CityBlock.Text = $"未找到「{input.Trim()}」";
            return;
        }

        // 命中多个时取最相关的一个（Open-Meteo 已按相关度排序）。
        // 不做二次选择列表：组件里塞二级选择对话框成本高，且首条命中率足够。
        var city = matches[0];
        try { new SettingsStore().SaveWeatherCity(city); }
        catch (Exception ex) { StarLog.Error("保存天气城市失败", ex); }

        s_cached = null;
        s_cachedCityKey = string.Empty;
        await RefreshAsync(force: true);
    }

    private void ShowEmpty(bool empty)
    {
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ForecastHost.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ApplyForecastVisibility(empty);
        if (empty)
        {
            HourlyHost.Children.Clear();
            CityBlock.Text = "选择城市";
            UpdatedBlock.Text = string.Empty;
            TempBlock.Text = "--°";
            DescBlock.Text = "暂无数据";
            IconBlock.Text = "☀️";
            FeelsBlock.Text = string.Empty;
        }
    }

    /// <summary>
    /// 两个预报容器二选一：空态（没选城市 / 正在加载）都藏起来，有数据时按当前视图点亮。
    /// 用一个方法统一算，避免出现「切回多日视图时 ScrollViewer 还停在 Collapsed」这种状态残留。
    /// </summary>
    private void ApplyForecastVisibility(bool empty)
    {
        var hourly = _view == WeatherForecastView.Hourly;
        DailyScroll.Visibility = empty || hourly ? Visibility.Collapsed : Visibility.Visible;
        HourlyScroll.Visibility = empty || !hourly ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowError()
    {
        // 拿不到数据但已有缓存时，宁可继续显示旧数据（比闪一片空白体验好）
        if (s_cached is null) ShowEmpty(true);
        else UpdatedBlock.Text = "更新失败";
    }

    private static T? Try<T>(Func<T> f, T? fallback)
    {
        try { return f(); }
        catch { return fallback; }
    }
}
