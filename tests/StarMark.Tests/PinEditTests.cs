#nullable enable
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 PM：贴图可以就地继续编辑（复用截图那条标注链）。
/// <para>
/// 两件事分开钉：<b>回写判据</b>是纯函数（尺寸对不对、能不能写回），走契约测；
/// <b>"没有第二份编辑器"</b>与"编辑态收掉选区外壳"在 UI 层，只能扫源码钉住接线。
/// </para>
/// </summary>
public sealed class PinEditTests
{
    // ────────── 契约：能不能写回这张贴图 ──────────

    [Fact]
    public void MatchingBuffer_IsAccepted()
        => Assert.Null(CaptureGeometry.PinEditProblem(4, 3, new byte[4 * 3 * 4]));

    [Fact]
    public void NullPixelsMeansDiscarded_NotAFailedWriteBack()
        // 放弃本次编辑与"写回失败"是两件事：前者不该弹一条红字。
        => Assert.Null(CaptureGeometry.PinEditProblem(4, 3, null));

    [Fact]
    public void WrongSizeNamesBothNumbersSoTheUserCanTellWhichIsWhich()
    {
        var problem = CaptureGeometry.PinEditProblem(4, 3, new byte[16]);

        Assert.NotNull(problem);
        Assert.Contains("16", problem);          // 交回来的
        Assert.Contains("48", problem);          // 这张图该有的
        Assert.Contains("4 × 3", problem);
    }

    [Fact]
    public void OverflowedSizeCannotPassAsMatching()
    {
        // 46341×46341×4 = 8,589,953,124，按 int 乘会绕回 18,532。
        // 判据走 long，所以一份正好 18,532 字节的像素不能被当成"对得上"。
        Assert.NotNull(CaptureGeometry.PinEditProblem(46341, 46341, new byte[18_532]));
    }

    // ────────── 闸门：只有一份编辑器，接线要接对 ──────────

    [Fact]
    public void PinEditingReusesTheCaptureEditorInsteadOfASecondOne()
    {
        // 标注这一层被修过十几轮（尺寸回弹、文字两层、拖字变大…）。再写一份实现，
        // 每条修复就得在两份里各成立一次——那正是"另一份迟早漂移"的形状。
        var service = SourceGate.ReadRepoFile("src/StarMark.UI/Services/ScreenshotService.cs");
        var edit = SourceGate.MethodBody(service, "public static void EditPin(PinWindow pin)");

        Assert.Contains("new CaptureOverlayWindow(", edit);
        Assert.Contains("pin.EnterEditing()", edit);
        Assert.Contains("pin.EndEditing(result.Pixels, result.ClosePin)",
            SourceGate.MethodBody(service, "private static void FinishPinEdit"));
        // 服务自己不画标注：它只把像素交给那条链
        Assert.Equal(0, SourceGate.Count(service, "AnnotationPainter"));
    }

    [Fact]
    public void EditModeTurnsOffTheSelectionChrome()
    {
        var cs = SourceGate.ReadRepoFile("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs");
        var editCtor = SourceGate.MethodBody(cs, "double scale, double sourceScale,");

        // 编辑态没有"选区"：压暗与框选说明必须收起，否则用户会去拖一圈根本不存在的边界
        Assert.Contains("DimLayer.Visibility = Visibility.Collapsed", editCtor);
        Assert.Contains("HintChip.Visibility = Visibility.Collapsed", editCtor);
        Assert.Contains("BeginEditingExisting(pixels, width, height)", editCtor);

        // 而且不许"重新框一块"：那一按会把底图连同已画的标注一起丢掉
        var pressed = SourceGate.MethodBody(cs, "private void Root_PointerPressed");
        Assert.Contains("if (_editing) return;", pressed);
        Assert.Contains("!_editing && _base is not null && !Armed && TryBeginAdjust", pressed);
    }

    [Fact]
    public void EveryEditCommitWritesBackBeforeDoingTheAction()
    {
        // 用户画完点的是「复制」：不先写回，回到桌面看到的还是旧图——凭空丢一次改动。
        var commit = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs"),
            "private void Commit(CommitAction action)");

        Assert.Contains("if (_editing) FinishEdit(final.Pixels, closePin: false);", commit);
        Assert.True(commit.IndexOf("FinishEdit(final.Pixels", System.StringComparison.Ordinal)
                    < commit.IndexOf("switch (action)", System.StringComparison.Ordinal),
            "写回必须发生在动作之前（动作可能耗时，中间不能还留着没落定的编辑态）");
    }

    [Fact]
    public void FinalPixelsRenderAtSourceSize_NotDisplaySize()
    {
        // 贴图在 2.5× 时"选区矩形"是显示尺寸；按它渲染会得到一张放大过的糊图，
        // 而且字节数与贴图对不上（PinEditProblem 会拒绝它——但那是兜底，不是做法）。
        var body = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs"),
            "private (byte[] Pixels, int Width, int Height)? FinalPixels()");

        Assert.Contains("_contentWidth, _contentHeight", body);
        Assert.DoesNotContain("selection.Width, selection.Height", body);
    }

    [Fact]
    public void PointerMappingDividesTheZoomOut()
    {
        var cs = SourceGate.ReadRepoFile("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs");

        Assert.Contains("/ _sourceScale", SourceGate.MethodBody(cs, "private PixelPoint ToLocal"));
        Assert.Contains("* _sourceScale", SourceGate.MethodBody(cs, "private (double X, double Y) LocalToDip"));
    }

    [Fact]
    public void PinOffersThreeWaysIntoEditing()
    {
        // 双击 / 右键菜单 / 按 E 都要能进：贴图铺满屏幕时，"只能右键才能改"是最难受的那一种。
        var xaml = SourceGate.ReadRepoFile("src/StarMark.UI/Views/PinWindow.xaml");
        Assert.Contains("DoubleTapped=\"Root_DoubleTapped\"", xaml);
        Assert.Contains("Text=\"编辑这张（双击 / 按 E）\" Click=\"Edit_Click\"", xaml);

        var cs = SourceGate.ReadRepoFile("src/StarMark.UI/Views/PinWindow.xaml.cs");
        Assert.Contains("ScreenshotService.EditPin(this)",
            SourceGate.MethodBody(cs, "private void Root_KeyDown"));
    }
}
