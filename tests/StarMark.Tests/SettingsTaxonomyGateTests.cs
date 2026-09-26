#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 设置页"一张卡住在哪一页"的守门（批次 WC-2 系列）。
/// <para>
/// 设置页被用户点名过两次同类缺陷：<b>功能过多且分类不合理</b>。修法只有"搬家"这一种动作，
/// 而搬家最容易留下的两种残骸恰恰是编译器与运行时都不报错的：
/// ① <b>同一个设置出现两份编辑入口</b>（搬走了旧的那份没删干净，或复制时漏删）——
///    两处绑定同一个属性时看着都"生效"，改一处另一处显示旧值，用户只能靠猜；
/// ② <b>相对方位的说法穿帮</b>（"与上方「组件材质」互不影响"被留在另一页里）——
///    文字仍是通顺的中文，只是指向了一个不存在的位置，等于假指引。
/// </para>
/// <para>这两类都只能扫 XAML 结构来钉，故有此文件（测试工程不引用 <c>StarMark.UI</c>）。</para>
/// </summary>
public sealed class SettingsTaxonomyGateTests
{
    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    private static readonly string[] ExpectedTabs =
    {
        "常规", "AI 增强", "桌面工具（组件）", "拓展功能", "快捷键", "数据", "健康与诊断",
    };

    private static string Markup(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>页签清单与顺序都钉死。<b>顺序也是分类的一部分</b>：日常项在前、可选能力在后。</summary>
    [Fact]
    public void TabsAreTheAgreedTaxonomyInOrder()
    {
        var found = Regex.Matches(ReadRepoFile(Xaml), "<TabViewItem Header=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(ExpectedTabs, found);
    }

    /// <summary>每张卡的标题在全页唯一：搬家时漏删旧副本，第一现场就是这里。</summary>
    [Fact]
    public void NoCardTitleIsDuplicated()
    {
        var titles = Regex.Matches(ReadRepoFile(Xaml),
                "<TextBlock Text=\"([^\"]+)\" Style=\"\\{StaticResource SettingTitle\\}\"")
            .Select(m => m.Groups[1].Value).ToList();

        Assert.True(titles.Count > 10, $"只扫到 {titles.Count} 张卡——锚点本身失效了，守门不能白过");
        Assert.Equal(titles.Count, titles.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// 每个可写设置<b>只有一个</b> <c>Mode=TwoWay</c> 编辑入口。
    /// 标题唯一仍挡不住"两张卡各绑一次同一属性"（卡片标题不同、绑的却是同一个量）。
    /// </summary>
    [Fact]
    public void EachSettingHasExactlyOneEditor()
    {
        var xaml = ReadRepoFile(Xaml);
        var writable = new[]
        {
            "BackdropIndex", "MainWindowBackdropIndex", "WidgetOpacity", "MainWindowOpacity",
            "LocalDiskSearchEnabled", "EyeRestEnabled", "EyeRestEnforced", "EyeRestDeferOnFullscreen",
            "TrendingEnabled", "ClipboardHistoryEnabled", "EnableTray", "MinimizeToTray", "GithubToken",
            "CanvasEnabled", "CanvasInScreenshots",
        };

        foreach (var p in writable)
            Assert.Equal(1, Count(xaml, $"ViewModel.{p}, Mode=TwoWay"));
    }

    /// <summary>分类本身：卡必须住在裁决给它的那一页（"搬了"这件事要有据可查，否则只是口头完成）。</summary>
    [Fact]
    public void CardsLiveOnThePagesTheUserRuledFor()
    {
        var xaml = ReadRepoFile(Xaml);

        var ai = Between(xaml, "<TabViewItem Header=\"AI 增强\">", "</TabViewItem>");
        Assert.Contains("这三样分别要填什么", ai);
        var regular = Between(xaml, "<TabViewItem Header=\"常规\">", "</TabViewItem>");
        Assert.Contains("ViewModel.GithubToken", regular);   // Token 是"库"的日常配置，留在常规页
        Assert.DoesNotContain("这三样分别要填什么", regular);
        Assert.DoesNotContain("AI 助手", Markup(regular));    // 说明卡与表单一起走，不能一份表单搬家、一份说明书留下

        var widgets = Between(xaml, "<TabViewItem Header=\"桌面工具（组件）\">", "</TabViewItem>");
        Assert.Contains("组件材质", widgets);
        Assert.Contains("ViewModel.BackdropIndex", widgets);
        Assert.DoesNotContain("ViewModel.MainWindowBackdropIndex", widgets);

        var extras = Between(xaml, "<TabViewItem Header=\"拓展功能\">", "</TabViewItem>");
        Assert.Contains("本地磁盘搜索", extras);
        Assert.Contains("ViewModel.LocalDiskSearchEnabled", extras);
        Assert.Contains("网址来源（RSS / Atom）", extras);
        Assert.DoesNotContain("剪贴板历史", Markup(extras));
        // 屏幕画布（批次 WD-5）：开关与那份只读键位一览都住在拓展功能页
        Assert.Contains("<TextBlock Text=\"屏幕画布\" Style=\"{StaticResource SettingTitle}\"", extras);
        Assert.Contains("ViewModel.CanvasEnabled", extras);
        Assert.Contains("ViewModel.CanvasHotkeySheet", extras);
        // 「截图带画布」（批次 WH）跟着画布走：它说的是同一块玻璃，只不过影响的是截图那一帧
        Assert.Contains("ViewModel.CanvasInScreenshots", extras);

        var health = Between(xaml, "<TabViewItem Header=\"健康与诊断\">", "</TabViewItem>");
        Assert.Contains("护眼 · 休息提醒", health);
        Assert.Contains("ViewModel.EyeRestEnabled", health);

        var data = Between(xaml, "<TabViewItem Header=\"数据\">", "</TabViewItem>");
        // 「数据」页仍会在提权说明里提到本地磁盘搜索（那是一句原因，不是第二处开关），
        // 所以钉的是"这张卡不在这页"，而不是这个词不在这页。
        Assert.DoesNotContain("<TextBlock Text=\"本地磁盘搜索\" Style=\"{StaticResource SettingTitle}\"", data);
        Assert.DoesNotContain("ViewModel.LocalDiskSearchEnabled", data);
    }

    /// <summary>
    /// 跨页的相对方位说法（"上方／下方／下一栏"）必须清干净：卡一搬家，这些词就指向了不存在的东西。
    /// 点名页签的说法（"在「常规」页"）不受限制。
    /// </summary>
    [Fact]
    public void NoRelativeDirectionSurvivesAMove()
    {
        var markup = Markup(ReadRepoFile(Xaml));

        foreach (var phrasing in new[] { "上方「", "下方「", "见下方", "在下方单独设置" })
            Assert.False(markup.Contains(phrasing, StringComparison.Ordinal), $"残留的相对方位说法：{phrasing}");
    }
}
