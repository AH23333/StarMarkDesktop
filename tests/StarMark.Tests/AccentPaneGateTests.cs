using System;
using System.Text.RegularExpressions;
using StarMark.Core.Appearance;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 两个宿主共用取色器。UI 不在测试工程引用内，回流和绘制接线用源码守门；
/// 色值与对比度直接调用 Core，不替代运行时交互验证。
/// </summary>
public class AccentPaneGateTests
{
    private const string Pane = "src/StarMark.UI/Views/ColorPickerPane.cs";
    private const string Editor = "src/StarMark.UI/Views/WidgetAppearanceEditor.cs";
    private const string PageCs = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string Page = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string Vm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";

    /// <summary>
    /// ① 那颗串的拼写四臂逐字钉（#4C4A48＝现场那颗：卡背 #2c2c2c 上肉眼认不出；纯白染料连白衬都会糊平；
    /// 高位补零⛔ 丢）。界面那颗读数已灭，这颗算式仍是存档腿的字面形状——拼错一颗就存进去一颗错的。
    /// </summary>
    [Theory]
    [InlineData(0x4C, 0x4A, 0x48, "#4C4A48")]   // 现场那颗：卡背 #2c2c2c 上肉眼认不出
    [InlineData(0xFF, 0xFF, 0xFF, "#FFFFFF")]   // 纯白染料：连白衬都会糊平，只剩那颗串读得清
    [InlineData(0x00, 0x00, 0x00, "#000000")]
    [InlineData(0x0A, 0xB0, 0xC3, "#0AB0C3")]   // 高位补零⛔ 丢
    public void TheHexReadoutIsSpelledExactly(byte r, byte g, byte b, string want)
        => Assert.Equal(want, AppPalette.ToHexText(Windows.UI.Color.FromArgb(255, r, g, b)));

    /// <summary>
    /// ② 那块白衬与那颗 hex 读数已灭（片144）：点选反馈由面板上那排高亮与方图指示自己给。
    /// VM 那两个读数属性与「两颗读数一起重贴」那条手臂随同一批拆。
    /// </summary>
    [Fact]
    public void TheNeutralPlateAndItsReadoutAreGone()
    {
        var xaml = ReadRepoFile(Page);
        Assert.Equal(0, Count(xaml, "Background=\"White\""));   // 白衬
        Assert.Equal(0, Count(xaml, "ColorSpectrum"));          // 旧润色器
        Assert.Equal(0, Count(xaml, "ThemeAccentBrush"));
        Assert.Equal(0, Count(xaml, "ThemeAccentHex"));
        var vm = Code(ReadRepoFile(Vm));
        Assert.Equal(0, Count(vm, "ThemeAccentBrush"));
        Assert.Equal(0, Count(vm, "ThemeAccentHex"));
        Assert.Equal(0, Count(vm, "OnThemeAccentColorChanged"));   // 那条重贴手臂随读数一起拆
    }

    /// <summary>
    /// ③ 面板是「自定义」那一格唯一的编辑入口：x:Bind 的 TwoWay 旧入口随旧润色器一起没了；
    /// 点定经 <c>Picked</c> 交进 ViewModel，实时生效与落盘走本页既有那条去抖链。
    /// </summary>
    [Fact]
    public void ThePaneIsTheSingleEditingEntryForTheCustomAccent()
    {
        Assert.Equal(0, Count(ReadRepoFile(Page), "ViewModel.ThemeAccentColor, Mode=TwoWay"));
        var pageCs = Code(ReadRepoFile(PageCs));
        Assert.Equal(1, Count(pageCs, "pane.Picked += c =>"));
        Assert.Contains("if (c is { } v) ViewModel.ThemeAccentColor = v;", pageCs, StringComparison.Ordinal);
        Assert.Equal(1, Count(pageCs, "AccentPaneHost.Children.Add(pane.Swatches);"));
        Assert.Equal(1, Count(pageCs, "AccentPaneHost.Children.Add(pane.Gradient);"));
        // VM 那颗颜色被写回（装载／换档）时面板跟着同步——面板不订阅 VM，由宿主这一拍推。
        Assert.Equal(1, Count(pageCs, "_accentPane?.SyncTo(ViewModel.ThemeAccentColor);"));
    }

    /// <summary>
    /// ④ 存档腿仍只有一颗 hex 算式：界面那颗读数已灭 ⇒ <c>AppPalette.ToHexText</c> 在 VM 里只剩存档那一颗
    /// （界面⛔ 自己再拼一遍——同一句串两个出处就是名册 425 那一族）。
    /// </summary>
    [Fact]
    public void TheHexCalculatorHasOneAskInTheViewModel()
    {
        var vm = Code(ReadRepoFile(Vm));
        Assert.Equal(1, Count(vm, "AppPalette.ToHexText"));
        Assert.Contains("AccentIndex == 1 ? AppPalette.ToHexText(ThemeAccentColor) : null", vm, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑤ 双宿主共用同一颗面板：编辑器带「跟随全局」钮（覆盖值 null 那一档得有一颗按钮可点），
    /// 设置页不带——「随系统」由上面那颗下拉管。SV 方图＋色相条都在 <c>Gradient</c> 上，宿主只摆位。
    /// </summary>
    [Fact]
    public void BothHostsMountTheSamePane()
    {
        Assert.Equal(1, Count(Code(ReadRepoFile(Editor)), "new ColorPickerPane(showFollowGlobal: true)"));
        Assert.Equal(1, Count(Code(ReadRepoFile(PageCs)), "new ColorPickerPane(showFollowGlobal: false)"));
        var pane = Code(ReadRepoFile(Pane));
        Assert.Contains("internal sealed class ColorPickerPane", pane, StringComparison.Ordinal);
        Assert.Equal(1, Count(pane, "public void SyncTo(Color? color)"));
        Assert.Equal(2, Count(pane, "Gradient.Children.Add("));    // SV 方图＋色相条
        Assert.Equal(1, Count(pane, "internal static string ToHex(Color c)"));
        // 编辑器写值与面板身份比对共用同一颗 hex 形状（#AARRGGBB、alpha 固定 FF）。
        Assert.Equal(1, Count(Code(ReadRepoFile(Editor)), "ColorPickerPane.ToHex("));
        Assert.Contains("$\"#FF{c.R:X2}{c.G:X2}{c.B:X2}\"", pane, StringComparison.Ordinal);
    }

    // 钉住分支和语句顺序，而不是只数调用：黑／灰、H=360 和量化后的同值都不能重算 HSV。
    [Fact]
    public void SameColorEchoKeepsHsvWhileChangedExternalColorsStillSync()
    {
        var sync = MethodBody(Code(ReadRepoFile(Pane)), "public void SyncTo(Color? color)");
        Assert.Equal(Compact("""
            public void SyncTo(Color? color)
            {
                if (_current == color) return;
                _current = color;
                if (color is { } rgb)
                {
                    var (h, s, v) = RgbToHsv(rgb);
                    _pickH = h; _pickS = s; _pickV = v;
                    UpdateHueFill();
                    UpdateSvIndicator();
                    UpdateHueIndicator();
                }
                RefreshSwatchHighlights();
            }
            """), Compact(sync));
    }

    [Fact]
    public void PickedCachesTheColorBeforeTheHostCanSynchronouslyEchoIt()
    {
        var raise = MethodBody(Code(ReadRepoFile(Pane)), "private void RaisePicked()");
        Assert.Equal(Compact("""
            private void RaisePicked()
            {
                var c = HsvToRgb(_pickH, _pickS, _pickV);
                _current = c;
                RefreshSwatchHighlights();
                Picked?.Invoke(c);
            }
            """), Compact(raise));
    }

    [Fact]
    public void ConstructionPaintsTheSquareEvenWhenTheFirstSyncIsNull()
    {
        var pane = Code(ReadRepoFile(Pane));
        var constructor = MethodBody(pane, "public ColorPickerPane(bool showFollowGlobal)");
        Assert.EndsWith(Compact("""
            Gradient.Children.Add(_hueCanvas);
            UpdateHueFill();
            UpdateSvIndicator();
            UpdateHueIndicator();
            }
            """), Compact(constructor));
        var fill = MethodBody(pane, "private void UpdateHueFill()");
        Assert.Contains("_svHueRect.Fill = new SolidColorBrush(HsvToRgb(_pickH, 1, 1));", fill, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSquareOffersFullBrightnessWithoutAutomaticColorNameTooltips()
    {
        var pane = Code(ReadRepoFile(Pane));
        var sv = MethodBody(pane, "private void SvUpdate(PointerRoutedEventArgs e)");
        Assert.Contains("_pickS = Math.Clamp(p.X / _svCanvas.Width, 0, 1);", sv, StringComparison.Ordinal);
        Assert.Contains("_pickV = Math.Clamp(1 - p.Y / _svCanvas.Height, 0, 1);", sv, StringComparison.Ordinal);
        Assert.EndsWith("UpdateSvIndicator();RaisePicked();}", Compact(sv));
        Assert.DoesNotContain("ColorSpectrum", pane, StringComparison.Ordinal);
        // 仅保留预设按钮的词表名称提示；方图和色相条不猜颜色名。
        var swatch = MethodBody(pane, "private static Button MakeSwatchButton");
        Assert.Contains("LocText.SetToolTipKey(btn, key);", swatch, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolTip", WithoutMethod(pane, "private static Button MakeSwatchButton"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SwatchForegroundUsesTheSameBackgroundAndLeavesFollowGlobalToTheTheme()
    {
        var swatch = MethodBody(Code(ReadRepoFile(Pane)), "private static Button MakeSwatchButton");
        Assert.DoesNotContain("Foreground", Between(swatch, "private static Button", "if (hex is not null"), StringComparison.Ordinal);
        Assert.EndsWith(Compact("""
            if (hex is not null && WidgetAppearance.ParseColorBrush(hex) is { } b)
            {
                btn.Background = b;
                btn.Foreground = new SolidColorBrush(SurfaceReadability.ReadableOn(b.Color));
            }
            return btn;
            }
            """), Compact(swatch));
    }

    [Theory]
    [InlineData("white", "#FFFFFFFF", "#FF000000")]
    [InlineData("black", "#FF000000", "#FFFFFFFF")]
    [InlineData("lightGray", "#FFF3F3F3", "#FF000000")]
    [InlineData("darkGray", "#FF2B2B2B", "#FFFFFFFF")]
    [InlineData("green", "#FF16A34A", "#FF000000")]
    [InlineData("yellow", "#FFEAB308", "#FF000000")]
    public void ActualPresetColorsGetContrastingText(string name, string hex, string textHex)
    {
        var palette = Between(Code(ReadRepoFile(Pane)), "(string NameKey, string Hex)[] Palette", "};");
        Assert.Contains($"(\"anno.color.{name}\", \"{hex}\")", palette, StringComparison.Ordinal);
        var surface = AppPalette.ParseArgb(hex)!.Value;
        var foreground = SurfaceReadability.ReadableOn(surface);
        Assert.Equal(AppPalette.ParseArgb(textHex)!.Value, foreground);
        Assert.True(SurfaceReadability.Contrast(foreground, surface) >= 4.5, $"{name} 预设文字对比度不足");
    }

    private static string Compact(string source) => Regex.Replace(source, @"\s+", "");
}
