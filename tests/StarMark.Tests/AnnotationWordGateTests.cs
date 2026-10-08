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
/// </summary>
public sealed class AnnotationWordGateTests
{
    private const string Ann = "src/StarMark.Core/Capture/Annotation.cs";
    private const string Ink = "src/StarMark.Core/Canvas/CanvasInk.cs";
    private const string Overlay = "src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs";

    /// <summary>
    /// 语言读数（真跑，不是源码扫描）：切到英文再问那一族，每一颗都该念英文；
    /// <b>连"两栏共用一颗名字"这件事也在这里读</b>（画布侧的 <c>CanvasTool.Name()</c> 与 <c>CanvasWidths.StepLabels</c>）。
    /// <para>
    /// ⚠ 这一格钉的是<b>「哪一颗键喂哪一处屏幕」＋「取词发生在问的那一刻」</b>，⛔ 钉表里的措辞：
    /// 期望那一侧现取同一颗键，所以改一句说法（表里自己那一行）不会假失败，而<b>退回面字、喂错键、
    /// 存成上一次取词的快照</b>三种形状都还会红（台架的 C2 臂专门拿"只改措辞"钉这一条，坑表 #378）。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryWordTheTwoBarsSayIsReadFromTheTableInEachLanguage()
    {
        var before = Locale.Current;
        try
        {
            Locale.TrySet(AppLanguage.English);
            Assert.Equal(AppLanguage.English, Locale.Current);            // ⛔ 拿 TrySet 的返回值当"换没换到"（#112）
            Assert.Equal(Locale.T("anno.tool.rectangle"), Annotation.ToolName(AnnotationTool.Rectangle));
            Assert.Equal(Locale.T("anno.tool.eraser"), Annotation.ToolName(AnnotationTool.Eraser));
            Assert.Equal(Locale.T("anno.hint.rectangle"), Annotation.ToolHint(AnnotationTool.Rectangle));
            Assert.Equal(Locale.T("anno.unit.radius"), Annotation.ThicknessUnit(AnnotationTool.Number));
            Assert.Equal(ThinMediumThick(), Annotation.ThicknessNames.ToArray());
            // 色板的身份是键：把那一刻取到的成品字存进 NameKey，这一格当场露形（台架 A4）
            Assert.Equal(new[]
            {
                "anno.color.red", "anno.color.yellow", "anno.color.green", "anno.color.blue",
                "anno.color.white", "anno.color.black",
            }, Annotation.Palette.Select(c => c.NameKey).ToArray());
            Locale.TrySet(AppLanguage.Japanese);
            Assert.Equal(AppLanguage.Japanese, Locale.Current);
            // 期望那一侧跟着语言走、实现那一侧若不现取就停在上一门语言上——换语言这一格才是它的用处
            Assert.Equal(ThinMediumThick(), Annotation.ThicknessNames.ToArray());
            Assert.Equal(Locale.T("anno.color.black"), Annotation.Palette[5].Name);
            Assert.Equal(Locale.T("anno.tool.rectangle"), Annotation.ToolName(AnnotationTool.Rectangle));
            Locale.TrySet(AppLanguage.English);
            // 两栏共用的那一颗：画布侧问的是同一张表
            Assert.Equal(Locale.T("anno.tool.highlighter"), CanvasTool.Highlighter.Name());
            Assert.Equal(Annotation.ThicknessNames.ToArray(), CanvasWidths.StepLabels.ToArray());
            // 拒绝句里那个工具名也是现算的（从前「橡皮擦」自己写了一份，与表里的「橡皮」两个名字）
            var eraser = new Annotation(AnnotationTool.Eraser, new[] { new PixelPoint(0, 0) },
                Annotation.Opaque(0, 0, 0), 4);
            Assert.Equal(Locale.Fmt("anno.problem.notDrawable", Annotation.ToolName(AnnotationTool.Eraser)),
                eraser.Problem());
            var few = new Annotation(AnnotationTool.Pen, new[] { new PixelPoint(1, 1) },
                Annotation.Opaque(0, 0, 255), 4);
            Assert.Equal(Locale.Fmt("anno.problem.tooFewPoints",
                Annotation.ToolName(AnnotationTool.Pen), Annotation.MinPoints(AnnotationTool.Pen)), few.Problem());
        }
        finally
        {
            Locale.TrySet(before);
        }
        Assert.Equal(before, Locale.Current);
    }

    /// <summary>三档名字按<b>当前语言</b>现取——读数自己的期望那一侧，⛔ 抄一份成品字进测试。</summary>
    private static string[] ThinMediumThick()
        => new[]
        {
            Locale.T("anno.thickness.thin"), Locale.T("anno.thickness.medium"), Locale.T("anno.thickness.thick"),
        };

    /// <summary>
    /// 形状禁项：Core 与那两档宿主里⛔ 再留任何一颗词的面字。
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
        foreach (var dead in new[]
        {
            "\"矩形\"", "\"椭圆\"", "\"折线\"", "\"荧光笔\"", "\"打码\"", "\"序号\"", "\"橡皮\"",
            "\"拖一个方框圈出来\"", "\"按住划出任意线条\"",
            "\"圆点半径\"", "\"笔刷直径\"", "\"格子边长\"",
            "\"细\"", "\"中\"", "\"粗\"",
            "\"红\"", "\"黄\"", "\"绿\"", "\"蓝\"", "\"白\"", "\"黑\"",
            "\"橡皮擦不是可绘制的标注\"", "\"颜色是全透明的，画上去等于没画\"",
        })
        {
            Assert.DoesNotContain(dead, ann);
            Assert.DoesNotContain(dead, ink);
        }
        Assert.DoesNotContain("\"未选工具\"", ovl);
        // 三档名字与颜色名各只许一个主人：投影那两份不许再列字面量
        Assert.DoesNotContain("StepLabels = {", ink);
        // ⚠ 只禁"旧形状"不够：画布那一侧若改成<b>自己再取一次词</b>（活的、字面与 Core 全等），
        // 那条"两栏念同一颗"的语言读数会绿——⇒ 禁项要钉住"这里不许出现这一族的键"这一形状本身。
        Assert.Equal(0, SourceGate.Count(ink, "anno.thickness."));
        Assert.Equal(1, SourceGate.Count(ann, "new[] { Locale.T(\"anno.thickness.thin\")"));
        Assert.Equal(6, SourceGate.Count(ann, "new AnnotationColor(\"anno.color."));
        // ⚠ 那颗"每次现算"不许退回**建表那一刻存下的快照**（#373：取词计数抓不到这种形状，只有这一条咬得住）
        Assert.DoesNotContain("ThicknessNames { get; }", ann);
    }

    /// <summary>
    /// 那颗颜色的<b>身份</b>是键、<b>念法</b>每次现算（WI-46c 那颗组名同一形状）。
    /// <para>⛔ 把成品字存进 <c>AnnotationColor</c>：那条色板是启动时一次性建的，存成品字就等于把语言焊死在第一次取词上。</para>
    /// </summary>
    [Fact]
    public void TheColourNameIsReadEveryTimeItIsShown()
    {
        Assert.Contains("public string Name => Locale.T(NameKey);", SourceGate.Code(SourceGate.ReadRepoFile(Ann)));
        Assert.DoesNotContain("record struct AnnotationColor(string Name,", SourceGate.Code(SourceGate.ReadRepoFile(Ann)));
    }
}
