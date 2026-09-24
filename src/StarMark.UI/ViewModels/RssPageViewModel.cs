#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using StarMark.Abstractions;
using StarMark.Abstractions.Feed;
using StarMark.Core.Feed;
using StarMark.Integrations.Feed;
using StarMark.UI.Helpers;
using StarMark.UI.Services;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 主窗「RSS」页：每个订阅源<b>一个文件夹</b>，文件夹里是它当前给出的条目。
/// <para>
/// 与热榜页同一族（外部数据、<b>默认不入库</b>，D4 口径），差别在落点：这里的条目收进库时
/// 会带上一层文件夹（<c>RssFolders</c>），于是「收藏」这个动作同时完成了归档。
/// </para>
/// <para>
/// <b>抓取只在这一页按「刷新」时发生</b>（没有后台定时器）：订阅地址是用户输入的第三方站点，
/// 自作主张地定时去访问它，等于替用户向外部服务承诺了他没同意的流量。
/// </para>
/// <para>
/// 这一页刻意<b>没有搜索、没有排序</b>（用户裁决）：条目按源分组、组内按源给的顺序，
/// 右键也只是主窗右键的一个子集（打开 / 复制 / 收藏）。要做筛选去「文件夹」页——那里已经是本机的数据了。
/// </para>
/// </summary>
public partial class RssPageViewModel : ObservableObject
{
    private readonly SettingsStore _settings;

    /// <summary>校验符（ETag / Last-Modified）按源 id 记住，让第二次刷新能走 304 少下载一次。</summary>
    private readonly Dictionary<int, (string? Etag, string? LastModified)> _validators = new();

    /// <summary>正在抓的那一轮的控制器；null＝没在抓。<b>抓取一定要能中途叫停</b>：
    /// 订阅地址是第三方的，一个连不上的地址配上 20 秒超时就能把这一页按住几十秒。</summary>
    private CancellationTokenSource? _rounds;

    /// <summary>一轮最多花多久。到点就停手，把已经抓到的呈现出来——比"整页转圈等最后一个坏源"有用。
    /// <para>取 60 秒的依据：<see cref="RssClient"/> 单个源的超时是 20 秒，逐源串行意味着
    /// "两个坏源加一批好源"仍然跑得完；再多用户就不是在等结果，而是在等一个决定。</para></summary>
    private static readonly TimeSpan RoundBudget = TimeSpan.FromSeconds(60);

    public ObservableCollection<RssSourceSection> Sections { get; } = new();

    /// <summary>
    /// 源列表或某一轮结果变了 ⇒ 页面要重铺手风琴。
    /// <para>为什么是事件而不是让页面去监听集合：状态与行是<b>原地改</b>的（源没变、只是这一轮抓完了），
    /// 集合本身没有 Reset，页面光订阅 CollectionChanged 会永远等不到那一次刷新——
    /// 表现就是"抓完了界面还是空的"。与 <c>FolderTreePageViewModel.RootsReady</c> 同一分工。</para>
    /// </summary>
    public event Action? StructureChanged;

    /// <summary>展开时一次渲染多少条（照「文件夹」页的口径：先给一屏，剩下的按「展开更多」要）。</summary>
    public const int InitialRows = 30;
    public const int MoreStep = 50;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _emptyHint = string.Empty;

    public RssPageViewModel()
    {
        _settings = App.Services.GetRequiredService<SettingsStore>();
        Enabled = _settings.LoadRssEnabled();
        // 收藏结果与"打不开"的原因都落在这一行的状态上：动作没有回显，用户只能靠列表有没有变来猜。
        RssItemActions.NoticeRaised += m => StatusText = m;
    }

    public bool HasSections => Sections.Count > 0;

    /// <summary>状态行有没有内容（没有时整行不占位，避免一屏空白）。</summary>
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    /// <summary>同一个按钮在抓取期间就是「停止抓取」：另起一个按钮要多占一处布局，而用户要的只是"别等了"。</summary>
    public string RefreshLabel => IsBusy ? "停止抓取" : "刷新";

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(RefreshLabel));

    private void HasSectionsChanged() => OnPropertyChanged(nameof(HasSections));

    partial void OnEnabledChanged(bool value)
    {
        if (!value)
        {
            Sections.Clear();
            HasSectionsChanged();
            EmptyHint = "RSS 未开启：到「设置 → 网址来源（RSS / Atom）」打开总开关，这里才会出现各个订阅源。";
        }
    }

    /// <summary>进入页面：按当前设置重读源列表（开关与源都可能在设置页刚被改动）。</summary>
    public void ReloadSources()
    {
        Enabled = _settings.LoadRssEnabled();
        var sources = _settings.LoadRssSources();
        if (!Enabled)
        {
            Sections.Clear();
            HasSectionsChanged();
            StructureChanged?.Invoke();
            return;
        }

        var keep = Sections.ToDictionary(s => s.Config.Id, s => s);
        Sections.Clear();
        foreach (var source in sources)
            Sections.Add(keep.TryGetValue(source.Id, out var old) ? old.WithConfig(source) : new RssSourceSection(source));
        HasSectionsChanged();
        StructureChanged?.Invoke();

        EmptyHint = Sections.Count == 0
            ? "还没有添加任何来源地址：到「设置 → 网址来源（RSS / Atom）」填一个订阅地址再回来。"
            : "还没有抓过。点上面的「刷新」抓一次——只有这一步会真的去访问那些地址。";
        StatusText = Sections.Count == 0
            ? string.Empty
            : $"{Sections.Count} 个来源，{Sections.Count(s => s.Config.Enabled)} 个启用中。点「刷新」看它们给了什么。";
    }

    /// <summary>「刷新」/「停止抓取」同一个按钮：正在抓时再点一次就是叫停。</summary>
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            _rounds?.Cancel();
            StatusText = "正在停止…（已经抓到的会留在上面）";
            return;
        }
        ReloadSources();
        if (!Enabled) return;
        if (Sections.Count == 0)
        {
            StatusText = "还没有添加任何来源地址。";
            return;
        }

        IsBusy = true;
        StatusText = "正在抓取…（再点一下可以停）";
        var cts = _rounds = new CancellationTokenSource(RoundBudget);
        try
        {
            var collected = await CollectedLinksAsync(cts.Token);
            using var client = new RssClient();
            var run = await RssAggregator.RunAsync(
                Sections.Select(s => s.Config).ToList(),
                async source =>
                {
                    cts.Token.ThrowIfCancellationRequested();      // 停手时别再开新请求
                    _validators.TryGetValue(source.Id, out var cached);
                    var fetched = await client.FetchAsync(source.Url, cached.Etag, cached.LastModified, cts.Token);
                    // 源没重发校验符时沿用旧的：否则下一次又退化成全量下载
                    _validators[source.Id] = (fetched.ETag ?? cached.Etag, fetched.LastModified ?? cached.LastModified);
                    return fetched;
                },
                cts.Token);

            foreach (var outcome in run.Outcomes)
            {
                var section = Sections.FirstOrDefault(s => s.Config.Id == outcome.Source.Id);
                if (section is null) continue;
                section.SetStatus(RssSourceStatus.Describe(outcome));
                // 只有源真的给了条目才换掉这一组的行：304 / 失败 / 没抓到时留着上一轮的结果，
                // 否则"源说没变化"（好消息）会把用户正在看的那一列清空，看着像坏了。
                if (outcome.Ok && outcome.Entries.Count > 0)
                    section.SetRows(outcome.Entries.Select(e => RowFor(e, collected)).ToList());
            }

            // 汇总说的是"这一页真的摆出来了多少条"，不是聚合器那份带 200 条上限的摊平清单：
            // 分组是按源各自取的，两个数不是一回事，拿后者报前者就会出现"页面上明明更多"。
            var shown = Sections.Sum(s => s.Rows.Count);
            StructureChanged?.Invoke();

            EmptyHint = shown == 0 ? "这一轮没有任何源给出条目，原因写在每个文件夹的标题上。" : string.Empty;
            StatusText = Summarise(run, shown);
        }
        catch (Exception ex)
        {
            StarLog.Error("[RSS] 刷新失败", ex);
            StatusText = "抓取没能完成：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            _rounds = null;
            cts.Dispose();
        }
    }

    /// <summary>离开页面只掐掉在途请求：已经抓到的那一轮结果留在单例 VM 上，回来不必重抓。</summary>
    public void CancelLoading() => _rounds?.Cancel();

    /// <summary>点条目 / 右键「打开」：直接跳文章。<b>打不开必须说出原因</b>（点了没反应是最难自证的一种失败）。</summary>
    public async Task OpenAsync(ItemCardViewModel vm)
    {
        var reason = await LauncherEx.TryOpenAsync(vm.Uri);
        if (reason is not null) StatusText = "没能打开：" + reason;
    }

    private static ItemCardViewModel RowFor(RssEntry entry, HashSet<string> collected)
    {
        var vm = new ItemCardViewModel(RssRowDraft.ForCandidate(entry));
        vm.SetCollected(collected.Contains(entry.Link.Trim()));
        return vm;
    }

    /// <summary>一轮下来给人看的那一句。<b>失败数与"没抓到"数分不开，用户就不知道该改地址还是该查网络</b>。</summary>
    private string Summarise(RssRunResult run, int shown)
    {
        var enabledCount = Sections.Count(s => s.Config.Enabled);
        var okCount = run.Outcomes.Count(o => o.Ok && o.Source.Enabled);
        var text = shown == 0
            ? $"{enabledCount} 个启用的源里有 {okCount} 个通了，但没有给出任何条目"
            : $"共 {shown} 条（{okCount} 个源给了内容）。点条目直接跳文章，点「收藏到文件夹」才进库。";
        if (run.FailedCount > 0) text += $"　{run.FailedCount} 个源失败，原因写在对应那一行";
        if (run.StoppedCount > 0) text += $"　已停止，还有 {run.StoppedCount} 个源没抓（再点一次接着抓）";
        return text;
    }

    /// <summary>
    /// 已经收藏过的链接。<b>读不出来的那一轮按"没收藏过"显示，但要把降级说出来</b>：
    /// 那颗按钮只是会重复点一次幂等的 upsert，而悄悄少报会让人以为自己的收藏丢了。
    /// </summary>
    private async Task<HashSet<string>> CollectedLinksAsync(CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var links = await App.Services.GetRequiredItemRepository().GetCollectedRssLinksAsync(ct);
            foreach (var link in links) set.Add(link);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[RSS] 读取已收藏清单失败，收藏状态按未收藏显示：{ex.Message}");
        }
        return set;
    }
}

/// <summary>
/// 页面上的"一个订阅源"＝一个文件夹：标题给源名、落点路径与这一轮的状态，下面是它给的条目。
/// <para><b>实现 <see cref="INotifyPropertyChanged"/></b>：抓取是在行铺好之后才回填状态的，
/// 不通知就出现"文件夹标题永远停在『还没抓过』"——点了按钮却没有回报，等于没做（批次 NF 同一教训）。</para>
/// </summary>
public sealed class RssSourceSection : INotifyPropertyChanged
{
    public RssSourceSection(RssSourceConfig config) => Config = config;

    public RssSourceConfig Config { get; private set; }

    public ObservableCollection<ItemCardViewModel> Rows { get; } = new();

    public string Name => Config.Name;
    public string Url => Config.Url;
    public bool Enabled => Config.Enabled;

    /// <summary>这一组的收藏落点（与真正写进库的那份 ExtraJson 同一个函数算出来，提示不会指错路径）。</summary>
    public string FolderPath => RssFolders.DisplayPath(Config.Name);

    public string Status { get; private set; } = RssSourceStatus.NeverFetched;

    /// <summary>条数只在真有内容时出现：空文件夹再标一个"共 0 条"就是噪音，状态那一行已经说清了原因。</summary>
    public string CountText => Rows.Count == 0 ? string.Empty : $"共 {Rows.Count} 条";

    /// <summary>源改了名/改了地址之后复用同一组行（配置是同一个 id）。</summary>
    public RssSourceSection WithConfig(RssSourceConfig config)
    {
        Config = config;
        RaiseAll();
        return this;
    }

    public void SetRows(IReadOnlyList<ItemCardViewModel> rows)
    {
        Rows.Clear();
        foreach (var row in rows) Rows.Add(row);
        Raise(nameof(CountText));
    }

    public void SetStatus(string status)
    {
        if (Status == status) return;
        Status = status;
        Raise();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAll()
    {
        foreach (var name in new[] { nameof(Name), nameof(Url), nameof(Enabled), nameof(FolderPath), nameof(CountText) })
            Raise(name);
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
