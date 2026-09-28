#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Core.Widgets;
using StarMark.UI.ViewModels;
using StarMark.UI.Views;

namespace StarMark.UI.Services;

/// <summary>
/// 组件内容工厂：负责"某种组件的内容长什么样"，窗口宿主只负责承载。
/// 移植自 DeskBox <c>Services/WidgetContentFactory.cs</c> 的分层思路——
/// 元数据在 <see cref="WidgetRegistry"/>，创建逻辑在工厂，生命周期与层级在窗口/管理器。
/// 新增组件只需在此注册一个委托，不必修改 <c>WidgetWindow</c>。
/// </summary>
public sealed class WidgetContentFactory
{
    private readonly Dictionary<WidgetKind, Func<WidgetWindow, UIElement>> _builders = new();

    public static WidgetContentFactory Default { get; } = CreateDefault();

    public void Register(WidgetKind kind, Func<WidgetWindow, UIElement> builder)
    {
        _builders[kind] = builder;
    }

    public bool CanBuild(WidgetKind kind) => _builders.ContainsKey(kind);

    public UIElement Build(WidgetKind kind, WidgetWindow host)
    {
        if (_builders.TryGetValue(kind, out var builder))
        {
            return builder(host);
        }

        string title = WidgetRegistry.Default.TryGet(kind, out var d) ? d.Title : kind.ToString();
        return new TextBlock
        {
            Text = $"「{title}」暂未实现",
            Margin = new Thickness(16),
            Opacity = 0.6,
        };
    }

    private static WidgetContentFactory CreateDefault()
    {
        var factory = new WidgetContentFactory();
        factory.Register(WidgetKind.QuickLaunch, w => new QuickLaunchWidget(w.Storage, w.Manager, w.InstanceId, w));
        factory.Register(WidgetKind.Todo, w => new TodoWidget(w.Repository, w.InstanceId));
        factory.Register(WidgetKind.QuickNote, w => new QuickNoteWidget(w.Repository, w.InstanceId));
        factory.Register(WidgetKind.Clock, w => new ClockWidget());
        factory.Register(WidgetKind.Search, w => new SearchWidget(w.Repository));
        // 差异化条目格：四种模式共用一个 ItemGridWidget，按 WidgetKind 决定查询策略。
        factory.Register(WidgetKind.TagGrid, w => new ItemGridWidget(ItemGridMode.Tag, w));
        factory.Register(WidgetKind.Clipboard, w => new ItemGridWidget(ItemGridMode.Clipboard, w));
        factory.Register(WidgetKind.Activity, w => new ItemGridWidget(ItemGridMode.Activity, w));
        factory.Register(WidgetKind.Pinned, w => new ItemGridWidget(ItemGridMode.Pinned, w));
        // Phase C：今日速览（日期 + 农历/节日 + 倒计时 + 常看条目）
        factory.Register(WidgetKind.Glance, w => new GlanceWidget(w.Repository));
        // Phase C：天气（Open-Meteo 实况 + 未来三天）+ 音乐（SMTC 播放控制）
        factory.Register(WidgetKind.Weather, _ => new WeatherWidget());
        factory.Register(WidgetKind.Music, _ => new MusicWidget());
        // 效率组件批次 LA：计算器（历史带按实例持久化，故要拿到实例配置与管理器）
        factory.Register(WidgetKind.Calc, w => new CalcWidget(w.Config, w.Manager));
        factory.Register(WidgetKind.WorldClock, w => new WorldClockWidget(w.Config, w.Manager));
        factory.Register(WidgetKind.Countdown, w => new CountdownWidget(w.Config, w.Manager));
        factory.Register(WidgetKind.Focus, w => new FocusTimerWidget(w.Config, w.Manager));
        // 效率组件批次 MM：系统监控（勾选按实例持久化；采样只在窗口可见时进行）
        factory.Register(WidgetKind.SystemMonitor, w => new SystemMonitorWidget(w.Config, w.Manager));
        return factory;
    }
}
