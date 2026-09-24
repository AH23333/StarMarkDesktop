#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.Abstractions.Feed;
using StarMark.Core.Feed;
using StarMark.Integrations.Feed;
using StarMark.UI.Helpers;
using StarMark.UI.Services;

namespace StarMark.UI.Views;

/// <summary>
/// 设置页的「网址来源（RSS）」这一栏。单独一个 partial 文件而不是塞进 1200 行的主文件：
/// 这一栏的所有状态都只服务它自己，混在一起只会让两边都更难读。
/// <para>
/// <b>D4 口径在这里落地</b>：源里的条目只是<b>候选</b>，页面列出来给人挑，
/// <b>点「收藏」才写进库</b>——不做自动导入、不做已读未读，那会变成订阅流。
/// </para>
/// <para>
/// 抓取只在按「刷新并预览」时发生（没有后台定时器）：订阅地址是用户输入的第三方站点，
/// 自作主张地定时去访问它，等于替用户向外部服务承诺了他没同意的流量。
/// </para>
/// </summary>
public sealed partial class SettingsPage
{
    private readonly ObservableCollection<RssSourceRow> _rssSources = new();
    private readonly ObservableCollection<RssEntryRow> _rssEntries = new();
    private bool _rssBusy;

    /// <summary>校验符（ETag / Last-Modified）按源 id 记住，让第二次刷新能走 304 少下载一次。</summary>
    private readonly Dictionary<int, (string? Etag, string? LastModified)> _rssValidators = new();

    /// <summary>正在抓的那一轮的控制器；null＝没在抓。<b>抓取一定要能中途叫停</b>：
    /// 订阅地址是第三方的，一个连不上的地址配上 20 秒超时就能把这一栏按住几十秒。</summary>
    private CancellationTokenSource? _rssRounds;

    /// <summary>一轮最多花多久。到点就停手，把已经抓到的呈现出来——比"整页转圈等最后一个坏源"有用。
    /// <para>取 60 秒的依据：<see cref="RssClient"/> 单个源的超时是 20 秒，逐源串行意味着
    /// "两个坏源加一批好源"仍然跑得完；再多用户就不是在等结果，而是在等一个决定。</para></summary>
    private static readonly System.TimeSpan RoundBudget = System.TimeSpan.FromSeconds(60);

    private void InitRssSection()
    {
        RssSourceRows.ItemsSource = _rssSources;
        RssEntryList.ItemsSource = _rssEntries;
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
            RssStatusText.Text = "没能添加：" + bad;
            return;
        }
        if (_rssSources.Any(row => string.Equals(row.Config.Url.Trim(), url, StringComparison.OrdinalIgnoreCase)))
        {
            RssStatusText.Text = "这个地址已经在列表里了，不必重复添加";
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
        RssStatusText.Text = "已添加「" + source.Name + "」。点「刷新并预览」看看它给了什么。";
    }

    private void RssDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RssSourceRow row }) return;
        _rssSources.Remove(row);
        _rssValidators.Remove(row.Config.Id);
        PersistRssSources();
        RssSourceEmpty.Visibility = _rssSources.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        RssStatusText.Text = "已移除「" + row.Name + "」（它之前被收藏进来的条目不受影响）";
    }

    private void RssSource_Toggled(object sender, RoutedEventArgs e)
    {
        // 分两段判：写成 ToggleSwitch { Tag: RssSourceRow row } 的否定模式后，开关本体就丢了，
        // 而这里要读的恰恰是它的新值。
        if (sender is not ToggleSwitch toggle || toggle.Tag is not RssSourceRow row) return;
        row.SetEnabled(toggle.IsOn);
        PersistRssSources();
    }

    private void PersistRssSources()
        => App.Services.GetRequiredService<SettingsStore>()
            .SaveRssSources(_rssSources.Select(row => row.Config).ToList());

    private async void RssRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_rssBusy)
        {
            // 同一个按钮在抓取期间就是「停止抓取」：另起一个按钮要多占一处布局，而用户要的只是"别等了"
            _rssRounds?.Cancel();
            RssStatusText.Text = "正在停止…（已经抓到的会留在下面）";
            return;
        }
        if (_rssSources.Count == 0)
        {
            RssStatusText.Text = "还没有添加任何来源地址。";
            return;
        }
        _rssBusy = true;
        RssRefreshButton.Content = "停止抓取";
        RssStatusText.Text = "正在抓取…（再点一下可以停）";
        var cts = new CancellationTokenSource(RoundBudget);
        _rssRounds = cts;
        try
        {
            using var client = new RssClient();
            var run = await RssAggregator.RunAsync(
                _rssSources.Select(row => row.Config).ToList(),
                async source =>
                {
                    cts.Token.ThrowIfCancellationRequested();      // 停手时别再开新请求
                    _rssValidators.TryGetValue(source.Id, out var cached);
                    var fetched = await client.FetchAsync(source.Url, cached.Etag, cached.LastModified, cts.Token);
                    // 源没重发校验符时沿用旧的：否则下一次又退化成全量下载
                    _rssValidators[source.Id] = (fetched.ETag ?? cached.Etag, fetched.LastModified ?? cached.LastModified);
                    return fetched;
                });

            foreach (var row in _rssSources)
                row.SetStatus(Describe(run.Outcomes.FirstOrDefault(o => o.Source.Id == row.Config.Id)));

            var collected = await CollectedUrisAsync();
            _rssEntries.Clear();
            foreach (var entry in run.Entries)
                _rssEntries.Add(new RssEntryRow(entry, collected.Contains(entry.Link)));

            var enabledCount = _rssSources.Count(row => row.Config.Enabled);
            var okCount = run.Outcomes.Count(o => o.Ok && o.Source.Enabled);
            RssStatusText.Text = run.Entries.Count == 0
                ? $"{enabledCount} 个启用的源里有 {okCount} 个通了，但没有给出任何条目" +
                  (run.FailedCount > 0 ? $"；{run.FailedCount} 个源失败（原因写在各自那一行）" : string.Empty)
                : $"共 {run.Entries.Count} 条候选（{okCount} 个源给了内容）" +
                  (run.FailedCount > 0 ? $"；{run.FailedCount} 个源失败，原因写在对应那一行" : string.Empty)
                  + "。点「收藏」才会进库。";
            if (run.StoppedCount > 0)
                RssStatusText.Text += $"　已停止，还有 {run.StoppedCount} 个源没抓（再点一次接着抓）";
        }
        catch (Exception ex)
        {
            StarLog.Error("[RSS] 刷新失败", ex);
            RssStatusText.Text = "抓取没能完成：" + ex.Message;
        }
        finally
        {
            _rssBusy = false;
            _rssRounds = null;
            RssRefreshButton.Content = "刷新并预览";
            cts.Dispose();
        }
    }

    /// <summary>一行源这一轮的显示文本。<b>停用 / 没变化 / 失败 / 空 四种要分得开</b>：
    /// 把它们都写成"没有内容"，用户就没法判断该改地址还是该改网络。</summary>
    private static string Describe(RssSourceOutcome? outcome) => outcome switch
    {
        null => "没有参与这一轮",
        var o when o.Stopped => "没抓到它（已停止或整轮到时）",
        var o when !o.Source.Enabled => "已停用，这一轮没有抓",
        { NotModified: true } => "没有新内容（源说未变化）",
        { Ok: false } o => "失败：" + o.Error,
        var o when o.Entries.Count == 0 => "通了，但没有条目",
        var o => $"抓到 {o.Entries.Count} 条",
    };

    /// <summary>已经收藏过的地址集合：让"已收藏"在刷新后仍然显示得出来，而不是每次都像没点过。</summary>
    private async System.Threading.Tasks.Task<HashSet<string>> CollectedUrisAsync()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var repo = App.Services.GetRequiredItemRepository();
            foreach (var item in await repo.GetBySourceAsync(ItemSources.Local, ItemType.Bookmark, 2000, CancellationToken.None))
                if (!string.IsNullOrWhiteSpace(item.Uri)) set.Add(item.Uri.Trim());
        }
        catch (Exception ex)
        {
            // 读不出已收藏清单不影响列出候选：只是那一列标记会缺，别把整页卡住
            StarLog.Warn($"[RSS] 读取本机收藏失败，收藏状态按未收藏显示：{ex.Message}");
        }
        return await System.Threading.Tasks.Task.FromResult(set);
    }

    private async void RssCollect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RssEntryRow row }) return;
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var item = new Item
            {
                Type = ItemType.Bookmark,
                Source = ItemSources.Local,
                SourceId = RssEntryIdentity.BookmarkSourceId(row.Entry.Link),
                Title = row.Entry.Title,
                Subtitle = row.Entry.SourceName,
                Uri = row.Entry.Link,
                Description = string.IsNullOrWhiteSpace(row.Entry.Summary) ? null : row.Entry.Summary,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var repo = App.Services.GetRequiredItemRepository();
            var id = await repo.RecordItemAsync(item, CancellationToken.None);
            row.MarkCollected();
            if (id > 0)
                await repo.LogActivityAsync(ActivityKind.BookmarkAdd, $"{ItemSources.Local}:{item.SourceId}",
                    item.Title, item.Uri, CancellationToken.None);
            RssStatusText.Text = "已收进本机收藏：" + item.Title;
        }
        catch (Exception ex)
        {
            StarLog.Error($"[RSS] 收藏失败（{row.Entry.Link}）", ex);
            RssStatusText.Text = "收藏没能完成：" + ex.Message;
        }
    }
}

/// <summary>设置页里"一个来源地址"那一行。
/// <b>实现 <see cref="INotifyPropertyChanged"/></b>：抓取是在列好行之后才回填状态的，
/// 不通知就出现"源那一行永远停在『还没抓过』"——点了按钮却没有回报，等于没做。</summary>
public sealed class RssSourceRow : INotifyPropertyChanged
{
    public RssSourceRow(RssSourceConfig config) => Config = config;

    public RssSourceConfig Config { get; private set; }

    /// <summary>给模板里的开关当初始值用（一次性绑定）。<b>控件本身声明在 XAML 模板里</b>：
    /// 由代码造一个控件塞进 DataTemplate，容器一重建就会因为"同一个控件已挂在别处"而炸。</summary>
    public bool Enabled => Config.Enabled;

    public string Name => Config.Name;
    public string Url => Config.Url;
    public string Status { get; private set; } = "还没抓过";

    public void SetEnabled(bool enabled)
    {
        if (Config.Enabled == enabled) return;
        Config = Config with { Enabled = enabled };
        Raise();
    }

    public void SetStatus(string status)
    {
        if (Status == status) return;
        Status = status;
        Raise();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>设置页里"一条候选条目"那一行。<b>收藏成功后要把按钮变成「已收藏」并禁点</b>：
/// 否则用户会重复点，而重复点只是把同一行更新一遍——看起来像没反应。</summary>
public sealed class RssEntryRow : INotifyPropertyChanged
{
    public RssEntryRow(RssEntry entry, bool collected)
    {
        Entry = entry;
        Collected = collected;
    }

    public RssEntry Entry { get; }
    public bool Collected { get; private set; }

    public string Title => Entry.Title;

    /// <summary>第二行的小字：源名 + 时间。<b>没时间就明说"源没给时间"</b>，不拿"刚刚"糊过去。</summary>
    public string Meta => Entry.PublishedAt is { } at
        ? $"{Entry.SourceName} · {at.ToLocalTime():yyyy-MM-dd HH:mm}"
        : Entry.SourceName + " · 源没给时间";

    public string ActionLabel => Collected ? "已收藏" : "收藏";
    public bool CanCollect => !Collected;

    public void MarkCollected()
    {
        if (Collected) return;
        Collected = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActionLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCollect)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
