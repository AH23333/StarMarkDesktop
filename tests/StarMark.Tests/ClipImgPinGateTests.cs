#nullable enable
using System;
using System.IO;
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// ClipIMG-P3「贴到桌面」的收官闸。<b>摆放是纯判据、入口是文本事实</b>：
/// 前者错在半屏之外（图推出工作区就没得拖回），后者错在"某一处有、某一处没有"——
/// 2d 那回"复制图片只长在一条路径上"就是后者的真机形状，所以入口的三处同源在这里钉死，
/// 而不是等右键翻四个面去抽查。
/// </summary>
public sealed class ClipImgPinGateTests
{
    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(SearchRepo(), relative));

    private static string SearchRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarMark.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    // ────────── 摆放判据：居中、大图贴角、负原点（副屏） ──────────

    [Fact]
    public void SmallImageCentersOnWorkArea()
    {
        var p = ItemCardPolicy.CenteredPlacement(0, 0, 1920, 1040, 800, 600);
        Assert.Equal(560, p.X);                       // (1920-800)/2
        Assert.Equal(220, p.Y);                       // (1040-600)/2
        Assert.Equal(800, p.Width);
        Assert.Equal(600, p.Height);
    }

    [Fact]
    public void ImageBiggerThanWorkPinsToTopLeft_NotOffScreen()
    {
        // 4K 图贴到 1080p 副屏：负半屏偏移会把左上角推出屏外，那半永远找不回
        var p = ItemCardPolicy.CenteredPlacement(0, 0, 1920, 1040, 3840, 2160);
        Assert.Equal(0, p.X);
        Assert.Equal(0, p.Y);
    }

    [Fact]
    public void SecondaryMonitorLeftOfMainKeepsOriginInsideWork()
    {
        // 副屏在主屏左边：工作区原点为负；居中要在这个负带内，而不是回到 (0,0) 贴到主屏去
        var p = ItemCardPolicy.CenteredPlacement(-1920, 0, 1920, 1040, 800, 600);
        Assert.Equal(-1360, p.X);
        Assert.Equal(220, p.Y);
    }

    [Fact]
    public void DegenerateInputsNeverYieldNegativeSize()
    {
        var p = ItemCardPolicy.CenteredPlacement(0, 0, 100, 100, -50, 0);
        Assert.Equal(0, p.Width);
        Assert.Equal(0, p.Height);
    }

    // ────────── 入口三处同源：判据与出口只长成一族 ──────────

    [Fact]
    public void PinEntrySitsInBothContextMenuPlaces()
    {
        var builder = Read(Path.Combine("src", "StarMark.UI", "Helpers", "ItemContextMenu.cs"));
        Assert.Contains("贴到桌面", builder);
        Assert.Contains("PinImageToDesktop", builder);

        var xaml = Read(Path.Combine("src", "StarMark.UI", "Controls", "ItemCard.xaml"));
        Assert.Contains("Text=\"贴到桌面\"", xaml);
        // handler 名与判据属性同帧出现：绑定错挂到别的属性上，就是"菜单里有、点了判据不同"的分岔起点
        Assert.Contains("Click=\"Menu_PinToDesktop\"", xaml);
        var bound = xaml.IndexOf("贴到桌面", StringComparison.Ordinal);
        Assert.Contains("CanCopyAsImage", xaml[bound..(bound + 400)]);
    }

    [Fact]
    public void PinActionGuardsOnTheSameSingleCriterion()
    {
        var actions = Read(Path.Combine("src", "StarMark.UI", "Helpers", "ItemCardActions.cs"));
        // 复制与贴出对"什么算图片行"的答案必须逐字同判据；出现第二次独立判据（比如再看一次类型）就是分岔
        Assert.Equal(2, actions.Split("if (!vm.CanCopyAsImage) return;").Length - 1);

        // 贴出不登记回声是裁决过的口径（不经剪贴板），但代码里要留这一句解释，防止后来者"补登记"
        Assert.Contains("不经剪贴板", actions.Replace("<b>", "").Replace("</b>", ""));
    }

    [Fact]
    public void CodeBehindHandlerIsSelfContainedLikeItsSiblings()
    {
        var code = Read(Path.Combine("src", "StarMark.UI", "Controls", "ItemCard.xaml.cs"));
        Assert.Contains("Menu_PinToDesktop", code);
        Assert.Contains("ItemCardActions.PinImageToDesktop(ViewModel)", code);
    }
}
