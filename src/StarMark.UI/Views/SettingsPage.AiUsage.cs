#nullable enable
using System;
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
            AiBudgetBox.Value = App.Services.GetRequiredService<SettingsStore>().LoadAiBudget().MonthlyTokenBudget;
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[AI 用量] 预算档位没读出来（面板用默认显示）：{ex.Message}");
        }
        _ = RefreshAiUsageAsync();   // 面板是展示件：读失败只留一行解释，绝不让设置页开不了
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
                ? $"本月还没花过 token（预算 {verdict.Used + verdict.Remaining:N0}）"
                : $"近 30 天（按账本最后一条起算）已用 {used.TotalTokens:N0} / 预算 {budget.MonthlyTokenBudget:N0}，"
                  + $"共 {used.Calls} 次调用";
            if (used.EstimatedCalls > 0)
                text += $"（其中 {used.EstimatedCalls} 次是按字符估算的，服务没回计量）";
            if (used.Calls > 0)
                text += "。" + string.Join("；", snapshot.ByFeature.Select(f => $"{f.Name} {f.TotalTokens:N0}")) + "。";
            switch (verdict.State)
            {
                case AiBudgetState.Warning:
                    text += $"到 {budget.MonthlyTokenBudget:N0} 会自动暂停，还剩 {verdict.Remaining:N0} token。";
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
