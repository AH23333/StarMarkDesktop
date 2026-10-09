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
    [ObservableProperty] private string _scoreGrade = string.Empty;
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
        ScoreGrade = r.Score >= 80 ? Locale.T("health.grade.good") : r.Score >= 50 ? Locale.T("health.grade.ok") : Locale.T("health.grade.bad");
        ScoreBrush = r.Score >= 80
            ? new SolidColorBrush(Color.FromArgb(255, 16, 124, 16))   // 绿
            : r.Score >= 50
                ? new SolidColorBrush(Color.FromArgb(255, 214, 137, 16)) // 琥珀
                : new SolidColorBrush(Color.FromArgb(255, 196, 43, 28));  // 红

        ScoreSummary = r.Factors.Count == 0
            ? Locale.T("health.summary.good")
            : Locale.Fmt("health.summary.todo", string.Join(Locale.T("health.join.list"), r.Factors.Select(f => f.Label.Render())));
        // 扣分行整链（WI-33）：外壳与因子名/明细全部按键现取，重算那一刻渲染当前语言。
        DeductionLines = r.Factors.Select(f => Locale.Fmt("health.deduction.row", f.Label.Render(), f.Deduction, f.Detail.Render())).ToList();

        LanguageSummary = r.LanguageTop.Count == 0
            ? Locale.T("health.stat.none")
            : string.Join(" · ", r.LanguageTop.Take(5).Select(s => $"{s.Language} {s.Count}"));
        DuplicateSummary = r.DuplicateTop.Count == 0
            ? Locale.T("health.stat.none")
            // 「、」与那对括号的<b>宽度</b>都是语言内容（批次 WI-57 的普查尺量到）：连接号早已归
            // <c>health.join.list</c> ⇒ 这里⛔ 再抄一份（同一件事两个主人＝P-171 那一族），括号形状走表。
            // ⚠ 通用 en 列闸门看不见它——那颗只禁汉字与假名（坑表 #403 第二次兑现）。
            : string.Join(Locale.T("health.join.list"),
                r.DuplicateTop.Take(5).Select(d => Locale.Fmt("health.dup.item", d.Title, d.Count)));
        TagSummary = r.TagHistogram.Count == 0
            ? Locale.T("health.stat.none")
            : string.Join(" · ", r.TagHistogram.Take(5).Select(t => $"{t.Tag} {t.Count}"));
        // 整句在重算那一刻按键取词（进这一页必过 LoadHealthAsync＝组合句一族的"按语言重算"时机）。
        LanguageLine = Locale.Fmt("health.stat.lang", LanguageSummary);
        DomainsLine = Locale.Fmt("health.stat.domains", r.DistinctDomains);
        DuplicateLine = Locale.Fmt("health.stat.dup", DuplicateSummary);
        TagLine = Locale.Fmt("health.stat.tag", TagSummary);

        var max = r.NewTrend.Count == 0 ? 0 : r.NewTrend.Max(t => t.Count);
        TrendBars = r.NewTrend
            .Select(t => new HealthTrendBar
            {
                Count = t.Count,
                DateLabel = t.Date,
                Height = max > 0 && t.Count > 0 ? Math.Max(3, t.Count * 80.0 / max) : 0,
            })
            .ToList();
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
    }

    /// <summary>
    /// 「剪贴板历史」总开关（默认关）。翻位即持久化 + <b>立刻</b>启停监听窗口，
    /// 并把"到底有没有开始记录"照实写进状态行——开成功与开失败必须区分得出来（P-53/P-54 同口径）。
    /// </summary>
    [ObservableProperty] private bool _clipboardHistoryEnabled;

    [ObservableProperty] private string _clipboardHistoryStatus = string.Empty;

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
