#nullable enable
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;
using StarMark.Abstractions;
using StarMark.Abstractions.Language;
using StarMark.Core.Capture;
using StarMark.Core.Hotkeys;
using StarMark.Core.Widgets;
using StarMark.Integrations.SystemTray;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;
using StarMark.UI.Views;

namespace StarMark.UI;

/// <summary>
/// MainWindow 的这一段——托盘这一头：菜单每一项的措辞与勾选态从哪来、点了之后走哪条出口（含贴图组子菜单）。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class MainWindow
{

    // ───────────────────────── 托盘 ─────────────────────────

    private void CreateTray()
    {
        if (_trayHost is not null) return;
        _trayHost = new TrayHost();
        // 子菜单直接从组件注册表生成：以前这是 TrayHost 里一份手写的 5 条标题数组，
        // 注册表长出到 12 种后，7 种组件（含新的剪贴板格）在托盘里既看不到也关不掉（P-62b）。
        _trayHost.WidgetMenuItems = WidgetStorage.AllKinds
            .Select(k => new TrayWidgetItem((int)k, WidgetStorage.KindTitle(k)))
            .ToList();
        _trayHost.IsWidgetEnabled = kind => _widgetManager.IsEnabled((WidgetKind)kind);
        _trayHost.ShowRequested += () => DispatcherQueue.TryEnqueue(() => Present(false));
        _trayHost.ExitRequested += () => DispatcherQueue.TryEnqueue(ExitApp);
        _trayHost.WidgetsToggleRequested += () => DispatcherQueue.TryEnqueue(() => _ = _widgetManager.ToggleAllAsync());
        _trayHost.WidgetToggleRequested += kind => DispatcherQueue.TryEnqueue(() =>
        {
            var k = (WidgetKind)kind;
            _ = _widgetManager.SetEnabledAsync(k, !_widgetManager.IsEnabled(k));
        });
        _trayHost.ShowAllWidgetsRequested += () => DispatcherQueue.TryEnqueue(() => _ = _widgetManager.ShowAllAsync());
        _trayHost.HideAllWidgetsRequested += () => DispatcherQueue.TryEnqueue(() => _ = _widgetManager.HideAllAsync());
        _trayHost.SettingsRequested += () => DispatcherQueue.TryEnqueue(() => Present(true));
        // 附加命令的清单用回调现取，不用属性快照：主题/性能模式/自启/热键开关都会变，
        // 快照等于"托盘显示的是上一次右键时的状态"，勾选框会指着错的那一项。
        _trayHost.CommandProvider = BuildTrayCommands;
        _trayHost.CommandInvoked += tag => DispatcherQueue.TryEnqueue(() => RunTrayCommand(tag));
        _trayHost.Initialize();
    }

    // ───────────────────────── 托盘：附加命令 ─────────────────────────
    // 编号是"宿主自己的语义标签"，与 TrayHost 发出去的菜单命令号无关（它按渲染顺序发号）。

    private const int TrayScreenshot = 1;
    private const int TrayTopmostToggle = 2;
    private const int TrayScreenPin = 3;
    private const int TrayScreenOcr = 7;
    private const int TrayCanvas = 8;
    private const int TrayPinsShowHide = 4;
    private const int TrayPinsClickThrough = 5;
    private const int TrayPinsCloseAll = 6;
    private const int TrayThemeDefault = 10;
    private const int TrayThemeLight = 11;
    private const int TrayThemeDark = 12;
    private const int TrayPerfBalanced = 20;
    private const int TrayPerfSaver = 21;
    private const int TrayAutostart = 30;
    private const int TrayHotkeysEnabled = 31;

    private static StarMark.UI.Services.AutostartService Autostart()
        => App.Services.GetRequiredService<StarMark.UI.Services.AutostartService>();

    /// <summary>
    /// 「贴图组」那棵子菜单：每组一行，点开是这一组自己的三个动作。
    /// <para>父行（组名那一行）只是容器，Tag 用 <see cref="TrayGroupParent"/>——它永远发不出命令，
    /// 但照样占一个号位（发号规则见 <c>TrayHost.AppendCommands</c>）。真正的动作号由 Core 的
    /// <see cref="StarMark.Core.Capture.PinGrouping"/> 编码，<b>这里不自己算号</b>。</para>
    /// <para>一行都不剩的时候整棵子菜单不出现：那时它是一条只能盯着的空目录，比没有更让人以为分组丢了。</para>
    /// </summary>
    private const int TrayGroupParent = -1;

    private static TrayHost.TrayCommandItem? GroupMenu()
    {
        var groups = StarMark.UI.Services.PinManager.Groups;
        if (groups.Count == 0) return null;
        return new TrayHost.TrayCommandItem("贴图组（整组隐藏 / 忽略鼠标 / 关闭）", TrayGroupParent,
            Children: groups.Select(group => new TrayHost.TrayCommandItem(
                PinGrouping.GroupLabel(group.Name, group.Members.Count), TrayGroupParent,
                Children:
                [
                    new(PinGrouping.HideLabel(group.AllHidden), PinGrouping.TagOf(group.Serial, PinGrouping.Action.Show)),
                    new(PinGrouping.ThroughLabel(group.AllThrough), PinGrouping.TagOf(group.Serial, PinGrouping.Action.Through)),
                    new(PinGrouping.CloseLabel(group.Members.Count), PinGrouping.TagOf(group.Serial, PinGrouping.Action.Close)),
                ])).ToList());
    }

    /// <summary>右键那一刻现取托盘附加命令：勾选态反映当前设置，正在截图时不让人再点一次。</summary>
    private IReadOnlyList<TrayHost.TrayCommandItem> BuildTrayCommands()
    {
        var perf = _settings.LoadPerformanceMode();
        var hotkeysOn = _settings.LoadEnableGlobalHotKey();
        var (pins, hidden) = (StarMark.UI.Services.PinManager.Count, StarMark.UI.Services.PinManager.AreHidden);
        var list = new List<TrayHost.TrayCommandItem>
        {
            new("截图（框选区域）", TrayScreenshot,
                Enabled: !StarMark.UI.Services.ScreenshotService.IsCapturing, SeparatorBefore: true),
            new("贴图（框选后钉在桌面）", TrayScreenPin,
                Enabled: !StarMark.UI.Services.ScreenshotService.IsCapturing),
            new("识字（框选后复制文字）", TrayScreenOcr,
                Enabled: !StarMark.UI.Services.ScreenshotService.IsCapturing),
            // 画布的开关。穿透态下画布收不到任何鼠标事件，托盘这一项与全局热键是"找回来"的两条路（§16.6）。
            new("屏幕画布（讲解时在屏幕上画）", TrayCanvas,
                StarMark.UI.Services.CanvasService.IsRunning, SeparatorBefore: true),
            // 三条"所有贴图"的动作。一张都没有时点它们都是空动作 ⇒ 灰掉并把状态写进标签，
            // 比"点了没反应"好（P-54 口径）。标签按要执行的动作说人话（菜单惯例），
            // 只有"忽略鼠标"这项是状态开关，所以它用勾选态。
            new(PinLabel(pins, hidden), TrayPinsShowHide, SeparatorBefore: true, Enabled: pins > 0),
            new("贴图忽略鼠标", TrayPinsClickThrough, StarMark.UI.Services.PinManager.ClickThrough, Enabled: pins > 0),
            new("关闭所有贴图", TrayPinsCloseAll, Enabled: pins > 0),            new("所有组件置顶 / 不置顶", TrayTopmostToggle, SeparatorBefore: true),
            new("主题 · 跟随系统", TrayThemeDefault, _themePref == ThemePreference.Default, SeparatorBefore: true),
            new("主题 · 浅色", TrayThemeLight, _themePref == ThemePreference.Light),
            new("主题 · 深色", TrayThemeDark, _themePref == ThemePreference.Dark),
            new("性能 · 均衡", TrayPerfBalanced, perf == StarMark.Core.Performance.PerformanceMode.Balanced, SeparatorBefore: true),
            new("性能 · 省资源", TrayPerfSaver, perf == StarMark.Core.Performance.PerformanceMode.ResourceSaver),
            new("开机自动启动", TrayAutostart, Autostart().IsEnabled(), SeparatorBefore: true),
            new("全局快捷键已启用", TrayHotkeysEnabled, hotkeysOn),
        };
        // 有了组才多出这一棵，而且插在"所有贴图"那三条之后（先全局、后按组，与人的操作顺序一致）。
        // 一组贴图收起后它的工具条跟着消失，托盘这一行就是那几张图唯一的出口——不能只在贴图条上给入口。
        if (GroupMenu() is { } groupMenu) list.Insert(list.FindIndex(item => item.Tag == TrayPinsCloseAll) + 1, groupMenu);
        // 画布总开关关掉时整条不出现（发起人裁决："关掉就别留入口"）。按 Ctrl+Alt+D 仍会给一句
        // "要先在设置里打开"——那是 CanvasService 的闸门，不是这条菜单项的职责（见 Start 里的开关检查）。
        return _settings.LoadCanvasEnabled()
            ? list
            : list.Where(item => item.Tag != TrayCanvas).ToList();
    }

    /// <summary>贴图显隐那一项的标签：没有贴图时把状态写进文字，灰掉的项才知道自己为什么点不动。</summary>
    private static string PinLabel(int pins, bool hidden)
        => pins == 0 ? "显示 / 收起所有贴图（当前没有贴图）" : hidden ? "显示所有贴图" : "收起所有贴图";

    private void RunTrayCommand(int tag)
    {
        switch (tag)
        {
            case TrayScreenshot:
                StarMark.UI.Services.ScreenshotService.Start(StarMark.UI.Services.CaptureMode.Toolbar);
                break;
            case TrayScreenPin:
                StarMark.UI.Services.ScreenshotService.Start(StarMark.UI.Services.CaptureMode.Pin);
                break;
            case TrayScreenOcr:
                StarMark.UI.Services.ScreenshotService.Start(StarMark.UI.Services.CaptureMode.Ocr);
                break;
            case TrayCanvas:
                StarMark.UI.Services.CanvasService.Toggle();
                break;
            case TrayPinsShowHide:
                StarMark.UI.Services.PinManager.ToggleHidden();
                break;
            case TrayPinsClickThrough:
                StarMark.UI.Services.PinManager.ToggleClickThrough();
                break;
            case TrayPinsCloseAll:
                StarMark.UI.Services.PinManager.CloseAll();
                break;
            // 组命令的号由 Core 编码（组号 + 动作），这里只认号就交出去：不查那一组还在不在——
            // 菜单行是右键那一刻的快照，走到这里时它可能已经被关掉；"点了没反应"由 PinManager 回一句原因。
            case int groupTag when PinGrouping.IsGroupTag(groupTag):
                StarMark.UI.Services.PinManager.RunGroupCommand(groupTag);
                break;
            case TrayTopmostToggle:
                _ = _widgetManager.ToggleAllTopmostAsync();
                break;
            case TrayThemeDefault:
            case TrayThemeLight:
            case TrayThemeDark:
                ApplyThemePreference(tag switch
                {
                    TrayThemeLight => ThemePreference.Light,
                    TrayThemeDark => ThemePreference.Dark,
                    _ => ThemePreference.Default,
                });
                break;
            case TrayPerfBalanced:
            case TrayPerfSaver:
                var mode = tag == TrayPerfSaver ? StarMark.Core.Performance.PerformanceMode.ResourceSaver : StarMark.Core.Performance.PerformanceMode.Balanced;
                // 只需要写盘：性能模式的消费方（内存回收、监控采样间隔…）每次都经
                // PerformanceSettingsPolicy 现读，所以不需要"再通知一遍"，也就不会漏通知。
                _settings.SavePerformanceMode(mode);
                break;
            case TrayAutostart:
                Autostart().SetEnabled(!Autostart().IsEnabled());
                break;
            case TrayHotkeysEnabled:
                var enable = !_settings.LoadEnableGlobalHotKey();
                _settings.SaveEnableGlobalHotKey(enable);
                ApplyTraySettings();
                break;
        }
    }

    private void DisposeTray()
    {
        _trayHost?.Dispose();
        _trayHost = null;
    }

    /// <summary>设置页保存后调用：托盘与全局热键开关即时生效，无需重启。</summary>
    public void ApplyTraySettings()
    {
        var trayEnabled = _settings.LoadEnableTray();
        var hotkeyEnabled = _settings.LoadEnableGlobalHotKey();
        if (trayEnabled) CreateTray();
        else DisposeTray();

        // 热键的注册 / 注销只归 HotkeyService。此前这里调的是 TrayHost.RegisterGlobalHotKey()——
        // 一个只打一行日志的空壳（热键早就迁到 MainWindow 句柄上注册），所以「启用全局快捷键」
        // 拨完当场不生效，要等重启或下一次点「保存快捷键」。
        if (App.Services.GetRequiredService<HotkeyService>() is { } hotkey)
            hotkey.ApplyBindings(hotkeyEnabled
                ? _settings.GetRegisterableHotkeyBindings()
                : new Dictionary<string, HotkeyGesture>());
    }
}
