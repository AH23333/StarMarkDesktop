#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 设置页八颗分类标签的<b>选中高亮底部形状</b>（用户 2026-10-30 报：一部分直、一部分弧）。
/// <para>
/// 真机逐行量边界坐实（改前）：直＝常规／AI 增强／数据／健康与诊断，
/// 弧＝使用说明／桌面工具（组件）／拓展功能／快捷键——同一排两种底部。
/// 来路⛔ 在我们这边：八颗 <c>TabViewItem</c> 写法一致，页内只有一条隐式样式。
/// 是框架模板选中态收起 <c>BottomBorderLine</c> 后，改由 <c>SelectedBackgroundPath</c>
/// （几何取自 <c>TabViewTemplateSettings.TabGeometry</c>、<c>Margin="-4,0"</c>）
/// 加两颗 4×4 的 <c>Left/RightRadiusRenderArc</c> 画那颗"浏览器标签脚"，
/// 而模板根 <c>Grid</c> 写着 <c>UseLayoutRounding="False"</c>＋<c>SizeToContent</c> 的小数宽度
/// ⇒ 那颗脚四颗吸附出来、四颗抹平。
/// </para>
/// <para>
/// 修法＝把那颗模板接过来、摘掉那三块零件，选中态改由 <c>TabContainer</c> 自己上底色
/// （上半截 8 圆角、下半截 0）。这一档钉的是"接过来的那份⛔ 偷偷长回去"，
/// 外加第一版照抄直接把八颗整条打死那两处坑（真机量到设置页只剩标题与「返回」）。
/// </para>
/// </summary>
public sealed class SettingsTabHighlightGateTests
{
    private const string Page = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string PageCs = "src/StarMark.UI/Views/SettingsPage.xaml.cs";

    /// <summary>那颗接过来的模板住在应用级字典里（⛔ 塞进设置页那颗档：它会把页内 29 行以下
    /// 整体推下去一百多行，账本里指向那颗档的行号指针全跟着平移——落空棘轮就是这么被推动的）。</summary>
    private const string TabStyleXaml = "src/StarMark.UI/Themes/SettingsTabStyle.xaml";

    /// <summary>那颗样式确实挂在应用级合并链上（挂在字典里而⛔ 有人合并＝屏幕上什么都⛔ 变）。</summary>
    [Fact]
    public void TheAppLevelDictionaryThatCarriesTheStyleIsMerged()
    {
        var app = ReadRepoFile("src/StarMark.UI/App.xaml");
        Assert.Contains("Themes/SettingsTabStyle.xaml", app, StringComparison.Ordinal);
        Assert.DoesNotContain("<Style TargetType=\"TabViewItem\">", ReadRepoFile(Page), StringComparison.Ordinal);
    }

    /// <summary>
    /// ① 那颗脚那三块零件⛔ 以 <c>x:Name</c>／<c>Target</c> 的形状回到我们这份模板里（读<b>解析后的树</b>，
    /// ⛔ 读整档原文：那三个名字在本档的<b>注释</b>里逐颗出现，按原文找会把我自己的说明当成零件——第一版就是这么红的）；
    /// ② 我们这份确实接管了模板，且选中底色与"上圆下直"那颗圆角在位。
    /// </summary>
    [Fact]
    public void TheBorrowedTemplateDropsTheTabFootAndPaintsTheSelectionItself()
    {
        var xaml = ReadRepoFile(TabStyleXaml);
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            "SelectedBackgroundPath", "LeftRadiusRenderArc", "RightRadiusRenderArc",
        };
        var doc = XDocument.Parse(xaml);
        Assert.Empty(doc.Descendants()
            .Where(e => names.Contains((string?)e.Attribute(x + "Name") ?? string.Empty))
            .Select(e => (string?)e.Attribute(x + "Name")!));
        Assert.Empty(doc.Descendants(ui + "Setter")
            .Select(e => ((string?)e.Attribute("Target") ?? string.Empty).Split('.')[0])
            .Where(head => names.Contains(head)));

        Assert.Contains("<Style TargetType=\"TabViewItem\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Template\">", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"TabContainer.Background\" Value=\"{ThemeResource TabViewItemHeaderBackgroundSelected}\"",
            xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"BottomBorderLine.Visibility\" Value=\"Collapsed\"", xaml, StringComparison.Ordinal);
        // 上半截照旧 8、下半截 0＝八颗同一直边。
        Assert.Contains("CornerRadius=\"8,8,0,0\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// 第一版照抄踩到的两处：那两颗资源住在框架字典的<b>非主题</b>那半，我们这份档里解析不到，
    /// 后果是八颗标签整条⛔ 渲染（运行期，编译⛔ 报）。⇒ 这一格钉住它们⛔ 许再被抄回来，
    /// 并钉住"⛔ 关闭钮"那档内边距是按原值写死的。
    /// </summary>
    [Fact]
    public void TheTwoFrameworkOnlyStaticResourcesStayOutOfOurTemplate()
    {
        var xaml = ReadRepoFile(TabStyleXaml);
        Assert.DoesNotContain("{StaticResource UseSystemFocusVisuals}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TabViewItemHeaderPaddingWithCloseButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TabViewItemHeaderPaddingWithoutCloseButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<CloseButton", xaml, StringComparison.Ordinal);
        Assert.Contains("Padding=\"8,3,8,3\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// 八颗标签共用<b>一颗</b>隐式样式，且那颗样式只有一个主人（页内一份都⛔ 有）——
    /// 页内只要再挂一条 <c>TargetType="TabViewItem"</c>，应用级那一份就整条吃不到（隐式样式⛔ 合并），
    /// 表现就是"改了⛔ 生效"，而屏幕上看着像原样。八颗各自⛔ 许再改形状（那正是"两种高亮"回来的路）。
    /// </summary>
    [Fact]
    public void EightTabsShareOneImplicitStyleAndNoneOfThemOverridesItsShape()
    {
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var pageSource = ReadRepoFile(Page);
        Assert.DoesNotContain("<Style TargetType=\"TabViewItem\">", pageSource, StringComparison.Ordinal);

        var dict = XDocument.Parse(ReadRepoFile(TabStyleXaml));
        var style = Assert.Single(dict.Root!.Elements(ui + "Style"),
            e => (string?)e.Attribute("TargetType") == "TabViewItem");
        Assert.Equal("False", (string?)Assert.Single(style.Elements(ui + "Setter"),
            s => (string?)s.Attribute("Property") == "IsClosable").Attribute("Value"));

        var items = XDocument.Parse(pageSource).Descendants(ui + "TabViewItem").ToList();
        Assert.Equal(8, items.Count);
        foreach (var item in items)
        {
            Assert.Null(item.Attribute("Style"));
            Assert.Null(item.Attribute("CornerRadius"));
            Assert.Null(item.Attribute("HeaderTemplate"));
            Assert.NotNull(item.Attribute(x + "Name"));
            Assert.NotNull(item.Attribute("Tag"));
        }
        // 换语言重贴吃的是 Header：八颗都挂 LocText.Key（✅P-266／P-267 同一条口径）。
        Assert.Equal(8, Count(pageSource, "i18n:LocText.Key=\"settings.tab."));
        Assert.Contains("SettingsTabs.TabItems", Code(ReadRepoFile(PageCs)), StringComparison.Ordinal);
    }
}
