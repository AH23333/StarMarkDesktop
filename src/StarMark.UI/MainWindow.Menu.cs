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
/// MainWindow 的这一段——右上角与「组件」那两颗的菜单：菜单项从注册表现算，不写死。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class MainWindow
{

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettingsPage();

    // ===== 桌面组件顶栏入口 =====

    private void WidgetsButton_Click(object sender, RoutedEventArgs e)
    {
        _widgetsMenu ??= BuildWidgetsMenu();
        _widgetsMenu.ShowAt(WidgetsButton, new Point(0, WidgetsButton.ActualHeight));
    }

    private MenuFlyout BuildWidgetsMenu()
    {
        var menu = new MenuFlyout();
        foreach (var kind in WidgetStorage.AllKinds)
        {
            var item = new ToggleMenuFlyoutItem { Text = WidgetStorage.KindTitle(kind) };
            var captured = kind;
            item.Click += (_, _) =>
                _ = _widgetManager.SetEnabledAsync(captured, !_widgetManager.IsEnabled(captured));
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var showAll = new MenuFlyoutItem { Text = "全部显示" };
        showAll.Click += (_, _) => _ = _widgetManager.ShowAllAsync();
        var hideAll = new MenuFlyoutItem { Text = "全部隐藏" };
        hideAll.Click += (_, _) => _ = _widgetManager.HideAllAsync();
        menu.Items.Add(showAll);
        menu.Items.Add(hideAll);
        menu.Items.Add(new MenuFlyoutSeparator());
        var toggle = new MenuFlyoutItem { Text = "显示/隐藏全部组件" };
        toggle.Click += (_, _) => _ = _widgetManager.ToggleAllAsync();
        menu.Items.Add(toggle);
        var manage = new MenuFlyoutItem { Text = "在设置中管理…" };
        manage.Click += (_, _) => OpenSettingsPage();
        menu.Items.Add(manage);

        menu.Opening += (_, _) =>
        {
            for (var i = 0; i < WidgetStorage.AllKinds.Count; i++)
            {
                if (menu.Items[i] is ToggleMenuFlyoutItem t)
                    t.IsChecked = _widgetManager.IsEnabled(WidgetStorage.AllKinds[i]);
            }
        };
        return menu;
    }

    // ===== 开发辅助：状态转储（STARMARK_DIAG / STARMARK_SIM_*）=====

    private void SetupDiag()
    {
        var diagPath = Environment.GetEnvironmentVariable("STARMARK_DIAG");
        var simQuery = Environment.GetEnvironmentVariable("STARMARK_SIM_QUERY");
        if (string.IsNullOrWhiteSpace(diagPath)) return;

        var simOn = !string.IsNullOrWhiteSpace(simQuery);
        var diagTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        diagTimer.Tick += (_, _) =>
        {
            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"[tick {DateTimeText.TickStamp(DateTime.Now)}] page={ViewModel.CurrentPageTag} searchbox=[{SearchBox.Text}]");
            if (ContentFrame.Content is SearchPage sp)
            {
                lines.AppendLine($"  SEARCH: query=[{sp.ViewModel.Query}] empty=[{sp.ViewModel.EmptyHint}] " +
                                 $"results={sp.ViewModel.Results.Count} has={sp.ViewModel.HasResults} busy={sp.ViewModel.IsSearching}");
            }
            if (ContentFrame.Content is FolderTreePage tp)
            {
                var roots = tp.ViewModel.Roots;
                lines.AppendLine($"  TREE: roots={roots.Count} empty=[{tp.ViewModel.EmptyHint}]");
                foreach (var r in roots.Take(12))
                {
                    lines.AppendLine($"    - {r.Name} (total={r.TotalCount} own={r.Items.Count} sub={r.Children.Count})");
                    foreach (var c in tp.ViewModel.Hydrate(r).Take(6))
                        lines.AppendLine($"        card[{r.Name}]: {c.Type} | {c.Title}");
                    if (r.Children.Count > 0)
                        foreach (var ch in r.Children)
                            foreach (var c in tp.ViewModel.Hydrate(ch).Take(3))
                                lines.AppendLine($"        card[{r.Name}/{ch.Name}]: {c.Type} | {c.Title}");
                }
            }
            if (simOn)
            {
                var probe = SearchBox.Text;
                if (probe == string.Empty)
                    SearchBox.Text = simQuery!;      // 第1次：输入查询
                else if (probe == simQuery && _diagSimStep == 0)
                {
                    _diagSimStep = 1;
                    SearchBox.Text = string.Empty;  // 第2次：清空搜索栏
                }
            }
            System.IO.File.AppendAllText(diagPath!, lines.ToString());
        };
        diagTimer.Start();
    }
}
