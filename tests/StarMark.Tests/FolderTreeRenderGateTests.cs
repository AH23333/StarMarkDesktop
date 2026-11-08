#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 文件夹树「展开更多」卡顿闸门（片241，扫源码不跑界面）。
/// <para>
/// 现场读数（starmark-20261108.log）：80 张已加载时单击「展开更多」＝UI 线程阻塞 1-2.5s、自耗 CPU ≈1.4s，
/// 根因是 RenderItems 每次点都 <c>itemsPanel.Children.Clear()</c> 后全量重建（每张 ItemCard ≈20ms 的 18KB XAML）。
/// 这里守三半，缺一算没做：① ⛔ 已渲染卡片清场重建；② 分帧封顶（每回合 ≤8 张，回合间让出 UI 线程）；
/// ③ 整树重建后旧代滞留批次自毙（bornEpoch/_treeEpoch），不往已分离面板塞卡。
/// </para>
/// </summary>
public sealed class FolderTreeRenderGateTests
{
    private const string Page = "src/StarMark.UI/Views/FolderTreePage.xaml.cs";

    private static string Code() => SourceGate.Code(SourceGate.ReadRepoFile(Page));

    /// <summary>增量第一律：面板不清场。Clear 一回来，单击「展开更多」又变整秒级重建——这是本批修掉的主现场。</summary>
    [Fact]
    public void ThePanelIsNeverBulkCleared()
    {
        Assert.DoesNotContain("itemsPanel.Children.Clear", Code());
    }

    /// <summary>分帧封顶常量必须存在且够小：一回合塞回 50 张等于把分帧做回了一帧。</summary>
    [Fact]
    public void FramesAreCappedSmall()
    {
        var code = Code();
        var m = System.Text.RegularExpressions.Regex.Match(code, @"CardRenderBatch\s*=\s*(\d+)");
        Assert.True(m.Success, "找不到 CardRenderBatch 常量（每回合卡片封顶）");
        Assert.True(int.Parse(m.Groups[1].Value) is > 0 and <= 8,
            $"每回合 {m.Groups[1].Value} 张太大，回合间让不出 UI 线程，分帧失去意义");
    }

    /// <summary>点击「展开更多」走的是增量路：按钮文案即时换（UpdateExpandButton）＋只补差额的分帧（RenderItems）。</summary>
    [Fact]
    public void ExpandMoreIsIncrementalNotRebuilt()
    {
        var code = Code();
        Assert.Contains("UpdateExpandButton();", code);
        Assert.Contains("RenderItems();", code);
        Assert.DoesNotContain("TryEnqueue(RenderItems)", code);   // 老路：直接把整批渲染排成一帧
    }

    /// <summary>整树重建（换语言/换主题/改标签重载）后，旧闭包的滞留批次必须比对代次自毙。</summary>
    [Fact]
    public void StaleRenderBatchesSelfTerminate()
    {
        var code = Code();
        Assert.Contains("_treeEpoch", code);
        Assert.Contains("bornEpoch != _treeEpoch", code);
    }

    /// <summary>增量游标必须真实存在且被推进：没有 rendered 计数，「只补差额」就是一句空话。</summary>
    [Fact]
    public void TheIncrementalCursorAdvances()
    {
        var code = Code();
        Assert.Contains("int rendered = 0", code);
        Assert.Contains("rendered++", code);
        Assert.Contains("ViewModel.HydrateRange(node, rendered, take)", code);   // 取数从游标起，不再从 0 全取
    }
}
