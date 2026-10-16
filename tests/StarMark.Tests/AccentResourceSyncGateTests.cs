#nullable enable
using System;
using System.Linq;
using Windows.UI;
using StarMark.Core.Appearance;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-82：把「主题色＝自定义」交进<b>框架自己那一族颜色资源</b>（P-213 的落地腿）。
/// <para>
/// <b>为什么这一档值得单独装尺</b>：批次 WI-81 只覆盖了"代码按键取画笔"那一半，
/// 而 XAML 里按 <c>{ThemeResource}</c> 直接取的那几处与控件<b>内部模板</b>问的是
/// <c>SystemAccentColor*</c> 这一族<b>颜色</b>资源——那一半的表现就是"改了颜色只有一半跟着变"。
/// </para>
/// <para>
/// <b>真机凭据（档 <c>bin/WI82/ShotF/settings_full.png</c>）</b>：深色主题 + 自定义 #FF12B76A 时，
/// 设置页那颗<b>打开态开关的轨道</b>与<b>滑杆已填段</b>实测 <b>#71D4A6</b>——
/// 与下面 <c>RampIsTheFiveWindowsSteps</c> 钉的 <c>Light2</c> <b>逐字节相同</b>。
/// 也就是说这一族资源确实被框架模板读到并落到了屏幕上，⛔ 写进没人问的资源表里自我表扬。
/// </para>
/// </summary>
public sealed class AccentResourceSyncGateTests
{
    private const string Sync = "src/StarMark.UI/Helpers/AccentResourceSync.cs";
    private const string App = "src/StarMark.UI/App.xaml.cs";
    private const string ViewModel = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string WidgetWindow = "src/StarMark.UI/Views/WidgetWindow.Appearance.cs";
    private const string WidgetManager = "src/StarMark.UI/Services/WidgetManager.Toggles.cs";
    private const string Palette = "src/StarMark.Core/Appearance/AppPalette.cs";

    private static readonly Color Sample = Color.FromArgb(0xFF, 0x12, 0xB7, 0x6A);

    /// <summary>
    /// ①派生梯＝Windows 那五档（朝白 +20/+40、朝黑 −10/−20/−30）。
    /// 每颗都逐字节钉住：那颗 <c>Light2</c> 就是真机量到 #71D4A6 的来路（⛔ 换算法把屏幕上那颗改了）。
    /// </summary>
    [Fact]
    public void RampIsTheFiveWindowsSteps()
    {
        var (l1, l2, d1, d2, d3) = AppPalette.AccentRamp(Sample);
        Assert.Equal(Color.FromArgb(0xFF, 0x41, 0xC5, 0x88), l1);
        Assert.Equal(Color.FromArgb(0xFF, 0x71, 0xD4, 0xA6), l2);   // ← 真机开关轨道实测那颗
        Assert.Equal(Color.FromArgb(0xFF, 0x10, 0xA5, 0x5F), d1);
        Assert.Equal(Color.FromArgb(0xFF, 0x0E, 0x92, 0x55), d2);
        Assert.Equal(Color.FromArgb(0xFF, 0x0D, 0x80, 0x4A), d3);

        var all = new[] { l1, l2, d1, d2, d3 };
        Assert.Equal(5, all.Select(c => (c.R << 16) | (c.G << 8) | c.B).Distinct().Count());
        Assert.All(all, c => Assert.Equal(0xFF, c.A));              // 派生出来的一律实色
        // 亮档严格越混越亮、暗档严格越混越暗（这把尺⛔ 读"差不多"）
        Assert.True(SurfaceReadability.Luminance(l1) < SurfaceReadability.Luminance(l2));
        Assert.True(SurfaceReadability.Luminance(d1) > SurfaceReadability.Luminance(d2));
        Assert.True(SurfaceReadability.Luminance(d2) > SurfaceReadability.Luminance(d3));
        Assert.True(SurfaceReadability.Luminance(l2) > SurfaceReadability.Luminance(Sample));
        Assert.True(SurfaceReadability.Luminance(d3) < SurfaceReadability.Luminance(Sample));
    }

    /// <summary>②混色那颗的两端与越界：t=0 原样、t=1 全取目标、负数夹到端点（⛔ 混出过冲或负值）。</summary>
    [Fact]
    public void MixTowardHasPinnedEnds()
    {
        Assert.Equal(Sample, AppPalette.MixToward(Sample, 255, 255, 255, 0.0));
        Assert.Equal(Color.FromArgb(0xFF, 255, 255, 255), AppPalette.MixToward(Sample, 255, 255, 255, 1.0));
        Assert.Equal(Color.FromArgb(0xFF, 0, 0, 0), AppPalette.MixToward(Sample, 0, 0, 0, 99.0));
        Assert.Equal(Sample, AppPalette.MixToward(Sample, 0, 0, 0, -3.0));   // 负数夹回 0＝原样
        Assert.Equal(0xFF, AppPalette.MixToward(Sample, 0, 0, 0, 0.5).A);   // 结果永远实色
    }

    /// <summary>
    /// ③纯黑/纯白这两颗极端染料⛔ 让派生梯塌成一颗——那是"选了自定义颜色却什么都没变"的形状。
    /// </summary>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    public void RampStaysDistinctAtTheExtremes(byte level)
    {
        var c = Color.FromArgb(0xFF, level, level, level);
        var (l1, l2, d1, d2, d3) = AppPalette.AccentRamp(c);
        var distinct = new[] { c, l1, l2, d1, d2, d3 }
            .Select(x => (x.R << 16) | (x.G << 8) | x.B).Distinct().Count();
        Assert.True(distinct >= 3, $"染料 #{level:X2} 时派生梯只剩 {distinct} 颗");
        if (level == 0xFF) Assert.Equal(c, l2);                      // 已经全白：亮档无处可去（照实相等）
        if (level == 0x00) Assert.Equal(c, d3);                      // 已经全黑：暗档同理
    }

    /// <summary>
    /// ④那一族键名⛔ 两处名单，且<b>摘除走同一份</b>（⛔ 写九颗、摘三颗——留下那几颗就把系统强调色永久钉死）；
    /// 桶名单整颗钉住＝只喂框架真会查的那两桶。只写应用级主题字典，⛔ 动 <c>MergedDictionaries</c> 里框架那份档。
    /// </summary>
    [Fact]
    public void TheKeyRosterLivesInOnePlaceAndRemovalUsesIt()
    {
        var sync = Code(ReadRepoFile(Sync));
        Assert.Equal(1, Count(sync, "private static readonly string[] Keys"));
        // 那颗前缀出现 18 次＝名单里声明 9 颗 ＋ 写字典时逐颗再问 9 次。
        // 两个数<b>必须同涨</b>：只加名单不写值＝那颗键没人喂；只写值不进名单＝摘的时候漏它（＝把系统强调色永久钉死）。
        Assert.Equal(18, Count(sync, "\"SystemAccentColor"));
        Assert.Equal(9, Count(sync, "dict[\"SystemAccentColor"));
        Assert.Equal(1, Count(sync, "foreach (var key in Keys) dict.Remove(key)"));
        Assert.Equal(1, Count(sync, "app.ThemeDictionaries[bucket] = created"));
        Assert.Equal(0, Count(sync, "MergedDictionaries"));
        // 整颗名单钉死（⛔ 只数前两颗的名字——那样多塞第三桶时这行照旧成立＝假证人，批次 WI-82 台架 A5 撞出来的）：
        // 框架只查 "Light"（浅色）与 "Default"（其余）这两桶，写第三桶＝喂给没人读的资源。
        Assert.Equal(1, Count(sync, "Buckets = { \"Light\", \"Default\" };"));
    }

    /// <summary>
    /// ⑤两颗写的人、两颗重解的人，且<b>只有染料真换了那一拍</b>才惊动主题：
    /// 无条件 <c>Retheme</c> 的表现是"拖任何一根滑杆整屏闪一下"。
    /// </summary>
    [Fact]
    public void OnlyTheAccentChangedBeatRethemes()
    {
        Assert.Equal(1, Count(Code(ReadRepoFile(App)), "AccentResourceSync.Apply("));
        Assert.Equal(1, Count(Code(ReadRepoFile(ViewModel)), "AccentResourceSync.Apply("));
        Assert.Contains("var accentChanged = ThemeAccent.Custom != AppPalette.ParseArgb(accentText);",
            Code(ReadRepoFile(ViewModel)), StringComparison.Ordinal);
        Assert.Equal(2, Count(Code(ReadRepoFile(ViewModel)), "if (accentChanged)"));
        Assert.Equal(1, Count(Code(ReadRepoFile(WidgetWindow)), "AccentResourceSync.Retheme(this)"));
        Assert.Equal(1, Count(Code(ReadRepoFile(WidgetManager)), "w.RethemeAccent()"));

        // 启动那颗必须排在<b>首窗创建之前</b>（⛔ 晚一步＝首屏按系统那颗染一遍、再重染一遍）。
        // 锚点取那颗<b>调用形</b>：裸名 "MigrationRunner" 在 DI 注册段里先出现过一次，用它数会读成"排在前面"。
        var app = Code(ReadRepoFile(App));
        Assert.True(app.IndexOf("AccentResourceSync.Apply(", StringComparison.Ordinal)
                  < app.IndexOf("MigrationRunner>().EnsureSchema()", StringComparison.Ordinal),
            "AccentResourceSync.Apply 排到了迁移之后＝可能晚于首窗创建");
    }

    /// <summary>⑥派生算法⛔ 第二处：UI 侧只准问 <c>AppPalette.AccentRamp</c>，⛔ 自己再混一遍。</summary>
    [Fact]
    public void TheRampHasOneOwnerAndUiOnlyAsksIt()
    {
        Assert.Equal(1, Count(ReadRepoFile(Palette), "public static (Color Light1"));
        Assert.Equal(1, ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("AppPalette.cs", StringComparison.Ordinal))
            .Sum(f => Count(Code(f.Text), "AppPalette.AccentRamp(")));
        Assert.Equal(0, ReadRepoUnder("src/StarMark.UI")
            .Sum(f => Count(Code(f.Text), "MixToward(")));
    }
}
