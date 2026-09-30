#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「外观 + 边缘磁吸」那三根滑杆的量程／默认只许有一个主人的形状闸门（批次 SD，P-123 清单 #4）。
/// <para>
/// 收口前同一件事最多有五个主人：仓储读档夹一次、落盘再夹一次；VM 字段初值一遍、读取兜底一遍；
/// XAML 的 <c>Minimum/Maximum/StepFrequency</c> 一遍。判据本身住在 Core，所以它的<b>行为</b>由
/// <see cref="AppearanceSettingsPolicyTests"/> 真测；这里只能钉<b>写法</b>（#184：测试工程不引用 StarMark.UI）。
/// </para>
/// <para>
/// <b>与 SA 那条闸门的一个差别（值得记住）</b>：SA 能把"量程数字"直接钉成禁项（4096 这种数在 UI 工程里没几个无关用法），
/// 这批不能——0.72、8、4、64、40 在界面里是<b>无关的量</b>（<c>Opacity="0.72"</c> 的装饰文字、<c>ColumnSpacing="8"</c>、
/// HSL 饱和度 0.72），钉数字就是天天误伤（#144）。所以这里全部改钉<b>形状</b>：谁在夹取、谁在写滑杆属性、谁的实参槽位里出现了判据名字。
/// </para>
/// <para>
/// <b>所有禁项都读"抹掉注释后的代码"</b>：这批正是要在注释里写"旧写法是 <c>Math.Clamp(px, 0, 40)</c>"的地方，
/// 用原文扫会让一句解释性注释把闸门弄红（而被人当噪声放宽规则＝闸门消失）。机制本身由
/// <see cref="CommentBlindnessIsReal_NotJustAnAssertionAboutIt"/> 钉住。
/// </para>
/// </summary>
public sealed class AppearanceRangeGateTests
{
    private const string JudgeFile = "src/StarMark.Core/Appearance/AppearanceSettingsPolicy.cs";
    private const string StoreFile = "src/StarMark.UI/Helpers/SettingsStore.Appearance.cs";
    private const string ViewModelFile = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string XamlFile = "src/StarMark.UI/Views/SettingsPage.xaml";

    /// <summary>四根滑杆（组件／主窗各一根不透明度 ＋ 磁吸两根）按 <c>Value=</c> 的绑定目标点名。</summary>
    private static readonly string[] BoundValueTargets =
        ["ViewModel.MainWindowOpacity", "ViewModel.WidgetOpacity", "ViewModel.SnapStrength", "ViewModel.SnapSpacing"];

    private static readonly string[] RangeAttributes = ["Minimum", "Maximum", "StepFrequency"];

    // ────────── ① 仓储不再自己写夹取 ──────────

    /// <summary>
    /// 读档与落盘都要走判据的 <c>Normalize*</c>。<b>为什么禁 <c>Math.Clamp</c> 而不是禁某个数字</b>：
    /// 数字在界面里到处是无关的量，而"这里有没有第二份夹取逻辑"才是这批要防的动作形状。
    /// </summary>
    [Fact]
    public void StoreClampsThroughTheJudgeNotItsOwnMath()
    {
        var code = CodeOf(StoreFile);
        Assert.DoesNotContain("Math.Clamp(", code, StringComparison.Ordinal);
        // 反空转：禁项为零必须配正面计数，否则"整段被删光"也算通过（#161）。
        // 10 个调用点＝四组设置各有"读＋写"（不透明度那组主窗与组件各一读一写）。
        Assert.Equal(10, Count(code, "AppearanceSettingsPolicy.Normalize"));
        Assert.Equal(4, Regex.Matches(code, @"AppearanceSettingsPolicy\.\w+Default\b").Count);
    }

    // ────────── ② 界面上那四根滑杆不许自带量程 ──────────

    [Fact]
    public void BoundSlidersTakeTheirRangeFromTheJudge()
    {
        var offenders = SliderElements()
            .Where(s => BoundValueTargets.Any(b => s.Contains(b, StringComparison.Ordinal)))
            .SelectMany(s => RangeAttributes.Select(a => (Slider: s, Attr: a, Value: AttrValue(s, a))))
            .Where(x => x.Value is not null && !x.Value!.StartsWith("{x:Bind", StringComparison.Ordinal))
            .Select(x => $"{x.Attr}=\"{x.Value}\"")
            .ToList();

        Assert.True(offenders.Count == 0,
            "外观／磁吸的滑杆又自己写量程了（该绑 AppearanceSettingsPolicy 转发的属性）：\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// 抽取器的自证：正好抓到那四根，且每根的<b>三个</b>量程属性都是绑定而不是"没写所以没违规"。
    /// <para>这条存在的原因：②是"零命中即通过"的禁项闸门，绑定名写错一个字、或者整段属性被删掉，
    /// 它都会一直绿（#161）。</para>
    /// </summary>
    [Fact]
    public void SliderExtractorSeesTheFourBoundOnes()
    {
        var sliders = SliderElements();
        Assert.True(sliders.Length >= 6, $"只抽到 {sliders.Length} 个 Slider——抽取器失效了");
        Assert.Equal(4, sliders.Count(s => BoundValueTargets.Any(b => s.Contains(b, StringComparison.Ordinal))));
        foreach (var target in BoundValueTargets)
        {
            var slider = sliders.Single(s => s.Contains(target, StringComparison.Ordinal));
            Assert.Equal(3, RangeAttributes.Count(a =>
                AttrValue(slider, a)?.StartsWith("{x:Bind", StringComparison.Ordinal) == true));
        }
        Assert.Null(AttrValue("<Slider Value=\"0\"/>", "Minimum"));   // 没写这个属性就是没写，不许当成违规
    }

    // ────────── ③ VM 的初值与兜底都转发自判据 ──────────

    /// <summary>
    /// 四颗字段初值 + 四次读取兜底，实参槽位里必须是判据的名字。
    /// <para>按"包含"锚而不是行首：这些行前面挂着 <c>[ObservableProperty]</c>，
    /// 行首锚定那条教训（#189）已经红过一次了。锚点自己也须唯一，否则"其中一行合规"会掩盖另一行违规。</para>
    /// </summary>
    [Fact]
    public void ViewModelInitialValuesAndFallbacksComeFromTheJudge()
    {
        var lines = CodeOf(ViewModelFile).Split('\n').Select(l => l.Trim()).ToList();
        var anchors = new[]
        {
            "private double _widgetOpacity = ", "private double _mainWindowOpacity = ",
            "private double _snapSpacing = ", "private double _snapStrength = ",
            "Safe(_settings.LoadWidgetOpacity,", "Safe(_settings.LoadMainWindowOpacity,",
            "Safe(_settings.LoadWidgetSnapSpacing,", "Safe(_settings.LoadWidgetSnapStrength,",
        };
        foreach (var anchor in anchors)
        {
            // 锚点自己也要唯一：出现两行＝其中一行合规就能掩盖另一行违规
            var line = Assert.Single(lines, l => l.Contains(anchor, StringComparison.Ordinal));
            Assert.Contains("AppearanceSettingsPolicy.", line, StringComparison.Ordinal);
        }
    }

    // ────────── ④ 设置侧不许再借用求解器那颗物理像素默认 ──────────

    /// <summary>
    /// <c>WidgetSnapCalculator.Default*</c> 是<b>物理像素</b>，设置页那三根滑杆是<b>逻辑像素</b>；
    /// 批次 SD 之前滑杆的 DIP 默认寄在物理像素那颗上＝在赌缩放恒为 100%。
    /// <para>这条不是"那个常量不许存在"：它今天仍被 <c>WidgetWindow</c> 用作会话前的占位初值与迟滞宽度推导，
    /// 所以下面同时钉"那边还在读它"，免得这条禁项退化成没人管的空闸门（#161）。</para>
    /// </summary>
    [Fact]
    public void SettingsSideNeverBorrowsTheSolversPhysicalDefaults()
    {
        foreach (var file in new[] { StoreFile, "src/StarMark.UI/Helpers/WidgetAppearance.cs" })
            Assert.DoesNotContain("WidgetSnapCalculator.Default", CodeOf(file), StringComparison.Ordinal);

        Assert.Equal(3, Count(CodeOf("src/StarMark.UI/Views/WidgetWindow.xaml.cs"), "WidgetSnapCalculator.Default"));
        Assert.Contains("WidgetSnapCalculator.Default",
            CodeOf("src/StarMark.UI/Views/WidgetWindow.DragResize.cs"), StringComparison.Ordinal);
    }

    // ────────── ⑤ 判据不空壳 ──────────

    [Fact]
    public void TheJudgeHoldsEveryNumberAndNormalizesThem()
    {
        var code = CodeOf(JudgeFile);
        Assert.Equal(15, Regex.Matches(code, @"public const (double|int) \w+ =").Count);
        Assert.Equal(4, Regex.Matches(code, @"public static (double|int) Normalize\w+\(").Count);
        // 不许退成"只是把数字抄一遍、其实谁也没夹"的空壳：每颗 Normalize 都得真的用一次 Math.Clamp
        Assert.Equal(4, Count(code, "Math.Clamp("));
    }

    // ────────── ⑥ 转发的量程，XAML 必须各读一次 ──────────

    /// <summary>
    /// VM 转发出去的 9 颗量程（三组 × 下限/上限/步进），XAML 里必须各有读者；反过来 VM 里也必须有这 9 颗出口。
    /// <b>为什么单独钉</b>：转发出去没人读＝这批只是把数字搬了个家，界面还在用自己写的那份。
    /// </summary>
    [Fact]
    public void EveryForwardedRangePropertyHasAnXamlReader()
    {
        var forwarders = new[]
        {
            "OpacityFloor", "OpacityCeiling", "OpacityStep",
            "SnapStrengthFloor", "SnapStrengthCeiling", "SnapStrengthStep",
            "SnapSpacingFloor", "SnapSpacingCeiling", "SnapSpacingStep",
        };
        var xaml = XamlCode();
        var unread = forwarders
            .Where(p => !xaml.Contains($"ViewModel.{p}, Mode=OneTime", StringComparison.Ordinal))
            .ToList();
        Assert.True(unread.Count == 0, "这些量程转发了却没人绑（界面仍在用自己写的那份）：\n" + string.Join("\n", unread));

        var vm = CodeOf(ViewModelFile);
        Assert.Equal(forwarders.Length, forwarders.Count(p =>
            vm.Contains($"public double {p} =>", StringComparison.Ordinal)
            || vm.Contains($"public int {p} =>", StringComparison.Ordinal)));
    }

    // ────────── ⑦ 本闸门依赖的机制，自己也要被钉住 ──────────

    /// <summary>
    /// 上面每条禁项都靠"注释被抹平成空格"才不误伤，所以这个机制不能只是我说一句。
    /// 同一段文本：<b>注释里</b>出现违规形状 ⇒ 看不见；<b>代码里</b>出现 ⇒ 看得见，且同行注释不会把后半段真违规一起带走（#190）。
    /// </summary>
    [Fact]
    public void CommentBlindnessIsReal_NotJustAnAssertionAboutIt()
    {
        Assert.DoesNotContain("Math.Clamp(", CodeOfText("// 旧写法是 Math.Clamp(px, 0, 40)\nint a = 1;\n"),
            StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(", CodeOfText("int a = Math.Clamp(px, 0, 40); // 以前也这样\n"),
            StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(", CodeOfText("/* 说明 */ var b = Math.Clamp(x, 0, 1); // 尾注\n"),
            StringComparison.Ordinal);
    }

    // ─────────────────────────  共用小工具  ─────────────────────────

    private static string[]? _sliders;

    private static string[] SliderElements()
        => _sliders ??= Regex.Matches(XamlCode(), @"<Slider\b[^>]*?/>", RegexOptions.Singleline)
            .Select(m => m.Value).ToArray();

    /// <summary>源码文件：抹掉注释后的代码（禁项规则一律用它，见类注释）。</summary>
    private static string CodeOf(string relativePath) => CodeOfText(ReadRepoFile(relativePath));

    private static string CodeOfText(string text) => FormatScanner.Scan(text).Code;

    /// <summary>XAML：注释是 <c>&lt;!-- --&gt;</c>，C# 扫描器不认，得自己抹。</summary>
    private static string XamlCode()
        => Regex.Replace(Xaml(), @"<!--.*?-->", " ", RegexOptions.Singleline);

    private static string Xaml()
        => File.ReadAllText(Path.Combine(RepoRoot(), XamlFile.Replace('/', Path.DirectorySeparatorChar)));

    private static string? AttrValue(string element, string attr)
    {
        var m = Regex.Match(element, $@"{attr}\s*=\s*""([^""]*)""");
        return m.Success ? m.Groups[1].Value : null;
    }
}
