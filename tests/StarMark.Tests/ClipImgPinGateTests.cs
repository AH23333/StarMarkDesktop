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

    private static int Count(string text, string needle) => text.Split(needle, StringSplitOptions.None).Length - 1;

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
        Assert.Contains("ItemCardActions.PinImageToDesktop(vm)", builder);
        // 组件行那颗的标题与可点性都取卡片那一份属性，不在这里再拼一句"为什么点不动"
        Assert.Contains("vm.PinImageMenuText", builder);
        Assert.Contains("pin.IsEnabled = vm.CanPinAsImage;", builder);

        var xaml = Read(Path.Combine("src", "StarMark.UI", "Controls", "ItemCard.xaml"));
        Assert.Contains("Click=\"Menu_PinToDesktop\"", xaml);
        Assert.Contains("Text=\"{x:Bind ViewModel.PinImageMenuText, Mode=OneWay}\"", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.CanPinAsImage, Mode=OneWay}\"", xaml);
        // 出不出现在菜单上仍只认那一颗判据（可见性与可点性是两件事，绑反了就会出现"灰掉一整类行"）
        var bound = xaml.IndexOf("Click=\"Menu_PinToDesktop\"", StringComparison.Ordinal);
        Assert.Contains("ViewModel.CanCopyAsImage", xaml[bound..(bound + 400)]);
    }

    [Fact]
    public void PinActionGuardsOnTheSameSingleCriterion()
    {
        var actions = Read(Path.Combine("src", "StarMark.UI", "Helpers", "ItemCardActions.cs"));
        // 「复制图片」守"这一行是不是图片行"；「贴到桌面」多守一句"像素还在不在"，
        // 但那一颗是卡片上的派生属性（见 PinGreyOutDerivesFromTheImageCriterion），
        // 这里绝不再判第二次"什么算图片行"（那会长出第二种图片行）。
        Assert.Equal(1, Count(actions, "if (!vm.CanCopyAsImage) return;"));   // 只剩 CopyImage
        Assert.Contains("if (!vm.CanPinAsImage) return;", actions);

        // 贴出不登记回声是裁决过的口径（不经剪贴板），但代码里要留这一句解释，防止后来者"补登记"
        Assert.Contains("不经剪贴板", actions.Replace("<b>", "").Replace("</b>", ""));
    }

    /// <summary>
    /// 置灰的判据与那句原因<b>只有一个出处</b>：文案写死在 XAML 与共享构建器两处，就会长出两种
    /// "为什么这颗点不动"（WM 那批"图标排上任何文字都不许写死在 XAML"是同一条）。
    /// </summary>
    [Fact]
    public void PinGreyOutDerivesFromTheImageCriterion()
    {
        var vm = Read(Path.Combine("src", "StarMark.UI", "ViewModels", "ItemCardViewModel.cs"));
        // 派生而不是重判：可见性那颗（CanCopyAsImage）仍是唯一认"图片行"的属性，缺文件只是多扣一票。
        Assert.Contains("public bool CanPinAsImage => CanCopyAsImage && !ClipboardImageMissing;", vm);
        // 缺文件时原因写在标题上（灰项自己在标题上说原因；置灰而不解释＝用户只能猜是程序坏了）。
        Assert.Equal(1, Count(vm, "\"贴到桌面\""));                 // 亮着的那一句只有一份
        // 灰着的那一句：动作名归本类（菜单项的名字只有一个主人），事实那半句归政策层（批次 SI）。
        Assert.Contains("贴到桌面（这张图的{ClipboardPolicy.MissingFileClause}）", vm);

        // 两处宿主都不许自带一份字面量——只许读上面那两个属性。
        Assert.DoesNotContain("\"贴到桌面\"", Read(Path.Combine("src", "StarMark.UI", "Controls", "ItemCard.xaml")));
        Assert.DoesNotContain("\"贴到桌面\"", Read(Path.Combine("src", "StarMark.UI", "Helpers", "ItemContextMenu.cs")));
    }

    [Fact]
    public void CodeBehindHandlerIsSelfContainedLikeItsSiblings()
    {
        var code = Read(Path.Combine("src", "StarMark.UI", "Controls", "ItemCard.xaml.cs"));
        Assert.Contains("Menu_PinToDesktop", code);
        Assert.Contains("ItemCardActions.PinImageToDesktop(ViewModel)", code);
    }
}
