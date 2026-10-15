#nullable enable
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using StarMark.Abstractions;
using StarMark.Abstractions.Insights;
using StarMark.Abstractions.I18n;
using StarMark.Integrations.Clipboard;
using StarMark.Core.Backup;
using StarMark.Core.Hotkeys;
using StarMark.Core.Insights;
using StarMark.Core.Performance;
using StarMark.UI.Helpers;
using Windows.UI;

namespace StarMark.UI.ViewModels;

/// <summary>
/// SettingsPageViewModel 的这一段——健康度与诊断的读法：什么时候去问引擎、拿回来的东西怎么排给界面上那一行。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public partial class SettingsPageViewModel
{

    // ===== 收藏健康度（P2-6）=====
    [ObservableProperty] private HealthReport _healthReport = new();
    [ObservableProperty] private bool _isHealthLoading;
    [ObservableProperty] private bool _hasHealthError;
    [ObservableProperty] private string _healthError = string.Empty;
    [ObservableProperty] private string _scoreText = "—";
    [ObservableProperty] private LocMessage? _scoreGrade;

    /// <summary>屏幕上那一行念的是<b>现在这门语言</b>：槽里只存「键」，取词推迟到这一拍（✅片9d／P-203，与片9c 第二刀同一口径）。</summary>
    public string ScoreGradeText => ScoreGrade?.Render() ?? string.Empty;
    [ObservableProperty] private Brush _scoreBrush = new SolidColorBrush(Color.FromArgb(255, 16, 124, 16));
    [ObservableProperty] private string _scoreSummary = string.Empty;
    [ObservableProperty] private string _languageSummary = string.Empty;
    [ObservableProperty] private string _duplicateSummary = string.Empty;
    [ObservableProperty] private string _tagSummary = string.Empty;
    [ObservableProperty] private List<HealthTrendBar> _trendBars = new();
    // 四行"外壳＋读数"的整句（WI-32）：外壳住表、读数进实参——标记里不再留半句。
    [ObservableProperty] private List<string> _deductionLines = new();
    [ObservableProperty] private string _languageLine = string.Empty;
    [ObservableProperty] private string _domainsLine = string.Empty;
    [ObservableProperty] private string _duplicateLine = string.Empty;
    [ObservableProperty] private string _tagLine = string.Empty;

    // ⛔ 上面那九处显示属性存成品字：换语言那一刻没人回来重念，屏幕上就停在上一门的话（P-184 点名的就是这一族里
    // 那颗连接号——它当时有三处念法，每处都在"算出来那一瞬间"把语言定死）。存的换成「键＋实参」，
    // 取词一起推迟到 <see cref="PaintHealthReadouts"/> 那一拍；先例＝✅WI-40-3b 机制 (C)／✅片9d P-203。
    /// <summary>「待优化：{0}」那颗实参是一位 <see cref="LocJoin"/>：连起来与取词都在贴上去那一刻。</summary>
    private LocMessage? _scoreSummaryFact;
    /// <summary>扣分那一列：每行的因子名与明细都还是键，列子只在这那一拍才念成字符串。</summary>
    private IReadOnlyList<LocMessage> _deductionFacts = new List<LocMessage>();
    /// <summary>统计那三串的槽：<b>空态那句「（无）」住表</b>（所以槽型是 <c>object?</c>），
    /// 非空那串是「代码 数量」＋语言中性的「 · 」——它没有词可翻，⛔ 为它立第三颗分隔符表行（账本定死的口径）。</summary>
    private object? _languageSummaryFact;
    private object? _duplicateSummaryFact;
    private object? _tagSummaryFact;
    private LocMessage? _languageLineFact;
    private LocMessage? _domainsLineFact;
    private LocMessage? _duplicateLineFact;
    private LocMessage? _tagLineFact;

    /// <summary>拉取全部条目并构建健康度报告（P2-6）。本地聚合，零网络。</summary>
    public async Task LoadHealthAsync()
    {
        if (_repository is null) return;
        IsHealthLoading = true;
        HealthError = string.Empty;
        HasHealthError = false;
        try
        {
            // 整表读 + 全量健康度聚合都是同步跑完的（Microsoft.Data.Sqlite 无真异步 I/O），
            // 而这里是"进设置页"的直接 await 目标 ⇒ 不 offload 就是一次点设置页冻结整窗。
            // await 不加 ConfigureAwait(false)：Task.Run 之后仍回到 UI 线程刷 HealthReport/视图。
            var report = await Task.Run(async () =>
            {
                var items = await _repository.GetAllAsync(
                    new BrowseFilter { IncludeHidden = false, Limit = int.MaxValue }, CancellationToken.None);
                return InsightsService.BuildHealthReport(items);
            });
            HealthReport = report;
            RefreshHealthView();
        }
        catch (Exception ex)
        {
            HasHealthError = true;
            HealthError = Locale.Fmt("health.error", ex.Message);
            StarLog.Error($"健康度计算失败: {ex}");
        }
        finally { IsHealthLoading = false; }
    }

    private void RefreshHealthView()
    {
        var r = HealthReport;
        ScoreText = r.Score.ToString();
        ScoreGrade = r.Score >= 80 ? LocMessage.Of("health.grade.good") : r.Score >= 50 ? LocMessage.Of("health.grade.ok") : LocMessage.Of("health.grade.bad");
        ScoreBrush = r.Score >= 80
            ? new SolidColorBrush(Color.FromArgb(255, 16, 124, 16))   // 绿
            : r.Score >= 50
                ? new SolidColorBrush(Color.FromArgb(255, 214, 137, 16)) // 琥珀
                : new SolidColorBrush(Color.FromArgb(255, 196, 43, 28));  // 红

        _scoreSummaryFact = r.Factors.Count == 0
            ? LocMessage.Of("health.summary.good")
            : LocMessage.Of("health.summary.todo",
                new LocJoin(r.Factors.Select(f => (object?)f.Label).ToList(), "health.join.list"));
        // 扣分行整链（WI-33→P-184）：外壳、因子名、明细全部存「键＋实参」，念成字符串交给 PaintHealthReadouts 那一拍。
        _deductionFacts = r.Factors
            .Select(f => LocMessage.Of("health.deduction.row", f.Label, f.Deduction, f.Detail))
            .ToList();

        _languageSummaryFact = r.LanguageTop.Count == 0
            ? LocMessage.Of("health.stat.none")
            : string.Join(" · ", r.LanguageTop.Select(s => $"{s.Language} {s.Count}").Take(5));
        _duplicateSummaryFact = r.DuplicateTop.Count == 0
            ? LocMessage.Of("health.stat.none")
            // 「、」与那对括号的<b>宽度</b>都是语言内容（批次 WI-57 的普查尺量到）：连接号归
            // <c>health.join.list</c> ⇒ ⛔ 这里再抄一份（同一件事两个主人＝P-171 那一族），括号形状走表。
            // ⚠ 通用 en 列闸门看不见它——那颗只禁汉字与假名（坑表 #403 第二次兑现）。
            // ⛔ 在这里 string.Join：那会把连接号钉在"算出报告这一刻"，外层重贴只改外壳 ⇒ 交 LocJoin。
            : new LocJoin(r.DuplicateTop.Take(5)
                .Select(d => (object?)LocMessage.Of("health.dup.item", d.Title, d.Count)).ToList(), "health.join.list");
        _tagSummaryFact = r.TagHistogram.Count == 0
            ? LocMessage.Of("health.stat.none")
            : string.Join(" · ", r.TagHistogram.Select(t => $"{t.Tag} {t.Count}").Take(5));
        _languageLineFact = LocMessage.Of("health.stat.lang", _languageSummaryFact);
        _domainsLineFact = LocMessage.Of("health.stat.domains", r.DistinctDomains);
        _duplicateLineFact = LocMessage.Of("health.stat.dup", _duplicateSummaryFact);
        _tagLineFact = LocMessage.Of("health.stat.tag", _tagSummaryFact);

        var max = r.NewTrend.Count == 0 ? 0 : r.NewTrend.Max(t => t.Count);
        TrendBars = r.NewTrend
            .Select(t => new HealthTrendBar
            {
                Count = t.Count,
                DateLabel = t.Date,
                Height = max > 0 && t.Count > 0 ? Math.Max(3, t.Count * 80.0 / max) : 0,
            })
            .ToList();
        PaintHealthReadouts();
    }

    /// <summary>
    /// 把存着的事实按<b>此刻</b>的语言念成屏幕上那几行（批次 WI-68／P-184）。
    /// <para>那颗 <c>health.join.list</c> 全仓有<b>三处</b>念它：Core 的重复项明细、这里的"待优化"与
    /// "疑似重复 Top"——三处交的都是键，取词一起推迟到这一颗（⛔ 在 <see cref="RefreshHealthView"/> 里
    /// <c>string.Join(Locale.T(连接键), …)</c> 念完：那会把连接号钉在"算出它的那一瞬间"，外层重贴只改外壳，
    /// 屏幕上剩下「A、B, C」那种半中半英——<see cref="LocJoin"/> 的 doc-comment 逐字写过的那句）。</para>
    /// <para>这一拍<b>只重念已有</b>：⛔ 查库／重算分数／重建趋势柱（与 <see cref="PaintDiagnostics"/>
    /// 同一条界线，坑表 #386）。没采过时那些槽是 null／空表，念出来还是空串，⛔ 把"还没加载"刷成假读数。</para>
    /// <para>宿主自己订阅 <c>Locale.Changed</c> 并成对摘掉（<c>SettingsPage</c> 的 Loaded／Unloaded）——
    /// 这一页的节拍里没有"语言换了"这件事。</para>
    /// </summary>
    public void PaintHealthReadouts()
    {
        ScoreSummary = _scoreSummaryFact?.Render() ?? string.Empty;
        DeductionLines = _deductionFacts.Select(m => m.Render()).ToList();
        LanguageSummary = _languageSummaryFact?.ToString() ?? string.Empty;
        DuplicateSummary = _duplicateSummaryFact?.ToString() ?? string.Empty;
        TagSummary = _tagSummaryFact?.ToString() ?? string.Empty;
        LanguageLine = _languageLineFact?.Render() ?? string.Empty;
        DomainsLine = _domainsLineFact?.Render() ?? string.Empty;
        DuplicateLine = _duplicateLineFact?.Render() ?? string.Empty;
        TagLine = _tagLineFact?.Render() ?? string.Empty;
    }

    // ===== 诊断（P2-8）=====
    public System.Collections.ObjectModel.ObservableCollection<DiagnosticRow> DiagnosticEntries { get; }
        = new();

    /// <summary>
    /// 采回来的<b>事实</b>（每格都是键＋实参，还没取词）。
    /// <para>为什么要把事实存下来而不是在换语言时重采一次：<c>CollectAsync</c> 里有 <c>COUNT(*)</c>
    /// 全表扫描与逐表统计，而 <c>Locale.Changed</c> 那条订阅<b>只许做"把已经贴上去的那句重贴一遍"这一件事</b>
    /// （<see cref="Locale.Changed"/> 自己的注释就写着"重新查库不属于这里"）。</para>
    /// </summary>
    private IReadOnlyList<StarMark.Core.Diagnostics.DiagnosticEntry> _diagnosticFacts
        = System.Array.Empty<StarMark.Core.Diagnostics.DiagnosticEntry>();

    /// <summary>采集失败的那句话（同样存键）。读它之前先看 <see cref="HasDiagnosticsError"/>——这颗 null ⛔ 兼"有没有坏"的判据。</summary>
    private LocMessage? _diagnosticsFailure;

    [ObservableProperty] private bool _hasDiagnosticsError;
    [ObservableProperty] private string _diagnosticsError = string.Empty;

    /// <summary>
    /// 把存着的事实按<b>此刻</b>的语言贴成屏幕上那一行（批次 WI-57，P-187）。
    /// <para>这一颗是那一格<b>唯一</b>的取词点：采集与换语言两条路都走到这里，所以屏幕上不会出现
    /// "上一门的语言"。宿主自己订阅 <c>Locale.Changed</c> 并成对摘掉（<c>SettingsPage</c> 的 Loaded／Unloaded）——
    /// 这一页的节拍里没有"语言换了"这件事（坑表 #386 同口径）。</para>
    /// </summary>
    public void PaintDiagnostics()
    {
        DiagnosticEntries.Clear();
        foreach (var e in _diagnosticFacts)
            DiagnosticEntries.Add(new DiagnosticRow(e.Label.Render(), e.Value.Render()));
        DiagnosticsError = HasDiagnosticsError && _diagnosticsFailure is { } failure ? failure.Render() : string.Empty;
        OnPropertyChanged(nameof(ScoreGradeText));   // 那一行也归这一拍念：档位是"分数＋一颗键"，⛔ 存成品字（P-203）
    }

    /// <summary>
    /// 「剪贴板历史」总开关（默认关）。翻位即持久化 + <b>立刻</b>启停监听窗口，
    /// 并把"到底有没有开始记录"照实写进状态行——开成功与开失败必须区分得出来（P-53/P-54 同口径）。
    /// </summary>
    [ObservableProperty] private bool _clipboardHistoryEnabled;

    [ObservableProperty] private LocMessage? _clipboardHistoryStatus;

    /// <summary>LoadFromStore 回灌初值期间抑制启停动作，免得每次进设置页就凭默认值建/拆监听窗口。</summary>
    private bool _suppressClipboardApply;

    partial void OnClipboardHistoryEnabledChanged(bool value)
    {
        if (_suppressClipboardApply) return;
        ApplyClipboardHistorySwitch(value);
    }

    /// <summary>采集只读诊断信息（P2-8）。本地查询，零网络。<b>采的是事实，贴的是念法</b>（两件事分两步，见 <see cref="PaintDiagnostics"/>）。</summary>
    public async Task LoadDiagnosticsAsync()
    {
        if (_diagnostics is null) return;
        HasDiagnosticsError = false;
        try
        {
            // 诊断里含 COUNT(*) 全量扫描与逐表统计：同样是"进设置页即同步跑完"的站点，offload 后回 UI 填集合。
            _diagnosticFacts = await Task.Run(() => _diagnostics.CollectAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            HasDiagnosticsError = true;
            _diagnosticsFailure = LocMessage.Of("diag.collectFailed", ex.Message);
            StarLog.Error($"诊断信息采集失败: {ex}");
        }
        PaintDiagnostics();
    }
}

/// <summary>
/// 诊断面板在屏幕上的一格（批次 WI-57，P-187）。
/// <para><b>它与 <c>DiagnosticEntry</c> 的分别是"什么时候取词"</b>：那颗存键＋实参（可以放着不动），
/// 这一颗存的是<b>贴上去那一刻</b>按当前语言取出来的两串字。中间这一步只许住在
/// <c>PaintDiagnostics</c> 里——把成品字存回字段＝屏幕上那一行停在旧语言，而通用表闸门看不见
/// （被问的键没变，变的是它什么时候被念出来；坑表 #361／#372 同一族）。</para>
/// </summary>
public sealed record DiagnosticRow(string Label, string Value);
