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
/// 计算器组件：表达式求值 + 常用速算 + 单位换算。
/// <para>
/// 全部判定在 <c>StarMark.Core.Calc</c>（求值器 / 速算目录与算法 / 换算表），本类只搬运输入与输出。
/// 历史带按实例存进 <c>widgets.json</c>（与快捷入口的 Links 同一通道），因此同类型的两个计算器互不干扰。
/// </para>
/// </summary>
public sealed partial class CalcWidget : UserControl
{
    public CalcWidgetViewModel ViewModel { get; }

    private readonly WidgetManager _manager;
    private readonly string _instanceId;
    private TextBox[] _quickBoxes = Array.Empty<TextBox>();
    private TextBlock[] _quickLabels = Array.Empty<TextBlock>();
    private FrameworkElement[] _quickCells = Array.Empty<FrameworkElement>();

    /// <summary>InitializeComponent 期间设置 SelectedIndex 也会触发 SelectionChanged，此时 VM 与控件都还没接好。</summary>
    private bool _ready;

    public CalcWidget(WidgetInstanceConfig config, WidgetManager manager)
    {
        ViewModel = new CalcWidgetViewModel();
        _manager = manager;
        _instanceId = config.Id;
        InitializeComponent();
        _quickBoxes = new[] { QuickBox0, QuickBox1, QuickBox2, QuickBox3 };
        _quickLabels = new[] { QuickLabel0, QuickLabel1, QuickLabel2, QuickLabel3 };
        _quickCells = new FrameworkElement[] { QuickCell0, QuickCell1, QuickCell2, QuickCell3 };
        ViewModel.SeedHistory(config.CalcHistory);
        // 三页各先算一次：否则切过去是一片空白，看起来像功能没做
        ViewModel.RecalcUnit();
        ApplyQuickMode(0);
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

    // ───────────────────────── 常用速算 ─────────────────────────

    /// <summary>四个输入槽按模式复用：谁显示、叫什么名字，全部听 <see cref="QuickCalculator"/> 的槽表。</summary>
    private void ApplyQuickMode(int index)
    {
        var fields = ViewModel.SelectQuickMode(index);
        for (var i = 0; i < _quickBoxes.Length; i++)
        {
            // 先一律清空再决定显隐：收起来的格子若留着上一模式的文本，切回来就是"看着像填错"的旧值
            _quickBoxes[i].Text = string.Empty;
            _quickCells[i].Visibility = i < fields.Count ? Visibility.Visible : Visibility.Collapsed;
            if (i >= fields.Count) continue;
            _quickLabels[i].Text = fields[i].Required ? fields[i].Label : fields[i].Label + "（可空）";
            _quickBoxes[i].PlaceholderText = fields[i].Placeholder;
            _quickBoxes[i].Tag = i;
        }
        ShowQuickHint(string.Empty);
    }

    private void QuickModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        ApplyQuickMode(QuickModeBox.SelectedIndex);
    }

    private void QuickBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || sender is not TextBox { Tag: int slot }) return;
        ShowQuickHint(string.Empty);
        ViewModel.SetQuickText(slot, ((TextBox)sender).Text);
    }

    private void CopyQuick_Click(object sender, RoutedEventArgs e)
    {
        var text = ViewModel.QuickCopyText;
        if (text is null)
        {
            ShowQuickHint("没有可复制的结果：还有必填的格子没填对");
            return;
        }
        ShowQuickHint(TryCopy(text, out var why) ? "已复制速算结果" : $"复制失败：{why}");
    }

    private void ClearQuick_Click(object sender, RoutedEventArgs e)
    {
        foreach (var box in _quickBoxes) box.Text = string.Empty;
        ShowQuickHint("已清空");
    }

    /// <summary>常用页的提示走自己的文本块（<c>HintBlock</c> 在"计算"页里，从这页看不见）；空文本＝回落到模式说明。</summary>
    private void ShowQuickHint(string text)
        => QuickHintBlock.Text = string.IsNullOrEmpty(text) ? ViewModel.CurrentQuickMode.Hint : text;

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
