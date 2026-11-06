#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 主窗搜索结果的「最小差异」闸门（片212·P-48）。测试工程不引用 StarMark.UI，
/// 钉的是源码形状：<b>整跑（逐键合并窗口每 150 ms 一拍）⛔ 整表 Clear+重建</b>——
/// 那会让每张卡片重放一遍 <c>EntranceThemeTransition</c>（同一批结果每键闪一次，
/// 真机反馈「打字时闪／卡」的那一半）。
/// <para>口径与组件侧同族先例一致（<c>TodoWidgetViewModel.SyncVisible</c>／
/// <c>ItemGridWidgetViewModel.ApplyRows</c>）：按预览去重键对账，屏幕内容没变 ⇒ 复用旧实例、
/// 只把高亮词就地换掉；内容变了 ⇒ 那一张换新。三颗集合按引用逐位对齐（Move／Insert）。</para>
/// <para>浏览态（<c>LoadBrowseAsync</c>）与 <c>Reset()</c> 的 Clear 是刻意保留的另一条路：
/// 单次转场（空词／离开页面），不在逐键热路径上——本闸只钉 <c>RunSearchAsync</c> 的两条臂。</para>
/// </summary>
public sealed class SearchResultsMinimalDiffGateTests
{
    private const string Vm = "src/StarMark.UI/ViewModels/SearchPageViewModel.cs";
    private const string Card = "src/StarMark.UI/ViewModels/ItemCardViewModel.cs";

    [Fact]
    public void TheFullRunReconcilesByKeyInsteadOfClearing()
    {
        var run = SourceGate.MethodBody(SourceGate.ReadRepoFile(Vm), "private async Task RunSearchAsync(bool append)");

        // 旧病：整跑一进来就把三颗集合推倒（成功臂与失败臂各一处）。
        Assert.DoesNotContain("Results.Clear()", run, StringComparison.Ordinal);
        Assert.DoesNotContain("ExactResults.Clear()", run, StringComparison.Ordinal);
        Assert.DoesNotContain("RelatedResults.Clear()", run, StringComparison.Ordinal);

        // 新形状：键＝单源静态、复用判据＝卡片上的深比、三颗集合＝按引用对齐。
        Assert.Contains("ItemCardViewModel.PreviewDedupeKeyOf(item)", run, StringComparison.Ordinal);
        Assert.Contains("old.SameDisplayContent(item)", run, StringComparison.Ordinal);
        Assert.Contains("SyncByReference(Results, fresh)", run, StringComparison.Ordinal);
        Assert.Contains("SyncByReference(ExactResults, exactTarget)", run, StringComparison.Ordinal);
        Assert.Contains("SyncByReference(RelatedResults, relatedTarget)", run, StringComparison.Ordinal);

        // 失败臂清场走同一颗对齐（对齐到空表），⛔ 留第二处「整表推倒」的写法。
        Assert.Contains("SyncByReference(Results, Array.Empty<ItemCardViewModel>())", run, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSyncHelperAlignsByReferenceMoveInsertNeverClear()
    {
        var sync = SourceGate.MethodBody(SourceGate.ReadRepoFile(Vm), "private static void SyncByReference");

        Assert.Contains(".Move(at, i)", sync, StringComparison.Ordinal);      // 位置错了挪
        Assert.Contains(".Insert(i, want)", sync, StringComparison.Ordinal);  // 缺了补
        Assert.Contains("ReferenceEqualityComparer.Instance", sync, StringComparison.Ordinal); // 按引用（⛔ 按 id：虚拟行恒 0）
        Assert.DoesNotContain(".Clear()", sync, StringComparison.Ordinal);    // 对齐那颗自身⛔ 推倒重来
    }

    [Fact]
    public void TheDedupeKeyHasOneSourceAndTheReuseCheckCoversTheScreen()
    {
        var card = SourceGate.ReadRepoFile(Card);

        // 键＝单源静态：对账发生在「还没建卡片」的那一拍，实例属性转投同一把尺，
        // 两处各写一份就会分岔（虚拟行的退路改了、一边没跟上，复用就会认错行）。
        Assert.Contains("public static string PreviewDedupeKeyOf(Item item)", card, StringComparison.Ordinal);
        Assert.Contains("public string PreviewDedupeKey => PreviewDedupeKeyOf(_item)", card, StringComparison.Ordinal);

        // 复用判据＝深比屏幕字段：少一颗就是「编辑了标题、屏上还是旧标题」的陈旧档。
        var same = SourceGate.MethodBody(card, "public bool SameDisplayContent(Item other)");
        foreach (var field in new[]
                 {
                     "Title", "Subtitle", "Uri", "Description", "Notes", "ExtraJson",
                     "StarsCount", "FileSize", "UpdatedAt", "Hidden", "Pinned", "Tags",
                 })
            Assert.Contains(field, same, StringComparison.Ordinal);

        // 屏幕外的字段进判据＝没变的卡片也被换掉（进场动画重放＝本刀要消的病）：
        // 这条钉「排除名单」里最易被顺手混进来的那颗（FTS 专用拼接文本，不上屏）。
        Assert.DoesNotContain("SearchText", same, StringComparison.Ordinal);
    }
}
