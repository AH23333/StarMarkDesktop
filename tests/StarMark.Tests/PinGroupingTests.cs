using System;
using System.Collections.Generic;
using StarMark.Abstractions.I18n;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 贴图组判据（批次 S4-③）。UI 层不引用测试工程，所以能被机器检的那部分判据全部住在这里：
/// 命名、上限、聚合态（尤其<b>空组</b>那条 <c>All()</c> 陷阱）、菜单措辞的方向、托盘命令号的编解码。
/// </summary>
[Collection("LocaleGlobal")]
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
        Assert.Equal(expectedLabel, PinGrouping.HideLabel(allHidden).Render());
        Assert.Equal(expectedNext, PinGrouping.NextHidden(allHidden));
    }

    [Theory]
    [InlineData(true, "取消这组忽略鼠标", false)]
    [InlineData(false, "这组忽略鼠标", true)]
    public void TheThroughWordingAndTheThroughDirectionAgree(bool allThrough, string expectedLabel, bool expectedNext)
    {
        Assert.Equal(expectedLabel, PinGrouping.ThroughLabel(allThrough).Render());
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
        // ✅WI-65 片4：那颗判据交的是「键＋实参」⇒ 这里比 Render() 之后的句子（⛔ 再比对象，坑表 #480）
        var atLimit = PinGrouping.LimitProblem(PinGrouping.MaxGroups)!.Render();
        Assert.Contains(PinGrouping.MaxGroups.ToString(), atLimit);
        Assert.Contains("关闭", atLimit);                     // 给出路，不是只说"不行"
        Assert.NotNull(PinGrouping.LimitProblem(PinGrouping.MaxGroups + 1));
        Assert.Null(PinGrouping.LimitProblem(0));
    }

    [Fact]
    public void NamesNeverReuseAndLabelsAlwaysCarryTheCount()
    {
        Assert.Equal("组 1", PinGrouping.NameOf(1).Render());
        Assert.Equal("组 12", PinGrouping.NameOf(12).Render());        // 序号只增不复用：删掉「组 1」不会再来一个「组 1」
        Assert.Equal("组 2（3 张）", PinGrouping.GroupLabel(PinGrouping.NameOf(2), 3).Render());
        Assert.Contains("0 张", PinGrouping.CloseLabel(0).Render());
        // "名字后面带张数"这个形状只许一个主人（批次 WI-46c）：关闭那一行是 GroupLabel 拼出来的，⛔ 自己再写一份「（N 张）」
        // ⚠ 比的是读数（坑表 #504）：两颗 LocMessage 的 Args 是数组、按引用比 ⇒ 直接 NotEqual 会静默空转
        Assert.Equal(PinGrouping.GroupLabel(LocMessage.Of("pin.groupClose"), 3).Render(),
                     PinGrouping.CloseLabel(3).Render());
        Assert.True(PinGrouping.ShouldDrop(0));
        Assert.False(PinGrouping.ShouldDrop(1));
    }

    /// <summary>
    /// <b>组名是念法，不是身份</b>（批次 WI-46c）：身份是序号，念法每次现算，所以换语言那一刻它跟着换。
    /// <para>从前这里是一颗 <c>const string NamePrefix = "组 "</c>，而 <c>PinGroup.Name</c> 把结果<b>存</b>进建组那一刻——
    /// 于是托盘与条上那几行从此停在旧语言里（形状闸门看不出来：那张名册的键还在被问）。</para>
    /// <para>⚠ 读数取的是<b>切过去之后</b>那一档：换语言的返回值（"变没变"）⛔ 当"换没换到"的探针（#112）。</para>
    /// </summary>
    [Fact]
    public void TheGroupNameIsReadEveryTimeItIsShown()
    {
        var before = Locale.Current;
        try
        {
            Locale.TrySet(AppLanguage.English);
            Assert.Equal(AppLanguage.English, Locale.Current);
            Assert.Equal("Group 7", PinGrouping.NameOf(7).Render());
            Assert.Equal("Group 7 (3 pins)", PinGrouping.GroupLabel(PinGrouping.NameOf(7), 3).Render());
            Assert.Equal("Close this group (3 pins)", PinGrouping.CloseLabel(3).Render());
            // ✅WI-65 片4：那一句现在交「键＋实参」，而夹在里面的两颗<b>名字</b>也是键 ⇒ 三处都在这一刻才取词
            var limit = PinGrouping.LimitProblem(PinGrouping.MaxGroups)!.Render();
            Assert.Contains("at most 8 groups", limit);
            Assert.DoesNotContain("贴图组", limit);            // 那句引用的名字也跟着换
            Assert.Contains(@"""Close this group"" row under ""Pin group""", limit);
        }
        finally
        {
            Locale.TrySet(before);
        }
        Assert.Equal(before, Locale.Current);
    }
    /// <summary>
    /// ✅WI-65 片5b：组标签交出来的是<b>还没取词的那句</b>，且<b>内层那颗名字也是键</b>。
    /// <para>缺陷现场在提示卡那一边：<c>PinManager</c> 从前把 <c>GroupLabel(...)</c> 的<b>成品串</b>塞进
    /// <c>LocMessage.Of("pin.card.groupedBody", …)</c> 的 <c>{0}</c> 槽 ⇒ 外层换语言也贴不动里层那半句
    /// （#393／P-196 那一族）。本批起组名与组标签都是「键＋实参」，塞进槽的就是键。</para>
    /// <para>⚠ 时序照坑表 #501：<b>造在前／换在中／贴在后</b>。每换一次语言再现造一句，那种坏形照样句句都对，
    /// 那一格就成了一扇永远绿的门；最后一行再要一条「旧那颗与换语言后新造那颗同形」＝话只有一个主人。</para>
    /// </summary>
    [Fact]
    public void TheGroupLabelsAreHandedOutUnrenderedSoTheCardSlotFollowsTheLanguage()
    {
        var before = Locale.Current;
        try
        {
            Locale.TrySet(AppLanguage.Chinese);
            var built = PinGrouping.GroupLabel(PinGrouping.NameOf(2), 3);
            Assert.Equal("pin.groupLabel", built.Key);
            Assert.Equal("pin.group.name", Assert.IsType<LocMessage>(built.Args![0]).Key);   // 槽里那颗名字也是键
            Assert.Equal(3, Assert.IsType<int>(built.Args![1]));
            Assert.Equal("组 2（3 张）", built.Render());

            Locale.TrySet(AppLanguage.English);                          // 换语言发生在贴之前
            Assert.Equal("Group 2 (3 pins)", built.Render());            // 内层那颗名字跟着外层一起换
            Assert.DoesNotContain("组", built.Render());
            Assert.Equal(built.Render(), PinGrouping.GroupLabel(PinGrouping.NameOf(2), 3).Render());
            Assert.Equal("Close this group (3 pins)", PinGrouping.CloseLabel(3).Render());
        }
        finally
        {
            Locale.TrySet(before);
        }
    }
}
