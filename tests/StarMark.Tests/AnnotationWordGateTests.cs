#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Abstractions.I18n;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-49（P-181）的闸门：<b>画笔那套"念给人听的词"只许有表里那一份出处，而且每次上屏现算</b>。
/// <para>
/// 起因是给 P-179 数屏幕字时把尺伸到 Core：那一族从前整族写死中文，而<b>两把旧尺都扫不到它</b>——
/// 供话者名单按"这个文件有没有卡片／菜单出口"筛（Core 没有），整档汉字尺又只对 UI 那几档跑过（坑表 #375）。
/// 它上屏在两处：截图那条工具条的 tooltip 与画布那条栏的状态行——同一个词厂，所以一刀交完。
/// </para>
/// <para>⚠ 三颗"每次现算"的形状（<c>ThicknessNames</c>／<c>Palette</c> 那颗 <c>Name</c>／<c>StepLabels</c>）
/// 是这一族的真缺陷面：它们从前是<b>建表那一刻存下的成品字</b>，换语言之后两条栏都念旧语言，
/// 而通用闸门看不见（被问的键没变，变的是实参）⇒ 按 #373 的口径，一条语言读数＋一条形状禁项成对。</para>
/// <para>✅批次 WI-65 片3（P-194 那一族的第三档）：词厂交<b>「键＋实参」</b>，那一拍跟着搬到宿主——
/// 条上那排 tooltip 与那颗错误角标都改由 <c>LocText</c> 贴（贴图态那扇条子窗会一直开着，
/// <c>ToolTipService.SetToolTip(念完的那句)</c> 等于把语言钉在建条那一刻）。
/// 唯一的例外按现场钉死：<c>AnnotationPainter.Paint</c> 用同一句判据填 <c>InvalidOperationException</c>，
/// 那一处<b>刻意保持念完</b>——异常消息的出口是日志与崩溃转储，没有屏幕宿主（那颗归 <b>P-184</b>）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class AnnotationWordGateTests
{
    private const string Ann = "src/StarMark.Core/Capture/Annotation.cs";
    private const string Ink = "src/StarMark.Core/Canvas/CanvasInk.cs";
    private const string Overlay = "src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs";
    private const string Bar = "src/StarMark.UI/Views/CaptureOverlayWindow.ToolBar.cs";
    private const string Pick = "src/StarMark.UI/Views/CaptureOverlayWindow.Pickers.cs";
    private const string Draw = "src/StarMark.UI/Views/CaptureOverlayWindow.Draw.cs";
    private const string Painter = "src/StarMark.Core/Capture/AnnotationPainter.cs";
    private const string Geo = "src/StarMark.Core/Capture/CaptureGeometry.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";

    /// <summary>本批新造的两颗键（一颗连接符、一颗「名字：说明」的胶水句）；逐颗点名＝第⑩格。</summary>
    private const string NameJoin = "capture.bar.nameJoin";

    private const string ShapeTip = "capture.pick.shapeTip";

    /// <summary>
    /// ① 语言读数（真跑，不是源码扫描）：切到英文再问那一族，每一颗都该念英文；
    /// <b>连"两栏共用一颗名字"这件事也在这里读</b>（画布侧的 <c>CanvasTool.Name()</c> 与 <c>CanvasWidths.StepLabels</c>）。
    /// <para>
    /// ⚠ 这一格钉的是<b>「哪一颗键喂哪一处屏幕」＋「取词发生在问的那一刻」</b>，⛔ 钉表里的措辞：
    /// 期望那一侧现取同一颗键，所以改一句说法（表里自己那一行）不会假失败，而<b>退回面字、喂错键、
    /// 存成上一次取词的快照</b>三种形状都还会红（台架的 C2 臂专门拿"只改措辞"钉这一条，坑表 #378）。
    /// </para>
    /// <para>✅片3：出口换型成 <c>LocMessage</c> ⇒ 两边都<b>念出来再比</b>，⛔ 把两颗 <c>LocMessage</c> 交给
    /// <c>Assert.Equal</c> 自己（record 合成的相等对 <c>Args</c> 数组只比引用＝坑表 #480）。</para>
    /// </summary>
    [Fact]
    public void EveryWordTheTwoBarsSayIsReadFromTheTableInEachLanguage()
    {
        var before = Locale.Current;
        try
        {
            Locale.TrySet(AppLanguage.English);
            Assert.Equal(AppLanguage.English, Locale.Current);            // ⛔ 拿 TrySet 的返回值当"换没换到"（#112）
            Assert.Equal(Locale.T("anno.tool.rectangle"), Annotation.ToolName(AnnotationTool.Rectangle).Render());
            Assert.Equal(Locale.T("anno.tool.eraser"), Annotation.ToolName(AnnotationTool.Eraser).Render());
            Assert.Equal(Locale.T("anno.hint.rectangle"), Annotation.ToolHint(AnnotationTool.Rectangle)!.Render());
            Assert.Equal(Locale.T("anno.unit.radius"), Annotation.ThicknessUnit(AnnotationTool.Number).Render());
            Assert.Equal(ThinMediumThick(), Rendered(Annotation.ThicknessNames));
            // 色板的身份是键：把那一刻取到的成品字存进 NameKey，这一格当场露形（台架 A4）
            Assert.Equal(new[]
            {
                "anno.color.red", "anno.color.yellow", "anno.color.green", "anno.color.blue",
                "anno.color.white", "anno.color.black",
            }, Annotation.Palette.Select(c => c.NameKey).ToArray());
            Locale.TrySet(AppLanguage.Japanese);
            Assert.Equal(AppLanguage.Japanese, Locale.Current);
            // 期望那一侧跟着语言走、实现那一侧若不现取就停在上一门语言上——换语言这一格才是它的用处
            Assert.Equal(ThinMediumThick(), Rendered(Annotation.ThicknessNames));
            Assert.Equal(Locale.T("anno.color.black"), Annotation.Palette[5].Name.Render());
            Assert.Equal(Locale.T("anno.tool.rectangle"), Annotation.ToolName(AnnotationTool.Rectangle).Render());
            Locale.TrySet(AppLanguage.English);
            // 两栏共用的那一颗：画布侧问的是同一张表
            Assert.Equal(Locale.T("anno.tool.highlighter"), CanvasTool.Highlighter.Name().Render());
            Assert.Equal(Rendered(Annotation.ThicknessNames), Rendered(CanvasWidths.StepLabels));
            // 拒绝句里那个工具名也是现算的（从前「橡皮擦」自己写了一份，与表里的「橡皮」两个名字）
            var eraser = new Annotation(AnnotationTool.Eraser, new[] { new PixelPoint(0, 0) },
                Annotation.Opaque(0, 0, 0), 4);
            Assert.Equal(Locale.Fmt("anno.problem.notDrawable", Annotation.ToolName(AnnotationTool.Eraser)),
                eraser.Problem()!.Render());
            var few = new Annotation(AnnotationTool.Pen, new[] { new PixelPoint(1, 1) },
                Annotation.Opaque(0, 0, 255), 4);
            Assert.Equal(Locale.Fmt("anno.problem.tooFewPoints",
                Annotation.ToolName(AnnotationTool.Pen), Annotation.MinPoints(AnnotationTool.Pen)), few.Problem()!.Render());
        }
        finally
        {
            Locale.TrySet(before);
        }
        Assert.Equal(before, Locale.Current);
    }

    /// <summary>
    /// ② 套娃那一格（✅片3）：外层那句重贴时，<b>夹在里面的那颗工具名必须跟着翻</b>。
    /// <para>这一格防的是"词厂交了 <c>LocMessage</c>，而宿主提前念一次"——当时屏幕上看不出差别，
    /// 直到有人换语言：外层换了、里面还停在上一门，半中半英（✅WI-65 片1／片2 同一形状）。</para>
    /// </summary>
    [Fact]
    public void TheToolNameNestedInTheBarSentenceTurnsWithTheOuterSentence()
    {
        var before = Locale.Current;
        try
        {
            Locale.TrySet(AppLanguage.English);
            var message = LocMessage.Of("capture.bar.brushTip",
                Annotation.ToolName(AnnotationTool.Pen), Annotation.ToolHint(AnnotationTool.Pen));
            var englishName = Locale.T("anno.tool.pen");
            var english = message.Render();
            Assert.Contains(englishName, english, StringComparison.Ordinal);

            Locale.TrySet(AppLanguage.Japanese);
            var japanese = message.Render();
            Assert.Contains(Locale.T("anno.tool.pen"), japanese, StringComparison.Ordinal);
            Assert.DoesNotContain(englishName, japanese, StringComparison.Ordinal);       // 里面那颗没停在英文上
            Assert.NotEqual(english, japanese);                                           // 比念出来的句子，⛔ 比对象（#480）
        }
        finally
        {
            Locale.TrySet(before);
        }
    }

    /// <summary>
    /// ③ 那一排名字的连接符也交表（✅片3）：那串「Rectangle / Ellipse」从前用
    /// <c>string.Join(" / ", 念完的那几颗)</c> 拼在 C# 里 ⇒ 每颗名字被钉在算出它的那一刻。
    /// 改交 <c>LocJoin</c> 之后<b>被贴上去那一刻才取词</b>（先例＝<c>ocr.how.join</c>／<c>rss.statusJoin</c>＝规则 #100）。
    /// <para>期望那一侧⛔ 抄表里的字面：两颗名字与那颗连接符都现取同一批键，改措辞不会假失败（#378 同条）。</para>
    /// </summary>
    [Fact]
    public void TheJoinedNameListRendersItsOwnWordsAtPasteTime()
    {
        var before = Locale.Current;
        try
        {
            var list = new LocJoin(new object?[]
            {
                Annotation.ToolName(AnnotationTool.Rectangle), Annotation.ToolName(AnnotationTool.Ellipse),
            }, NameJoin);

            Locale.TrySet(AppLanguage.English);
            Assert.Equal(Locale.T("anno.tool.rectangle") + Locale.T(NameJoin) + Locale.T("anno.tool.ellipse"),
                list.ToString());
            var pinned = list.ToString();

            Locale.TrySet(AppLanguage.Japanese);
            Assert.Equal(Locale.T("anno.tool.rectangle") + Locale.T(NameJoin) + Locale.T("anno.tool.ellipse"),
                list.ToString());
            Assert.DoesNotContain(pinned, list.ToString(), StringComparison.Ordinal);     // 整句都翻了，没留半中半英
        }
        finally
        {
            Locale.TrySet(before);
        }
    }

    /// <summary>
    /// ④ 认不得的工具回的是<b>「没话」</b>而不是"一句空话"（✅片3）：从前 <c>ToolHint</c> 的兜底臂交空字符串，
    /// 宿主把它当一句话拼进「名字：」后面，屏幕上就剩一颗孤零零的冒号。换成 <c>null</c> 之后
    /// "这一处没话说"在类型上就说得出（与那颗 <c>Problem()</c> 的 null 同一口径）。
    /// </summary>
    [Fact]
    public void AnUnknownToolHasNothingToSayRatherThanAnEmptySentence()
    {
        Assert.Null(Annotation.ToolHint((AnnotationTool)(-1)));
        Assert.Equal("anno.hint.pen", Annotation.ToolHint(AnnotationTool.Pen)!.Key);
        var ann = SourceGate.Code(SourceGate.ReadRepoFile(Ann));
        Assert.Equal(0, SourceGate.Count(ann, "LocMessage.Of(\"common.rawValue\""));      // 空话⛔ 走数据门冒充有话说
        Assert.Equal(0, SourceGate.Count(ann, "LocMessage.Of(string.Empty"));
    }

    /// <summary>
    /// ⑤ 条上那排 tooltip 与那颗错误角标都由宿主 <c>LocText</c> 贴（✅片3·机制 (C) 的宿主那一拍）。
    /// <para>钉的是<b>旧写法一颗都不许剩</b>（<c>ToolTipService.SetToolTip(</c> 与 <c>ErrorText.Text = </c> 各钉 0）；
    /// 新写法的颗数只钉"至少四颗"——那个数会随邻族改动漂，钉死它等于给下一批留一颗假证人（#484 同条）。</para>
    /// </summary>
    [Fact]
    public void TheBarAndTheErrorChipArePastedByTheHostRatherThanAssigned()
    {
        var bar = SourceGate.Code(SourceGate.ReadRepoFile(Bar));
        var pick = SourceGate.Code(SourceGate.ReadRepoFile(Pick));
        var draw = SourceGate.Code(SourceGate.ReadRepoFile(Draw));

        Assert.Equal(0, SourceGate.Count(bar, "ToolTipService.SetToolTip("));
        Assert.Contains("LocText.SetMessage(button, ToolTipService.ToolTipProperty, tip);", bar);
        Assert.True(SourceGate.Count(bar, "LocText.SetMessage(") >= 4,
            $"条上那几处重贴应当都在（现读 {SourceGate.Count(bar, "LocText.SetMessage(")} 处）");
        Assert.Equal(0, SourceGate.Count(pick, "$\"{Annotation."));                        // 「名字：说明」⛔ 再在内插串里拼

        Assert.Contains("private void ShowError(LocMessage reason)", draw);
        Assert.Contains("LocText.SetMessage(ErrorText, reason);", draw);
        Assert.Equal(0, SourceGate.Count(draw, "ErrorText.Text = "));
    }

    /// <summary>
    /// ⑥ 唯一保持念完的那一处按现场钉死（✅片3）：<c>Paint</c> 用同一句判据填 <c>InvalidOperationException</c>。
    /// <para>那句话的出口是日志与崩溃转储，<b>没有屏幕宿主</b> ⇒ 当场念完才是对的（那颗归 <b>P-184</b>：
    /// ⛔ 为了"进表"而进表）。这一格同时是"把所有 <c>.Render()</c> 一律当泄漏"那种一刀切的反面证人——
    /// 同一颗判据在两条出口上本来就该有两种交法（✅P-191 取 (A) 同一条理由）。</para>
    /// </summary>
    [Fact]
    public void TheOnePlaceThatKeepsRenderingTheSentenceIsTheExceptionMessage()
    {
        var painter = SourceGate.Code(SourceGate.ReadRepoFile(Painter));
        Assert.Contains("throw new InvalidOperationException(problem.Render());", painter);
        Assert.Equal(0, SourceGate.Count(painter, "Locale.T("));
        Assert.Equal(0, SourceGate.Count(painter, "Locale.Fmt("));
    }

    /// <summary>
    /// ⑦ 形状禁项：Core 那三档里⛔ 再留任何一颗词的面字。
    /// <para>⚠ 每条都<b>带引号点名</b>（#368）：裸词的写法会撞上日志措辞与注释里那些解释，
    /// 假失败最坏——它教人把闸门改松而不是把词交出去。禁项一律读 <c>Code()</c>（连注释读会逼后人删注释换绿灯）。</para>
    /// <para>⚠ 这一颗⛔ 是装饰：台架那一格把 <c>Palette</c> 的名字<b>又存回字段</b>时，取词计数一条都不变，
    /// 只有这里咬得住（#373 同条）。</para>
    /// </summary>
    [Fact]
    public void NoWordOfThatFamilyIsWrittenInCodeAgain()
    {
        var ann = SourceGate.Code(SourceGate.ReadRepoFile(Ann));
        var ink = SourceGate.Code(SourceGate.ReadRepoFile(Ink));
        var ovl = SourceGate.Code(SourceGate.ReadRepoFile(Overlay));
        var geo = SourceGate.Code(SourceGate.ReadRepoFile(Geo));
        foreach (var dead in new[]
        {
            "\"矩形\"", "\"椭圆\"", "\"折线\"", "\"荧光笔\"", "\"打码\"", "\"序号\"", "\"橡皮\"",
            "\"拖一个方框圈出来\"", "\"按住划出任意线条\"",
            "\"圆点半径\"", "\"笔刷直径\"", "\"格子边长\"",
            "\"细\"", "\"中\"", "\"粗\"",
            "\"红\"", "\"黄\"", "\"绿\"", "\"蓝\"", "\"白\"", "\"黑\"",
            "\"橡皮擦不是可绘制的标注\"", "\"颜色是全透明的，画上去等于没画\"",
            "\"选区落在所有显示器之外，没有可截取的内容\"", "\"本机没有可用的显示器画面\"",
        })
        {
            Assert.DoesNotContain(dead, ann);
            Assert.DoesNotContain(dead, ink);
            Assert.DoesNotContain(dead, geo);
        }
        Assert.DoesNotContain("\"未选工具\"", ovl);
        // 三档名字与颜色名各只许一个主人：投影那两份不许再列字面量
        Assert.DoesNotContain("StepLabels = {", ink);
        // ⚠ 只禁"旧形状"不够：画布那一侧若改成<b>自己再取一次词</b>（活的、字面与 Core 全等），
        // 那条"两栏念同一颗"的语言读数会绿——⇒ 禁项要钉住"这里不许出现这一族的键"这一形状本身。
        Assert.Equal(0, SourceGate.Count(ink, "anno.thickness."));
        Assert.Equal(1, SourceGate.Count(ann, "LocMessage.Of(\"anno.thickness.thin\")"));
        Assert.Equal(6, SourceGate.Count(ann, "new AnnotationColor(\"anno.color."));
        // ⚠ 那颗"每次现算"不许退回**建表那一刻存下的快照**（#373：取词计数抓不到这种形状，只有这一条咬得住）
        Assert.DoesNotContain("ThicknessNames { get; }", ann);
        Assert.DoesNotContain("IReadOnlyList<string> ThicknessNames", ann);
    }

    /// <summary>
    /// ⑧ 那颗颜色的<b>身份</b>是键、<b>念法</b>每次现算（WI-46c 那颗组名同一形状），✅片3 起交的是「键＋实参」。
    /// <para>⛔ 把成品字存进 <c>AnnotationColor</c>：那条色板是启动时一次性建的，存成品字就等于把语言焊死在第一次取词上。</para>
    /// </summary>
    [Fact]
    public void TheColourNameIsReadEveryTimeItIsShown()
    {
        Assert.Contains("public LocMessage Name => LocMessage.Of(NameKey);",
            SourceGate.Code(SourceGate.ReadRepoFile(Ann)));
        Assert.DoesNotContain("record struct AnnotationColor(string Name,",
            SourceGate.Code(SourceGate.ReadRepoFile(Ann)));
    }

    /// <summary>
    /// ⑨ 词厂整档一颗当场取词都不留（P-194 数这一族的尺）：28 处 <c>LocMessage.Of(</c> 对上落档时称的数，
    /// <c>Locale.T(</c>／<c>Locale.Fmt(</c> 各钉 <b>0</b>。
    /// <para><c>CaptureGeometry</c> 这一档⛔ 整档归零：选区／裁剪那四处交键，<b>还剩贴图那两颗</b>
    /// （<c>pin.card.limitBody</c>／<c>pin.card.noneBody</c>）归片3 第二刀 ⇒ 剩余量钉成数、⛔ 写成完成态（#178／#469 同条）。</para>
    /// </summary>
    [Fact]
    public void TheJudgeHandsOutKeysAndNeverReadsThemItself()
    {
        var ann = SourceGate.Code(SourceGate.ReadRepoFile(Ann));
        Assert.Equal(0, SourceGate.Count(ann, "Locale.T("));
        Assert.Equal(0, SourceGate.Count(ann, "Locale.Fmt("));
        Assert.Equal(28, SourceGate.Count(ann, "LocMessage.Of("));     // 1 色名 ＋ 3 档名 ＋ 5 单位 ＋ 7 判据 ＋ 1 工具名 ＋ 11 悬停说明

        var geo = SourceGate.Code(SourceGate.ReadRepoFile(Geo));
        Assert.Equal(0, SourceGate.Count(geo, "Locale.T(\"capture."));
        Assert.Equal(0, SourceGate.Count(geo, "Locale.Fmt(\"capture."));
        Assert.Equal(4, SourceGate.Count(geo, "LocMessage.Of(\"capture."));
        Assert.Equal(2, SourceGate.Count(geo, "Locale.T(") + SourceGate.Count(geo, "Locale.Fmt("));   // 只剩贴图那两颗
        Assert.Contains("public static LocMessage? SelectionProblem(", geo);
        Assert.Contains("public static LocMessage? CropProblem(", geo);

        var ink = SourceGate.Code(SourceGate.ReadRepoFile(Ink));
        Assert.Equal(0, SourceGate.Count(ink, "Locale.T("));
        Assert.Contains("public static LocMessage Name(this CanvasTool tool)", ink);
        Assert.Contains("public static IReadOnlyList<LocMessage> StepLabels", ink);
    }

    /// <summary>
    /// ⑩ 本批新造的两颗键逐颗点名（⛔ 只报一个总数）：各自在表里<b>恰一行</b>、宿主里各念到位，
    /// 而那条 <c>string.Join(" / ", Annotation.…)</c> 的旧拼法一颗都不许剩。
    /// </summary>
    [Fact]
    public void TheTwoKeysThisBatchInventedAreDeclaredOnceAndNamed()
    {
        var table = SourceGate.Code(SourceGate.ReadRepoFile(Table));
        foreach (var key in new[] { NameJoin, ShapeTip })
            Assert.Equal(1, SourceGate.Count(table, $"new Entry(\"{key}\","));

        Assert.Contains("LocMessage.Of(\"capture.pick.shapeTip\"", SourceGate.Code(SourceGate.ReadRepoFile(Pick)));
        var bar = SourceGate.Code(SourceGate.ReadRepoFile(Bar));
        Assert.Equal(2, SourceGate.Count(bar, $"\"{NameJoin}\""));                         // 图形名那一排 ＋ 三档名那一排
        Assert.Equal(0, SourceGate.Count(bar, "string.Join(\" / \", Annotation."));
    }

    /// <summary>三档名字按<b>当前语言</b>现取——读数自己的期望那一侧，⛔ 抄一份成品字进测试。</summary>
    private static string[] ThinMediumThick()
        => new[]
        {
            Locale.T("anno.thickness.thin"), Locale.T("anno.thickness.medium"), Locale.T("anno.thickness.thick"),
        };

    /// <summary>比句子⛔ 比对象（坑表 #480：record 合成的相等对 <c>Args</c> 数组只比引用）。</summary>
    private static string[] Rendered(IReadOnlyList<LocMessage> messages)
        => messages.Select(static m => m.Render()).ToArray();
}
