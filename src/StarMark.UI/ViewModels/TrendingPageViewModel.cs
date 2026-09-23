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
    private const string AllLanguages = "全部";

    private readonly TrendingService _service;
    private readonly IItemRepository _repository;
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
    [ObservableProperty] private string _language = string.Empty;
    [ObservableProperty] private string _filter = string.Empty;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _failed;
    [ObservableProperty] private bool _isStale;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _emptyHint = string.Empty;

    /// <summary>有没有配 Token：只决定那一行提示与「去设置配 Token」按钮，<b>不</b>决定 Star 按钮能不能点。</summary>
    [ObservableProperty] private bool _hasToken;

    public TrendingPageViewModel()
    {
        _service = App.Services.GetRequiredService<TrendingService>();
        _repository = App.Services.GetRequiredItemRepository();
        _settings = App.Services.GetRequiredService<SettingsStore>();
        _githubOptions = App.Services.GetRequiredService<GitHubOptions>();

        Enabled = _settings.LoadTrendingEnabled();
        PeriodCode = TrendingPeriods.Code(_settings.LoadTrendingPeriod());
        Language = _settings.LoadTrendingLanguage();
        HasToken = !string.IsNullOrWhiteSpace(_githubOptions.Token);

        // 动作结果的唯一出口：主窗按钮、卡片右键、组件右键三处都广播到这里，页面只订阅一处。
        TrendingItemActions.NoticeRaised += m => StatusText = m;
    }

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);
    public bool HasRows => Rows.Count > 0;

    /// <summary>没配 Token 时才提示（配了就不占一行）；按钮永远可用，点了会说要配什么。</summary>
    public bool NeedsTokenHint => HasToken == false;

    public bool IsBusy => IsLoading;

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsBusy));

    partial void OnPeriodCodeChanged(string value) { _ = ReloadAsync(force: false); }
    partial void OnLanguageChanged(string value) { _ = ReloadAsync(force: false); }
    partial void OnFilterChanged(string value) => RebuildRows();
    partial void OnEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(NeedsTokenHint));
        if (!value) { Rows.Clear(); HasRowsChanged(); }
    }
    partial void OnHasTokenChanged(bool value) => OnPropertyChanged(nameof(NeedsTokenHint));

    /// <summary>
    /// 语言下拉数据源：「全部」+ 库里 star 条目真实出现过的语言（复用既有统计，不再写一套）。
    /// 下拉可编辑 ⇒ 用户能手输任何语言，不受本机有没有 star 过该语言限制。
    /// </summary>
    public async Task<IReadOnlyList<string>> LoadLanguageOptionsAsync()
    {
        var list = new List<string> { AllLanguages };
        try
        {
            foreach (var lang in await _repository.GetStarLanguagesAsync(CancellationToken.None))
                if (!list.Any(x => string.Equals(x, lang, StringComparison.OrdinalIgnoreCase))) list.Add(lang);
        }
        catch (Exception ex) { StarLog.Warn($"读取本机 star 语言列表失败，热榜语言筛选只留「全部」：{ex.Message}"); }
        return list;
    }

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
            StatusText = string.Empty;
            EmptyHint = "热榜未开启：到「设置 → GitHub 热榜」打开后，这里才会抓取 GitHub 热榜。";
            return;
        }

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        IsLoading = true;
        Failed = false;
        StatusText = string.Empty;
        HasToken = !string.IsNullOrWhiteSpace(_githubOptions.Token);
        try
        {
            var period = TrendingPeriods.TryParse(PeriodCode, out var p) ? p : TrendingPeriod.Weekly;
            var result = await _service.GetAsync(period, string.IsNullOrWhiteSpace(Language) ? null : Language.Trim(),
                force, cts.Token);
            _lastResult = result;
            _last = result.Repos;
            RebuildRows();
            StatusText = Describe(result);
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消本次抓取，列表保持原样。";
        }
        catch (Exception ex)
        {
            // 失败必须带原因且留着「重试」；把 Rows 清空是刻意的：留着上一次的列表又不标 stale，
            // 用户会以为那是本期结果。
            Failed = true;
            Rows.Clear();
            HasRowsChanged();
            EmptyHint = $"抓取失败：{ex.Message}";
            StatusText = "抓取失败，下面给出的是原因；点「重试」可以再抓一次。";
            StarLog.Warn($"热榜页抓取失败（{PeriodCode}/{Language}）：{ex.Message}");
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
            ? "本期没有拿到候选。可能是 GitHub 热榜页暂时不可用——点「重试」换一次试试。"
            : Rows.Count == 0
                ? $"本期 {(_last.Count)} 个候选里没有匹配「{needle}」的（标题 / 描述 / 语言）。清空筛选即可看全部。"
                : string.Empty;

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
    private static string Describe(TrendingResult r)
    {
        var via = r.Via switch
        {
            TrendingSource.SearchApi => "来源：GitHub 搜索接口兜底（没有“本期新增星数”）",
            TrendingSource.TrendingHtml => "来源：GitHub 热榜页",
            _ => "来源：上次缓存（未记录来源）",
        };
        var age = TrendingCacheCodec.DescribeAge(r.FetchedAt, DateTimeOffset.Now);
        var head = r.Stale
            ? $"抓取失败·已显示上次结果（{age}）"
            : r.FromCache ? $"{via}·{age}已缓存" : via + (age.Length > 0 ? $"·{age}" : "");
        return r.Notice is { Length: > 0 } notice ? head + "｜" + notice : head;
    }

    private void HasRowsChanged() => OnPropertyChanged(nameof(HasRows));
}
