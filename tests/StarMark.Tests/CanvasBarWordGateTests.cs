#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-51（P-180）的闸门：<b>画布那条工具条上的屏幕字，只许有表里那一份出处</b>，
/// 而且<b>嵌着别的按钮名字的那几句只许交「键＋没渲染的实参」</b>。
/// <para>
/// ⚠ 这一族的证人全是<b>源码形状</b>：<c>StarMark.Tests</c> 引用不到 <c>StarMark.UI</c>（坑表 #370 那条边界），
/// 所以"切到英文之后那颗 tooltip 说什么"只能由<b>真机读数</b>补（已排在 #575 那一跑），这里⛔ 假称读过。
/// </para>
/// <para>
/// 尺＝抹注释后逐行取连续中日韩片段（坑表 #375／#378：调用形状那把尺看不见标记里的 <c>Content=""</c>，
/// 引号配对那把看不见插值串里的三元）。日志行按 #368 那条界线另列，本批两档宿主里没有日志行。
/// </para>
/// </summary>
public sealed class CanvasBarWordGateTests
{
    private const string Xaml = "src/StarMark.UI/Views/CanvasToolbarWindow.xaml";
    private const string Code = "src/StarMark.UI/Views/CanvasToolbarWindow.xaml.cs";
    private const string LocText = "src/StarMark.UI/Helpers/LocText.cs";
    private const string AnnotationCs = "src/StarMark.Core/Capture/Annotation.cs";
    private const string Stage = "src/StarMark.Core/Capture/AnnotationStage.cs";
    private const string Hub = "src/StarMark.UI/Services/AnnotationHub.cs";

    private static readonly Regex Cjk = new("[\\u3000-\\u30ff\\u3400-\\u4dbf\\u4e00-\\u9fff\\uff00-\\uffef]", RegexOptions.Compiled);

    private static string XamlMarkup()
        => Regex.Replace(SourceGate.ReadRepoFile(Xaml), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>带着中日韩片段的行（逐行原文；红的时候直接印出来，省得下一手再猜是哪颗）。</summary>
    private static string[] CjkLines(string rel)
        => (rel.EndsWith(".xaml", StringComparison.Ordinal)
                ? Regex.Replace(SourceGate.ReadRepoFile(rel), "<!--.*?-->", "\n", RegexOptions.Singleline)
                : SourceGate.Code(SourceGate.ReadRepoFile(rel)))
            .Replace("\r\n", "\n").Split('\n')
            .Where(line => Cjk.IsMatch(line) && !line.Contains("StarLog", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    /// <summary>
    /// 那一档窗的两份文件（标记与 code-behind）⛔ 再留一颗自己的字面字。
    /// <para>⚠ <b>一般式</b>而不是"我上次看到的那几种写法"（台架规则 #131）：加一颗新按钮时无论是
    /// <c>Content=""</c>、<c>ToolTipService.ToolTip=""</c>、代码里的实参还是内插串，都落在这把尺里。</para>
    /// </summary>
    [Fact]
    public void TheBarCarriesNoWordsOfItsOwn()
    {
        foreach (var rel in new[] { Xaml, Code, Stage, Hub })
        {
            var left = CjkLines(rel);
            Assert.True(left.Length == 0,
                $"{rel} 还剩 {left.Length} 行自己的汉字面字：\n  " + string.Join("\n  ", left));
        }
    }

    /// <summary>
    /// 标记里每一颗按钮的<b>名字只能由那颗键给</b>：正文一颗键、说明一颗键，<c>Content=""</c> 与
    /// <c>ToolTipService.ToolTip=""</c> 那两条旧出口在这一档一律关掉。
    /// <para>唯一的例外是「⌨」那颗：它的正文是一个<b>记号</b>不是词（⛔ 进表——表里存的是给人读的话，
    /// 不是键帽图形），但它同样必须挂着那颗说明键。</para>
    /// </summary>
    [Fact]
    public void EveryButtonOnTheBarGetsItsWordFromAKey()
    {
        var xaml = XamlMarkup();
        var buttons = Regex.Matches(xaml, @"<Button[^>]*>", RegexOptions.Singleline);
        // 下界防"整排被删空"（本批现场 15 颗：三支笔＋撤销/重做/清屏/穿透/白板/幕布/光晕/贴图/存图/复制/⌨/✕）
        Assert.True(buttons.Count >= 15, $"这一档只数到 {buttons.Count} 颗按钮，标记大概被截断了");
        foreach (var match in buttons.Cast<Match>())
        {
            var tag = match.Value;
            if (tag.Contains("Content=\"⌨\"", StringComparison.Ordinal))
            {
                Assert.Contains("i18n:LocText.ToolTipKey=", tag, StringComparison.Ordinal);
                continue;
            }
            Assert.True(!tag.Contains("Content=\"", StringComparison.Ordinal),
                $"标记里又写了正文面字：{tag.Trim()}");
            Assert.Contains("i18n:LocText.Key=", tag, StringComparison.Ordinal);
        }
        // 那条旧路（附带属性直接挂一句话）换语言不重贴 ⇒ 在这一档整条关掉
        Assert.DoesNotContain("ToolTipService.ToolTip=", xaml);
    }

    /// <summary>
    /// <b>别人已经管着的名字，这一档只借不抄</b>：三支笔与三档档位名的主人是 Core 的词厂（✅P-181），
    /// 「贴图」那颗的名字主人是贴图那张卡。⛔ 给它们另开一颗 <c>canvas.btn.*</c> 行——
    /// 那正是"同一个东西在两条栏上叫法不一样"的起点（WI-49 并句之前的真症状）。
    /// </summary>
    [Fact]
    public void WordsOwnedElsewhereAreBorrowedNotRetyped()
    {
        var xaml = XamlMarkup();
        foreach (var key in new[]
                 {
                     "anno.tool.pen", "anno.tool.highlighter", "anno.tool.eraser",
                     "anno.thickness.thin", "anno.thickness.medium", "anno.thickness.thick",
                     "pin.card.category",
                 })
        {
            Assert.Equal(1, SourceGate.Count(xaml, $"i18n:LocText.Key=\"{key}\""));
            Assert.Contains(UiStrings.Keys(), k => k == key);
        }
        // 一般式（台架规则 #131）：⛔ 只钉"上次那种写法"——这一族的借名一律不许在画布前缀下再立一行
        foreach (var dup in new[] { "pen", "marker", "highlighter", "eraser", "thin", "medium", "thick", "pin" })
        {
            var forbidden = "canvas.btn." + dup;
            Assert.DoesNotContain(UiStrings.Keys(), k => k == forbidden);
        }
        // 颜色格子由表里那颗 <c>NameKey</c> 现算（✅P-181）：这里钉的是"条上没有第二份颜色名单"
        Assert.Contains("LocText.SetToolTipKey(button, colour.NameKey)", CodeOf(Code));
    }

    /// <summary>
    /// 状态行是<b>每次状态变化重写</b>的那一族：取词发生在写的那一刻，⛔ 挂 <c>LocText</c>
    /// （WI-44／WI-45 同一条判据——挂上来只会多几条贴不出去的空壳）。
    /// <para>⚠ 这条与 <c>Locale.Changed += Refresh</c> 是<b>一对</b>：既然取词在调用点，换语言时就没人
    /// 替它重贴，那一行会一直停在旧语言直到用户下一次动工具。只交表不订阅＝屏幕上半中半英（本批的真缺陷形状）。</para>
    /// </summary>
    [Fact]
    public void TheStatusLineRewordsItselfAtTheMomentItChanges()
    {
        var status = SourceGate.MethodBody(CodeOf(Code), "private string StatusText()");
        Assert.True(SourceGate.Count(status, @"Locale.Fmt(""canvas.status.") >= 4,
            $"状态行只从表里取了 {SourceGate.Count(status, @"Locale.Fmt(""canvas.status.")} 句（现场是 5 句：墨／幕布尾巴／绘制中／被白板拦／交回鼠标／穿透中）");
        Assert.DoesNotContain("LocText.Set", status);
        var bar = CodeOf(Code);
        Assert.DoesNotContain("LocText.SetMessage", bar);
        Assert.Contains("Locale.Changed += Refresh;", bar);
        Assert.Contains("Locale.Changed -= Refresh;", bar);         // 订阅与摘掉必须成对（事件泄漏那一族）
    }

    /// <summary>
    /// <b>嵌着别的按钮名字的悬停说明只许交「键＋没渲染的那句」</b>（✅WI-36 那颗窗口标题同一条口径）。
    /// <para>递已渲染的词＝换语言时外层由 <c>LocText</c> 重贴、句子里那两颗名字还停在旧语言（坑表 #276 那一族：
    /// 屏幕说明与实现分岔，只有换过一次语言才看得见）；而实参必须走 <see cref="LocMessage"/>，
    /// 因为 <c>string.Format</c> 只对 <c>ToString()</c> 出声、那颗嵌套句要<b>在贴上去那一刻</b>才取词。</para>
    /// </summary>
    [Fact]
    public void TooltipsThatQuoteAnotherButtonPassKeysNotWords()
    {
        var build = SourceGate.MethodBody(CodeOf(Code), "private void BuildEmbeddedNameTooltips()");
        Assert.Equal(4, SourceGate.Count(build, "LocText.SetToolTipKey("));
        Assert.True(SourceGate.Count(build, "LocMessage.Of(") >= 6,
            "那四句里嵌的名字必须逐个交没渲染的那句（现场 7 个实参）");
        // 一般式：这一族里⛔ 任何"当场取词"的写法（换了形状也算）
        Assert.DoesNotContain("Locale.T(", build);
        Assert.DoesNotContain("Locale.Fmt(", build);
        Assert.DoesNotContain("Annotation.ToolName(", build);
        // 图形那五颗的说明同一形状：外层一句键，名字那颗交键不交词
        var shapes = SourceGate.MethodBody(CodeOf(Code), "private void BuildShapeButtons()");
        Assert.Contains(@"LocText.SetToolTipKey(button, key, LocMessage.Of(tool.NameKey()))", shapes);
        // 而贴的时候必须真的带上实参（⛔ 回到 Register(..., null)：那颗键还在被问，变的是它收到的实参——
        // 计数型判据抓不到（✅WI-46c 台架 B3 同一条），所以这里钉的是机制那一句）
        var paste = SourceGate.MethodBody(CodeOf(LocText), "private static void OnToolTipKeyChanged");
        Assert.Contains("ToolTipArgStore.TryGetValue(d, out var box)", paste);
        Assert.Contains("Register(d, ToolTipService.ToolTipProperty, key!, args);", paste);
        Assert.Contains("ToolTipService.SetToolTip(d, Text(key!, args));", paste);
        Assert.DoesNotContain("Register(d, ToolTipService.ToolTipProperty, key!, null);", paste);
        // 不带实参那一路必须把旧信箱清掉，否则下一次换语言按上一句的读数填空
        Assert.Contains("ToolTipArgStore.Remove(element);",
            SourceGate.MethodBody(CodeOf(LocText), "public static void SetToolTipKey(DependencyObject element, string value)"));
    }

    /// <summary>
    /// 工具名到这条栏上必须是<b>键</b>：词厂那颗 <c>ToolNameKey</c> 交键、<c>ToolName</c> 才取词，
    /// 而画布那支笔的 <c>NameKey()</c> 只是它的一层投影（⛔ 第二份名单，✅P-181 那条口径）。
    /// </summary>
    [Fact]
    public void ToolNamesReachTheBarAsKeys()
    {
        var keyFactory = SourceGate.MethodBody(CodeOf(AnnotationCs), "public static string ToolNameKey(AnnotationTool tool)");
        Assert.DoesNotContain("Locale.T(", keyFactory);              // 一般式：这一颗只交键，取词不在这里
        foreach (var tool in Enum.GetValues<AnnotationTool>())
            Assert.True(UiStrings.TryGet(Annotation.ToolNameKey(tool), out _),
                $"{tool} 的名字那颗键没进表：{Annotation.ToolNameKey(tool)}");
        foreach (var tool in Enum.GetValues<CanvasTool>())
            Assert.Equal(Annotation.ToolNameKey(tool.ToAnnotation()), tool.NameKey());
        // 画布栏上的图形说明由那颗键厂给（⛔ 在界面里另列一份"哪颗按钮说哪句"）
        var hint = SourceGate.MethodBody(CodeOf(Code), "private static string ShapeHintKey(CanvasTool tool)");
        Assert.True(SourceGate.Count(hint, "\"canvas.hint.") >= 6, $"图形说明只数到 {SourceGate.Count(hint, "\"canvas.hint.")} 颗键");
        Assert.DoesNotContain("Locale.", hint);
    }

    /// <summary>
    /// 本批交表的<b>每一颗键都得有人问</b>（通用闸门 <c>TheTableHasNoRowNothingAsksFor</c> 已钉过全表，
    /// 这里再按本批名单点一次名：⛔ 只靠那一条计数判据——它抓不到"问错了键"）。
    /// </summary>
    [Fact]
    public void EveryRowThisBatchShippedIsActuallyAskedFor()
    {
        var asked = XamlMarkup() + CodeOf(Code) + CodeOf(Stage) + CodeOf(Hub);
        var keys = Regex.Matches(asked, @"""(canvas\.[a-z][\w.]*|anno\.[\w.]+|pin\.[\w.]+)""").Cast<Match>()
            .Select(m => m.Groups[1].Value).Distinct().ToArray();
        var known = new HashSet<string>(UiStrings.Keys(), StringComparer.Ordinal);
        Assert.True(keys.Length >= 30, $"这一族只数到 {keys.Length} 颗键（本批落地后现场是 40＋）");
        foreach (var key in keys)
            Assert.True(known.Contains(key), $"代码在问一颗表里没有的键：{key}");
        foreach (var prefix in new[] { "canvas.btn.", "canvas.bar.tip.", "canvas.status.", "canvas.hint.", "canvas.notice." })
            Assert.Contains(keys, k => k.StartsWith(prefix, StringComparison.Ordinal));
    }
}
