#nullable enable
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 组件窗里的滚动条闸门（批次 VO，扫源码不跑界面）。
/// <para>
/// 用户裁决的形状是<b>"藏掉条，滚轮还能滚"</b>——所以这里守的是两半，缺一半都算没做：
/// ① 组件视图里<b>不许再有会露出来的条</b>（<c>Auto</c>／<c>Visible</c>）；
/// ② <b>也不许顺手把滚动关掉</b>（该处不能改成 <c>Disabled</c>：那是内容一多就被裁掉，比有条更糟），
/// 而且<b>主窗口那几页必须照旧留条</b>（他指的是贴在桌面上的组件，长表单页把条藏掉反而找不到北）。
/// </para>
/// </summary>
public sealed class WidgetScrollBarGateTests
{
    private const string ViewsDir = "src/StarMark.UI/Views";

    /// <summary>能自己带滚动条的元素种类。<b>逐个元素名扫，不靠"整档里没有 Auto"</b>：漏扫一种就是假绿。</summary>
    private static readonly string[] ScrollingHosts = ["ScrollViewer", "ListView", "GridView", "TextBox", "RichEditBox"];

    /// <summary>主窗口那几页：批次 VO 明确<b>不在射程内</b>，条要留着。</summary>
    private static readonly string[] MainWindowPages =
    [
        "SettingsPage.xaml", "SearchPage.xaml", "TrendingPage.xaml", "ClipboardPage.xaml",
        "ActivityPage.xaml", "HiddenPage.xaml", "SnapshotPage.xaml", "TagsPage.xaml",
        "FolderTreePage.xaml", "RssPage.xaml",
    ];

    /// <summary>Views 目录下全部组件视图（文件名带 Widget 的 XAML，含宿主窗 <c>WidgetWindow.xaml</c>）。</summary>
    private static (string Name, string Markup)[] WidgetViews()
    {
        var root = Path.Combine(SourceGate.RepoRoot(), ViewsDir.Replace('/', Path.DirectorySeparatorChar));
        var files = Directory.GetFiles(root, "*Widget*.xaml").OrderBy(path => path).ToArray();
        // 扫不到就是路径错了：闸门失效比红测危险，直接抛（同 SourceGate 的口径）
        Assert.True(files.Length >= 12, $"只扫到 {files.Length} 个组件视图（组件至少有 12 扇，路径或命名改了要去改这条普查）");
        return files.Select(path => (Path.GetFileName(path),
                    Regex.Replace(File.ReadAllText(path), "<!--.*?-->", string.Empty, RegexOptions.Singleline)))
                    .ToArray();
    }

    private static MatchCollection StartTags(string markup, string element)
        => Regex.Matches(markup, $"<{element}(?=[\\s>])[^>]*>", RegexOptions.Singleline);

    /// <summary>
    /// 组件视图里每一颗滚动宿主：它写出来的 <c>*ScrollBarVisibility</c> 只许是 <c>Hidden</c> 或 <c>Disabled</c>。
    /// <para><b>为什么还要禁"没写"</b>：不写就是框架默认（<c>Auto</c>），组件里那一条照样会露出来——
    /// 本批就是靠"补上 <c>ItemGridWidget</c> 那处一直没写过的默认值"才把最后一条藏掉的。</para>
    /// </summary>
    [Fact]
    public void EveryWidgetScrollingHostHidesItsBar()
    {
        var scanned = 0;
        foreach (var (name, markup) in WidgetViews())
            foreach (var element in ScrollingHosts)
                foreach (Match tag in StartTags(markup, element))
                {
                    // 单行输入框（计算器、倒计时那些）根本没有可滚的内容：只有多行 TextBox 才算滚动宿主
                    if (element == "TextBox" && !tag.Value.Contains("AcceptsReturn")) continue;
                    scanned++;
                    foreach (Match bar in Regex.Matches(tag.Value, @"(?:Horizontal|Vertical)ScrollBarVisibility=""([^"")]+)"""))
                        Assert.True(bar.Groups[1].Value is "Hidden" or "Disabled",
                                    $"{name} 的 <{element}> 把条设成了 {bar.Groups[1].Value}（组件里只许藏起来或本来就不滚）");
                    if (element is "ScrollViewer" or "ListView" or "GridView")
                        Assert.Contains("ScrollBarVisibility", tag.Value);
                }
        Assert.True(scanned >= 11, $"只扫到 {scanned} 颗滚动宿主（普查面被改窄了，这条闸门就没证人了）");
    }

    /// <summary>
    /// 宿主窗那一条是<b>他实际看到的每一条</b>：每个组件的内容都过 <c>ContentScroll</c>。
    /// 纵向必须 <c>Hidden</c>（还能滚），横向保持 <c>Disabled</c>（内容按宽换行，不该横着跑）。
    /// </summary>
    [Fact]
    public void TheHostContentScrollHidesTheBar_ButKeepsScrolling()
    {
        var window = WidgetViews().Single(view => view.Name == "WidgetWindow.xaml").Markup;
        var scroll = StartTags(window, "ScrollViewer").Cast<Match>().Single(tag => tag.Value.Contains("x:Name=\"ContentScroll\""));
        Assert.Contains("VerticalScrollBarVisibility=\"Hidden\"", scroll.Value);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", scroll.Value);
        Assert.DoesNotContain("VerticalScrollBarVisibility=\"Disabled\"", scroll.Value);   // 那等于把滚动一起关掉
    }

    /// <summary>
    /// 待办那一列是 <c>ListView</c>：它的滚动条住在模板里，XAML 上<b>没有 <c>&lt;ScrollViewer&gt;</c> 这个元素</b>，
    /// 只有附加属性那条路。上面那枚普查认的是元素开始标记，这一颗要单独钉——否则"全绿而条还在"。
    /// </summary>
    [Fact]
    public void TheTodoListHidesItsBarThroughTheAttachedProperty()
    {
        var todo = WidgetViews().Single(view => view.Name == "TodoWidget.xaml").Markup;
        var list = StartTags(todo, "ListView").Single().Value;
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Hidden\"", list);
    }

    /// <summary>
    /// 主窗口那几页<b>一条都不许跟着改</b>：这批的射程只有组件窗。
    /// 若有人整目录把 <c>Auto</c> 换成 <c>Hidden</c>（最省事的"全局一致"），这一枚立刻红。
    /// </summary>
    [Fact]
    public void MainWindowPagesStillShowTheirBars()
    {
        foreach (var page in MainWindowPages)
        {
            var markup = Regex.Replace(SourceGate.ReadRepoFile($"{ViewsDir}/{page}"),
                "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", markup);
        }
    }

    /// <summary>
    /// 条藏掉之后，<b>原先只为让开那条覆盖式滚动条而垫的留白要一起撤</b>（天气那两处：右侧 14px、底部 16px）。
    /// 留着就是纯空白；留着还说明改动只做到"看不见条"，没做到"那一片本来就不必让"。
    /// </summary>
    [Fact]
    public void TheGuttersReservedForTheBarAreGone()
    {
        var weather = WidgetViews().Single(view => view.Name == "WeatherWidget.xaml").Markup;
        Assert.DoesNotContain("Padding=\"0,0,14,0\"", weather);
        Assert.DoesNotContain("Padding=\"0,0,0,16\"", weather);
    }
}
