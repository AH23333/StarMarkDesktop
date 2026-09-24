#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions.Feed;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// 设置页的「网址来源（RSS）」这一栏，只管三件事：<b>有哪些源、每个源开不开、这一栏整体开不开</b>。
/// <para>
/// 条目预览与收藏在批次 RB 从这里搬去了主窗「RSS」页（用户裁决）。留在设置页时的形状是
/// "改配置的地方嵌了一个阅读器"：源一多，那一块列表把整页撑开，而按「刷新并预览」抓完，
/// 结果却显示在一个连条目都点不开的位置。
/// </para>
/// <para>
/// <b>这一栏不发任何请求</b>：抓取按一次才抓一次，那颗按钮长在 RSS 页上。订阅地址是用户输入的
/// 第三方站点，自作主张地定时去访问它，等于替用户向外部服务承诺了他没同意的流量。
/// </para>
/// </summary>
public sealed partial class SettingsPage
{
    private readonly ObservableCollection<RssSourceRow> _rssSources = new();

    private void InitRssSection()
    {
        RssSourceRows.ItemsSource = _rssSources;
        ReloadRssSourceRows();
    }

    private void ReloadRssSourceRows()
    {
        var stored = App.Services.GetRequiredService<SettingsStore>().LoadRssSources();
        _rssSources.Clear();
        foreach (var source in stored) _rssSources.Add(new RssSourceRow(source));
        RssSourceEmpty.Visibility = _rssSources.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RssAdd_Click(object sender, RoutedEventArgs e)
    {
        var url = RssUrlBox.Text?.Trim() ?? string.Empty;
        var name = RssNameBox.Text?.Trim();
        // 就地给原因：地址写错了却只是"没加上"，用户会反复点同一个按钮
        if (RssSourceConfig.UrlProblem(url) is { } bad)
        {
            ViewModel.RssStatus = "没能添加：" + bad;
            return;
        }
        if (_rssSources.Any(row => string.Equals(row.Config.Url.Trim(), url, StringComparison.OrdinalIgnoreCase)))
        {
            ViewModel.RssStatus = "这个地址已经在列表里了，不必重复添加";
            return;
        }

        var nextId = _rssSources.Count == 0 ? 1 : _rssSources.Max(row => row.Config.Id) + 1;
        var source = new RssSourceConfig(nextId,
            string.IsNullOrWhiteSpace(name) ? RssSourceConfig.FallbackName(url) : name, url);
        _rssSources.Add(new RssSourceRow(source));
        PersistRssSources();
        RssUrlBox.Text = string.Empty;
        RssNameBox.Text = string.Empty;
        RssSourceEmpty.Visibility = Visibility.Collapsed;
        // 新源默认启用 ⇒ 这一栏就此算"开着"：导航栏立刻跟上，不要再让他去找那个总开关
        ViewModel.RefreshRssEnabled();
        ViewModel.RssStatus = "已添加「" + source.Name + "」。到「RSS」页按「刷新」看看它给了什么。";
    }

    private void RssDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RssSourceRow row }) return;
        _rssSources.Remove(row);
        PersistRssSources();
        RssSourceEmpty.Visibility = _rssSources.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        ViewModel.RefreshRssEnabled();
        ViewModel.RssStatus = "已移除「" + row.Name + "」（它之前被收藏进来的条目不受影响）";
    }

    private void RssSource_Toggled(object sender, RoutedEventArgs e)
    {
        // 分两段判：写成 ToggleSwitch { Tag: RssSourceRow row } 的否定模式后，开关本体就丢了，
        // 而这里要读的恰恰是它的新值。
        if (sender is not ToggleSwitch toggle || toggle.Tag is not RssSourceRow row) return;
        row.SetEnabled(toggle.IsOn);
        PersistRssSources();
        // 停掉最后一个启用的源＝这一栏没有可抓的东西了：导航项该跟着收掉（用户没表过态时才这样算）
        ViewModel.RefreshRssEnabled();
    }

    private void OpenRss_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateTo("rss");

    private void PersistRssSources()
        => App.Services.GetRequiredService<SettingsStore>()
            .SaveRssSources(_rssSources.Select(row => row.Config).ToList());
}

/// <summary>
/// 设置页里"一个来源地址"那一行。
/// <para><b>这里刻意没有"这一轮抓到几条 / 为什么没抓到"</b>：抓取发生在另一页，
/// 把它留在这里只会显示上一次会话的旧值，而旧值看起来和新值一模一样（假线索）。
/// 那一句话现在长在 <see cref="RssSourceSection"/> 的文件夹标题上。</para>
/// <para>实现 <see cref="INotifyPropertyChanged"/>：停用/启用是就地改的，不通知就会出现
/// 名字还在、开关弹回去的观感。</para>
/// </summary>
public sealed class RssSourceRow : INotifyPropertyChanged
{
    public RssSourceRow(RssSourceConfig config) => Config = config;

    public RssSourceConfig Config { get; private set; }

    /// <summary>给模板里的开关当初始值用（一次性绑定）。<b>控件本身声明在 XAML 模板里</b>：
    /// 由代码造一个控件塞进 DataTemplate，容器一重建就会因为"同一个控件已挂在别处"而炸。</summary>
    public bool Enabled => Config.Enabled;

    public string Name => Config.Name;
    public string Url => Config.Url;

    public void SetEnabled(bool enabled)
    {
        if (Config.Enabled == enabled) return;
        Config = Config with { Enabled = enabled };
        Raise();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
