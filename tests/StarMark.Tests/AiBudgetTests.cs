#nullable enable
using StarMark.Abstractions.Ai;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// §20.2 预算裁决的三态分界——<b>这一组断言钉的是"什么时候该喊、什么时候该拦、什么时候该装死"</b>：
/// Warning 不拦（预算的意义是可见性，不是暗中掐线）；Tripping 只出现一次（拉闸后转入 Blocked，
/// 不许"越线批批变 Tripping"导致界面反复弹"刚刚熔掉"）；Blocked 对窗口滚动免疫（§20.2 明写手动恢复）。
/// </summary>
public sealed class AiBudgetTests
{
    [Fact]
    public void UnderLimit_Allows()
    {
        var v = AiBudget.Default.Judge(100_000);
        Assert.Equal(AiBudgetState.Ok, v.State);
        Assert.True(v.AllowsCall);
        Assert.Equal(400_000, v.Remaining);
        Assert.Equal(100_000, v.Used);
    }

    [Fact]
    public void PastEightyPercent_WarnsButPasses()
    {
        var v = AiBudget.Default.Judge(420_000);
        Assert.Equal(AiBudgetState.Warning, v.State);
        Assert.True(v.AllowsCall);        // 80% 是喊，不是拦
    }

    [Fact]
    public void ReachingBudget_TripsOnce()
    {
        var b = AiBudget.Default;
        var first = b.Judge(500_000);
        Assert.Equal(AiBudgetState.Tripping, first.State);
        Assert.False(first.AllowsCall);

        var underByOne = b.Judge(499_999);  // 边界差一元不算越线，但 80–100% 之间该喊的是 Warning
        Assert.Equal(AiBudgetState.Warning, underByOne.State);
    }

    [Fact]
    public void BlockedSurvivesWindowRollback()
    {
        // 窗口滚回去了（比如手动重置后账变空）但熔断位还亮——仍必须拒（§20.2：恢复=手动点击）
        var blocked = AiBudget.Default with { PausedByBudget = true };
        var v = blocked.Judge(0);
        Assert.Equal(AiBudgetState.Blocked, v.State);
        Assert.False(v.AllowsCall);
    }

    [Fact]
    public void TrippedAndStillOver_StaysBlocked_NotTrippingAgain()
    {
        var b = AiBudget.Default with { PausedByBudget = true };
        var v = b.Judge(900_000);
        Assert.Equal(AiBudgetState.Blocked, v.State);   // Tripping 只在"第一次发现"时出现，之后都是 Blocked
    }

    [Fact]
    public void TokensAreClamped_NotSilentlyWidened()
    {
        Assert.Equal(AiBudget.MinMonthlyTokens, AiBudget.Default.WithMonthlyTokens(1).MonthlyTokenBudget);
        Assert.Equal(AiBudget.DefaultMonthlyTokens, AiBudget.Default.WithMonthlyTokens(-7).MonthlyTokenBudget);

        var raised = AiBudget.Default.WithMonthlyTokens(2_000_000);
        Assert.Equal(2_000_000, raised.MonthlyTokenBudget);
        Assert.True(raised.Judge(1_900_000).AllowsCall);   // Warning（95%）——调高额度立刻反映到裁决，不用重启
    }

    [Fact]
    public void MonthlyWindow_AnchorsOnLedgerNotWallClock()
    {
        const long max = 2_000_000_000;
        // 精确 30 天；这个数是"从最后一条账往回看"，改系统墙钟改不动它（§20.5）
        Assert.Equal(max - 30L * 24 * 3600, AiBudget.MonthlyFrom(max));
    }
}
