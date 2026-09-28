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
/// MainWindow 的这一段——导航这一头：页签可见性、跳转与选择变化。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class MainWindow
{

    /// <summary>
    /// 打开设置页。<paramref name="tab"/> 是页签标题（如「健康与诊断」）：从组件右键跳过来的人
    /// 要的是那一栏，落在默认页等于让他到了门口再自己找房间。
    /// </summary>
    private void OpenSettingsPage(string? tab = null)
    {
        ViewModel.CurrentPageTag = "settings";
        NavView.SelectedItem = null;
        SetNavVisible(false);
        if (ContentFrame.Content is not SettingsPage)
            ContentFrame.Navigate(typeof(SettingsPage));
        if (ContentFrame.Content is SettingsPage page) page.SelectTab(tab);
        PushToolbarToContent();
    }

    // ===== 导航 =====

    /// <summary>
    /// 搜索态折叠整条导航栏，浏览态恢复。
    /// 浏览器扩展是「搜索时用 toolbar 整行替换 tabs」，不留空白；
    /// 这里靠 NavView 独占 Grid 的一行（Height=Auto）+ Collapsed 实现同样效果。
    /// </summary>
    private void SetNavVisible(bool visible)
        => NavView.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs? args)
    {
        // 编程触发（初始选择）时 args 为 null，直接读 SelectedItem
        if (NavView.SelectedItem is not NavigationViewItem item) return;
        if (item.Tag is string tag)
        {
            ViewModel.CurrentPageTag = tag;
            SetNavVisible(true);
            NavigateToPage(tag);
            PushToolbarToContent();
        }
    }

    public void NavigateTo(string tag, object? param = null)
    {
        NavView.SelectedItem = null;
        // 编程式导航不触发 SelectionChanged（上面把 SelectedItem 置 null），故在此同步当前页标记；
        // 否则 FolderTreePageViewModel/SearchPageViewModel 的「仅当前页刷新」闸停在旧值，深链进入的页不再响应数据变更。
        ViewModel.CurrentPageTag = tag;
        SetNavVisible(tag is not ("search" or "settings"));
        NavigateToPage(tag, param);
        PushToolbarToContent();
    }

    private void NavigateToPage(string tag, object? param = null)
    {
        var pageType = tag switch
        {
            "search" => typeof(SearchPage),
            "tree" => typeof(FolderTreePage),
            "tags" => typeof(TagsPage),
            "activity" => typeof(ActivityPage),
            "hidden" => typeof(HiddenPage),
            "clipboard" => typeof(ClipboardPage),
            "trending" => typeof(TrendingPage),
            "rss" => typeof(RssPage),
            "snapshot" => typeof(SnapshotPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(SearchPage),
        };
        ContentFrame.Navigate(pageType, param);
    }

    /// <summary>
    /// 按设置开关导航栏的「热榜」项（<b>即时</b>，不等重启）。
    /// <para>关掉的那一刻若正停在这一页，必须退回文件夹页：入口已经没了、屏幕上却还留着它的内容，
    /// 是"程序坏了"的典型观感（P-54 那条口径的另一面——状态变了界面就得跟着变）。</para>
    /// </summary>
    public void ApplyTrendingNavVisibility(bool enabled)
    {
        NavTrendingItem.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled && ViewModel.CurrentPageTag == "trending") NavigateTo("tree");
    }

    /// <summary>
    /// 按 RSS 总开关即时收放导航栏的「RSS」项（与 <see cref="ApplyTrendingNavVisibility"/> 同一形状：
    /// 关掉的那一刻若正停在这一页就退回文件夹页——入口已经没了、屏幕上却还留着它的内容，是"程序坏了"的典型观感）。
    /// </summary>
    public void ApplyRssNavVisibility(bool enabled)
    {
        NavRssItem.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled && ViewModel.CurrentPageTag == "rss") NavigateTo("tree");
    }

    private void ShowHidden_Click(object sender, RoutedEventArgs e)
    {
        // 复选框只是当前页过滤器；隐藏页由导航菜单“隐藏”进入
        PushToolbarToContent();
    }

    /// <summary>状态点语义色（caution=进行中/失败，success=成功）；主题切换时按此重解析。</summary>
    private StatusKind _statusKind = StatusKind.Caution;
}
