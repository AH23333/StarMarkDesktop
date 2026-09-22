#nullable enable
using System;
using System.Linq;
using Xunit;
using StarMark.Core.Widgets;
using StarMark.Integrations.SystemTray;

namespace StarMark.Tests;

/// <summary>
/// 托盘「桌面组件」子菜单的号段与注入清单契约（P-62b）。
/// <para>
/// 这条缺陷的成因有两半，本文件各钉一半：① 子菜单曾经按 <see cref="TrayHost"/> 里一份手写的 5 条标题
/// 数组生成，而组件注册表已长出到 12 种 ⇒ 7 种组件在托盘里既看不到也关不掉（含新的剪贴板格）；
/// ② 逐组件项按 <c>Base + 序号</c> 发号，而"全部显示/隐藏"两个固定命令原先紧贴着 1110/1111，
/// 整段<b>只有 10 个槽</b> ⇒ 把清单补全的那一步本身就会让第 11、12 项踩到 sentinel，
/// 点"天气"实际执行"全部显示"。两半都是"看起来还在、点了不对"的静默错动作，且全在 UI 装配侧，
/// 构建与既有测试一律放过。
/// </para>
/// </summary>
public sealed class TrayWidgetMenuTests
{
    /// <summary>托盘子菜单的行来自这份注册表投影，故它的长度就是"托盘应该能表达多少种组件"。</summary>
    private static int TrayItemCount => WidgetStorage.AllKinds.Count;

    [Fact]
    public void EveryTrayRow_FitsBelowTheSentinelCommands()
    {
        // 最后一行的命令号必须仍在块内：越过 ShowAll 就等于把"全部显示/隐藏"当成某个组件。
        var last = TrayWidgetMenu.Base + TrayItemCount - 1;
        Assert.True(last < TrayWidgetMenu.ShowAll,
            $"组件种类 {TrayItemCount} 超出命令块容量 {TrayWidgetMenu.Capacity}：最后一项命令号 {last} >= ShowAll {TrayWidgetMenu.ShowAll}。");
        Assert.True(last < TrayWidgetMenu.HideAll);
    }

    [Fact]
    public void Capacity_MatchesTheBlockAndIsNotTheOldTenSlots()
    {
        // 旧写法把 sentinel 手抄在 1110/1111，容量于是被压成 10——比现有 12 种组件还少。
        Assert.Equal(TrayWidgetMenu.Base + TrayWidgetMenu.Capacity, TrayWidgetMenu.ShowAll);
        Assert.Equal(TrayWidgetMenu.ShowAll + 1, TrayWidgetMenu.HideAll);
        Assert.True(TrayWidgetMenu.Capacity > TrayItemCount,
            "命令块必须容得下全部组件种类，否则托盘又会开始静默漏项。");
    }

    [Fact]
    public void InjectedRows_CarryUniqueNonEmptyTitledRowsForEveryCreatableKind()
    {
        var titles = WidgetStorage.AllKinds
            .Select(k => new TrayWidgetItem((int)k, WidgetStorage.KindTitle(k)))
            .ToList();

        Assert.All(titles, row => Assert.False(string.IsNullOrWhiteSpace(row.Title)));
        Assert.Equal(titles.Count, titles.Select(t => t.Title).Distinct().Count());
        Assert.Equal(titles.Count, titles.Select(t => t.Kind).Distinct().Count());

        // 展示名带图标（DisplayTitle = "{Glyph} {Title}"），用户在托盘里就靠它认出是哪一格。
        Assert.Equal("📋 剪贴板格", WidgetStorage.KindTitle(WidgetKind.Clipboard));
        Assert.StartsWith("★", WidgetStorage.KindTitle(WidgetKind.QuickLaunch));
    }

    [Fact]
    public void KindValues_AreStableWireNumbers()
    {
        // 注入行传的是 kind 的整数，托盘回调按它 (WidgetKind)kind 还原 ⇒ 序号与枚举值不再被当成同一件事。
        Assert.Equal(0, (int)WidgetKind.QuickLaunch);
        Assert.Equal(6, (int)WidgetKind.Clipboard);   // 原 SearchResults 的 wire 值，改名不改号
        Assert.Equal(11, (int)WidgetKind.Music);
        Assert.Equal(Enum.GetValues<WidgetKind>().Length, TrayItemCount);
    }
}
