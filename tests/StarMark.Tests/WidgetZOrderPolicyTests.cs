#nullable enable
using System.Linq;
using Xunit;
using StarMark.Core.Widgets;

namespace StarMark.Tests;

/// <summary>
/// 空闲期组件 Z 序策略测试（对应 DeskBox IdleWidgetZOrderPolicy 的行为契约）。
/// 返回值第一个元素位于最上层。
/// </summary>
public sealed class WidgetZOrderPolicyTests
{
    private const string Display = "DISPLAY1";

    [Fact]
    public void LowerRows_AreKeptAboveUpperRows()
    {
        // 位置靠下的组件保持在上方，避免上方组件的投影压暗下方组件的顶边
        var upper = new WidgetZOrderCandidate(1, Display, Top: 100, Left: 0, "a");
        var lower = new WidgetZOrderCandidate(2, Display, Top: 400, Left: 0, "b");

        var ordered = WidgetZOrderPolicy.OrderHighestToLowest([upper, lower]);

        Assert.Equal(2L, ordered[0].WindowHandle);
        Assert.Equal(1L, ordered[1].WindowHandle);
    }

    [Fact]
    public void SameTop_PrefersLeftmostLast()
    {
        var left = new WidgetZOrderCandidate(1, Display, Top: 200, Left: 0, "a");
        var right = new WidgetZOrderCandidate(2, Display, Top: 200, Left: 500, "b");

        var ordered = WidgetZOrderPolicy.OrderHighestToLowest([left, right]);

        Assert.Equal(2L, ordered[0].WindowHandle);
    }

    [Fact]
    public void ZeroHandleCandidates_AreDropped()
    {
        var valid = new WidgetZOrderCandidate(7, Display, Top: 100, Left: 0, "a");

        var ordered = WidgetZOrderPolicy.OrderHighestToLowest([
            new WidgetZOrderCandidate(0, Display, Top: 900, Left: 0, "invalid"),
            valid
        ]);

        Assert.Single(ordered);
        Assert.Equal(7L, ordered[0].WindowHandle);
    }

    [Fact]
    public void DuplicateHandles_AreDeduplicated()
    {
        var ordered = WidgetZOrderPolicy.OrderHighestToLowest([
            new WidgetZOrderCandidate(5, Display, Top: 100, Left: 0, "first"),
            new WidgetZOrderCandidate(5, Display, Top: 300, Left: 0, "second")
        ]);

        Assert.Single(ordered);
        // 保留首个出现的候选
        Assert.Equal("first", ordered[0].StableKey);
    }

    [Fact]
    public void DisplaysAreGroupedBeforePosition()
    {
        var displayBLower = new WidgetZOrderCandidate(1, "DISPLAY2", Top: 900, Left: 0, "b-lower");
        var displayAUpper = new WidgetZOrderCandidate(2, "DISPLAY1", Top: 100, Left: 0, "a-upper");

        var ordered = WidgetZOrderPolicy.OrderHighestToLowest([displayBLower, displayAUpper]);

        Assert.Equal("DISPLAY1", ordered[0].DisplayKey);
        Assert.Equal("DISPLAY2", ordered[1].DisplayKey);
    }

    [Fact]
    public void FullTie_IsResolvedDeterministicallyByStableKey()
    {
        var ordered = WidgetZOrderPolicy.OrderHighestToLowest([
            new WidgetZOrderCandidate(1, Display, Top: 200, Left: 300, "z"),
            new WidgetZOrderCandidate(2, Display, Top: 200, Left: 300, "a")
        ]);

        Assert.Equal("a", ordered[0].StableKey);
        Assert.Equal("z", ordered[1].StableKey);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(WidgetZOrderPolicy.OrderHighestToLowest([]));
    }
}
