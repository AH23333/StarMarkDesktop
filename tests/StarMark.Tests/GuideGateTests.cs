#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Core.Guide;
using StarMark.Core.Hotkeys;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 应用内说明那一族（批次 VC-3）：首启一次性引导、顶栏「？」、设置页「使用说明」栏。
/// <para><b>这一族最坏的两件事都不是崩溃，而是"说错话"</b>：① 同一件事在两处各写一遍文字，
/// 改了一处没改另一处，用户看到三种说法；② 把按键抄进文案，用户在设置里改了键之后引导还在教他按旧键。
/// 所以这里的判据分两半：<b>内容</b>钉"每页都有、没有一句把全局热键抄进文字、没有一句要用户多做一步"，
/// <b>接线</b>钉"三处宿主读的是同一份表，而那份表对着能导航到的页比"。</para>
/// </summary>
public sealed class GuideGateTests
{
    private const string NavCode = "src/StarMark.UI/MainWindow.Nav.cs";
    private const string NavXaml = "src/StarMark.UI/MainWindow.xaml";
    private const string WindowCode = "src/StarMark.UI/MainWindow.xaml.cs";
    private const string GuideWiring = "src/StarMark.UI/MainWindow.Guide.cs";
    private const string SettingsGuide = "src/StarMark.UI/Views/SettingsPage.Guide.cs";
    private const string SettingsXaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string SettingsCtor = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string GuideRender = "src/StarMark.UI/Helpers/GuideUi.cs";
    private const string GuideStore = "src/StarMark.UI/Helpers/SettingsStore.Guide.cs";

    /// <summary>应用能导航到的全部页（<c>NavigateToPage</c> 那张 switch 表，不是侧栏那几颗——搜索页不在侧栏里但照样要说得清）。</summary>
    private static List<string> NavigableTags()
        => Regex.Matches(Code(ReadRepoFile(NavCode)), @"""([a-z]+)""\s*=>\s*typeof\(")
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>侧栏上摆出来的那几页（XAML 里的 NavigationViewItem）。
    /// <para>批次 WI-1 起这些项不再有 <c>Content</c> 字面量，而是各带一颗 <c>i18n:LocText.Key</c>。
    /// 这里顺手钉住<b>键的后缀必须等于那一页的 Tag</b>：<c>nav.tags</c> 挂在 <c>Tag="tree"</c> 上
    /// 既不会编译错也不会红在别的格子上，它只会在用户切换语言之后让某一页顶着另一页的名字。</para></summary>
    private static List<string> NavItemTags()
    {
        var matches = Regex.Matches(ReadRepoFile(NavXaml),
            @"NavigationViewItem x:Name=""[^""]+"" i18n:LocText\.Key=""nav\.([a-z]+)"" Tag=""([a-z]+)""");
        foreach (Match match in matches)
            Assert.Equal(match.Groups[2].Value, match.Groups[1].Value);
        return matches.Select(m => m.Groups[2].Value).ToList();
    }

    // ===== 内容 =====

    /// <summary>
    /// 每一页都要有自己的说明，且说明表里不许有"应用去不了的页"。
    /// <para>方向是两边的：新加一页而没补说明＝那一页的「？」会说"还没有说明"（这是兜底，不是通过）；
    /// 说明表里留一条已经删掉的页＝文字活着而页死了。两侧都比一次，才堵得住"改了 switch 忘了说明"。</para>
    /// </summary>
    [Fact]
    public void EveryNavigablePageHasExactlyOneGuideAndNoGuideBelongsToADeadPage()
    {
        var navigable = NavigableTags();
        Assert.True(navigable.Count >= 9, $"从 {NavCode} 只认出 {navigable.Count} 页（switch 形状变了，闸门要先跟上）");
        Assert.Subset(new HashSet<string>(navigable, StringComparer.Ordinal), new HashSet<string>(NavItemTags(), StringComparer.Ordinal));

        foreach (var tag in navigable)
        {
            var guide = PageGuides.For(tag);
            Assert.NotNull(guide);
            Assert.Equal(tag, guide!.Tag);      // 说明与页对不上号＝浮层说出上一页的话（比没有说明更坏）
        }
        Assert.Equal(navigable.Count, PageGuides.InOrder.Count);   // 多一条＝有一条永远说不出口
    }

    /// <summary>
    /// 说明要真的说事：标题不空、至少两条、每条不短于一句话的起头。
    /// <para>"一条空壳说明"能让上一条闸门全绿而用户什么也没学到，所以这里量的是内容而不是"有没有键"。</para>
    /// </summary>
    [Fact]
    public void EachGuideSaysSeveralThingsRatherThanAnEmptyShell()
    {
        foreach (var guide in PageGuides.InOrder)
        {
            Assert.False(string.IsNullOrWhiteSpace(guide.Title), $"{guide.Tag} 没有标题");
            Assert.True(guide.Lines.Count >= 2, $"{guide.Tag} 只有 {guide.Lines.Count} 行说明，说不到两件事");
            Assert.All(guide.Lines, line => Assert.True(line.Trim().Length >= 8, $"{guide.Tag} 有一行太短：「{line}」"));
        }
        Assert.All(FirstRunGuide.Steps, step => Assert.True(step.Lines.Count >= 2, $"首启那一段「{step.Title}」少于两行"));
    }

    /// <summary>
    /// <b>文案里不许抄全局热键</b>。
    /// <para>抄下来的那一串在用户改键之后就是假话，而"引导说 F1、实际按了没反应"这种对不上账
    /// 比少写一句更糟。按键一律由 UI 从绑定表现场渲染（<c>GuideUi.HotkeyHints</c>），
    /// 所以这里禁的是字面：<c>Ctrl+Alt</c> 与 <c>F1</c>／<c>F3</c>／<c>F4</c>／<c>F5</c>。
    /// <c>Ctrl+Enter</c> 与 <c>↑↓</c> 不在禁项里——它们是页面内的键盘操作，不是可改的全局绑定。</para>
    /// </summary>
    [Fact]
    public void NoGuideLineCopiesAGlobalHotkeyIntoText()
    {
        var forbidden = new[] { "Ctrl+Alt", "Ctrl + Alt", "F1", "F3", "F4", "F5" };
        var all = PageGuides.InOrder.SelectMany(g => new[] { g.Title }.Concat(g.Lines))
            .Concat(FirstRunGuide.Steps.SelectMany(s => new[] { s.Title }.Concat(s.Lines)))
            .Concat(new[] { FirstRunGuide.Title });
        foreach (var line in all)
        {
            foreach (var token in forbidden) Assert.DoesNotContain(token, line, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 说明里不许出现"把这一步推回给用户"的句式（P-54 那条裁决的延伸）。
    /// <para>引导是产品的一部分：一句"请自行查阅文档"或"请先重启程序"就把活推回给用户，
    /// 而这一族存在的理由正是"不用去外面查"。</para>
    /// <para><b>判据是句式而不是字面清单</b>：反向台架第一版钉的是"请重启／请稍后"那一串常量，
    /// 变异写成"请<b>先</b>重启"就绕过去了（该红没红，见坑表那一格）。所以这里查的是
    /// <c>请</c> 之后八个字之内出现一个"要用户去做的事"，再加两样独立成形的推诿说法。</para>
    /// </summary>
    [Fact]
    public void NoGuideLineAsksTheUserForAnExtraStep()
    {
        var pushes = new[]
        {
            @"请[^。；]{0,8}(重启|稍后|稍等|确认|自行|手动|查阅|联系|检查|关掉其他|自己先)",
            @"自行(查阅|参考|备份|处理)",
            @"(文档|官网|issue)\s*(里|上|中)?\s*(有|见)?说明",
        };
        var lines = PageGuides.InOrder.SelectMany(g => g.Lines).Concat(FirstRunGuide.Steps.SelectMany(s => s.Lines));
        foreach (var line in lines)
        {
            foreach (var pattern in pushes)
            {
                Assert.True(!Regex.IsMatch(line, pattern),
                    $"这一行把活推回给用户了：「{line}」（命中 {pattern}）");
            }
        }
    }

    /// <summary>
    /// 取不到说明时的兜底<b>不算"这一页有说明"</b>。
    /// <para>兜底条目一旦进了 <see cref="PageGuides.Tags"/>，第一条闸门就会自己骗自己：
    /// 每个 tag 都查得到东西，而用户看到的是"还没有说明"。</para>
    /// </summary>
    [Fact]
    public void TheFallbackGuideIsHonestButDoesNotCountAsCoverage()
    {
        var missing = PageGuides.ForOrSaySo("definitely-not-a-page");
        Assert.Contains("还没有说明", missing.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("definitely-not-a-page", PageGuides.Tags, StringComparer.Ordinal);
        Assert.Null(PageGuides.For("definitely-not-a-page"));
        Assert.Null(PageGuides.For(null));
    }

    // ===== 接线：三个宿主读的是同一份表 =====

    /// <summary>
    /// 「？」要说的是<b>当前这一页</b>，而且内容在打开那一刻现取。
    /// <para>钉"读 <c>ViewModel.CurrentPageTag</c>"而不是钉一段中文：前者是行为，后者一改措辞就成假失败。
    /// 也钉"不许缓存"：切页时顺手刷新的写法漏一个入口（搜索态、深链、托盘回跳）就会说错页。</para>
    /// </summary>
    [Fact]
    public void TheQuestionButtonReadsTheCurrentPageWhenItOpensRatherThanACachedCopy()
    {
        var wiring = Code(ReadRepoFile(GuideWiring));
        Assert.Contains("GuideFlyout_Opening", wiring, StringComparison.Ordinal);
        Assert.Contains("PageGuides.ForOrSaySo(ViewModel.CurrentPageTag)", wiring, StringComparison.Ordinal);
        Assert.Contains("GuidePanel.Children.Clear()", wiring, StringComparison.Ordinal);
        // 首启那份：只有"没看过"才弹，且弹出去的路径只有一条（渲染住在 GuideUi，两处宿主共用）
        Assert.Contains("_settings.LoadGuideShown()", wiring, StringComparison.Ordinal);
        Assert.Equal(1, Count(wiring, "GuideUi.ShowFirstRunAsync("));
        Assert.Contains("MarkGuideShown", Code(ReadRepoFile(GuideRender)), StringComparison.Ordinal);

        var xaml = ReadRepoFile(NavXaml);
        Assert.Equal(1, Count(xaml, "x:Name=\"GuideButton\""));
        Assert.Equal(1, Count(xaml, "Opening=\"GuideFlyout_Opening\""));
        Assert.Equal(1, Count(Code(ReadRepoFile(WindowCode)), "MaybeShowFirstRunGuide();"));
    }

    /// <summary>
    /// 设置页那一栏是<b>宿主而不是第二份文字</b>：XAML 里只许有栏标题与那颗"重看"按钮，
    /// 正文一律由 <c>PageGuides.InOrder</c> 铺出来。
    /// <para>判据写成"这一栏的 XAML 里出现不了成段的中文说明"：把文字搬进 XAML 是最省事的写法，
    /// 也正是这一族最不该有的第二个出处。</para>
    /// </summary>
    [Fact]
    public void TheSettingsTabHostsTheGuidesInsteadOfRetypingThem()
    {
        var xaml = ReadRepoFile(SettingsXaml);
        Assert.Equal(1, Count(xaml, $@"Header=""{PageGuides.SettingsTabHeader}"""));
        Assert.Equal(1, Count(xaml, "x:Name=\"GuideTabPanel\""));
        Assert.Equal(1, Count(xaml, "ReplayGuideButton_Click"));

        var code = Code(ReadRepoFile(SettingsGuide));
        Assert.Contains("foreach (var page in PageGuides.InOrder)", code, StringComparison.Ordinal);
        Assert.Contains("GuideUi.HotkeyHints(", code, StringComparison.Ordinal);
        Assert.Equal(1, Count(Code(ReadRepoFile(SettingsCtor)), "BuildGuideTab();"));
        // 重看之后仍然记"已看过"：这一颗是"再看一遍"，不是"取消一次性"
        Assert.Contains("GuideUi.ShowFirstRunAsync(", code, StringComparison.Ordinal);

        // 这一栏的 XAML 里只许有"一颗按钮 + 一句说明按钮干什么"，正文一条都不许落在这里。
        // 抄一段进 XAML 是最省事的写法（不用回 Core 改表），而它正是这一族第二个出处的开始。
        var tab = Between(xaml, $@"Header=""{PageGuides.SettingsTabHeader}""", @"Header=""AI 增强""");
        Assert.Equal(1, Count(tab, "<TextBlock"));
        Assert.Equal(1, Count(tab, "<Button"));
        Assert.DoesNotContain("GuideTabPanel.Children.Add", code, StringComparison.Ordinal);   // 手写条目＝绕过那张表
    }

    /// <summary>
    /// 一次性那个记号是<b>三态</b>的：没这一格（老配置）＝没看过，看过＝true。
    /// <para>把"没表过态"读成"看过"，老用户升级之后就永远看不到引导了——这一条钉的就是那一个 <c>?? false</c>。
    /// 而"重看一遍"那颗按钮<b>不抹记号</b>：它是"再看一次"，不是"取消一次性"，
    /// 抹掉会让下次启动又弹一次，而用户刚刚才亲手把它关掉过。</para>
    /// </summary>
    [Fact]
    public void TheOneTimeFlagTreatsNeverSetAsNeverSeen()
    {
        var store = Code(ReadRepoFile(GuideStore));
        Assert.Contains("d.FirstRunGuideShown ?? false", store, StringComparison.Ordinal);
        Assert.Contains("d.FirstRunGuideShown = true", store, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstRunGuideShown = null", store, StringComparison.Ordinal);
        var render = MethodBody(ReadRepoFile(GuideRender), "public static async Task ShowFirstRunAsync");
        Assert.True(render.IndexOf("await dialog.ShowAsync()", StringComparison.Ordinal)
                    < render.IndexOf("settings.MarkGuideShown()", StringComparison.Ordinal),
            "必须等对话框关掉之后再记「已看过」——先记后弹的话，弹失败就永久没有了这一次");
    }

    /// <summary>
    /// 首启要列的那几颗键，动作名必须真的存在（写错一个字母＝那一行永远不出现在引导里，而没人会红）。
    /// </summary>
    [Fact]
    public void EveryActionListedInTheFirstRunGuideIsARealHotkeyAction()
    {
        var all = HotkeyActions.All();
        foreach (var action in FirstRunGuide.HotkeyActionsToShow)
        {
            Assert.Contains(action, all, StringComparer.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(HotkeyActions.DisplayName(action)),
                $"{action} 没有中文名，引导里那一行会读成「：Ctrl + Alt + Space」这种半截话");
        }
    }
}
