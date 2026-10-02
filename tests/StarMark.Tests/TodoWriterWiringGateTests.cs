#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 待办组件的<b>接线</b>闸门（批次 UM）：VM 里每颗公开写入方法都必须有人调它。
/// <para>
/// 起因是重量 R7 那批"☆ 没读码复核"的前提时查到的一颗真缺陷：<c>SetDueAsync</c> 自批次 <c>2c6ec59</c>
/// 就存在（带着错误文案、视图里预留了截止日期那一行、类注释还把它列进"用户最能感知的四项"），
/// 但<b>视图里一个调用点都没有</b>。于是"截止日期"只剩展示侧：用户设不出截止日，
/// 而 <c>DescribeDue</c> 的"今天/明天/逾期"、按截止排序、<c>IsOverdue</c> 全部跟着变成永远不触发的那几行——
/// 五千多格测试里没有一格会红（与 KJ／WF-1 是同族：<b>缺一块接线，不写失败也不报错</b>）。
/// </para>
/// <para>
/// 所以这里钉两层：① <b>逐颗</b>"公开 Async 写入方法必须被调"，以后新加的写入方法自动被这条管住；
/// ② 截止日期那颗的具体形状——三个出口、整条调用式含实参、算法只许走
/// <see cref="StarMark.Abstractions.LocalItemState.DayOffsetStartUnix"/>，
/// 不许退回 <c>DateTimeOffset.Now.AddDays</c>（定长 24 绝对小时在夏令时回拨那天会跳错日历日，P-37）。
/// </para>
/// </summary>
public sealed class TodoWriterWiringGateTests
{
    private const string VmPath = "src/StarMark.UI/ViewModels/TodoWidgetViewModel.cs";
    private const string ViewPath = "src/StarMark.UI/Views/TodoWidget.xaml";
    private const string CodeBehindPath = "src/StarMark.UI/Views/TodoWidget.xaml.cs";

    /// <summary>VM 公开写入方法的形状：<c>public [async] Task XxxAsync(</c>。</summary>
    private static readonly Regex PublicWriter = new(
        @"public\s+(?:async\s+)?Task\s+(\w+Async)\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// 一颗方法算"接上了"，当且仅当：视图 code-behind 直接调它，<b>或</b>它在本类内还被别处调用
    /// （<c>LoadAsync</c> 属于后者——它是刷新，不是用户入口）。
    /// </summary>
    private static bool Wired(string name, string vm, string behind)
        => behind.Contains("ViewModel." + name + "(", StringComparison.Ordinal)
           || SourceGate.Count(vm, name + "(") > 1;      // 只有定义那一处＝没人调

    [Fact]
    public void EveryPublicWriterOfTheTodoViewModelIsCalledSomewhere()
    {
        var vm = SourceGate.ReadRepoFile(VmPath);
        var behind = SourceGate.ReadRepoFile(CodeBehindPath);

        var writers = PublicWriter.Matches(vm).Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(writers.Count >= 7,
            $"VM 里只认出 {writers.Count} 颗公开写入方法（HEAD 上有 7 颗）⇒ 先怀疑这颗正则，别改判据");

        // 正对照：这七颗是 HEAD 上真在事的写入面，逐颗点名（SetDueAsync 就是当年漏接的那一颗）
        Assert.All(new[]
        {
            "AddAsync", "ToggleAsync", "SetColorAsync", "SetDueAsync",
            "DeleteAsync", "UndoDeleteAsync", "PersistVisibleOrderAsync",
        }, name =>
        {
            Assert.True(writers.Contains(name), $"VM 里认不出 {name}：签名形状变了，普查那一半已经失效");
            Assert.True(Wired(name, vm, behind), $"{name} 没有调用点＝视图里缺一个入口（全绿而功能缺一块，批次 UM 那一族）");
        });

        // 判缺：同一条判据必须挑得出"只有定义、没人调"的名字——否则上面那句 Assert.All 可以靠白名单混过去
        Assert.False(Wired("NoSuchWriterEverExistedAsync", vm, behind));

        var dead = writers.Where(n => !Wired(n, vm, behind)).ToList();
        Assert.True(dead.Count == 0, "这些公开写入方法没有调用点：\n" + string.Join("\n", dead.Select(n => "  " + n)));
    }

    /// <summary>截止日期那条入口的三个出口、整条调用式，以及"次日"算法只许住在一处。</summary>
    [Fact]
    public void TheDueEntryExistsAndTheDayMathIsNotReimplementedInTheViewModel()
    {
        var xaml = SourceGate.ReadRepoFile(ViewPath);
        Assert.Equal(3, SourceGate.Count(xaml, "Click=\"TodoDue_Click\""));   // 今天 / 明天 / 清除，少一档就是入口又缺一块

        var behind = SourceGate.ReadRepoFile(CodeBehindPath);
        // 钉整条调用式（含实参）：只钉"调用了 SetDueAsync"的话，把 id 与偏移写反、或绕开菜单顺序另硬编码一档，照样全绿
        Assert.Contains("ViewModel.SetDueAsync(id, dayOffset);", behind);
        Assert.DoesNotContain("ViewModel.SetDueAsync(id, 1);", behind);

        var vm = SourceGate.ReadRepoFile(VmPath);
        Assert.Contains("LocalItemState.DayOffsetStartUnix(DateTimeOffset.Now, dayOffset.Value)", vm);
        Assert.DoesNotContain("DateTimeOffset.Now.AddDays(", vm);             // 定长 24 小时会跳错日历日（P-37）
    }
}
