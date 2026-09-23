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
        Assert.Contains("x:Name=\"ToolPanel\"", xaml);
        Assert.Contains("x:Name=\"ColourPanel\"", xaml);
        Assert.Contains("x:Name=\"WeightPanel\"", xaml);
        // 工具与颜色按钮一律由代码按 Core 的表生成：XAML 里出现它们就是"两份事实"的开始
        Assert.DoesNotContain("GroupName=\"Tool\"", xaml);
        Assert.DoesNotContain("GroupName=\"Colour\"", xaml);
        Assert.DoesNotContain("GroupName=\"Weight\"", xaml);
        // 判据要精确到"XAML 里没有任何手写的单选定按钮"：按工具名搜会连注释一起打到（注释里出现"矩形"是正常的）
        Assert.DoesNotContain("<RadioButton", xaml);
    }

    [Fact]
    public void EveryToolStripButtonIsBuiltFromTheModel()
    {
        var cs = ReadOverlay(xaml: false);
        Assert.Contains("Enum.GetValues<AnnotationTool>()", cs);
        Assert.Contains("Annotation.Palette", cs);
        Assert.Contains("Annotation.ThicknessSteps", cs);
        Assert.Contains("Annotation.ThicknessNames", cs);
        Assert.Contains("Annotation.ToolName(tool)", cs);
        Assert.Contains("Annotation.ToolHint(tool)", cs);
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
