#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「同键换读数必须真贴上去」的闸门（✅P-261，Phase C 片131）。
/// <para>
/// 这颗缺陷只有真机看得见，而它一旦被人<b>顺手改回去</b>（把三条路重新写成裸 <c>SetValue</c>、
/// 或者把那颗共用出口的判据放宽成"总是 SetValue"）又只有真机看得见——台架与全量测试全绿而屏幕上念的是上一句。
/// 2026-10-28 沙盒实测：同一进程里第二发存图，盘上是 <c>…123300.png</c>、日志也说存了它，
/// 而右下角那张卡正文照旧念第一发的 <c>…122958.png</c>（抬头同理，只是那句恰好一字不差才没露脸）。
/// </para>
/// <para>
/// 根因形状：<c>LocText</c> 的取词发生在<b>附带属性的变更回调</b>里，而值没变回调就不响。
/// 三条<b>带读数</b>的路（正文／悬停说明／占位符）都是"实参进信箱 + 键进属性"，
/// 于是同一扇复用的窗第二次贴同一颗键时，新实参没人贴。提示卡按设计就是一扇复用的窗，
/// 它的两句永远是同两颗键（<c>card.title.withCategory</c> ＋ <c>common.rawValue</c>）⇒ 必中。
/// </para>
/// <para>
/// 下面三格各钉一维：① 三条带读数的路都走那颗共用出口（⛔ 再有人退回裸 SetValue）；
/// ② 那颗出口的判据是"键相等就自己贴"，比较用 <c>Ordinal</c>（换成文化敏感＝同一颗键可能被认成两颗，
/// 或反之）；③ 三颗回调各自只是把活交给对应的 <c>Apply…</c> 那一臂（⛔ 在回调里再写一份取词实现）。
/// </para>
/// </summary>
public sealed class LocTextRepasteGateTests
{
    private const string LocText = "src/StarMark.UI/Helpers/LocText.cs";

    [Fact]
    public void TheThreeArgsCarryingRoutesAllGoThroughTheSharedGate()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(LocText));

        Assert.Contains("SetOrRepaste(element, KeyProperty, GetKey(element), value, ApplyKey);", code, StringComparison.Ordinal);
        Assert.Contains("SetOrRepaste(element, ToolTipKeyProperty, GetToolTipKey(element), key, ApplyToolTipKey);", code, StringComparison.Ordinal);
        Assert.Contains("SetOrRepaste(element, PlaceholderKeyProperty, GetPlaceholderKey(element), key, ApplyPlaceholderKey);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSharedGateRepastesItselfWhenOnlyTheArgsChanged()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(LocText));

        // 键没变 ⇒ 属性变更回调不会响，这一臂必须自己贴；键真变了才交给 SetValue（回调照旧负责）
        Assert.Contains("if (string.Equals(current, value, StringComparison.Ordinal)) apply(element, value);", code, StringComparison.Ordinal);
        Assert.Contains("else element.SetValue(property, value);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EachChangedCallbackOnlyDelegatesToItsOwnApplyArm()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(LocText));

        Assert.Contains("=> ApplyKey(d, e.NewValue as string);", code, StringComparison.Ordinal);
        Assert.Contains("=> ApplyToolTipKey(d, e.NewValue as string);", code, StringComparison.Ordinal);
        Assert.Contains("=> ApplyPlaceholderKey(d, e.NewValue as string);", code, StringComparison.Ordinal);
    }
}
