#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-50（P-179）的闸门：<b>截图／贴图那条工具条的 tooltip 与贴图右键菜单上的屏幕字，只许有表里那一份出处</b>，
/// 而且<b>用户可改的那颗全局键⛔ 抄进文案</b>。
/// <para>
/// ⚠ 这一族的证人全是<b>源码形状</b>而不是运行时读数：`StarMark.Tests` 引用不到 `StarMark.UI`（坑表 #370 那条边界），
/// 所以"切到英文之后那条 tooltip 说什么"这一格只能由<b>真机读数</b>补（已排在 #575 那一跑），这里⛔ 假称读过。
/// </para>
/// <para>尺＝抹注释后逐行取连续汉字片段（坑表 #375／#378：调用形状那把尺看不见 <c>TipContent=</c> 与私有包装的实参，
/// 引号配对那把看不见插值串里的三元）；日志行按 #368 那条界线<b>另列且不进表</b>。</para>
/// </summary>
public sealed class CaptureBarWordGateTests
{
    private const string Bar = "src/StarMark.UI/Views/CaptureOverlayWindow.ToolBar.cs";
    private const string Pickers = "src/StarMark.UI/Views/CaptureOverlayWindow.Pickers.cs";
    private const string Pin = "src/StarMark.UI/Views/CaptureOverlayWindow.Pin.cs";
    private const string BarWindow = "src/StarMark.UI/Views/CaptureBarWindow.cs";
    private const string NoticeWindow = "src/StarMark.UI/Views/NoticeCardWindow.cs";
    private const string AppearanceEditor = "src/StarMark.UI/Views/WidgetAppearanceEditor.cs";

    private static readonly Regex Cjk = new(@"[一-鿿]+", RegexOptions.Compiled);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>这一档里"带着汉字且⛔ 日志"的行（逐行原文，红的时候直接印出来，省得下一手再去猜是哪颗）。</summary>
    private static string[] CjkLines(string code)
        => code.Replace("\r\n", "\n").Split('\n')
            .Where(line => Cjk.IsMatch(line) && !line.Contains("StarLog", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    /// <summary>
    /// 那三档⛔ 再留一颗自己的汉字面字（日志除外）。
    /// <para>⚠ 这条是<b>一般式</b>而不是"我上次看到的那几种写法"（台架规则 #131）：
    /// 换一种赋值形状（属性赋值、实参、内插里的三元）都照样落在这把尺里。</para>
    /// </summary>
    [Fact]
    public void TheBarAndThePinMenuCarryNoWordsOfTheirOwn()
    {
        foreach (var rel in new[] { Bar, Pickers, Pin })
        {
            var left = CjkLines(CodeOf(rel));
            Assert.True(left.Length == 0,
                $"{rel} 还剩 {left.Length} 行自己的汉字面字：\n  " + string.Join("\n  ", left));
        }
    }

    /// <summary>
    /// <b>F3／F5 是用户可改的全局热键</b>：文案里⛔ 抄键名，一律收<b>当前绑定</b>那颗键的文本当实参。
    /// <para>同一禁项在批次 VC（⛔ 抄全局热键）与 ✅P-178（主窗那张卡）各定过一次，本批是它的第三处宿主。
    /// 划清界线（卷首那条口径）：<c>Ctrl+C／Ctrl+S／Ctrl+Z／Ctrl+Y／Enter／Esc</c> 是<b>这扇窗自己的</b>按键
    /// （本窗键盘处理，用户改不到），随句子一起进表，不属"抄可改键"这一族。</para>
    /// </summary>
    [Fact]
    public void NoSentenceNamesAUserChangeableHotkey()
    {
        foreach (var rel in new[] { Bar, Pickers, Pin })
        {
            var code = CodeOf(rel);
            Assert.False(Regex.IsMatch(code, @"\bF[1-9]\b"), $"{rel} 的文案里又抄了一颗裸功能键（F1–F9 用户可改）");
            Assert.DoesNotContain("Ctrl+Alt", code);
        }
        // 三处抄键的句子＝三处借那颗公共出口（✅P-178 的裁决：⛔ 搬家、⛔ 第三处自己写一份键位文本）
        Assert.Equal(2, SourceGate.Count(CodeOf(Bar), "CanvasService.BindingText("));
        Assert.Equal(1, SourceGate.Count(CodeOf(Pin), "CanvasService.BindingText("));
        Assert.DoesNotContain("HotkeyDisplay.Display(", CodeOf(Bar));
        Assert.DoesNotContain("HotkeyDisplay.Display(", CodeOf(Pin));
        // ⚠ 光数"借了几次那颗公共出口"抓不到**递错动作**：两句都说"按这颗键"，把 F3 与 F5 对调之后
        // 计数、汉字面字、裸功能键三条判据全绿，屏幕上却是一条说明配了一颗哑键 ⇒ 键与句子得**成对**钉住。
        var bar = CodeOf(Bar);
        Assert.Contains(
            @"LocMessage.Of(""capture.bar.pin"", CanvasService.BindingText(HotkeyActions.ScreenPin))", bar);
        Assert.Contains(
            @"LocMessage.Of(""capture.bar.through"",
                CanvasService.BindingText(HotkeyActions.ScreenPinClickThrough))", bar);
        Assert.Contains(
            @"Locale.Fmt(""pin.bar.through"", CanvasService.BindingText(HotkeyActions.ScreenPinClickThrough))", CodeOf(Pin));
    }

    /// <summary>
    /// 引用<b>屏幕上别处那一行</b>的名字时交实参，⛔ 抄在句子里（✅WI-46c 那条同一口径）：
    /// 「贴图组」是托盘那一行、五颗图形名与三档档位名是表里那几颗。
    /// </summary>
    [Fact]
    public void WordsOwnedElsewhereArePassedInNotRetyped()
    {
        var bar = CodeOf(Bar);
        Assert.Equal(2, SourceGate.Count(bar, @"LocMessage.Of(""pin.card.groupCategory"")"));
        // 图形那一串的名单由那颗主人现给：加一支图形笔时这句话跟着变（⛔ 在 tooltip 里再抄五颗名字）
        Assert.Equal(1, SourceGate.Count(bar, "AnnotationTools.Shapes.Select(static shape => (object?)Annotation.ToolName(shape))"));
        // 档位名从前在这里是<b>第三份</b>字面量（细 / 中 / 粗）⇒ 交表里那颗主人；✅WI-65 片3 进一步：那排名字⛔
        // 在交出之前就被 string.Join 念完（外层换语言重贴时里面会停旧话）⇒ 交 LocJoin，连接符本身也进表。
        Assert.Equal(2, SourceGate.Count(bar, "new LocJoin("));
        Assert.Equal(0, SourceGate.Count(bar, @"string.Join("" / "", Annotation."));
    }

    /// <summary>
    /// 角标那两处（穿透那句、不透明度那句）是<b>每次状态变化重贴</b>的：取词发生在写的那一刻，
    /// ⛔ 挂 <c>LocText</c>——那是常驻那一族的路，挂上来只会多几条贴不出去的空壳（WI-44／WI-45 同条判据）。
    /// </summary>
    [Fact]
    public void TheStatusBadgeRewordsItselfAtTheMomentItChanges()
    {
        var pin = CodeOf(Pin);
        Assert.Equal(2, SourceGate.Count(pin, @"Locale.Fmt(""pin."));
        Assert.DoesNotContain("LocText.Set", pin);
        Assert.DoesNotContain("LocText.Attach", pin);
    }

    /// <summary>
    /// 这一族的<b>每一颗键都得有人问</b>（通用闸门 <c>TheTableHasNoRowNothingAsksFor</c> 已经钉过全表，
    /// 这里再按本批的名单点一次名：⛔ 只靠那一条计数判据——它抓不到"问错了键"）。
    /// </summary>
    [Fact]
    public void EveryRowThisBatchShippedIsActuallyAskedFor()
    {
        var asked = new[] { Bar, Pickers, Pin }.Select(CodeOf).Aggregate((a, b) => a + b);
        // ✅WI-65 片3：这一档的句子一半已改交「键＋实参」⇒ 数键的那把尺⛔ 只认旧写法（只认旧写法就只数到 17 颗，
        // 而"每一颗键都得有人问"这条判据会<b>静默变弱</b>＝坑表 #486 同一族）。两种写法一起数。
        var keys = Regex.Matches(asked, @"(?:Locale\.(?:T|Fmt)|LocMessage\.Of)\(""([a-z][\w.]+)""").Cast<Match>()
            .Select(m => m.Groups[1].Value).Distinct().ToArray();
        var known = new HashSet<string>(UiStrings.All.Select(e => e.Key), StringComparer.Ordinal);
        Assert.True(keys.Length >= 30, $"这一族只数到 {keys.Length} 颗键（本批落地后现场是 30＋）");
        foreach (var key in keys)
            Assert.True(known.Contains(key), $"代码在问一颗表里没有的键：{key}");
        foreach (var prefix in new[] { "capture.bar.", "capture.pick.", "pin.menu.", "pin.bar." })
            Assert.Contains(keys, k => k.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// 窗口标题那一族：本批交完的那几档宿主里，<c>Title</c> 只许由 <c>LocText.SetWindowTitle</c> 写——
    /// WinUI 3 的 <c>Window</c> 不是 <c>DependencyObject</c>，进不了附带属性那棵树（坑表 #338），
    /// 那一族的另一条路（<c>Locale.T</c> 当场取词）会让标题停在旧语言。
    /// <para>⚠ 射程＝<b>整族交完的那几档</b>：全仓同一个形状还剩一处（<c>SettingsPage.Ai.cs</c> 那颗确认框抬头），
    /// 属 P-169 ①（WI-41 那一族还没交完，提前交＝标题跟语言、正文不跟，WI-46a 判定 3 的口径）。交完时把宿主加进这份名单。</para>
    /// <para>批次 WI-52（P-183）加的第三档＝「组件外观」那扇编辑器窗：它先 <c>var win = new Window()</c>
    /// 再交标题，所以那颗调用式的宿主名与前两档（<c>this</c>）不同——名单因此带上<b>那颗调用怎么写</b>，
    /// ⛔ 把三种形状并成一条"含 SetWindowTitle"就算过（那等于不再看它写在哪扇窗上）。</para>
    /// </summary>
    [Fact]
    public void TheWindowTitlesOfTheHostsClosedHereComeFromTheTable()
    {
        foreach (var (rel, call, key) in new[]
                 {
                     (BarWindow, @"LocText.SetWindowTitle(this, ""capture.bar.windowTitle"")", "capture.bar.windowTitle"),
                     (NoticeWindow, @"LocText.SetWindowTitle(this, ""card.windowTitle"")", "card.windowTitle"),
                     (AppearanceEditor, @"LocText.SetWindowTitle(win, ""widget.appearance.windowTitle"")", "widget.appearance.windowTitle"),
                 })
        {
            var code = CodeOf(rel);
            Assert.Contains(call, code, StringComparison.Ordinal);
            Assert.Contains(UiStrings.Keys(), k => k == key);
            // 一般式（台架规则 #131）：⛔ 只钉上次那一行字面量——宿主自己往 Title 上写值的任何形状
            // （直接赋值、object initializer、内插串）都算这一族回来了。SetWindowTitle 那颗方法名不算。
            var own = Regex.Matches(code, @"(?<![A-Za-z0-9_])Title\s*=").Count;
            Assert.True(own == 0, $"{rel} 还自己写 {own} 处 Title（窗口标题只许交 LocText 的第二张名单）");
        }
    }
}
