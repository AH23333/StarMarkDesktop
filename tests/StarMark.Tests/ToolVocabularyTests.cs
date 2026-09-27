#nullable enable
using System;
using System.Linq;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 S2-a：<b>工具词汇只有一张表</b>（方案 §3.4"工具/色表/橡皮/聚光语义一处重定义"的第一步）。
/// <para>
/// 画布那八颗从前自己列一份分组、自己写一份中文名，于是同一条链的两根栏上
/// 一个叫"荧光笔"一个叫"荧光"、一个叫"橡皮"一个叫"橡皮擦"；而加一种工具要记得改四处——
/// 与批次 WM 那条"按钮文字不许写死在 XAML"是同一种病，只是搬进了模型。
/// 现在名字与分组都从 <see cref="AnnotationTools"/>／<see cref="Annotation.ToolName"/> 要答案。
/// </para>
/// </summary>
public sealed class ToolVocabularyTests
{
    /// <summary>来回映射必须无损：画布每颗都指到同名那颗，再回来还是它自己。</summary>
    [Theory]
    [InlineData(CanvasTool.Pen)]
    [InlineData(CanvasTool.Highlighter)]
    [InlineData(CanvasTool.Eraser)]
    [InlineData(CanvasTool.Rectangle)]
    [InlineData(CanvasTool.Ellipse)]
    [InlineData(CanvasTool.Line)]
    [InlineData(CanvasTool.PolyLine)]
    [InlineData(CanvasTool.Arrow)]
    public void EachCanvasToolMapsToTheSameNamedAnnotationTool(CanvasTool tool)
        => Assert.Equal(Enum.Parse<AnnotationTool>(tool.ToString()), tool.ToAnnotation());

    /// <summary>
    /// 分组是<b>从总表投影</b>出来的，所以顺序跟着总表走——这条钉的是"投影没有把条上的顺序打乱"：
    /// 按钮换位置是用户用肌肉记忆找的东西，比换个名字更疼。
    /// </summary>
    [Fact]
    public void TheCanvasGroupsAreTheSharedTablesFilteredToWhatTheBoardSupports()
    {
        Assert.Equal(
            new[] { CanvasTool.Rectangle, CanvasTool.Ellipse, CanvasTool.Line, CanvasTool.PolyLine, CanvasTool.Arrow },
            CanvasTools.Shapes);
        Assert.Equal(
            new[] { CanvasTool.Pen, CanvasTool.Highlighter, CanvasTool.Eraser },
            CanvasTools.Brushes);
        // 图形那一组的"哪些算图形"也问同一张表（两处各判一次就会出现"条上收进选择栏、点下去走的却是笔"）
        Assert.All(CanvasTools.Shapes, shape => Assert.True(AnnotationTools.IsShapeTool(shape.ToAnnotation())));
        Assert.All(CanvasTools.Brushes, brush => Assert.False(brush.IsShape()));
    }

    /// <summary>
    /// 名字只有一处出处，而且两边叫法一致。<b>其中两颗是这次收敛掉的措辞分岔</b>
    /// （截图侧从前叫"荧光／橡皮擦"）——留档在这里，真机验收时若要求保留旧措辞，改的应当是这张表，不是改回两份。
    /// </summary>
    [Fact]
    public void OneToolNamesOneThingAcrossBothBars()
    {
        Assert.Equal("荧光笔", CanvasTool.Highlighter.Name());
        Assert.Equal("橡皮", CanvasTool.Eraser.Name());
        foreach (var tool in Enum.GetValues<CanvasTool>())
            Assert.Equal(Annotation.ToolName(tool.ToAnnotation()), tool.Name());
    }

    /// <summary>
    /// 粗细档位两边<b>有意不同</b>（画布要站着讲课、几米外看得见），所以这里钉的是"这份不同是有意的"，
    /// 而不是把它悄悄统一：改任何一张表都会让用户在两条栏上挑到不一样粗的同一颗笔。
    /// </summary>
    [Fact]
    public void TheWidthTablesAreDeliberatelyDifferent()
    {
        Assert.Equal(new[] { 4, 9, 18 }, CanvasWidths.Steps);
        Assert.Equal(new[] { 2, 4, 8 }, Annotation.ThicknessSteps);
        Assert.Equal(CanvasWidths.StepLabels, Annotation.ThicknessNames.ToArray());   // 标签是同一套：细/中/粗
        // 两边的默认档都是中间那一档——画布那一份存的是索引，截图那一份存的是"取第 1 档"，
        // 两边都不许哪天悄悄把默认挪到极细或极粗（换档要看得见变，默认不许跟着变）
        Assert.Equal(CanvasWidths.Steps[1], CanvasWidths.At(CanvasWidths.DefaultStepIndex));
        Assert.Equal(Annotation.ThicknessSteps[1], Annotation.DefaultThickness(AnnotationTool.Pen));
    }

    /// <summary>
    /// 反回潮闸门：<b>画布侧不许再写一份名字表</b>。
    /// <para>这次收的正是那份"第二出处"，所以这里钉的是"它不能再长回来"：分组只准从总表投影，
    /// 名字只准问 <see cref="Annotation.ToolName"/>，类体里不许再出现任何带引号的工具名
    /// （注释里也别写带引号的那一份——写进去就会把这条禁项顶成假失败，见坑表）。</para>
    /// </summary>
    [Fact]
    public void TheCanvasSideNeverCarriesItsOwnNameTable()
    {
        var table = SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.Core/Canvas/CanvasInk.cs"), "public static class CanvasTools");
        Assert.Contains("Capture.Annotation.ToolName(tool.ToAnnotation())", table);
        Assert.Contains("Capture.AnnotationTools.Shapes.Select(FromAnnotation)", table);
        Assert.Contains("Capture.AnnotationTools.Brushes.Where(Supports)", table);
        Assert.Contains("Capture.AnnotationTools.IsShapeTool(tool.ToAnnotation())", table);
        foreach (var label in new[] { "画笔", "荧光笔", "荧光", "橡皮", "橡皮擦", "矩形", "椭圆", "直线", "折线", "箭头" })
            Assert.DoesNotContain($"\"{label}\"", table);
    }
}
