#nullable enable
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 贴图窗该摆在哪儿（批次 PJ：真机反馈"过度放大后贴图跑出屏幕外，再也看不见"）。
/// <para>
/// 判据全在 <see cref="CaptureGeometry.PinOrigin"/> 这一条纯函数里，界面只递坐标——因为这条规则
/// 的正确与否只能用"给定位置与尺寸，落点是什么"来断言，而缩放那一步一旦离开屏幕就再也抓不回来。
/// </para>
/// <para>口径按 Snipaste：<b>缩放钉住左上角</b>；整块塞得下屏幕时必须整块留在屏内（这就是"左上角在屏外、
/// 缩到能看见时自动回到屏幕边缘"）；塞不下时不强求整块可见，但每个方向都至少留一条像素在屏内。</para>
/// </summary>
public sealed class PinPlacementTests
{
    private static readonly IntRect Screen = new(0, 0, 1920, 1040);
    private static readonly IntRect LeftMonitor = new(-2560, 0, 2560, 1440);

    [Theory]
    [InlineData(-3000, -2000, 0, 0)]         // 整块在左上屏外：回到左上角
    [InlineData(5000, 900, 1120, 440)]       // 右下角越界：贴右下边缘
    [InlineData(-100, 100, 0, 100)]          // 只横向越界：纵向那 100 该原样留着
    [InlineData(100, 100, 100, 100)]         // 本来就在屏内：一格都不许动
    public void WholePinFits_SoItIsPulledBackInsideTheScreen(int x, int y, int wantX, int wantY)
        => Assert.Equal((wantX, wantY), CaptureGeometry.PinOrigin(x, y, 800, 600, Screen));

    [Fact]
    public void NegativeOriginMonitor_IsRespected_NotSnappedToPrimary()
    {
        // 副屏在主屏左边时工作区原点是负的：按 (0,0) 夹会把贴图从副屏拽回主屏。
        // 这块屏的右边界在 -2560+2560=0 ⇒ 800 宽的贴图最远只能到 x=-800
        Assert.Equal((-800, 100), CaptureGeometry.PinOrigin(100, 100, 800, 600, LeftMonitor));
        Assert.Equal((-2000, 300), CaptureGeometry.PinOrigin(-2000, 300, 800, 600, LeftMonitor));  // 本来就在屏内：不许动
    }

    [Fact]
    public void BiggerThanTheScreen_LeavesAReachableStrip_InsteadOfVanishing()
    {
        // 5000×4000 摆在 1920×1040 上本来就不可能整块看见：这时允许左上角在屏外，
        // 但右下角必须露头——而"本来就露头"的位置一格都不该动
        Assert.Equal((-4000, -3000), CaptureGeometry.PinOrigin(-4000, -3000, 5000, 4000, Screen));

        var (x, y) = CaptureGeometry.PinOrigin(-9000, -9000, 5000, 4000, Screen);
        Assert.Equal((-4952, -3952), (x, y));                       // keep=48：右下角正好留 48×48 在屏内
        Assert.True(x + 5000 >= 48 && y + 4000 >= 48, "整块丢光就是用户报的那个死法");
    }

    [Fact]
    public void EmptyWorkArea_LeavesThePinExactlyWhereItWas()
        => Assert.Equal((-9000, -9000),
            CaptureGeometry.PinOrigin(-9000, -9000, 800, 600, new IntRect(0, 0, 0, 0)));

    [Fact]
    public void WorkAreaNarrowerThanTheKeepMargin_DoesNotThrow()
    {
        // Math.Clamp 在 min > max 时是抛 ArgumentException 的：40×40 的工作区做不到"至少留 48"，
        // 这里要的是不崩（崩了就是贴图一缩小整个应用挂掉）
        var (x, y) = CaptureGeometry.PinOrigin(-9000, -9000, 800, 600, new IntRect(0, 0, 40, 40));
        Assert.Equal((-752, -552), (x, y));
    }

    [Fact]
    public void DegenerateZeroSize_StillEndsUpOnScreen()
    {
        var (x, y) = CaptureGeometry.PinOrigin(-9000, -9000, 0, 0, Screen);
        Assert.True(x >= 0 && y >= 0, "0 尺寸的贴图也不许停在屏外");
    }

    /// <summary>
    /// 界面那两条路（滚轮缩放、拖动）都必须走这条判据，且缩放不许再绕中心。
    /// <para>绕中心就是这次的缺陷本体：中心被拖到屏外以后，往下缩只是围着屏外的中心收拢。
    /// 测试工程引用不到 UI 层，所以这条只能扫源码——但它钉的是"还有没有人自己算落点"。</para>
    /// <para>批次 PN：贴图窗本身就是截图那条编辑链，所以这两条路从 PinWindow 搬进了
    /// CaptureOverlayWindow 的贴图态——判据一个字没改，只是要跟着换文件找。</para>
    /// </summary>
    [Fact]
    public void TheUiOnlyAsksForTheOrigin_ItNoLongerAnchorsZoomAtTheCenter()
    {
        var cs = SourceGate.ReadRepoFile("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs");
        Assert.DoesNotContain("(current.Width - w) / 2", cs);
        Assert.DoesNotContain("ResizeKeepingCenter", cs);
        // 收边判据只有两处调用：拖动一处、"图 + 条"整窗一处（缩放与 90° 旋转都经 ApplyPinWindowRect
        // 走后者）——批次 WD-6 起窗口比图多下面那一条，收边必须按整窗算，三个动作各算一套必分岔
        Assert.Equal(2, SourceGate.Count(cs, "CaptureGeometry.PinOrigin("));
        Assert.Contains("ResizePinAnchoringTopLeft();", SourceGate.MethodBody(cs, "private void Root_PointerWheelChanged"));
        // 工作区取"这块屏"，不是主屏——多屏下按主屏夹会把贴图从副屏拽走
        Assert.Contains("WindowInterop.GetWorkArea(this)", cs);
    }
}
