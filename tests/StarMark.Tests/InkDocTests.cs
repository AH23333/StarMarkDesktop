#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 标注编辑历史（撤销/重做/清空）的状态契约。
/// <para>
/// 这一层最容易出的错不是"算不出来"，而是<b>分支残留</b>与<b>清空回不来</b>：
/// 撤销两步再画一条，"前进"却把用户刚画的东西换成一条他没选中的旧线；误点清空丢掉一整页标注。
/// 两条都有断言。
/// </para>
/// </summary>
public sealed class InkDocTests
{
    private static Annotation Mark(int x)
        => new(AnnotationTool.Line, new[] { new PixelPoint(x, 0), new PixelPoint(x + 5, 5) },
            Annotation.Opaque(0, 0, 255), 4);

    [Fact]
    public void FreshHistoryIsNothingToUndoOrRedo()
    {
        var history = new InkDoc();
        Assert.Empty(history.Marks);
        Assert.Equal(0, history.Count);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.False(history.Undo());          // 按到底了要返回 false，界面才灰得掉按钮
        Assert.False(history.Redo());
    }

    [Fact]
    public void AddUndoRedoRoundTrip()
    {
        var history = new InkDoc();
        history.Add(Mark(1));
        history.Add(Mark(2));
        Assert.Equal(2, history.Count);

        Assert.True(history.Undo());
        Assert.Single(history.Marks);
        Assert.True(history.CanRedo);

        Assert.True(history.Undo());
        Assert.Empty(history.Marks);
        Assert.False(history.CanUndo);

        Assert.True(history.Redo());
        Assert.True(history.Redo());
        Assert.Equal(2, history.Count);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void DrawingAfterUndoDiscardsTheRedoBranch()
    {
        var history = new InkDoc();
        history.Add(Mark(1));
        history.Add(Mark(2));
        history.Undo();
        history.Add(Mark(3));

        Assert.False(history.CanRedo, "旧分支必须作废：否则「前进」会画出一条用户已经放弃的线");
        Assert.Equal(2, history.Count);
        Assert.Equal(3, history.Marks[1].Points[0].X);   // 新画的那条在最后
    }

    [Fact]
    public void ClearIsUndoableAndRestoresTheOriginalOrder()
    {
        var history = new InkDoc();
        for (var i = 1; i <= 3; i++) history.Add(Mark(i));
        var before = history.Marks.Select(m => m.Points[0].X).ToList();

        history.Clear();
        Assert.Empty(history.Marks);
        Assert.True(history.CanUndo);

        Assert.True(history.Undo());
        Assert.Equal(before, history.Marks.Select(m => m.Points[0].X).ToList());   // 顺序不能乱（后画的盖在上面）
    }

    [Fact]
    public void ClearingAnEmptyHistoryAddsNoStep()
    {
        var history = new InkDoc();
        history.Clear();
        history.Clear();
        Assert.False(history.CanUndo, "空历史再点清空不该产生步骤，否则要点两次撤销才回到有标注");
    }

    [Fact]
    public void HistoryIsBoundedSoItCanSnapShotEveryStroke()
    {
        var history = new InkDoc();
        for (var i = 0; i < InkDoc.MaxStates + 15; i++) history.Add(Mark(i));
        Assert.Equal(InkDoc.MaxStates + 15, history.Count);   // 标注本身不丢

        var steps = 0;
        while (history.Undo()) steps++;
        Assert.InRange(steps, 1, InkDoc.MaxStates);           // 只有有界的步数可退
        Assert.Equal(InkDoc.MaxStates + 15 - steps, history.Count);
    }

    [Fact]
    public void ResetWipesEverythingIncludingTheHistory()
    {
        // 换选区时调用：底图都换了，旧标注摆在新框里没有任何意义
        var history = new InkDoc();
        history.Add(Mark(1));
        history.Add(Mark(2));
        history.Reset();
        Assert.Empty(history.Marks);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void MarksAreThePaintOrder()
    {
        var history = new InkDoc();
        var first = Mark(1);
        var second = Mark(2);
        history.Add(first);
        history.Add(second);
        Assert.Same(first, history.Marks[0]);
        Assert.Same(second, history.Marks[1]);
    }

    // ────────── 就地改一条（批次 RE-3：移动 / 缩放 / 转方向后落回历史）──────────

    [Fact]
    public void ReplacingTheSelectedMarkIsExactlyOneUndoableStep()
    {
        var history = new InkDoc();
        var original = Mark(1);
        history.Add(original);
        history.Add(Mark(2));

        history.ReplaceAt(1, Mark(99));
        Assert.Equal(99, history.Marks[1].Points[0].X);
        Assert.Same(original, history.Marks[0]);        // 没被改的那一条必须原样（不是重建出来的近似值）
        Assert.Equal(2, history.Count);                 // 改一条不会变成"删了再画一条"（计数不能变）
    }

    [Fact]
    public void UndoAfterAReplaceHandsBackThePreviousState()
    {
        var history = new InkDoc();
        history.Add(Mark(1));
        history.Add(Mark(2));
        var moved = Mark(2).MovedBy(3, 0);

        history.ReplaceAt(1, moved);
        Assert.Equal(5, history.Marks[1].Points[0].X);

        Assert.True(history.Undo());                    // 撤销一次改，不是撤销整条
        Assert.Equal(2, history.Count);
        Assert.Equal(2, history.Marks[1].Points[0].X);
    }

    [Fact]
    public void RemovingTheSelectedMarkKeepsTheRestInPaintOrder()
    {
        var history = new InkDoc();
        for (var i = 1; i <= 3; i++) history.Add(Mark(i));

        history.RemoveAt(1);
        Assert.Equal(new[] { 1, 3 }, history.Marks.Select(m => m.Points[0].X));

        Assert.True(history.Undo());
        Assert.Equal(new[] { 1, 2, 3 }, history.Marks.Select(m => m.Points[0].X));
    }

    [Fact]
    public void EditingAnExpiredIndexDoesNothingRatherThanThrow()
    {
        // 界面上选中记下的是个下标：撤销/清空之后它可能已经不属于任何一条。
        // 这里抛异常等于把用户一次普通的按键变成遮罩窗崩溃（遮罩一崩，整场截图就没了）。
        var history = new InkDoc();
        history.Add(Mark(1));
        history.Undo();                                 // 退回到"还没有标注"

        history.ReplaceAt(0, Mark(2));
        history.RemoveAt(0);
        history.ReplaceAt(-1, Mark(3));
        Assert.Empty(history.Marks);
        Assert.False(history.CanUndo, "越界的改不该制造一步空历史");
    }

    [Fact]
    public void EditingAfterUndoDiscardsTheRedoBranch()
    {
        var history = new InkDoc();
        history.Add(Mark(1));
        history.Add(Mark(2));
        history.Undo();

        history.ReplaceAt(0, Mark(7).MovedBy(0, 5));
        Assert.False(history.CanRedo, "改过的分支必须作废：否则「前进」会把用户刚改的位置换回旧的那一份");
        Assert.Equal(5, history.Marks[0].Points[0].Y);
    }

    // ────────── 落笔序号（§7：撤销要跨叠比"谁最后落"） ──────────

    [Fact]
    public void EmptyDocOrdersLastSoItIsNeverPickedAsTheNewest()
    {
        var empty = new InkDoc();
        Assert.Equal(InkOrder.None, empty.LastOrder);

        var drawn = new InkDoc();
        drawn.Add(Mark(1));
        Assert.True(drawn.LastOrder > InkOrder.None, "没画过的那一叠必须排最后，不能被选成「最后落的那一笔」");
    }

    [Fact]
    public void LastOrderFollowsTheNewestMarkAndStepsBackOnUndo()
    {
        var history = new InkDoc();
        history.Add(Mark(1));
        var first = history.Marks[0].Order;
        history.Add(Mark(2));
        var second = history.Marks[1].Order;

        Assert.True(second > first, "后落的那一笔序号必须更大——跨叠比较靠的就是这个大小关系");
        Assert.Equal(second, history.LastOrder);

        Assert.True(history.Undo());
        Assert.True(first == history.LastOrder,
            "撤回去之后「最新」要跟着退回前一笔，不然撤销会指着一条已经不存在的笔迹");

        Assert.True(history.Redo());
        Assert.Equal(second, history.LastOrder);
    }

    [Fact]
    public void MovingOrScalingAMarkKeepsItsOrder()
    {
        var history = new InkDoc();
        history.Add(Mark(3));
        var born = history.Marks[0].Order;

        history.ReplaceAt(0, history.Marks[0].MovedBy(1, 1));
        Assert.Equal(born, history.Marks[0].Order);
        Assert.True(born == history.LastOrder,
            "改几何是改那一条笔迹，不是又落了一笔：序号必须跟着走");
    }

    [Fact]
    public void TwoIdenticalLookingMarksAreDistinctSoTheEraserTakesOne()
    {
        // 用户能画出两条一模一样的矩形（同样的两个对角），而"擦掉其中一个"不该把另一个也带走。
        // 序号进到记录判等里就是为了这一条：从前 HashSet 按值判等，两条重影互相顶掉。
        var a = Mark(4);
        var b = Mark(4);
        Assert.NotEqual(a, b);

        var history = new InkDoc();
        history.Add(a);
        history.Add(b);

        history.BeginErase();
        history.ApplyErase(new HashSet<Annotation> { a });
        history.EndErase();

        Assert.Single(history.Marks);
        Assert.Equal(b.Order, history.Marks[0].Order);
    }
}
