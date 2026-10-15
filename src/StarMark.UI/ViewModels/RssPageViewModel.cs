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
using StarMark.Abstractions.I18n;
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
/// <b>打开这一页先摆缓存，不联网</b>（用户裁决"每天仅刷新一次"）：条目与校验符落在
/// <c>rss-cache.json</c>，进页面直接读回来；只有<b>过了 <see cref="RssFeedCache.AutoRefreshGap"/> 的源</b>
/// 才会被自动补抓一轮，其余要用户点「刷新」才动。「刷新」＝强制一轮（无视到期判断）。
/// </para>
/// <para>
/// 与自动刷新分不开的一句：<b>抓回来的条目是"并进"缓存而不是盖掉</b>（<see cref="RssFeedCache.Merge"/>），
/// 所以增量刷新之后用户看到的仍是同一列，不会因为他正指着的那条突然跳走而点错。
/// </para>
/// <para>
/// 这一页刻意<b>没有搜索、没有排序</b>（用户裁决）：条目按源分组、组内按源给的顺序，
/// 右键也只是主窗右键的一个子集（打开 / 复制 / 收藏）。要做筛选去「文件夹」页——那里已经是本机的数据了。
/// </para>
/// </summary>
public partial class RssPageViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly RssCacheStore _cache;

    /// <summary>
    /// 缓存的那一份（条目 + 校验符 + 上次成功时刻）。<b>一轮只读一次、只写一次</b>：
    /// 逐源读写会随源数线性放大，而校验符与条目本来就是"一轮"这个整体的一部分。
    /// </summary>
    private RssCacheFile _file = new();

    /// <summary>
    /// 已经收进库的那些链接（用来标"这一条收藏过了"）。<b>进页面读一次，一轮内不改</b>：
    /// 逐行查库就是"每项一趟往返"那个形状（批次 PA），而收藏动作自己会更新它改动的那一行。
    /// </summary>
    private HashSet<string> _collected = new();

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
    [ObservableProperty] private LocMessage? _statusText;
    [ObservableProperty] private LocMessage? _emptyHint;

    public RssPageViewModel()
    {
        _settings = App.Services.GetRequiredService<SettingsStore>();
        _cache = App.Services.GetRequiredService<RssCacheStore>();
        Enabled = _settings.LoadRssEnabled();
        // 收藏结果与"打不开"的原因都落在这一行的状态上：动作没有回显，用户只能靠列表有没有变来猜。
        RssItemActions.NoticeRaised += m => StatusText = m;
    }

    public bool HasSections => Sections.Count > 0;

    /// <summary>状态行有没有内容（没有时整行不占位，避免一屏空白）。</summary>
    public bool HasStatus => StatusText is not null;

    partial void OnStatusTextChanged(LocMessage? value) => OnPropertyChanged(nameof(HasStatus));

    /// <summary>同一个按钮在抓取期间就是「停止抓取」：另起一个按钮要多占一处布局，而用户要的只是"别等了"。
    /// <b>这里交回键</b>（批次 WI-2k）：那两个词都是表里的行，页面按这颗状态推给 <c>LocText</c>，
    /// 换语言那一刻跟着重贴——留在 VM 里拼成字符串就等于把语言定死在这一次。</summary>
    public LocMessage RefreshLabel => IsBusy ? LocMessage.Of("rss.stop") : LocMessage.Of("dialog.refresh");

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(RefreshLabel));

    private void HasSectionsChanged() => OnPropertyChanged(nameof(HasSections));

    partial void OnEnabledChanged(bool value)
    {
        if (!value)
        {
            Sections.Clear();
            HasSectionsChanged();
            EmptyHint = LocMessage.Of("rss.emptyDisabled");
        }
    }

    /// <summary>
    /// 进入页面：按当前设置重读源列表（开关与源都可能在设置页刚被改动），并把<b>本机缓存</b>的那一份摆出来。
    /// <para>这一步刻意不联网：用户裁决"每天仅刷新一次"，而打开页面不等于想看新东西——
    /// 先看上次抓到的，缺不缺新的由 <see cref="PrimeAsync"/> 按到期判断决定。</para>
    /// </summary>
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

        _file = _cache.Load();
        var keep = Sections.ToDictionary(s => s.Config.Id, s => s);
        Sections.Clear();
        foreach (var source in sources)
        {
            var section = keep.TryGetValue(source.Id, out var old) ? old.WithConfig(source) : new RssSourceSection(source);
            Sections.Add(section);
            ShowCached(section);
        }
        HasSectionsChanged();
        DropCachesOfDeletedSources(sources);
        StructureChanged?.Invoke();

        // 这一句只在"一个源都没有"时看得见（页面上的空态块按 HasSections 反向显示），
        // 所以它只说这一种情况；有源但还没抓到东西时该说什么，由下面的 StatusText 负责（它永远在）。
        EmptyHint = Sections.Count == 0
            ? LocMessage.Of("rss.emptyNoSources")
            : null;
        var due = DueCount();
        StatusText = Sections.Count == 0
            ? null
            : due > 0
                ? LocMessage.Of("rss.statusCacheDue", Sections.Count,
                    Sections.Count(s => s.Config.Enabled), due, RssFeedCache.GapMessage)
                : LocMessage.Of("rss.statusCacheFresh", Sections.Count,
                    Sections.Count(s => s.Config.Enabled), RssFeedCache.GapMessage);
    }

    /// <summary>把某个源的缓存行与"上次抓取/什么时候再自动刷新"摆到它那一行上。</summary>
    private void ShowCached(RssSourceSection section)
    {
        var cached = _file.Find(section.Config.Id);
        if (cached is null || cached.Entries.Count == 0)
        {
            section.SetRows(Array.Empty<ItemCardViewModel>());
            section.SetStatus(RssSourceStatus.NeverFetched);
            return;
        }
        section.SetRows(RssFeedCache.ToEntries(cached, section.Config).Select(e => RowFor(e, _collected)).ToList());
        section.SetStatus(RssSourceStatus.FromCache(cached, NowUnix));
    }

    /// <summary>
    /// 源被删掉之后把它的缓存一起清掉：这一档会随源数长大，留着没人再读的条目就是只增不减的磁盘占用。
    /// <para>只在真的少了东西时才落盘——否则每次进这一页都白写一次文件。</para>
    /// </summary>
    private void DropCachesOfDeletedSources(IReadOnlyList<RssSourceConfig> sources)
    {
        if (_file.Sources.Count == 0) return;
        var before = _file.Sources.Count;
        _cache.Without(_file, sources.Select(s => s.Id));
        if (_file.Sources.Count != before) _cache.Save(_file);
    }

    private int DueCount() => Sections.Count(s => s.Config.Enabled && RssFeedCache.IsDue(_file.Find(s.Config.Id), NowUnix));

    private static long NowUnix => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>
    /// 页面进来的那一次：<b>只补抓到期的源</b>（一天一次），其余连请求都不发。
    /// <para>订阅地址是用户填的第三方站点，自作主张地反复访问＝替用户向外部服务承诺了他没同意的流量；
    /// 而他的抱怨是"每次刷新极为缓慢"，所以自动那一档压到一天一次，要立刻看新的仍然由「刷新」按钮明说。</para>
    /// </summary>
    public async Task PrimeAsync()
    {
        ReloadSources();
        // IsBusy 那一句挡的是"上一轮还在跑就又开一轮"：切走时只叫停了请求，那一轮的收尾（并缓存、落盘）还在 await 之后，
        // 两轮同时在写同一份档就是互相盖。这一页该摆的缓存上面那次 ReloadSources 已经摆出来了，不缺这一轮的画面。
        if (!Enabled || Sections.Count == 0 || IsBusy) return;
        await StartRoundAsync(auto: true);
    }

    /// <summary>「刷新」/「停止抓取」同一个按钮：正在抓时再点一次就是叫停。</summary>
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            _rounds?.Cancel();
            StatusText = LocMessage.Of("rss.stopping");
            return;
        }
        ReloadSources();
        if (!Enabled) return;
        if (Sections.Count == 0)
        {
            StatusText = LocMessage.Of("rss.noSources");
            return;
        }
        await StartRoundAsync(auto: false);
    }

    /// <summary>
    /// 开一轮的唯一闸门（自动补抓与手动「刷新」都从这里进）。<b>IsBusy 与那枚 CTS 都在第一个 await 之前置好</b>，
    /// 各挡一种真机现象：
    /// ① 连点两下「刷新」：读库那几毫秒里如果还没占住名额，第二击会通过"没在忙"的判断开出<b>第二轮</b>——
    /// 两轮并写同一份缓存，且 <c>_rounds</c> 被后一个盖掉，前一个再也停不了；
    /// ② "按了停止没反应"：停止那一下必须有东西可掐，所以 CTS 在这一段就建好，哪怕还停在读库这一步
    /// （掐了之后聚合器把每个源都记成"没抓到"，已抓到的照样留下）。
    /// </summary>
    private async Task StartRoundAsync(bool auto)
    {
        IsBusy = true;
        var cts = _rounds = new CancellationTokenSource(RoundBudget);
        try
        {
            // 库里那份"已经收藏过的链接"是个本地读，不参与取消（半途掐它只会把收藏状态标错）
            _collected = await CollectedLinksAsync(CancellationToken.None);
            foreach (var section in Sections) ShowCached(section);   // 收藏状态要等这一步读出来才标得对
            StructureChanged?.Invoke();
            await RunRoundAsync(auto, cts.Token);
        }
        catch (Exception ex)
        {
            StarLog.Error("[RSS] 刷新失败", ex);
            StatusText = LocMessage.Of("rss.roundFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            _rounds = null;
            cts.Dispose();
        }
    }

    /// <summary>
    /// 跑一轮。<paramref name="auto"/> 为真＝进页面时的自动补抓，只带上<b>到期的</b>那些源；
    /// 为假＝用户按了「刷新」，全部启用的源都去问一次（条件请求，源说没变化就只花一个来回）。
    /// </summary>
    private async Task RunRoundAsync(bool auto, CancellationToken ct)
    {
        var targets = Sections
            .Where(s => s.Config.Enabled && (!auto || RssFeedCache.IsDue(_file.Find(s.Config.Id), NowUnix)))
            .ToList();
        if (targets.Count == 0) return;

        StatusText = auto
            ? LocMessage.Of("rss.fetchingDue", targets.Count, RssFeedCache.GapMessage)
            : LocMessage.Of("rss.fetchingAll", targets.Count);

        // 校验符的"上一次是什么"在这一轮开始时定死（读 _file），抓回来的新值只在轮末串行落盘：
        // 抓取那一路是并发的，Dictionary 不能一边被读一边被写。
        var validators = targets.ToDictionary(s => s.Config.Id, s => _file.Find(s.Config.Id));
        using var client = new RssClient();
        var run = await RssAggregator.RunAsync(
            targets.Select(s => s.Config).ToList(),
            async source =>
            {
                ct.ThrowIfCancellationRequested();                      // 停手时别再开新请求
                var known = validators[source.Id];
                return await client.FetchAsync(source.Url, known?.Etag, known?.LastModified, ct);
            },
            ct);

        var now = NowUnix;
        var added = 0;
        foreach (var outcome in run.Outcomes)
        {
            var section = Sections.FirstOrDefault(s => s.Config.Id == outcome.Source.Id);
            if (section is null) continue;
            var cached = _file.Find(outcome.Source.Id);
            if (!outcome.Ok)
            {
                // 失败/被停掉：既不建空缓存项（那会随坏源攒一堆没人读的条目），也<b>不推进</b> FetchedAtUnix
                // ——否则"每天一次"会把一个只坏了十分钟的源按住一整天。
                section.SetStatus(RssSourceStatus.Describe(outcome, cached?.Entries.Count));
                continue;
            }
            if (cached is null)
            {
                cached = new RssCachedSource { SourceId = outcome.Source.Id };
                _file.Sources.Add(cached);
            }
            // 源没重发校验符时沿用旧的：否则下一次又退化成全量下载
            cached.Etag = outcome.Etag ?? cached.Etag;
            cached.LastModified = outcome.LastModified ?? cached.LastModified;
            if (outcome.NotModified)
            {
                cached.FetchedAtUnix = now;                        // 通了、只是没变化：这一天的额度算用掉了
                section.SetStatus(RssSourceStatus.Describe(outcome, cached.Entries.Count));
            }
            else
            {
                added += RssFeedCache.Merge(cached, outcome.Entries, now);
                // 行取自<b>合并后的缓存</b>（不是这一轮的条目）：增量刷新之后用户看到的仍是同一列
                section.SetRows(RssFeedCache.ToEntries(cached, outcome.Source).Select(e => RowFor(e, _collected)).ToList());
                section.SetStatus(RssSourceStatus.Describe(outcome, cached.Entries.Count));
            }
        }

        // 一轮一次落盘（逐源写会随源数线性放大，而且中途崩溃会留下半新半旧的一档）
        var saved = _cache.Save(_file);

        // 汇总说的是"这一页真的摆出来了多少条"，不是聚合器那份带 200 条上限的摊平清单：
        // 分组是按源各自取的，两个数不是一回事，拿后者报前者就会出现"页面上明明更多"。
        var shown = Sections.Sum(s => s.Rows.Count);
        StructureChanged?.Invoke();

        // 这里刻意不写 EmptyHint：页面上那块空态按"有没有源"显示，有源时它根本不可见，
        // 而"哪几个源没给条目"已经逐行写在标题上了（写在看不见的地方＝给后来人撒假线索）。
        StatusText = saved
            ? Summarise(run, shown, added, auto)
            : LocMessage.Of("rss.statusJoin", Summarise(run, shown, added, auto), LocMessage.Of("rss.cacheWriteFailed"));
    }

    /// <summary>离开页面只掐掉在途请求：已经抓到的那一轮结果留在单例 VM 上，回来不必重抓。</summary>
    public void CancelLoading() => _rounds?.Cancel();

    /// <summary>点条目 / 右键「打开」：直接跳文章。<b>打不开必须说出原因</b>（点了没反应是最难自证的一种失败）。</summary>
    public async Task OpenAsync(ItemCardViewModel vm)
    {
        var reason = await LauncherEx.TryOpenAsync(vm.Uri);
        if (reason is not null) StatusText = LocMessage.Of("rss.openFailed", reason);
    }

    private static ItemCardViewModel RowFor(RssEntry entry, HashSet<string> collected)
    {
        var vm = new ItemCardViewModel(RssRowDraft.ForCandidate(entry));
        vm.SetCollected(collected.Contains(entry.Link.Trim()));
        return vm;
    }

    /// <summary>
    /// 一轮下来给人看的那一句。<b>失败数与"没抓到"数分不开，用户就不知道该改地址还是该查网络</b>。
    /// <para>自动那一轮必须自己说清楚"这页为什么只显示这些"：<see cref="RssFeedCache.AutoRefreshGap"/>
    /// 之外的那些源这一轮根本没被问，报告只写"共 N 条"就会被当成实时结果，看不到新文章就判定功能坏了。</para>
    /// </summary>
    private LocMessage Summarise(RssRunResult run, int shown, int added, bool auto)
    {
        var okCount = run.Outcomes.Count(o => o.Ok);
        var text = shown == 0
            ? LocMessage.Of("rss.summaryNone", run.Outcomes.Count, okCount)
            : auto
                ? LocMessage.Of("rss.summaryAuto", RssFeedCache.GapMessage, run.Outcomes.Count, added, shown)
                : LocMessage.Of("rss.summaryManual", shown, okCount, added);
        // 附加那两句各是一整行表里的话，用同一个连接句拼：⛔ 不许在这里 += 字符串（那又把语言定死了）。
        if (run.FailedCount > 0) text = LocMessage.Of("rss.statusJoin", text, LocMessage.Of("rss.summaryFailed", run.FailedCount));
        if (run.StoppedCount > 0) text = LocMessage.Of("rss.statusJoin", text, LocMessage.Of("rss.summaryStopped", run.StoppedCount));
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

    /// <summary>这一行"这一轮怎么样了"——交的是<b>没取词的</b>「键＋实参」：那一行由宿主 <c>LocText.SetMessage</c> 贴，
    /// 所以换语言时已经铺出来的那一行会跟着换（✅WI-65 片2；⛔ 在这里就 <c>Render()</c> 一次）。</summary>
    public LocMessage Status { get; private set; } = RssSourceStatus.NeverFetched;

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
    }

    public void SetStatus(LocMessage status)
    {
        // ⚠ 这里从前写着 <c>if (Status == status) return;</c>——那颗短路换型之后⛔ 搬得过来：
        // <c>LocMessage</c> 是 record 而 <c>Args</c> 是数组，合成相等对数组只比引用 ⇒ 同键同实参的两句永远不相等（坑表 #480）；
        // 而改成「比 <c>Render()</c> 后的句子」就把一次取词放进了界面那一路——那颗闸门（<c>I18nWiringGateTests</c>
        // 的 <c>NestedReadingsArePassedUnrendered</c>）钉的正是「这一档 <c>.Render()</c> 命中 0」。
        // 这一格本来也只是给"<c>Status</c> 被绑出去"那种宿主用的，而这一页的表头是 code-behind 现建的（没人订这一颗事件），
        // 所以宁可让它每次都报一声，⛔ 为了省一次通知在链上先念一遍词。
        Status = status;
        Raise();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAll()
    {
        foreach (var name in new[] { nameof(Name), nameof(Url), nameof(Enabled), nameof(FolderPath) })
            Raise(name);
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
