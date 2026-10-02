#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 设置页里<b>凭据输入格</b>的形状守门（批次 UI，P-146）。
/// <para>
/// 起因是一条真实存在的泄漏面：<c>TextBox</c> 的内容在 UIA 树里就是<b>可读的 Value</b>——读屏、放大镜、
/// 自动化、截图这类走 UIA 或走像素的路径都能把 GitHub Token 带走，而且不需要任何权限。
/// <c>PasswordBox</c> 不给这个 Value，代价是它的 <c>Password</c> <b>不可绑定</b>（WinUI 刻意如此），
/// 于是"档 → 界面 → 档"这一条链必须由代码里<b>那一对读写</b>撑起来。
/// </para>
/// <para>
/// 所以这一族要钉的是<b>两件事一起成立</b>，缺一不可：① 那一格确实是打码的（不是 TextBox）；
/// ② 换成 code-behind 之后"只有一个编辑入口、且只有一对读写"这条没有松动——
/// 以前它是靠数 <c>Mode=TwoWay</c> 钉住的（<see cref="SettingsTaxonomyGateTests.EachSettingHasExactlyOneEditor"/>），
/// 那套计数对 PasswordBox 根本不成立，直接把属性从那张表里删掉就等于<b>没人管这一格了</b>。
/// </para>
/// <para>
/// 判据一律<b>不钉控件名</b>：名字是从代码里 <c>.Password</c> 的用法派生出来的。
/// 改名（<c>GithubTokenBox → GithubTokenInput</c> 这种）必须原样放过——把名字钉死会让下一次合理的
/// 重命名变成一次"闸门红了"的假事故，而那正是让人去放宽判据的最快路径（#33 那条纪律）。
/// </para>
/// </summary>
public sealed class CredentialInputGateTests
{
    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string PageCode = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string ViewModel = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";

    /// <summary>住在 ViewModel 里、以字符串形态存的凭据属性。<b>只加不减</b>：
    /// 再加一颗（比如哪天设置页能填其它 Token）就登记进来，否则那一格连"不许用明文框"这条都不成立。
    /// 这张表不是靠人记得住——<see cref="TheCredentialTableCoversEveryCredentialShapedViewModelProperty"/>
    /// 拿源码里实际存在的属性反过来对它，删表项与"加了新凭据却没登记"都会当场红。</summary>
    private static readonly string[] CredentialProperties = { "GithubToken" };

    /// <summary>形状像凭据的属性名后缀（小写比对）：新增一颗而表里没它，就是这一族要拦的那种"顺手加一格明文框"。</summary>
    private static readonly string[] CredentialNameMarks = { "token", "apikey", "api_key", "secret", "password", "credential" };

    private static string Markup(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>扫出所有<b>绑定到指定属性</b>的输入元素（跨行也算：属性可以换行写）。</summary>
    private static List<string> PlainBoxesBoundTo(string xaml, string property)
        => Regex.Matches(Markup(xaml), @"<TextBox\b[^>]*?/?>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .Where(el => el.Contains($"ViewModel.{property}", StringComparison.Ordinal))
            .ToList();

    /// <summary>代码里被当作密码框读过的那些控件名（<c>Xxx.Password</c> 的 <c>Xxx</c>）。</summary>
    private static string[] BoxesReadThroughPassword(string code)
        => Regex.Matches(code, @"(\w+)\.Password").AsEnumerable()
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// 那张凭据表<b>不许被偷偷删空</b>，也不许漏掉新长出来的凭据：拿 ViewModel 里实际存在的
    /// <c>[ObservableProperty] private string _xxx</c> 反向对账，名字形状像凭据的必须<b>逐个登记</b>。
    /// <para>为什么这一格比前面几格更要紧：前面全是"按表扫"，表 emptied 之后每一格都会因为
    /// "没有东西要查"而整齐地绿过去——那是这一族最坏的一种假绿（查不出任何东西， yet 一句红都没有）。</para>
    /// </summary>
    [Fact]
    public void TheCredentialTableCoversEveryCredentialShapedViewModelProperty()
    {
        var shaped = Regex.Matches(Code(ReadRepoPartials(ViewModel)),
                @"\[ObservableProperty\]\s+private\s+string\s+_(\w+)")
            .Select(m => m.Groups[1].Value)
            .Where(n => CredentialNameMarks.Any(mark => n.ToLowerInvariant().Contains(mark, StringComparison.Ordinal)))
            .Select(n => char.ToUpperInvariant(n[0]) + n[1..])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        // 锚点自证：这一族今天确实存在（Token 那一颗），否则"表与源码相互对账"这句话是空的
        Assert.NotEmpty(shaped);
        Assert.Equal(shaped, CredentialProperties.OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>禁项扫描器自己要有证人：喂一段"退回明文框"的样串必须命中，
    /// 否则这一格是装饰（#228/#236 那一族——注释替被改掉的形状答题、锚点死了守门白过）。</summary>
    [Fact]
    public void ThePlainTextBoxScannerActuallyBites()
    {
        var sample = "<StackPanel><TextBox PlaceholderText=\"ghp_...\" Text=\"{x:Bind ViewModel.GithubToken, Mode=TwoWay}\"/></StackPanel>";
        Assert.Single(PlainBoxesBoundTo(sample, "GithubToken"));
        // 同一套扫描必须放过合法形状：打码框、以及"文字里出现那个属性名但元素不是 TextBox"
        Assert.Empty(PlainBoxesBoundTo("<PasswordBox x:Name=\"GithubTokenBox\"/>", "GithubToken"));
        Assert.Empty(PlainBoxesBoundTo("<TextBlock Text=\"ViewModel.GithubToken\"/>", "GithubToken"));
        // 也放过写在注释里的旧写法：守门读的是抹掉注释之后的那一份，注释不该替被改掉的形状答题
        Assert.Empty(PlainBoxesBoundTo("<!-- TextBox Text=\"{x:Bind ViewModel.GithubToken}\" --><Grid/>", "GithubToken"));
    }

    /// <summary>每一颗凭据属性<b>都不得</b>由 <c>TextBox</c> 承载。今天的现场就是这一格被咬住过（批次 UI）。</summary>
    [Fact]
    public void NoCredentialIsTypedIntoAPlainTextBox()
    {
        var xaml = ReadRepoFile(Xaml);
        foreach (var property in CredentialProperties)
            Assert.Empty(PlainBoxesBoundTo(xaml, property));
    }

    /// <summary>代码里读过的每一颗密码框，在 XAML 里<b>必须是 PasswordBox 且只有一颗</b>，
    /// 并且<b>没有同名的 TextBox 残留</b>（换控件最容易留下的就是那一半旧标记）。
    /// 名字派生自代码，不派生自常量：合理改名不许惊动这一格。</summary>
    [Fact]
    public void EveryBoxReadThroughPasswordIsDeclaredMaskedExactlyOnce()
    {
        var xaml = Markup(ReadRepoFile(Xaml));
        var code = Code(ReadRepoPartials(PageCode));
        var boxes = BoxesReadThroughPassword(code);
        // 锚点自证：这一页确实有密码框（AI 的 Key 与 GitHub 的 Token），扫到 0 颗就是锚点死了
        Assert.True(boxes.Length >= 2, $"只从代码里派生出 {boxes.Length} 颗密码框——锚点失效，守门不许白过");
        foreach (var box in boxes)
        {
            Assert.Single(Regex.Matches(xaml, $"<PasswordBox\\b[^>]*x:Name=\"{box}\"", RegexOptions.Singleline));
            Assert.Empty(Regex.Matches(xaml, $"<TextBox\\b[^>]*x:Name=\"{box}\"", RegexOptions.Singleline));
        }
    }

    /// <summary>
    /// 换成不可绑定的控件之后，"只有一个编辑入口"这条换了形状但仍要成立：
    /// 对每一颗凭据属性，代码里<b>档→界面</b>与<b>界面→档</b>各恰好一处，<b>而且是同一颗控件</b>。
    /// <para>为什么合不成一处：分在两颗控件上就是"界面显示 A 的密码、保存的却是 B"——
    /// 编译器不管、运行时也不报错，症状是"用户明明填了 Token，同步还是说没配置"。
    /// 为什么各只许一处：两处读就是两份真相（与 <see cref="SettingsTaxonomyGateTests.EachSettingHasExactlyOneEditor"/> 同一族）。</para>
    /// </summary>
    [Fact]
    public void EachCredentialHasExactlyOnePairOfPasswordReadersOnTheSameBox()
    {
        var code = Code(ReadRepoPartials(PageCode));
        foreach (var property in CredentialProperties)
        {
            var into = Regex.Matches(code, $@"(\w+)\.Password\s*=\s*ViewModel\.{property}\b");
            var outOf = Regex.Matches(code, $@"ViewModel\.{property}\s*=\s*(\w+)\.Password\b");
            Assert.Single(into);
            Assert.Single(outOf);
            Assert.Equal(into[0].Groups[1].Value, outOf[0].Groups[1].Value);   // 两头指同一颗控件
        }
    }

    /// <summary>
    /// <b>载入那一行必须落在 <c>LoadFromStoreSilently</c> 的抑制区内</b>。
    /// <para>赋 <c>Password</c> 会当场触发 <c>PasswordChanged</c>，而那个处理器往 ViewModel 里推值——
    /// 属性变更是去抖保存的触发源（批次 GM/HC 那一族），于是"打开设置页"就变成"重写一次凭据档"。
    /// 这条不需要人发现：它今天成立，明天有人觉得"顺手放到别处更清楚"就破了，而症状只是多写几次盘。</para>
    /// </summary>
    [Fact]
    public void TheLoadDirectionHappensInsideTheSaveSuppressedRegion()
    {
        var code = Code(ReadRepoPartials(PageCode));
        var body = MethodBody(code, "private void LoadFromStoreSilently()");
        Assert.Contains("_suppressSave = true", body, StringComparison.Ordinal);
        Assert.Matches(@"(\w+)\.Password\s*=\s*ViewModel\.GithubToken", body);
        // 反向：那一行不许同时出现在抑制区之外（别处再来一次赋值就是第二次写盘的入口）
        var elsewhere = Code(ReadRepoPartials(PageCode)).Replace(body, string.Empty);
        Assert.DoesNotMatch(@"(\w+)\.Password\s*=\s*ViewModel\.GithubToken", elsewhere);
    }

    /// <summary>打码不是只把界面挡住：<b>那一条链上任何一环都不许把密码写进日志</b>。
    /// （凭据落盘的账在 P-30，本格只管这一族新加的那条路——改成 code-behind 之后，
    /// 代码里读密码的地方从"没有"变成"有"，所以这条要当场跟上。）</summary>
    [Fact]
    public void NoLogLineReadsAPasswordField()
    {
        var offenders = Code(ReadRepoPartials(PageCode)).Split('\n')
            .Where(l => l.Contains("StarLog", StringComparison.Ordinal) && l.Contains(".Password", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(offenders);
    }
}
