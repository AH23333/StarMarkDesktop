#nullable enable
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using Xunit;
using E = StarMark.Core.Capture.CaptureGeometry.SelectionEdge;

namespace StarMark.Tests;

/// <summary>
/// 框选之后"改框"这一态的判据（批次 PK）：哪一按是移动、哪一按改哪条边、改完落在哪里。
/// <para>
/// 这批把"框选完自动拿着一支矩形笔"改成"先给你十字箭头：能拖框、能改边缘，
/// 每支工具点一次选中、再点一次取消"。选区决定<b>截到哪块画面</b>，所以它的几何
/// 与平移量必须在能断言的这一层算清楚——界面上算错一格，用户看到的是标注与画面错位，
/// 而那只有眼睛看得见。
/// </para>
/// </summary>
public sealed class SelectionAdjustTests
{
    private static readonly IntRect Screen = new(0, 0, 1920, 1040);
    private static readonly IntRect Box = new(100, 200, 400, 300);   // 右下角 (500,500)

    // ────────── 这一按落在哪个部位 ──────────

    [Theory]
    [InlineData(100, 200, E.TopLeft)]         // 角先于边：那 8×8 同时属于两条边，判反了会拖出相反的方向
    [InlineData(500, 200, E.TopRight)]
    [InlineData(100, 500, E.BottomLeft)]
    [InlineData(500, 500, E.BottomRight)]
    [InlineData(100, 350, E.Left)]
    [InlineData(500, 350, E.Right)]
    [InlineData(300, 200, E.Top)]
    [InlineData(300, 500, E.Bottom)]
    [InlineData(300, 350, E.Move)]            // 框内＝整块移动
    [InlineData(0, 0, E.None)]                // 框外＝放行给"重新框一块"
    [InlineData(560, 350, E.None)]
    public void ThePressIsClassified_CornersThenEdgesThenInside(int x, int y, E want)
        => Assert.Equal(want, CaptureGeometry.SelectionEdgeAt(Box, new PixelPoint(x, y), 8));

    [Theory]
    [InlineData(8, E.Left)]                   // 容差边界：把手画在框外一侧，不放宽就点不着
    [InlineData(9, E.None)]
    public void TheToleranceBandIsInclusive(int offset, E want)
        => Assert.Equal(want, CaptureGeometry.SelectionEdgeAt(Box, new PixelPoint(Box.X - offset, 350), 8));

    [Theory]
    [InlineData(3000, 0, 1520, 200)]         // 向右拖不出屏：贴右边缘为止（1920 - 400）
    [InlineData(-1000, -1000, 0, 0)]
    [InlineData(30, -20, 130, 180)]          // 屏内位移原样跟上
    public void MovingStaysInsideTheScreen_AndKeepsTheSize(int dx, int dy, int wantX, int wantY)
    {
        var moved = CaptureGeometry.MoveSelection(Box, dx, dy, Screen);
        Assert.Equal(wantX, moved.X);
        Assert.Equal(wantY, moved.Y);
        Assert.Equal((400, 300), (moved.Width, moved.Height));
    }

    // ────────── 改大小：对面钉住 ──────────

    [Fact]
    public void DraggingTheLeftEdgePinsTheRightOne()
    {
        var r = CaptureGeometry.ResizeSelection(Box, E.Left, 60, 0, Screen, 10);
        Assert.Equal((160, 200, 340, 300), (r.X, r.Y, r.Width, r.Height));
        Assert.Equal(500, r.Right);           // 对面那条边一格没动
    }

    [Fact]
    public void DraggingTheBottomRightCornerMovesBothEdges()
    {
        var r = CaptureGeometry.ResizeSelection(Box, E.BottomRight, 40, -40, Screen, 10);
        Assert.Equal((100, 200, 440, 260), (r.X, r.Y, r.Width, r.Height));
    }

    [Theory]
    [InlineData(E.Left, 9999)]                // 往里拉到底：只能收到最小边长，不许翻面（负宽高会算出反向边界）
    [InlineData(E.Right, -9999)]
    public void MinSideHoldsInBothDirections(E edge, int dx)
    {
        var r = CaptureGeometry.ResizeSelection(Box, edge, dx, 0, Screen, 24);
        Assert.True(r.Width >= 24, $"最小边长没夹住：{r.Width}");
        Assert.True(r.X >= Screen.X && r.Right <= Screen.Right);   // 也不许越过本屏工作区
    }

    [Theory]
    [InlineData(E.Top, -9999, 0)]
    [InlineData(E.Bottom, 9999, 1040)]
    public void EdgesStopAtTheWorkArea(E edge, int dy, int wantEdge)
    {
        var r = CaptureGeometry.ResizeSelection(Box, edge, 0, dy, Screen, 10);
        Assert.Equal(wantEdge, edge == E.Top ? r.Y : r.Bottom);
        Assert.True(r.Height >= 10);
    }

    [Theory]
    [InlineData(E.Move)]                      // Move/None 走的是"移动"那条路，这里不该改大小
    [InlineData(E.None)]
    public void TheNonResizeCasesLeaveTheBoxAlone(E edge)
        => Assert.Equal(Box, CaptureGeometry.ResizeSelection(Box, edge, 50, 50, Screen, 10));

    // ────────── 改框之后：标注跟着画面走，且不制造一步历史 ──────────

    [Fact]
    public void ShiftingTheMarksAnchorsPointsAndTheScalePivot()
    {
        var history = new AnnotationHistory();
        history.Add(new Annotation(AnnotationTool.Text, new[] { new PixelPoint(30, 40) }, Annotation.Opaque(0, 0, 255), 4)
        { Text = "一行字", FontHeight = 22, Pivot = new PixelPoint(10, 12) });
        history.Add(new Annotation(AnnotationTool.Rectangle, new[] { new PixelPoint(1, 2), new PixelPoint(9, 9) },
            Annotation.Opaque(255, 0, 0), 3));

        history.ShiftAllBy(-20, 10);          // 选区原点向右移了 20 ⇒ 标注在局部坐标里要向左回去

        var text = history.Marks[0];
        Assert.Equal(new PixelPoint(10, 50), text.Points[0]);
        Assert.Equal(new PixelPoint(-10, 22), text.Pivot);          // 缩放轴一起走，否则下一次缩放会跳到旧位置
        Assert.Equal(new PixelPoint(-19, 12), history.Marks[1].Points[0]);
    }

    [Fact]
    public void MovingTheFrameIsNotAnUndoStep()
    {
        var history = new AnnotationHistory();
        history.ShiftAllBy(7, 7);                       // 空栈上平移：不凭空造出一步
        Assert.False(history.CanUndo);

        history.Add(new Annotation(AnnotationTool.Ellipse, new[] { new PixelPoint(5, 5), new PixelPoint(50, 50) },
            Annotation.Opaque(0, 255, 0), 3));
        history.ShiftAllBy(12, 12);
        Assert.True(history.CanUndo);                   // 还是只有"画了一条"那一步
        Assert.False(history.CanRedo);
        Assert.Equal(new PixelPoint(17, 17), history.Marks[0].Points[0]);
        Assert.True(history.Undo());                    // 一步就退回空：平移没被记成第二步
        Assert.Empty(history.Marks);
    }

    [Fact]
    public void AZeroShiftChangesNothing_AndEmptyHistoryIsSafe()
    {
        var history = new AnnotationHistory();
        history.ShiftAllBy(0, 0);
        Assert.Empty(history.Marks);
    }

    /// <summary>
    /// 界面那侧只钉结构：未选工具时不许起笔、改框不许再"重裁底图＋平移标注"（批次 PU 起标注跟屏走）、
    /// 光标走 CursorLayer。测试工程引用不到 UI，所以这三条只能扫源码——它们各自对应一种"只有真机看得见"的错。
    /// </summary>
    [Fact]
    public void TheUiGuardsStayInTheShapesTheTestsCanBeAbout()
    {
        var cs = SourceGate.ReadRepoPartials("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs");
        // ① 未选工具 ⇒ 这一按是改框（Move 那条在抓标注之后接手），且改完不再走到"画一笔"
        var press = SourceGate.MethodBody(cs, "private void Root_PointerPressed");
        // 边与角优先于落笔（Snipaste 同款），但 SelectionEdgeAt 对框内一律回 Move——
        // Move 不许在落笔前劫持那一按（真机反馈"点了编辑工具反而把框拖走、笔落不上"的真因）
        Assert.Contains("edge is not (CaptureGeometry.SelectionEdge.None or CaptureGeometry.SelectionEdge.Move)", press);
        Assert.Contains("BeginAdjust(CaptureGeometry.SelectionEdge.Move, physical, e.Pointer);", press);
        Assert.DoesNotContain("!Armed && TryBeginAdjust", press);
        Assert.Contains("if (_tool is not { } tool)", press);   // 未选笔这一按不起笔（贴图态那里是移动整张图）
        Assert.True(press.IndexOf("BeginAdjust(CaptureGeometry.SelectionEdge.Move", System.StringComparison.Ordinal)
                    < press.IndexOf("BeginToolStroke(", System.StringComparison.Ordinal),
            "改框的分支排在起笔之后＝未选工具时那一按仍会被当成画");
        // ② 批次 PU：改框<b>不再重裁底图、不再平移标注</b>——标注跟屏走，选区只是"裁到哪"的记号
        var adjust = SourceGate.MethodBody(cs, "private void AdjustTo");
        Assert.Contains("_selection = _adjustPending;", adjust);
        Assert.DoesNotContain("_history.ShiftAllBy", cs);       // 那对"重裁＋平移"必须整体消失
        Assert.DoesNotContain("GdiScreenCapture.Crop(new FrameCopyRequest(frame, ox, oy, next.Width", cs);
        // ③ 光标：WinUI 3 没有 UIElement.PointerCursor，只能走 CursorLayer 暴露的 ProtectedCursor
        Assert.Contains("Root.Cursor = Microsoft.UI.Input.InputSystemCursor.Create(", cs);
        Assert.Contains("public sealed class CursorLayer : Grid",
            SourceGate.ReadRepoFile("src/StarMark.UI/Views/CursorLayer.cs"));
        Assert.Contains("<local:CursorLayer",
            SourceGate.ReadRepoFile("src/StarMark.UI/Views/CaptureOverlayWindow.xaml"));
        // ④ 进入编辑态＝一支笔都没选（工具条不预先高亮，光标是十字箭头）。
        //    批次 PM 之后这份"摆底图"的动作由截图与贴图编辑共用（PU 起叫 EnterEditing），
        //    规则本身一字未改；锚点扫不到就说明结构变了，正是它该红的时候。
        var enter = SourceGate.MethodBody(cs, "private void EnterEditing(");
        Assert.Contains("SyncTools();", enter);
        Assert.Contains("ApplyCursor();", enter);
    }

    /// <summary>
    /// 工具的选中态没有"默认那支笔"。批次 WD-6 起口径：点笔一次＝选中＋弹出颜色/粗细浮层，
    /// <b>再点当前这支＝收笔</b>（两条收笔出口：再点一次与 Esc，见
    /// TwoStageCaptureTests.RepeatingTheSelectedPenHolstersIt_AndThePickerOnlyOpensOnSelection）；
    /// 这里只钉"不许有隐形默认工具"这条不变的底线。
    /// </summary>
    [Fact]
    public void NoToolIsSilentlyPreselected()
    {
        var cs = SourceGate.ReadRepoPartials("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs");
        Assert.Contains("private AnnotationTool? _tool;", cs);              // 没有"默认那支笔"这回事
        Assert.DoesNotContain("_tool = AnnotationTool.Rectangle;", cs);     // 也不许在别处偷偷选上
        Assert.Contains("private void SetTool(AnnotationTool? tool)", cs);
        // 换颜色/粗细的入口保留右键一路（批次 PV：点笔一次＝选中＋弹浮层）
        Assert.Contains("button.RightTapped += BrushTool_RightTapped;", cs);
    }

    /// <summary>正在画的那一笔用的是哪支笔，在按下那一刻就钉住：中途换工具不许改正在画的这一笔。</summary>
    [Fact]
    public void AStrokeInFlightKeepsTheToolThatStartedIt()
    {
        var cs = SourceGate.ReadRepoPartials("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs");
        // 批次 PU 起"框内/框外"两条落笔路共用一个分发（BeginToolStroke），笔种在按下那一刻钉进去
        var dispatch = SourceGate.MethodBody(cs, "private void BeginToolStroke");
        Assert.Contains("_strokeTool = tool;", dispatch);
        var press = SourceGate.MethodBody(cs, "private void Root_PointerPressed");
        Assert.Contains("if (_tool is not { } tool)", press);
        Assert.Contains("BeginToolStroke(physical, e.Pointer);", press);
        Assert.Contains("private AnnotationTool _strokeTool", cs);
        Assert.Contains("BeginStroke(PixelPoint local, Pointer pointer, AnnotationTool tool)", cs);
        Assert.DoesNotContain("new Annotation(_tool,", cs);   // 落笔用 _strokeTool，不再读那个可变的当前工具
    }
}
