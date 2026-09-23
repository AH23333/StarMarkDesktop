#nullable enable
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StarMark.Abstractions;
using StarMark.Core.Calc;
using StarMark.Core.Widgets;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Views;

/// <summary>
/// 计算器组件：表达式求值 + 单位换算 + 时间戳换算。
/// <para>
/// 全部判定在 <c>StarMark.Core.Calc</c>（求值器 / 换算表 / 时间戳），本类只搬运输入与输出。
/// 历史带按实例存进 <c>widgets.json</c>（与快捷入口的 Links 同一通道），因此同类型的两个计算器互不干扰。
/// </para>
/// </summary>
public sealed partial class CalcWidget : UserControl
{
    public CalcWidgetViewModel ViewModel { get; }

    private readonly WidgetManager _manager;
    private readonly string _instanceId;

    /// <summary>InitializeComponent 期间设置 SelectedIndex 也会触发 SelectionChanged，此时 VM 与控件都还没接好。</summary>
    private bool _ready;

    public CalcWidget(WidgetInstanceConfig config, WidgetManager manager)
    {
        ViewModel = new CalcWidgetViewModel();
        _manager = manager;
        _instanceId = config.Id;
        InitializeComponent();
        ViewModel.SeedHistory(config.CalcHistory);
        // 三个换算页各先算一次：否则切过去是一片空白，看起来像功能没做
        ViewModel.RecalcUnit();
        SyncHistoryEmptyHint();
        _ready = true;
    }

    // ───────────────────────── 计算 ─────────────────────────

    private void ExprBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        ViewModel.ApplyExpression(ExprBox.Text);
    }

    private async void ExprBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_ready || e.Key != Windows.System.VirtualKey.Enter) return;
        if (!ViewModel.CommitExpression())
        {
            // 按了回车却没记东西，必须说清为什么（否则用户会以为历史功能坏了）
            ShowHint("这一条没有记入：只有算出结果的算式才进历史带");
            return;
        }
        e.Handled = true;
        ShowHint(string.Empty);
        SyncHistoryEmptyHint();
        await SaveHistoryAsync();
    }

    private void HistoryRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CalcHistoryEntry entry }) return;
        ExprBox.Text = entry.Expression;
        ExprBox.Select(ExprBox.Text.Length, 0);
        ExprBox.Focus(FocusState.Programmatic);
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearHistory();
        SyncHistoryEmptyHint();
        ShowHint("历史已清空");
        await SaveHistoryAsync();
    }

    private void ClearExpr_Click(object sender, RoutedEventArgs e) => ExprBox.Text = string.Empty;

    private void CopyResult_Click(object sender, RoutedEventArgs e)
    {
        var r = ExpressionEvaluator.Evaluate(ViewModel.Expression);
        if (!r.Ok)
        {
            ShowHint("没有可复制的结果：算式还没算对");
            return;
        }
        if (TryCopy(ExpressionEvaluator.Format(r.Value), out var why))
            ShowHint($"已复制 {ExpressionEvaluator.Format(r.Value)}");
        else
            ShowHint($"复制失败：{why}");
    }

    // ───────────────────────── 换算 ─────────────────────────

    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        ViewModel.CategoryIndex = CategoryBox.SelectedIndex;
        // 换类别后起止单位被重置，下拉要跟着回显，否则"显示的单位"与"实际算的单位"不一致
        FromBox.SelectedIndex = ViewModel.FromIndex;
        ToBox.SelectedIndex = ViewModel.ToIndex;
    }

    private void FromBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        ViewModel.FromIndex = FromBox.SelectedIndex;
        ViewModel.RecalcUnit();
    }

    private void ToBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        ViewModel.ToIndex = ToBox.SelectedIndex;
        ViewModel.RecalcUnit();
    }

    private void UnitValueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        ViewModel.RecalcUnit();
    }

    private void Swap_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SwapUnits();
        FromBox.SelectedIndex = ViewModel.FromIndex;
        ToBox.SelectedIndex = ViewModel.ToIndex;
    }

    // ───────────────────────── 时间戳 ─────────────────────────

    private void StampBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        ViewModel.RecalcStamp();
    }

    private void ClockBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        ViewModel.RecalcClock();
    }

    private void UseNow_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.UseNow();
        // 文本框回填会再触发一次 TextChanged→重算，值相同故无害
        StampBox.Text = ViewModel.StampText;
        ClockBox.Text = ViewModel.ClockText;
    }

    private void CopyStamp_Click(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(ViewModel.StampText?.Trim(), out var seconds))
        {
            ShowHint("没有可复制的秒值：左边时间戳还没填对");
            return;
        }
        ShowHint(TryCopy(seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), out var why)
            ? $"已复制秒值 {seconds}"
            : $"复制失败：{why}");
    }

    // ───────────────────────── 共用 ─────────────────────────

    /// <summary>写剪贴板。失败（被别的进程占用/无权限）必须报出来，不能静默——用户下一步就是粘贴。</summary>
    private static bool TryCopy(string text, out string? error)
    {
        error = null;
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            StarLog.Error("计算器复制失败", ex);
            return false;
        }
    }

    private void ShowHint(string text)
    {
        HintBlock.Text = text;
        HintBlock.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SyncHistoryEmptyHint()
        => HistoryEmptyHint.Visibility = ViewModel.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private System.Threading.Tasks.Task SaveHistoryAsync()
        => _manager.SaveCalcHistoryAsync(_instanceId, ViewModel.ToPersisted());
}
