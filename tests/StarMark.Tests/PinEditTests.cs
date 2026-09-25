#nullable enable
using System.IO;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 PM/PN：贴图那扇窗<b>本身就是</b>截图时的编辑器（用户口径：
/// "可以始终使用截图时的菜单，唯一改变的只有…是框选区域还是钉在桌面上的贴图"）。
/// <para>
/// 这一批没有可断言的纯函数（改的是"哪扇窗持有这条链"），所以钉的是**结构**：
/// 只允许有一条标注链、贴图态必须收掉选区外壳、坐标必须除掉缩放、工具条必须承担原来那份右键菜单。
/// 结构一旦被改回去（例如又开一扇"编辑窗"、或再写一份 PinWindow），这些闸门会红。
/// </para>
/// </summary>
public sealed class PinEditTests
{
    private const string Overlay = "src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs";
    private const string OverlayXaml = "src/StarMark.UI/Views/CaptureOverlayWindow.xaml";

    [Fact]
    public void ThereIsOnlyOnePinWindowClass_TheCaptureEditorItself()
    {
        // 第二份"贴图窗"就是第二份标注链的开始——PM 那一版正是这样，被用户退回。
        Assert.False(File.Exists(Path.Combine(SourceGate.RepoRoot(), "src/StarMark.UI/Views/PinWindow.xaml.cs")));

        var manager = SourceGate.ReadRepoFile("src/StarMark.UI/Services/PinManager.cs");
        Assert.Contains("List<CaptureOverlayWindow> Pins", manager);
        Assert.Contains("new CaptureOverlayWindow(bgra, width, height, placement", manager);
        Assert.Equal(0, SourceGate.Count(manager, "PinWindow"));
    }

    [Fact]
    public void PinnedWindowTurnsOffTheSelectionChrome()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);
        var ctor = SourceGate.MethodBody(cs, "double zoom)");

        // 整块都是内容：压暗、框选说明这些"选区外壳"必须收起，否则用户会去拖一圈不存在的边界
        Assert.Contains("DimLayer.Visibility = Visibility.Collapsed", ctor);
        Assert.Contains("HintChip.Visibility = Visibility.Collapsed", ctor);
        Assert.Contains("BeginEditingExisting(pixels, width, height)", ctor);
        Assert.Contains("PinManager.Unregister(this)", ctor);

        // 而"重新框一块 / 改框"两条分支都要被挡掉：那一按会连底图带已画标注一起清空
        var pressed = SourceGate.MethodBody(cs, "private void Root_PointerPressed");
        Assert.Contains("if (_pinned)", pressed);
        Assert.Contains("if (!Armed) BeginPinDrag(e.Pointer);", pressed);   // 没选笔＝不动笔 ⇒ 这一按是移动这张图
        Assert.Contains("!_pinned && _base is not null && !Armed && TryBeginAdjust", pressed);
        // 贴图态的"选区"＝整块画面，所以"未选笔"那一按先在选区内被拦下、根本走不到选区外面。
        // 这条就是真机反馈"贴图拖不动"的位置：没在里面接上 BeginPinDrag，移动就永远触发不了。
        Assert.Contains("if (_tool is not { } tool)", pressed);
        Assert.Contains("if (_pinned) BeginPinDrag(e.Pointer);", pressed);
    }

    /// <summary>
    /// 贴图这条工具条<b>只能压在画面上</b>（窗口就是那张图），而"截一小块钉住"常常没有一条工具条宽：
    /// 不整条缩进画面的话，被裁掉的正好是右边那几颗（复制 / 存图 / 识字 / 穿透 / 关闭）。
    /// </summary>
    [Fact]
    public void BarIsScaledDownSoTheRightHandButtonsStayReachable()
    {
        var bar = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void PositionBar");
        Assert.Contains("Math.Clamp((screenWidth - 8) / barWidth, 0.5, 1.0)", bar);
        // 摆放一律按缩放后的尺寸算：按原尺寸摆会把条子推出去一半
        Assert.Contains("x + w - scaledWidth", bar);
        Assert.Contains("y + h + 6 + scaledHeight", bar);
    }

    /// <summary>穿透中的贴图收不到鼠标，那条"悬停才收起"的工具条会一直留在画面上且没人点得动它。</summary>
    [Fact]
    public void ClickThroughPutsTheBarAway_BecauseTheWindowNoLongerSeesTheMouse()
    {
        var through = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "public bool ApplyClickThrough");
        Assert.Contains("if (_clickThrough) ActionBar.Visibility = Visibility.Collapsed;", through);
    }

    [Fact]
    public void PointerMappingDividesTheZoomOut_AndWheelKeepsItInStep()
    {
        // 不除掉缩放，2.5× 的贴图就会"鼠标明明在字上、笔落在字外"；
        // 滚轮改了倍率却忘了同步这个除数，是同一缺陷的另一半。
        var cs = SourceGate.ReadRepoFile(Overlay);

        Assert.Contains("/ _sourceScale", SourceGate.MethodBody(cs, "private PixelPoint ToLocal"));
        Assert.Contains("* _sourceScale", SourceGate.MethodBody(cs, "private (double X, double Y) LocalToDip"));
        var wheel = SourceGate.MethodBody(cs, "private void Root_PointerWheelChanged");
        Assert.Contains("_sourceScale = next;", wheel);
        Assert.Contains("ResizePinAnchoringTopLeft()", wheel);
    }

    [Fact]
    public void FinalPixelsRenderAtSourceSize_NotDisplaySize()
    {
        // 贴图在 2.5× 时"窗口矩形"是显示尺寸；按它渲染会得到一张拉伸过的糊图。
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay),
            "private (byte[] Pixels, int Width, int Height)? FinalPixels()");

        Assert.Contains("_contentWidth, _contentHeight", body);
        Assert.DoesNotContain("selection.Width, selection.Height", body);
    }

    [Fact]
    public void ToolbarCarriesWhatThePinContextMenuUsedTo()
    {
        // 用户口径：贴图那份右键菜单"完全可以"由截图时这条小菜单取代。所以菜单删了，
        // 但它承担过的动作必须都在条上——少一项就是功能被删掉了。
        var cs = SourceGate.ReadRepoFile(Overlay);
        Assert.DoesNotContain("AttachedFlyout", SourceGate.ReadRepoFile(OverlayXaml));

        var bar = SourceGate.MethodBody(cs, "private void BuildToolBar()");
        Assert.Contains("if (_pinned)", bar);
        Assert.Contains("Through_Click", bar);                                  // 鼠标穿透
        Assert.Contains("_pinned ? \"关闭这张（Esc）\"", bar);                    // 关闭这张
        Assert.Contains("if (!_pinned)", bar);                                  // "再钉一张"只在截图态出现
        Assert.Contains("CopyIcon()", bar);
        Assert.Contains("SaveIcon()", bar);
        Assert.Contains("OcrIcon()", bar);
    }

    [Fact]
    public void BarHidesOnLeaveButNeverWhileTheUserIsUsingAPen()
    {
        // 一屏十几张贴图，条子常驻会盖住画面；但"画着画着条没了"比看不见更烦。
        var cs = SourceGate.ReadRepoFile(Overlay);
        var exited = SourceGate.MethodBody(cs, "private void Root_PointerExited");

        Assert.Contains("Armed || _editingText || _polyLine is not null || _selected is not null", exited);
        Assert.Contains("ActionBar.Visibility = Visibility.Collapsed", exited);
    }

    [Fact]
    public void EscapeAndCrossCloseThePin_WhileCaptureStillCancelsTheShot()
    {
        var cs = SourceGate.ReadRepoFile(Overlay);

        Assert.Contains("if (_pinned) Close();", SourceGate.MethodBody(cs, "private void Cancel_Click"));
        var keys = SourceGate.MethodBody(cs, "private void Root_KeyDown");
        Assert.Contains("if (_pinned) Close();     // 贴图态：Esc＝关闭这张", keys);
        // 贴图态右键不能是"丢"：图已经画了一半，一次误触不该把它清空
        Assert.Contains("if (!_pinned) Settle(null);", SourceGate.MethodBody(cs, "private void Root_RightTapped"));
    }

    [Fact]
    public void PinnedWindowNeverDeliversASelectionToTheCaptureSession()
    {
        // 贴图不在截图会话里：交回选区会让服务去关"整场会话的遮罩窗"，那是一屏之外的另一批窗。
        var settle = SourceGate.MethodBody(SourceGate.ReadRepoFile(Overlay), "private void Settle(IntRect? selection)");
        Assert.Contains("if (!_pinned) _finish(this, selection);", settle);
    }
}
