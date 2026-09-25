#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「无边框窗口的代价」闸门。PO-2 用真机分段计量定位到 <c>OverlappedPresenter.SetBorderAndTitleBar</c>
/// 单次 ≈68 ms（同一个方法里其余五步合计 &lt;3 ms），去掉它之后 22 颗组件的恢复从 1406 ms 降到 641 ms。
/// 这条数字是量出来的，不是猜的，所以要把结论钉住：
/// 谁要是为了"与 DeskBox 同款序列"把那一句加回来，就得重新量一遍并解释代价。
/// </summary>
public sealed class FramelessWindowCostTests
{
    [Fact]
    public void RemoveDefaultWindowFrame_UsesWin32StyleBits_NotTheSlowPresenterCall()
    {
        var body = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.UI/Helpers/WindowInterop.cs"),
            "public static void RemoveDefaultWindowFrame(");

        // 框架那句"关边框/标题栏"是这段里唯一的贵调用（实测 ≈68 ms/次），样式位由 Win32 一次清完。
        Assert.DoesNotContain("SetBorderAndTitleBar(", body);
        Assert.Contains("WS_CAPTION", body);
        Assert.Contains("SWP_FRAMECHANGED", body);   // 不清非客户区的话部分系统仍残留标题栏
        // 只有"不进任务栏 / Alt+Tab"必须走框架，且它实测 ≤2 ms。
        Assert.Contains("IsShownInSwitchers", body);
    }

    [Fact]
    public void WidgetFirstStyleAndShow_AreSplitSoTheRulerKeepsWorking()
    {
        // PO-1 的教训（RI-5 同族）：尺子要装在真的会被走到的那一段上。
        // 首装（样式/尺寸/外壳）与每次都要走的"点亮"必须分开量，否则一颗窗口 100 ms 到底是哪一步无从判断。
        var source = SourceGate.ReadRepoFile("src/StarMark.UI/Views/WidgetWindow.xaml.cs");

        Assert.Contains("private void StyleForTheFirstTime()", source);
        Assert.Contains("private void ShowOnDesktop()", source);
        var reveal = SourceGate.MethodBody(source, "public void Reveal()");
        Assert.Contains("StyleForTheFirstTime", reveal);
        Assert.Contains("ShowOnDesktop", reveal);
    }
}
