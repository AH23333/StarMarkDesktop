#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「搜索即输入」的接线闸门（PO-4）。测试工程不引用 StarMark.UI，所以只能扫源码钉住两件事：
/// <b>键入这条路径确实走合并窗口</b>、<b>显式动作确实把待跑的合并关掉</b>。
/// <para>
/// 为什么值得钉：这条优化没有可断言的纯函数（合并的是"何时开跑"，不是数值），
/// 而它一旦被人"顺手改回去"（OnQueryChanged 直接 _ = SearchAsync()），代价是每敲一个字
/// 把整条链（整表 SQL + 多源合并 + 映射 + 卡片重建）跑一遍——正是真机反馈里"打字时卡"的来源。
/// RI-5 那条教训同理：规则写在没人经过的方法里等于没写，所以钉的是"谁调谁"。
/// </para>
/// </summary>
public sealed class SearchTypingMergeTests
{
    private const string Vm = "src/StarMark.UI/ViewModels/SearchPageViewModel.cs";

    [Fact]
    public void TypedQueryGoesThroughTheMergeWindow_NotStraightIntoTheSearch()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Vm), "partial void OnQueryChanged");

        Assert.Contains("SearchDebouncedAsync", body);
        Assert.DoesNotContain("_ = SearchAsync()", body);
    }

    [Fact]
    public void ExplicitSearchInvalidatesThePendingMerge()
    {
        // 回车/按钮/切来源排序语言都走 SearchAsync：它必须先把压着的那一轮作废，
        // 否则用户按完回车，150 ms 后还会被补一次一模一样的查询（两次结果交叠）。
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(Vm), "private async Task SearchAsync()");

        Assert.Contains("CancelPendingTypingSearch()", body);
        Assert.Contains("RunSearchAsync(append: false)", body);
    }

    [Fact]
    public void MergedRunReadsTheLiveQuery_NotASnapshot()
    {
        // 合并窗口到期后跑的是"当下那一句"：延时期一到就现取 Query。
        // 若这里改成把键入时的文本传下去，连打"abc"会变成搜"a"（旧文本快照的经典错）。
        var merged = SourceGate.MethodBody(SourceGate.ReadRepoFile(Vm), "private async Task SearchDebouncedAsync");
        Assert.Contains("RunSearchAsync(append: false)", merged);
        var run = SourceGate.MethodBody(SourceGate.ReadRepoFile(Vm), "private async Task RunSearchAsync(bool append)");
        Assert.Contains("Query.Trim()", run);
    }
}
