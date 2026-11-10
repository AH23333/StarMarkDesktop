#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.I18n;
using StarMark.UI.Helpers;
using StarMark.UI.Services;

namespace StarMark.UI.Views;

/// <summary>
/// 设置页「AI 仪表盘」卡（P-294 用户拍板：长在 AI 栏的新卡——最近请求明细＋汇总读数＋按天趋势小图）。
/// <para>数据源＝§20.1 那本 <c>ai_usage</c> 账本（P-293 链路落账处）：预算栏判额、这里画趋势，
/// 两只读面同一份账——<b>这里⛔ 另起第二套计数</b>，数字全部来自 <see cref="AiClassifyService.DashboardSnapshotAsync"/>。</para>
/// <para>汇总念<b>全期</b>合计，预算栏那行念 30 天窗口——两句各管一段，不是同一句说两遍。</para>
/// </summary>
public sealed partial class SettingsPage
{
    /// <summary>趋势小图的天数。14 天＝两星期一眼比出"哪天烧得多"，再多就是报表不是仪表盘。</summary>
    private const int DashboardTrendDays = 14;

    /// <summary>最近请求明细列几条。10 条＝最近一次整理的各批＋再往前的几次；更长的是日志不是仪表盘。</summary>
    private const int DashboardRecentTake = 10;

    /// <summary>趋势条的最高高度（像素）。条高与峰值成正比、保底 4 像素——纯视觉常数，不进词条不进存档。</summary>
    private const double DashboardBarPeakHeight = 44;

    private readonly ObservableCollection<AiTrendBar> _aiTrendBars = new();
    private readonly ObservableCollection<AiDashboardRow> _aiDashboardRows = new();

    private void InitAiDashboard()
    {
        AiDashboardTrend.ItemsSource = _aiTrendBars;
        AiDashboardRecentList.ItemsSource = _aiDashboardRows;
        _ = RefreshAiDashboardAsync();
    }

    private async Task RefreshAiDashboardAsync()
    {
        try
        {
            var snapshot = await App.Services.GetRequiredService<AiClassifyService>()
                .DashboardSnapshotAsync(DashboardRecentTake, DashboardTrendDays);
            if (snapshot is null)
            {
                // 账本没接线：预算栏那格念过同一句，这里复用——两处该是同一句话，不是两个出处
                LocText.SetMessage(AiDashboardSummaryText, LocMessage.Of("settings.ai.usage.noLedger"));
                _aiTrendBars.Clear();
                _aiDashboardRows.Clear();
                return;
            }
            if (snapshot.Latest is null)
            {
                LocText.SetMessage(AiDashboardSummaryText, LocMessage.Of("settings.ai.dashboard.empty"));
                _aiTrendBars.Clear();
                _aiDashboardRows.Clear();
                return;
            }

            // 汇总读数：全期合计＋估算几条＋最近一条时刻（时刻走 DateTimeText 的 RZ 口径，不跟语言走）
            LocText.SetMessage(AiDashboardSummaryText, LocMessage.Of("settings.ai.dashboard.summary",
                snapshot.AllTime.Calls, NumberText.Grouped(snapshot.AllTime.TotalTokens),
                snapshot.AllTime.EstimatedCalls, DateTimeText.Minute(snapshot.Latest.Value)));

            // 按天趋势小图：条高 ∝ 当日 token；零日画空条占位——"这天没花"在图上要看得见
            var peak = snapshot.Days.Count == 0 ? 1 : Math.Max(1, snapshot.Days.Max(day => day.TotalTokens));
            _aiTrendBars.Clear();
            foreach (var day in snapshot.Days)
            {
                var height = day.TotalTokens <= 0 ? 0
                    : Math.Max(4.0, Math.Round(DashboardBarPeakHeight * day.TotalTokens / peak));
                // 轨道高＝峰值条高那颗常数（片259）：条在固定高的轨道里贴底对齐，日号一律排在轨道脚下，
                // 短条/空条不再把自己的日号顶到与高条齐平。XAML 里⛔ 写死 44——那会是第二份峰值。
                _aiTrendBars.Add(new AiTrendBar(day.Day, day.Calls, day.TotalTokens, height, DashboardBarPeakHeight));
            }

            // 最近请求明细：仓储已按新→旧排好，这里照单铺
            _aiDashboardRows.Clear();
            foreach (var entry in snapshot.Recent)
                _aiDashboardRows.Add(new AiDashboardRow(entry));
        }
        catch (Exception ex)
        {
            // 面板是展示件：读不到只说读不到，绝不让设置页开不了（与预算栏同一条纪律）
            LocText.SetMessage(AiDashboardSummaryText,
                LocMessage.Of("settings.ai.status.usageReadFailed", LocMessage.Of("common.rawValue", ex.Message)));
        }
    }

    /// <summary>换语言那一拍：趋势条的悬浮提示与明细行只把存着的「键＋实参」重念一遍（⛔ 重新读账本）。</summary>
    private void RepasteAiDashboardRows()
    {
        foreach (var bar in _aiTrendBars) bar.Repaste();
        foreach (var row in _aiDashboardRows) row.Repaste();
    }
}

/// <summary>
/// 趋势小图的一根条。悬浮提示存<b>「键＋实参」</b>⛔ 存成品字——这根条由 <c>DataTemplate</c> 的
/// <c>{Binding}</c> 递出去，换语言那一刻由 <see cref="Repaste"/> 回来重念（机制 (C)·又一位宿主，P-294）。
/// </summary>
public sealed class AiTrendBar : INotifyPropertyChanged
{
    private readonly LocMessage _tip;

    public AiTrendBar(string day, int calls, long tokens, double barHeight, double trackHeight)
    {
        // 日号当标签（"07"），整颗日期进提示——图要的是一眼比高矮，精确到日是悬浮之后的事
        DayLabel = day.Length == 10 ? day.Substring(8, 2) : day;
        BarHeight = barHeight;
        // 条躺的轨道高（片259）：值＝峰值条高那颗常数，由调用方交进来——行类不自己存 44，
        // 视觉常数只活在 SettingsPage.AiDashboard 一处。
        TrackHeight = trackHeight;
        _tip = LocMessage.Of("settings.ai.dashboard.barTip",
            LocMessage.Of("common.rawValue", day), NumberText.Grouped(tokens), calls);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DayLabel { get; }
    public double BarHeight { get; }

    /// <summary>这根条躺的固定高轨道（<c>{Binding}</c> 的落点）：条在轨道里贴底，日号一律排在轨道脚下。</summary>
    public double TrackHeight { get; }

    /// <summary>条上的悬浮提示（<c>{Binding}</c> 的落点；⛔ 在这里存渲染结果＝换语言停在上一门）。</summary>
    public string TipText => _tip.Render();

    public void Repaste() => PropertyChanged?.Invoke(this, new(nameof(TipText)));
}

/// <summary>
/// 最近请求明细的一行。两句都存<b>「键＋实参」</b>，换语言由 <see cref="Repaste"/> 回来重念
/// （与 <c>AiGroupRow</c> 同一形状：模板上挂不了"这一行自己的那颗键"）。
/// </summary>
public sealed class AiDashboardRow : INotifyPropertyChanged
{
    private readonly LocMessage _mainLine;
    private readonly LocMessage _tokensLine;

    public AiDashboardRow(AiUsageEntry entry)
    {
        // 时刻按 RZ 口径走 DateTimeText（不跟语言走）；功能名与模型名是数据，交 rawValue 那道门
        _mainLine = LocMessage.Of("settings.ai.dashboard.rowMain",
            DateTimeText.Minute(entry.At.ToLocalTime()),
            LocMessage.Of("common.rawValue", entry.Feature),
            entry.Model is null
                ? LocMessage.Of("settings.ai.dashboard.noModel")
                : LocMessage.Of("common.rawValue", entry.Model));
        _tokensLine = LocMessage.Of(entry.Usage.Estimated
                ? "settings.ai.dashboard.rowTokensEstimated" : "settings.ai.dashboard.rowTokens",
            NumberText.Grouped(entry.Usage.InputTokens), NumberText.Grouped(entry.Usage.OutputTokens));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string MainLine => _mainLine.Render();
    public string TokensLine => _tokensLine.Render();

    public void Repaste()
    {
        PropertyChanged?.Invoke(this, new(nameof(MainLine)));
        PropertyChanged?.Invoke(this, new(nameof(TokensLine)));
    }
}
