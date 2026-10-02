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
/// ② 颜色与截止日期两颗入口的具体形状——行 id 只许从<b>被点那颗按钮</b>的 <c>Tag</c> 取、档位是捕获变量、
/// 整条调用式含实参，以及"次日"算法只许走
/// <see cref="StarMark.Abstractions.LocalItemState.DayOffsetStartUnix"/>，
/// 不许退回 <c>DateTimeOffset.Now.AddDays</c>（定长 24 绝对小时在夏令时回拨那天会跳错日历日，P-37）。
/// </para>
/// <para>
/// 批次 UN 把 ② 从"只钉截止日期"扩成"两颗入口一起钉"，起因是用户真机反馈<b>「待办的颜色标记无效」</b>：
/// 那颗颜色点走的是同一副 XAML 声明的 flyout ＋ <c>item.Parent</c> 反查，而这条反查<b>在这台机器上从来没通过</b>
/// （库里 8 条待办没有一个 <c>color</c> 键；同一模板里那颗不套 flyout、直接 <c>Tag="{x:Bind Id}"</c> 的
/// CheckBox 却写过 <c>done</c>）。两处的写入面 <c>SetColorAsync</c>／<c>SetDueAsync</c> 一直都有，缺的仍是接线。
/// </para>
/// </summary>
public sealed class TodoWriterWiringGateTests
{
    private const string VmPath = "src/StarMark.UI/ViewModels/TodoWidgetViewModel.cs";
    private const string ViewPath = "src/StarMark.UI/Views/TodoWidget.xaml";
    private const string CodeBehindPath = "src/StarMark.UI/Views/TodoWidget.xaml.cs";

    /// <summary>
    /// 待办视图的 XAML：这里读的针都是"标记里的属性/元素"，而<b>说明注释里必然要写出被禁的那副形状</b>
    /// （"不许退回 XAML 声明的 MenuFlyout"这句话本身就带着 <c>&lt;MenuFlyoutItem</c>）。
    /// 所以断言前一律先抹 <c>&lt;!-- --&gt;</c>，否则注释替代码答话（#123／坑表 #228）。
    /// </summary>
    private static string MarkupXaml()
        => Regex.Replace(SourceGate.ReadRepoFile(ViewPath), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>
    /// C# 侧同理：本门的针全是<b>代码片段</b>（<c>ViewModel.SetColorAsync(id, 变量)</c> 那一整句的形状），
    /// 而"把那一行注释掉"是最便宜的假装修——读原文的话它永远在，闸门就白写（#195→规则 14）。
    /// </summary>
    private static string CodeBehind() => SourceGate.Code(SourceGate.ReadRepoFile(CodeBehindPath));

    private static string VmCode() => SourceGate.Code(SourceGate.ReadRepoFile(VmPath));

    /// <summary>
    /// 带某个 <c>Click</c> 的那颗开始标记里<b>必须同时带着行 id</b>。
    /// 两个属性分开数会放过"Click 接上了、Tag 漏了"——那正是颜色点哑掉的那副形状（id 到不了写入方法）。
    /// </summary>
    private static int ButtonsWithRowIdAndClick(string xaml, string click)
        => Regex.Matches(xaml, @"<Button\b[^>]*>", RegexOptions.Singleline).Cast<Match>()
            .Count(m => m.Value.Contains($"Click=\"{click}\"")
                        && m.Value.Contains(@"Tag=""{x:Bind Id}"""));

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
        var vm = VmCode();
        var behind = CodeBehind();

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

    /// <summary>
    /// 两颗入口（颜色／截止日期）的具体形状：<b>行 id 只许从被点按钮的 Tag 取</b>、菜单在 code-behind 现建、档位是捕获变量；
    /// XAML 里那份"MenuFlyoutItem 带 Tag ＋ 反查 Parent"的写法不许回来——它就是那颗颜色点哑了一年多的那条路。
    /// </summary>
    [Fact]
    public void TheColorAndDueEntriesTakeRowIdFromTheClickedButtonAndTheDayMathLivesOnce()
    {
        var xaml = MarkupXaml();
        var behind = CodeBehind();
        var vm = VmCode();

        // ① 两颗入口各接上一个 Click，且<b>同一颗开始标记里</b>带着行 id；认不到 id 不许静默
        Assert.Equal(1, ButtonsWithRowIdAndClick(xaml, "ColorButton_Click"));
        Assert.Equal(1, ButtonsWithRowIdAndClick(xaml, "DueButton_Click"));
        Assert.Contains("StarLog.Warn", behind);
        Assert.Equal(2, SourceGate.Count(behind, "TryRowId(sender, out var id)"));              // 两颗都过同一道认 id 的关口
        Assert.Matches(@"sender is FrameworkElement \{ Tag: long \w+ \}", behind);              // id 只许来自被点那颗元素的 Tag

        // ② 那份"XAML 声明的 flyout ＋ 从菜单项反查 id/Parent"必须整条消失
        Assert.DoesNotContain("<MenuFlyoutItem", xaml);
        Assert.DoesNotContain("item.Parent is not MenuFlyout", behind);

        // ③ 钉的是实参<b>形状</b>（行 id ＋ 一个变量档位），不是我的局部变量名——改名不该红（规则 26），
        //    但"绕开档位表硬编码一档"和"档位靠菜单项位置反推"必须红。
        Assert.Matches(@"ViewModel\.SetColorAsync\(id, \w+\)", behind);
        Assert.Matches(@"ViewModel\.SetDueAsync\(id, \w+\)", behind);
        Assert.DoesNotMatch(@"ViewModel\.SetColorAsync\(id, \d", behind);
        Assert.DoesNotMatch(@"ViewModel\.SetDueAsync\(id, \d", behind);
        Assert.DoesNotContain(".Items.IndexOf(", behind);                                       // 档位靠位置反推＝当初断掉的那一环

        // ④ 档位表逐档都在、且各自只一份（少一档＝那一档没入口，正是这批栽过的形状）。两张表分开数：
        //    颜色 0～6 共 7 档，截止 0／1／null 共 3 档——"0"在两表各出现一次，所以必须分块数。
        static string Block(string src, string anchor)
        {
            var from = src.IndexOf(anchor, StringComparison.Ordinal);
            Assert.True(from >= 0, $"找不到档位表 {anchor}（表被改名或删掉＝入口整条没了，这条正是要拦的）");
            return src[from..src.IndexOf("};", from, StringComparison.Ordinal)];
        }
        var colorTiers = Block(behind, "ColorTiers =");
        var dueTiers = Block(behind, "DueTiers =");
        Assert.Equal(7, Regex.Matches(colorTiers, @"\(\d+, ""[^""]+""\)").Count);
        Assert.Equal(3, Regex.Matches(dueTiers, @"\((?:\d+|null), ""[^""]+""\)").Count);
        for (var tier = 0; tier <= 6; tier++)
            Assert.Single(Regex.Matches(colorTiers, @"\(" + tier + @", ""[^""]+""\)"));

        // ⑤ "次日"算法只住一处
        Assert.Contains("LocalItemState.DayOffsetStartUnix(DateTimeOffset.Now, dayOffset.Value)", vm);
        Assert.DoesNotContain("DateTimeOffset.Now.AddDays(", vm);             // 定长 24 小时会跳错日历日（P-37）
    }
}
