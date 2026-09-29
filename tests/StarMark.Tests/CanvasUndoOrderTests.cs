#nullable enable
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 S2-b／S2-c：<b>撤销退的是"全机最后落的那一笔"</b>（方案 §7 的栈全局化、§13 用例 9 的 LIFO）。
/// <para>从前画布的 <c>Undo()</c> 是 <c>foreach (screen) screen.Ink.Undo()</c>——一次按键把<b>每块屏各退一条</b>。
/// 单屏看不出问题，双屏上按一次撤销，两块屏同时各掉一笔，而掉的那两条根本不是同一时刻画的：
/// "撤错东西"比"撤不动"糟得多，因为它把用户没打算撤的东西悄悄抹了。
/// 修法是把"哪一笔最后落"这个问题交给模型答，编排只问它。</para>
/// <para>这一叠现在就是 <see cref="InkDoc"/>（归属＝Board＋屏号），序号来自 <see cref="InkOrder"/>——
/// 与截图/贴图那两叠同一个载体、同一份时钟，所以"谁最后落"在架构上只有一个答案。</para>
/// </summary>
public sealed class CanvasUndoOrderTests
{
    private const string Service = "src/StarMark.UI/Services/CanvasService.cs";

    /// <summary>一块屏的那一叠（Index 只是归属记号，这里比较的是序号不是屏号）。</summary>
    private static InkDoc Board() => new(new InkSurface(SurfaceRole.Board, 0));

    private static InkDoc Draw(PixelPoint from, PixelPoint to, CanvasTool tool = CanvasTool.Pen)
    {
        var doc = Board();
        doc.Add(Stroke(from, to, tool));
        return doc;
    }

    /// <summary>走实时那条路（构造 + AddPoint），与真机上拖出来的那一条同形。</summary>
    private static Annotation Stroke(PixelPoint from, PixelPoint to, CanvasTool tool = CanvasTool.Pen)
    {
        var stroke = new CanvasStroke(tool, Annotation.Opaque(0x23, 0x11, 0xE8), 9, from);
        stroke.AddPoint(to);
        return stroke.ToAnnotation();
    }

    [Fact]
    public void LaterStrokesCarryABiggerOrderEvenAcrossScreens()
    {
        var first = Draw(new PixelPoint(1, 1), new PixelPoint(30, 30));
        var second = Draw(new PixelPoint(400, 400), new PixelPoint(430, 430));
        Assert.True(second.Marks[0].Order > first.Marks[0].Order,
            "后落的那一笔序号必须更大——不然'谁最后'这件事没有答案，只能每屏各退一条");
    }

    [Fact]
    public void EveryScreenCompetesOnItsOwnNewestStroke()
    {
        var left = Draw(new PixelPoint(1, 1), new PixelPoint(30, 30));
        var right = Draw(new PixelPoint(400, 400), new PixelPoint(430, 430));
        // 左边再画一笔之后，"最后落的那一笔"就换边了（撤销跟着手走，不是跟着屏的顺序走）
        Assert.True(right.LastOrder > left.LastOrder);
        left.Add(Stroke(new PixelPoint(60, 60), new PixelPoint(90, 90)));
        Assert.True(left.LastOrder > right.LastOrder);
    }

    [Fact]
    public void AnEmptyScreenNeverWinsTheUndoChoice()
    {
        var drawn = Draw(new PixelPoint(1, 1), new PixelPoint(30, 30));
        Assert.Equal(InkOrder.None, Board().LastOrder);      // 空层排最后，不能被选中
        Assert.True(drawn.LastOrder > Board().LastOrder);
    }

    [Fact]
    public void UndoingOneStrokeMovesThePointerBackToThePreviousOne()
    {
        var doc = Board();
        doc.Add(Stroke(new PixelPoint(1, 1), new PixelPoint(30, 30)));
        var firstOrder = doc.LastOrder;
        doc.Add(Stroke(new PixelPoint(50, 50), new PixelPoint(80, 80)));
        Assert.True(doc.LastOrder > firstOrder);
        Assert.True(doc.Undo());
        Assert.Equal(firstOrder, doc.LastOrder);                     // 退一条，"这块屏最后落的那一笔"就退回前一条
        Assert.True(doc.Undo());
        Assert.False(doc.Undo());                                    // 没有笔迹可退时必须说"没退"，不能空转
    }

    [Fact]
    public void ClearingTheBoardIsUndoneLikeAnyOtherStroke()
    {
        // 换到 InkDoc 之后板子的"清空"和截图那侧同一套语义：误点一下不该把整块板子的墨一次性丢掉。
        // 从前画布那条是自己数的列表，清空＝不可撤销的既成事实；这一条是这次统一顺带带来的能力，钉住它别退回去。
        var doc = Draw(new PixelPoint(1, 1), new PixelPoint(30, 30));
        doc.Add(Stroke(new PixelPoint(50, 50), new PixelPoint(80, 80)));
        doc.Clear();
        Assert.Equal(0, doc.Count);
        Assert.True(doc.Undo());
        Assert.Equal(2, doc.Count);
    }

    /// <summary>
    /// 接线闸门：编排那边<b>只准挑一块屏退</b>。
    /// <para>钉"不许出现 <c>foreach ... Ink.Undo()</c>"是因为这个循环看起来人畜无害（"每屏各撤各的"注释还写着），
    /// 而它给的恰恰是错语义；只改判据不改注释的话，下一次有人照着注释把它写回来毫无阻力。</para>
    /// </summary>
    [Fact]
    public void TheBoardUndoesASingleScreenAndOnlyRepaintsThatOne()
    {
        var undo = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Service), "public static void Undo()");
        Assert.Contains(".OrderByDescending(s => s.Ink.LastOrder)", undo);
        Assert.Contains("if (target is null || !target.Ink.Undo()) return;", undo);
        Assert.Contains("Recomposite(target);", undo);
        Assert.Contains("Flush(target);", undo);
        // 钉这一句是因为那个循环看起来人畜无害、注释还写着"每屏各撤各的"——它给的恰恰是错语义。
        Assert.DoesNotContain("screen.Ink.Undo()", undo);
        Assert.DoesNotContain("FlushAll();", undo);      // 别的屏一笔没动，不该跟着整屏重烤（4K 一帧几十兆）
    }

    // ────────── 重做（批次 S2-c3：画布也有 redo）──────────

    [Fact]
    public void NextOrderIsExactlyTheStrokeRedoBringsBack()
    {
        var doc = Board();
        doc.Add(Stroke(new PixelPoint(1, 1), new PixelPoint(30, 30)));
        doc.Add(Stroke(new PixelPoint(50, 50), new PixelPoint(80, 80)));
        var newest = doc.LastOrder;
        Assert.Equal(InkOrder.None, doc.NextOrder);               // 没撤销过时"重做"不该赢任何比较
        doc.Undo();
        Assert.Equal(newest, doc.NextOrder);
        Assert.True(doc.Redo());
        Assert.Equal(InkOrder.None, doc.NextOrder);               // 回到了栈顶
    }

    [Fact]
    public void RedoTargetCannotBeChosenByLastOrder()
    {
        // 判别性输入（坑表 #150 的口径：随机采样采不出分歧）：左边画三条、右边画一条，
        // 两边各撤销一次之后，"还留着的那一笔最新"的是<b>左边</b>，而"刚被撤掉的那一笔"在<b>右边</b>。
        // 用 LastOrder 挑就退错一叠——放回去的不是用户刚撤的，比什么都不放更糟。
        var left = Board();
        left.Add(Stroke(new PixelPoint(1, 1), new PixelPoint(9, 9)));
        left.Add(Stroke(new PixelPoint(10, 10), new PixelPoint(18, 18)));
        left.Add(Stroke(new PixelPoint(20, 20), new PixelPoint(28, 28)));
        var right = Board();
        right.Add(Stroke(new PixelPoint(400, 400), new PixelPoint(430, 430)));

        Assert.True(left.Undo());
        Assert.True(right.Undo());
        Assert.True(left.LastOrder > right.LastOrder, "这条用例的前提：LastOrder 会把选择指到左边（那是错的答案）");
        Assert.True(right.NextOrder > left.NextOrder, "NextOrder 才指对用户刚撤掉的那一叠");

        var picked = new[] { left, right }.OrderByDescending(d => d.NextOrder).First();
        Assert.Same(right, picked);
    }

    [Fact]
    public void RedoAfterClearBringsTheWholeBoardBack()
    {
        var doc = Draw(new PixelPoint(1, 1), new PixelPoint(30, 30));
        doc.Add(Stroke(new PixelPoint(50, 50), new PixelPoint(80, 80)));
        doc.Clear();
        Assert.True(doc.Undo());                                  // 撤销"清空"＝整叠回来
        Assert.True(doc.Redo());                                  // 再重做＝又清空
        Assert.Equal(0, doc.Count);
    }

    /// <summary>接线闸门：重做与撤销同形——<b>只挑一叠、只重烤那一屏</b>，且挑的依据是 NextOrder。</summary>
    [Fact]
    public void TheBoardRedoesASingleScreenUsingTheOrderRedoWouldRestore()
    {
        var redo = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Service), "public static void Redo()");
        Assert.Contains("s.Ink.CanRedo", redo);
        Assert.Contains(".OrderByDescending(s => s.Ink.NextOrder)", redo);
        Assert.Contains("if (target is null || !target.Ink.Redo()) return;", redo);
        Assert.Contains("Recomposite(target);", redo);
        Assert.Contains("Flush(target);", redo);
        Assert.DoesNotContain("screen.Ink.Redo()", redo);
        Assert.DoesNotContain("FlushAll();", redo);
        // 撤销那一条不能用 NextOrder、重做那一条不能用 LastOrder：两个问题不同，写反一次就是"退错东西"。
        Assert.DoesNotContain("NextOrder", SourceGate.MethodBody(SourceGate.ReadRepoPartials(Service), "public static void Undo()"));
    }

    // ────────── 跨宿主落点（批次 S2-c3：全局撤销到底退哪一叠） ──────────

    /// <summary>
    /// 只有"前台那扇窗是贴图"时这一键才不归画布。桌面上挂着贴图 ≠ 用户在贴图上画画——
    /// 按"有没有贴图"判会把画布的撤销抢走，交给一张他根本没在看的贴图。
    /// </summary>
    [Theory]
    [InlineData(SurfaceRole.Pin, true)]
    [InlineData(SurfaceRole.Board, false)]
    [InlineData(SurfaceRole.Strip, false)]
    [InlineData(SurfaceRole.Sheet, false)]      // Sheet 到这步之前就回不来了（那批热键整批不注册），臂仍要显式给答案
    public void ThePinOnlyTakesTheGlobalUndoWhenItIsTheWindowInTheLight(SurfaceRole role, bool expected)
        => Assert.Equal(expected, InkRouting.UndoBelongsToFocusedPin(role));

    [Fact]
    public void NoFocusedWindowOrAnUnknownOneIsNeverAPin()
        => Assert.False(InkRouting.UndoBelongsToFocusedPin(null));

    /// <summary>接线闸门：落点判据只准在 Core 一份，UI 只问"前台是谁"再分派（WF-1 那一族的口径）。</summary>
    [Fact]
    public void TheRoutingDecisionLivesInCore_AndTheWiringOnlyAsksIt()
    {
        var body = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.UI/Services/AnnotationHub.cs"),
            "public static void HotkeyUndoRedo(bool redo)");
        Assert.Contains("InkRouting.UndoBelongsToFocusedPin(", body);
        Assert.DoesNotContain("SurfaceRole.Pin", body);                   // 比对角色这件事不在接线层
        Assert.Equal(1, SourceGate.Count(body, "GetForegroundWindow()")); // 前台窗只问一次（两次就可能问到不同的窗）
        Assert.Contains("if (redo) CanvasService.HotkeyRedo();", body);
        Assert.Contains("else CanvasService.HotkeyUndo();", body);

        var app = SourceGate.ReadRepoFile("src/StarMark.UI/App.xaml.cs");
        Assert.Contains("AnnotationHub.HotkeyUndoRedo(redo: false)", app);
        Assert.Contains("AnnotationHub.HotkeyUndoRedo(redo: true)", app);

        // 贴图自己那条豁免：正在输入文字时这一按属于输入框（撤字），不属于笔迹历史。
        var overlay = SourceGate.ReadRepoFile("src/StarMark.UI/Views/CaptureOverlayWindow.History.cs");
        Assert.Contains("if (_editingText) return;",
            SourceGate.MethodBody(overlay, "internal void HotkeyUndoRedo(bool redo)"));
    }
}
