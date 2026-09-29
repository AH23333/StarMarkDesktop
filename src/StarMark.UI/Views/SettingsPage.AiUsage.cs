#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.UI.Helpers;
using StarMark.UI.Services;

namespace StarMark.UI.Views;

/// <summary>
/// 设置页「token 预算与用量」段（§20.1 可见性 / §20.2 预算与熔断）。
    /// <para><b>这里不判闸</b>：判额只有一个出处（<see cref="AiClassifyService.BudgetVerdictAsync"/>），
    /// 面板只把它的结论画出来——"显示没超却被拦"与"显示超了还能用"两种鬼状态都来自两处各判一次。</para>
/// </summary>
public sealed partial class SettingsPage
{
    private void InitAiUsagePanel()
    {
        try
        {
            var budget = App.Services.GetRequiredService<SettingsStore>().LoadAiBudget();
            AiBudgetBox.Value = budget.MonthlyTokenBudget;
            // 置位这一格要压住 TextChanged：否则"打开设置页"这件事本身就是一次写盘（同 AiTimeoutBox 那条）。
            _aiLoading = true;
            try
            {
                AiWarnRatioBox.Text = budget.WarnRatioOverride is { } ratio
                    ? PercentOfRatio(ratio)
                    : string.Empty;
            }
            finally
            {
                _aiLoading = false;
            }
            RefreshAiWarnRatioNote();
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[AI 用量] 预算档位没读出来（面板用默认显示）：{ex.Message}");
        }
        _ = RefreshAiUsageAsync();   // 面板是展示件：读失败只留一行解释，绝不让设置页开不了
    }

    /// <summary>界面按"百分之几"说话，存档与判额按小数比例走。<b>换算只有这一颗</b>，
    /// 而"这个比例合不合法"全在 <see cref="AiBudget.WithWarnRatio"/> 里判——界面里不许长出第二份区间。
    /// 形状走 <see cref="NumberText"/>：这一格显示的是可带一位小数的百分数，德式会变成 <c>85,3</c>。</summary>
    private static string PercentOfRatio(double ratio)
        => NumberText.UpTo1(ratio * 100);

    private void RefreshAiWarnRatioNote()
    {
        var ratio = App.Services.GetRequiredService<SettingsStore>().LoadAiBudget().WarnRatio;
        AiWarnRatioNoteText.Text =
            $"用到预算的 {PercentOfRatio(ratio)}% 时先提醒（到线不拦，只说明；留空＝出厂 {PercentOfRatio(AiBudget.DefaultWarnRatio)}%）。";
    }

    private void AiWarnRatioBox_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_aiLoading) return;               // 装载时的置位不写盘
        var store = App.Services.GetRequiredService<SettingsStore>();
        var text = AiWarnRatioBox.Text.Trim();
        // 只认不带千位分隔的小数写法：同一份存档不该按机器的区域设置变含义（R3 那条核查管的就是这类数）。
        var percent = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value / 100.0
            : (double?)null;
        var next = store.LoadAiBudget().WithWarnRatio(percent);
        store.SaveAiBudget(next);
        // "留空/打不进数字"与"采纳了"都回到同一句现状说明；只有"填了但被回默认"才需要单独解释，
        // 判据是覆盖位里存进去的到底是不是我刚才那个数——不在界面再判一次合法区间。
        if (percent is null || next.WarnRatioOverride == percent)
        {
            RefreshAiWarnRatioNote();
            return;
        }
        AiWarnRatioNoteText.Text =
            $"{PercentOfRatio(percent.Value)}% 不是 0 与 1 之间的比例（也就是只能填 1–99 这一档），"
            + $"已按出厂 {PercentOfRatio(AiBudget.DefaultWarnRatio)}% 执行。";
    }

    private async Task RefreshAiUsageAsync()
    {
        try
        {
            var services = App.Services;
            var store = services?.GetRequiredService<SettingsStore>();
            var classifier = services?.GetRequiredService<AiClassifyService>();
            if (store is null || classifier is null) return;

            // 读一次账本 + 一次裁决；裁决要拿"窗口合计"，快照也带窗口合计——同一窗口两处不重算：
            // 先取快照（含 max at），再判额用的是快照里的同一个 from。
            var snapshot = await classifier.UsageSnapshotAsync();
            var budget = store.LoadAiBudget();
            var verdict = snapshot is null ? budget.Judge(0) : budget.Judge(snapshot.Window.TotalTokens);
            // 面板顺带触发一次"该熔则熔"（越线但没拉闸的补刀）——入口闸门在功能侧，这里只保证展示不说谎。
            if (verdict.State == AiBudgetState.Tripping)
                store.SaveAiBudget(budget with { PausedByBudget = true });

            AiBudgetUnpauseButton.Visibility =
                store.LoadAiBudget().PausedByBudget ? Visibility.Visible : Visibility.Collapsed;

            if (snapshot is null)
            {
                AiUsageText.Text = "用量账本还没接上（不影响使用，本次会话的消耗不会记录）。";
                return;
            }

            var used = snapshot.Window;
            var text = snapshot.MaxRecordedAt is null
                ? $"本月还没花过 token（预算 {NumberText.Grouped(verdict.Used + verdict.Remaining)}）"
                : $"近 30 天（按账本最后一条起算）已用 {NumberText.Grouped(used.TotalTokens)} / 预算 {NumberText.Grouped(budget.MonthlyTokenBudget)}，"
                  + $"共 {used.Calls} 次调用";
            if (used.EstimatedCalls > 0)
                text += $"（其中 {used.EstimatedCalls} 次是按字符估算的，服务没回计量）";
            if (used.Calls > 0)
                text += "。" + string.Join("；", snapshot.ByFeature.Select(f => $"{f.Name} {NumberText.Grouped(f.TotalTokens)}")) + "。";
            switch (verdict.State)
            {
                case AiBudgetState.Warning:
                    text += $"到 {NumberText.Grouped(budget.MonthlyTokenBudget)} 会自动暂停（预警线设在预算的 {PercentOfRatio(budget.WarnRatio)}%），"
                        + $"还剩 {NumberText.Grouped(verdict.Remaining)} token。";
                    break;
                case AiBudgetState.Tripping:
                case AiBudgetState.Blocked:
                    text += budget.PausedByBudget
                        ? "AI 已按预算自动暂停——点「恢复 AI」继续；恢复前请先把预算调到够用的档位。"
                        : "AI 已按预算自动暂停。";
                    break;
            }
            AiUsageText.Text = text;
        }
        catch (Exception ex)
        {
            AiUsageText.Text = "用量读不出来：" + ex.Message;
        }
    }

    private void AiBudget_Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var store = App.Services.GetRequiredService<SettingsStore>();
            var current = store.LoadAiBudget();
            // 只换算额度、熔断位原样：调额度不等于赦熔断——恢复是另一颗按钮的显式动作（防"顺手改额度"绕过手动恢复）
            store.SaveAiBudget(current.WithMonthlyTokens((long)Math.Round(AiBudgetBox.Value)));
            _ = RefreshAiUsageAsync();
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "预算没存上：" + ex.Message;
        }
    }

    private void AiBudget_Unpause_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var store = App.Services.GetRequiredService<SettingsStore>();
            // 恢复只是熄灭熔断位；若窗口内用量仍然越线，下一次 AI 动作的入口闸门会当场再熔——诚实的恢复，不是永久豁免。
            store.SaveAiBudget(store.LoadAiBudget() with { PausedByBudget = false });
            _ = RefreshAiUsageAsync();
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "恢复没成功：" + ex.Message;
        }
    }
}
