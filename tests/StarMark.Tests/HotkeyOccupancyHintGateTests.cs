#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 组合键被占用时"看得见失败 ＋ 点得动的出路"这条接线（批次 SW；P-54 → P-136）。
/// <para>
/// UI 层出不了契约测（#184：测试工程不引用 StarMark.UI），所以这里守的是接线形状。
/// 值得守的点很具体：<b>那句指路的话只有一个出处</b>（Core 的 <c>HotkeyErrorText.RebindHint</c>），
/// 界面把它拼到失败那一行上；页底汇总不再另抄一份——同一件事两处各写一遍，下次改措辞只会改到一半。
/// </para>
/// </summary>
public sealed class HotkeyOccupancyHintGateTests
{
    private const string Partial = "src/StarMark.UI/Views/SettingsPage.Hotkeys.cs";
    private const string Judge = "src/StarMark.Core/Hotkeys/HotkeyErrorText.cs";

    private static string Code(string file) => SourceGate.Code(SourceGate.ReadRepoFile(file));

    /// <summary>
    /// 逐行提示必须把 Core 那句指路拼上。少了它，界面就退回"报完被占用就停手"——
    /// 而那正是 P-136 登记的原始症状（设置页显示这颗键已绑上，按下去没反应，也没说下一步做什么）。
    /// </summary>
    [Fact]
    public void TheFailingRowCarriesTheWayOut()
        => Assert.Contains("HotkeyErrorText.RebindHint",
            SourceGate.Between(Code(Partial), "row.RegisterErrorText =", "RegisterErrorSummary ="), StringComparison.Ordinal);

    /// <summary>
    /// 反向也钉：<b>换键那句话在界面里一份都不许有</b>，在判据那颗里恰好一份。
    /// 两个方向都钉，才不是"看着像有守门"（#195 那条口径）。
    /// </summary>
    [Fact]
    public void TheRebindSentenceHasExactlyOneSource()
    {
        Assert.DoesNotContain("换一个组合", Code(Partial), StringComparison.Ordinal);
        Assert.Equal(1, SourceGate.Count(Code(Judge), "换一个组合"));
    }

    /// <summary>
    /// 汇总句仍要点名「重试注册」（自愈之外还得留一个立刻能按的出口），且<b>不许把人支使去重启</b>；
    /// 换键的指引改由每一行自己说，所以汇总里那句"怎么写"必须是<b>指向行</b>的，不是重复一遍做法。
    /// </summary>
    [Fact]
    public void TheSummaryStillPointsAtTheImmediateRetry()
    {
        var summary = SourceGate.Between(Code(Partial), "RegisterErrorSummary =", "private void RetryHotkeyRegister_Click");

        Assert.Contains("重试注册", summary, StringComparison.Ordinal);
        Assert.Contains("该行的提示", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("重启", summary, StringComparison.Ordinal);
    }
}
