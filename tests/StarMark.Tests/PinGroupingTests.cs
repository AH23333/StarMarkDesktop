using System;
using System.Collections.Generic;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 贴图组判据（批次 S4-③）。UI 层不引用测试工程，所以能被机器检的那部分判据全部住在这里：
/// 命名、上限、聚合态（尤其<b>空组</b>那条 <c>All()</c> 陷阱）、菜单措辞的方向、托盘命令号的编解码。
/// </summary>
public sealed class PinGroupingTests
{
    [Fact]
    public void AnEmptyGroupIsNeverAggregatedAsHidden()
    {
        // All() 对空序列返回 true。照那个语义走，托盘会在一个刚被关空的组上显示「显示这一组」——
        // 一个底下没有任何贴图可显示的假出口，而且点了确实什么都没发生。
        Assert.False(PinGrouping.AllIn(Array.Empty<bool>()));
        Assert.True(PinGrouping.AllIn(new[] { true }));
        Assert.False(PinGrouping.AllIn(new[] { true, false }));
        Assert.True(PinGrouping.AllIn(new[] { true, true }));
    }

    /// <summary>措辞与执行方向必须同向：标签说「显示」而实际再隐藏一次，是全绿而功能坏。</summary>
    [Theory]
    [InlineData(true, "显示这一组", false)]
    [InlineData(false, "隐藏这一组", true)]
    public void TheHideWordingAndTheHideDirectionAgree(bool allHidden, string expectedLabel, bool expectedNext)
    {
        Assert.Equal(expectedLabel, PinGrouping.HideLabel(allHidden));
        Assert.Equal(expectedNext, PinGrouping.NextHidden(allHidden));
    }

    [Theory]
    [InlineData(true, "取消这组忽略鼠标", false)]
    [InlineData(false, "这组忽略鼠标", true)]
    public void TheThroughWordingAndTheThroughDirectionAgree(bool allThrough, string expectedLabel, bool expectedNext)
    {
        Assert.Equal(expectedLabel, PinGrouping.ThroughLabel(allThrough));
        Assert.Equal(expectedNext, PinGrouping.NextThrough(allThrough));
    }

    [Fact]
    public void GroupTagsRoundTripAndNeverCollideWithHostTags()
    {
        for (var serial = 1; serial <= 200; serial++)
            foreach (PinGrouping.Action action in Enum.GetValues<PinGrouping.Action>())
            {
                var tag = PinGrouping.TagOf(serial, action);
                Assert.Equal(serial, PinGrouping.SerialOf(tag));
                Assert.Equal(action, PinGrouping.ActionOf(tag));
                Assert.True(PinGrouping.IsGroupTag(tag));
                // 宿主自己那批 tag（截图/贴图/主题/性能/开机自启…）全在 1..99
                Assert.True(tag > 99);
            }

        // 号段外的数、以及留给未来的余量位都不算组命令：宁可点不动，也不能点错。
        Assert.False(PinGrouping.IsGroupTag(PinGrouping.TagBase - 1));
        Assert.False(PinGrouping.IsGroupTag(0));
        Assert.False(PinGrouping.IsGroupTag(PinGrouping.TagOf(7, PinGrouping.Action.Show) + 3));
    }

    /// <summary>动作数值进命令号 ⇒ 只能追加。重排会让同一串号在旧版托盘缓存/新解码之间指错动作。</summary>
    [Fact]
    public void TheActionNumbersArePinned()
    {
        Assert.Equal(0, (int)PinGrouping.Action.Show);
        Assert.Equal(1, (int)PinGrouping.Action.Through);
        Assert.Equal(2, (int)PinGrouping.Action.Close);
        Assert.Equal(4, PinGrouping.TagStride);   // 三个动作 + 一个余量位；余量位不算组命令
    }

    [Fact]
    public void TheGroupLimitSpeaksAReasonAtTheBoundary()
    {
        Assert.Null(PinGrouping.LimitProblem(PinGrouping.MaxGroups - 1));
        var atLimit = PinGrouping.LimitProblem(PinGrouping.MaxGroups);
        Assert.NotNull(atLimit);
        Assert.Contains(PinGrouping.MaxGroups.ToString(), atLimit!);
        Assert.Contains("关闭", atLimit!);                     // 给出路，不是只说"不行"
        Assert.NotNull(PinGrouping.LimitProblem(PinGrouping.MaxGroups + 1));
        Assert.Null(PinGrouping.LimitProblem(0));
    }

    [Fact]
    public void NamesNeverReuseAndLabelsAlwaysCarryTheCount()
    {
        Assert.Equal("组 1", PinGrouping.NameOf(1));
        Assert.Equal("组 12", PinGrouping.NameOf(12));        // 序号只增不复用：删掉「组 1」不会再来一个「组 1」
        Assert.Equal("组 2（3 张）", PinGrouping.GroupLabel(PinGrouping.NameOf(2), 3));
        Assert.Contains("0 张", PinGrouping.CloseLabel(0));
        Assert.True(PinGrouping.ShouldDrop(0));
        Assert.False(PinGrouping.ShouldDrop(1));
    }
}
