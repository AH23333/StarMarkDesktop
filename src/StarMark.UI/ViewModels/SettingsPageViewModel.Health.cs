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
            HealthError = $"健康度计算失败：{ex.Message}";
            StarLog.Error($"健康度计算失败: {ex}");
        }
        finally { IsHealthLoading = false; }
    }

    private void RefreshHealthView()
    {
        var r = HealthReport;
        ScoreText = r.Score.ToString();
        ScoreGrade = r.Score >= 80 ? "健康" : r.Score >= 50 ? "一般，可优化" : "需整理";
        ScoreBrush = r.Score >= 80
            ? new SolidColorBrush(Color.FromArgb(255, 16, 124, 16))   // 绿
            : r.Score >= 50
                ? new SolidColorBrush(Color.FromArgb(255, 214, 137, 16)) // 琥珀
                : new SolidColorBrush(Color.FromArgb(255, 196, 43, 28));  // 红

        ScoreSummary = r.Factors.Count == 0
            ? "收藏整理良好，继续保持"
            : "待优化：" + string.Join("、", r.Factors.Select(f => f.Label));

        LanguageSummary = r.LanguageTop.Count == 0
            ? "（无）"
            : string.Join(" · ", r.LanguageTop.Take(5).Select(s => $"{s.Language} {s.Count}"));
        DuplicateSummary = r.DuplicateTop.Count == 0
            ? "（无）"
            : string.Join("、", r.DuplicateTop.Take(5).Select(d => $"{d.Title}（{d.Count}）"));
        TagSummary = r.TagHistogram.Count == 0
            ? "（无）"
            : string.Join(" · ", r.TagHistogram.Take(5).Select(t => $"{t.Tag} {t.Count}"));

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
    public System.Collections.ObjectModel.ObservableCollection<StarMark.Core.Diagnostics.DiagnosticEntry> DiagnosticEntries { get; }
        = new();
    [ObservableProperty] private bool _hasDiagnosticsError;
    [ObservableProperty] private string _diagnosticsError = string.Empty;

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

    /// <summary>采集只读诊断信息（P2-8）。本地查询，零网络。</summary>
    public async Task LoadDiagnosticsAsync()
    {
        if (_diagnostics is null) return;
        HasDiagnosticsError = false;
        try
        {
            // 诊断里含 COUNT(*) 全量扫描与逐表统计：同样是"进设置页即同步跑完"的站点，offload 后回 UI 填集合。
            var entries = await Task.Run(() => _diagnostics.CollectAsync(CancellationToken.None));
            DiagnosticEntries.Clear();
            foreach (var e in entries) DiagnosticEntries.Add(e);
        }
        catch (Exception ex)
        {
            HasDiagnosticsError = true;
            DiagnosticsError = $"诊断信息采集失败：{ex.Message}";
            StarLog.Error($"诊断信息采集失败: {ex}");
        }
    }
}
