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
    [InlineData(AnnotationTool.PolyLine)]
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
        Assert.Equal(9, Enum.GetValues<AnnotationTool>().Length);
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

    /// <summary>文字的外接框必须<b>等于 GDI 量出来的那一块</b>：这块框现在还要当"点哪里算选中"与
    /// "选择框画在哪"，按字数估宽会偏，用户看到的就是"我点这行字，框跑到别处去了"。</summary>
    [Fact]
    public void TextBoundsIsWhatGdiActuallyMeasures()
    {
        var one = new Annotation(AnnotationTool.Text, new[] { new PixelPoint(0, 0) },
            Annotation.Opaque(0, 0, 0), 4) { Text = "中", FontHeight = 20 };
        var measured = StarMark.Integrations.Capture.GdiTextDrawer.Measure("中", 20);
        Assert.Equal(new IntRect(0, 0, measured.Width, measured.Height), one.Bounds());

        var many = one with { Text = "一二三四五六七八九十" };
        Assert.True(many.Bounds().Width > one.Bounds().Width * 5, "字数多了框就该跟着变宽，否则末尾的字被切");
        Assert.Null(one.Problem());
    }

    // ────────── 变换：移动 / 缩放 / 旋转 ──────────

    [Fact]
    public void NoTransformHandsBackTheSamePointListWithoutCopying()
    {
        var plain = Make(AnnotationTool.Pen);
        Assert.False(plain.HasTransform);
        Assert.Same(plain.Points, plain.TransformedPoints());     // 热路径：每帧都要过这里，不该复制
    }

    [Fact]
    public void RotatingQuarterTurnSweepsTheLineOntoTheOtherAxis()
    {
        // 一条向右的横线，绕第一个点转 90°（顺时针）⇒ 变成向下竖线，起点不动
        var line = new Annotation(AnnotationTool.Line,
            new[] { new PixelPoint(10, 10), new PixelPoint(30, 10) }, Annotation.Opaque(0, 0, 0), 4);
        var turned = line.RotatedBy(90);
        Assert.Equal(new[] { new PixelPoint(10, 10), new PixelPoint(10, 30) }, turned.TransformedPoints());
        Assert.Equal(90d, turned.Rotation);
    }

    [Fact]
    public void ScalingDoublesTheDistanceFromThePivotOnly()
    {
        var line = new Annotation(AnnotationTool.Line,
            new[] { new PixelPoint(10, 10), new PixelPoint(20, 15) }, Annotation.Opaque(0, 0, 0), 4)
        { Pivot = new PixelPoint(10, 10) };
        Assert.Equal(new[] { new PixelPoint(10, 10), new PixelPoint(30, 20) }, line.ScaledBy(2).TransformedPoints());
    }

    [Fact]
    public void MovingCarriesThePivotAlongSoTheRotationStaysPut()
    {
        // 先转 90° 再整体平移：轴点必须跟着走，否则"拖一下位置"会把形状绕回旧轴甩出去
        var turned = new Annotation(AnnotationTool.Line,
            new[] { new PixelPoint(0, 0), new PixelPoint(20, 0) }, Annotation.Opaque(0, 0, 0), 4).RotatedBy(90);
        var moved = turned.MovedBy(5, 7);
        Assert.Equal(new PixelPoint(5, 7), moved.Origin);
        Assert.Equal(new[] { new PixelPoint(5, 7), new PixelPoint(5, 27) }, moved.TransformedPoints());
        Assert.Equal(90d, moved.Rotation);        // 平移不该改角度
    }

    [Theory]
    [InlineData(0.0001)]      // 缩到看不见 ⇒ 再也点不中，必须有下限
    [InlineData(1000d)]       // 放大到撑破画面 ⇒ 必须有上限
    public void ScaleIsClampedAtBothEnds(double factor)
    {
        var line = Make(AnnotationTool.Line);
        var scaled = line.ScaledBy(factor);
        Assert.InRange(scaled.Scale, Annotation.MinScale, Annotation.MaxScale);
        Assert.Null(scaled.Problem());
    }

    [Theory]
    [InlineData(-90d, 270d)]
    [InlineData(360d, 0d)]
    [InlineData(725d, 5d)]
    public void AngleIsNormalisedIntoOneTurn(double added, double expected)
        => Assert.Equal(expected, Make(AnnotationTool.Line).RotatedBy(added).Rotation);

    [Fact]
    public void TextAcceptsRotationLikeEverythingElse()
    {
        var text = Make(AnnotationTool.Text, text: "字");
        var rotated = text.RotatedBy(30);
        Assert.Null(rotated.Problem());                     // 文字旋转已落地（批次 RF-2），不再拒
        Assert.Equal(30, rotated.Rotation);
        // 缩放对文字是有效的（＝改字号）
        Assert.Null(text.ScaledBy(1.5).Problem());
        Assert.Equal(33, text.ScaledBy(1.5).DrawFontHeight);
    }

    /// <summary>
    /// 转出去的字，<b>选择框要跟着转</b>：只报未旋转的宽高，框就会横在原地而字站出去。
    /// <para>断言取三件互相独立的事：中心钉住（转的是自己不是位置）、面积只会变大（外接框没算小）、
    /// 而 90°/270° 必须<b>两轴对调</b>（没真的转就换不了轴——这一条能抓到"角度被吃掉"那类假实现）。</para>
    /// </summary>
    [Theory]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(150)]
    [InlineData(180)]
    [InlineData(270)]
    public void TextBoundsGrowWithRotation(double angle)
    {
        // 单点：真实的一条文字标注就是"一个锚点 + 一段字"（两个点的写法会让外接框把那个不存在的第二点也算进去）
        var text = new Annotation(AnnotationTool.Text, new[] { new PixelPoint(20, 20) },
            Annotation.Opaque(0, 0, 255), 4)
        { Text = "一段中英 mixed 文字", FontHeight = 40 };
        var flat = text.Bounds();
        var turned = text.RotatedBy(angle).Bounds();

        Assert.True(Math.Abs(flat.X + flat.Width / 2.0 - (turned.X + turned.Width / 2.0)) <= 2,
            $"横向中心该钉住（绕中心转，不是把位置转出去）：{flat} → {turned}");
        Assert.True(Math.Abs(flat.Y + flat.Height / 2.0 - (turned.Y + turned.Height / 2.0)) <= 2,
            $"纵向中心该钉住：{flat} → {turned}");
        Assert.True((long)turned.Width * turned.Height >= (long)flat.Width * flat.Height,
            $"旋转后的外接框面积不会变小：{flat} → {turned}");
        Assert.True(turned.Height >= flat.Height, $"短边方向该被长边占进去：{flat} → {turned}");
        if (angle is 90 or 270)
        {
            Assert.True(Math.Abs(turned.Width - flat.Height) <= 2 && Math.Abs(turned.Height - flat.Width) <= 2,
                $"转 {angle}° 必须两轴对调（没真的转就换不了轴）：{flat} → {turned}");
        }
    }

    [Fact]
    public void BoundsFollowTheTransform()
    {
        var line = new Annotation(AnnotationTool.Line,
            new[] { new PixelPoint(0, 0), new PixelPoint(40, 0) }, Annotation.Opaque(0, 0, 0), 4);
        Assert.Equal(48, line.Bounds().Width);            // 40 + 两侧各 4 的线宽外沿
        Assert.Equal(8, line.Bounds().Height);
        var turned = line.RotatedBy(90);                  // 横的变竖的：长边换到竖直方向
        Assert.Equal(8, turned.Bounds().Width);
        Assert.Equal(48, turned.Bounds().Height);
    }

    // ────────── 角点容差与文字缩放轴（批次 RF-1：真机反馈"一拖就变大、位置四窜"）──────────

    /// <summary>
    /// 角点容差<b>必须随形状尺寸收缩</b>：一行字只有 22 像素高，给它 12 像素的角点容差，
    /// 上下两个角带就在中间接上了 —— 于是每一次拖动都被判成缩放（真机反馈的原话是
    /// "可拖动但实际位置四窜，甚至出现文字显著脱离文字框范围内，并且增大"）。
    /// 这条是纯几何，所以在模型里断言；界面只在取用的那一刻经过它。
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(6, 6)]
    [InlineData(44, 22)]          // 一两个汉字那么短的一行
    [InlineData(300, 22)]         // 长句：高仍是短板
    [InlineData(40, 40)]
    [InlineData(400, 300)]        // 大框：该拿到满容差才点得中
    [InlineData(9, 400)]
    public void CornerToleranceNeverSwallowsTheMiddleOfTheShape(int width, int height)
    {
        var slop = Annotation.HandleSlopFor(new IntRect(0, 0, width, height));
        var shortSide = Math.Min(width, height);
        Assert.True(slop >= 0, "容差不该是负的（负的会一个像素都点不中）");
        Assert.True(2 * slop < shortSide,
            $"{shortSide}px 的短边配 {slop}px 角点容差：两端的带子在中间接上了，那一档就没有移动了");
    }

    [Fact]
    public void BigShapesStillGetTheFullTolerance()
        => Assert.Equal(Annotation.HandleSlop, Annotation.HandleSlopFor(new IntRect(0, 0, 400, 300)));

    [Fact]
    public void ATextLineShrinksTheCornerZoneInsteadOfBecomingAllCorner()
        => Assert.Equal(5, Annotation.HandleSlopFor(new IntRect(0, 0, 44, 22)));   // 22/4＝5：中间还剩 12px 归移动

    /// <summary>
    /// 改字号时字要<b>留在原地</b>：轴在左上角时，放大会把整行字向右下方推出去，
    /// 用户看到的就是"位置四窜 + 字跑到框外面"。轴放在字块正中之后，缩放前后中心不动，只有宽高在长。
    /// </summary>
    [Fact]
    public void ScalingTextKeepsItInPlace()
    {
        var text = TextAt("中英 mixed 一行");
        var before = Centre(text.Bounds());
        var after = Centre(text.ScaledBy(1.8).Bounds());
        var grown = text.ScaledBy(1.8).Bounds();

        Assert.True(grown.Width > text.Bounds().Width && grown.Height > text.Bounds().Height,
            $"放大就该整体变大：{text.Bounds()} → {grown}");
        Assert.True(Math.Abs(before.X - after.X) <= 1.5, $"横向中心不该跑：{before} → {after}");
        Assert.True(Math.Abs(before.Y - after.Y) <= 1.5, $"纵向中心不该跑：{before} → {after}");
    }

    private static Annotation TextAt(string text)
        => new(AnnotationTool.Text, new[] { new PixelPoint(30, 40) }, Annotation.Opaque(0, 0, 255), 4)
        { Text = text, FontHeight = 22 };

    private static (double X, double Y) Centre(IntRect box) => (box.X + box.Width / 2.0, box.Y + box.Height / 2.0);

    /// <summary>移动一条改过字号的字：轴跟着一起走，下一次缩放仍以这条字现在的中心为轴（不然会跳回原位）。</summary>
    [Fact]
    public void MovingCarriesThePivotAlongWithTheGlyphs()
    {
        var text = TextAt("字");
        var moved = text.MovedBy(12, -8);
        Assert.Equal(new PixelPoint(42, 32), moved.Points[0]);      // 字本身跟着走
        Assert.Equal(text.Origin.X + 12, moved.Origin.X);           // 轴也跟着走：下一次缩放仍以"现在"的中心为轴
        Assert.Equal(text.Origin.Y - 8, moved.Origin.Y);
        Assert.NotEqual(moved.Points[0], moved.Origin);             // 轴在字块中心，不是左上那一个点
    }

    [Fact]
    public void HitTestForgivesASlopAndPrefersTheTopmostMark()
    {
        var first = new Annotation(AnnotationTool.Line,
            new[] { new PixelPoint(0, 0), new PixelPoint(40, 40) }, Annotation.Opaque(0, 0, 0), 4);
        var second = new Annotation(AnnotationTool.Line,
            new[] { new PixelPoint(0, 40), new PixelPoint(40, 0) }, Annotation.Opaque(0, 0, 0), 4);
        var marks = new[] { first, second };
        var middle = new PixelPoint(20, 20);              // 两条的重叠处
        Assert.Equal(1, StarMark.Core.Capture.AnnotationPainter.HitTest(marks, middle, 0));
        Assert.Equal(0, StarMark.Core.Capture.AnnotationPainter.HitTest(new[] { first }, new PixelPoint(60, 5), 25));
        Assert.Null(StarMark.Core.Capture.AnnotationPainter.HitTest(new[] { first }, new PixelPoint(60, 5), 4));
    }

    [Fact]
    public void EmptyPointsDoNotCrash()
    {
        var empty = new Annotation(AnnotationTool.Rectangle, Array.Empty<PixelPoint>(), Annotation.Opaque(0, 0, 0), 4);
        Assert.NotNull(empty.Problem());
        Assert.Equal(default, empty.Bounds());
    }
    /// <summary>条上"哪几种收进图形选择栏、哪几种各占一颗"由 <see cref="AnnotationTools"/> 说。
    /// <b>两组必须不重不漏盖住整个枚举</b>：漏一个就有一种画法在条上根本没有出口（只有跑起来才发现），
    /// 重一个则同一支笔会在两处各亮一次选中态。</summary>
    [Fact]
    public void ShapesAndBrushesCoverEveryToolExactlyOnce()
    {
        var all = AnnotationTools.Shapes.Concat(AnnotationTools.Brushes).ToList();
        Assert.Equal(Enum.GetValues<AnnotationTool>().Length, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
        // 折线与直线必须同组：两段差别只在"点几下"，分在两组会让人在两个地方各找一半
        Assert.True(AnnotationTools.IsShapeTool(AnnotationTool.PolyLine));
        Assert.True(AnnotationTools.IsShapeTool(AnnotationTool.Line));
        Assert.False(AnnotationTools.IsShapeTool(AnnotationTool.Text));
        Assert.False(AnnotationTools.IsShapeTool(AnnotationTool.Mosaic));
    }

    // ────────── 抓取判定：按这一下究竟改什么（批次 RH-1） ──────────

    /// <summary>把手画在角上：顶上一颗在框外 26 像素（与遮罩窗同一个数），所以扫框内时不会误判成旋转。</summary>
    private static PixelPoint HandleAbove(IntRect box) => new(box.X + box.Width / 2, box.Y - 26);

    /// <summary>
    /// 用户报的那一条：<b>拖动一行字，结果字变大</b>。
    /// <para>机制不是字号算错，而是"这一下算拖什么"判错：一行字只有二十来像素高，
    /// 四角各让出 5 像素的缩放区正好压在字的两端——而人抓一行字要挪，手就放在那一头。
    /// 所以这一条把整个框内的像素<b>逐个数过去</b>：没有一个应判成缩放。
    /// 扫全框而不是抽几点，是因为这正是一个"位置相关"的缺陷，抽样恰好会抽到中间那块干净的。</para>
    /// </summary>
    [Theory]
    [InlineData("字")]                                     // 最短的一个字：框最窄，角区占比最大
    [InlineData("把这句话挪一下位置")]
    [InlineData("A very long line of english text here")]  // 拉丁字母的宽度另算一版
    public void DraggingAnywhereInsideATextLineIsNeverAMagnify(string text)
    {
        var mark = TextAt(text);
        var box = mark.Bounds();
        var handle = HandleAbove(box);

        var wrong = new List<string>();
        for (var y = box.Y; y < box.Bottom; y++)
            for (var x = box.X; x < box.Right; x++)
            {
                var grab = mark.GrabAt(new PixelPoint(x, y), handle, 6);
                if (grab != AnnotationGrab.Move) wrong.Add($"({x},{y})→{grab}");
            }

        Assert.True(box.Width > 0 && box.Height > 0);
        Assert.Empty(wrong.Take(6));                                    // 框内每一格都必须是"移动"
    }

    /// <summary>反面的一半：缩放这条路**没有被砍掉**——把手那半个角（框外）仍然改字号。
    /// 只断言"框内不缩放"是不够的：那会让"永远不缩放"的错误实现照样全绿。</summary>
    [Fact]
    public void ATextLineStillScalesFromTheHalfOfEachHandleOutsideTheBox()
    {
        var mark = TextAt("挪我");
        var box = mark.Bounds();
        var handle = HandleAbove(box);
        var outside = new[]
        {
            new PixelPoint(box.X - 3, box.Y - 3), new PixelPoint(box.Right + 3, box.Y - 3),
            new PixelPoint(box.Right + 3, box.Bottom + 3), new PixelPoint(box.X - 3, box.Bottom + 3),
        };

        foreach (var at in outside)
            Assert.Equal(AnnotationGrab.Scale, mark.GrabAt(at, handle, 6));
    }

    /// <summary>几何类不许被这条规则连带改掉：它们的框远大于角点区，按在内侧那一圈本来就是改大小。</summary>
    [Fact]
    public void ShapesKeepTheirInsideCornerZone()
    {
        var rect = new Annotation(AnnotationTool.Rectangle,
            new[] { new PixelPoint(0, 0), new PixelPoint(300, 160) }, Annotation.Opaque(255, 0, 0), 4);
        var box = rect.Bounds();

        Assert.Equal(AnnotationGrab.Scale, rect.GrabAt(new PixelPoint(box.X + 2, box.Y + 2), HandleAbove(box), 6));
        Assert.Equal(AnnotationGrab.Move, rect.GrabAt(new PixelPoint(box.X + box.Width / 2, box.Y + box.Height / 2), HandleAbove(box), 6));
    }

    /// <summary>
    /// 判序：旋转把手 &gt; 缩放 &gt; 移动。<b>这一条只有搬进模型才断言得出来</b>（界面里那条链引用不到）。
    /// <para>场景不是想象出来的：顶边贴到选区上沿时，遮罩窗会把把手<b>夹进框内</b>（框外那一按会被当成重新框选，
    /// 等于那颗把手永远点不到，而屏幕上它明明画在那儿）。这时把手同时落在"框内"里——判序若写反成先判移动，
    /// 旋转就永远够不着，而全绿的用例里不会有任何一条喊出来。</para>
    /// </summary>
    [Fact]
    public void AHandleClampedInsideTheBoxStillWinsOverMoving()
    {
        var mark = Make(AnnotationTool.Text, text: "贴着选区上沿的一行字");
        var box = mark.Bounds();
        // 把手被夹到框内那一点：它既是"框内"（移动）又是把手（旋转）
        var clamped = new PixelPoint(box.X + box.Width / 2, box.Y + 5);

        Assert.Equal(AnnotationGrab.Rotate, mark.GrabAt(clamped, clamped, 6));
        // 反过来，框里别处不许被夹进来的把手整块吞掉（那等于旋转吞了移动）
        var elsewhere = new PixelPoint(box.X + 2, box.Y + box.Height / 2);
        Assert.True(clamped.X - elsewhere.X > Annotation.HandleSlop);
        Assert.Equal(AnnotationGrab.Move, mark.GrabAt(elsewhere, clamped, 6));
    }

    /// <summary>
    /// 连着拖五次之后字号必须还是那个字号——这条钉的是<b>累积效应</b>：单看一次拖动"稍微变大"
    /// 很像 DPI/取整的噪声，只有连拖几次才看得出那是一个停不下来的棘轮。
    /// </summary>
    [Fact]
    public void RepeatedDragsNeverGrowTheGlyphs()
    {
        var mark = TextAt("连续拖五次看看会不会变大");
        var box = mark.Bounds();
        var handle = HandleAbove(box);

        for (var round = 0; round < 5; round++)
        {
            var press = new PixelPoint(box.X + box.Width / 2, box.Y + box.Height / 2);
            Assert.Equal(AnnotationGrab.Move, mark.GrabAt(press, handle, 6));
            mark = mark.MovedBy(7, 5);
            box = mark.Bounds();
            handle = HandleAbove(box);
        }

        Assert.Equal(1d, mark.Scale);
        Assert.Equal(Annotation.DefaultFontHeight, mark.DrawFontHeight);   // 字高没被拖动改过
        Assert.Equal(TextAt("连续拖五次看看会不会变大").Bounds().Height, box.Height);
    }

    // ────────── 缩放时"钉住哪一头"（批次 RH-2） ──────────

    private static (int X, int Y) CornerOf(IntRect box, bool right, bool bottom)
        => (right ? box.Right : box.X, bottom ? box.Bottom : box.Y);

    /// <summary>
    /// 用户报的第二条："缩放文字后，位置与文字框都偏移了"。
    /// <para>默认那条路是绕<b>字块中心</b>缩放（改字号往四周均匀长），所以放大时左上角会被一起推到左上方——
    /// 但用户是按着某一头的把手在拖，他期望的是<b>另一头一步都不挪</b>。这条把四个把手各拖一遍，
    /// 逐一对量"对面那一角缩放前后是不是同一个像素"。</para>
    /// <para>容差 1 像素只给取整（字模量宽对倍数不完全是线性的），不给方向：方向错了就是几像素到几十像素。</para>
    /// </summary>
    [Theory]
    [InlineData(false, false)]      // 抓左上 ⇒ 钉右下
    [InlineData(true, false)]       // 抓右上 ⇒ 钉左下
    [InlineData(true, true)]        // 抓右下 ⇒ 钉左上（最常见的那一下）
    [InlineData(false, true)]       // 抓左下 ⇒ 钉右上
    public void ScalingTextFromOneEndKeepsTheOtherEndPut(bool grabRight, bool grabBottom)
    {
        var mark = TextAt("拖着这一行的某一头改大小");
        var before = mark.Bounds();
        var anchor = CornerOf(before, !grabRight, !grabBottom);

        var grown = mark
            .WithScalePivotTowards(new PixelPoint(CornerOf(before, grabRight, grabBottom).X, CornerOf(before, grabRight, grabBottom).Y))
            .ScaledBy(1.9);
        var after = grown.Bounds();

        Assert.True(after.Width > before.Width && after.Height > before.Height, "放大就该两边都长");
        var kept = CornerOf(after, !grabRight, !grabBottom);
        Assert.True(Math.Abs(kept.X - anchor.X) <= 1 && Math.Abs(kept.Y - anchor.Y) <= 1,
            $"钉住的那一头跑了：({anchor.X},{anchor.Y}) → ({kept.X},{kept.Y})");
    }

    /// <summary>
    /// 缩完一次再从<b>另一头</b>缩一次——第二次的"钉住"必须是<em>此刻</em>那一头，不是最初那一头。
    /// <para>轴点若按<em>未缩放</em>那份字框算：第一次把左端拉出去了，第二次的"左端"其实已经换了一个像素位置，
    /// 拿旧框的左端去钉，第二次一拖整行字就跳回去（连抓两下是常见的用法，不是边角）。</para>
    /// </summary>
    [Fact]
    public void ScalingFromTheOtherEndAfterwardsPinsTheEndThatIsActuallyThere()
    {
        var mark = TextAt("先拉左边再拉右边");
        var box = mark.Bounds();

        // 第一次：抓左上 ⇒ 钉右下
        mark = mark.WithScalePivotTowards(new PixelPoint(box.X, box.Y)).ScaledBy(1.5);
        var pinnedRight = box.Right;
        var pinnedBottom = box.Bottom;
        var second = mark.Bounds();
        Assert.True(second.Right == pinnedRight && second.Bottom == pinnedBottom,
            $"第一次没钉住右下：({pinnedRight},{pinnedBottom}) → ({second.Right},{second.Bottom})");
        Assert.True(second.X < box.X, "抓左上放大，左端该往外走");

        // 第二次：改抓右下 ⇒ 钉左上，而"左上"现在是<em>第一次之后</em>那一头
        mark = mark.WithScalePivotTowards(new PixelPoint(second.Right, second.Bottom)).ScaledBy(1.5);
        var third = mark.Bounds();
        Assert.True(third.X == second.X && third.Y == second.Y,
            $"第二次钉错了头（回到了最初那一头？）：({second.X},{second.Y}) → ({third.X},{third.Y})");
    }

    /// <summary>
    /// 转过的字，<b>包围盒的角落在字外面的空处</b>：把轴钉在那儿等于什么都没钉（字还是会跑）。
    /// 所以轴要落在<b>局部（未旋转）那一框</b>的角上——也就是"这行字自己的另一头"。
    /// </summary>
    [Fact]
    public void ARotatedLineAnchorsOnItsOwnEndNotOnTheEmptyCornerOfItsBox()
    {
        const string text = "转了九十度的一行字";
        var mark = TextAt(text).RotatedBy(90);
        var box = mark.Bounds();

        var anchored = mark.WithScalePivotTowards(new PixelPoint(box.Right, box.Bottom)).ScaledBy(1.7);
        var pivot = anchored.Pivot ?? throw new InvalidOperationException("轴点没设上");
        var (width, height) = StarMark.Integrations.Capture.GdiTextDrawer.Measure(text, anchored.DrawFontHeight);
        var topLeft = anchored.TransformedPoints()[0];
        var localCorners = new[]
        {
            topLeft,
            new PixelPoint(topLeft.X + width, topLeft.Y),
            new PixelPoint(topLeft.X, topLeft.Y + height),
            new PixelPoint(topLeft.X + width, topLeft.Y + height),
        };

        Assert.Contains(localCorners, corner => corner.X == pivot.X && corner.Y == pivot.Y);
        // 而包围盒那一角是空处：它<em>不该</em>是轴（写成断言是为了"哪天有人改用 Bounds() 的角"当场红）
        Assert.DoesNotContain(localCorners, corner =>
            Math.Abs(corner.X - box.Right) <= 1 && Math.Abs(corner.Y - box.Bottom) <= 1);
    }

    /// <summary>
    /// 拖"位置"拖得再远，字高与倍数都不许动 —— 真机反馈"拖动文字会变大"的正身。
    /// <para>这条缺陷原来在模型里查不到：判定（<see cref="GrabAt"/>）与算式（原来的 <c>Preview</c>）分处两层，
    /// 而算式读的是界面的可变字段 <c>_grab</c>——松手前它已被清零，于是"移动"落进了缩放分支，
    /// 倍数按"按下点到松手点"算，一路顶到上限 6×。日志原件：<c>[AnnoGrab] Move</c> ＋
    /// <c>[AnnoDrag] Move font 22→132 scale 1.000→6.000</c>。算式搬成这里的纯函数后，这一拖能被直接断言。</para>
    /// </summary>
    [Fact]
    public void DraggingToMoveNeverChangesTheGlyphHeightHoweverFar()
    {
        var mark = TextAt("拖着这一行走远一点");
        var before = mark.Bounds();

        var moved = mark.DraggedBy(new PixelPoint(386, 376), new PixelPoint(802, 406), AnnotationGrab.Move);
        var after = moved.Bounds();

        Assert.Equal(1d, moved.Scale);
        Assert.Equal(mark.DrawFontHeight, moved.DrawFontHeight);
        Assert.Equal(before.Width, after.Width);
        Assert.Equal(before.X + 416, after.X);        // 位置该老实跟着手
        Assert.Equal(before.Y + 30, after.Y);
    }

    /// <summary>反面的一半：缩放那一路要真的能变大，而"没判到任何一路"（None）必须什么都不改。
    /// 旧版让 None 与缩放共用 <c>default</c> 分支——兜底哪个都不该像缩放。</summary>
    [Fact]
    public void ScalingStillScalesWhileAnUnclaimedPressChangesNothing()
    {
        var mark = TextAt("按住一头放大");
        var box = mark.Bounds();
        var anchored = mark.WithScalePivotTowards(new PixelPoint(box.X, box.Y));          // 抓左上 ⇒ 钉右下

        var grown = anchored.DraggedBy(
            new PixelPoint(box.X, box.Y), new PixelPoint(box.X - 120, box.Y - 120), AnnotationGrab.Scale);
        Assert.True(grown.DrawFontHeight > mark.DrawFontHeight,
            $"往外拖对角就该放大：{mark.DrawFontHeight} → {grown.DrawFontHeight}");

        var untouched = mark.DraggedBy(new PixelPoint(0, 0), new PixelPoint(900, 900), AnnotationGrab.None);
        Assert.Same(mark, untouched);
    }

    /// <summary>转方向那一路只改角度：不许像上面那样把字号顺手带上去。</summary>
    [Fact]
    public void RotatingDragChangesTheAngleButNeverTheGlyphHeight()
    {
        var mark = TextAt("按住旋转把手转一下");
        var box = mark.Bounds();

        var turned = mark.DraggedBy(
            new PixelPoint(box.Right, box.Y), new PixelPoint(box.X, box.Bottom), AnnotationGrab.Rotate);

        Assert.Equal(1d, turned.Scale);
        Assert.Equal(mark.DrawFontHeight, turned.DrawFontHeight);
        Assert.NotEqual(Annotation.NormalizeAngle(mark.Rotation), turned.Rotation);
    }

    /// <summary>缩放这条路没有被砍窄：一行字在<b>框外</b>那一整圈（拿满容差）都还算按在把手上。
    /// 容差随短边收缩那条是为了不让角点区吃掉整行字，而框外本来就不与"移动"抢地方。</summary>
    [Fact]
    public void ATextLinesScaleZoneReachesAWholeHandleOutsideTheBox()
    {
        var mark = TextAt("挪我");
        var box = mark.Bounds();
        var handle = HandleAbove(box);

        var outside = new[]
        {
            new PixelPoint(box.X - 10, box.Y - 10), new PixelPoint(box.Right + 9, box.Y - 9),
            new PixelPoint(box.Right + 10, box.Bottom + 10), new PixelPoint(box.X - 10, box.Bottom + 10),
        };
        foreach (var at in outside)
            Assert.Equal(AnnotationGrab.Scale, mark.GrabAt(at, handle, 6));
    }
}
