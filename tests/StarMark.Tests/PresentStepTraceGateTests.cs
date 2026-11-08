#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// Present() 逐步计时取证闸门（片243，扫源码不跑界面）。
/// <para>
/// 现场（starmark-20261108.log 全天取证）：三例等待型阻塞（托盘恢复/热键呼出・1.2-1.8s・CPU≈0）
/// 都走这条路，此前零读数——整体被 try/catch 罩住、黑箱一个。分步插桩后下一轮用户复现时，
/// Show/Activate/SW_RESTORE/SetForegroundWindow/OpenSettingsPage 谁在等，读数直接留名。
/// 快路径（合计几十 ms）一条日志都不发。
/// </para>
/// </summary>
public sealed class PresentStepTraceGateTests
{
    private static string MainWindowCode() => SourceGate.Code(SourceGate.ReadRepoPartials("src/StarMark.UI/MainWindow.xaml.cs"));

    /// <summary>五步全在计时罩里：每步都过 TracePresentStep（差值读数，瓶颈步＝相邻两条差最大）。</summary>
    [Fact]
    public void PresentTimesEachStepAndLogsSlowOnes()
    {
        var present = SourceGate.MethodBody(MainWindowCode(), "public void Present(bool settings, string? settingsTabId = null)");
        var squashed = SourceGate.Squash(present);
        Assert.Contains("TracePresentStep(sw, mark, \"AppWindow.Show\")", squashed);
        Assert.Contains("TracePresentStep(sw, mark, \"Activate\")", squashed);
        Assert.Contains("TracePresentStep(sw, mark, \"ShowWindow(SW_RESTORE)\")", squashed);
        Assert.Contains("TracePresentStep(sw, mark, \"SetForegroundWindow\")", squashed);
        Assert.Contains("TracePresentStep(sw, mark, \"OpenSettingsPage\")", squashed);
        Assert.Contains("OpenSettingsPage(settingsTabId)", present);          // settings 路由原样
        Assert.Contains("Present 全程", squashed);                            // 慢现场总账
    }

    /// <summary>取证帮手只在慢现场说话：差值超阈值才记一行，快路径零日志开销。</summary>
    [Fact]
    public void TraceHelperLogsOnlySlowSteps()
    {
        var step = SourceGate.MethodBody(MainWindowCode(), "private static long TracePresentStep(");
        Assert.Contains("now - fromMs >= PresentStepLogMs", step);   // 差值判阈（sw 不停表）
        Assert.Contains("StarLog.Info", step);
    }
}
