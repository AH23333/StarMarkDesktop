#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Abstractions.Trending;
using StarMark.Core.Trending;
using StarMark.Integrations.GitHub;
using StarMark.UI.Helpers;
using StarMark.UI.Services;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 「热榜」页 ViewModel：展示 GitHub 热榜候选，行数据<b>不入库</b>。
/// <para>
/// 四态各自带下一步，且<b>空与失败必须分得开</b>（把抓取失败显示成空列表，就是让人去查一个不存在的问题）：
/// 加载中 / 空（"本期没有候选" + 刷新）/ 失败（带原因 + 「重试」+「停止」）/ stale（"上次结果，N 小时前" + 「重试」）。
/// </para>
/// <para>
/// 状态文本刻意做成"来源标注 + 时刻 + 提示"三段合一的一句话：兜底来源（Search API）与热榜页给出的字段不同
/// （没有"本期新增星数"），不标注就会让人以为榜单本身长这样。
/// </para>
/// </summary>
public partial class TrendingPageViewModel : ObservableObject
{
    private readonly TrendingService _service;
    private readonly SettingsStore _settings;
    private readonly GitHubOptions _githubOptions;

    /// <summary>未过滤的原始结果（页内过滤在内存做：这批数据不落库，也就进不了统一搜索）。</summary>
    private IReadOnlyList<TrendingRepo> _last = Array.Empty<TrendingRepo>();
    private TrendingResult? _lastResult;
    private CancellationTokenSource? _cts;

    public ObservableCollection<ItemCardViewModel> Rows { get; } = new();

    /// <summary>功能开关（关时导航项本身不显示；这里兜住"设置里刚关掉但页面还开着"）。</summary>
    [ObservableProperty] private bool _enabled;

    [ObservableProperty] private string _periodCode = TrendingPeriods.Code(TrendingPeriod.Weekly);
    [ObservableProperty] private string _filter = string.Empty;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _failed;
    [ObservableProperty] private bool _isStale;
    /// <summary>状态行（来源标注／时刻／动作结果）同 <see cref="_emptyHint"/>：交回键＋实参，⛔ 不在这里拼句子。</summary>
    [ObservableProperty] private LocMessage? _statusText;
    /// <summary>空态／失败态那一行：交回<b>键＋实参</b>而不是拼好的句子（批次 WI-2k），
    /// 换语言那一刻由 <c>LocText</c> 按新语言重算。⛔ 这里拼字符串＝把语言定死在上一次算的那一刻。</summary>
    [ObservableProperty] private LocMessage? _emptyHint;

    /// <summary>有没有配 Token：只决定那一行提示与「去设置配 Token」按钮，<b>不</b>决定 Star 按钮能不能点。</summary>
    [ObservableProperty] private bool _hasToken;

    /// <summary>
    /// 回灌初值期间抑制"筛选变了就重载"。没有这个闸门，构造里给 PeriodCode/Language 赋值就会
    /// 立刻发起一次抓取，紧接着页面 OnNavigatedTo 又发一次 ⇒ 两次并发、前一次被取消，
    /// 状态行还会被"已取消本次抓取"盖掉真正的原因（与设置页 _suppressTrendingApply 同一口径）。
    /// </summary>
    private bool _suppressReload = true;

    public TrendingPageViewModel()
    {
        _service = App.Services.GetRequiredService<TrendingService>();
        _settings = App.Services.GetRequiredService<SettingsStore>();
        _githubOptions = App.Services.GetRequiredService<GitHubOptions>();

        Enabled = _settings.LoadTrendingEnabled();
        PeriodCode = TrendingPeriods.Code(_settings.LoadTrendingPeriod());
        HasToken = !string.IsNullOrWhiteSpace(_githubOptions.Token);
        _suppressReload = false;

        // 动作结果的唯一出口：主窗按钮、卡片右键、组件右键三处都广播到这里，页面只订阅一处。
        TrendingItemActions.NoticeRaised += m => StatusText = m;
    }

    public bool HasStatus => StatusText is not null;
    public bool HasRows => Rows.Count > 0;

    /// <summary>没配 Token 时才提示（配了就不占一行）；按钮永远可用，点了会说要配什么。</summary>
    public bool NeedsTokenHint => HasToken == false;

    public bool IsBusy => IsLoading;

    partial void OnStatusTextChanged(LocMessage? value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsBusy));

    partial void OnPeriodCodeChanged(string value)
    {
        if (!_suppressReload) _ = ReloadAsync(force: false);
    }

    partial void OnFilterChanged(string value) => RebuildRows();
    partial void OnEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(NeedsTokenHint));
        if (!value) { Rows.Clear(); HasRowsChanged(); }
    }
    partial void OnHasTokenChanged(bool value) => OnPropertyChanged(nameof(NeedsTokenHint));

    /// <summary>页面进入 / 筛选变化：走缓存短路（同一本地日历日不重复抓）。</summary>
    public Task ReloadAsync(bool force) => LoadAsync(force);

    /// <summary>「停止」：抓取在途时把这次请求掐掉，列表保持原样——取消不等于失败，不许走兜底。</summary>
    public void CancelLoading() => _cts?.Cancel();

    public async Task LoadAsync(bool force)
    {
        if (!Enabled)
        {
            Rows.Clear();
            HasRowsChanged();
            Failed = false;
            IsStale = false;
            StatusText = null;
            EmptyHint = LocMessage.Of("trending.emptyDisabled");
            return;
        }

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        IsLoading = true;
        Failed = false;
        StatusText = null;
        HasToken = !string.IsNullOrWhiteSpace(_githubOptions.Token);
        try
        {
            var period = TrendingPeriods.TryParse(PeriodCode, out var p) ? p : TrendingPeriod.Weekly;
            // 语言固定给 null＝不筛：v1 曾给过语言下拉，但候选与本机已 star 的语言混在一起会让人误判（用户裁决删除）。
            var result = await _service.GetAsync(period, language: null, force, cts.Token);
            _lastResult = result;
            _last = result.Repos;
            RebuildRows();
            StatusText = Describe(result);
        }
        catch (OperationCanceledException)
        {
            StatusText = LocMessage.Of("trending.statusCancelled");
        }
        catch (Exception ex)
        {
            // 失败必须带原因且留着「重试」；把 Rows 清空是刻意的：留着上一次的列表又不标 stale，
            // 用户会以为那是本期结果。
            Failed = true;
            Rows.Clear();
            HasRowsChanged();
            EmptyHint = TrendingFailure.Of(ex);            // 两条腿都失败时那句已在 Core 外面拼好（交的是键），⛔ 在这里再套一层
            StatusText = LocMessage.Of("trending.statusFailed");
            StarLog.Warn($"热榜页抓取失败（{PeriodCode}）：{ex.Message}");
        }
        finally
        {
            IsLoading = false;
            if (ReferenceEquals(_cts, cts)) _cts = null;
        }
    }

    /// <summary>把 _last（含页内过滤）铺成卡片行，并回填「已 Star / 已收藏」两态。</summary>
    private void RebuildRows()
    {
        var period = TrendingPeriods.TryParse(PeriodCode, out var p) ? p : TrendingPeriod.Weekly;
        var via = _lastResult?.Via ?? TrendingSource.TrendingHtml;
        var needle = Filter?.Trim() ?? string.Empty;

        Rows.Clear();
        foreach (var repo in _last)
        {
            if (needle.Length > 0 && !Matches(repo, needle)) continue;
            Rows.Add(new ItemCardViewModel(TrendingRowDraft.ForRow(repo, period, via)));
        }
        HasRowsChanged();
        EmptyHint = _last.Count == 0
            ? LocMessage.Of("trending.emptyNoCandidates")
            : Rows.Count == 0
                ? LocMessage.Of("trending.emptyNoMatch", _last.Count, needle)
                : null;

        _ = RefreshRowStatesAsync();
    }

    private static bool Matches(TrendingRepo repo, string needle)
        => repo.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase)
           || repo.Description.Contains(needle, StringComparison.OrdinalIgnoreCase)
           || (repo.Language?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>回填两态（读本机已同步的 star / 书签，不发探测请求）。</summary>
    private async Task RefreshRowStatesAsync()
    {
        try { await TrendingItemActions.RefreshStatesAsync(Rows.ToList(), CancellationToken.None); }
        catch (Exception ex) { StarLog.Warn($"热榜行状态回填失败（按钮仍可用）：{ex.Message}"); }
    }

    /// <summary>
    /// 来源标注：一句话讲清"这批数据来自哪条腿、什么时候、有没有降级"。
    /// 兜底与 stale 都必须显式——它们都是"看着正常但语义不同"的状态。
    /// </summary>
    private static LocMessage Describe(TrendingResult r)
    {
        var via = r.Via switch
        {
            TrendingSource.SearchApi => LocMessage.Of("trending.viaSearchApi"),
            TrendingSource.TrendingHtml => LocMessage.Of("trending.viaTrendingPage"),
            _ => LocMessage.Of("trending.viaCacheUnknown"),
        };
        // 读数句子交的也是键＋实参：它们由 LocMessage.ToString 在<b>贴上去的那一刻</b>才取词，
        // 所以换语言重贴时整句（含"N 分钟前"）一起改口。
        var age = TrendingCacheCodec.DescribeAge(r.FetchedAt, DateTimeOffset.Now);
        var head = r.Stale
            ? LocMessage.Of("trending.statusStale", age)
            : r.FromCache ? LocMessage.Of("trending.statusCached", via, age)
            : age is null ? via : LocMessage.Of("trending.statusViaAge", via, age);
        // Notice 交的是键（批次 WI-58／P-190）：套进外层那句时⛔ 在这里 Render——
        // 抓取可能在中文档、这一句可能在换语言之后才贴，取词必须留在贴上去那一刻。
        return r.Notice is { } notice ? LocMessage.Of("trending.statusNotice", head, notice) : head;
    }

    private void HasRowsChanged() => OnPropertyChanged(nameof(HasRows));
}
