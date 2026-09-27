#nullable enable
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 S2-b：<b>撤销退的是"全机最后落的那一笔"</b>（方案 §7 的栈全局化、§13 用例 9 的 LIFO）。
/// <para>从前画布的 <c>Undo()</c> 是 <c>foreach (screen) screen.Ink.Undo()</c>——一次按键把<b>每块屏各退一条</b>。
/// 单屏看不出问题，双屏上按一次撤销，两块屏同时各掉一笔，而掉的那两条根本不是同一时刻画的：
/// "撤错东西"比"撤不动"糟得多，因为它把用户没打算撤的东西悄悄抹了。
/// 修法是把"哪一笔最后落"这个问题交给模型答（<see cref="CanvasStroke.Order"/>），编排只问它。</para>
/// </summary>
public sealed class CanvasUndoOrderTests
{
    private const string Service = "src/StarMark.UI/Services/CanvasService.cs";

    private static CanvasInk Draw(PixelPoint from, PixelPoint to, CanvasTool tool = CanvasTool.Pen)
    {
        var ink = new CanvasInk();
        ink.Begin(tool, Annotation.Opaque(0x23, 0x11, 0xE8), 9, from);
        ink.Extend(to);
        ink.End();
        return ink;
    }

    [Fact]
    public void LaterStrokesCarryABiggerOrderEvenAcrossScreens()
    {
        var first = Draw(new PixelPoint(1, 1), new PixelPoint(30, 30));
        var second = Draw(new PixelPoint(400, 400), new PixelPoint(430, 430));
        Assert.True(second.Strokes[0].Order > first.Strokes[0].Order,
            "后落的那一笔序号必须更大——不然'谁最后'这件事没有答案，只能每屏各退一条");
    }

    [Fact]
    public void EveryScreenCompetesOnItsOwnNewestStroke()
    {
        var left = Draw(new PixelPoint(1, 1), new PixelPoint(30, 30));
        var right = Draw(new PixelPoint(400, 400), new PixelPoint(430, 430));
        // 左边再画一笔之后，"最后落的那一笔"就换边了（撤销跟着手走，不是跟着屏的顺序走）
        Assert.True(right.LastOrder > left.LastOrder);
        left.Begin(CanvasTool.Pen, Annotation.Opaque(0x23, 0x11, 0xE8), 9, new PixelPoint(60, 60));
        left.Extend(new PixelPoint(90, 90));
        left.End();
        Assert.True(left.LastOrder > right.LastOrder);
    }

    [Fact]
    public void AnEmptyScreenNeverWinsTheUndoChoice()
    {
        var drawn = Draw(new PixelPoint(1, 1), new PixelPoint(30, 30));
        Assert.Equal(long.MinValue, new CanvasInk().LastOrder);      // 空层排最后，不能被选中
        Assert.True(drawn.LastOrder > new CanvasInk().LastOrder);
    }

    [Fact]
    public void UndoingOneStrokeMovesThePointerBackToThePreviousOne()
    {
        var ink = new CanvasInk();
        ink.Begin(CanvasTool.Pen, Annotation.Opaque(0x23, 0x11, 0xE8), 9, new PixelPoint(1, 1));
        ink.Extend(new PixelPoint(30, 30));
        ink.End();
        var firstOrder = ink.LastOrder;
        ink.Begin(CanvasTool.Pen, Annotation.Opaque(0x23, 0x11, 0xE8), 9, new PixelPoint(50, 50));
        ink.Extend(new PixelPoint(80, 80));
        ink.End();
        Assert.True(ink.LastOrder > firstOrder);
        Assert.True(ink.Undo());
        Assert.Equal(firstOrder, ink.LastOrder);                     // 退一条，"这块屏最后落的那一笔"就退回前一条
        Assert.True(ink.Undo());
        Assert.False(ink.Undo());                                    // 没有笔迹可退时必须说"没退"，不能空转
    }

    /// <summary>
    /// 接线闸门：编排那边<b>只准挑一块屏退</b>。
    /// <para>钉"不许出现 <c>foreach ... Ink.Undo()</c>"是因为这个循环看起来人畜无害（"每屏各撤各的"注释还写着），
    /// 而它给的恰恰是错语义；只改判据不改注释的话，下一次有人照着注释把它写回来毫无阻力。</para>
    /// </summary>
    [Fact]
    public void TheBoardUndoesASingleScreenAndOnlyRepaintsThatOne()
    {
        var undo = SourceGate.MethodBody(SourceGate.ReadRepoFile(Service), "public static void Undo()");
        Assert.Contains(".OrderByDescending(s => s.Ink.LastOrder)", undo);
        Assert.Contains("if (target is null || !target.Ink.Undo()) return;", undo);
        Assert.Contains("Recomposite(target);", undo);
        Assert.Contains("Flush(target);", undo);
        // 钉这一句是因为那个循环看起来人畜无害、注释还写着"每屏各撤各的"——它给的恰恰是错语义。
        Assert.DoesNotContain("screen.Ink.Undo()", undo);
        Assert.DoesNotContain("FlushAll();", undo);      // 别的屏一笔没动，不该跟着整屏重烤（4K 一帧几十兆）
    }
}
