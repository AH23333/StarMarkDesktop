#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Guide;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 首启<b>分步向导</b>那一族（批次 WJ-1）：内容在 Core、跳转落到真页、示意图两侧对账。
/// <para><b>这一族最坏的三种样子都不会崩</b>：① "带我去这一页"指向一个没有的页 ⇒ 点了没反应；
/// ② 某一步引了一张没画过的示意图 ⇒ 屏幕上是一片空白而没有任何地方报错；
/// ③ 向导自己写了一份文案 ⇒ 与 Core 那份漂成两套（VC-3 立这条就是为了堵它）。
/// 所以这里的判据全是<b>两侧对账</b>而不是"字段非空"：名字对不对，要看对面那份清单。</para>
/// <para>⛔ 图内不许出现界面文字（用户裁决）：放了字，图就跟着语言与改版走——刚做的界面语言这条线
/// 会立刻被一张焊死中文的图打回原形（坑表 #271 同族："属性写对了"不等于"看得见的是对的"）。</para>
/// </summary>
public sealed class GuideWizardGateTests
{
    private const string NavCode = "src/StarMark.UI/MainWindow.Nav.cs";
    private const string GuideRender = "src/StarMark.UI/Helpers/GuideUi.cs";
    private const string ArtView = "src/StarMark.UI/Helpers/GuideArtView.cs";

    /// <summary>应用真能导航到的页（<c>NavigateToPage</c> 那张 switch，与 <c>GuideGateTests</c> 同一口径）。</summary>
    private static List<string> NavigableTags()
        => Regex.Matches(Code(ReadRepoFile(NavCode)), @"""([a-z]+)""\s*=>\s*typeof\(")
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>UI 那侧真正画出来的示意图名字（<c>GuideArt.Xxx =&gt; ...</c> 那些 case）。
    /// <para>switch 里写的是<b>常量名</b>（<c>GuideArt.Window</c>）而表里存的是<b>常量值</b>（"window"），
    /// 所以比对按忽略大小写——这一族的坏样子是"某个 case 被删掉"或"常量改了名"，两种都会红；
    /// 而把字面小写抄进 switch 反而是第二个出处，不该鼓励。</para></summary>
    private static List<string> DrawnArt()
        => Regex.Matches(Code(ReadRepoFile(ArtView)), @"GuideArt\.(\w+)\s*=>")
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    private static void AssertScannersSeeSomething()
    {
        Assert.True(NavigableTags().Count >= 9, $"只认出 {NavigableTags().Count} 页 ⇒ switch 形状变了，闸门要先跟上");
        Assert.True(DrawnArt().Count >= GuideArt.All.Count, "示意图的 case 形状变了，先看扫描器再判红绿");
        Assert.True(FirstRunGuide.Steps().Count >= 4, $"向导只剩 {FirstRunGuide.Steps().Count} 步，先怀疑表被截断");
    }

    [Fact]
    public void EveryStepThatPromisesAPageCanActuallyBeTakenThere()
    {
        AssertScannersSeeSomething();
        var reachable = new HashSet<string>(NavigableTags(), StringComparer.Ordinal);
        var jumps = FirstRunGuide.Steps().Where(s => s.JumpTag is not null).Select(s => s.JumpTag!).ToList();
        Assert.True(jumps.Count >= 3, "向导里几乎每步都该能把人带到那一页，只剩一两颗跳转说明表被改坏了");
        foreach (var step in FirstRunGuide.Steps())
        {
            if (step.JumpTag is null) continue;
            Assert.True(reachable.Contains(step.JumpTag),
                $"「{step.TitleKey}」要带去 {step.JumpTag}，而那张 switch 到不了这一页 ⇒ 那颗按钮点了没反应");
        }
    }

    /// <summary>
    /// 每一步都要有一张<b>真画出来的</b>图，而每一张画出来的图都要有人用。
    /// <para>方向是两边的：引用了没画的 ⇒ 那一步是一片空白；画了没人引用的 ⇒ 白留一份要维护的几何。
    /// 一片空白是这一族里最难发现的坏法——元素都建起来了，只是没内容。</para>
    /// </summary>
    [Fact]
    public void EveryStepCarriesAPictureAndNoPictureIsUnused()
    {
        AssertScannersSeeSomething();
        var drawn = new HashSet<string>(DrawnArt(), StringComparer.OrdinalIgnoreCase);
        foreach (var step in FirstRunGuide.Steps())
        {
            Assert.True(step.Art is not null, $"「{step.TitleKey}」没配图：向导里出现纯文字的一步＝退回 VC-3 那一版");
            Assert.True(GuideArt.IsReal(step.Art), $"「{step.TitleKey}」配了一张不存在的示意图：{step.Art}");
            Assert.True(drawn.Contains(step.Art!), $"名字认得但 UI 没画：{step.Art}（那一步会是一片空白）");
        }
        var used = new HashSet<string>(FirstRunGuide.Steps().Select(s => s.Art!), StringComparer.Ordinal);
        Assert.True(used.SetEquals(GuideArt.All.Where(GuideArt.IsReal)), "名字表里有画了却没人引用的图");
    }

    /// <summary>
    /// 标题里不许焊死步数（"先知道这四件事"那一版的病）。
    /// <para>步数住在表里，标题再写一遍就是第二个出处：加一步忘了改标题，屏幕上就是一句假话——
    /// 这正是坑表 #272 那条口径要求"封闭集合断言必须报口径"的那个形状，这里干脆不许出现。</para>
    /// </summary>
    [Fact]
    public void TheWizardTitleDoesNotBakeInAStepCount()
    {
        Assert.DoesNotMatch(@"[一二三四五六七八九十0-9]\s*步|这[一两三四五六七八九十0-9]件事", Locale.T(FirstRunGuide.TitleKey));
        Assert.True(LocaleStepCounterComesFromTheTable(),
            "第几步／共几步必须由表算出来（Locale.Fmt + steps.Count），不许再写一个数字进 UI");
    }

    private static bool LocaleStepCounterComesFromTheTable()
        => Code(ReadRepoFile(GuideRender)).Contains("Locale.Fmt(\"guide.step\", index + 1, steps.Count)", StringComparison.Ordinal);

    /// <summary>
    /// 向导的措辞只能来自 Core 那份表，按钮文本只能来自字符串表。
    /// <para>判据两边都钉：<b>内容</b>侧不许出现 <c>new GuideSection(</c>（在 UI 里另立一份段）；
    /// <b>措辞</b>侧四颗按钮一律 <c>Locale.T("guide.…")</c>，且不许有字面按钮文本——
    /// 切了英文之后一颗写着「下一步」的按钮，就是"半中半英"当场重现（批次 WI-1 那一族）。</para>
    /// </summary>
    [Fact]
    public void TheWizardRendersTheTableRatherThanWritingItsOwnWords()
    {
        var code = Code(ReadRepoFile(GuideRender));
        Assert.Equal(1, Count(code, "var steps = FirstRunGuide.Steps();"));
        Assert.Equal(0, Count(code, "new GuideSection("));
        Assert.Equal(0, Count(code, "ButtonText = \""));
        Assert.Equal(0, Count(code, "Content = \""));
        foreach (var key in new[] { "guide.next", "guide.prev", "guide.jump", "guide.step", "guide.primary", "guide.secondary" })
            Assert.Contains($"\"{key}\"", code, StringComparison.Ordinal);
    }

    /// <summary>没有页可去的那一步必须<b>把按钮藏起来</b>，而不是留一颗点了没反应的东西。</summary>
    [Fact]
    public void AStepWithNoPageToShowHidesItsJumpButton()
    {
        var body = MethodBody(Code(ReadRepoFile(GuideRender)), "void Render()");
        Assert.Contains("step.JumpTag is null ? Visibility.Collapsed : Visibility.Visible", body, StringComparison.Ordinal);
        Assert.Equal(0, Count(body, "jump.Visibility = Visibility.Visible;"));   // 无条件显示＝第一类假按钮
    }

    /// <summary>
    /// 跳转只走那一扇导航门（<c>MainWindow.NavigateTo</c>），向导不许自己拧 <c>NavView</c>。
    /// <para>第二条导航路的坏法不是跳错——是它绕开 <c>CurrentPageTag</c> 那一套刷新闸，
    /// 于是"到了那一页而那一页不刷新数据"，这正是 VQ 那一族查过的"点了没反应"。</para>
    /// </summary>
    [Fact]
    public void TheJumpGoesThroughTheOneNavigationDoor()
    {
        var code = Code(ReadRepoFile(GuideRender));
        Assert.Contains("App.MainWindow?.NavigateTo(tag)", code, StringComparison.Ordinal);
        Assert.Equal(0, Count(code, "NavView.SelectedItem"));
        Assert.Equal(0, Count(code, "ContentFrame.Navigate"));
    }

    /// <summary>进度点用<b>形状</b>（实心／空心）区分当前这一步，不只是换个颜色：高对比度主题与色弱下要仍然读得出。</summary>
    [Fact]
    public void TheProgressPipsSeparateTheCurrentStepByShapeNotOnlyColour()
    {
        var pip = MethodBody(Code(ReadRepoFile(GuideRender)), "private static Ellipse Pip");
        Assert.Contains("pip.Fill =", pip, StringComparison.Ordinal);
        Assert.Contains("pip.Stroke = ink", pip, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity", pip, StringComparison.Ordinal);   // 靠透明度区分＝最容易被主题吃掉
    }

    /// <summary>
    /// 示意图只画形状、<b>图内不许有一个字</b>（用户裁决）。
    /// <para>判据是句式而不是清单：只要那颗图元是文本类控件（<c>TextBlock</c>／<c>ContentControl</c> 装了字），
    /// 图就变成第二份文案——跟着语言走、跟着改版过期。</para>
    /// </summary>
    [Fact]
    public void TheSchematicsDrawShapesAndNeverContainWords()
    {
        var code = Code(ReadRepoFile(ArtView));
        Assert.Equal(0, Count(code, "TextBlock"));
        Assert.DoesNotContain("Content = \"", code, StringComparison.Ordinal);
        Assert.Contains("private static Rectangle Box(", code, StringComparison.Ordinal);
        // 六张图必须同尺寸：换步时内容跳动＝读起来像坏了
        Assert.Contains("public const double Side =", code, StringComparison.Ordinal);
        Assert.Contains("public const double Tall =", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// 认不得的图名要<b>出声</b>并画个空框，不能悄悄什么都不摆。
    /// <para>正常走不到（上面那条两侧对账拦着），但"走不到"不等于"可以静默"——
    /// 一片空白在屏幕上读起来就是"引导坏了"（坑表 #271 那一族的兜底口径）。</para>
    /// </summary>
    [Fact]
    public void AnArtNameThatWasNeverDrawnSpeaksInsteadOfLeavingABlank()
    {
        var code = Code(ReadRepoFile(ArtView));
        Assert.Contains("StarLog.Warn(", code, StringComparison.Ordinal);
        var unnamed = MethodBody(code, "private static Canvas Unnamed");
        Assert.Contains("Box(", unnamed, StringComparison.Ordinal);   // 至少留下一个能看见的框
    }
}
