#nullable enable
using System;
using System.IO;
using System.Linq;
using StarMark.Core.Capture;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 截图标注工具条的<b>唯一真源</b>闸门（扫源码，不跑界面）。
/// <para>
/// 为什么需要这条：工具条上的按钮"有哪些"这件事，现在只存在于 <see cref="AnnotationTool"/> 一张表里，
/// 界面按它生成。一旦有人图省事在 XAML 里手写一个按钮，就变成两份事实——
/// 之后加个工具，模型里有、条上没有（或反过来），而这种错**只有跑起来才看得见**，正是最难自查的那类。
/// </para>
/// </summary>
public sealed class CaptureOverlayGateTests
{
    private static string ReadOverlay(bool xaml)
    {
        var dir = Path.GetDirectoryName(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "StarMark.UI")))
            dir = Path.GetDirectoryName(dir);
        var root = dir ?? throw new InvalidOperationException("未找到仓库根目录（src/StarMark.UI）");
        var name = xaml ? "CaptureOverlayWindow.xaml" : "CaptureOverlayWindow.xaml.cs";
        return File.ReadAllText(Path.Combine(root, "src", "StarMark.UI", "Views", name));
    }

    [Fact]
    public void ToolStripIsGeneratedNotHandWritten()
    {
        var xaml = ReadOverlay(xaml: true);
        var bar = ActionBar(xaml);
        // 整条由代码按模型生成：XAML 里出现任何按钮/下拉/文字，就变成"有哪些工具"的第二份事实，
        // 而那种分岔只有跑起来才看得见。
        Assert.Contains("x:Name=\"BarRow\"", xaml);
        Assert.DoesNotContain("<Button", bar);
        Assert.DoesNotContain("DropDownButton", bar);
        Assert.DoesNotContain("<RadioButton", bar);
        Assert.DoesNotContain("<Flyout", bar);
        Assert.DoesNotContain("Content=\"", bar);      // 条上不写字：文字只出现在 ToolTip（悬停读得到，也不占地方）
        Assert.DoesNotContain("<TextBlock", bar);
    }

    [Fact]
    public void EveryToolStripButtonIsBuiltFromTheModel()
    {
        var cs = ReadOverlay(xaml: false);
        // 条上的清单来自 Core 的分组表，不再自己数枚举：分组（哪些收进选择栏、哪些各占一颗）
        // 一旦在界面里重推一遍，"新工具落进哪一组"就又变成两处各说一次。
        Assert.Contains("AnnotationTools.Shapes", cs);
        Assert.Contains("AnnotationTools.Brushes", cs);
        Assert.DoesNotContain("Enum.GetValues<AnnotationTool>()", cs);
        Assert.Contains("BarRow.Children.Add", cs);
        Assert.Contains("Annotation.Palette", cs);
        Assert.Contains("Annotation.ThicknessSteps", cs);
        Assert.Contains("Annotation.ThicknessNames", cs);
        Assert.Contains("Annotation.ToolName(tool)", cs);
        Assert.Contains("Annotation.ToolHint(tool)", cs);
    }

    /// <summary>新加工具时它必须有自己看得出来的图标：漏一条臂就会静默落进兜底形状，
    /// 于是条上出现两颗一模一样的点，用户挑的不是想要的那个（图标没有文字兜底，撞车就是真的撞车）。</summary>
    [Fact]
    public void EveryToolHasItsOwnIcon()
    {
        var body = SourceGate.MethodBody(ReadOverlay(xaml: false), "private static UIElement ToolIcon");
        foreach (AnnotationTool tool in Enum.GetValues<AnnotationTool>())
            Assert.Equal(1, SourceGate.Count(body, $"AnnotationTool.{tool} =>"));
        Assert.Equal(Enum.GetValues<AnnotationTool>().Length, SourceGate.Count(body, "AnnotationTool."));
    }

    [Fact]
    public void ToolStripIsASingleCompactRow()
    {
        // 用户反馈的原话是"太大"。这条闸门钉的是"别再长回两行、也别把图标换回带文字的下拉"：
        // 工具条 Border 里只允许一个横向 StackPanel，出现纵向 StackPanel 就是又加了一行。
        var bar = ActionBar(ReadOverlay(xaml: true));
        Assert.Equal(1, Count(bar, "<StackPanel"));
        Assert.Contains("Orientation=\"Horizontal\"", bar);
        Assert.DoesNotContain("Orientation=\"Vertical\"", bar);
        Assert.True(Count(bar, "<Border") == 1, "工具条里再套一个 Border 就是又开了一层");
    }

    /// <summary>工具条那一截 XAML（从 ActionBar 到下一个浮层）。</summary>
    private static string ActionBar(string xaml)
        => xaml[xaml.IndexOf("x:Name=\"ActionBar\"", StringComparison.Ordinal)..
            xaml.IndexOf("x:Name=\"HintChip\"", StringComparison.Ordinal)];

    private static int Count(string haystack, string needle)
    {
        var total = 0;
        for (var at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            total++;
        return total;
    }

    [Fact]
    public void EveryToolStillHasANameAndHint()
    {
        // 与上面那条配对：新增一个枚举值时，名字与说明必须一起补上，否则这里先红
        foreach (var tool in Enum.GetValues<AnnotationTool>())
        {
            Assert.NotEqual(tool.ToString(), Annotation.ToolName(tool));
            Assert.False(string.IsNullOrEmpty(Annotation.ToolHint(tool)));
        }
    }

    /// <summary>拖动中途的采样点归谁管，只能在模型里说一次（Annotation.IsTwoPointTool）。
    /// 界面上若改回"一律追加"，绘制端就又要猜哪一点才是另一端——真机反馈"松手后图形变得非常小"就是这么来的。</summary>
    [Fact]
    public void StrokeSamplesFollowTheModelsTwoPointRule()
    {
        var body = SourceGate.MethodBody(ReadOverlay(xaml: false), "private void ExtendStroke");
        Assert.Contains("Annotation.IsTwoPointTool", body);
        Assert.Contains("points[^1] = local", body);            // 两点工具：覆盖最后一点，而不是再追加一个
    }

    [Fact]
    public void CommittedImageGoesThroughThePainterOnEveryExit()
    {
        // 四个落点必须吃同一份"合成后的像素"。漏掉一个就会出现"预览有标注、存出来没有"，
        // 而这件事只在用户把图发出去之后才发现。
        var cs = ReadOverlay(xaml: false);
        Assert.Contains("AnnotationPainter.Render(", cs);
        foreach (var call in new[]
        {
            "ScreenshotService.CopyPixelsAsync(", "ScreenshotService.SavePixelsAsync(",
            "ScreenshotService.PinPixels(", "OcrService.CopyTextFromPixelsAsync(",
        })
        {
            Assert.Contains(call, cs);
        }
        Assert.DoesNotContain("CopySelectionAsync(_frame", cs);
        Assert.DoesNotContain("SaveSelectionAsync(_frame", cs);
        Assert.DoesNotContain("PinSelection(_frame", cs);
        Assert.DoesNotContain("CopyTextFromSelectionAsync(_frame", cs);
    }

    [Fact]
    public void AnnotationLayerSitsUnderTheSelectionChrome()
    {
        // 标注层必须在压暗层之前：摆到 SelRect 之后，刚烤好的图会盖住选区描边的内半边
        var xaml = ReadOverlay(xaml: true);
        Assert.True(xaml.IndexOf("x:Name=\"AnnotateLayer\"", StringComparison.Ordinal)
            < xaml.IndexOf("x:Name=\"DimLayer\"", StringComparison.Ordinal));
    }
    /// <summary>
    /// 就地输入那一行字，每条离开它的出口都必须先落笔。<b>真机反馈"文字编辑功能无效"就是这类出口漏了</b>：
    /// 在画布上点第二下会把刚打的字清空、没按 Enter 就点动作按钮会把字丢掉、
    /// 焦点没进输入框时 Enter 会变成"复制整张截图"并关掉遮罩——三种表现都指向同一句"我打的字呢"。
    /// </summary>
    [Fact]
    public void TypedTextIsCommittedOnEveryWayOut()
    {
        var cs = ReadOverlay(xaml: false);
        Assert.Contains("EndTextEditing(commit: true)", SourceGate.MethodBody(cs, "private void Commit(CommitAction action)"));
        Assert.Contains("EndTextEditing(commit: true)", SourceGate.MethodBody(cs, "private void BeginStroke"));
        Assert.Contains("EndTextEditing(commit: true)", SourceGate.MethodBody(cs, "private void SetTool"));
        // 点进输入框不算"在选区里起一笔"（否则第二次点击进来就把这行清空了）
        Assert.Contains("TextEditor.PointerPressed += (_, e) => e.Handled = true;", cs);
        // 焦点没落进输入框时必须当场说出来：静默失效是最难自查的一类
        Assert.Contains("if (!TextEditor.Focus(FocusState.Programmatic))", cs);
        // 焦点跑掉时 Enter/Esc 也不能被当成"复制整张 / 取消这一屏"
        Assert.Contains("if (_editingText && e.Key is VirtualKey.Enter or VirtualKey.Escape)",
            SourceGate.MethodBody(cs, "private void Root_KeyDown"));
    }

    /// <summary>折线是"点出来的"，所以收口必须有两个出口（键盘与鼠标各一个），
    /// 而换选区时正在点的那一条要丢掉——留着它会在新框里画出一个对不上位置的圈。</summary>
    [Fact]
    public void PolyLineIsClosedOnBothEndsAndDroppedOnNewSelection()
    {
        var cs = ReadOverlay(xaml: false);
        var keys = SourceGate.MethodBody(cs, "private void Root_KeyDown");
        Assert.Contains("FinishPolyLine(commit: e.Key == VirtualKey.Enter)", keys);   // Enter 收口 / Esc 丢掉
        Assert.Contains("Root.DoubleTapped", cs);                                     // 双击也是收口（只给键盘＝找不到出口）
        Assert.Contains("PlaceVertex(ToLocal(physical))", cs);                        // 每一按钉一个顶点，不走拖动那套
        Assert.Contains("FinishPolyLine(commit: false)", SourceGate.MethodBody(cs, "private void ResetAnnotations"));
    }
    /// <summary>
    /// 就地输入那一块黄底必须<b>左上角对齐</b>。Grid 的子元素默认 Stretch：只给它 Margin 定位，
    /// 它会从点击处一路铺到屏幕右下角（真机反馈的"点击处到右下角的黄色矩形"），
    /// 输入框本身也跟着拉成一条巨大的东西——这一条只能在 XAML 里守，代码里看不出来。
    /// </summary>
    [Fact]
    public void TextEditorIsAnchoredTopLeftInsteadOfStretching()
    {
        var xaml = ReadOverlay(xaml: true);
        var host = SourceGate.Between(xaml, "x:Name=\"TextEditorHost\"", "</Border>");
        Assert.Contains("HorizontalAlignment=\"Left\"", host);
        Assert.Contains("VerticalAlignment=\"Top\"", host);
        // 只给 MinWidth 而不给上限，输入框会一路长到 Margin 之外；MaxWidth 是"别铺满屏"的下限保障
        Assert.Contains("MaxWidth=", host);
    }

    /// <summary>
    /// 打码要<b>边画边看见</b>：它的职责是遮住敏感信息，松手才出现等于让用户在看不见的情况下涂。
    /// 所以预览这一层对打码走真像素（重烤一次），不再画"笔刷大小的圈"——那一臂已经不可达，
    /// 留着它就是一条会让人以为"打码预览还是个圈"的假线索。
    /// </summary>
    [Fact]
    public void MosaicPreviewsInRealPixelsAndKeepsNoStrokeRing()
    {
        var cs = ReadOverlay(xaml: false);
        Assert.Contains("Rebake(MosaicLive(points))", SourceGate.MethodBody(cs, "private void PaintPreview"));
        Assert.DoesNotContain("AnnotationTool.Mosaic", SourceGate.MethodBody(cs, "private void DrawLive"));
        // 按下那一下就要能糊住一格：起点先存两份（MosaicBrush 逐段走，两个重合的点正好是笔尖那一格）。
        // 锚点取"存两份"这一句本身——只搜工具名会被注释或分支条件冒充成绿灯。
        Assert.Contains("new List<PixelPoint> { local, local }", SourceGate.MethodBody(cs, "private void BeginStroke"));
    }
}
