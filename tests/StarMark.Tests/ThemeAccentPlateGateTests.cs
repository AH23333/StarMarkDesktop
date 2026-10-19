using System;
using System.IO;
using StarMark.Core.Appearance;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// P-215：那颗主题色预览方块的<b>白衬</b>与那颗 <b>hex 上屏</b>各配一把尺。
/// <para>测试工程⛔ 引用 StarMark.UI ⇒ 界面那一头只有源码文本的形状尺（同 ✅P-238 那条结构性约束）；
/// 那颗串本体（<see cref="AppPalette.ToHexText"/>）住 Core，直接字节级摆。</para>
/// </summary>
public class ThemeAccentPlateGateTests
{
    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string Vm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string Plate = "<Border Background=\"White\" CornerRadius=\"6\" Padding=\"6\"";
    private const string HexLine = "Text=\"{x:Bind ViewModel.ThemeAccentHex, Mode=OneWay}\"";

    private static readonly string RepoRoot = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarMark.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到 StarMark.sln：那颗形状尺的射程⛔ 靠猜目录");
    }

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    [Theory]
    [InlineData(0x4C, 0x4A, 0x48, "#4C4A48")]   // 现场那颗：卡背 #2c2c2c 上肉眼认不出
    [InlineData(0xFF, 0xFF, 0xFF, "#FFFFFF")]   // 纯白染料：连白衬都会糊平，只剩那颗串读得清
    [InlineData(0x00, 0x00, 0x00, "#000000")]
    [InlineData(0x0A, 0xB0, 0xC3, "#0AB0C3")]   // 高位补零⛔ 丢
    public void TheHexReadoutIsSpelledExactly(byte r, byte g, byte b, string want)
        => Assert.Equal(want, AppPalette.ToHexText(Windows.UI.Color.FromArgb(255, r, g, b)));

    /// <summary>白衬那一格在、且只有那一颗；它自己的背景⛔ 跟着主题走（跟着变黑＝回到那颗坏的原样）。</summary>
    [Fact]
    public void TheSwatchSitsOnExactlyOneNeutralPlate()
    {
        var xaml = Read(Xaml);
        Assert.Equal(1, Count(xaml, Plate));
        var plateAt = xaml.IndexOf(Plate, StringComparison.Ordinal);
        var openTagEnd = xaml.IndexOf('>', plateAt);
        Assert.DoesNotContain("ThemeResource", xaml.Substring(plateAt, openTagEnd - plateAt), StringComparison.Ordinal);
        // 方块那颗画笔⛔ 离开白衬自己住：白衬必须是方块的直接外壳（中间再隔一层＝衬可能盖不住）。
        Assert.Contains("<Rectangle Height=\"26\"", xaml.Substring(openTagEnd, 260), StringComparison.Ordinal);
    }

    /// <summary>白衬与那颗串都只随「自定义颜色」那一档露面（随系统那一档摊着取色器＝一颗动了什么都不改变的旋钮）。
    /// <para>那一档的露面判据在这张卡里恰好三颗：取色器、白衬、那颗串（方块自己⛔ 再挂一颗＝第二个主人）。</para></summary>
    [Fact]
    public void ThePlateAndTheReadoutAppearOnlyForTheCustomAccentArm()
    {
        var xaml = Read(Xaml);
        var plateAt = xaml.IndexOf(Plate, StringComparison.Ordinal);
        Assert.True(plateAt >= 0, "白衬那一格不在：P-215 那颗修法拉回去了");
        var hexAt = xaml.IndexOf(HexLine, StringComparison.Ordinal);
        var hexElem = xaml.LastIndexOf("<TextBlock", hexAt, StringComparison.Ordinal);
        Assert.True(hexElem > plateAt, "那颗串那一格⛔ 排在白衬之后＝它念的⛔ 是这一格那颗颜色");
        Assert.Equal(3, Count(xaml, "x:Bind ViewModel.CustomAccentVisible, Mode=OneWay"));   // 取色格＋白衬＋那颗串同进同退
        Assert.Equal(1, Count(xaml.Substring(plateAt, hexElem - plateAt), "x:Bind ViewModel.CustomAccentVisible, Mode=OneWay"));
        var tail = xaml.Substring(hexElem, xaml.IndexOf("</StackPanel>", hexElem, StringComparison.Ordinal) - hexElem);
        Assert.Equal(1, Count(tail, "CustomAccentVisible"));            // 那颗串自己那一颗
        Assert.Equal(0, Count(tail, "<Rectangle"));                     // 方块⛔ 再挂一颗露面判据（白衬已经管着它）
    }

    [Fact]
    public void TheReadoutIsNotASecondPlaceWhereTheColourIsEditable()
    {
        var xaml = Read(Xaml);
        // 读数走 OneWay；编辑入口只许有 ColorSpectrum 那一颗。
        Assert.Equal(1, Count(xaml, "Color=\"{x:Bind ViewModel.ThemeAccentColor, Mode=TwoWay}\""));
        Assert.Contains("Fill=\"{x:Bind ViewModel.ThemeAccentBrush, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.ThemeAccentHex, Mode=TwoWay", xaml, StringComparison.Ordinal);
    }

    /// <summary>那颗串只有<b>一颗算式</b>：保存那一腿早就在用 AppPalette.ToHexText，界面⛔ 自己再拼一遍。</summary>
    [Fact]
    public void TheHexComesFromTheOneCalculatorTheSaveLegAlreadyUses()
    {
        var vm = Read(Vm);
        Assert.Contains("public string ThemeAccentHex => AppPalette.ToHexText(ThemeAccentColor);", vm, StringComparison.Ordinal);
        Assert.Equal(2, Count(FormatScanner.Scan(vm).Code, "AppPalette.ToHexText"));   // 一颗上屏＋一颗进设置档
        // XAML 那一头先抹注释再数（本档自己就带着「AppPalette.ToHexText」那句话说清出处＝名册 425 那一族）。
        var xamlPure = System.Text.RegularExpressions.Regex.Replace(Read(Xaml), "<!--.*?-->", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.Equal(0, Count(xamlPure, "ToHexText") + Count(xamlPure, "X2}") + Count(xamlPure, "StringFormat=#"));
    }

    /// <summary>改一颗颜色＝两颗读数一起重贴（⛔ 只notify一颗：那颗串会冻在上一个颜色上）。</summary>
    [Fact]
    public void ChangingTheColourRepaintsBothReadouts()
    {
        var vm = Read(Vm);
        var at = vm.IndexOf("partial void OnThemeAccentColorChanged", StringComparison.Ordinal);
        Assert.True(at >= 0, "那颗重贴手臂不在：改色之后屏幕上会剩一颗旧值");
        var body = vm.Substring(at, vm.IndexOf("partial void OnAccentIndexChanged", at, StringComparison.Ordinal) - at);
        Assert.Contains("OnPropertyChanged(nameof(ThemeAccentBrush));", body, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(ThemeAccentHex));", body, StringComparison.Ordinal);
    }

    private static int Count(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }
}
