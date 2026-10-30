#nullable enable
using System;
using System.Xml.Linq;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 代码建的那一批 <c>Expander</c> 的<b>可访问名</b>（✅P-267 同族・片143）。
/// <para>形状与来路：⛔ 读码起的手。片142 那把新尺（走桌面 RawView・先问 rect 有限再落盘）逐页现读，
/// 屏幕上还念不出名字的可交互元素共 <b>33 颗</b>＝「快捷键」页 16 颗＋「桌面工具（组件）」页 17 颗，
/// 三语各一跑颗数相等。再拿真机探针问它们的身份：<c>class=Microsoft.UI.Xaml.Controls.Expander</c> 而
/// <c>ControlType.Button</c>，Header 是两块 TextBlock 拼的（分类名／类型名 ＋ 那颗计数）
/// ⇒ Expander 的抬头 peer 念不出这种合成体的名字，辅助技术只剩一句「按钮」。</para>
/// <para>这一格守三件事：①<b>两颗建造处都挂上名</b>（少一处＝那一页整片按钮又念不出）；
/// ②挂的是<b>抬头那句话自己的那颗键</b>（⛔ 新字串、⛔ 第二份文案，与 ✅P-265 那颗工厂同一口径）；
/// ③贴名字这件事<b>只住一处</b>（<c>ExpanderTitleNames.TitleNamed</c>），两页各自 <c>SetNameKey</c> 一遍＝第三种写法又长出来。</para>
/// <para>⚠ 射程：这里钉的是源码形状，屏幕上念不念得出来由真机那把尺逐页复称（片143 读数），
/// 换语言那一刻跟着改口也在那一跑里量。</para>
/// </summary>
public sealed class ExpanderNameKeyGateTests
{
    private const string HotkeysCs = "src/StarMark.UI/Views/SettingsPage.Hotkeys.cs";
    private const string WidgetsCs = "src/StarMark.UI/Views/SettingsPage.Widgets.cs";
    private const string HelperCs = "src/StarMark.UI/Helpers/ExpanderTitleName.cs";
    private const string SettingsXaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string LocTextCs = "src/StarMark.UI/Helpers/LocText.cs";

    /// <summary>
    /// 热键页那颗分类 Expander：标题与可访问名<b>同出一颗键的取法</b>，且两处都在同一个方法体里。
    /// <para>⛔ 只数全档颗数：把那颗借名挪到别的方法去，颗数照旧绿而分类 Expander 又变回裸按钮。</para>
    /// </summary>
    [Fact]
    public void TheCategoryExpanderBorrowsTheSameKeyItsHeaderShows()
    {
        var cs = Code(ReadRepoFile(HotkeysCs));
        var body = MethodBody(cs, "private Expander BuildCategoryExpander");
        Assert.Contains("LocText.SetKey(title, HotkeyActions.CategoryTitleKey(category));", body, StringComparison.Ordinal);
        Assert.Contains("}.TitleNamed(HotkeyActions.CategoryTitleKey(category));", body, StringComparison.Ordinal);
        Assert.DoesNotContain("LocText.SetNameKey(", body, StringComparison.Ordinal);   // 名字⛔ 在这一处自己贴一遍
    }

    /// <summary>组件页那颗类型 Expander 同一条路，借的是组件注册表那颗 <c>KindTitleKey</c>（托盘子菜单早就在用它）。</summary>
    [Fact]
    public void TheWidgetKindExpanderBorrowsTheRegistryTitleKey()
    {
        var cs = Code(ReadRepoFile(WidgetsCs));
        Assert.Contains("LocText.SetKey(title, WidgetStorage.KindTitleKey(kind));", cs, StringComparison.Ordinal);
        Assert.Contains("}.TitleNamed(WidgetStorage.KindTitleKey(kind)));", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("LocText.SetNameKey(", cs, StringComparison.Ordinal);
    }

    /// <summary>
    /// 贴名字这件事只有一颗主人：全仓 <c>LocText.SetNameKey(</c> 的调用形现读恰一颗（＝那颗扩展方法自己），
    /// 而两个宿主各调用 <c>.TitleNamed(</c> 一颗（现读恰两颗）。
    /// <para>两颗数各问各的一维：只数 <c>SetNameKey</c> 抓不到"第三处绕过扩展方法自己写"，
    /// 只数 <c>TitleNamed</c> 抓不到"扩展方法里那次贴法被人改掉"。</para>
    /// </summary>
    [Fact]
    public void OnlyTheHelperPastesTheNameAndTwoHostsAskForIt()
    {
        var helper = Code(ReadRepoFile(HelperCs));
        Assert.Equal(1, Count(helper, "LocText.SetNameKey(expander, titleKey);"));
        Assert.DoesNotContain("AutomationProperties.SetName(", helper, StringComparison.Ordinal);

        var pastes = 0;
        var hosts = 0;
        foreach (var (relative, text) in ReadRepoUnder("src"))
        {
            if (relative.EndsWith("Helpers/LocText.cs", StringComparison.Ordinal)) continue;
            pastes += Count(Code(text), "LocText.SetNameKey(");
            hosts += Count(Code(text), ".TitleNamed(");
        }
        Assert.Equal(1, pastes);
        Assert.Equal(2, hosts);
    }

    /// <summary>
    /// 设置页那颗「这一版的更新说明」（✅P-269・片145 真机读数结案・只留形状守卫）。
    /// <para>
    /// 立案那一问是<b>落点</b>：<c>LocText.Key</c> 走 <c>ResolveTextProperty</c> 的
    /// <c>Text→Content→Header→…</c> 次序，而 <c>Expander</c> 同有 Content（正文容器）与 Header。
    /// 沙盒读数（<c>STARMARK_FAKE_NOTES</c> 那颗临时门摆出那一格，量完已逐字节撤净）：
    /// ①抬头念得出也<b>看得见</b>那句——UIA 里是 <c>按钮 这一版的更新说明 ID: UpdateNotesExpander</c>
    /// ＋子节点 <c>文本 这一版的更新说明</c>；②展开后正文是<b>说明全文</b>（<c>窗格 FAKE-BODY-7741…</c>），
    /// 没被抬头那句顶掉；③换语言那一刻重贴<b>只换抬头</b>（→<c>What's in this release</c>），
    /// 正文一字未动。⇒ 三问都是"本来就对"，这一颗就地结案，下面钉的是让它可以不对的那三处形状。
    /// </para>
    /// </summary>
    [Fact]
    public void TheReleaseNotesExpanderPutsTheSentenceInTheHeaderAndKeepsTheBodyApart()
    {
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace i18n = "using:StarMark.UI.Helpers";
        const string notesKey = "settings.general.releaseNotes";

        var page = XDocument.Parse(ReadRepoFile(SettingsXaml));
        var expander = Assert.Single(page.Descendants(ui + "Expander"),
            e => (string?)e.Attribute(x + "Name") == "UpdateNotesExpander");
        // 屏幕上那行字与辅助技术里那个名字共用同一颗键（⛔ 第二份文案）。
        Assert.Equal(notesKey, (string?)expander.Attribute(i18n + "LocText.Key"));
        Assert.Equal(notesKey, (string?)expander.Attribute(i18n + "LocText.NameKey"));
        // 抬头⛔ 另写死一份：那句字只由那颗键贴上去（写了死字，换语言就只剩那一半是活的）。
        Assert.Empty(expander.Elements(ui + "Expander.Header"));

        // 正文只有一个主人：Content 里那颗 TextBlock 绑的就是 ViewModel.ReleaseNotesText。
        var body = Assert.Single(expander.Descendants(ui + "TextBlock"));
        Assert.Contains("ViewModel.ReleaseNotesText", (string?)body.Attribute("Text") ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Contains("Mode=OneWay", (string?)body.Attribute("Text") ?? string.Empty, StringComparison.Ordinal);

        // 让上面两行成立的那条规矩：有 Header 的元素⛔ 碰 Content。摘掉它，正文容器就会被抬头那句吃掉。
        var locText = Code(ReadRepoFile(LocTextCs));
        Assert.Contains("""if (name == "Content" && hasHeader) continue;""", locText, StringComparison.Ordinal);
        Assert.Contains("var hasHeader = FindProperty(target, \"Header\") is not null;", locText, StringComparison.Ordinal);
    }
}
