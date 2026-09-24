#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 截图标注的模型判据。
/// <para>
/// 这一层管的是"这条标注到底画不画得出去、该占多大地方"。工具条上"点下去什么都没发生"
/// 与"存出来的图比预览少了一块"这两类反馈，根都会落在这里的某一条判据上。
/// </para>
/// </summary>
public sealed class AnnotationTests
{
    private static Annotation Make(AnnotationTool tool, int thickness = 4, string? text = null, int fontHeight = 22)
        => new(tool, new[] { new PixelPoint(10, 10), new PixelPoint(50, 30) }, Annotation.Opaque(0, 0, 255), thickness)
        { Text = text, FontHeight = fontHeight };

    // ────────── 颜色 ──────────

    [Fact]
    public void PaletteCoversBothDarkAndLightBackgrounds()
    {
        // 只有彩色的一组：在深色截图上画一条蓝线等于没画。黑白两色是"任何截图都看得见"的下限。
        var names = Annotation.Palette.Select(color => color.Name).ToList();
        Assert.Contains("黑", names);
        Assert.Contains("白", names);
        Assert.Equal(6, names.Count);
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (var color in Annotation.Palette)
        {
            Assert.Equal(255, color.Bgra >>> 24);                   // 出厂颜色必须不透明
            Assert.False(string.IsNullOrWhiteSpace(color.Name));
        }
    }

    [Fact]
    public void OpaquePutsChannelsInBgraOrder()
    {
        // 位序写反是"红蓝互换"这种要靠眼睛才发现的错，所以逐字节钉住
        var bgra = Annotation.Opaque(0x12, 0x34, 0x56);
        Assert.Equal(0x12, bgra & 0xFF);
        Assert.Equal(0x34, bgra >> 8 & 0xFF);
        Assert.Equal(0x56, bgra >> 16 & 0xFF);
        Assert.Equal(0xFF, bgra >>> 24);
    }

    [Fact]
    public void OnlyTheHighlighterForcesTransparency()
    {
        var solid = Make(AnnotationTool.Rectangle);
        Assert.Equal(solid.ColorBgra, solid.EffectiveColorBgra);
        var marker = Make(AnnotationTool.Highlighter);
        Assert.Equal(Annotation.HighlighterAlpha, marker.EffectiveColorBgra >>> 24);
        Assert.Equal(marker.ColorBgra & 0x00FFFFFF, marker.EffectiveColorBgra & 0x00FFFFFF);   // 色相不变，只减浓度
    }

    // ────────── 线宽 ──────────

    [Theory]
    [InlineData(AnnotationTool.Pen, 4)]
    [InlineData(AnnotationTool.Highlighter, 16)]
    [InlineData(AnnotationTool.Mosaic, 24)]
    public void BrushLikeToolsStartWiderThanTheLineTools(AnnotationTool tool, int expected)
        => Assert.Equal(expected, Annotation.DefaultThickness(tool));

    [Fact]
    public void BrushToolsScaleTheSameThreeWeights()
    {
        // 三档标签是"细/中/粗"，同一个标签在不同工具上给不同粗细：
        // 涂敏感信息的笔刷若只有 2–8 像素，就会出现一条条漏缝（那是安全缺陷，不是难看）
        Assert.Equal(2, Annotation.ThicknessFor(AnnotationTool.Rectangle, 0));
        Assert.Equal(8, Annotation.ThicknessFor(AnnotationTool.Rectangle, 2));
        Assert.Equal(32, Annotation.ThicknessFor(AnnotationTool.Highlighter, 2));
        Assert.Equal(48, Annotation.ThicknessFor(AnnotationTool.Mosaic, 2));
        Assert.True(Annotation.ThicknessFor(AnnotationTool.Mosaic, 2) <= Annotation.MaxThickness,
            "最粗的一档必须落在 Problem() 允许的范围内，否则选了粗档就再也画不出去");
        // 越界档位不能崩，也不能读出未定义的值
        Assert.Equal(2, Annotation.ThicknessFor(AnnotationTool.Line, -5));
        Assert.Equal(8, Annotation.ThicknessFor(AnnotationTool.Line, 99));
    }

    [Fact]
    public void WeightNamesAndStepsAreOneForOne()
    {
        Assert.Equal(Annotation.ThicknessSteps.Length, Annotation.ThicknessNames.Count);
        Assert.All(Annotation.ThicknessNames, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Fact]
    public void EveryToolExplainsWhatItWillDo()
    {
        foreach (var tool in Enum.GetValues<AnnotationTool>())
        {
            var hint = Annotation.ToolHint(tool);
            Assert.False(string.IsNullOrEmpty(hint), $"{Annotation.ToolName(tool)} 没有悬停说明");
            // 说明不能只是把按钮名重念一遍（那一行是废话）；提到这个名字本身没问题
            Assert.NotEqual(Annotation.ToolName(tool), hint);
            Assert.True(hint.Length >= Annotation.ToolName(tool).Length + 4, $"{Annotation.ToolName(tool)} 的说明太短，说不清这一下会发生什么");
        }
    }

    [Fact]
    public void EveryThicknessStepIsDrawableAndSorted()
    {
        Assert.NotEmpty(Annotation.ThicknessSteps);
        Assert.True(Annotation.ThicknessSteps.SequenceEqual(Annotation.ThicknessSteps.OrderBy(x => x)), "档位必须递增");
        foreach (var thickness in Annotation.ThicknessSteps)
            Assert.True(thickness is >= Annotation.MinThickness and <= Annotation.MaxThickness);
        Assert.All(Annotation.Palette, color => Assert.Null(
            new Annotation(AnnotationTool.Rectangle, new[] { new PixelPoint(0, 0), new PixelPoint(4, 4) }, color.Bgra, 2).Problem()));
    }

    // ────────── 能不能画 ──────────

    [Theory]
    [InlineData(AnnotationTool.Rectangle)]
    [InlineData(AnnotationTool.Ellipse)]
    [InlineData(AnnotationTool.Line)]
    [InlineData(AnnotationTool.Arrow)]
    [InlineData(AnnotationTool.Pen)]
    [InlineData(AnnotationTool.Highlighter)]
    [InlineData(AnnotationTool.Mosaic)]
    public void ShapeWithoutTwoPointsIsNotDrawn(AnnotationTool tool)
    {
        Assert.Equal(2, Annotation.MinPoints(tool));
        var onePoint = new Annotation(tool, new[] { new PixelPoint(5, 5) }, Annotation.Opaque(0, 0, 255), 4);
        var problem = onePoint.Problem();
        Assert.NotNull(problem);
        Assert.Contains(Annotation.ToolName(tool), problem);        // 原因要点名是哪个工具，否则提示没法定位
    }

    [Fact]
    public void EveryRejectionNamesTheReason()
    {
        Assert.Contains("文字是空的", new Annotation(AnnotationTool.Text, new[] { new PixelPoint(1, 1) },
            Annotation.Opaque(255, 255, 255), 4).Problem());
        Assert.Contains("粗细 0", Make(AnnotationTool.Rectangle, thickness: 0).Problem());
        Assert.Contains("粗细 49", Make(AnnotationTool.Rectangle, thickness: 49).Problem());
        Assert.Contains("文字高度 5", Make(AnnotationTool.Text, text: "字", fontHeight: 5).Problem());
        Assert.Contains("文字高度 201", Make(AnnotationTool.Text, text: "字", fontHeight: 201).Problem());
        // 全透明：画上去等于没画，必须在画之前就说
        Assert.Contains("全透明", new Annotation(AnnotationTool.Rectangle,
            new[] { new PixelPoint(0, 0), new PixelPoint(3, 3) }, Annotation.WithAlpha(Annotation.Opaque(0, 0, 255), 0), 4).Problem());
        Assert.Null(Make(AnnotationTool.Text, text: "中").Problem());
    }

    [Fact]
    public void EveryToolHasAChineseName()
    {
        // 枚举名直接进提示等于让用户读代码。这条断言的作用是让"漏了一个臂"当场红。
        var all = Enum.GetValues<AnnotationTool>();
        Assert.True(all.Length >= 8);
        foreach (var tool in all)
        {
            var name = Annotation.ToolName(tool);
            Assert.False(name == tool.ToString(), $"{tool} 没有中文名");
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }

    // ────────── 覆盖范围 ──────────

    /// <summary>
    /// 哪些工具"只看两个角"、哪些要留下整条折线——这条分组必须存在且只有一处能说：
    /// 界面据此决定拖动中途的采样是覆盖还是追加，绘制端据此决定另一端取最后一点。
    /// 两边各写一遍就会长成"预览对、落笔错"（真机反馈：松手后图形只剩针尖大）。
    /// </summary>
    [Fact]
    public void TwoPointToolsAreExactlyTheCornerShapes()
    {
        var corners = Enum.GetValues<AnnotationTool>().Where(Annotation.IsTwoPointTool).OrderBy(tool => tool).ToList();
        Assert.Equal(
            new[] { AnnotationTool.Rectangle, AnnotationTool.Ellipse, AnnotationTool.Line, AnnotationTool.Arrow },
            corners);
        // 新加工具时这条先红：它属于"两个角"还是"一条折线"必须想清楚，不能默认落进某一组
        Assert.Equal(8, Enum.GetValues<AnnotationTool>().Length);
    }

    [Fact]
    public void BoundsGrowOutwardByTheLineWidth()
    {
        var rect = new Annotation(AnnotationTool.Rectangle,
            new[] { new PixelPoint(10, 10), new PixelPoint(20, 30) }, Annotation.Opaque(255, 0, 0), 4);
        Assert.Equal(new IntRect(6, 6, 18, 28), rect.Bounds());     // 四边各向外扩 4（线宽）
    }

    [Fact]
    public void BoundsIgnorePointOrder()
    {
        // 鼠标从右下往左上拖是常态：两点必须按对角理解，而不是"起点一定在左上"
        var a = new Annotation(AnnotationTool.Rectangle,
            new[] { new PixelPoint(10, 10), new PixelPoint(20, 30) }, Annotation.Opaque(255, 0, 0), 4).Bounds();
        var b = new Annotation(AnnotationTool.Rectangle,
            new[] { new PixelPoint(20, 30), new PixelPoint(10, 10) }, Annotation.Opaque(255, 0, 0), 4).Bounds();
        Assert.Equal(a, b);
    }

    [Fact]
    public void MosaicBoundsIncludeWholeBlocks()
    {
        // 马赛克按格子打散，格子边长 12：外接框必须把格子余量算进去，
        // 否则脏矩形会切掉最后半格，用户看见"涂过的区域右下角有一列没糊"
        var mosaic = new Annotation(AnnotationTool.Mosaic,
            new[] { new PixelPoint(0, 0), new PixelPoint(12, 12) }, Annotation.Opaque(0, 0, 0), 24);
        var bounds = mosaic.Bounds();
        Assert.True(bounds.Width > 12 + Annotation.MosaicBlockSize || bounds.Height > 12 + Annotation.MosaicBlockSize);
        Assert.True(bounds.X <= -Annotation.MosaicBlockSize);
    }

    [Fact]
    public void TextBoundsNeverNarrowerThanTheCharacters()
    {
        var one = new Annotation(AnnotationTool.Text, new[] { new PixelPoint(0, 0) },
            Annotation.Opaque(0, 0, 0), 4) { Text = "中", FontHeight = 20 };
        var many = one with { Text = "一二三四五六七八九十" };
        Assert.Equal(new IntRect(0, 0, 17, 40), one.Bounds());
        Assert.True(many.Bounds().Width > one.Bounds().Width * 5, "字数多了框就该跟着变宽，否则末尾的字被切");
        Assert.Null(one.Problem());
    }

    [Fact]
    public void EmptyPointsDoNotCrash()
    {
        var empty = new Annotation(AnnotationTool.Rectangle, Array.Empty<PixelPoint>(), Annotation.Opaque(0, 0, 0), 4);
        Assert.NotNull(empty.Problem());
        Assert.Equal(default, empty.Bounds());
    }
}
