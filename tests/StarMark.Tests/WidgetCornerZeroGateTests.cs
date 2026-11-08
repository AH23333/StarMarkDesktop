#nullable enable
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「圆角半径调到 0（直角档）」的守门：直角必须是真的直角，而不是被 DWM 用系统默认半径
/// 重新圆化一遍。
/// <para>
/// 根因形状（坑表 #1163）：<c>SetRoundedWindowRegion</c> 的 <c>≤ 0</c> 分支从前写的是
/// <c>SetDwmCornerPreference(window, DWMWCP_ROUND)</c>——自定义区域确实移除了，但 DWM
/// 随即用<b>系统默认半径</b>把窗口重新圆化，屏幕上正是用户报的「圆角拖到最小反而回到默认圆角」。
/// 那行注释当年写的是「直角 / 恢复 DWM 自带圆角」——两义并置，实现选了错的那义。
/// </para>
/// <para>
/// 钉法沿用 <see cref="AppearanceRangeGateTests"/> 那族的口径：禁项读<b>抹掉注释后的代码</b>
/// （这里正是要在注释里引用旧写法的地方），配正面计数反空转（#161）。
/// </para>
/// </summary>
public sealed class WidgetCornerZeroGateTests
{
    private const string InteropFile = "src/StarMark.UI/Helpers/WindowInterop.cs";

    [Fact]
    public void RadiusZeroGoesSquare_NotBackToDwmDefaultRounding()
    {
        var body = MethodBody(Code(ReadRepoFile(InteropFile)), "public static void SetRoundedWindowRegion");

        // 直角分支：移除自定义区域 + 显式关掉 DWM 圆角。禁项是 DWMWCP_ROUND——
        // 它正是「调到最小却回到默认圆角」那颗根因；注释里引用旧写法不算违规（抹注释后扫）。
        var square = Between(body, "cornerRadiusLogical <= 0", "return;");
        Assert.Equal(0, Count(square, "DWMWCP_ROUND"));
        Assert.Equal(1, Count(square, "SetDwmCornerPreference(window, DWMWCP_DONOTROUND)"));
        Assert.Contains("SetWindowRgn(hwnd, IntPtr.Zero, true)", square);

        // 反空转：整个方法里 DWMWCP_ROUND 归零（禁项真的有人守），DONOTROUND 恰好两处
        // （直角分支 + 自定义半径分支的「关掉 DWM 自带圆角避免双重圆角」）——少于 2 说明
        // 抹注释或分支被删，禁项闸门在空转。
        Assert.Equal(0, Count(body, "DWMWCP_ROUND"));
        Assert.Equal(2, Count(body, "DWMWCP_DONOTROUND"));
    }
}
