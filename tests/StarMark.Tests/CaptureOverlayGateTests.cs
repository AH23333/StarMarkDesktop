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
        Assert.Contains("x:Name=\"ToolPicker\"", xaml);
        Assert.Contains("x:Name=\"ColourPicker\"", xaml);
        Assert.Contains("x:Name=\"WeightPicker\"", xaml);
        // 工具与颜色项一律由代码按 Core 的表生成：XAML 里出现它们就是"两份事实"的开始。
        // 判据精确到"没有任何手写的单选定按钮"——按工具名搜会连注释一起打到（注释里出现"矩形"是正常的）
        Assert.DoesNotContain("<RadioButton", xaml);
        Assert.DoesNotContain("<Flyout", xaml);           // 下拉内容也在代码里按模型建，声明在 XAML 里就得逐项起名再填
    }

    [Fact]
    public void EveryToolStripButtonIsBuiltFromTheModel()
    {
        var cs = ReadOverlay(xaml: false);
        Assert.Contains("Enum.GetValues<AnnotationTool>()", cs);
        Assert.Contains("ToolPicker.Flyout = new Flyout", cs);
        Assert.Contains("ColourPicker.Flyout = new Flyout", cs);
        Assert.Contains("WeightPicker.Flyout = new Flyout", cs);
        Assert.Contains("Annotation.Palette", cs);
        Assert.Contains("Annotation.ThicknessSteps", cs);
        Assert.Contains("Annotation.ThicknessNames", cs);
        Assert.Contains("Annotation.ToolName(tool)", cs);
        Assert.Contains("Annotation.ToolHint(tool)", cs);
    }

    [Fact]
    public void ToolStripIsASingleCompactRow()
    {
        // 用户反馈的原话是"太大，且全是按钮排开"。这条闸门钉的就是"别再长回三行"：
        // 工具条 Border 里只允许一个横向 StackPanel，出现纵向 StackPanel 就是又加了一行。
        var xaml = ReadOverlay(xaml: true);
        var bar = xaml[xaml.IndexOf("x:Name=\"ActionBar\"", StringComparison.Ordinal)..
            xaml.IndexOf("x:Name=\"HintChip\"", StringComparison.Ordinal)];
        Assert.Equal(1, Count(bar, "<StackPanel Orientation=\"Horizontal\""));
        Assert.DoesNotContain("<StackPanel Spacing", bar);
        Assert.Contains("DropDownButton", bar);
        Assert.True(Count(bar, "<Button ") <= 8, "行内直接摆的按钮超过 8 个就又回到全排开了");
    }

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
}
